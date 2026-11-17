using Imar18.Domain.Geometry;

namespace Imar18.Domain.Numerics;

/// <summary>
/// <c>double</c> kullanmadan decimal yardımcıları (AC-PILOT-01). Cepheli alan cetvelindeki kenar uzunluğu (spec 25.5 madde 3)
/// bu sınıfla hesaplanır.
/// </summary>
public static class DecimalMath
{
    /// <summary>
    /// Newton–Raphson karekök. Başlangıç tahmini kökün üstünde seçilir (değer ≥ 1 ise değerin kendisi, değilse 1); dizi
    /// monoton azalır ve azalma durduğunda decimal çözünürlüğünde (≈ 28 anlamlı hane) en yakın değere ulaşılmıştır.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Değer negatifse.</exception>
    public static decimal Sqrt(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (value == 0m)
        {
            return 0m;
        }

        var x = value >= 1m ? value : 1m;
        while (true)
        {
            var next = (x + (value / x)) / 2m;
            if (next >= x)
            {
                break;
            }

            x = next;
        }

        // Tam karelerde son hanedeki yuvarlama artığını temizler (25 → 5.000…0001 yerine 5).
        var yuvarlanmis = decimal.Round(x, 20, MidpointRounding.AwayFromZero);
        return yuvarlanmis * yuvarlanmis == value ? yuvarlanmis : x;
    }

    /// <summary>İki jeodezik nokta arası yatay uzaklık s = √(ΔY² + ΔX²) (m, yuvarlanmamış).</summary>
    public static decimal KenarUzunlugu(GeodeticPoint a, GeodeticPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Sqrt((dx * dx) + (dy * dy));
    }
}

/// <summary>
/// Fen Klasörü / Tescil Sayfası alan gösterimi (spec 25.6 madde 3): Hektar (He), Metrekare (M2, 0..9999) ve Desimetrekare
/// (Dm2, 0..99). Örn. 3539.74 m² → 0 | 3539 | 74.
/// </summary>
public readonly record struct HeM2Dm2(long He, int M2, int Dm2)
{
    /// <summary>Desteklenen en büyük alan (m²).</summary>
    public const decimal MaxAlanM2 = 1_000_000_000_000m;

    /// <summary>Alanı 2 ondalığa (1 dm², AwayFromZero) yuvarlar ve üç sütuna ayırır.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Alan negatif veya <see cref="MaxAlanM2"/>'den büyükse.</exception>
    public static HeM2Dm2 Ayir(decimal alanM2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(alanM2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(alanM2, MaxAlanM2);

        var dm2Toplam = (long)decimal.Round(alanM2 * 100m, 0, MidpointRounding.AwayFromZero);
        var he = dm2Toplam / 1_000_000L;
        var kalan = dm2Toplam % 1_000_000L;
        return new HeM2Dm2(he, (int)(kalan / 100L), (int)(kalan % 100L));
    }

    /// <summary>"He M2 Dm2" (Dm2 iki haneli).</summary>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{He} {M2} {Dm2:00}");
}
