using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Geometry;

/// <summary>
/// P7.3-C2 (SAHA-007): ortak nokta satırı <c>NoktaNo;Y1;X1;Y2;X2</c>. I. sistem (<c>Y1, X1</c>) deprem öncesi, II. sistem
/// (<c>Y2, X2</c>) deprem sonrası koordinatlarıdır (jeodezik: Y Sağa/Doğu, X Yukarı/Kuzey; metre).
/// </summary>
public sealed record DonusumOrtakNoktasi(string NoktaNo, decimal Y1, decimal X1, decimal Y2, decimal X2);

/// <summary>
/// P7.3-C2: proje dönüşüm girdisi (RevisionId'ye dahildir). Proje noktaları "deprem sonrası" (II. sistem) kabul edilir;
/// "deprem öncesi" (I. sistem) koordinatları I → II dönüşümünün tersiyle hesaplanır. <see cref="Mk"/> koordine özetteki
/// <c>MK</c> sütunudur (mühendis girdisi; arayüz varsayılanı 0.09 — SORU-041).
/// </summary>
public sealed record DonusumGirdisi(DonusumModeli Model, decimal Mk, IReadOnlyList<DonusumOrtakNoktasi> OrtakNoktalar)
{
    /// <summary>Arayüz varsayılan MK değeri (SORU-041 varsayımı).</summary>
    public const decimal VarsayilanMk = 0.09m;

    /// <summary>Kabul edilen en büyük MK (m) — uygulama güvenlik sınırı, mevzuat değeri değildir.</summary>
    public const decimal MaxMk = 10m;

    /// <summary>Modelin gerektirdiği en az ortak nokta sayısı (Helmert 2, Afin 3).</summary>
    public static int EnAzNokta(DonusumModeli model) => model == DonusumModeli.Helmert2B ? 2 : 3;
}

/// <summary>Ortak noktanın dönüşüm sonrası (II. sistemde hesaplanan) koordinatı ve hatası: V = hesaplanan − II. sistem.</summary>
public sealed record DonusumArtigi(string NoktaNo, decimal Y, decimal X, decimal Vy, decimal Vx);

/// <summary>
/// SAHA-014: Netcad <c>.DNS</c> <c>$AMAT</c> katsayıları (dosyada <c>A, B, CY, CX, C, D</c> sırasıyla). Doğrulanmış ileri dönüşüm
/// (I. sistem = Deprem Öncesi → II. sistem = Deprem Sonrası; Y = Doğu, X = Kuzey):
/// <c>Y2 = A·Y1 + C·X1 + CY</c>, <c>X2 = B·Y1 + D·X1 + CX</c>. Helmert'te <c>A = D</c>, <c>C = −B</c> (gerçek DNS'te katsayılar
/// <c>$NOK</c> noktalarının en küçük kareler Helmert çözümüyle aynıdır: <c>A = a, C = b, B = −b</c>).
/// <see cref="Uygula"/> sonucu <see cref="PolygonRing.MaxScale"/> (6) ondalığa AwayFromZero yuvarlanır: CAD sözleşmesindeki
/// kesinlik zarfı korunur, 1 mm kuantizasyon yapılmaz (SORU-025/032); aynı girdi her zaman aynı sonucu verir (paylaşılan köşeler korunur).
/// </summary>
public sealed record DonusumKatsayilari(decimal A, decimal B, decimal C, decimal D, decimal Cy, decimal Cx)
{
    /// <summary>Uygulama güvenlik sınırı (mevzuat değeri değildir): |det − 1| bu değeri aşan matris reddedilir (ölçek ≈ ±%0.5).</summary>
    public const decimal MaxDeterminantSapmasi = 0.01m;

    /// <summary>Uygulama güvenlik sınırı (reviewer M2, taşma önlemi): |A|, |B|, |C|, |D| en çok bu değer olabilir.</summary>
    public const decimal MaxMatrisElemani = 2m;

    /// <summary>Uygulama güvenlik sınırı (reviewer M2, taşma önlemi): |CY|, |CX| (m) en çok bu değer olabilir.</summary>
    public const decimal MaxOtelemeM = 10_000_000m;

