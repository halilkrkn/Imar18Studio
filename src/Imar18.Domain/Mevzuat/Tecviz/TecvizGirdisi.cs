using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Mevzuat.Tecviz;

/// <summary>Koordinat kalitesi (spec 4.3.1).</summary>
public enum KoordinatKalitesi
{
    ArsivOnayli,
    Gecici,
}

/// <summary>
/// Mühendis tarafından girilen tecviz (yüzölçümü yanılma sınırı) kaydı (spec 4.3.1, D1, RG 27.09.2022/31966).
/// Sınır formülle hesaplanmaz; TKGM / Netcad dengeleme raporundan mühendis girer.
/// Private yapıcı: geçersiz örnek kurulamaz, tek giriş <see cref="Olustur"/> (P1 BULGU-2 dersi).
/// </summary>
public sealed record TecvizGirdisi
{
    private TecvizGirdisi(int olcekPaydaM, string uretimYontemi, KoordinatKalitesi koordinatKalite, decimal tecvizSiniriM2, string kaynakNotu)
    {
        OlcekPaydaM = olcekPaydaM;
        UretimYontemi = uretimYontemi;
        KoordinatKalite = koordinatKalite;
        TecvizSiniriM2 = tecvizSiniriM2;
        KaynakNotu = kaynakNotu;
    }

    /// <summary>Pafta ölçek paydası M (1/M; projeden/parselden girilir, D3).</summary>
    public int OlcekPaydaM { get; }

    /// <summary>Parselin orijinal üretim yöntemi (serbest metin).</summary>
    public string UretimYontemi { get; }

    /// <summary>Arşiv onaylı / geçici koordinat.</summary>
    public KoordinatKalitesi KoordinatKalite { get; }

    /// <summary>Mühendis tarafından girilen tecviz sınırı (m², &gt; 0).</summary>
    public decimal TecvizSiniriM2 { get; }

    /// <summary>Sınırın kaynağı (örn. "TKGM Fen Kayıt / Netcad Dengeleme Raporu").</summary>
    public string KaynakNotu { get; }

    /// <summary>Girdiyi doğrular; hata durumunda <see cref="ErrorCodes.TecvizGirdisiGecersiz"/>.</summary>
    public static Result<TecvizGirdisi> Olustur(
        int olcekPaydaM,
        string uretimYontemi,
        KoordinatKalitesi koordinatKalite,
        decimal tecvizSiniriM2,
        string kaynakNotu)
    {
        if (olcekPaydaM <= 0)
        {
            return Gecersiz("scale denominator must be positive");
        }

        if (string.IsNullOrWhiteSpace(uretimYontemi))
        {
            return Gecersiz("production method is empty");
        }

        if (!Enum.IsDefined(koordinatKalite))
        {
            return Gecersiz("undefined coordinate quality");
        }

        if (tecvizSiniriM2 <= 0m)
        {
            return Gecersiz("tecviz limit must be positive");
        }

        if (string.IsNullOrWhiteSpace(kaynakNotu))
        {
            return Gecersiz("source note is empty");
        }

        return Result<TecvizGirdisi>.Success(
            new TecvizGirdisi(olcekPaydaM, uretimYontemi.Trim(), koordinatKalite, tecvizSiniriM2, kaynakNotu.Trim()));
    }

    private static Result<TecvizGirdisi> Gecersiz(string neden) =>
        HataSablonu.Basarisiz<TecvizGirdisi>(ErrorCodes.TecvizGirdisiGecersiz, ("reason", neden));
}
