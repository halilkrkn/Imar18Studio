using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Application.Tests.Onay;

/// <summary>
/// Antigravity Bağımsız Denetim ve Sınır Testleri (Faz P7.0b — SAHA-004 Application / Onay / Sıfırlama).
/// src/ üretim kodlarına dokunulmadan bağımsız sınır, negatif ve güvenlik kapısı doğrulamaları yapar:
/// 1. Toplam sınır toleransı tavanı tam 1.00 m² (onay verilebilir) ve 1.01 m² (Critical onay engeli) geçişi.
/// 2. İmar yolu R1 − R2 − çizili çapraz denetiminde tam sınır eşitliği (Info) ve +0.01 m² sapmada Critical yükseltmesi.
/// 3. 10 parselden uzun bulgularda teknik mesaj ve TopolojiNotu kısaltma ("(+N)") sözleşmesi.
/// 4. Katalog 1.8.0 (ERR-TOPO-009 / ERR-TOPO-010) seviye, kategori, inceleme ve AuditLogRequired sözleşmesi.
/// 5. ProjeyiSifirlaKomutu sonrası çıktı engeli (ERR-CIKTI-007 + OnaysizGirisim) ve depo hatasında tam rollback.
/// </summary>
public sealed class AntigravityIndependentSaha004AppAuditTests
{
    private sealed class SabitTopoloji(IReadOnlyList<ParselTopolojiBulgusu> sonuc) : IParselTopolojiDenetcisi
    {
        public IReadOnlyList<ParselTopolojiBulgusu> Denetle(IReadOnlyList<ParselGeometrisi> parseller, CancellationToken cancellationToken = default) => sonuc;
    }

    private sealed class SahteUretici(CiktiTuru tur) : ICiktiUretici
    {
        public CiktiTuru Tur => tur;

