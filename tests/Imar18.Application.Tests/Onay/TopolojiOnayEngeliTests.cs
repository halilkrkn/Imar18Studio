using FluentAssertions;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Application.Tests.Onay;

/// <summary>
/// P6 (SORU-034 k): parseller arası çakışma / boşluk / kadastro dışına taşma Critical bulgudur ve mühendis onayını engeller
/// (AC-PILOT-05); analiz yapılamazsa hesap eksik sayılır. Geometriler OrnekProje'nin (K1 20×10) sentetik varyantlarıdır.
/// </summary>
public sealed class TopolojiOnayEngeliTests
{
    private static async Task<(ProjeKomutYolu Yol, ApprovalService Onay)> GeometriIleAsync(params ParselGeometrisi[] geometri)
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(geometri), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        return (yol, new ApprovalService(yol, zaman, OrnekProje.Topoloji));
    }

    private static ParselGeometrisi D(string id, ParselRolu rol, decimal y1, decimal y2) => OrnekProje.Dikdortgen(id, rol, y1, y2);

    [Theory]
    [InlineData(12.5, 12.0, ErrorCodes.TopoParselCakismasi, "A1, Y1", "5.00")]   // A1 [0,12.5] ∩ Y1 [12,20] = 0.5 × 10
    [InlineData(12.0, 13.0, ErrorCodes.TopoParselBoslugu, "A1, K1, Y1", "10.00")] // [12,13] × 10 karşılanmıyor
    public async Task Topoloji_Olusan_Parsellerde_Cakisma_Veya_Bosluk_Varsa_Onay_Kritik_Hatayla_Engellenmeli(
        double a1Son, double y1Bas, string beklenenKod, string kimlikler, string alan)
    {
        var (yol, servis) = await GeometriIleAsync(
            D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, (decimal)a1Son), D("Y1", ParselRolu.YolTerk, (decimal)y1Bas, 20m));

        var d = servis.Degerlendir();
        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        d.Bulgular.Should().ContainSingle(b => b.Code == beklenenKod).Which.Should().Match<AppError>(b =>
            b.Severity == ErrorSeverity.Critical && b.TechnicalMessage.Contains(kimlikler) && b.TechnicalMessage.Contains(alan + " m2"));
        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
        yol.Belge.Onay.Should().BeNull();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
        servis.OnayEkraniHazirla().Engeller.Should().Contain(ErrorCatalog.Get(beklenenKod).UserMessage);
    }

    [Fact]
    public async Task Topoloji_Kadastro_Disina_Tasan_Yol_Onayi_Engellemeli()
    {
        var (_, servis) = await GeometriIleAsync(
            D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, 12m), D("Y1", ParselRolu.YolTerk, 12m, 21m));

        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        servis.Degerlendir().Bulgular.Should().Contain(b => b.Code == ErrorCodes.TopoKadastroDisinaTasma && b.TechnicalMessage.Contains("10.00 m2"));
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
    }

    [Fact]
    public async Task Topoloji_Tam_Kaplamada_Bulgu_Olmamali_Ve_Onay_Verilmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var servis = new ApprovalService(yol, zaman, OrnekProje.Topoloji);

        servis.Degerlendir().Bulgular.Should().NotContain(b => b.Category == ErrorCategory.Topology);
        (await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Topoloji_Analizi_Yapilamazsa_Hesap_Eksik_Sayilmali_Ve_Onay_Engellenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var servis = new ApprovalService(yol, zaman, new SabitTopoloji([new ParselTopolojiBulgusu(TopolojiBulguTuru.AnalizYapilamadi, [], 0m)]));

        var d = servis.Degerlendir();
        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        d.EksikGirdiler.Should().Contain("topoloji analizi");
        d.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TopoAnalizYapilamadi);
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayIcinHesapEksik);
    }

    [Fact]
    public async Task Topoloji_Kendini_Kesen_Parsel_Analize_Verilmemeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var kayit = new SabitTopoloji([]);
        var papyon = new ParselGeometrisi("A9", ParselRolu.Imar,
            [new GeodeticPoint(OrnekProje.X0, OrnekProje.Y0), new GeodeticPoint(OrnekProje.X0 + 10m, OrnekProje.Y0 + 10m),
             new GeodeticPoint(OrnekProje.X0 + 10m, OrnekProje.Y0), new GeodeticPoint(OrnekProje.X0, OrnekProje.Y0 + 4m)]); // alanı sıfırdan farklı, kendini kesen
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu([.. OrnekProje.Geometri(), papyon]), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        new ApprovalService(yol, zaman, kayit).Degerlendir();

        kayit.SonGirdi.Select(p => p.ParselId).Should().BeEquivalentTo(["K1", "A1", "Y1"]);
    }

    [Fact]
    public async Task Topoloji_Denetcisi_Beklenmeyen_Istisna_Atarsa_Onay_Reddedilip_Denetlenmeli_Cokmemeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var servis = new ApprovalService(yol, zaman, new FirlatanTopoloji());

        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayIcinHesapEksik);
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
        servis.Degerlendir().EksikGirdiler.Should().Contain("topoloji analizi");
    }

    private sealed class FirlatanTopoloji : IParselTopolojiDenetcisi
    {
        public IReadOnlyList<ParselTopolojiBulgusu> Denetle(IReadOnlyList<ParselGeometrisi> parseller, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("benzetilmiş NTS hatası");
    }

    /// <summary>Sabit sonuç döndüren ve son girdiyi kaydeden test çifti.</summary>
    private sealed class SabitTopoloji(IReadOnlyList<ParselTopolojiBulgusu> sonuc) : IParselTopolojiDenetcisi
    {
        public IReadOnlyList<ParselGeometrisi> SonGirdi { get; private set; } = [];

        public IReadOnlyList<ParselTopolojiBulgusu> Denetle(IReadOnlyList<ParselGeometrisi> parseller, CancellationToken cancellationToken = default)
        {
            SonGirdi = parseller;
            return sonuc;
        }
    }
}