    /// <summary>Dönüştürülmüş koordinatın ondalık basamak sayısı (= <see cref="PolygonRing.MaxScale"/>).</summary>
    public const int KoordinatOndaligi = PolygonRing.MaxScale;

    /// <summary>2×2 matrisin determinantı <c>A·D − B·C</c> (alan ölçeği).</summary>
    public decimal Determinant => (A * D) - (B * C);

    /// <summary>4 parametreli Helmert biçimi mi (<c>A = D</c>, <c>C = −B</c>)?</summary>
    public bool HelmertMi => A == D && C == -B;

    /// <summary>Deprem Öncesi noktayı Deprem Sonrası'na ileri dönüştürür (6 ondalık, AwayFromZero).</summary>
    public GeodeticPoint Uygula(GeodeticPoint oncesi) => new(
        X: Math.Round((B * oncesi.Y) + (D * oncesi.X) + Cx, KoordinatOndaligi, MidpointRounding.AwayFromZero),
        Y: Math.Round((A * oncesi.Y) + (C * oncesi.X) + Cy, KoordinatOndaligi, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Katsayıların ileri dönüşüm için kabul edilebilirliği: null değil, matris elemanları |·| ≤ <see cref="MaxMatrisElemani"/> ve
    /// ötelemeler |·| ≤ <see cref="MaxOtelemeM"/> (decimal taşması olmaz), determinant pozitif (yansıma/tekillik yok) ve
    /// |det − 1| ≤ <see cref="MaxDeterminantSapmasi"/>. Aksi hâlde <c>ERR-CRS-004</c>; istisna atmaz.
    /// </summary>
    public static Result<DonusumKatsayilari> Dogrula(DonusumKatsayilari? k)
    {
        if (k is null)
        {
            return Hata("coefficients are missing");
        }

        if (new[] { k.A, k.B, k.C, k.D }.Any(v => Math.Abs(v) > MaxMatrisElemani) || Math.Abs(k.Cy) > MaxOtelemeM || Math.Abs(k.Cx) > MaxOtelemeM)
        {
            return Hata("coefficients exceed the application safety bounds (|A..D| <= 2, |CY|,|CX| <= 1e7 m)");
        }

        if (k.Determinant <= 0m)
        {
            return Hata("transformation matrix is singular or mirrored (det <= 0)");
        }

        return Math.Abs(k.Determinant - 1m) > MaxDeterminantSapmasi
            ? Hata("transformation scale is outside the application safety bound (|det - 1| > 0.01)")
            : Result<DonusumKatsayilari>.Success(k);

        static Result<DonusumKatsayilari> Hata(string neden)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CrsDonusumHesaplanamadi);
            return Result<DonusumKatsayilari>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", neden, StringComparison.Ordinal) });
        }
    }
}

