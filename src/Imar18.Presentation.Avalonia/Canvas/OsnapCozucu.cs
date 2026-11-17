using Imar18.Domain.Geometry;

namespace Imar18.Presentation.Avalonia.Canvas;

/// <summary>OSNAP (nesne yakalama) kipleri; birden fazlası birlikte açık olabilir.</summary>
[Flags]
public enum OsnapKipi
{
    Yok = 0,

    /// <summary>Köşe / uç nokta.</summary>
    Uc = 1,

    /// <summary>Kenar orta noktası.</summary>
    Orta = 2,

    /// <summary>İmleçten kenara dik ayak noktası (90°).</summary>
    Dik = 4,

    /// <summary>İki kenarın kesişimi.</summary>
    Kesisim = 8,
}

/// <summary>Yakalanan nokta (mm'ye yuvarlanmış dünya koordinatı) ve kip.</summary>
public sealed record OsnapSonucu(GeodeticPoint Nokta, OsnapKipi Kip);

/// <summary>
/// P7 OSNAP çözücüsü (tuval yardımcısı): imlece tolerans içindeki en öncelikli yakalama noktasını bulur. Öncelik
/// Uç &gt; Kesişim &gt; Orta &gt; Dik; aynı öncelikte en yakın nokta seçilir. Hesap <c>decimal</c> iledir ve yalnız imleç
/// gösterimini etkiler (geometriye/projeye yazmaz).
/// </summary>
public static class OsnapCozucu
{
    /// <summary>Kesişim taramasına alınacak en fazla yakın kenar (imleç başına O(k²)).</summary>
    public const int EnFazlaKesisimKenari = 64;

    public static OsnapSonucu? Coz(CanvasScene scene, GeodeticPoint imlec, decimal toleransM, OsnapKipi kipler)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (kipler == OsnapKipi.Yok || toleransM <= 0m)
        {
            return null;
        }

        var tol2 = toleransM * toleransM;
        OsnapSonucu? enIyi = null;
        var enIyiMesafe = decimal.MaxValue;
        var enIyiSira = int.MaxValue;
        var yakinKenarlar = new List<(GeodeticPoint A, GeodeticPoint B)>();

        void Aday(GeodeticPoint p, OsnapKipi kip, int sira)
        {
            var d = Mesafe2(p, imlec);
            if (d <= tol2 && (sira < enIyiSira || (sira == enIyiSira && d < enIyiMesafe)))
            {
                enIyi = new OsnapSonucu(Yuvarla(p), kip);
                enIyiSira = sira;
                enIyiMesafe = d;
            }
        }

        foreach (var pl in scene.Polylines)
        {
            var v = pl.Vertices;
            for (var i = 0; i < v.Count; i++)
            {
                if ((kipler & OsnapKipi.Uc) != 0 && Math.Abs(v[i].X - imlec.X) <= toleransM && Math.Abs(v[i].Y - imlec.Y) <= toleransM)
                {
                    Aday(v[i], OsnapKipi.Uc, 0);
                }

                if (i + 1 >= v.Count)
                {
                    continue;
                }

                var a = v[i];
                var b = v[i + 1];
                if (a == b || !KutuIcinde(a, b, imlec, toleransM))
                {
                    continue;
                }

                if ((kipler & OsnapKipi.Orta) != 0)
                {
                    Aday(new GeodeticPoint(X: (a.X + b.X) / 2m, Y: (a.Y + b.Y) / 2m), OsnapKipi.Orta, 2);
                }

                if ((kipler & OsnapKipi.Dik) != 0 && DikAyak(a, b, imlec) is { } ayak)
                {
                    Aday(ayak, OsnapKipi.Dik, 3);
                }

                if ((kipler & OsnapKipi.Kesisim) != 0 && yakinKenarlar.Count < EnFazlaKesisimKenari)
                {
                    yakinKenarlar.Add((a, b));
                }
            }
        }

        for (var i = 0; i < yakinKenarlar.Count; i++)
        {
            for (var j = i + 1; j < yakinKenarlar.Count; j++)
            {
                if (Kesisim(yakinKenarlar[i].A, yakinKenarlar[i].B, yakinKenarlar[j].A, yakinKenarlar[j].B) is { } k)
                {
                    Aday(k, OsnapKipi.Kesisim, 1);
                }
            }
        }

        return enIyi;
    }

    private static decimal Mesafe2(GeodeticPoint a, GeodeticPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    private static bool KutuIcinde(GeodeticPoint a, GeodeticPoint b, GeodeticPoint p, decimal tol) =>
        p.X >= Math.Min(a.X, b.X) - tol && p.X <= Math.Max(a.X, b.X) + tol
        && p.Y >= Math.Min(a.Y, b.Y) - tol && p.Y <= Math.Max(a.Y, b.Y) + tol;

    private static GeodeticPoint? DikAyak(GeodeticPoint a, GeodeticPoint b, GeodeticPoint p)
    {
        var ux = b.X - a.X;
        var uy = b.Y - a.Y;
        var uzunluk2 = (ux * ux) + (uy * uy);
        var t = (((p.X - a.X) * ux) + ((p.Y - a.Y) * uy)) / uzunluk2;
        return t is < 0m or > 1m ? null : new GeodeticPoint(X: a.X + (t * ux), Y: a.Y + (t * uy));
    }

    private static GeodeticPoint? Kesisim(GeodeticPoint a, GeodeticPoint b, GeodeticPoint c, GeodeticPoint d)
    {
        var rx = b.X - a.X;
        var ry = b.Y - a.Y;
        var sx = d.X - c.X;
        var sy = d.Y - c.Y;
        var payda = (rx * sy) - (ry * sx);
        if (payda == 0m)
        {
            return null; // paralel / çakışık
        }

        var qx = c.X - a.X;
        var qy = c.Y - a.Y;
        var t = ((qx * sy) - (qy * sx)) / payda;
        var u = ((qx * ry) - (qy * rx)) / payda;
        return t is < 0m or > 1m || u is < 0m or > 1m ? null : new GeodeticPoint(X: a.X + (t * rx), Y: a.Y + (t * ry));
    }

    private static GeodeticPoint Yuvarla(GeodeticPoint p) => new(
        X: decimal.Round(p.X, 3, MidpointRounding.AwayFromZero),
        Y: decimal.Round(p.Y, 3, MidpointRounding.AwayFromZero));
}
