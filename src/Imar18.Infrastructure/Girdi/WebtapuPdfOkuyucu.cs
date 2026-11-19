using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Imar18.Application.Girdi;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Numerics;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Imar18.Infrastructure.Girdi;

/// <summary>
/// P7.3-B1 (SAHA-006): webtapu "Tapu Kaydı (Aktif Malikler için Detaylı)" PDF okuyucusu (PdfPig, metin katmanı).
/// <list type="bullet">
/// <item>Döndürülmüş/iri filigran glifleri ("BİLGİ AMAÇLIDIR") elenir; kelimeler taban çizgisine göre satırlara toplanır.</item>
/// <item>Başlık: bilinen etiketler (<c>Ada/Parsel:</c>, <c>AT Yüzölçüm(m2):</c>, <c>İl/İlçe:</c>, <c>Cilt/Sayfa No:</c> …) aynı satırda
/// bir sonraki etikete kadar değeri verir (iki sütunlu düzen).</item>
/// <item>"MÜLKİYET BİLGİLERİ" tablosu: sütun sınırları başlık kelimelerinin konumundan; satır çapası sol sütunda 6+ haneli sistem
/// no ve <c>pay/payda</c> içeren satırdır; çapasız satırlar (alt satırlar ve sonraki sayfanın başına taşan kısım) önceki kayda
/// eklenir. Malik hücresi <c>(SN:n) AD SOYAD : BABA Oğlu|Kızı</c> biçimindedir.</item>
/// <item>Doğrulama: aktif hisse toplamı tam 1 (<see cref="Fraction"/>), her hisse m² = round(AT × hisse, 2) ± 0.01.</item>
/// </list>
/// Hata metinlerine ad, kişi no veya sistem no yazılmaz; yalnız satır sırası (KVKK).
/// </summary>
public sealed partial class WebtapuPdfOkuyucu : ITapuKaydiOkuyucu
{
    /// <summary>Kabul edilen en büyük dosya (webtapu belgesi birkaç yüz KB'dir).</summary>
    public const long MaxBoyut = 32L * 1024 * 1024;

    private const double SatirToleransi = 3d;

    private static readonly string[][] Etiketler =
    [
        ["Zemin", "Tipi:"], ["Taşınmaz", "Kimlik", "No:"], ["İl/İlçe:"], ["Kurum", "Adı:"], ["Mahalle/Köy", "Adı:"], ["Mevkii:"],
        ["Cilt/Sayfa", "No:"], ["Kayıt", "Durum:"], ["Ada/Parsel:"], ["AT", "Yüzölçüm(m2):"], ["Bağımsız", "Bölüm"], ["YüzÖlçümü:"],
        ["Blok/Kat/Giriş/BBNo:"], ["Arsa", "Pay/Payda:"], ["Ana", "Taşınmaz", "Nitelik:"],
    ];

    private sealed record Kelime(int Sayfa, double X0, double X1, double Y, string Metin)
    {
        public double Merkez => (X0 + X1) / 2d;
    }

    private sealed record Satir(int Sayfa, double Y, IReadOnlyList<Kelime> Kelimeler)
    {
        public string Metin => string.Join(' ', Kelimeler.Select(k => k.Metin));
    }

    private sealed record Sinirlar(double El, double Hisse, double Metrekare, double Toplam, double Edinme, double Terkin);

