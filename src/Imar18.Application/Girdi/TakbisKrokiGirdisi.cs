using System.Globalization;
using System.Numerics;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;

namespace Imar18.Application.Girdi;

/// <summary>
/// Vektorel/manuel TAKBİS hisse satiri. <see cref="MalikKodu"/>: <c>AD SOYAD (BABA ADI)</c> etiketi veya eski anonim kod
/// (<c>M01</c>, <c>Malik-01</c>) — SORU-055; T.C. Kimlik No girilmez (KVKK, <see cref="MalikEtiketi"/>).
/// </summary>
public sealed record TakbisHisseGirdisi(string MalikKodu, BigInteger Pay, BigInteger Payda, bool Muvafakatli);

/// <summary>Vektorel/manuel TAKBİS kadastro parseli girdisi (P4.2).</summary>
public sealed record TakbisParselGirdisi(string ParselKimligi, decimal TapuAlaniM2, IReadOnlyList<TakbisHisseGirdisi> Hisseler);

/// <summary>Yaklasik yer krokisi satiri: malikin hangi kadastro parselinden hangi imar parseline ne kadar alan aldigi (FR-F1.12).</summary>
public sealed record KrokiEslesmeGirdisi(string MalikKodu, string KadastroParselKimligi, string ImarParselKodu, decimal AlanM2);

/// <summary>
/// TAKBİS girdisini dogrulayip Domain <see cref="KadastroParseli"/>'ne cevirir. Hisseler <see cref="Fraction"/> ile tam toplanir.
/// Hatalar: hisse yok ERR-TAPU-002, toplam ≠ 1 ERR-TAPU-003, diger gecersiz girdiler ERR-TAPU-004. Mesajlara malik adi/kodu
/// veya kimlik numarasi yazilmaz. SORU-055: malik alani <see cref="MalikEtiketi.GecerliMi"/> kuralina uyar (ad etiketi veya eski kod);
/// parsel ici yinelenen malik <see cref="MalikEtiketi.Anahtar"/> ile (yazim farkindan bagimsiz) denetlenir; kimlik yazildigi gibi saklanir.
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
        var gorulen = new HashSet<string>(StringComparer.Ordinal); // MalikEtiketi.Anahtar
        foreach (var h in girdi.Hisseler)
        {
            var kod = h.MalikKodu?.Trim() ?? string.Empty;
            if (kod.Length == 0)
            {
                return GirdiHatasi.Gecersiz<KadastroParseli>("owner code must not be empty");
            }

            if (MalikEtiketi.KimlikNumarasiIceriyor(kod))
            {
                return GirdiHatasi.Gecersiz<KadastroParseli>("owner field must not contain a national id number (KVKK)");
            }

            if (!MalikEtiketi.GecerliMi(kod))
            {
                return GirdiHatasi.Gecersiz<KadastroParseli>(
                    "owner field must be a name label 'AD SOYAD (BABA ADI)' or a code (letters, digits, space, - _ . ( ) / ' , & #; max 100; no ';', leading '#' or control characters) (KVKK)");
            }

            if (!gorulen.Add(MalikEtiketi.Anahtar(kod)))
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
}

/// <summary>
/// SAHA-011 (reviewer H2): parseller arasinda yazim farkli ayni malik (<c>Ayşe Yılmaz (Ali)</c> / <c>AYŞE YILMAZ (ALİ)</c>) Domain'de
/// Ordinal gruplandigi icin iki kisi sayilirdi. Her <see cref="MalikEtiketi.Anahtar"/> icin ilk gorulen yazim (parsel sirasiyla)
/// tum parsellerde kanonik <see cref="KadastroMaliki.MalikId"/> olur; hisse ve muvafakat degismez.
/// </summary>
public static class TakbisMalikKimlikleri
{
    public static IReadOnlyList<KadastroParseli> Birlestir(IReadOnlyList<KadastroParseli> parseller)
    {
        ArgumentNullException.ThrowIfNull(parseller);
        var kanonik = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in parseller.SelectMany(p => p.Malikler))
        {
            kanonik.TryAdd(MalikEtiketi.Anahtar(m.MalikId), m.MalikId);
        }

        return parseller
            .Select(p => p with { Malikler = p.Malikler.Select(m => m with { MalikId = kanonik[MalikEtiketi.Anahtar(m.MalikId)] }).ToList().AsReadOnly() })
            .ToList()
            .AsReadOnly();
    }
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

            // SAHA-011: yazım farkından bağımsız (Türkçe büyük harf, "AD / BABA" ≡ "AD (BABA)") eşleşir; Domain'e TAKBİS kimliği gider.
            var anahtar = MalikEtiketi.Anahtar(s.MalikKodu?.Trim() ?? string.Empty);
            var tapuMaliki = parsel.Malikler.FirstOrDefault(m => string.Equals(MalikEtiketi.Anahtar(m.MalikId), anahtar, StringComparison.Ordinal));
            if (tapuMaliki is null)
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

            sonuc.Add(new HakEslestirmeSatiri(tapuMaliki.MalikId, parselId, imar, s.AlanM2));
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
