using FluentAssertions;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Application.Tests.Onay;

/// <summary>
/// SAHA-004 (SORU-037): sınır toleransı içindeki topoloji bulguları Warning (<c>ERR-TOPO-009</c>), çizilmemiş imar yolu Info
/// (<c>ERR-TOPO-010</c>) olur ve onayı engellemez; her bulgu Türkçe parsel / alan ayrıntısıyla (TopolojiNotu) raporlanır.
/// Denetçi sabit bulgu döndürür (Application NTS'ye bağımlı değildir); geometri OrnekProje'nin tam kaplamasıdır.
/// </summary>
public sealed class TopolojiSinirToleransiOnayTests
{
    private static async Task<(ProjeKomutYolu Yol, ApprovalService Onay)> BulgularlaAsync(params ParselTopolojiBulgusu[] bulgular)
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        return (yol, new ApprovalService(yol, zaman, new SabitTopoloji(bulgular)));
    }

    [Fact]
    public async Task Topoloji_Sinir_Toleransi_Bulgusu_Uyari_Olmali_Ve_Onay_Verilmeli()
    {
        var (yol, servis) = await BulgularlaAsync(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["A1", "Y1"], 0.01m, SinirToleransiIcinde: true),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["A1", "K1"], 0.02m, SinirToleransiIcinde: true));

        var d = servis.Degerlendir();
        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        d.KritikVar.Should().BeFalse();
        d.Bulgular.Where(b => b.Category == ErrorCategory.Topology).Should().HaveCount(2)
            .And.OnlyContain(b => b.Code == ErrorCodes.TopoSinirToleransi && b.Severity == ErrorSeverity.Warning);
        d.Bulgular.Should().Contain(b => b.TechnicalMessage == "overlap new parcels within boundary tolerance: parcels A1, Y1, 0.01 m2.");
        d.TopolojiNotlari.Select(n => n.Metin).Should().Equal(
            "Oluşan parseller çakışması: A1 ↔ Y1 — 0.01 m² (sınır toleransı içinde; onayı engellemez)",
            "Kadastro sınırı dışına taşma: A1, K1 — 0.02 m² (sınır toleransı içinde; onayı engellemez)");
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        yol.Belge.Onay.Should().NotBeNull();
    }

    [Fact]
    public async Task Topoloji_Imar_Yolu_Bilgisi_Onayi_Engellememeli_Ada_Kodlari_Yazilmali()
    {
        // Yol ayri cizilmemis proje: K1 200 m2, A1 120 m2, DOP R1 200 / R2 120 -> beklenen yol alani R1 - R2 = 80 m2.
        var (yol, servis) = await BulgularlaAsync(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.ImarYoluAlani, ["101/1", "102/1", "103/4", "K1"], 80.00m));
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(
            [OrnekProje.Dikdortgen("K1", ParselRolu.Kadastro, 0m, 20m), OrnekProje.Dikdortgen("A1", ParselRolu.Imar, 0m, 12m)]), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var d = servis.Degerlendir();
        var ekran = servis.OnayEkraniHazirla();

        d.Bulgular.Should().ContainSingle(b => b.Category == ErrorCategory.Topology).Which.Should().Match<AppError>(b =>
            b.Code == ErrorCodes.TopoImarYoluAlani && b.Severity == ErrorSeverity.Info
            && b.TechnicalMessage.Contains("80.00 m2") && b.TechnicalMessage.Contains("blocks 101, 102, 103"));
        ekran.OnayVerilebilir.Should().BeTrue(string.Join(" | ", ekran.Engeller));
        ekran.TopolojiNotlari.Should().ContainSingle().Which.Metin.Should().Be(
            "İmar yolu / kamu alanı (çizilmemiş): 101/1, 102/1, 103/4, K1 — 80.00 m² (adalar: 101, 102, 103; parsel boşluğu sayılmadı)");
    }

    [Fact]
    public async Task Topoloji_Imar_Yolu_Alani_Beklenen_Yol_Alanindan_Sapiyorsa_Kritik_Bosluk_Olmali()
    {
        // Reviewer H1: OrnekProje Y1 (80 m2) cizili -> beklenen cizilmemis yol = 200 - 120 - 80 = 0; 60 m2 yol eksik parsel olabilir.
        var (_, servis) = await BulgularlaAsync(new ParselTopolojiBulgusu(TopolojiBulguTuru.ImarYoluAlani, ["101/1", "102/1", "K1"], 60.00m));

        var d = servis.Degerlendir();
        var ekran = servis.OnayEkraniHazirla();

        d.Bulgular.Should().ContainSingle(b => b.Category == ErrorCategory.Topology).Which.Should().Match<AppError>(b =>
            b.Code == ErrorCodes.TopoParselBoslugu && b.Severity == ErrorSeverity.Critical && b.TechnicalMessage.Contains("0.00 m2"));
        ekran.OnayVerilebilir.Should().BeFalse();
        d.TopolojiNotlari.Should().ContainSingle().Which.Metin.Should().Contain("ile uyuşmuyor").And.Contain("onay engeli");
    }

    [Fact]
    public async Task Topoloji_Toplam_Sinir_Toleransi_Tavani_Asilinca_Onay_Engeli_Olmali()
    {
        // Reviewer H2: 3 x 0.40 = 1.20 m2 > 1.00 m2 proje tavani -> tolerans bulgulari Critical olur.
        var (_, servis) = await BulgularlaAsync(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["A1", "Y1"], 0.40m, SinirToleransiIcinde: true),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["A1", "K1"], 0.40m, SinirToleransiIcinde: true),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["K1", "Y1"], 0.40m, SinirToleransiIcinde: true));

        var d = servis.Degerlendir();

        d.KritikVar.Should().BeTrue();
        d.Bulgular.Should().NotContain(b => b.Code == ErrorCodes.TopoSinirToleransi);
        d.TopolojiNotlari.Should().HaveCount(3).And.OnlyContain(n => n.Metin.Contains("toplam sınır toleransı 1.00 m² aşıldı"));
    }

    [Fact]
    public async Task Topoloji_Tolerans_Disi_Engelde_Parsel_Ve_Alan_Ayrintisi_Engel_Listesinde_Olmali()
    {
        var (_, servis) = await BulgularlaAsync(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["101/2", "K1"], 0.20m),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["A1", "Y1"], 0.01m, SinirToleransiIcinde: true));

        var ekran = servis.OnayEkraniHazirla();

        ekran.OnayVerilebilir.Should().BeFalse();
        ekran.Engeller.Should().Contain(ErrorCatalog.Get(ErrorCodes.TopoKadastroDisinaTasma).UserMessage)
            .And.Contain("↳ Kadastro sınırı dışına taşma: 101/2, K1 — 0.20 m² (onay engeli)")
            .And.NotContain(e => e.Contains("A1 ↔ Y1"), "sınır toleransındaki bulgu engel listesine girmez");
        ekran.TopolojiNotlari.Should().HaveCount(2);
    }

    private sealed class SabitTopoloji(IReadOnlyList<ParselTopolojiBulgusu> sonuc) : IParselTopolojiDenetcisi
    {
        public IReadOnlyList<ParselTopolojiBulgusu> Denetle(IReadOnlyList<ParselGeometrisi> parseller, CancellationToken cancellationToken = default) => sonuc;
    }
}
