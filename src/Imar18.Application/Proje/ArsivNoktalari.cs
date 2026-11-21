using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Application.Proje;

/// <summary>
/// SAHA-020: arşiv onaylı kadastro (TK_PARSEL) köşe noktası. <see cref="Sira"/> 1. adım listesindeki köşe sırası,
/// <see cref="NoktaNo"/> mühendisin verdiği resmî arşiv nokta numarası (ör. <c>2652001</c>), <see cref="ArsivY"/> / <see cref="ArsivX"/>
/// arşiv onaylı koordinat (Y = Sağa/Doğu, X = Yukarı/Kuzey; Fen Klasörü "Deprem Öncesi" tablosuna aynen basılır),
/// <see cref="KadastroParselId"/> köşenin ilk görüldüğü kadastro parseli. <see cref="KoseY"/> / <see cref="KoseX"/> eşleme anahtarıdır:
/// köşenin projedeki kaynak koordinatı (<c>.DNS</c> ileri dönüşümü varsa Deprem Öncesi orijinal CAD köşesi, yoksa aktif köşe). Arşiv
/// koordinatı elle düzeltilse de köşe bu anahtarla bulunur; geometri değişip anahtar köşe kalmazsa kayıt çıktıda uygulanmaz (uyarı).
/// </summary>
public sealed record ArsivNoktaKaydi(int Sira, string NoktaNo, decimal ArsivY, decimal ArsivX, string KadastroParselId, decimal KoseY, decimal KoseX)
{
    /// <summary>Eşleme anahtarı (jeodezik: X = Kuzey, Y = Doğu).</summary>
    public GeodeticPoint Kose => new(KoseX, KoseY);
}

/// <summary>SAHA-020: 1. adım köşe listesinin satırı (kaynak koordinat sisteminde tekil kadastro köşesi).</summary>
public sealed record KadastroKosesi(int Sira, string KadastroParselId, GeodeticPoint Kose);

/// <summary>Toplu yapıştırılan arşiv satırı (<c>NoktaNo Y X</c>).</summary>
public sealed record ArsivNoktaGirdisi(string NoktaNo, decimal Y, decimal X);

/// <summary>Toplu aktarım sonucu: güncellenmiş kayıtlar, eşlenen ve eşlenemeyen satır sayıları.</summary>
public sealed record ArsivTopluAktarimSonucu(IReadOnlyList<ArsivNoktaKaydi> Kayitlar, int Eslenen, IReadOnlyList<string> Eslenemeyen);

/// <summary>
/// SAHA-020 yardımcıları: kadastro köşe listesi, <c>.NCZ</c> tip 1 noktalarından varsayılan arşiv numarası, toplu (<c>.ncn</c>) aktarım
/// eşlemesi ve RevisionId özeti. Tüm koordinat hesapları <c>decimal</c>'dir.
/// </summary>
public static class ArsivNoktalari
{
    /// <summary>Tip 1 nokta / yapıştırılan satır ile kadastro köşesi çakışma toleransı (m; mühendis talebi ≤ 0.02 m).</summary>
    public const decimal CakismaToleransiM = 0.02m;

    /// <summary>Nokta no en fazla 14 karakter (Netcad nokta adı 15 baytlık Pascal alanı — <c>PARSELASYON HARİTASI.NCZ</c>).</summary>
    public const int EnFazlaNoktaNoUzunlugu = 14;

