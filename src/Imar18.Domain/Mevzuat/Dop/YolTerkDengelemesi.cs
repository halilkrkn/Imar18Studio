using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tecviz;

namespace Imar18.Domain.Mevzuat.Dop;

/// <summary>Dengeleme cetveli girdi kalemi: geçici kimlik (örn. 902/1/C1, 902/1/Y1) ve Gauss hesap alanı (m²).</summary>
public sealed record DengelemeKalemi(string Id, decimal HesapAlaniM2);

/// <summary>Dengeleme cetveli satırı: hesaplanan alan, düzeltme ve dengelenmiş alan (spec 25.5 madde 4).</summary>
public sealed record DengelenmisKalem(string Id, decimal HesapAlaniM2, decimal DuzeltmeM2, decimal DengelenmisAlanM2);

/// <summary>Y1 dengeleme sonucu. Σ dengelenmiş = tapu toplamı (tam eşitlik).</summary>
public sealed record YolTerkDengelemeSonucu(
    decimal TapuToplamiM2,
    decimal HesapToplamiM2,
    decimal FarkM2,
    decimal TecvizSiniriM2,
    IReadOnlyList<DengelenmisKalem> ImarParselleri,
    DengelenmisKalem YolTerk,
    IReadOnlyList<DengelenmisKalem> DigerKamuAlanlari)
{
    /// <summary>İmar parselleri dengelenmiş toplamı (DOP sayfası R2 ile karşılaştırılır).</summary>
    public decimal ImarParselToplamiM2 => ImarParselleri.Sum(p => p.DengelenmisAlanM2);

    /// <summary>Tüm kalemlerin dengelenmiş toplamı.</summary>
    public decimal DengelenmisToplamM2 =>
        ImarParselToplamiM2 + YolTerk.DengelenmisAlanM2 + DigerKamuAlanlari.Sum(p => p.DengelenmisAlanM2);
}

/// <summary>
/// Tecviz içi farkın kamuya terk edilen yol (Y1) alanında dengelenmesi (spec 25.4, D31 Kural 4):
/// fark = Tapu − Σ Hesap yalnızca Y1'e eklenir; imar parselleri ve diğer kamu alanları (PARK vb.) düzeltme +0.00 alır.
/// Önkoşul: <see cref="TecvizChecker"/> değerlendirmesi geçmiş olmalıdır (parsel bazlı esas + toplam girildiyse çift-geçiş,
/// SORU-013/016); geçmediyse dengeleme yapılmaz (<see cref="ErrorCodes.TecvizSiniriAsildi"/>). Tapu toplamı tecviz parsellerinin
/// tapu toplamıdır; dengeleme kalemlerinin hesap toplamı tecviz hesap toplamına tam eşit olmalıdır.
/// </summary>
public static class YolTerkDengelemesi
{
    public static Result<YolTerkDengelemeSonucu> Dengele(
        IReadOnlyCollection<DengelemeKalemi> imarParselleri,
        DengelemeKalemi yolTerk,
        IReadOnlyCollection<DengelemeKalemi> digerKamuAlanlari,
        TecvizDegerlendirmesi tecviz)
    {
        ArgumentNullException.ThrowIfNull(tecviz);
        ArgumentNullException.ThrowIfNull(imarParselleri);
        ArgumentNullException.ThrowIfNull(yolTerk);
        ArgumentNullException.ThrowIfNull(digerKamuAlanlari);

        var parseller = imarParselleri.ToArray();
        var digerleri = digerKamuAlanlari.ToArray();

        if (parseller.Length == 0)
        {
            return Gecersiz("no zoning parcels supplied");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var tumu = parseller.Append(yolTerk).Concat(digerleri).ToArray();
        for (var i = 0; i < tumu.Length; i++)
        {
            var k = tumu[i];
            if (k is null || string.IsNullOrWhiteSpace(k.Id))
            {
                return Gecersiz($"empty id at item #{i + 1}");
            }

            if (!ids.Add(k.Id))
            {
                return Gecersiz($"duplicate id at item #{i + 1}");
            }

            if (k.HesapAlaniM2 <= 0m)
            {
                return Gecersiz($"non-positive area at item #{i + 1}");
            }
        }

        if (!tecviz.Gecti)
        {
            var asim = tecviz.Bulgular.FirstOrDefault(b => b.Code == ErrorCodes.TecvizSiniriAsildi);
            return Result<YolTerkDengelemeSonucu>.Failure(asim ?? HataSablonu.Doldur(ErrorCodes.TecvizSiniriAsildi,
                ("delta", "n/a"), ("limit", "n/a"), ("parcelId", "tecviz input incomplete")));
        }

        var tapuToplami = tecviz.ToplamSonucu?.TapuAlaniM2 ?? tecviz.ParselSonuclari.Sum(p => p.TapuAlaniM2);
        var tecvizHesapToplami = tecviz.ToplamSonucu?.HesapAlaniM2 ?? tecviz.ParselSonuclari.Sum(p => p.HesapAlaniM2);
        var hesapToplami = tumu.Sum(k => k.HesapAlaniM2);
        if (hesapToplami != tecvizHesapToplami)
        {
            return Gecersiz("balancing items total differs from tecviz computed total");
        }

        var fark = tapuToplami - hesapToplami;
        var sinir = tecviz.ToplamSonucu?.SinirM2 ?? tecviz.ParselSonuclari.Sum(p => p.SinirM2);

        var y1 = yolTerk.HesapAlaniM2 + fark;
        if (y1 <= 0m)
        {
            return Gecersiz("balanced road area is not positive");
        }

        return Result<YolTerkDengelemeSonucu>.Success(new YolTerkDengelemeSonucu(
            tapuToplami,
            hesapToplami,
            fark,
            sinir,
            parseller.Select(Degismez).ToList(),
            new DengelenmisKalem(yolTerk.Id, yolTerk.HesapAlaniM2, fark, y1),
            digerleri.Select(Degismez).ToList()));
    }

    private static DengelenmisKalem Degismez(DengelemeKalemi k) => new(k.Id, k.HesapAlaniM2, 0m, k.HesapAlaniM2);

    private static Result<YolTerkDengelemeSonucu> Gecersiz(string neden) =>
        HataSablonu.Basarisiz<YolTerkDengelemeSonucu>(ErrorCodes.DopGirdisiGecersiz, ("reason", neden));
}
