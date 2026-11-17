using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.Pilot.Tests.Destek;
using Xunit;

namespace Imar18.Pilot.Tests;

/// <summary>
/// P6.2 — Pilot 2 karma mülkiyet ve DOP (AC-PILOT-02). Sentetik proje: P2/1 (6000 m², MLK-01 ½ + KAMU-01 ½), P2/2 (4000 m²,
/// MLK-02); dört 1500 m² imar parseli + 4000 m² yol. Elle beklenen: D1 = 500, D2 = 200, R3 = 300 ile
/// DOPO = (10000 − 500 − 6000 + 200 + 300) / (10000 − 500) = 4000 / 9500 = 8/19 = 0.4210526 (7 hane); %45 sınırı (&gt;) aşılırsa
/// Critical onay engeli; tam %45 sınırda engel yoktur. Proje-C (pilot-2 altın verisi): kamuya ayrılan = Y1 + park + R2 farkı.
/// </summary>
public sealed class Pilot2KarmaMulkiyetDopTests
{
    private static PilotProjesi Karma(DopGirdisi? dop = null, bool mlk01Muvafakat = true) => new()
    {
        Ad = "Pilot 2 karma mülkiyet (sentetik)",
        Kadastro =
        [
            new PilotKadastro("P2/1", 6000.00m, 6000.00m,
                [new KadastroMaliki("MLK-01", new Fraction(1, 2), mlk01Muvafakat), new KadastroMaliki("KAMU-01", new Fraction(1, 2), true)]),
            new PilotKadastro("P2/2", 4000.00m, 4000.00m, [PilotProjesi.Tam("MLK-02")]),
        ],
        Olusan =
        [
            new PilotOlusan("P2/A1", ParselRolu.Imar, 1500.00m), new PilotOlusan("P2/A2", ParselRolu.Imar, 1500.00m),
            new PilotOlusan("P2/A3", ParselRolu.Imar, 1500.00m), new PilotOlusan("P2/A4", ParselRolu.Imar, 1500.00m),
            new PilotOlusan("P2/Y1", ParselRolu.YolTerk, 4000.00m),
        ],
        TecvizSiniriM2 = 10.00m,
        Dop = dop ?? DopGirdisi.Olustur(10000.00m, 6000.00m, d1: 500.00m, d2: 200.00m, r3: 300.00m).Value,
    };

    [Fact]
    public async Task Pilot2_D1_D2_R3_Sifirdan_Farkli_DOPO_Elle_Hesapla_Ayni_Olmali_Ve_Onaylanabilmeli()
    {
        using var alan = await PilotCalismaAlani.AcAsync(Karma());

        var d = alan.Onay.Degerlendir();
        var onay = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);