    /// <summary>
    /// Kadastro köşeleri: <see cref="ProjeBelgesi.IleriDonusum"/> doluysa Deprem Öncesi (<see cref="ProjeBelgesi.DepremOncesiParseller"/>),
    /// değilse aktif parseller; kadastro parselleri kimlik (Ordinal) sırasıyla, köşe sırasıyla, tekil (ortak köşe bir kez, ilk parselde).
    /// </summary>
    public static IReadOnlyList<KadastroKosesi> Koseler(ProjeBelgesi belge)
    {
        ArgumentNullException.ThrowIfNull(belge);
        var kaynak = belge.IleriDonusum is not null && belge.DepremOncesiParseller is { } o ? o : belge.Parseller;
        var gorulen = new HashSet<GeodeticPoint>();
        var sonuc = new List<KadastroKosesi>();
        foreach (var p in kaynak.Where(p => p.Rol == ParselRolu.Kadastro).OrderBy(p => p.ParselId, StringComparer.Ordinal))
        {
            foreach (var v in p.Koseler)
            {
                if (gorulen.Add(v))
                {
                    sonuc.Add(new KadastroKosesi(sonuc.Count + 1, p.ParselId, v));
                }
            }
        }

        return sonuc.AsReadOnly();
    }

    /// <summary>
    /// Varsayılan kayıtlar: projede aynı köşeye ait kayıt varsa o korunur; yoksa köşeyle <see cref="CakismaToleransiM"/> içinde çakışan
    /// (en yakın, henüz kullanılmamış) adlı CAD noktasının adı, o da yoksa köşe sırası numara olur (kullanılmışsa önüne <c>K</c> eklenir;
    /// liste her zaman tekil no taşır). Arşiv koordinatı varsayılan olarak köşenin kendisidir.
    /// </summary>
    public static IReadOnlyList<ArsivNoktaKaydi> Varsayilan(ProjeBelgesi belge, IReadOnlyList<CadPoint> cadNoktalari)
    {
        ArgumentNullException.ThrowIfNull(belge);
        ArgumentNullException.ThrowIfNull(cadNoktalari);
        var koseler = Koseler(belge);
        var korunan = KayitliEslesme(belge.ArsivKadastroNoktalari, koseler);

        // Reviewer O5: yalnız geçerli nokta no biçimindeki adlar (Uygula'da reddedilecek varsayılan üretilmez).
        var adli = cadNoktalari.Where(n => NoktaNoGecerli(n.Name)).ToList();
        var kullanilan = korunan.Values.Select(k => k.NoktaNo).ToHashSet(StringComparer.Ordinal);
        var sonuc = new List<ArsivNoktaKaydi>();
        foreach (var k in koseler)
        {
            if (korunan.TryGetValue(k.Sira, out var kayit))
            {
                // SAHA-021: mühendisin kayıtlı arşiv no'su ve arşiv Y, X'i hiçbir zaman CAD / sıra varsayılanıyla ezilmez; köşe ≤ 0.02 m
                // kaydıysa (CAD yeniden yüklendi) eşleme anahtarı yeni köşeye taşınır.
                // Reviewer O2: mühendisin girmediği (anahtarla ≤ 1 mm aynı) varsayılan arşiv Y, X de yeni köşeye taşınır — elle ters
                // kipte "girilmemiş" ayrımı bozulmaz; girilmiş arşiv Y, X aynen kalır.
                var girilmemis = Uzaklik(new GeodeticPoint(kayit.ArsivX, kayit.ArsivY), kayit.Kose) <= AyniKoordinatToleransiM;
                sonuc.Add(kayit with
                {
                    Sira = k.Sira, KadastroParselId = k.KadastroParselId, KoseY = k.Kose.Y, KoseX = k.Kose.X,
                    ArsivY = girilmemis ? k.Kose.Y : kayit.ArsivY, ArsivX = girilmemis ? k.Kose.X : kayit.ArsivX,
                });
                continue;
            }

            var cad = adli.Select(n => (Nokta: n, Uzaklik: Uzaklik(n.Position, k.Kose)))
                .Where(x => x.Uzaklik <= CakismaToleransiM && !kullanilan.Contains(x.Nokta.Name))
                .OrderBy(x => x.Uzaklik).ThenBy(x => x.Nokta.Name, StringComparer.Ordinal)
                .Select(x => x.Nokta.Name).FirstOrDefault();
            var no = cad ?? k.Sira.ToString(CultureInfo.InvariantCulture);
            while (cad is null && kullanilan.Contains(no))
            {
                no = "K" + no; // sıralı no bir CAD adıyla çakışırsa yinelenmez (komut yinelenen no'yu reddeder)
            }

            kullanilan.Add(no);
            sonuc.Add(new ArsivNoktaKaydi(k.Sira, no, k.Kose.Y, k.Kose.X, k.KadastroParselId, k.Kose.Y, k.Kose.X));
        }

        return sonuc.AsReadOnly();
    }

