using System.Globalization;
using Imar18.Application.Cikti;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Application.KoordinatDonusumu;

/// <summary>
/// P7.3-C2 dönüşüm özeti: parametreler, ortak nokta artıkları ve proje noktalarının (deprem sonrası) ters dönüşümle
/// hesaplanan deprem öncesi koordinatları (2 ondalık, AwayFromZero; aynı nokta numaralarıyla).
/// SAHA-014: <see cref="IleriDonusum"/> ise parametreler <c>.DNS</c> katsayılarıdır (birebir) ve <see cref="DepremOncesi"/> aktif
/// (Deprem Sonrası) noktaların aynı nokta numaralı orijinal CAD koordinatlarıdır (ters hesap yapılmaz).
/// </summary>
public sealed record DonusumOzeti(DonusumGirdisi Girdi, DonusumParametreleri Parametreler, IReadOnlyList<KoordinatSatiri> DepremOncesi)
{
    /// <summary>SAHA-014: <c>.DNS</c> ile Deprem Öncesi → Deprem Sonrası ileri dönüşüm uygulanmış.</summary>
    public bool IleriDonusum { get; init; }

    /// <summary>
    /// SAHA-014 ileri dönüşüm özeti: aktif noktalar (<paramref name="depremSonrasi"/>) için Deprem Öncesi karşılık, orijinal parsel
    /// köşelerinin ileri dönüşümünden kurulan sözlükle bulunur (komut aynı aktif noktaya iki farklı orijinal düşmesini reddeder).
    /// Bulunamayan nokta veya çelişkili eşleme <c>ERR-CRS-004</c> (Fen Klasörü .zip engellenir; sessiz yanlış koordinat basılmaz).
    /// Ortak nokta artıkları <paramref name="girdi"/> sırasıyla (komutun kanonik NoktaNo ordinal sırası; <c>.DNS</c> dosya sırası
    /// değil — reviewer L4) basılır.
    /// </summary>
    public static Result<DonusumOzeti> Ileri(
        DonusumGirdisi girdi,
        DonusumKatsayilari katsayilar,
        IReadOnlyList<ParselGeometrisi> depremOncesiParseller,
        IReadOnlyList<KoordinatSatiri> depremSonrasi)
    {
        ArgumentNullException.ThrowIfNull(girdi);
        ArgumentNullException.ThrowIfNull(katsayilar);
        ArgumentNullException.ThrowIfNull(depremOncesiParseller);
        ArgumentNullException.ThrowIfNull(depremSonrasi);
        var eslem = new Dictionary<GeodeticPoint, GeodeticPoint>();
        foreach (var o in depremOncesiParseller.SelectMany(p => p.Koseler))
        {
            var a = katsayilar.Uygula(o);
            if (eslem.TryGetValue(a, out var onceki) && onceki != o)
            {
                return Hata("two distinct original vertices map to the same transformed point");
            }

            eslem[a] = o;
        }

        var oncesi = new List<KoordinatSatiri>(depremSonrasi.Count);
        foreach (var k in depremSonrasi)
        {
            if (!eslem.TryGetValue(new GeodeticPoint(X: k.X, Y: k.Y), out var o))
            {
                return Hata("an active point has no original (earthquake-before) vertex");
            }

            oncesi.Add(new KoordinatSatiri(k.NoktaNo, o.Y, o.X));
        }

        return Result<DonusumOzeti>.Success(new DonusumOzeti(girdi, DonusumParametreleri.Katsayilardan(katsayilar, girdi.OrtakNoktalar), oncesi.AsReadOnly())
        {
            IleriDonusum = true,
        });

        static Result<DonusumOzeti> Hata(string neden)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CrsDonusumHesaplanamadi);
            return Result<DonusumOzeti>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", neden, StringComparison.Ordinal) });
        }
    }
}

/// <summary>
/// P7.3-C2: ortak noktalı dönüşüm servisi. Hesap <see cref="IDonusumHesaplayici"/> portundadır (GeometryEngine); bu servis
/// I → II parametrelerini alır ve proje noktalarına (II. sistem, deprem sonrası) tam ondalık ters dönüşüm uygular.
/// </summary>
public sealed class DonusumServisi
{
    private readonly IDonusumHesaplayici _hesaplayici;

