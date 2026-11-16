namespace Imar18.Domain.Diagnostics;

/// <summary>Katalogdaki hata kodu sabitleri (spec 5.2 önekleri). Mevzuat değeri içermez.</summary>
public static class ErrorCodes
{
    public const string GenelBeklenmeyen = "ERR-GENEL-001";

    public const string GirdCadOkunamadi = "ERR-GIRD-001";
    public const string GirdCadSurumUyumsuz = "ERR-GIRD-002";
    public const string GirdPdfBozuk = "ERR-GIRD-003";
    public const string GirdNczOkunamadi = "ERR-GIRD-NCZ-001";

    public const string CrsEksik = "ERR-CRS-001";
    public const string CrsEpokUyumsuz = "ERR-CRS-002";

    public const string TopoKendiUzerindeKesisim = "ERR-TOPO-001";
    public const string TopoAcikPoligon = "ERR-TOPO-002";

    public const string TapuMalikEslesmedi = "ERR-TAPU-001";
    public const string TapuHisseEksik = "ERR-TAPU-002";
    public const string TapuHisseToplamiBirDegil = "ERR-TAPU-003";
    public const string TapuDagitimGirdisiGecersiz = "ERR-TAPU-004";

    public const string TecvizSiniriAsildi = "ERR-TECVIZ-001";
    public const string TecvizParselToplamSonucuFarkli = "ERR-TECVIZ-002";
    public const string TecvizGirdisiEksik = "ERR-TECVIZ-003";

    public const string DopYuzde45Asildi = "ERR-DOP-001";
    public const string DopMahsupEksik = "ERR-DOP-002";

    public const string PlanCepheDerinlik = "ERR-PLAN-001";
    public const string PlanAsgariAlan = "ERR-PLAN-002";

    public const string KuantAlanSapmasi = "ERR-KUANT-001";
    public const string KuantKoseCakismasi = "ERR-KUANT-002";

    public const string CiktiParsidZorunluAlanEksik = "ERR-CIKTI-001";
    public const string CiktiFenKlasoruSablon = "ERR-CIKTI-002";
    public const string CiktiCadDisaAktarim = "ERR-CIKTI-003";

    public const string GuvenAuditZinciriBozuk = "ERR-GUVEN-001";
    public const string GuvenYetkisizErisim = "ERR-GUVEN-002";
}

/// <summary>
/// Merkezi, sürümlü hata kataloğu (spec 5, AC-ERR-01..03). Hata mesajları UI'de string olarak tutulmaz.
/// Bu sürüm Pilot çekirdek kodlarını içerir; kodlar eklendikçe <see cref="Version"/> artırılır.
/// Hiçbir kayıt mevzuat sayısal değeri uydurmaz (yalnızca spec'te geçen %45 DOPO üst sınırı anılır).
/// </summary>
public static class ErrorCatalog
{
    /// <summary>Katalog sürümü (AC-ERR-01).</summary>
    public const string Version = "1.0.0";