    /// <summary>
    /// SAHA-021: kayıtlı arşiv noktalarının köşelere eşlemesi (köşe sırası → kayıt). Önce eşleme anahtarı birebir aynı köşe, kalanlar
    /// <see cref="CakismaToleransiM"/> içindeki en yakın köşe (en yakın çiftler önce; her kayıt ve köşe bir kez).
    /// </summary>
    public static IReadOnlyDictionary<int, ArsivNoktaKaydi> KayitliEslesme(IReadOnlyList<ArsivNoktaKaydi> kayitlar, IReadOnlyList<KadastroKosesi> koseler)
    {
        ArgumentNullException.ThrowIfNull(kayitlar);
        ArgumentNullException.ThrowIfNull(koseler);
        var sonuc = new Dictionary<int, ArsivNoktaKaydi>();
        var kullanilan = new HashSet<ArsivNoktaKaydi>();
        var anahtar = new Dictionary<GeodeticPoint, ArsivNoktaKaydi>();
        foreach (var k in kayitlar)
        {
            anahtar.TryAdd(k.Kose, k);
        }

        foreach (var k in koseler)
        {
            if (anahtar.TryGetValue(k.Kose, out var kayit) && kullanilan.Add(kayit))
            {
                sonuc[k.Sira] = kayit;
            }
        }

        var adaylar = koseler.Where(k => !sonuc.ContainsKey(k.Sira))
            .SelectMany(k => kayitlar.Where(r => !kullanilan.Contains(r)).Select(r => (Kose: k, Kayit: r, Uzaklik: Uzaklik(k.Kose, r.Kose))))
            .Where(x => x.Uzaklik <= CakismaToleransiM)
            .OrderBy(x => x.Uzaklik).ThenBy(x => x.Kose.Sira).ThenBy(x => x.Kayit.Sira)
            .ToList();
        foreach (var (k, kayit, _) in adaylar)
        {
            if (!sonuc.ContainsKey(k.Sira) && kullanilan.Add(kayit))
            {
                sonuc[k.Sira] = kayit;
            }
        }

        return sonuc;
    }

