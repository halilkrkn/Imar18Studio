namespace Imar18.Domain.Geometry;

/// <summary>
/// Basit poligon (kendini kesmeyen halka) denetimi — decimal ile tam aritmetik (P5 reviewer H2). Koordinatlar ilk köşeye göre
/// yerelleştirilir (|Δ| &lt; 1e6, ≤ 6 ondalık → çapraz çarpım ≤ 26 anlamlı hane, decimal'de kesin). Bitişik olmayan iki kenar
/// herhangi bir noktada (uç dokunma dahil) temas ederse, bitişik iki kenar ise üst üste katlanırsa (geri dönüş) halka basit
/// değildir. O(n²); <see cref="PolygonRing.MaxVertexCount"/> sınırı içindedir. NTS <c>IsValid</c> GeometryEngine'de ayrıca kalır.
/// </summary>
public static class PolygonTopolojisi
{
    public static bool KendiniKesiyorMu(PolygonRing halka)
    {
        ArgumentNullException.ThrowIfNull(halka);
        var v = halka.Vertices;
        var n = v.Count;
        var o = v[0];
        var p = new (decimal X, decimal Y)[n];
        for (var i = 0; i < n; i++)
        {
            p[i] = (v[i].X - o.X, v[i].Y - o.Y);
        }

        for (var i = 0; i < n; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % n];
            var c = p[(i + 2) % n];
            // Bitişik kenarlar: ortak köşede geri katlanma (b → c, a–b doğrusu üzerinde ve a yönünde).
            if (Yon(a, b, c) == 0 && Nokta(b, a, c) > 0m)
            {
                return true;
            }

            for (var j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                {
                    continue; // ilk ve son kenar ortak köşe paylaşır (bitişik)
                }

                if (KenarlarTemas(a, b, p[j], p[(j + 1) % n]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int Yon((decimal X, decimal Y) a, (decimal X, decimal Y) b, (decimal X, decimal Y) c) =>
        Math.Sign(((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X)));

    /// <summary>(a − m) · (c − m): pozitifse a ve c, m'den aynı yöndedir.</summary>
    private static decimal Nokta((decimal X, decimal Y) m, (decimal X, decimal Y) a, (decimal X, decimal Y) c) =>
        ((a.X - m.X) * (c.X - m.X)) + ((a.Y - m.Y) * (c.Y - m.Y));

    private static bool Uzerinde((decimal X, decimal Y) a, (decimal X, decimal Y) b, (decimal X, decimal Y) c) =>
        Math.Min(a.X, b.X) <= c.X && c.X <= Math.Max(a.X, b.X) && Math.Min(a.Y, b.Y) <= c.Y && c.Y <= Math.Max(a.Y, b.Y);

    private static bool KenarlarTemas((decimal X, decimal Y) a, (decimal X, decimal Y) b, (decimal X, decimal Y) c, (decimal X, decimal Y) d)
    {
        var d1 = Yon(c, d, a);
        var d2 = Yon(c, d, b);
        var d3 = Yon(a, b, c);
        var d4 = Yon(a, b, d);
        if (d1 * d2 < 0 && d3 * d4 < 0)
        {
            return true;
        }

        return (d1 == 0 && Uzerinde(c, d, a)) || (d2 == 0 && Uzerinde(c, d, b))
            || (d3 == 0 && Uzerinde(a, b, c)) || (d4 == 0 && Uzerinde(a, b, d));
    }
}
