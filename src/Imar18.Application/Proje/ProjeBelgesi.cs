using Imar18.Application.Girdi;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Revizyon;

namespace Imar18.Application.Proje;

/// <summary>
/// Pilot proje durumu (P5.1): hesap girdileri, geometri, CRS ve revizyona bağlı mühendis onayı. Değişmez kayıttır; kalıcı
/// durum yalnızca <see cref="ProjeKomutYolu"/> (CommandBus) üzerinden değişir. <see cref="Surum"/> her başarılı komutta artan
/// sayaçtır; içerik kimliği <see cref="RevizyonKimligi"/>'dir (sayaç ve onay kimliğe girmez).
/// </summary>
public sealed record ProjeBelgesi(
    string ProjeAdi,
    ProjeCrs? Crs,
    IReadOnlyList<ParselGeometrisi> Parseller,
    DopGirdisi? Dop,
    IReadOnlyList<TecvizParselGirdisi> TecvizParselleri,
    decimal? DuzenlemeToplamTecvizSiniriM2,
    HakEslestirmeGirdisi? HakEslestirme,
    IReadOnlyList<ParselKimlikCifti> KimlikCiftleri,
    MuhendisOnayi? Onay,
    long Surum)
{
    /// <summary>
    /// P7.3-B1: yerel Malik Rehberi (anonim kod ↔ ad/baba adı, parsel Cilt/Sayfa). Hesabı değiştirmez; bu yüzden
    /// <see cref="RevizyonKimligi"/>'ne girmez ve değişmesi onayı düşürmez (SORU-040 c). Yalnız çıktı cetvelleri kullanır.
    /// </summary>
    public MalikRehberi MalikRehberi { get; init; } = MalikRehberi.Bos;

    /// <summary>P7.3-C2: ortak noktalı dönüşüm girdisi (deprem öncesi ↔ sonrası). RevisionId'ye dahildir.</summary>
    public DonusumGirdisi? Donusum { get; init; }

    /// <summary>P7.3-C2: proje künyesi (İl, İlçe, Mahalle, Belediye, Pafta, Kadastro ID, Datum). RevisionId'ye dahildir.</summary>
    public ProjeKunyesi? Kunye { get; init; }

    /// <summary>Boş proje.</summary>
    public static ProjeBelgesi Yeni(string projeAdi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projeAdi);
        return new ProjeBelgesi(projeAdi.Trim(), null, [], null, [], null, null, [], null, 0);
    }

    /// <summary>Geometri ve CRS dahil içerik revizyonu (AC-PILOT-06).</summary>
    public RevisionId RevizyonKimligi() => ProjeRevizyonu.Hesapla(new ProjeRevizyonGirdisi(
        ProjeAdi, Crs, Parseller, Dop, TecvizParselleri, DuzenlemeToplamTecvizSiniriM2, HakEslestirme, KimlikCiftleri)
    {
        Donusum = Donusum,
        Kunye = Kunye,
    });

    /// <summary>Onay var ve güncel içerik revizyonuna bağlıysa true.</summary>
    public bool GecerliOnayVar(RevisionId guncelRevizyon) => Onay?.GecerliMi(guncelRevizyon) == true;
}

/// <summary>Denetim kaydı sonucu.</summary>
public enum AuditSonucu
{
    /// <summary>İşlem uygulandı.</summary>
    Basarili,

    /// <summary>İşlem doğrulamada reddedildi; durum değişmedi.</summary>
    Reddedildi,

    /// <summary>Geçerli mühendis onayı olmadan çıktı denendi (spec 11.2 madde 7).</summary>
    OnaysizGirisim,

    /// <summary>Veri değiştiği için mevcut onay otomatik düştü (AC-PILOT-06).</summary>
    OnayDustu,
}

/// <summary>
/// Pilot denetim kaydı (temel audit; hash zinciri/Ed25519 Faz 12'dedir). <see cref="Ayrinti"/> yalnızca sayım, kod ve
/// checksum taşır; malik kimliği, ad, TC Kimlik No veya koordinat yazılmaz (KVKK, AC-PILOT-08).
/// </summary>
public sealed record AuditKaydi(
    long Sira,
    DateTimeOffset Zaman,
    string KullaniciId,
    string Islem,
    AuditSonucu Sonuc,
    string? HataKodu,
    string? OncekiRevizyon,
    string? SonrakiRevizyon,
    string? Ayrinti);

/// <summary>Tek atomik birim: proje durumu + tüm denetim kayıtları (aynı transaction — arch.md).</summary>
public sealed record ProjeKaydi(ProjeBelgesi Belge, IReadOnlyList<AuditKaydi> Denetim);

/// <summary>
/// Yerel proje deposu sözleşmesi (FR-F6.10). <see cref="KaydetAsync"/> durumu ve denetimi birlikte, atomik yazmalıdır:
/// ya tamamı ya hiçbiri kalıcı olur. Bozuk/okunamayan kayıtta istisna yerine <c>ERR-CIKTI-009</c> döner.
/// </summary>
public interface IProjeDeposu
{
    Task<bool> VarMiAsync(CancellationToken cancellationToken = default);

    Task<Result<ProjeKaydi>> YukleAsync(CancellationToken cancellationToken = default);

    Task<Result<ProjeKaydi>> KaydetAsync(ProjeKaydi kayit, CancellationToken cancellationToken = default);
}