    /// <summary>
    /// Toplu aktarım (reviewer Y3: satır sırasından bağımsız): önce tüm satırlar koordinatı <see cref="CakismaToleransiM"/> içinde olan
    /// (en yakın) köşeye eşlenir; kalan satırlar yalnız mühendisin / CAD'in verdiği (köşe sırasından farklı, varsayılan olmayan) aynı
    /// nokta no'lu köşeye eşlenir — uzak koordinatlı bir "5" satırı varsayılan "5" no'lu köşenin arşiv koordinatını ezmez. Eşlenen kaydın
    /// nokta no'su ve arşiv koordinatı satırdan alınır; eşlenemeyen satırlar (yalnız nokta no) raporlanır; kayıt sayısı ve sırası değişmez.
    /// </summary>
    public static ArsivTopluAktarimSonucu TopluAktar(IReadOnlyList<ArsivNoktaKaydi> kayitlar, IReadOnlyList<ArsivNoktaGirdisi> satirlar)
    {
        ArgumentNullException.ThrowIfNull(kayitlar);
        ArgumentNullException.ThrowIfNull(satirlar);
        var liste = kayitlar.ToList();
        var atanan = new HashSet<int>();
        var eslesme = new int?[satirlar.Count];

        // 1. geçiş: koordinat (en yakın çiftler önce — satır sırasından bağımsız).
        var adaylar = satirlar.SelectMany((s, i) => Enumerable.Range(0, liste.Count)
                .Select(j => (Satir: i, Kayit: j, Uzaklik: Uzaklik(new GeodeticPoint(s.X, s.Y), liste[j].Kose))))
            .Where(x => x.Uzaklik <= CakismaToleransiM)
            .OrderBy(x => x.Uzaklik).ThenBy(x => x.Satir).ThenBy(x => x.Kayit)
            .ToList();
        foreach (var (satir, kayit, _) in adaylar)
        {
            if (eslesme[satir] is null && !atanan.Contains(kayit))
            {
                eslesme[satir] = kayit;
                atanan.Add(kayit);
            }
        }

        // 2. geçiş: varsayılan (köşe sırası) olmayan aynı nokta no.
        for (var i = 0; i < satirlar.Count; i++)
        {
            if (eslesme[i] is not null)
            {
                continue;
            }

            var no = satirlar[i].NoktaNo;
            var j = Enumerable.Range(0, liste.Count)
                .Where(k => !atanan.Contains(k) && string.Equals(liste[k].NoktaNo, no, StringComparison.Ordinal)
                    && no != liste[k].Sira.ToString(CultureInfo.InvariantCulture))
                .Select(k => (int?)k).FirstOrDefault();
            if (j is { } bulunan)
            {
                eslesme[i] = bulunan;
                atanan.Add(bulunan);
            }
        }

        var eslenemeyen = new List<string>();
        for (var i = 0; i < satirlar.Count; i++)
        {
            if (eslesme[i] is not { } k)
            {
                eslenemeyen.Add(satirlar[i].NoktaNo);
                continue;
            }

            liste[k] = liste[k] with { NoktaNo = satirlar[i].NoktaNo, ArsivY = satirlar[i].Y, ArsivX = satirlar[i].X };
        }

        return new ArsivTopluAktarimSonucu(liste.AsReadOnly(), atanan.Count, eslenemeyen.AsReadOnly());
    }

