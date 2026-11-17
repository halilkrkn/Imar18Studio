using System.Globalization;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.GeometryEngine.Crs;

/// <summary>Ortak noktadaki donusum artigi (hesaplanan - hedef), metre.</summary>
public sealed record NoktaArtigi(string NoktaId, decimal Vx, decimal Vy, decimal V, bool ToleransAsimi);

/// <summary>
/// 2B Helmert/Afin donusum raporu (P4.2). Parametreler mutlak koordinat cercevesindedir:
/// Helmert: X' = a X - b Y + tx, Y' = b X + a Y + ty; Afin: X' = a0 + a1 X + a2 Y, Y' = b0 + b1 X + b2 Y
/// (X = Kuzey, Y = Dogu). <see cref="KareselOrtalamaHataM"/> = sqrt(Σv² / f), f = serbestlik derecesi (f = 0 ise null).
/// </summary>
public sealed record DonusumRaporu(
    DonusumModeli Model,
    IReadOnlyDictionary<string, decimal> Parametreler,
    decimal? OlcekFaktoru,
    decimal? DonmeAcisiGrad,
    IReadOnlyList<NoktaArtigi> Artiklar,
    NoktaArtigi EnBuyukArtik,
    decimal? KareselOrtalamaHataM,
    int SerbestlikDerecesi,
    IReadOnlyList<AppError> Uyarilar)
{
    /// <summary>Rapor parametreleriyle bir noktayi donusturur.</summary>
    public GeodeticPoint Uygula(GeodeticPoint p) => Model == DonusumModeli.Helmert2B
        ? new GeodeticPoint(
            X: (Parametreler["a"] * p.X) - (Parametreler["b"] * p.Y) + Parametreler["tx"],
            Y: (Parametreler["b"] * p.X) + (Parametreler["a"] * p.Y) + Parametreler["ty"])
        : new GeodeticPoint(
            X: Parametreler["a0"] + (Parametreler["a1"] * p.X) + (Parametreler["a2"] * p.Y),
            Y: Parametreler["b0"] + (Parametreler["b1"] * p.X) + (Parametreler["b2"] * p.Y));
}

/// <summary>
/// En kucuk kareler 2B Helmert / Afin kestirimi (P4.2, roadmap §4 HelmertTransformReport). Normal denklemler agirlik
/// merkezine indirgenmis koordinatlarla <c>decimal</c>'de cozulur (kesinlik); yalnizca donme acisi (atan2) icin
/// <c>double</c> kullanilir. Tolerans muhendis girdisidir (sabit deger kodlanmaz). Hata: ERR-CRS-004.
/// </summary>
public static class HelmertAfinHesaplayici
{
    /// <summary>Uygulama guvenlik siniri: en fazla ortak nokta.</summary>
    public const int MaxNoktaSayisi = 500;

    /// <summary>Agirlik merkezine gore en buyuk yayilim (m): decimal normal denklemlerinde tasmayi onler.</summary>
    public const decimal MaxYayilimM = 100_000m;

    /// <summary>Afin normal matrisi icin en kucuk λmin/λmax (uygulama guvenlik siniri).</summary>
    public const decimal KosulEsigi = 1e-8m;

    public static Result<DonusumRaporu> Hesapla(DonusumModeli model, IReadOnlyList<OrtakNokta> noktalar, decimal? toleransM)
    {
        ArgumentNullException.ThrowIfNull(noktalar);

        var enAz = model == DonusumModeli.Helmert2B ? 2 : 3;
        if (noktalar.Count < enAz)
        {
            return Hata($"at least {enAz} common points are required for {model}");
        }

        if (noktalar.Count > MaxNoktaSayisi)
        {
            return Hata($"at most {MaxNoktaSayisi} common points are supported");
        }

        if (toleransM is < 0m)
        {
            return Hata("tolerance must not be negative");
        }

        var kimlikler = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in noktalar)
        {
            if (string.IsNullOrWhiteSpace(n.NoktaId) || !kimlikler.Add(n.NoktaId.Trim()))
            {
                return Hata("point id must be non-empty and unique");
            }
        }

