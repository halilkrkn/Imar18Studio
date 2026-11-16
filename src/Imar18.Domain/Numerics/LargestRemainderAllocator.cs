using System.Numerics;
using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Numerics;

/// <summary>Dağıtıma giren kalem: benzersiz kimlik (malik/parsel) ve kesirli ağırlık (başlangıç payı).</summary>
public sealed record AllocationWeight(string Id, Fraction Weight);

/// <summary>Dağıtım sonucu: kalem kimliği ve tam sayı birim (m² tahsiste 0,01 m²; tescilde 1 dm²).</summary>
public sealed record AllocationShare(string Id, BigInteger Units)
{
    /// <summary>Birimi m²'ye çevirir (1 birim = 0,01 m² = 1 dm²).</summary>
    public decimal ToAlanM2() => new Fraction(Units, 100).ToDecimal(2);
}

/// <summary>
/// Deterministik en büyük kalan (Largest Remainder) dağıtımı (spec 3.2 madde 4, D31 Kural 2, AC-PILOT-03).
/// Kotalar tam rasyonel (<see cref="Fraction"/>) hesaplanır; ΣÇıktı == toplam GARANTİDİR.
/// Eşit kalanda sıra: (1) en büyük başlangıç payı (ağırlık), (2) kimlik <see cref="StringComparer.Ordinal"/> sırası.
/// Çıktı kimliğe göre <see cref="StringComparer.Ordinal"/> sıralıdır; girdi sırasından bağımsızdır.
/// </summary>
public static class LargestRemainderAllocator
{
    /// <summary>
    /// <paramref name="totalUnits"/> tam sayı birimi ağırlıklara göre dağıtır.
    /// </summary>
    /// <returns>
    /// Başarıda kimlik sıralı paylar; hata durumunda <see cref="ErrorCodes.TapuHisseToplamiBirDegil"/> (Σ ağırlık ≠ 1)
    /// veya <see cref="ErrorCodes.TapuDagitimGirdisiGecersiz"/> (boş liste, boş/yinelenen kimlik, negatif ağırlık/toplam).
    /// </returns>
    public static Result<IReadOnlyList<AllocationShare>> Allocate(
        BigInteger totalUnits,
        IReadOnlyCollection<AllocationWeight> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var items = weights.ToArray(); // tek anlik kopya: dogrulama ve hesap ayni veriyi gorur

        if (totalUnits.Sign < 0)
        {
            return Fail(ErrorCodes.TapuDagitimGirdisiGecersiz, "{reason}", "total units is negative");
        }

        if (items.Length == 0)
        {
            return Fail(ErrorCodes.TapuDagitimGirdisiGecersiz, "{reason}", "no weights supplied");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            // Kimlik (TC No/ad olabilir) teknik mesaja yazilmaz; yalnizca sira numarasi (KVKK).
            if (item is null || string.IsNullOrWhiteSpace(item.Id))
            {
                return Fail(ErrorCodes.TapuDagitimGirdisiGecersiz, "{reason}", "empty or missing id");
            }

            if (item.Weight.Sign < 0)
            {
                return Fail(ErrorCodes.TapuDagitimGirdisiGecersiz, "{reason}", $"negative weight at item #{index + 1}");
            }

            if (!ids.Add(item.Id))
            {
                return Fail(ErrorCodes.TapuDagitimGirdisiGecersiz, "{reason}", $"duplicate id at item #{index + 1}");
            }
        }

        var weightSum = Fraction.Sum(items.Select(w => w.Weight));
        if (weightSum != Fraction.One)
        {
            return Fail(ErrorCodes.TapuHisseToplamiBirDegil, "{sum}", weightSum.ToString());
        }

        var total = Fraction.FromInteger(totalUnits);
        var rows = items
            .Select(w =>
            {
                var quota = total * w.Weight;
                var floor = quota.Floor();
                return new Row(w.Id, w.Weight, floor, quota - Fraction.FromInteger(floor));
            })
            .ToList();

        var distributed = rows.Aggregate(BigInteger.Zero, (acc, r) => acc + r.Floor);
        var deficit = totalUnits - distributed; // 0 <= deficit < rows.Count (Σ kalanlar tam sayı ve her kalan < 1)

        var bonusOrder = rows
            .OrderByDescending(r => r.Remainder)
            .ThenByDescending(r => r.Weight)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        var bonusIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < deficit; i++)
        {
            bonusIds.Add(bonusOrder[i].Id);
        }

        IReadOnlyList<AllocationShare> shares = rows
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new AllocationShare(r.Id, bonusIds.Contains(r.Id) ? r.Floor + BigInteger.One : r.Floor))
            .ToList();

        return Result<IReadOnlyList<AllocationShare>>.Success(shares);
    }

    /// <summary>
    /// m² alanı 0,01 m² (= 1 dm²) birimiyle dağıtır: birim sayısı = toplam × 100.
    /// </summary>
    /// <returns>
    /// Alan negatifse veya 2 ondalık basamaktan fazla hassasiyet içeriyorsa
    /// <see cref="ErrorCodes.TapuDagitimGirdisiGecersiz"/>; diğer hatalar <see cref="Allocate"/> gibidir.
    /// </returns>
    public static Result<IReadOnlyList<AllocationShare>> AllocateAlanM2(
        decimal totalM2,
        IReadOnlyCollection<AllocationWeight> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var units = Fraction.FromDecimal(totalM2) * Fraction.FromInteger(100);
        if (units.Sign < 0)
        {
            return Fail(ErrorCodes.TapuDagitimGirdisiGecersiz, "{reason}", "area is negative");
        }

        if (units.Denominator != BigInteger.One)
        {
            return Fail(ErrorCodes.TapuDagitimGirdisiGecersiz, "{reason}", "area has more than 2 decimal places");
        }

        return Allocate(units.Numerator, weights);
    }

    private static Result<IReadOnlyList<AllocationShare>> Fail(string code, string placeholder, string detail)
    {
        var error = ErrorCatalog.Get(code);
        return Result<IReadOnlyList<AllocationShare>>.Failure(
            error with { TechnicalMessage = error.TechnicalMessage.Replace(placeholder, detail, StringComparison.Ordinal) });
    }

    private sealed record Row(string Id, Fraction Weight, BigInteger Floor, Fraction Remainder);
}