    /// <summary>Kayıt listesinin kanonik SHA-256 özeti (boşsa null → RevisionId değişmez).</summary>
    public static string? Ozet(IReadOnlyList<ArsivNoktaKaydi> kayitlar)
    {
        ArgumentNullException.ThrowIfNull(kayitlar);
        if (kayitlar.Count == 0)
        {
            return null;
        }

        static string A(string v) => v.Length.ToString(CultureInfo.InvariantCulture) + ":" + v;
        static string D(decimal v) => A(v.ToString(CultureInfo.InvariantCulture));
        var sb = new StringBuilder("imar18-arsiv-noktalari-v1\n");
        foreach (var k in kayitlar.OrderBy(k => k.Sira))
        {
            sb.Append(A(k.Sira.ToString(CultureInfo.InvariantCulture))).Append(A(k.NoktaNo)).Append(D(k.ArsivY)).Append(D(k.ArsivX))
                .Append(A(k.KadastroParselId)).Append(D(k.KoseY)).Append(D(k.KoseX)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// Arşiv koordinatı ile köşe (eşleme anahtarı) arasında izin verilen en büyük uzaklık (m; reviewer Y4). Deprem kayması ve arşiv /
    /// CAD farkı bunun çok altındadır; aşılırsa Y / X yer değiştirmiş veya rakam hatalı sayılır ve komut reddeder.
    /// </summary>
    public const decimal EnFazlaArsivSapmasiM = 100m;

    /// <summary>Nokta no geçerli mi: boş değil, en fazla 14 karakter; harf, rakam, <c>-</c>, <c>.</c>, <c>_</c>, <c>/</c>.</summary>
    public static bool NoktaNoGecerli(string? no) =>
        no is { Length: > 0 and <= EnFazlaNoktaNoUzunlugu } && no == no.Trim()
        && no.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '/');

    /// <summary>Arşiv koordinatı köşeyle aynı sayılır (girilmemiş varsayılan; ondalık gösterim yuvarlaması payı, m).</summary>
    public const decimal AyniKoordinatToleransiM = 0.001m;

    /// <summary>
    /// İki nokta arası yatay uzaklık (decimal karekök). Reviewer: bileşenlerden biri <see cref="TasmaSiniriM"/>'yi aşarsa kare alınmaz
    /// (decimal taşması / istisna yok) ve <see cref="TasmaSiniriM"/> döner — tüm eşiklerden büyüktür.
    /// </summary>
    public static decimal Uzaklik(GeodeticPoint a, GeodeticPoint b)
    {
        var (dx, dy) = (a.X - b.X, a.Y - b.Y);
        if (Math.Abs(dx) > TasmaSiniriM || Math.Abs(dy) > TasmaSiniriM)
        {
            return TasmaSiniriM;
        }

        return Imar18.Domain.Numerics.DecimalMath.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Uzaklık hesabında taşma koruması sınırı (m).</summary>
    public const decimal TasmaSiniriM = 1_000_000_000m;
}

/// <summary>
/// SAHA-020: arşiv onaylı kadastro noktalarını projeye yazar (boş liste = temizle). Sıra 1..n tekil, nokta no
/// <see cref="ArsivNoktalari.NoktaNoGecerli"/> ve tekil (Ordinal), kadastro parsel kimliği dolu, eşleme anahtarı tekil ve arşiv koordinatı
/// köşeye <see cref="ArsivNoktalari.EnFazlaArsivSapmasiM"/> içinde olmalıdır (Y / X yer değiştirmesi reddedilir).
/// Fen Klasörü'ne basıldığı için RevisionId'ye girer (değişince onay düşer). Denetime yalnız sayı yazılır.
/// </summary>
public sealed record ArsivNoktalariAyarlaKomutu(IReadOnlyList<ArsivNoktaKaydi> Noktalar) : IProjeKomutu
{
    public string Ad => "ArsivNoktalariAyarla";

    public string? Ayrinti => Noktalar is null ? null : "noktaSayisi=" + Noktalar.Count.ToString(CultureInfo.InvariantCulture);

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        ArgumentNullException.ThrowIfNull(mevcut);
        if (Noktalar is null)
        {
            return KomutHatasi.Gecersiz(Ad, "point list is missing");
        }

        var nolar = new HashSet<string>(StringComparer.Ordinal);
        var siralar = new HashSet<int>();
        var koseler = new HashSet<GeodeticPoint>();
        for (var i = 0; i < Noktalar.Count; i++)
        {
            var k = Noktalar[i];
            var sayi = KomutHatasi.Sayi(i + 1);
            if (k is null || string.IsNullOrWhiteSpace(k.KadastroParselId) || k.KadastroParselId.Length > 64)
            {
                return KomutHatasi.Gecersiz(Ad, $"archive point #{sayi} has no cadastral parcel");
            }

            if (!ArsivNoktalari.NoktaNoGecerli(k.NoktaNo))
            {
                return KomutHatasi.Gecersiz(Ad, $"archive point #{sayi} number must be 1-14 letters/digits (- . _ /)");
            }

            if (!nolar.Add(k.NoktaNo))
            {
                return KomutHatasi.Gecersiz(Ad, $"archive point #{sayi} number is duplicated");
            }

            if (ArsivNoktalari.Uzaklik(new GeodeticPoint(k.ArsivX, k.ArsivY), k.Kose) > ArsivNoktalari.EnFazlaArsivSapmasiM)
            {
                return KomutHatasi.Gecersiz(Ad, $"archive point #{sayi} coordinate is more than 100 m from its corner (Y/X swapped?)");
            }

            if (k.Sira < 1 || !siralar.Add(k.Sira) || !koseler.Add(k.Kose))
            {
                return KomutHatasi.Gecersiz(Ad, $"archive point #{sayi} order or corner is duplicated");
            }
        }

        return Result<ProjeBelgesi>.Success(mevcut with { ArsivKadastroNoktalari = Noktalar.OrderBy(k => k.Sira).ToArray() });
    }
}
