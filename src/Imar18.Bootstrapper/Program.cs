using Avalonia;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Topoloji;
using Imar18.Infrastructure.Cad.Acad;
using Imar18.Infrastructure.Cad.Ncz;
using Imar18.Infrastructure.Cikti;
using Imar18.Infrastructure.Girdi;
using Imar18.Infrastructure.Proje;
using Imar18.Presentation.Avalonia;
using Imar18.Presentation.Avalonia.ViewModels;

namespace Imar18.Bootstrapper;

/// <summary>
/// Kompozisyon koku (P4.3): Infrastructure adaptorleri Application <see cref="CadImportService"/>'e baglanir ve
/// Presentation'a fabrika olarak verilir (Presentation Infrastructure'i bilmez — NetArchTest). Pilot Windows 10/11
/// hedeflidir (AC-PILOT-10); NU1903 nedeniyle Avalonia.Desktop yerine Win32 + Skia platformu kullanilir
/// (proje sahibi karari, 17 Kasım 2026).
/// </summary>
public static class Program
{
    /// <summary>Parseller arası çakışma/boşluk analizi (NTS overlay, GeometryEngine — P6).</summary>
    private static readonly IParselTopolojiDenetcisi Topoloji = new NtsParselTopolojiDenetcisi();

    [STAThread]
    public static void Main(string[] args)
    {
        var proje = ProjeAc();
        App.MainViewModelFactory = () => new MainWindowViewModel(
            new CadImportService([new NczFormatAdapter(), new DxfFormatAdapter(), new DwgFormatAdapter()]),
            proje,
            new CadImportService([new NczFormatAdapter(), new DxfFormatAdapter(), new DwgFormatAdapter()])); // P7.3-B2 Yaklaşık Yer Krokisi
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// P5: yerel pilot proje kaydi (%LOCALAPPDATA%\Imar18Studio\pilot-proje.imar18-pilot.json, atomik + SHA-256) ve onay/cikti
    /// servisleri. Kayit bozuksa uzerine yazilmaz; uygulama projesiz (P4 davranisi) acilir. Muhendis kimligi pilotta Windows
    /// kullanici adidir (kimlik dogrulama Faz 11+).
    /// </summary>
    private static ProjeCalismaAlani? ProjeAc()
    {
        var klasor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Imar18Studio");
        var depo = new AtomikJsonProjeDeposu(Path.Combine(klasor, "pilot-proje.imar18-pilot.json"));
        var acilis = ProjeKomutYolu.AcAsync(depo, "Pilot Proje", TimeProvider.System).GetAwaiter().GetResult();
        if (acilis.IsFailure)
        {
            return null;
        }

        var yol = acilis.Value;
        var kullanici = string.IsNullOrWhiteSpace(Environment.UserName) ? "muhendis" : Environment.UserName;
        return new ProjeCalismaAlani(
            yol,
            new ApprovalService(yol, TimeProvider.System, Topoloji),
            new CiktiServisi(yol, [new ClosedXmlCetvelUretici(), new QuestPdfFenKlasoruUretici(), new CksDosyaUretici(), new Imar18.Infrastructure.Cikti.FenKlasoru.FenKlasoruZipUretici()], new AtomikCiktiYazici(), Topoloji, new Imar18.GeometryEngine.Crs.HelmertAfinDonusumHesaplayici()),
            kullanici.Length > 64 ? kullanici[..64] : kullanici)
        {
            // P7.3-B (SAHA-006): webtapu PDF okuyucusu (PdfPig) ve Yaklaşık Yer Krokisi kesişim alanı (NTS, GeometryEngine).
            TapuOkuyucu = new WebtapuPdfOkuyucu(),
            Kesisim = new NtsParselKesisimHesaplayici(),
            Donusum = new Imar18.Application.KoordinatDonusumu.DonusumServisi(new Imar18.GeometryEngine.Crs.HelmertAfinDonusumHesaplayici()),
            // SAHA-014: Netcad .DNS okuyucusu (Deprem Öncesi → Deprem Sonrası ileri dönüşüm).
            DnsOkuyucu = new Imar18.Infrastructure.Cad.Donusum.NetcadDnsOkuyucu(),
        };
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseWin32()
        .UseSkia()
        .LogToTrace();
}
