using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Mevzuat.Tecviz;

/// <summary>Kümülatif kademe: [AltSinirM2, UstSinirM2) aralığında marjinal oran; son kademe açık uçludur (UstSinirM2 = null).</summary>
public sealed record TecvizKademesi(decimal AltSinirM2, decimal? UstSinirM2, decimal MarjinalOran);

/// <summary>
/// TPYS Md. 8(2) kademe tablosu. Ürün içinde SABİT TABLO YOKTUR (spec 4.3.2 değerleri doğrulanmamış TASLAK'tır);
/// tablo mühendis/mevzuat profili girdisidir. Kademeler 0'dan başlar, boşluksuz ve artan sıradadır, son kademe açık uçludur.
/// </summary>
public sealed class TecvizKademeTablosu
{
    private TecvizKademeTablosu(IReadOnlyList<TecvizKademesi> kademeler) => Kademeler = kademeler;

    /// <summary>Doğrulanmış kademeler.</summary>
    public IReadOnlyList<TecvizKademesi> Kademeler { get; }

    /// <summary>Tabloyu doğrular; hata durumunda <see cref="ErrorCodes.TecvizGirdisiGecersiz"/>.</summary>
    public static Result<TecvizKademeTablosu> Olustur(IReadOnlyList<TecvizKademesi> kademeler)
    {
        ArgumentNullException.ThrowIfNull(kademeler);
        var k = kademeler.ToArray();

        if (k.Length == 0)
        {
            return Gecersiz("tier table is empty");
        }

        var beklenenAlt = 0m;
        for (var i = 0; i < k.Length; i++)
        {
            var kademe = k[i];
            if (kademe is null)
            {
                return Gecersiz($"null tier #{i + 1}");
            }

            if (kademe.AltSinirM2 != beklenenAlt)
            {
                return Gecersiz($"tier #{i + 1} does not start where the previous ended");
            }

            if (kademe.MarjinalOran < 0m)
            {
                return Gecersiz($"negative rate at tier #{i + 1}");
            }

            var son = i == k.Length - 1;
            if (kademe.UstSinirM2 is null)
            {
                if (!son)
                {
                    return Gecersiz($"open-ended tier #{i + 1} must be last");
                }
            }
            else
            {
                if (son)
                {
                    return Gecersiz("last tier must be open-ended");
                }

                if (kademe.UstSinirM2.Value <= kademe.AltSinirM2)
                {
                    return Gecersiz($"empty or reversed range at tier #{i + 1}");
                }

                beklenenAlt = kademe.UstSinirM2.Value;
            }
        }

        return Result<TecvizKademeTablosu>.Success(new TecvizKademeTablosu(Array.AsReadOnly(k)));
    }

    private static Result<TecvizKademeTablosu> Gecersiz(string neden) =>
        HataSablonu.Basarisiz<TecvizKademeTablosu>(ErrorCodes.TecvizGirdisiGecersiz, ("reason", neden));
}

/// <summary>
/// Arşiv onaylı koordinat YARDIMCI hesaplayıcısı (TPYS Md. 8(2), spec 4.3.2; VARSAYILAN DEĞİL — mühendis açıkça seçmedikçe
/// uygulanmaz, asıl kontrol <see cref="TecvizGirdisi.TecvizSiniriM2"/> üzerinden yürür):
/// ΔA_max(A) = Σ r_k · max(0, min(A, u_k) − l_k), sonuç 2 ondalık AwayFromZero.
/// </summary>
public static class ArsivOnayliTecvizYardimcisi
{
    public static Result<decimal> Hesapla(decimal alanM2, TecvizKademeTablosu tablo)
    {
        ArgumentNullException.ThrowIfNull(tablo);

        if (alanM2 < 0m)
        {
            return HataSablonu.Basarisiz<decimal>(ErrorCodes.TecvizGirdisiGecersiz, ("reason", "negative area"));
        }

        var toplam = 0m;
        foreach (var k in tablo.Kademeler)
        {
            var ust = k.UstSinirM2 is { } u ? Math.Min(alanM2, u) : alanM2;
            var pay = ust - k.AltSinirM2;
            if (pay > 0m)
            {
                toplam += k.MarjinalOran * pay;
            }
        }

        return Result<decimal>.Success(Math.Round(toplam, 2, MidpointRounding.AwayFromZero));
    }
}
