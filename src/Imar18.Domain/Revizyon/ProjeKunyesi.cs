namespace Imar18.Domain.Revizyon;

/// <summary>
/// P7.3-C2: proje künyesi (Fen Klasörü başlıkları: İli, İlçesi, Köy/Mahalle, Belediye/İdare, Pafta No, Kadastro ID,
/// Datum/Projeksiyon). Resmî çıktılara basıldığı için RevisionId'ye dahildir (değişirse onay düşer). Kişisel veri içermez.
/// </summary>
public sealed record ProjeKunyesi(
    string Il,
    string Ilce,
    string KoyMahalle,
    string Belediye,
    string PaftaNolari,
    string KadastroId,
    string DatumProjeksiyon)
{
    /// <summary>Alan başına en çok karakter (uygulama sınırı).</summary>
    public const int MaxUzunluk = 120;

    public static ProjeKunyesi Bos { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>Tüm alanlar (ad, değer) — revizyon özeti ve doğrulama için sabit sırada.</summary>
    public IEnumerable<(string Ad, string Deger)> Alanlar()
    {
        yield return ("il", Il);
        yield return ("ilce", Ilce);
        yield return ("mahalle", KoyMahalle);
        yield return ("belediye", Belediye);
        yield return ("pafta", PaftaNolari);
        yield return ("kadastroId", KadastroId);
        yield return ("datum", DatumProjeksiyon);
    }
}