    public Result<TapuKaydi> Oku(Stream kaynak, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kaynak);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] bayt;
        try
        {
            using var ms = new MemoryStream();
            var tampon = new byte[81920];
            int n;
            while ((n = kaynak.Read(tampon, 0, tampon.Length)) > 0)
            {
                if (ms.Length + n > MaxBoyut)
                {
                    return Okunamadi("file too large");
                }

                ms.Write(tampon, 0, n);
            }

            bayt = ms.ToArray();
        }
        catch (IOException)
        {
            return Okunamadi("stream could not be read");
        }

        List<Kelime> kelimeler;
        try
        {
            using var belge = PdfDocument.Open(bayt);
            kelimeler = [];
            foreach (var sayfa in belge.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                kelimeler.AddRange(sayfa.GetWords().Where(Duz).Select(w => new Kelime(
                    sayfa.Number, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom, w.Text.Trim())).Where(k => k.Metin.Length > 0));
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Okunamadi("corrupt or unsupported PDF");
        }

        if (kelimeler.Count == 0)
        {
            return Okunamadi("no text layer (scanned document?)");
        }

        var satirlar = Satirlar(kelimeler);
        var baslik = Baslik(satirlar);
        if (!baslik.TryGetValue("Ada/Parsel:", out var adaParsel) || !baslik.TryGetValue("AT Yüzölçüm(m2):", out var atMetin))
        {
            return Okunamadi("not a webtapu deed record (Ada/Parsel or AT area missing)");
        }

        var ap = adaParsel.Split('/', StringSplitOptions.TrimEntries);
        if (ap.Length != 2 || ap[0].Length == 0 || ap[1].Length == 0 || !Sayi(atMetin, out var at) || at <= 0m)
        {
            return Okunamadi("invalid Ada/Parsel or AT area format");
        }

        var tablo = Tablo(satirlar);
        if (tablo is null)
        {
            return Okunamadi("ownership table header not found");
        }

        var (sinir, veri) = tablo.Value;
        var malikler = new List<TapuMalikSatiri>();
        foreach (var blok in Bloklar(veri))
        {
            var satir = MalikSatiri(blok, sinir);
            if (satir is null)
            {
                return Okunamadi($"ownership row #{malikler.Count + 1} is incomplete");
            }

            malikler.Add(satir);
        }

        var aktif = malikler.Where(m => !m.Terkinli).ToList();
        if (aktif.Count == 0)
        {
            var e = ErrorCatalog.Get(ErrorCodes.TapuHisseEksik);
            return Result<TapuKaydi>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{ownerId}", "deed record", StringComparison.Ordinal) });
        }

        var toplam = Fraction.Sum(aktif.Select(m => new Fraction(m.Pay, m.Payda)));
        if (toplam != Fraction.One)
        {
            var e = ErrorCatalog.Get(ErrorCodes.TapuHisseToplamiBirDegil);
            return Result<TapuKaydi>.Failure(e with
            {
                TechnicalMessage = e.TechnicalMessage.Replace(
                    "{sum}", toplam.Numerator.ToString(CultureInfo.InvariantCulture) + "/" + toplam.Denominator.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal),
            });
        }

        for (var i = 0; i < malikler.Count; i++)
        {
            var m = malikler[i];
            if (m.Terkinli)
            {
                continue;
            }

            var beklenen = (Fraction.FromDecimal(at) * new Fraction(m.Pay, m.Payda)).ToDecimal(4);
            if (Math.Abs(m.MetrekareM2 - decimal.Round(beklenen, 2, MidpointRounding.AwayFromZero)) > 0.01m)
            {
                var e = ErrorCatalog.Get(ErrorCodes.TapuDagitimGirdisiGecersiz);
                return Result<TapuKaydi>.Failure(e with
                {
                    TechnicalMessage = e.TechnicalMessage.Replace("{reason}", $"share area of ownership row #{i + 1} differs from AT area x share by more than 0.01 m2", StringComparison.Ordinal),
                });
            }
        }

        var ilIlce = Deger(baslik, "İl/İlçe:")?.Split('/', 2, StringSplitOptions.TrimEntries);
        var ciltSayfa = Deger(baslik, "Cilt/Sayfa No:")?.Split('/', 2, StringSplitOptions.TrimEntries);
        return Result<TapuKaydi>.Success(new TapuKaydi(
            Deger(baslik, "Taşınmaz Kimlik No:"),
            ilIlce?[0],
            ilIlce is { Length: 2 } ? ilIlce[1] : null,
            Deger(baslik, "Mahalle/Köy Adı:"),
            Deger(baslik, "Mevkii:"),
            ciltSayfa?[0],
            ciltSayfa is { Length: 2 } ? ciltSayfa[1] : null,
            ap[0],
            ap[1],
            at,
            Deger(baslik, "Ana Taşınmaz Nitelik:"),
            malikler.AsReadOnly()));
    }

    /// <summary>Filigran: döndürülmüş veya çok iri (≥ 40 pt) glifler okunmaz.</summary>
    private static bool Duz(Word w)
    {
        var h = w.Letters.FirstOrDefault();
        return h is null || (Math.Abs(h.GlyphRectangle.Rotation) < 1d && h.PointSize < 40d);
    }

    private static List<Satir> Satirlar(List<Kelime> kelimeler)
    {
        var sonuc = new List<Satir>();
        foreach (var sayfa in kelimeler.GroupBy(k => k.Sayfa).OrderBy(g => g.Key))
        {
            var grup = new List<Kelime>();
            double? y = null;
            foreach (var k in sayfa.OrderByDescending(k => k.Y).ThenBy(k => k.X0))
            {
                if (y is { } yy && Math.Abs(yy - k.Y) > SatirToleransi)
                {
                    sonuc.Add(new Satir(sayfa.Key, yy, grup.OrderBy(w => w.X0).ToList()));
                    grup = [];
                }

                y = grup.Count == 0 ? k.Y : y;
                grup.Add(k);
            }

            if (grup.Count > 0)
            {
                sonuc.Add(new Satir(sayfa.Key, y!.Value, grup.OrderBy(w => w.X0).ToList()));
            }
        }

        return sonuc;
    }

    /// <summary>Başlık etiketleri: etiket kelimeleri ardışıksa değer, aynı satırda bir sonraki etiketin başına kadardır.</summary>
    private static Dictionary<string, string> Baslik(List<Satir> satirlar)
    {
        var sonuc = new Dictionary<string, string>(StringComparer.Ordinal);
        var bitis = satirlar.FindIndex(s => s.Kelimeler[0].Metin.StartsWith("MÜLKİYET", StringComparison.Ordinal));
        var ilgili = bitis < 0 ? satirlar : satirlar.Take(bitis).ToList();
        for (var si = 0; si < ilgili.Count; si++)
        {
            var k = ilgili[si].Kelimeler;
            var baslangiclar = new List<(int Indeks, string[] Etiket)>();
            for (var i = 0; i < k.Count; i++)
            {
                var e = Etiketler.FirstOrDefault(et => EtiketBasliyor(k, i, et));
                if (e is not null)
                {
                    baslangiclar.Add((i, e));
                    i += e.Length - 1;
                }
            }

            for (var j = 0; j < baslangiclar.Count; j++)
            {
                var (indeks, etiket) = baslangiclar[j];
                var bas = indeks + etiket.Length;
                var son = j + 1 < baslangiclar.Count ? baslangiclar[j + 1].Indeks : k.Count;
                var deger = string.Join(' ', k.Skip(bas).Take(son - bas).Select(w => w.Metin));
                var anahtar = string.Join(' ', etiket);
                if (deger.Length == 0)
                {
                    continue;
                }

                // "Ana Taşınmaz Nitelik" çok satırlı olabilir: etiketsiz, değerin sütununda başlayan sonraki satırlar eklenir.
                if (anahtar == "Ana Taşınmaz Nitelik:")
                {
                    var x = k[bas].X0;
                    for (var d = si + 1; d < ilgili.Count && ilgili[d].Kelimeler[0].X0 >= x - 5d
                        && !ilgili[d].Kelimeler.Any(w => w.Metin.EndsWith(':')); d++)
                    {
                        deger += " " + ilgili[d].Metin;
                    }
                }

                sonuc.TryAdd(anahtar, deger);
            }
        }

        return sonuc;
    }

    private static bool EtiketBasliyor(IReadOnlyList<Kelime> k, int i, string[] etiket)
    {
        if (i + etiket.Length > k.Count)
        {
            return false;
        }

        for (var j = 0; j < etiket.Length; j++)
        {
            if (!string.Equals(k[i + j].Metin, etiket[j], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Deger(Dictionary<string, string> baslik, string anahtar) =>
        baslik.TryGetValue(anahtar, out var d) && d.Length > 0 ? d : null;

    /// <summary>Tablo başlığından sütun sınırları ve başlıktan sonraki veri satırları (doğrulama notunda biter).</summary>
    private static (Sinirlar, List<Satir>)? Tablo(List<Satir> satirlar)
    {
        var bi = satirlar.FindIndex(s => s.Kelimeler.Any(w => w.Metin == "Malik") && s.Kelimeler.Any(w => w.Metin == "El")
                                         && s.Kelimeler.Any(w => w.Metin == "Hisse") && s.Kelimeler.Any(w => w.Metin == "Terkin"));
        if (bi < 0)
        {
            return null;
        }

        var b = satirlar[bi].Kelimeler;
        Kelime? Bul(string metin, Kelime? sonra = null) => b.FirstOrDefault(w => w.Metin == metin && (sonra is null || w.X0 > sonra.X0));
        var el = Bul("El");
        var birligi = el is null ? null : Bul("Birliği", el);
        var hisse = el is null ? null : Bul("Hisse", el);
        var pay = hisse is null ? null : b.FirstOrDefault(w => w.X0 > hisse.X0 && w.Metin.StartsWith("Pay", StringComparison.Ordinal));
        var metrekare = Bul("Metrekare");
        var toplam = Bul("Toplam");
        var edinme = Bul("Edinme");
        var terkin = Bul("Terkin");
        if (el is null || birligi is null || hisse is null || pay is null || metrekare is null || toplam is null || edinme is null || terkin is null)
        {
            return null;
        }

        var sinir = new Sinirlar(
            El: el.X0,
            Hisse: (birligi.X1 + hisse.X0) / 2d,
            Metrekare: (pay.X1 + metrekare.X0) / 2d,
            Toplam: (metrekare.X1 + toplam.X0) / 2d,
            Edinme: (toplam.X1 + edinme.X0) / 2d,
            Terkin: terkin.X0 - 8d);

        var veri = new List<Satir>();
        foreach (var s in satirlar.Skip(bi + 1))
        {
            var metin = s.Metin;
            if (metin.StartsWith("Bu belgeyi", StringComparison.OrdinalIgnoreCase) || metin.Contains("webtapu.tkgm", StringComparison.OrdinalIgnoreCase)
                || metin.StartsWith("veya Web Tapu", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (SayfaNoDeseni().IsMatch(metin) || metin.StartsWith("BU BELGE TOPLAM", StringComparison.Ordinal) || metin.StartsWith("Tarih:", StringComparison.Ordinal))
            {
                continue;
            }

            veri.Add(s);
        }

        return (sinir, veri);
    }

    /// <summary>Çapa satırı (sistem no + pay/payda) ve ona eklenen çapasız satırlar; ilk çapadan önceki satırlar (başlık devamı) atlanır.</summary>
    private static IEnumerable<List<Satir>> Bloklar(List<Satir> veri)
    {
        List<Satir>? blok = null;
        foreach (var s in veri)
        {
            if (Capa(s))
            {
                if (blok is not null)
                {
                    yield return blok;
                }

                blok = [s];
            }
            else
            {
                blok?.Add(s);
            }
        }

        if (blok is not null)
        {
            yield return blok;
        }
    }

    private static bool Capa(Satir s) =>
        s.Kelimeler[0].Metin.Length >= 6 && s.Kelimeler[0].Metin.All(char.IsAsciiDigit) && s.Kelimeler.Any(w => HisseDeseni().IsMatch(w.Metin));

    private static TapuMalikSatiri? MalikSatiri(List<Satir> blok, Sinirlar sinir)
    {
        var sistem = blok[0].Kelimeler[0];
        var malik = new List<string>();
        var el = new List<string>();
        var hisse = new List<string>();
        var m2 = new List<string>();
        var toplam = new List<string>();
        var edinme = new List<string>();
        var terkin = new List<string>();
        foreach (var w in blok.SelectMany(s => s.Kelimeler))
        {
            if (ReferenceEquals(w, sistem))
            {
                continue;
            }

            var c = w.Merkez;
            (c < sinir.El ? malik
                : c < sinir.Hisse ? el
                : c < sinir.Metrekare ? hisse
                : c < sinir.Toplam ? m2
                : c < sinir.Edinme ? toplam
                : c < sinir.Terkin ? edinme
                : terkin).Add(w.Metin);
        }

        var hm = hisse.Select(h => HisseDeseni().Match(h)).FirstOrDefault(m => m.Success);
        if (hm is null || !Sayi(string.Concat(m2), out var metrekare))
        {
            return null;
        }

        var p = BigInteger.Parse(hm.Groups["p"].Value, CultureInfo.InvariantCulture);
        var q = BigInteger.Parse(hm.Groups["q"].Value, CultureInfo.InvariantCulture);
        if (p <= 0 || q <= 0)
        {
            return null;
        }

        _ = Sayi(string.Concat(toplam), out var toplamM2);
        var (kisiNo, ad, baba) = MalikHucresi(string.Join(' ', malik));
        if (ad.Length == 0)
        {
            return null;
        }

        var elBirligi = string.Concat(el).Trim();
        var terkinMetni = string.Join(' ', terkin).Trim();
        return new TapuMalikSatiri(
            sistem.Metin, kisiNo, ad, baba, elBirligi.Length == 0 ? null : elBirligi, p, q, metrekare, toplamM2,
            string.Join(' ', edinme), terkinMetni.Length > 0 && terkinMetni != "-");
    }

    /// <summary>"(SN:n) AD SOYAD : BABA Oğlu|Kızı" → (kişi no, ad soyad, baba adı).</summary>
    private static (string? KisiNo, string Ad, string? Baba) MalikHucresi(string metin)
    {
        string? kisi = null;
        var sn = KisiNoDeseni().Match(metin);
        if (sn.Success)
        {
            kisi = sn.Groups["n"].Value;
            metin = metin.Remove(sn.Index, sn.Length);
        }

        var iki = metin.IndexOf(':', StringComparison.Ordinal);
        var ad = (iki < 0 ? metin : metin[..iki]).Trim();
        string? baba = iki < 0 ? null : metin[(iki + 1)..].Trim();
        if (baba is not null)
        {
            baba = YakinlikDeseni().Replace(baba, string.Empty).Trim();
            baba = baba.Length == 0 ? null : baba;
        }

        return (kisi, WebtapuAktarici.Normalize(ad), baba is null ? null : WebtapuAktarici.Normalize(baba));
    }

    private static bool Sayi(string metin, out decimal deger) =>
        decimal.TryParse(metin.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out deger);

    private static Result<TapuKaydi> Okunamadi(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.GirdPdfBozuk);
        return Result<TapuKaydi>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage.Replace("{path}", "webtapu PDF (" + neden + ")", StringComparison.Ordinal),
        });
    }

    [GeneratedRegex(@"^(?<p>\d{1,12})/(?<q>\d{1,12})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HisseDeseni();

    [GeneratedRegex(@"\(SN:(?<n>\d{1,15})\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KisiNoDeseni();

    [GeneratedRegex(@"\s+(Oğlu|Kızı|OĞLU|KIZI)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex YakinlikDeseni();

    [GeneratedRegex(@"^\d+\s*/\s*\d+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SayfaNoDeseni();
}