/// <summary>
/// I → II sistemi 2B dönüşüm parametreleri (Netcad <c>.DNS</c> "A, B, C, D, CY, CX" düzeni — SAHA-014'te gerçek DNS ile doğrulandı):
/// <c>Y2 = A·Y1 + C·X1 + CY</c>, <c>X2 = B·Y1 + D·X1 + CX</c>. Helmert'te <c>C = −B</c>, <c>D = A</c>. Ölçek ve dönüklük yalnız
/// Helmert'te tanımlıdır; ortalama hata <c>m0 = √(Σv² / f)</c> (f = 0 ise null). P7.3-C2'deki eski etiketleme B ile C'yi yer
/// değiştirmiş yazıyordu (koordinat hesabı kendi içinde tutarlıydı; PDF etiketleri yanlıştı).
/// </summary>
public sealed record DonusumParametreleri(
    DonusumModeli Model,
    decimal A,
    decimal B,
    decimal C,
    decimal D,
    decimal Cy,
    decimal Cx,
    decimal? OlcekKatsayisi,
    decimal? DonuklukGrad,
    decimal? OrtalamaHataM,
    IReadOnlyList<DonusumArtigi> Artiklar)
{
    /// <summary>Katsayılar (Netcad düzeni).</summary>
    public DonusumKatsayilari Katsayilar => new(A, B, C, D, Cy, Cx);

    /// <summary>I. sistemden II. sisteme (deprem öncesi → sonrası), yuvarlamasız tam ondalık.</summary>
    public GeodeticPoint Uygula(GeodeticPoint birinci) => new(
        X: (B * birinci.Y) + (D * birinci.X) + Cx,
        Y: (A * birinci.Y) + (C * birinci.X) + Cy);

    /// <summary>
    /// II. sistemden I. sisteme (deprem sonrası → öncesi): 2×2 matrisin tam ondalık tersi
    /// (<c>Y1 = (D·dy − C·dx)/det</c>, <c>X1 = (A·dx − B·dy)/det</c>). Determinant sıfırsa <c>ERR-CRS-004</c>.
    /// </summary>
    public Result<GeodeticPoint> TersUygula(GeodeticPoint ikinci)
    {
        var det = (A * D) - (B * C);
        if (det == 0m)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CrsDonusumHesaplanamadi);
            return Result<GeodeticPoint>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", "transformation matrix is singular", StringComparison.Ordinal) });
        }

        var dy = ikinci.Y - Cy;
        var dx = ikinci.X - Cx;
        return Result<GeodeticPoint>.Success(new GeodeticPoint(
            X: ((A * dx) - (B * dy)) / det,
            Y: ((D * dy) - (C * dx)) / det));
    }

    /// <summary>
    /// SAHA-014: sabit (<c>.DNS</c>) katsayılardan parametre raporu — katsayılar birebir korunur; ortak noktalarda
    /// <c>V = hesaplanan − II. sistem</c> (tam ondalık), Helmert biçiminde ölçek <c>√(A² + C²)</c> ve dönüklük
    /// <c>atan2(C, A)·200/π</c> grad (12 ondalık), <c>m0 = √(Σ(Vy² + Vx²) / (2n − u))</c> (u = 4 Helmert, 6 Afin; f ≤ 0 ise null).
    /// Artıklar girdi sırasıyladır.
    /// </summary>
    public static DonusumParametreleri Katsayilardan(DonusumKatsayilari k, IReadOnlyList<DonusumOrtakNoktasi> noktalar)
    {
        ArgumentNullException.ThrowIfNull(k);
        ArgumentNullException.ThrowIfNull(noktalar);
        var helmert = k.HelmertMi;
        var artiklar = new List<DonusumArtigi>(noktalar.Count);
        var toplam = 0m;
        foreach (var n in noktalar)
        {
            ArgumentNullException.ThrowIfNull(n);
            var y = (k.A * n.Y1) + (k.C * n.X1) + k.Cy;
            var x = (k.B * n.Y1) + (k.D * n.X1) + k.Cx;
            var vy = y - n.Y2;
            var vx = x - n.X2;
            toplam += (vy * vy) + (vx * vx);
            artiklar.Add(new DonusumArtigi(n.NoktaNo.Trim(), y, x, vy, vx));
        }

        var f = (2 * noktalar.Count) - (helmert ? 4 : 6);
        return new DonusumParametreleri(
            Model: helmert ? DonusumModeli.Helmert2B : DonusumModeli.Afin2B,
            A: k.A,
            B: k.B,
            C: k.C,
            D: k.D,
            Cy: k.Cy,
            Cx: k.Cx,
            OlcekKatsayisi: helmert ? Numerics.DecimalMath.Sqrt((k.A * k.A) + (k.C * k.C)) : null,
            DonuklukGrad: helmert ? Math.Round(Numerics.DecimalMath.Atan2(k.C, k.A) * 200m / Numerics.DecimalMath.Pi, 12, MidpointRounding.AwayFromZero) : null,
            OrtalamaHataM: f > 0 ? Numerics.DecimalMath.Sqrt(toplam / f) : null,
            Artiklar: artiklar.AsReadOnly());
    }
}

/// <summary>
/// P7.3-C2: ortak noktalı 2B Helmert / Afin dönüşüm portu. Uygulaması GeometryEngine'dedir (<c>HelmertAfinHesaplayici</c>);
/// Application hesap motoruna doğrudan bağlanmaz. Hata: <c>ERR-CRS-004</c>.
/// </summary>
public interface IDonusumHesaplayici
{
    Result<DonusumParametreleri> Hesapla(DonusumModeli model, IReadOnlyList<DonusumOrtakNoktasi> ortakNoktalar);
}
