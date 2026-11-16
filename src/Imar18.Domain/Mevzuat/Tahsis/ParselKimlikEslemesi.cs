using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Mevzuat.Tahsis;

/// <summary>Geçici hesap kodu (örn. 902/1/C1) ↔ kalıcı tescil kimliği (örn. 103/1) çifti (spec 25.3).</summary>
public sealed record ParselKimlikCifti(string GeciciKod, string TescilNo);

/// <summary>
/// Çift yönlü, bire bir parsel kimlik eşlemesi (ParcelIdMappingService, D31 Kural 3). Biçim zorlanmaz
/// (saha verisinde hem 901/A1 hem 902/1/C1 kullanılır); yalnızca boş olmama ve iki yönde benzersizlik doğrulanır.
/// </summary>
public sealed class ParselKimlikEslemesi
{
    private readonly Dictionary<string, string> _geciciden;
    private readonly Dictionary<string, string> _tesciltan;

    private ParselKimlikEslemesi(IReadOnlyList<ParselKimlikCifti> ciftler)
    {
        Ciftler = ciftler;
        _geciciden = ciftler.ToDictionary(c => c.GeciciKod, c => c.TescilNo, StringComparer.Ordinal);
        _tesciltan = ciftler.ToDictionary(c => c.TescilNo, c => c.GeciciKod, StringComparer.Ordinal);
    }

    /// <summary>Geçici koda göre sıralı çiftler.</summary>
    public IReadOnlyList<ParselKimlikCifti> Ciftler { get; }

    public static Result<ParselKimlikEslemesi> Olustur(IReadOnlyCollection<ParselKimlikCifti> ciftler)
    {
        ArgumentNullException.ThrowIfNull(ciftler);
        var items = ciftler.ToArray();

        if (items.Length == 0)
        {
            return Gecersiz("no id pairs supplied");
        }

        var gecici = new HashSet<string>(StringComparer.Ordinal);
        var tescil = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Length; i++)
        {
            var c = items[i];
            if (c is null || string.IsNullOrWhiteSpace(c.GeciciKod) || string.IsNullOrWhiteSpace(c.TescilNo))
            {
                return Gecersiz($"empty id at pair #{i + 1}");
            }

            if (!gecici.Add(c.GeciciKod))
            {
                return Gecersiz($"temporary code {c.GeciciKod} mapped more than once");
            }

            if (!tescil.Add(c.TescilNo))
            {
                return Gecersiz($"registry number {c.TescilNo} mapped more than once");
            }
        }

        return Result<ParselKimlikEslemesi>.Success(
            new ParselKimlikEslemesi(items.OrderBy(c => c.GeciciKod, StringComparer.Ordinal).ToList()));
    }

    public bool TryTescilNo(string geciciKod, out string tescilNo) =>
        _geciciden.TryGetValue(geciciKod, out tescilNo!);

    public bool TryGeciciKod(string tescilNo, out string geciciKod) =>
        _tesciltan.TryGetValue(tescilNo, out geciciKod!);

    private static Result<ParselKimlikEslemesi> Gecersiz(string neden) =>
        HataSablonu.Basarisiz<ParselKimlikEslemesi>(ErrorCodes.TapuParselKimlikEslemesiGecersiz, ("reason", neden));
}
