using System.Buffers.Binary;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7.3-C3: 1. adım CAD içe aktarımı PARSELASYON HARİTASI.NCZ şablonunu CommandBus ile yönetir — geçerli .NCZ şablonu
/// saklar (aynı baytlar yeniden komut üretmez), .DXF/.DWG şablonu temizler, başarısız içe aktarma şablona dokunmaz.
/// Baytlar sentetiktir (yalnız Netcad 8.5 başlık imzası).
/// </summary>
public sealed class P73NczSablonuIceAktarimTests
{
    private sealed class SahteAdaptor(CadFormat format, bool basarili = true) : ICadFormatAdapter
    {
        public CadFormat Format => format;

        public bool SupportsExtension(string extension) =>
            string.Equals(extension.Trim().TrimStart('.'), format.ToString(), StringComparison.OrdinalIgnoreCase);

        public Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) => basarili
            ? Result<CadLayerDocument>.Success(new CadLayerDocument(format, [], [], [], 0, 0, 0, []))
            : Result<CadLayerDocument>.Failure(ErrorCatalog.Get(ErrorCodes.GirdNczOkunamadi));
    }

    private sealed class BosYazici : ICiktiYazici
    {
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static byte[] Ncz(byte isaret)
    {
        var b = new byte[80];
        b[0] = 0x01;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), 120);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5), b.Length);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(9), 125);
        b[^1] = isaret;
        return b;
    }

    private static async Task<(MainWindowViewModel Vm, ProjeKomutYolu Yol)> KurAsync(params ICadFormatAdapter[] adaptorler)
    {
        var zaman = new SabitZaman(DateTimeOffset.UnixEpoch);
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "UI Projesi", zaman)).Value;
        var alan = new ProjeCalismaAlani(yol, new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [], new BosYazici(), OrnekProje.Topoloji), OrnekProje.Muhendis);
        return (new MainWindowViewModel(new CadImportService(adaptorler), alan), yol);
    }

    [Fact]
    public async Task IceAktarim_NCZ_Sablonu_Saklamali_Ayni_Baytlarda_Yeni_Komut_Uretmemeli_DXF_Temizlemeli()
    {
        var (vm, yol) = await KurAsync(new SahteAdaptor(CadFormat.Ncz), new SahteAdaptor(CadFormat.Dxf));
        var bayt = Ncz(1);

        await vm.ImportAsync(new MemoryStream(bayt), ".NCZ");
        await vm.LastProjeTask;

        yol.Belge.NczSablonu!.Icerik.ToArray().Should().Equal(bayt);
        var sablonKomutu = yol.Denetim.Count(d => d.Islem == "NczSablonuAyarla");
        sablonKomutu.Should().Be(1);

        await vm.ImportAsync(new MemoryStream(bayt), "ncz");
        await vm.LastProjeTask;
        yol.Denetim.Count(d => d.Islem == "NczSablonuAyarla").Should().Be(1, "aynı şablon yeniden komut üretmez");

        await vm.ImportAsync(new MemoryStream([1, 2, 3]), ".dxf");
        await vm.LastProjeTask;
        yol.Belge.NczSablonu.Should().BeNull("DXF/DWG kaynağında iskelet moduna dönülür");
        yol.Denetim.Last(d => d.Islem == "NczSablonuAyarla").Ayrinti.Should().Be("temizlendi");
    }

    [Fact]
    public async Task IceAktarim_Basarisiz_NCZ_Sablona_Dokunmamali()
    {
        var (vm, yol) = await KurAsync(new SahteAdaptor(CadFormat.Ncz));
        await vm.ImportAsync(new MemoryStream(Ncz(1)), ".ncz");
        await vm.LastProjeTask;
        var once = yol.Belge.NczSablonu;

        var (vm2, _) = (new MainWindowViewModel(new CadImportService([new SahteAdaptor(CadFormat.Ncz, basarili: false)]),
            new ProjeCalismaAlani(yol, new ApprovalService(yol, new SabitZaman(DateTimeOffset.UnixEpoch), OrnekProje.Topoloji),
                new CiktiServisi(yol, [], new BosYazici(), OrnekProje.Topoloji), OrnekProje.Muhendis)), 0);
        await vm2.ImportAsync(new MemoryStream(Ncz(2)), ".ncz");

        yol.Belge.NczSablonu.Should().Be(once);
        vm2.StatusText.Should().StartWith("İçe aktarılamadı");
    }

    [Fact]
    public async Task IceAktarim_Saklanamayan_NCZ_Eski_Sablonu_Temizlemeli()
    {
        var (vm, yol) = await KurAsync(new SahteAdaptor(CadFormat.Ncz));
        await vm.ImportAsync(new MemoryStream(Ncz(1)), ".ncz");
        await vm.LastProjeTask;

        await vm.ImportAsync(new MemoryStream([1]), ".ncz");
        await vm.LastProjeTask;

        yol.Belge.NczSablonu.Should().BeNull("eski şablonla farklı bir kaynağın haritası üretilmemeli");
        vm.Warnings.Should().NotContain(w => w.Contains("şablon olarak saklanamadı", StringComparison.Ordinal), "uyarı yalnız boyut sınırı aşımında");
    }
}
