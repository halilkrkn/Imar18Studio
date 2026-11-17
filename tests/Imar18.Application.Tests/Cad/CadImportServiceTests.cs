using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Application.Tests.Cad;

/// <summary>
/// P4.3 / reviewer L6 + P4.1 L11: CAD ice aktarimi arka planda, <see cref="IProgress{T}"/> ve
/// <see cref="CancellationToken"/> ile calisir; iptal edilemeyen okuyucu (ACadSharp) iptalde beklenmez.
/// </summary>
public sealed class CadImportServiceTests
{
    private static readonly CadLayerDocument BosBelge = new(CadFormat.Dxf, [], [], [], 0, 0, 0, []);

    private sealed class SahteAdaptor(CadFormat format, Func<Stream, CancellationToken, Result<CadLayerDocument>> oku) : ICadFormatAdapter
    {
        public int CagriSayisi { get; private set; }

        public int? CagriIsParcacigi { get; private set; }

        public CadFormat Format => format;

        public bool SupportsExtension(string extension) =>
            string.Equals(extension.TrimStart('.'), format.ToString(), StringComparison.OrdinalIgnoreCase);

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default)
        {
            CagriSayisi++;
            CagriIsParcacigi = Environment.CurrentManagedThreadId;
            return oku(source, cancellationToken);
        }
    }

    private sealed class KayitliIlerleme : IProgress<CadImportProgress>
    {
        public List<CadImportProgress> Kayitlar { get; } = [];

        public void Report(CadImportProgress value)
        {
            lock (Kayitlar)
            {
                Kayitlar.Add(value);
            }
        }
    }

    private static MemoryStream Akis(int bayt = 16) => new(new byte[bayt]);

    [Fact]
    public async Task IceAktarim_Uzantiya_Gore_Adaptoru_Secip_Arka_Planda_Okumali_Ve_Ilerleme_Bildirmeli()
    {
        var dxf = new SahteAdaptor(CadFormat.Dxf, (_, _) => Result<CadLayerDocument>.Success(BosBelge));
        var dwg = new SahteAdaptor(CadFormat.Dwg, (_, _) => throw new InvalidOperationException("secilmemeli"));
        var servis = new CadImportService([dwg, dxf]);
        var ilerleme = new KayitliIlerleme();

        var sonuc = await servis.ImportAsync(Akis(), ".DXF", ilerleme, CancellationToken.None);

        sonuc.IsSuccess.Should().BeTrue();
        dxf.CagriSayisi.Should().Be(1);
        dwg.CagriSayisi.Should().Be(0);
        dxf.CagriIsParcacigi.Should().NotBe(Environment.CurrentManagedThreadId, "50 ms ustu islem arka planda calisir");
        ilerleme.Kayitlar.Select(k => k.Stage).Should().ContainInOrder(CadImportStage.Reading, CadImportStage.Completed);
        ilerleme.Kayitlar[^1].Percent.Should().Be(100);
    }

    [Fact]
    public async Task IceAktarim_Desteklenmeyen_Uzantida_Okumadan_ERR_GIRD_001_Dondurmeli()
    {
        var servis = new CadImportService([new SahteAdaptor(CadFormat.Dxf, (_, _) => Result<CadLayerDocument>.Success(BosBelge))]);

        var sonuc = await servis.ImportAsync(Akis(), ".shp", null, CancellationToken.None);

        sonuc.Error.Code.Should().Be(ErrorCodes.GirdCadOkunamadi);
        sonuc.Error.TechnicalMessage.Should().Contain("unsupported extension");
    }

    [Fact]
    public async Task IceAktarim_Boyut_Sinirini_Asan_Akisi_Adaptore_Vermeden_Reddetmeli()
    {
        var adaptor = new SahteAdaptor(CadFormat.Dxf, (_, _) => Result<CadLayerDocument>.Success(BosBelge));
        var servis = new CadImportService([adaptor], maxFileSizeBytes: 8);

        var sonuc = await servis.ImportAsync(Akis(9), "dxf", null, CancellationToken.None);

        sonuc.Error.TechnicalMessage.Should().Contain("too large");
        adaptor.CagriSayisi.Should().Be(0);
    }

    [Fact]
    public async Task IceAktarim_Iptal_Edilemeyen_Okuyucuyu_Beklemeden_Iptali_Hemen_Bildirmeli()
    {
        using var serbest = new ManualResetEventSlim(false);
        using var basladi = new ManualResetEventSlim(false);
        var adaptor = new SahteAdaptor(CadFormat.Dwg, (_, _) =>
        {
            basladi.Set();
            serbest.Wait(TimeSpan.FromSeconds(10)); // belirteci dinlemeyen ucuncu parti okuyucu
            return Result<CadLayerDocument>.Success(BosBelge);
        });
        var servis = new CadImportService([adaptor]);
        using var cts = new CancellationTokenSource();

        var gorev = servis.ImportAsync(Akis(), "dwg", null, cts.Token);
        basladi.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var saat = System.Diagnostics.Stopwatch.StartNew();
        await cts.CancelAsync();

        var eylem = async () => await gorev;

        await eylem.Should().ThrowAsync<OperationCanceledException>();
        saat.ElapsedMilliseconds.Should().BeLessThan(2000);
        serbest.Set();
    }

    [Fact]
    public async Task IceAktarim_Iptal_Sonrasi_Onceki_Okuma_Bitene_Dek_Yeni_Aktarimi_Engellemeli()
    {
        using var serbest = new ManualResetEventSlim(false);
        using var basladi = new ManualResetEventSlim(false);
        var adaptor = new SahteAdaptor(CadFormat.Dwg, (_, _) =>
        {
            basladi.Set();
            serbest.Wait(TimeSpan.FromSeconds(10));
            return Result<CadLayerDocument>.Success(BosBelge);
        });
        var servis = new CadImportService([adaptor]);
        using var cts = new CancellationTokenSource();
        var ilk = servis.ImportAsync(Akis(), "dwg", null, cts.Token);
        basladi.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await cts.CancelAsync();
        await ((Func<Task>)(() => ilk)).Should().ThrowAsync<OperationCanceledException>();

        var ikinci = await servis.ImportAsync(Akis(), "dwg", null, CancellationToken.None);

        ikinci.Error.TechnicalMessage.Should().Contain("previous import");
        serbest.Set();
        await servis.WaitForBackgroundReadAsync();
        (await servis.ImportAsync(Akis(), "dwg", null, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task IceAktarim_Onceden_Iptal_Edilmis_Belirtecle_Adaptoru_Cagirmamali()
    {
        var adaptor = new SahteAdaptor(CadFormat.Dxf, (_, _) => Result<CadLayerDocument>.Success(BosBelge));
        var servis = new CadImportService([adaptor]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var eylem = async () => await servis.ImportAsync(Akis(), "dxf", null, cts.Token);

        await eylem.Should().ThrowAsync<OperationCanceledException>();
        adaptor.CagriSayisi.Should().Be(0);
    }

    [Fact]
    public async Task IceAktarim_Adaptor_Hatasini_Aynen_Iletip_Basarisiz_Asamayi_Bildirmeli()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.GirdNczOkunamadi);
        var servis = new CadImportService([new SahteAdaptor(CadFormat.Ncz, (_, _) => Result<CadLayerDocument>.Failure(hata))]);
        var ilerleme = new KayitliIlerleme();

        var sonuc = await servis.ImportAsync(Akis(), "ncz", ilerleme, CancellationToken.None);

        sonuc.Error.Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
        ilerleme.Kayitlar[^1].Stage.Should().Be(CadImportStage.Failed);
    }

    [Fact]
    public async Task IceAktarim_Adaptor_Istisnasini_Sizdirmadan_ERR_GIRD_001_Yapmali()
    {
        var servis = new CadImportService([new SahteAdaptor(CadFormat.Dxf, (_, _) => throw new InvalidDataException("ic ayrinti 4000000.12"))]);

        var sonuc = await servis.ImportAsync(Akis(), "dxf", null, CancellationToken.None);

        sonuc.Error.Code.Should().Be(ErrorCodes.GirdCadOkunamadi);
        sonuc.Error.TechnicalMessage.Should().NotContain("4000000");
    }

    [Fact]
    public async Task IceAktarim_Bos_Adaptor_Listesi_Ve_Null_Girdiler_Reddedilmeli()
    {
        ((Action)(() => _ = new CadImportService([]))).Should().Throw<ArgumentException>();
        var servis = new CadImportService([new SahteAdaptor(CadFormat.Dxf, (_, _) => Result<CadLayerDocument>.Success(BosBelge))]);
        await ((Func<Task>)(() => servis.ImportAsync(null!, "dxf", null, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void IceAktarim_Desteklenen_Bicimleri_Listelemeli()
    {
        var servis = new CadImportService([
            new SahteAdaptor(CadFormat.Ncz, (_, _) => Result<CadLayerDocument>.Success(BosBelge)),
            new SahteAdaptor(CadFormat.Dxf, (_, _) => Result<CadLayerDocument>.Success(BosBelge)),
        ]);

        servis.SupportedFormats.Should().Equal(CadFormat.Ncz, CadFormat.Dxf);
    }
}
