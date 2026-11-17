using System.Reflection;

namespace Imar18.TestDestek;

/// <summary>
/// Ortak test verisi bulucusu (STATUS BULGU-3, P6). Tüm test projelerine <c>tests/Directory.Build.props</c> ile bağlanır.
/// <list type="bullet">
/// <item>Sentetik altın veri: derleme çıktısındaki <c>test-data/synthetic/</c> kopyası (depo kökünde <c>.sln</c> aranmaz;
/// yalnız <c>bin</c> klasörü kopyalanan izole ortamda da çalışır). <c>IMAR18_SENTETIK_VERI_KOKU</c> ortam değişkeni ezer.</item>
/// <item>Yerel gerçek veri (<c>test-data/real/</c>, gitignore'lu, AC-PILOT-08): asla kopyalanmaz. Önce
/// <c>IMAR18_GERCEK_VERI_KOKU</c> ortam değişkeni, sonra derleme zamanında yazılan <c>Imar18.GercekVeriKoku</c> assembly
/// meta verisi denenir; klasör yoksa <see cref="GercekVeriKoku"/> null'dır ve RealData testleri atlanır.</item>
/// </list>
/// </summary>
internal static class TestDataLocator
{
    public const string SentetikOrtamDegiskeni = "IMAR18_SENTETIK_VERI_KOKU";
    public const string GercekOrtamDegiskeni = "IMAR18_GERCEK_VERI_KOKU";
    public const string GercekMetaVeriAnahtari = "Imar18.GercekVeriKoku";

    private static readonly Lazy<string?> SentetikKokLazy = new(() =>
        SentetikKokuCoz(AppContext.BaseDirectory, Environment.GetEnvironmentVariable));

    private static readonly Lazy<string?> GercekKokLazy = new(() =>
        GercekKokuCoz(Environment.GetEnvironmentVariable, MetaVeri(typeof(TestDataLocator).Assembly, GercekMetaVeriAnahtari)));

    /// <summary>Sentetik altın veri kökü (<c>.../test-data/synthetic</c>).</summary>
    /// <exception cref="InvalidOperationException">Kök bulunamazsa (derleme çıktısına kopyalanmamışsa).</exception>
    public static string SentetikKok => SentetikKokLazy.Value
        ?? throw new InvalidOperationException(
            "test-data/synthetic derleme çıktısında bulunamadı (tests/Directory.Build.props kopyalama kuralını kontrol edin).");

    /// <summary>Yerel gerçek veri kökü; yoksa null (CI veya başka makine).</summary>
    public static string? GercekVeriKoku => GercekKokLazy.Value;

    public static bool GercekVeriVarMi => GercekVeriKoku is not null;

    /// <summary>Sentetik veri dosyasının tam yolu; dosya yoksa istisna (sessiz yanlış yol yok).</summary>
    /// <exception cref="FileNotFoundException">Dosya yoksa.</exception>
    public static string Sentetik(params string[] goreliParcalar)
    {
        ArgumentNullException.ThrowIfNull(goreliParcalar);
        var yol = Path.Combine([SentetikKok, .. goreliParcalar]);
        return File.Exists(yol) ? yol : throw new FileNotFoundException("Sentetik test verisi bulunamadı.", yol);
    }

    /// <summary>Saf çözümleme (test edilebilir): ortam değişkeni, sonra taban dizindeki <c>test-data/synthetic</c>.</summary>
    internal static string? SentetikKokuCoz(string tabanDizin, Func<string, string?> ortam)
    {
        ArgumentNullException.ThrowIfNull(tabanDizin);
        ArgumentNullException.ThrowIfNull(ortam);
        var ezme = ortam(SentetikOrtamDegiskeni);
        if (!string.IsNullOrWhiteSpace(ezme))
        {
            return Directory.Exists(ezme) ? Path.GetFullPath(ezme) : null;
        }

        var aday = Path.Combine(tabanDizin, "test-data", "synthetic");
        return Directory.Exists(aday) ? Path.GetFullPath(aday) : null;
    }

    /// <summary>Saf çözümleme (test edilebilir): ortam değişkeni, sonra derleme zamanı meta verisi; klasör yoksa null.</summary>
    internal static string? GercekKokuCoz(Func<string, string?> ortam, string? metaVeriYolu)
    {
        ArgumentNullException.ThrowIfNull(ortam);
        var ezme = ortam(GercekOrtamDegiskeni);
        var aday = !string.IsNullOrWhiteSpace(ezme) ? ezme : metaVeriYolu;
        return !string.IsNullOrWhiteSpace(aday) && Directory.Exists(aday) ? Path.GetFullPath(aday) : null;
    }

    private static string? MetaVeri(Assembly assembly, string anahtar) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, anahtar, StringComparison.Ordinal))?.Value;
}
