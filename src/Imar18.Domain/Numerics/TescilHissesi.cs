using System.Numerics;

namespace Imar18.Domain.Numerics;

/// <summary>
/// Tescil gosterimi (D31 Kural 2, spec 25.2): musatkil parsel (paydas sayisi 1) "TAM";
/// hisseli parselde payda = ParselAlani_m2 x 100 (dm2 tam sayi) ve pay tam sayi dm2 olarak yazilir.
/// Tescil payi sadelestirilmez (ortak payda korunur).
/// </summary>
public readonly record struct TescilHissesi(bool Tam, BigInteger Pay, BigInteger Payda)
{
    /// <summary>Parsel alanindan tescil paydasini (dm2 tam sayi) uretir.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Alan sifir veya negatifse.</exception>
    /// <exception cref="ArgumentException">Alan 2 ondalik basamaktan fazla hassasiyet iceriyorsa.</exception>
    public static BigInteger PaydaDm2(decimal parselAlaniM2)
    {
        if (parselAlaniM2 <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parselAlaniM2), parselAlaniM2, "Parsel alani pozitif olmalidir.");
        }

        var dm2 = Fraction.FromDecimal(parselAlaniM2) * Fraction.FromInteger(100);
        if (dm2.Denominator != BigInteger.One)
        {
            throw new ArgumentException(
                "Parsel alani 2 ondalik basamaktan (1 dm2) fazla hassasiyet iceremez.", nameof(parselAlaniM2));
        }

        return dm2.Numerator;
    }

    /// <summary>Hisseyi tescil gosterimine cevirir.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Paydas sayisi &lt; 1 veya alan &lt;= 0 ise.</exception>
    /// <exception cref="ArgumentException">
    /// Tek paydasta hisse 1 degilse; hisseli parselde hisse [0,1] disindaysa veya tam sayi dm2 degilse
    /// (once <see cref="LargestRemainderAllocator"/> ile dagitilmalidir).
    /// </exception>
    public static TescilHissesi Olustur(Fraction hisse, decimal parselAlaniM2, int paydasSayisi)
    {
        if (paydasSayisi < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(paydasSayisi), paydasSayisi, "Paydas sayisi en az 1 olmalidir.");
        }

        var payda = PaydaDm2(parselAlaniM2); // alan her durumda dogrulanir

        if (paydasSayisi == 1)
        {
            if (hisse != Fraction.One)
            {
                throw new ArgumentException("Musatkil (tek paydasli) parselde hisse 1 olmalidir.", nameof(hisse));
            }

            return new TescilHissesi(true, BigInteger.One, BigInteger.One);
        }

        if (hisse.Sign < 0 || hisse > Fraction.One)
        {
            throw new ArgumentException("Hisse 0 ile 1 arasinda olmalidir.", nameof(hisse));
        }

        var pay = hisse * Fraction.FromInteger(payda);
        if (pay.Denominator != BigInteger.One)
        {
            throw new ArgumentException(
                "Hisse, parsel alaninin dm2 payda sisteminde tam sayi pay vermiyor; once Largest Remainder ile dagitin.",
                nameof(hisse));
        }

        return new TescilHissesi(false, pay.Numerator, payda);
    }

    /// <summary>"TAM" veya "pay/payda".</summary>
    public override string ToString() => Tam ? "TAM" : $"{Pay}/{Payda}";
}