    public DonusumServisi(IDonusumHesaplayici hesaplayici)
    {
        ArgumentNullException.ThrowIfNull(hesaplayici);
        _hesaplayici = hesaplayici;
    }

    /// <summary>Yalnız parametreler (önizleme).</summary>
    public Result<DonusumParametreleri> ParametreleriHesapla(DonusumGirdisi girdi)
    {
        ArgumentNullException.ThrowIfNull(girdi);
        return _hesaplayici.Hesapla(girdi.Model, girdi.OrtakNoktalar);
    }

    /// <summary>Parametreler + proje noktalarının deprem öncesi koordinatları.</summary>
    public Result<DonusumOzeti> Hesapla(DonusumGirdisi girdi, IReadOnlyList<KoordinatSatiri> depremSonrasi)
    {
        ArgumentNullException.ThrowIfNull(girdi);
        ArgumentNullException.ThrowIfNull(depremSonrasi);
        var p = ParametreleriHesapla(girdi);
        if (p.IsFailure)
        {
            return Result<DonusumOzeti>.Failure(p.Error);
        }

        var oncesi = new List<KoordinatSatiri>(depremSonrasi.Count);
        foreach (var k in depremSonrasi)
        {
            var t = p.Value.TersUygula(new GeodeticPoint(X: k.X, Y: k.Y));
            if (t.IsFailure)
            {
                return Result<DonusumOzeti>.Failure(t.Error);
            }

            oncesi.Add(new KoordinatSatiri(k.NoktaNo, Yuvarla(t.Value.Y), Yuvarla(t.Value.X)));
        }

        return Result<DonusumOzeti>.Success(new DonusumOzeti(girdi, p.Value, oncesi.AsReadOnly()));
    }

    private static decimal Yuvarla(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// P7.3-C2: <c>NoktaNo;Y1;X1;Y2;X2</c> satır metni ayrıştırıcısı. Boş satırlar ve <c>#</c> ile başlayan satırlar atlanır; sayılar
/// <see cref="CultureInfo.InvariantCulture"/> (<c>.</c> ondalık ayracı) ile okunur. Hata: <c>ERR-GIRD-003</c> (satır no ile).
/// </summary>
public static class DonusumMetni
{
    public static Result<IReadOnlyList<DonusumOrtakNoktasi>> Ayristir(string? metin)
    {
        var sonuc = new List<DonusumOrtakNoktasi>();
        var satirlar = (metin ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < satirlar.Length; i++)
        {
            var s = satirlar[i].Trim();
            if (s.Length == 0 || s.StartsWith('#'))
            {
                continue;
            }

            var p = s.Split(';');
            if (p.Length != 5 || p[0].Trim().Length == 0
                || !Sayi(p[1], out var y1) || !Sayi(p[2], out var x1) || !Sayi(p[3], out var y2) || !Sayi(p[4], out var x2))
            {
                var e = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz);
                return Result<IReadOnlyList<DonusumOrtakNoktasi>>.Failure(e with
                {
                    TechnicalMessage = e.TechnicalMessage
                        .Replace("{command}", "DonusumMetni", StringComparison.Ordinal)
                        .Replace("{reason}", "line " + (i + 1).ToString(CultureInfo.InvariantCulture) + " must be NoktaNo;Y1;X1;Y2;X2 with '.' decimals", StringComparison.Ordinal),
                    UserMessage = "Ortak nokta satırı " + (i + 1).ToString(CultureInfo.InvariantCulture)
                        + " hatalı: 'NoktaNo;Y1;X1;Y2;X2' biçiminde ve ondalık ayracı '.' olmalı.",
                });
            }

            sonuc.Add(new DonusumOrtakNoktasi(p[0].Trim(), y1, x1, y2, x2));
        }

        return Result<IReadOnlyList<DonusumOrtakNoktasi>>.Success(sonuc.AsReadOnly());
    }

    /// <summary>Girdiyi metne çevirir (InvariantCulture).</summary>
    public static string Yaz(IEnumerable<DonusumOrtakNoktasi> noktalar)
    {
        ArgumentNullException.ThrowIfNull(noktalar);
        return string.Join(Environment.NewLine, noktalar.Select(n => string.Create(CultureInfo.InvariantCulture,
            $"{n.NoktaNo};{n.Y1};{n.X1};{n.Y2};{n.X2}")));
    }

    private static bool Sayi(string s, out decimal v) =>
        decimal.TryParse(s.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v);
}