        public string Uzanti => ".pdf";

        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default) => Result<byte[]>.Success([0x25, 0x50, 0x44, 0x46]);
    }

    private sealed class BellekYazici : ICiktiYazici
    {
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SilAsync(string yol, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Topoloji_Toplam_Sinir_Toleransi_Tam_1_00m2_Onay_Verilebilir_1_01m2_Kritik_Engel_Olmali()
    {
        // Durum A: Toplam tam 1.00 m² (0.50 + 0.50) -> sınır içinde, Warning (ERR-TOPO-009), onay verilebilir.
        var (yolKabul, _, zamanKabul) = await OrnekProje.TamProjeAsync();
        var servisKabul = new ApprovalService(yolKabul, zamanKabul, new SabitTopoloji([
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["A1", "Y1"], 0.50m, SinirToleransiIcinde: true),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["A1", "K1"], 0.50m, SinirToleransiIcinde: true),
        ]));

        var dKabul = servisKabul.Degerlendir();
        dKabul.KritikVar.Should().BeFalse();
        dKabul.Bulgular.Where(b => b.Category == ErrorCategory.Topology).Should().HaveCount(2)
            .And.OnlyContain(b => b.Code == ErrorCodes.TopoSinirToleransi && b.Severity == ErrorSeverity.Warning);
        (await servisKabul.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        // Durum B: Toplam 1.01 m² (0.50 + 0.50 + 0.01) -> tavan aşıldı, hepsi Critical, onay ERR-CIKTI-004 ile engellenir.
        var (yolRet, _, zamanRet) = await OrnekProje.TamProjeAsync();
        var servisRet = new ApprovalService(yolRet, zamanRet, new SabitTopoloji([
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["A1", "Y1"], 0.50m, SinirToleransiIcinde: true),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["A1", "K1"], 0.50m, SinirToleransiIcinde: true),
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["K1", "Y1"], 0.01m, SinirToleransiIcinde: true),
        ]));

        var dRet = servisRet.Degerlendir();
        dRet.KritikVar.Should().BeTrue();
        dRet.KritikSayisi.Should().Be(3);
        dRet.Bulgular.Should().NotContain(b => b.Code == ErrorCodes.TopoSinirToleransi);
        var onayRet = await servisRet.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        onayRet.IsFailure.Should().BeTrue();
        onayRet.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
    }

    [Theory]
    [InlineData(80.05, false)]
    [InlineData(80.06, true)]
    public async Task Topoloji_Imar_Yolu_R1_Eksi_R2_Tolerans_Siniri_Tam_Esitlikte_Info_0_01_Asimda_Kritik_Olmali(
        double yolAlaniM2,
        bool kritikBeklenir)
    {
        // K1 = 200 m² (Gauss = 200), A1 = 120 m² (çizili yol/kamu yok), DOP R1 = 200 / R2 = 120 -> beklenen yol = 80.00 m².
        // İzinli fark: |R1 - kadastroGauss| + SinirToleransiAlanM2 + toleransToplami = 0 + 0.05 + 0 = 0.05 m².
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu([
            OrnekProje.Dikdortgen("K1", ParselRolu.Kadastro, 0m, 20m),
            OrnekProje.Dikdortgen("A1", ParselRolu.Imar, 0m, 12m),
        ]), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var servis = new ApprovalService(yol, zaman, new SabitTopoloji([
            new ParselTopolojiBulgusu(TopolojiBulguTuru.ImarYoluAlani, ["101/1", "102/1", "K1"], (decimal)yolAlaniM2),
        ]));

        var d = servis.Degerlendir();
        var ekran = servis.OnayEkraniHazirla();
        var bulgu = d.Bulgular.Should().ContainSingle(b => b.Category == ErrorCategory.Topology).Subject;

        if (kritikBeklenir)
        {
            bulgu.Code.Should().Be(ErrorCodes.TopoParselBoslugu);
            bulgu.Severity.Should().Be(ErrorSeverity.Critical);
            ekran.OnayVerilebilir.Should().BeFalse();
            d.TopolojiNotlari.Should().ContainSingle().Which.Metin.Should().Contain("ile uyuşmuyor").And.Contain("onay engeli");
        }
        else
        {
            bulgu.Code.Should().Be(ErrorCodes.TopoImarYoluAlani);
            bulgu.Severity.Should().Be(ErrorSeverity.Info);
            ekran.OnayVerilebilir.Should().BeTrue(string.Join(" | ", ekran.Engeller));
            d.TopolojiNotlari.Should().ContainSingle().Which.Seviye.Should().Be(ErrorSeverity.Info);
        }
    }

    [Fact]
    public async Task Topoloji_On_Parselden_Fazla_Kimlikli_Bulguda_Not_Ve_Teknik_Mesaj_Kisaltilmali()
    {
        var kimlikler = Enumerable.Range(1, 12).Select(i => $"101/{i:00}").ToArray();
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var servis = new ApprovalService(yol, zaman, new SabitTopoloji([
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanKapsamaBoslugu, kimlikler, 1.50m),
        ]));

        var d = servis.Degerlendir();
        var bulgu = d.Bulgular.Should().ContainSingle(b => b.Code == ErrorCodes.TopoParselBoslugu).Subject;
        var not = d.TopolojiNotlari.Should().ContainSingle().Subject;

        bulgu.TechnicalMessage.Should().Contain("101/01").And.Contain("101/10").And.Contain("(+2)").And.NotContain("101/11");
        not.Metin.Should().Contain("101/01").And.Contain("101/10").And.Contain("(+2)").And.NotContain("101/11");
    }

    [Fact]
    public void ErrorCatalog_Saha004_Katalog_Surumu_Ve_Yeni_Kod_Sozlesmesi_Dogrulanmali()
    {
        ErrorCatalog.Version.Should().Be("1.8.0");

        var sinir = ErrorCatalog.Get(ErrorCodes.TopoSinirToleransi);
        sinir.Code.Should().Be("ERR-TOPO-009");
        sinir.Severity.Should().Be(ErrorSeverity.Warning);
        sinir.Category.Should().Be(ErrorCategory.Topology);
        sinir.RequiresEngineerReview.Should().BeTrue();
        sinir.AuditLogRequired.Should().BeFalse();

        var yol = ErrorCatalog.Get(ErrorCodes.TopoImarYoluAlani);
        yol.Code.Should().Be("ERR-TOPO-010");
        yol.Severity.Should().Be(ErrorSeverity.Info);
        yol.Category.Should().Be(ErrorCategory.Topology);
        yol.RequiresEngineerReview.Should().BeTrue();
        yol.AuditLogRequired.Should().BeFalse();
    }

    [Fact]
    public async Task ProjeyiSifirlaKomutu_Onayli_Projeyi_Sifirladiktan_Sonra_Cikti_Uretimi_Engellenmeli_Ve_Denetlenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var onay = new ApprovalService(yol, zaman, OrnekProje.Topoloji);
        var cikti = new CiktiServisi(yol, [new SahteUretici(CiktiTuru.FenKlasoru)], new BellekYazici(), OrnekProje.Topoloji);

        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        onay.GecerliOnayVar.Should().BeTrue();

        (await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        onay.GecerliOnayVar.Should().BeFalse();

        var ciktiSonuc = await cikti.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen-sifirlanmis.pdf");
        ciktiSonuc.IsFailure.Should().BeTrue();
        ciktiSonuc.Error.Code.Should().Be(ErrorCodes.CiktiGecerliOnayYok);
        yol.Denetim.Should().Contain(k => k.Sonuc == AuditSonucu.OnayDustu);
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.OnaysizGirisim);
    }

    [Fact]
    public async Task ProjeyiSifirlaKomutu_Depo_Hatasi_Durumunda_Tam_Rollback_Yapmali()
    {
        var (yol, depo, zaman) = await OrnekProje.TamProjeAsync();
        var onay = new ApprovalService(yol, zaman, OrnekProje.Topoloji);
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        var oncekiRevizyon = yol.Belge.RevizyonKimligi();
        var oncekiDenetimSayisi = yol.Denetim.Count;
        var oncekiParselSayisi = yol.Belge.Parseller.Count;
        depo.SonrakiYazmayiBozdur = true;

        var sonuc = await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        yol.Belge.RevizyonKimligi().Should().Be(oncekiRevizyon);
        yol.Belge.Parseller.Should().HaveCount(oncekiParselSayisi);
        yol.Belge.Crs.Should().NotBeNull();
        yol.Belge.Dop.Should().NotBeNull();
        yol.Belge.Onay.Should().NotBeNull();
        yol.Denetim.Should().HaveCount(oncekiDenetimSayisi);
    }
}
