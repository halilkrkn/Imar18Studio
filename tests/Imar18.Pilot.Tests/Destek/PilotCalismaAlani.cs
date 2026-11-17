using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Topoloji;
using Imar18.Infrastructure.Cikti;
using Imar18.Infrastructure.Proje;

namespace Imar18.Pilot.Tests.Destek;

/// <summary>Elle ilerletilen zaman sağlayıcı.</summary>
internal sealed class PilotZaman(DateTimeOffset baslangic) : TimeProvider
{
    public DateTimeOffset Simdi { get; set; } = baslangic;

    public override DateTimeOffset GetUtcNow() => Simdi;
}

/// <summary>
/// Gerçek bileşenlerle uçtan uca çalışma alanı: geçici klasörde <see cref="AtomikJsonProjeDeposu"/>, CommandBus
/// (<see cref="ProjeKomutYolu"/>), <see cref="ApprovalService"/>, NTS topoloji denetçisi ve ClosedXML/QuestPDF/CKS üreticileriyle
/// <see cref="CiktiServisi"/> (Bootstrapper ile aynı bağlama). Dispose geçici klasörü siler.
/// </summary>
internal sealed class PilotCalismaAlani : IDisposable
{
    public const string Muhendis = "pilot-muhendis-01";
    public static readonly IParselTopolojiDenetcisi Topoloji = new NtsParselTopolojiDenetcisi();

    private PilotCalismaAlani(string klasor, PilotZaman zaman)
    {
        Klasor = klasor;
        Zaman = zaman;
        ProjeDosyasi = Path.Combine(klasor, "proje.imar18-pilot.json");
    }

    public string Klasor { get; }

    public string ProjeDosyasi { get; }

    public PilotZaman Zaman { get; }

    public ProjeKomutYolu Yol { get; private set; } = null!;

    public ApprovalService Onay { get; private set; } = null!;

    public CiktiServisi Cikti { get; private set; } = null!;

    public static async Task<PilotCalismaAlani> AcAsync(PilotProjesi proje)
    {
        var alan = await BosAsync(proje.Ad);
        foreach (var k in proje.Komutlar())
        {
            var r = await alan.Yol.GonderAsync(k, Muhendis);
            if (r.IsFailure)
            {
                alan.Dispose();
                throw new InvalidOperationException($"Pilot komutu reddedildi ({k.Ad}): {r.Error.Code} {r.Error.TechnicalMessage}");
            }
        }

        return alan;
    }

    public static async Task<PilotCalismaAlani> BosAsync(string projeAdi)
    {
        var klasor = Path.Combine(Path.GetTempPath(), "imar18-pilot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var alan = new PilotCalismaAlani(klasor, new PilotZaman(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero)));
        await alan.YenidenAcAsync(projeAdi);
        return alan;
    }

    /// <summary>Projeyi diskteki kayıttan yeniden açar (uygulama yeniden başlatma benzetimi).</summary>
    public async Task<Domain.Diagnostics.Result<ProjeKomutYolu>> YenidenAcAsync(string projeAdi = "Pilot")
    {
        var acilis = await ProjeKomutYolu.AcAsync(new AtomikJsonProjeDeposu(ProjeDosyasi), projeAdi, Zaman);
        if (acilis.IsSuccess)
        {
            Yol = acilis.Value;
            Onay = new ApprovalService(Yol, Zaman, Topoloji);
            Cikti = new CiktiServisi(Yol, [new ClosedXmlCetvelUretici(), new QuestPdfFenKlasoruUretici(), new CksDosyaUretici()], new AtomikCiktiYazici(), Topoloji);
        }

        return acilis;
    }

    public string CiktiYolu(string ad) => Path.Combine(Klasor, "cikti", ad);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Klasor, recursive: true);
        }
        catch (IOException)
        {
            // Geçici klasör; silinemezse test sonucu etkilenmez.
        }
    }
}
