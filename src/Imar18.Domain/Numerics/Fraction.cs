using System.Globalization;
using System.Numerics;

namespace Imar18.Domain.Numerics;

/// <summary>
/// Tam rasyonel kesir (BigInteger Pay / BigInteger Payda). Her islemde EBOB ile sadelestirilir,
/// payda daima &gt; 0 tutulur. double/float kullanilmaz (spec 3.2 madde 1 ve 4, AC-PILOT-03).
/// <c>default(Fraction)</c> gecerli bir sifirdir (0/1).
/// </summary>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    // default(Fraction) icin payda alani 0 kalir; Denominator ozelligi bunu 1 olarak okur.
    private readonly BigInteger _numerator;
    private readonly BigInteger _denominator;

    /// <summary>Sifir (0/1).</summary>
    public static Fraction Zero => default;

    /// <summary>Bir (1/1).</summary>
    public static Fraction One => new(BigInteger.One, BigInteger.One);

    /// <summary>
    /// Kesri olusturur ve sadelestirir.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Payda sifir veya negatifse.</exception>
    public Fraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominator), denominator, "Payda pozitif olmalidir (sifir veya negatif payda gecersizdir).");
        }

        (_numerator, _denominator) = Reduce(numerator, denominator);
    }

    private Fraction(BigInteger numerator, BigInteger denominator, bool alreadyReduced)
    {
        _ = alreadyReduced;
        _numerator = numerator;
        _denominator = denominator;
    }

    /// <summary>Sadelestirilmis pay (isaret payda degil pay uzerindedir).</summary>
    public BigInteger Numerator => _numerator;

    /// <summary>Sadelestirilmis payda (daima &gt; 0).</summary>
    public BigInteger Denominator => _denominator.IsZero ? BigInteger.One : _denominator;

    /// <summary>Deger sifir mi?</summary>
    public bool IsZero => _numerator.IsZero;

    /// <summary>Isaret: -1, 0 veya 1.</summary>
    public int Sign => _numerator.Sign;

    /// <summary>Tam sayidan kesir uretir.</summary>
    public static Fraction FromInteger(BigInteger value) => new(value, BigInteger.One, true);

    /// <summary>
    /// Decimal degeri KAYIPSIZ tam kesre cevirir (decimal'in ondalik yapisi: mantis / 10^scale). Yuvarlama yapilmaz.
    /// </summary>
    public static Fraction FromDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var scale = (bits[3] >> 16) & 0xFF;
        var negative = (bits[3] & unchecked((int)0x80000000)) != 0;

        var mantissa = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        if (negative)
        {
            mantissa = -mantissa;
        }

        return new Fraction(mantissa, BigInteger.Pow(10, scale));
    }

    /// <summary>
    /// Decimal'e cevirir: <paramref name="scale"/> ondalik basamaga MidpointRounding.AwayFromZero ile yuvarlar.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">scale 0..28 disindaysa.</exception>
    /// <exception cref="OverflowException">Sonuc decimal araligina sigmazsa.</exception>
    public decimal ToDecimal(int scale = 6)
    {
        if (scale is < 0 or > 28)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "scale 0..28 araliginda olmalidir.");
        }

        var negative = _numerator.Sign < 0;
        var scaledNumerator = BigInteger.Abs(_numerator) * BigInteger.Pow(10, scale);
        var denominator = Denominator;

        var quotient = BigInteger.DivRem(scaledNumerator, denominator, out var remainder);
        if (remainder * 2 >= denominator)
        {
            quotient += BigInteger.One; // yarim ve uzeri: sifirdan uzaga
        }

        var bytes = quotient.ToByteArray(isUnsigned: true);
        if (bytes.Length > 12 && bytes.Skip(12).Any(b => b != 0))
        {
            throw new OverflowException("Deger System.Decimal araligina sigmiyor.");
        }

        Span<byte> padded = stackalloc byte[12];
        bytes.AsSpan(0, Math.Min(12, bytes.Length)).CopyTo(padded);

        var lo = BitConverter.ToInt32(padded[..4]);
        var mid = BitConverter.ToInt32(padded.Slice(4, 4));
        var hi = BitConverter.ToInt32(padded.Slice(8, 4));

        return new decimal(lo, mid, hi, negative && !quotient.IsZero, (byte)scale);
    }

    /// <summary>Mutlak deger.</summary>
    public Fraction Abs() => _numerator.Sign < 0 ? new Fraction(-_numerator, Denominator, true) : this;

    /// <summary>Tabana yuvarlama (negatiflerde -sonsuza dogru).</summary>
    public BigInteger Floor()
    {
        var quotient = BigInteger.DivRem(_numerator, Denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - BigInteger.One : quotient;
    }

    /// <summary>Kesirleri toplar; bos dizi sifir verir.</summary>
    public static Fraction Sum(IEnumerable<Fraction> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var total = Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    public static Fraction operator +(Fraction left, Fraction right) =>
        new(left._numerator * right.Denominator + right._numerator * left.Denominator, left.Denominator * right.Denominator);

    public static Fraction operator -(Fraction left, Fraction right) =>
        new(left._numerator * right.Denominator - right._numerator * left.Denominator, left.Denominator * right.Denominator);

    public static Fraction operator *(Fraction left, Fraction right) =>
        new(left._numerator * right._numerator, left.Denominator * right.Denominator);

    public static Fraction operator /(Fraction left, Fraction right)
    {
        if (right.IsZero)
        {
            throw new DivideByZeroException("Sifir kesre bolunemez.");
        }

        var numerator = left._numerator * right.Denominator;
        var denominator = left.Denominator * right._numerator;
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        return new Fraction(numerator, denominator);
    }

    public static Fraction operator -(Fraction value) => new(-value._numerator, value.Denominator, true);

    public static bool operator ==(Fraction left, Fraction right) => left.Equals(right);

    public static bool operator !=(Fraction left, Fraction right) => !left.Equals(right);

    public static bool operator <(Fraction left, Fraction right) => left.CompareTo(right) < 0;

    public static bool operator >(Fraction left, Fraction right) => left.CompareTo(right) > 0;

    public static bool operator <=(Fraction left, Fraction right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Fraction left, Fraction right) => left.CompareTo(right) >= 0;

    /// <summary>Deger esitligi (kesirler daima sadelestirilmis oldugundan yapisal karsilastirma yeterlidir).</summary>
    public bool Equals(Fraction other) => _numerator == other._numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is Fraction other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_numerator, Denominator);

    /// <summary>Capraz carpim ile tam karsilastirma (payda daima pozitif).</summary>
    public int CompareTo(Fraction other) =>
        (_numerator * other.Denominator).CompareTo(other._numerator * Denominator);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{_numerator}/{Denominator}");

    private static (BigInteger Numerator, BigInteger Denominator) Reduce(BigInteger numerator, BigInteger denominator)
    {
        if (numerator.IsZero)
        {
            return (BigInteger.Zero, BigInteger.One);
        }

        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return gcd.IsOne ? (numerator, denominator) : (numerator / gcd, denominator / gcd);
    }
}
