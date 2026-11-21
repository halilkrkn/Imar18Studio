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
    /// P7.3-B1: yerel Malik Rehberi (anonim kod ↔ ad/baba adı, parsel Cilt/Sayfa). Hesabı değiştirmez ama Fen Klasörü
    /// belgelerine basılır: doluysa SHA-256 özeti <see cref="RevizyonKimligi"/>'ne girer ve değişmesi onayı düşürür (SORU-046,
    /// SORU-040 c'nin yerine). Ad/baba adı denetim kaydına yazılmaz.
    /// </summary>
    public MalikRehberi MalikRehberi { get; init; } = MalikRehberi.Bos;

    /// <summary>P7.3-C2: ortak noktalı dönüşüm girdisi (deprem öncesi ↔ sonrası). RevisionId'ye dahildir.</summary>
    public DonusumGirdisi? Donusum { get; init; }

    /// <summary>P7.3-C2: proje künyesi (İl, İlçe, Mahalle, Belediye, Pafta, Kadastro ID, Datum). RevisionId'ye dahildir.</summary>
    public ProjeKunyesi? Kunye { get; init; }

    /// <summary>
    /// P7.3-C3: 1. adımda yüklenen kaynak <c>.NCZ</c> şablonu (yoksa <c>PARSELASYON HARİTASI.NCZ</c> iskelet modunda üretilir).
    /// Yalnız varsa SHA-256 özeti RevisionId'ye girer (şablonsuz projelerin kimliği değişmez).
    /// </summary>
    public Cad.NczSablonu? NczSablonu { get; init; }

    /// <summary>
    /// SAHA-014: <c>.DNS</c> ileri dönüşüm katsayıları (Deprem Öncesi → Deprem Sonrası). Doluysa <see cref="Parseller"/> aktif
    /// (Deprem Sonrası) geometridir ve her zaman <c>Parseller = İleri(<see cref="DepremOncesiParseller"/>)</c> değişmezi geçerlidir
    /// (<see cref="IleriDonusumDegismezi"/>; CommandBus ve yükleme denetler). Yalnız doluysa RevisionId'ye girer.
    /// </summary>
    public DonusumKatsayilari? IleriDonusum { get; init; }

    /// <summary>
    /// SAHA-014: CAD'den gelen orijinal (Deprem Öncesi) parseller; yalnız <see cref="IleriDonusum"/> doluyken dolu. Tekrar
    /// <c>.DNS</c> uygulanınca dönüşüm her zaman bunlardan yapılır (çift dönüşüm yok); Fen Klasörü "Deprem Öncesi" koordinatları
    /// bunlardan basılır.
    /// </summary>
    public IReadOnlyList<ParselGeometrisi>? DepremOncesiParseller { get; init; }

    /// <summary>
    /// SAHA-020: mühendisin verdiği arşiv onaylı kadastro (TK_PARSEL) köşe noktaları (nokta no + arşiv Y, X). KOORDİNE ÖZET, RÖLÖVE ve
    /// ALAN HESABI PDF'lerine basılır; yalnız doluysa özeti RevisionId'ye girer (eski projelerin kimliği değişmez).
    /// </summary>
    public IReadOnlyList<ArsivNoktaKaydi> ArsivKadastroNoktalari { get; init; } = [];

    /// <summary>Dönüşüm kipi: yok, P7.3-C2 elle ortak nokta (ters) veya SAHA-014 <c>.DNS</c> ileri dönüşüm.</summary>
    public DonusumKipi DonusumKipi => IleriDonusum is not null
        ? DonusumKipi.IleriDns
        : Donusum is not null ? DonusumKipi.ElleTers : DonusumKipi.Yok;

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
        MalikRehberiOzeti = MalikRehberi.Ozet(),
        NczSablonuOzeti = NczSablonu?.Sha256,
        IleriDonusum = IleriDonusum,
        DepremOncesiGeometri = IleriDonusum is null ? null : DepremOncesiParseller,
        ArsivNoktalariOzeti = ArsivNoktalari.Ozet(ArsivKadastroNoktalari),
    });

    /// <summary>Onay var ve güncel içerik revizyonuna bağlıysa true.</summary>
    public bool GecerliOnayVar(RevisionId guncelRevizyon) => Onay?.GecerliMi(guncelRevizyon) == true;
}

/// <summary>SAHA-014: projedeki koordinat dönüşümü kipi.</summary>
public enum DonusumKipi
{
    /// <summary>Dönüşüm yok: koordinatlar CAD'den geldiği gibi kullanılır.</summary>
    Yok,

    /// <summary>P7.3-C2: elle ortak nokta; proje noktaları II. sistem sayılır, I. sistem çıktıda ters dönüşümle hesaplanır.</summary>
    ElleTers,

    /// <summary>SAHA-014: <c>.DNS</c> ile Deprem Öncesi → Deprem Sonrası ileri dönüşüm; aktif geometri Deprem Sonrası'dır.</summary>
    IleriDns,
}

/// <summary>
/// SAHA-014 (architect K2): ileri dönüşüm değişmezinin tek merkezi denetimi. <see cref="ProjeBelgesi.IleriDonusum"/> boşsa
/// <see cref="ProjeBelgesi.DepremOncesiParseller"/> da boş olmalı; doluysa ortak noktalı dönüşüm girdisi bulunmalı, katsayılar
/// <see cref="DonusumKatsayilari.Dogrula"/>'dan geçmeli ve aktif parseller orijinallerin (aynı sıra, kimlik, rol, köşe sayısı)
/// birebir ileri dönüşümü olmalıdır. <see cref="ProjeKomutYolu"/> her komuttan sonra ve proje yüklenirken çağırır.
/// </summary>
public static class IleriDonusumDegismezi
{
    public static Result<ProjeBelgesi> Dogrula(ProjeBelgesi belge)
    {
        ArgumentNullException.ThrowIfNull(belge);
        if (belge.IleriDonusum is not { } k)
        {
            return belge.DepremOncesiParseller is null
                ? Result<ProjeBelgesi>.Success(belge)
                : KomutHatasi.Gecersiz("IleriDonusumDegismezi", "earthquake-before parcels exist without forward coefficients");
        }

        var dogrulama = DonusumKatsayilari.Dogrula(k);
        if (dogrulama.IsFailure)
        {
            return Result<ProjeBelgesi>.Failure(dogrulama.Error);
        }

        if (belge.Donusum is null || belge.DepremOncesiParseller is not { } orj || orj.Count != belge.Parseller.Count)
        {
            return KomutHatasi.Gecersiz("IleriDonusumDegismezi", "forward transformation needs common points and one original per active parcel");
        }

        for (var i = 0; i < orj.Count; i++)
        {
            var o = orj[i];
            var a = belge.Parseller[i];
            if (o is null || a is null || o.ParselId != a.ParselId || o.Rol != a.Rol || o.Koseler is null || a.Koseler is null
                || o.Koseler.Count != a.Koseler.Count)
            {
                return KomutHatasi.Gecersiz("IleriDonusumDegismezi", $"active parcel #{KomutHatasi.Sayi(i + 1)} does not match its original");
            }

            for (var j = 0; j < o.Koseler.Count; j++)
            {
                if (k.Uygula(o.Koseler[j]) != a.Koseler[j])
                {
                    return KomutHatasi.Gecersiz("IleriDonusumDegismezi", $"active parcel #{KomutHatasi.Sayi(i + 1)} is not the forward transform of its original");
                }
            }
        }

        return Result<ProjeBelgesi>.Success(belge);
    }
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
