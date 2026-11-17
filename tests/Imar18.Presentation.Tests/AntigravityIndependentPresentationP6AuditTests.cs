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
/// Antigravity Bağımsız Denetim Testleri (Faz P6 — Presentation, Sihirbaz ve Onay UI Akışı).
/// H1: Adım 2-4 panellerinin görünürlük ve DataContext izolasyonu.
/// M2: Arka plan onay (Task.Run) ve IsBusy yönetimi.
/// M6: Atomik hak eşleme (gönderim öncesi doğrulama).
/// </summary>
public sealed class AntigravityIndependentPresentationP6AuditTests
{
    private sealed class SahteUretici(CiktiTuru tur) : ICiktiUretici
    {
        public CiktiTuru Tur => tur;
        public string Uzanti => ".bin";
        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default) => Result<byte[]>.Success([1]);
    }

    private sealed class BellekYazici : ICiktiYazici
    {
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ProjeCalismaAlani Alan(ProjeKomutYolu yol, SabitZaman zaman) => new(
        yol,
        new ApprovalService(yol, zaman, OrnekProje.Topoloji),
        new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji),
        OrnekProje.Muhendis);

    [Fact]
    public void WizardViewModel_Adim_Gecislerinde_IsStep_Gorunurluk_Bayraklari_Tam_Izole_Olmali()
    {
        var vm = new WizardViewModel();

        // Başlangıç: Adım 1 (CRS / CAD Katmanları)
        vm.CurrentIndex.Should().Be(0);
        vm.IsStep1.Should().BeTrue();
        vm.IsStep2.Should().BeFalse();
        vm.IsStep3.Should().BeFalse();
        vm.IsStep4.Should().BeFalse();
        vm.IsStep5.Should().BeFalse();

        // İleri geçiş için girdi doğrulanmış olmalı
        vm.CurrentIndex = 1;
        vm.CurrentIndex.Should().Be(1);
        vm.IsStep1.Should().BeFalse();
        vm.IsStep2.Should().BeTrue();
        vm.IsStep3.Should().BeFalse();
        vm.IsStep4.Should().BeFalse();
        vm.IsStep5.Should().BeFalse();

        // İleri -> Adım 3 (DOP & Y1)
        vm.CurrentIndex = 2;
        vm.CurrentIndex.Should().Be(2);
        vm.IsStep3.Should().BeTrue();
        vm.IsStep2.Should().BeFalse();

        // İleri -> Adım 4 (Hak Eşleştirme)
        vm.CurrentIndex = 3;
        vm.CurrentIndex.Should().Be(3);
        vm.IsStep4.Should().BeTrue();
        vm.IsStep3.Should().BeFalse();

        // İleri -> Adım 5 (Onay & Çıktı)
        vm.CurrentIndex = 4;
        vm.CurrentIndex.Should().Be(4);
        vm.IsStep5.Should().BeTrue();
        vm.IsStep4.Should().BeFalse();

        // Geri dönüş
        vm.BackCommand.Execute(null);
        vm.CurrentIndex.Should().Be(3);
        vm.IsStep4.Should().BeTrue();
    }

    [Fact]
    public async Task HesapAdimlari_M6_Gecersiz_Kimlik_Eslemesinde_Hak_Komutu_Gonderilmemeli_Atomik()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik M6 Test Projesi", zaman)).Value;
        (await yol.GonderAsync(new ProjeCrsAyarlaKomutu(OrnekProje.Crs()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(OrnekProje.Geometri()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var alan = Alan(yol, zaman);
        var vm = new HesapAdimlariViewModel(alan, OrnekProje.Tecviz);
        await vm.YenileAsync();

        // 2. Tecviz uygula
        vm.TecvizSatirlari[0].TapuAlaniText = "200.00";
        await vm.TecvizUygulaCommand.ExecuteAsync(null);

        // 3. DOP uygula
        await vm.DopUygulaCommand.ExecuteAsync(null);

        var revizyonOnce = yol.Revizyon;

        // 4. Hak eşleştirme: TAKBİS ve Kroki geçerli, ancak geçici ↔ tescil eşlemesinde aynı geçici koda iki tescil no verilmiş (geçersiz eşleme)
        vm.TakbisMetni = "K1;Malik1;1/1;E";
        vm.KrokiMetni = "Malik1;K1;A1;120.00";
        vm.KimlikMetni = "A1;101/1\r\nA1;101/2"; // Çift tescil no -> ParselKimlikEslemesi reddeder

        await vm.HakUygulaCommand.ExecuteAsync(null);

        // Doğrulama: Gönderim öncesi yakalandığı için proje belgesindeki revizyon değişmemeli, hiçbir komut yazılmamalı
        vm.DurumMetni.Should().Be("Geçici ve kalıcı parsel kimlik eşlemesi geçersiz.");
        yol.Revizyon.Should().Be(revizyonOnce, "geçersiz geçici ↔ tescil eşleşmesinde HakEslestirmeAyarlaKomutu projeye yazılmamalıdır (atomik koruma)");
        yol.Belge.HakEslestirme.Should().BeNull();
    }

    [Fact]
    public async Task OnayCiktiViewModel_M2_Arka_Planda_Onay_Calismali_Ve_DurumMetni_Guncellenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var alan = Alan(yol, zaman);
        var vm = new OnayCiktiViewModel(alan);
        await vm.YenileAsync();

        vm.Ekran.Should().NotBeNull();
        vm.Ekran!.OnayVerilebilir.Should().BeTrue();
        vm.GecerliOnayVar.Should().BeFalse();

        // Onay metni onaylanmadan onaylanamaz
        vm.OnayMetniOkundu = true;
        await vm.OnaylaCommand.ExecuteAsync(null);

        vm.GecerliOnayVar.Should().BeTrue();
        vm.DurumMetni.Should().Contain("Revizyon onaylandı");

        // Geri alma testi
        await vm.GeriAlCommand.ExecuteAsync(null);
        vm.GecerliOnayVar.Should().BeFalse();
        vm.DurumMetni.Should().Contain("Onay geri alındı");
    }
}