        d.Dop!.DopoTam.Should().Be(new Fraction(8, 19));
        d.Dop.Dopo.Should().Be(0.4210526m);
        d.Dop.DopMiktariM2.Should().Be(4000.00m);
        d.Dop.KamuyaAyrilanAlanM2.Should().Be(4000.00m);
        d.Dop.TavanAsildi.Should().BeFalse();
        onay.IsSuccess.Should().BeTrue(onay.IsFailure ? onay.Error.TechnicalMessage : string.Empty);
    }

    [Fact]
    public async Task Pilot2_D1_D2_R3_Sifirdan_Farkliyken_CKS_Uretilmemeli_Diger_Ciktilar_Uretilmeli_SORU_034_e()
    {
        using var alan = await PilotCalismaAlani.AcAsync(Karma());
        (await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        var cks = await alan.Cikti.UretAsync(CiktiTuru.CksDopOzeti, PilotCalismaAlani.Muhendis, alan.CiktiYolu("dop.cks"));
        var pdf = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoru, PilotCalismaAlani.Muhendis, alan.CiktiYolu("fen.pdf"));

        cks.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
        File.Exists(alan.CiktiYolu("dop.cks")).Should().BeFalse();
        alan.Yol.Denetim.Should().Contain(k => k.Islem == "Cikti:CksDopOzeti" && k.Sonuc == AuditSonucu.Reddedildi);
        pdf.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Pilot2_Yuzde45_Asiminda_Kritik_Hata_Onayi_Engellemeli_Ve_Denetlenmeli()
    {
        // R1 = 10000, R2 = 5000 → DOPO = 0.5 > 0.45; aşım = 5000 − 0.45 × 10000 = 500.00 m².
        using var alan = await PilotCalismaAlani.AcAsync(Karma(DopGirdisi.Olustur(10000.00m, 5000.00m).Value));

        var d = alan.Onay.Degerlendir();
        var onay = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);

        d.Dop!.Dopo.Should().Be(0.5000000m);
        d.Dop.TavanAsildi.Should().BeTrue();
        d.Dop.AsimM2.Should().Be(500.00m);
        d.Bulgular.Should().Contain(b => b.Code == ErrorCodes.DopYuzde45Asildi && b.Severity == ErrorSeverity.Critical);
        onay.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
        alan.Yol.Denetim[^1].Should().Match<AuditKaydi>(k => k.Sonuc == AuditSonucu.Reddedildi && k.HataKodu == ErrorCodes.CiktiOnayKritikHataEngeli);
        alan.Onay.OnayEkraniHazirla().Engeller.Should().Contain(ErrorCatalog.Get(ErrorCodes.DopYuzde45Asildi).UserMessage);
    }

    [Fact]
    public async Task Pilot2_Tam_Yuzde45_Sinirda_Engel_Olmamali()
    {
        // R2 = 5500 → DOPO = 4500 / 10000 = 0.45 (sınır dahil, aşım yok). R2 ≠ dengeleme imar toplamı (6000) yalnız uyarıdır.
        using var alan = await PilotCalismaAlani.AcAsync(Karma(DopGirdisi.Olustur(10000.00m, 5500.00m).Value));

        var d = alan.Onay.Degerlendir();

        d.Dop!.Dopo.Should().Be(0.45m);
        d.Dop.TavanAsildi.Should().BeFalse();
        d.Bulgular.Should().Contain(b => b.Code == ErrorCodes.DopR2DengelemeFarki && b.Severity == ErrorSeverity.Warning);
        d.Bulgular.Should().NotContain(b => b.Code == ErrorCodes.DopYuzde45Asildi);
    }

    [Fact]
    public async Task Pilot2_Muvafakatsiz_Paydasin_Mustakil_Tahsisi_Engellenmeli_Muvafakatla_Kalkmali_AAD_17_1_d()
    {
        using var alan = await PilotCalismaAlani.AcAsync(Karma(mlk01Muvafakat: false));

        var engel = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);
        alan.Onay.Degerlendir().Bulgular.Should().Contain(b => b.Code == ErrorCodes.TapuMuvafakatsizMustakilTahsis && b.Severity == ErrorSeverity.Critical);
        engel.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);

        (await alan.Yol.GonderAsync(new HakEslestirmeAyarlaKomutu(Karma(mlk01Muvafakat: true).Hak()), PilotCalismaAlani.Muhendis)).IsSuccess.Should().BeTrue();
        (await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Pilot2_ProjeC_Kamuya_Ayrilan_Alan_Y1_Park_Ve_R2_Farkinin_Toplami_Olmali()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeC());

        var d = alan.Onay.Degerlendir();

        var park = d.Dengeleme!.DigerKamuAlanlari.Sum(k => k.DengelenmisAlanM2);
        d.Dop!.KamuyaAyrilanAlanM2.Should().Be(d.Dengeleme.YolTerk.DengelenmisAlanM2 + park + d.Dop.R2DengelemeFarkiM2);
        d.Dop.KamuyaAyrilanAlanM2.Should().Be(PilotProjesi.Dec(SahaProjeleri.ProjeCJson().GetProperty("resmiDopSayfasi"), "kamuyaAyrilanAlan"));
    }
}
