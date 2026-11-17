using System.Globalization;
using System.Numerics;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;

namespace Imar18.Application.Girdi;

/// <summary>Vektorel/manuel TAKBİS hisse satiri. <see cref="MalikKodu"/> anonim koddur (TC Kimlik No girilmez — KVKK).</summary>
public sealed record TakbisHisseGirdisi(string MalikKodu, BigInteger Pay, BigInteger Payda, bool Muvafakatli);

/// <summary>Vektorel/manuel TAKBİS kadastro parseli girdisi (P4.2).</summary>
public sealed record TakbisParselGirdisi(string ParselKimligi, decimal TapuAlaniM2, IReadOnlyList<TakbisHisseGirdisi> Hisseler);

/// <summary>Yaklasik yer krokisi satiri: malikin hangi kadastro parselinden hangi imar parseline ne kadar alan aldigi (FR-F1.12).</summary>
public sealed record KrokiEslesmeGirdisi(string MalikKodu, string KadastroParselKimligi, string ImarParselKodu, decimal AlanM2);

/// <summary>
/// TAKBİS girdisini dogrulayip Domain <see cref="KadastroParseli"/>'ne cevirir. Hisseler <see cref="Fraction"/> ile tam toplanir.
/// Hatalar: hisse yok ERR-TAPU-002, toplam ≠ 1 ERR-TAPU-003, diger gecersiz girdiler ERR-TAPU-004. Mesajlara malik kodu
/// veya kimlik numarasi yazilmaz.
/// </summary>
public static class TakbisGirdiDogrulayici
{
    public static Result<KadastroParseli> Dogrula(TakbisParselGirdisi girdi)
    {
        ArgumentNullException.ThrowIfNull(girdi);

        if (string.IsNullOrWhiteSpace(girdi.ParselKimligi))
        {
            return GirdiHatasi.Gecersiz<KadastroParseli>("parcel id must not be empty");
        }

        if (girdi.TapuAlaniM2 <= 0m || decimal.Round(girdi.TapuAlaniM2, 2) != girdi.TapuAlaniM2)
        {
            return GirdiHatasi.Gecersiz<KadastroParseli>("deed area must be positive with at most 2 decimals");
        }

        if (girdi.Hisseler is null || girdi.Hisseler.Count == 0)
        {
            var e = ErrorCatalog.Get(ErrorCodes.TapuHisseEksik);
            return Result<KadastroParseli>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{ownerId}", "parcel", StringComparison.Ordinal) });
        }

        var malikler = new List<KadastroMaliki>(girdi.Hisseler.Count);
        var gorulen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in girdi.Hisseler)
        {
            var kod = h.MalikKodu?.Trim() ?? string.Empty;
            if (kod.Length == 0)
            {
                return GirdiHatasi.Gecersiz<KadastroParseli>("owner code must not be empty");
            }

            if (!AnonimKodGibi(kod) || KimlikNumarasiGibi(kod))
            {
                return GirdiHatasi.Gecersiz<KadastroParseli>(
                    "owner code must be an anonymous code (ASCII letters/digits/-/_/., no spaces, not a national id number) (KVKK)");
            }

            if (!gorulen.Add(kod))
            {
                return GirdiHatasi.Gecersiz<KadastroParseli>("duplicate owner code in parcel");
            }

            if (h.Pay <= BigInteger.Zero || h.Payda <= BigInteger.Zero)
            {
                return GirdiHatasi.Gecersiz<KadastroParseli>("share numerator and denominator must be positive");
            }

            malikler.Add(new KadastroMaliki(kod, new Fraction(h.Pay, h.Payda), h.Muvafakatli));
        }

        var toplam = Fraction.Sum(malikler.Select(m => m.Hisse));
        if (toplam != Fraction.One)
        {
            var e = ErrorCatalog.Get(ErrorCodes.TapuHisseToplamiBirDegil);
            return Result<KadastroParseli>.Failure(e with
            {
                TechnicalMessage = e.TechnicalMessage.Replace(
                    "{sum}", $"{toplam.Numerator.ToString(CultureInfo.InvariantCulture)}/{toplam.Denominator.ToString(CultureInfo.InvariantCulture)}",
                    StringComparison.Ordinal),
            });
        }

