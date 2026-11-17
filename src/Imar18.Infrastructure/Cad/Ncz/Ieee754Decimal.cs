using System.Numerics;

namespace Imar18.Infrastructure.Cad.Ncz;

/// <summary>
/// IEEE 754 binary64 bit desenini <c>double</c> tipine hic donusturmeden, <see cref="BigInteger"/> ile tam degerinden
/// <c>decimal</c>'e cozer ve istenen ondalik olcege <see cref="MidpointRounding.AwayFromZero"/> ile yuvarlar.
/// <para>
/// Neden: <c>(decimal)double</c> donusumu 15 anlamli basamaga yuvarlar (cift yuvarlama riski) ve mimari kural
/// (architecture.md) <c>double</c>'i yalnizca GeometryEngine/SkiaSharp'a sinirlar. Bu cozucu ham NCZ koordinatini
/// tek ve belirlenimci bir yuvarlamayla <c>decimal</c>'e tasir.
/// </para>
/// </summary>
internal static class Ieee754Decimal
{
    private const int ExponentMask = 0x7FF;
    private const int ExponentBias = 1075; // 1023 + 52 kesir biti
    private const int SubnormalExponent = -1074;
    private const ulong FractionMask = 0x000F_FFFF_FFFF_FFFFUL;
    private const ulong ImplicitBit = 1UL << 52;

    /// <summary>decimal'in 96 bitlik tamsayi buyuklugu sinirı (2^96 - 1).</summary>
    private static readonly BigInteger MaxMagnitude = (BigInteger.One << 96) - 1;

    /// <summary>
    /// Bit desenini cozer. NaN/sonsuz veya decimal araligini asan degerde <c>false</c> doner. Negatif sifir ve
    /// olcekte sifira yuvarlanan degerler isaretsiz <c>0</c> verir.
    /// </summary>
    /// <param name="bits">Little-endian okunmus binary64 bitleri.</param>
    /// <param name="scale">Ondalik basamak sayisi (0..28).</param>
    /// <param name="value">Cozulen deger.</param>
    public static bool TryDecode(ulong bits, int scale, out decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 28);

        value = 0m;
        var negative = (bits >> 63) != 0;
        var exponentBits = (int)((bits >> 52) & ExponentMask);
        var fraction = bits & FractionMask;

        if (exponentBits == ExponentMask)
        {
            return false; // NaN veya sonsuz
        }

        BigInteger mantissa;
        int exponent;
        if (exponentBits == 0)
        {
            mantissa = fraction;
            exponent = SubnormalExponent;
        }
        else
        {
            mantissa = fraction | ImplicitBit;
            exponent = exponentBits - ExponentBias;
        }

        if (mantissa.IsZero)
        {
            return true;
        }

        // |deger| * 10^olcek = mantissa * 2^us * 10^olcek
        var numerator = mantissa * BigInteger.Pow(10, scale);
        BigInteger magnitude;
        if (exponent >= 0)
        {
            if (exponent > 96)
            {
                return false; // 2^96 ustu: decimal araligi disinda
            }

            magnitude = numerator << exponent;
        }
        else
        {
            var denominator = BigInteger.One << -exponent;
            magnitude = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (remainder * 2 >= denominator)
            {
                magnitude += 1; // yarim ve ustu: sifirdan uzaga
            }
        }

        if (magnitude > MaxMagnitude)
        {
            return false;
        }

        if (magnitude.IsZero)
        {
            return true;
        }

        var lo = (int)(uint)(magnitude & uint.MaxValue);
        var mid = (int)(uint)((magnitude >> 32) & uint.MaxValue);
        var hi = (int)(uint)((magnitude >> 64) & uint.MaxValue);
        value = new decimal(lo, mid, hi, negative, (byte)scale);
        return true;
    }
}