    private static readonly AppError[] Entries =
    [
        // ---- Genel (spec 5.2'de önek yok; bilinmeyen hata için varsayım — STATUS SORU-018) ----
        New(ErrorCodes.GenelBeklenmeyen, ErrorSeverity.Error, ErrorCategory.General,
            "Beklenmeyen bir hata oluştu.",
            "Unmapped or unknown error code: {code}.",
            "İşlemi yeniden deneyin; sorun sürerse kaydı destek için saklayın.",
            retryable: true, review: false),

        // ---- ERR-GIRD: Girdi / dosya ----
        New(ErrorCodes.GirdCadOkunamadi, ErrorSeverity.Error, ErrorCategory.Input,
            "CAD dosyası okunamadı.",
            "CAD file {path} could not be parsed by the adapter.",
            "Dosyanın bozuk olmadığını kontrol edin ya da DXF/DWG/NCZ biçiminde yeniden dışa aktarın.",
            retryable: true, review: false),
        New(ErrorCodes.GirdCadSurumUyumsuz, ErrorSeverity.Error, ErrorCategory.Input,
            "CAD dosyasının sürümü desteklenmiyor.",
            "Unsupported CAD file version {version} for file {path}.",
            "Dosyayı desteklenen bir sürüme veya DXF biçimine dönüştürüp yeniden yükleyin.",
            retryable: false, review: false),
        New(ErrorCodes.GirdPdfBozuk, ErrorSeverity.Error, ErrorCategory.Input,
            "PDF belgesi okunamadı.",
            "PDF document {path} is corrupt or unreadable.",
            "PDF dosyasını yeniden alın veya verileri elle girin.",
            retryable: true, review: false),
        New(ErrorCodes.GirdNczOkunamadi, ErrorSeverity.Error, ErrorCategory.Input,
            "Netcad NCZ dosyası okunamadı veya sürümü uyumsuz.",
            "NCZ block parse failure or unsupported Netcad version in {path}.",
            "Dosyayı Netcad'den DXF/DWG olarak dışa aktarıp yedek girdi olarak yükleyin.",
            retryable: false, review: false),

        // ---- ERR-CRS: Koordinat sistemi ----
        New(ErrorCodes.CrsEksik, ErrorSeverity.Critical, ErrorCategory.Crs,
            "Koordinat sistemi (CRS/dilim) tanımlı değil.",
            "Project CRS is missing for {projectId}.",
            "Proje ayarlarından CRS ve dilimi girin.",
            retryable: false, review: true),
        New(ErrorCodes.CrsEpokUyumsuz, ErrorSeverity.Warning, ErrorCategory.Crs,
            "Koordinat epoku uyumsuz görünüyor.",
            "Epoch mismatch between {sourceEpoch} and {targetEpoch}.",
            "Kaynak ve hedef epokları kontrol edin.",
            retryable: false, review: true),

        // ---- ERR-TOPO: Topoloji (spec 5.3 örneği birebir) ----
        New(ErrorCodes.TopoKendiUzerindeKesisim, ErrorSeverity.Critical, ErrorCategory.Topology,
            "Parsel sınırı kendi üzerinde kesişiyor.",
            "Self-intersection detected in polygon {parcelId}.",
            "Topoloji Onar ekranını kullanın veya CAD (DXF/DWG/NCZ) köşe sırasını kontrol edin.",
            retryable: false, review: true),
        New(ErrorCodes.TopoAcikPoligon, ErrorSeverity.Critical, ErrorCategory.Topology,
            "Parsel sınırı kapalı değil.",
            "Polygon {parcelId} ring is not closed.",
            "Parsel sınırının ilk ve son köşesini birleştirin.",
            retryable: false, review: true),

        // ---- ERR-TAPU: Tapu / kroki / hisse ----
        New(ErrorCodes.TapuMalikEslesmedi, ErrorSeverity.Critical, ErrorCategory.Tapu,
            "Malik eşleşmesi yapılamadı.",
            "Owner {ownerId} could not be matched to a cadastral record.",
            "Tapu verisini ve malik eşleştirmesini kontrol edin.",
            retryable: false, review: true),
        New(ErrorCodes.TapuHisseEksik, ErrorSeverity.Critical, ErrorCategory.Tapu,
            "Hisse bilgisi eksik.",
            "Share information is missing for {ownerId}.",
            "Tapu verisini kontrol edin.",
            retryable: false, review: true),
        New(ErrorCodes.TapuHisseToplamiBirDegil, ErrorSeverity.Critical, ErrorCategory.Tapu,
            "Hisselerin toplamı bütüne (1) eşit değil.",
            "Allocation weights sum to {sum}, expected exactly 1.",
            "Hisse paylarını kontrol edip toplamın tam olarak bire eşit olmasını sağlayın.",
            retryable: false, review: true),
        New(ErrorCodes.TapuDagitimGirdisiGecersiz, ErrorSeverity.Error, ErrorCategory.Tapu,
            "Hisse dağıtım girdisi geçersiz.",
            "Invalid allocation input: {reason}.",
            "Malik kimliklerinin benzersiz ve boş olmadığını, hisselerin negatif olmadığını kontrol edin.",
            retryable: false, review: false),

        // ---- ERR-TECVIZ: Tecviz (seviyeler için STATUS SORU-019) ----
        New(ErrorCodes.TecvizSiniriAsildi, ErrorSeverity.Critical, ErrorCategory.Tecviz,
            "Tapu alanı ile hesaplanan alan farkı yanılma sınırını aşıyor.",
            "|Gauss - Tapu| = {delta} exceeds engineer-entered limit {limit}.",
            "Koordinatları ve tapu alanını kontrol edin; tecviz girdisini mühendis olarak doğrulayın.",
            retryable: false, review: true),
        New(ErrorCodes.TecvizParselToplamSonucuFarkli, ErrorSeverity.Warning, ErrorCategory.Tecviz,
            "Parsel bazlı ve toplam bazlı tecviz kontrolleri farklı sonuç veriyor.",
            "Per-parcel and total tecviz checks disagree for regulation {regulationId}.",
            "Hangi kontrolün bağlayıcı olduğunu mühendis olarak teyit edin.",
            retryable: false, review: true),
        New(ErrorCodes.TecvizGirdisiEksik, ErrorSeverity.Error, ErrorCategory.Tecviz,
            "Tecviz sınırı girilmemiş.",
            "TecvizGirdisi missing for parcel {parcelId}.",
            "Tecviz sınırını mühendis girdisi olarak giriniz.",
            retryable: false, review: true),

        // ---- ERR-DOP: DOP / mahsup ----
        New(ErrorCodes.DopYuzde45Asildi, ErrorSeverity.Critical, ErrorCategory.Dop,
            "Düzenleme Ortaklık Payı oranı %45 yasal üst sınırını aşıyor.",
            "DOPO {dopo} exceeds 0.45 upper bound.",
            "Aşan kısım için mevzuattaki karşılama sırasını uygulayın ve mühendis incelemesi isteyin.",
            retryable: false, review: true),
        New(ErrorCodes.DopMahsupEksik, ErrorSeverity.Error, ErrorCategory.Dop,
            "DOP mahsup bilgisi eksik.",
            "Offset (mahsup) data missing for parcel {parcelId}.",
            "Mahsup girdilerini tamamlayın.",
            retryable: false, review: true),

        // ---- ERR-PLAN: Plan kuralı ----
        New(ErrorCodes.PlanCepheDerinlik, ErrorSeverity.Error, ErrorCategory.Plan,
            "Parsel cephe veya derinlik koşulunu sağlamıyor.",
            "Frontage/depth rule violated for parcel {parcelId}.",
            "Parsel kesim çizgisini veya plan koşullarını gözden geçirin.",
            retryable: false, review: true),
        New(ErrorCodes.PlanAsgariAlan, ErrorSeverity.Error, ErrorCategory.Plan,
            "Parsel asgari alan koşulunu sağlamıyor.",
            "Minimum area rule violated for parcel {parcelId}.",
            "Parsel alanını veya plan koşullarını gözden geçirin.",
            retryable: false, review: true),

        // ---- ERR-KUANT: Kuantizasyon ----
        New(ErrorCodes.KuantAlanSapmasi, ErrorSeverity.Critical, ErrorCategory.Quantization,
            "Koordinat yuvarlama sonrası alan sapması izin verilen sınırı aşıyor; çözüm geri alındı.",
            "Post-quantization area deviation {delta} exceeds tolerance {tolerance}; solution rolled back.",
            "Parsel kesimini yeniden çalıştırın veya mühendis müdahalesi isteyin.",
            retryable: true, review: true),
        New(ErrorCodes.KuantKoseCakismasi, ErrorSeverity.Critical, ErrorCategory.Quantization,
            "Koordinat yuvarlama sonrası köşeler çakıştı veya topoloji bozuldu; çözüm geri alındı.",
            "Vertex collision or invalid topology after quantization in {parcelId}; solution rolled back.",
            "Parsel kesimini yeniden çalıştırın veya mühendis müdahalesi isteyin.",
            retryable: true, review: true),

        // ---- ERR-CIKTI: Çıktı ----
        New(ErrorCodes.CiktiParsidZorunluAlanEksik, ErrorSeverity.Critical, ErrorCategory.Output,
            "Zorunlu alan eksik.",
            "Required export field {field} is missing; export blocked.",
            "Eksik alanı kontrol edip tamamlayın.",
            retryable: false, review: true),
        New(ErrorCodes.CiktiFenKlasoruSablon, ErrorSeverity.Error, ErrorCategory.Output,
            "Fen Klasörü şablonu oluşturulamadı.",
            "Fen Klasörü template {templateId} failed to render.",
            "Şablon sürümünü ve girdi verisini kontrol edin.",
            retryable: true, review: false),
        New(ErrorCodes.CiktiCadDisaAktarim, ErrorSeverity.Error, ErrorCategory.Output,
            "CAD dosyası dışa aktarılamadı.",
            "CAD export to {format} failed.",
            "Hedef biçimi ve çıktı yolunu kontrol edip yeniden deneyin.",
            retryable: true, review: false),

        // ---- ERR-GUVEN: Güvenlik ----
        New(ErrorCodes.GuvenAuditZinciriBozuk, ErrorSeverity.Critical, ErrorCategory.Security,
            "Denetim kaydı bütünlüğü doğrulanamadı.",
            "Audit chain verification failed at record {recordId}.",
            "Projeyi salt okunur açıp yöneticiye başvurun.",
            retryable: false, review: true),
        New(ErrorCodes.GuvenYetkisizErisim, ErrorSeverity.Critical, ErrorCategory.Security,
            "Bu işlem için yetkiniz yok.",
            "Unauthorized access attempt to {resource}.",
            "Yetkili bir kullanıcıyla yeniden deneyin.",
            retryable: false, review: true),
    ];

    private static readonly Dictionary<string, AppError> ByCode =
        Entries.ToDictionary(e => e.Code, StringComparer.Ordinal);

    /// <summary>Tüm katalog kayıtları.</summary>
    public static IReadOnlyList<AppError> All { get; } = Array.AsReadOnly(Entries);

    /// <summary>Koda göre kaydı getirir; bilinmeyen veya boş kod için genel hata (<see cref="ErrorCodes.GenelBeklenmeyen"/>) döner.</summary>
    public static AppError Get(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var error) ? error : ByCode[ErrorCodes.GenelBeklenmeyen];

    /// <summary>Kod katalogda tanımlıysa true.</summary>
    public static bool TryGet(string? code, out AppError error)
    {
        if (code is not null && ByCode.TryGetValue(code, out var bulunan))
        {
            error = bulunan;
            return true;
        }

        error = ByCode[ErrorCodes.GenelBeklenmeyen];
        return false;
    }

    private static AppError New(
        string code,
        ErrorSeverity severity,
        ErrorCategory category,
        string userMessage,
        string technicalMessage,
        string recommendedAction,
        bool retryable,
        bool review) =>
        new(code, severity, category, userMessage, technicalMessage, recommendedAction, retryable, review);
}