        return Result<KadastroParseli>.Success(new KadastroParseli(girdi.ParselKimligi.Trim(), girdi.TapuAlaniM2, malikler.AsReadOnly()));
    }

    /// <summary>Ayiraclar atildiginda 11 haneli, sifirla baslamayan sayi TC Kimlik No bicimidir ("123-456-789-01" dahil).</summary>
    private static bool KimlikNumarasiGibi(string kod)
    {
        var rakamlar = new string(kod.Where(char.IsAsciiDigit).ToArray());
        return rakamlar.Length == 11 && rakamlar[0] != '0' && kod.All(c => char.IsAsciiDigit(c) || c is '-' or '.' or '_');
    }

    /// <summary>Anonim kod: 1–32 ASCII harf/rakam ve - _ . (ad-soyad gibi bosluklu veya Turkce harfli metin reddedilir).</summary>
    private static bool AnonimKodGibi(string kod) =>
        kod.Length is >= 1 and <= 32 && kod.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}

/// <summary>Kroki satirlarini dogrulayip Domain <see cref="HakEslestirmeSatiri"/> listesine cevirir.</summary>
public static class KrokiGirdiDogrulayici
{
    public static Result<IReadOnlyList<HakEslestirmeSatiri>> Dogrula(IReadOnlyList<KrokiEslesmeGirdisi> satirlar, IReadOnlyList<KadastroParseli> kadastro)
    {
        ArgumentNullException.ThrowIfNull(satirlar);
        ArgumentNullException.ThrowIfNull(kadastro);

        if (satirlar.Count == 0)
        {
            return GirdiHatasi.Gecersiz<IReadOnlyList<HakEslestirmeSatiri>>("sketch must contain at least one row");
        }

        if (kadastro.GroupBy(p => p.ParselId, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            return GirdiHatasi.Gecersiz<IReadOnlyList<HakEslestirmeSatiri>>("duplicate cadastral parcel id in TAKBIS input");
        }

        var parseller = kadastro.ToDictionary(p => p.ParselId, StringComparer.Ordinal);
        var sonuc = new List<HakEslestirmeSatiri>(satirlar.Count);
        foreach (var s in satirlar)
        {
            var parselId = s.KadastroParselKimligi?.Trim() ?? string.Empty;
            if (!parseller.TryGetValue(parselId, out var parsel))
            {
                return GirdiHatasi.Gecersiz<IReadOnlyList<HakEslestirmeSatiri>>("sketch row references an unknown cadastral parcel");
            }

            var malik = s.MalikKodu?.Trim() ?? string.Empty;
            if (!parsel.Malikler.Any(m => string.Equals(m.MalikId, malik, StringComparison.Ordinal)))
            {
                var e = ErrorCatalog.Get(ErrorCodes.TapuMalikEslesmedi);
                return Result<IReadOnlyList<HakEslestirmeSatiri>>.Failure(e with
                {
                    TechnicalMessage = e.TechnicalMessage.Replace("{ownerId}", "sketch row owner", StringComparison.Ordinal),
                });
            }

            var imar = s.ImarParselKodu?.Trim() ?? string.Empty;
            if (imar.Length == 0)
            {
                return GirdiHatasi.Gecersiz<IReadOnlyList<HakEslestirmeSatiri>>("zoning parcel code must not be empty");
            }

            if (s.AlanM2 <= 0m || decimal.Round(s.AlanM2, 2) != s.AlanM2)
            {
                return GirdiHatasi.Gecersiz<IReadOnlyList<HakEslestirmeSatiri>>("sketch area must be positive with at most 2 decimals");
            }

            sonuc.Add(new HakEslestirmeSatiri(malik, parselId, imar, s.AlanM2));
        }

        return Result<IReadOnlyList<HakEslestirmeSatiri>>.Success(sonuc.AsReadOnly());
    }
}

internal static class GirdiHatasi
{
    public static Result<T> Gecersiz<T>(string reason)
    {
        var e = ErrorCatalog.Get(ErrorCodes.TapuDagitimGirdisiGecersiz);
        return Result<T>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", reason, StringComparison.Ordinal) });
    }
}
