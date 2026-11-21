using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Girdi;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// SAHA-021 (mühendis hatası "verdiğim arşiv onaylı koordinatlara kendisi yeni koordinat / sıra no atıyor"): 1. adım arşiv kartında
/// mühendisin henüz uygulanmamış nokta no ve arşiv Y, X düzenlemeleri, CAD yeniden yüklenip satırlar yeniden kurulduğunda (NCZ tip 1
/// noktası, köşe mm kayması) ezilmez; kayıtlı arşiv noktası her zaman korunur. 4. adım "Başlangıç Ada No" kutusu (varsayılan 101).
/// Sentetik proje: <see cref="OrnekProje"/> (K1 = 4 köşe; konum gerçek değil); arşiv numaraları uydurmadır.
/// </summary>
public sealed class Saha021ArsivVeAdaNoTests
{
    private sealed class BosYazici : ICiktiYazici
    {
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static GeodeticPoint P(decimal x, decimal y) => new(OrnekProje.X0 + x, OrnekProje.Y0 + y);

    private static async Task<ProjeCalismaAlani> AlanAsync()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-021", zaman)).Value;
        foreach (var k in OrnekProje.TamKomutlar())
        {
            (await yol.GonderAsync(k, OrnekProje.Muhendis)).IsSuccess.Should().BeTrue(k.Ad);
        }

        return new ProjeCalismaAlani(yol, new ApprovalService(yol, zaman, OrnekProje.Topoloji),
            new CiktiServisi(yol, [], new BosYazici(), OrnekProje.Topoloji), OrnekProje.Muhendis);
    }

    [Fact]
    public async Task Kaydedilmemis_Arsiv_Duzenlemesi_Cad_Yeniden_Yuklenince_Ezilmemeli_SAHA_021()
    {
        var alan = await AlanAsync();
        var vm = new ArsivNoktalariViewModel(alan);
        vm.Satirlar[0].NoktaNo = "901001";
        vm.Satirlar[0].ArsivY = "500000,07";
        vm.Satirlar[0].ArsivX = "4399999.95";

        // CAD yeniden yüklendi: köşe 1'de tip 1 noktası "2652001", köşe 2'de "2652002" var.
        vm.CadNoktalariniAyarla([new CadPoint("NOKTA", "2652001", P(0m, 0m)), new CadPoint("NOKTA", "2652002", P(0m, 20m))]);

        vm.Satirlar[0].NoktaNo.Should().Be("901001", "mühendisin yazdığı arşiv no CAD adıyla ezilmez");
        vm.Satirlar[0].ArsivY.Should().Be("500000,07", "yazılan metin aynen korunur");
        vm.Satirlar[0].ArsivX.Should().Be("4399999.95");
        vm.Satirlar[1].NoktaNo.Should().Be("2652002", "düzenlenmemiş köşe CAD varsayılanını alır");

        await vm.UygulaCommand.ExecuteAsync(null);

        var kayit = alan.Yol.Belge.ArsivKadastroNoktalari[0];
        (kayit.NoktaNo, kayit.ArsivY, kayit.ArsivX).Should().Be(("901001", 500000.07m, 4399999.95m));
    }

    [Fact]
    public async Task Kayitli_Arsiv_Noktasi_Cad_Adiyla_Ve_Sira_Numarasiyla_Ezilmemeli_Sifirlaninca_Duzenleme_Birakilmali_SAHA_021()
    {
        var alan = await AlanAsync();
        var vm = new ArsivNoktalariViewModel(alan);
        for (var i = 0; i < 4; i++)
        {
            vm.Satirlar[i].NoktaNo = "90100" + (i + 1);
        }

        vm.Satirlar[2].ArsivY = "500020.03";
        await vm.UygulaCommand.ExecuteAsync(null);

        vm.CadNoktalariniAyarla([new CadPoint("NOKTA", "2652003", P(10m, 20m))]);
        vm.Yenile(zorla: true);

        vm.Satirlar.Select(s => s.NoktaNo).Should().Equal("901001", "901002", "901003", "901004");
        vm.Satirlar[2].ArsivY.Should().Be("500020.03");

        vm.Satirlar[0].NoktaNo = "X1";
        vm.Sifirla();
        vm.Satirlar[0].NoktaNo.Should().Be("901001", "sıfırlamada kaydedilmemiş düzenleme bırakılır; kayıtlı arşiv no gelir");
    }

    [Theory]
    [InlineData("101", true, 101)]
    [InlineData("", true, 101)]
    [InlineData(" 201 ", true, 201)]
    [InlineData("301", true, 301)]
    [InlineData("0", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("10000000", false, 0)]
    public async Task Baslangic_Ada_No_Kutusu_Varsayilan_101_Gecersiz_Degeri_Reddetmeli_SAHA_021(string metin, bool gecerli, int beklenen)
    {
        var vm = new HesapAdimlariViewModel(await AlanAsync(), OrnekProje.Tecviz);
        vm.KrokiBaslangicAdaNo.Should().Be(KrokiParselasyonGirdisi.VarsayilanBaslangicAdaNo.ToString(System.Globalization.CultureInfo.InvariantCulture));

        vm.KrokiBaslangicAdaNo = metin;

        vm.KrokiBaslangicAdaNoHatali.Should().Be(!gecerli);
        vm.KrokiBaslangicAdaNoOku(out var ada).Should().Be(gecerli);
        if (gecerli)
        {
            ada.Should().Be(beklenen);
        }
    }
}