        // Kimlik sirasina gore islenir: girdi sirasindan bagimsiz, tekrar edilebilir sonuc.
        var sirali = noktalar.OrderBy(n => n.NoktaId.Trim(), StringComparer.Ordinal).ToArray();
        try
        {
            return model == DonusumModeli.Helmert2B ? Helmert(sirali, noktalar, toleransM) : Afin(sirali, noktalar, toleransM);
        }
        catch (OverflowException)
        {
            return Hata("numeric overflow; reduce the control point spread");
        }
    }

    private static Result<DonusumRaporu> Helmert(OrtakNokta[] n, IReadOnlyList<OrtakNokta> girdiSirasi, decimal? tol)
    {
        var (xs, ys, xt, yt, sinir) = Merkezler(n);
        if (sinir is not null)
        {
            return Hata(sinir);
        }

        decimal sxx = 0m, sa = 0m, sb = 0m;
        foreach (var p in n)
        {
            var x = p.Kaynak.X - xs;
            var y = p.Kaynak.Y - ys;
            var bx = p.Hedef.X - xt;
            var by = p.Hedef.Y - yt;
            sxx += (x * x) + (y * y);
            sa += (x * bx) + (y * by);
            sb += (x * by) - (y * bx);
        }

        if (sxx == 0m)
        {
            return Hata("points are coincident or collinear; geometry is degenerate");
        }

        var a = sa / sxx;
        var b = sb / sxx;
        var tx = xt - (a * xs) + (b * ys);
        var ty = yt - (b * xs) - (a * ys);
        var parametreler = new SortedDictionary<string, decimal>(StringComparer.Ordinal) { ["a"] = a, ["b"] = b, ["tx"] = tx, ["ty"] = ty };
        var olcek = Sqrt((a * a) + (b * b));
        var donme = decimal.Round((decimal)(Math.Atan2((double)b, (double)a) * 200d / Math.PI), 12, MidpointRounding.AwayFromZero);
        return Rapor(DonusumModeli.Helmert2B, parametreler, olcek, donme, girdiSirasi, tol, (2 * n.Length) - 4);
    }

    private static Result<DonusumRaporu> Afin(OrtakNokta[] n, IReadOnlyList<OrtakNokta> girdiSirasi, decimal? tol)
    {
        var (xs, ys, xt, yt, sinir) = Merkezler(n);
        if (sinir is not null)
        {
            return Hata(sinir);
        }

        decimal sxx = 0m, sxy = 0m, syy = 0m, sxX = 0m, syX = 0m, sxY = 0m, syY = 0m;
        foreach (var p in n)
        {
            var x = p.Kaynak.X - xs;
            var y = p.Kaynak.Y - ys;
            var bx = p.Hedef.X - xt;
            var by = p.Hedef.Y - yt;
            sxx += x * x;
            sxy += x * y;
            syy += y * y;
            sxX += x * bx;
            syX += y * bx;
            sxY += x * by;
            syY += y * by;
        }

        var det = (sxx * syy) - (sxy * sxy);
        var iz = sxx + syy;
        // Kosul olcusu det / iz² ≈ λmin / λmax: 1e-8 altinda (ornek 1 km'de 10 cm'den dar serit) geometri kotu kosulludur.
        if (det <= 0m || iz == 0m || det / (iz * iz) < KosulEsigi)
        {
            return Hata("points are collinear or nearly collinear; affine geometry is ill-conditioned");
        }

        var a1 = ((sxX * syy) - (syX * sxy)) / det;
        var a2 = ((syX * sxx) - (sxX * sxy)) / det;
        var b1 = ((sxY * syy) - (syY * sxy)) / det;
        var b2 = ((syY * sxx) - (sxY * sxy)) / det;
        var a0 = xt - (a1 * xs) - (a2 * ys);
        var b0 = yt - (b1 * xs) - (b2 * ys);
        var parametreler = new SortedDictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["a0"] = a0, ["a1"] = a1, ["a2"] = a2, ["b0"] = b0, ["b1"] = b1, ["b2"] = b2,
        };
        return Rapor(DonusumModeli.Afin2B, parametreler, null, null, girdiSirasi, tol, (2 * n.Length) - 6);
    }

    private static Result<DonusumRaporu> Rapor(
        DonusumModeli model,
        SortedDictionary<string, decimal> parametreler,
        decimal? olcek,
        decimal? donme,
        IReadOnlyList<OrtakNokta> girdiSirasi,
        decimal? tol,
        int serbestlik)
    {
        var gecici = new DonusumRaporu(model, parametreler, olcek, donme, [], new NoktaArtigi(string.Empty, 0m, 0m, 0m, false), null, serbestlik, []);
        var artiklar = new List<NoktaArtigi>(girdiSirasi.Count);
        var toplam = 0m;
        foreach (var p in girdiSirasi)
        {
            var h = gecici.Uygula(p.Kaynak);
            var vx = h.X - p.Hedef.X;
            var vy = h.Y - p.Hedef.Y;
            var kare = (vx * vx) + (vy * vy);
            toplam += kare;
            var v = Sqrt(kare);
            artiklar.Add(new NoktaArtigi(p.NoktaId.Trim(), vx, vy, v, tol is not null && v > tol.Value));
        }

        decimal? m0 = serbestlik > 0 ? Sqrt(toplam / serbestlik) : null;
        var enBuyuk = artiklar.OrderByDescending(a => a.V).ThenBy(a => a.NoktaId, StringComparer.Ordinal).First();
        IReadOnlyList<AppError> uyarilar = [];
        var asim = artiklar.Count(a => a.ToleransAsimi);
        if (asim > 0)
        {
            var u = ErrorCatalog.Get(ErrorCodes.CrsDonusumToleransAsimi);
            uyarilar = [u with { TechnicalMessage = u.TechnicalMessage
                .Replace("{count}", $"{asim.ToString(CultureInfo.InvariantCulture)} of {artiklar.Count.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal)
                .Replace("{tolerance}", tol!.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) }];
        }

        return Result<DonusumRaporu>.Success(gecici with
        {
            Artiklar = artiklar.AsReadOnly(),
            EnBuyukArtik = enBuyuk,
            KareselOrtalamaHataM = m0,
            Uyarilar = uyarilar,
        });
    }

    private static (decimal Xs, decimal Ys, decimal Xt, decimal Yt, string? Hata) Merkezler(OrtakNokta[] n)
    {
        decimal xs = 0m, ys = 0m, xt = 0m, yt = 0m;
        foreach (var p in n)
        {
            xs += p.Kaynak.X;
            ys += p.Kaynak.Y;
            xt += p.Hedef.X;
            yt += p.Hedef.Y;
        }

        xs /= n.Length;
        ys /= n.Length;
        xt /= n.Length;
        yt /= n.Length;
        foreach (var p in n)
        {
            if (Math.Abs(p.Kaynak.X - xs) > MaxYayilimM || Math.Abs(p.Kaynak.Y - ys) > MaxYayilimM
                || Math.Abs(p.Hedef.X - xt) > MaxYayilimM || Math.Abs(p.Hedef.Y - yt) > MaxYayilimM)
            {
                return (0m, 0m, 0m, 0m, "control point spread exceeds 100 km around the centroid");
            }
        }

        return (xs, ys, xt, yt, null);
    }

    /// <summary>decimal karekok (Newton; double tahminle baslar, decimal'de 3 adim inceltir).</summary>
    internal static decimal Sqrt(decimal value)
    {
        if (value <= 0m)
        {
            return 0m;
        }

        var x = (decimal)Math.Sqrt((double)value);
        if (x == 0m)
        {
            x = value;
        }

        for (var i = 0; i < 4; i++)
        {
            x = (x + (value / x)) / 2m;
        }

        return x;
    }

    private static Result<DonusumRaporu> Hata(string reason)
    {
        var e = ErrorCatalog.Get(ErrorCodes.CrsDonusumHesaplanamadi);
        return Result<DonusumRaporu>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", reason, StringComparison.Ordinal) });
    }
}
