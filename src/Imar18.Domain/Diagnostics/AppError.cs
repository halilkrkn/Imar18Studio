namespace Imar18.Domain.Diagnostics;

/// <summary>Hata onem seviyesi (spec 5.3). Critical: mühendis onayını engeller (AC-PILOT-05).</summary>
public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical,
}

/// <summary>Hata kategorisi (spec 5.2 tablosu; General yalnızca bilinmeyen hata için).</summary>
public enum ErrorCategory
{
    General,
    Input,
    Crs,
    Topology,
    Tapu,
    Tecviz,
    Dop,
    Plan,
    Quantization,
    Output,
    Security,
}

/// <summary>
/// Standart hata kaydı (spec 5.4). Kullanıcı mesajı teknik mesajdan ayrıdır (AC-ERR-03);
/// <c>AuditLogRequired</c> spec §5.3 JSON ile uyumludur; katalog varsayılanı Critical => true (STATUS SORU-021).
/// TechnicalMessage içindeki {yerTutucu} alanları çağıran tarafından <c>with</c> ile doldurulur.
/// </summary>
public sealed record AppError(
    string Code,
    ErrorSeverity Severity,
    ErrorCategory Category,
    string UserMessage,
    string TechnicalMessage,
    string RecommendedAction,
    bool Retryable,
    bool RequiresEngineerReview,
    bool AuditLogRequired);
