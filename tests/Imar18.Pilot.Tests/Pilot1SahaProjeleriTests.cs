using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Cks;
using Imar18.Pilot.Tests.Destek;
using Xunit;

namespace Imar18.Pilot.Tests;

/// <summary>
/// P6.1 — Pilot 1 saha projeleri (Proje-A/B/C) uçtan uca: CommandBus (atomik JSON kayıt) → değerlendirme (Gauss, NTS topoloji,
/// tecviz, Y1 dengeleme, DOP, hak eşleştirme) → mühendis onayı → gerçek CKS/Excel/PDF üretimi. Beklenen değerler
/// <c>test-data/synthetic/</c> altın verisinden okunur (AC-PILOT-01, SORU-011, SORU-014).
/// </summary>
public sealed class Pilot1SahaProjeleriTests
{
    public static TheoryData<string> Projeler => ["A", "B", "C"];

    private static PilotProjesi Proje(string kod) => kod switch
    {
        "A" => SahaProjeleri.ProjeA(),
        "B" => SahaProjeleri.ProjeB(),
        _ => SahaProjeleri.ProjeC(),
    };

    /// <summary>(R1, R2, DOPO, kamuya ayrılan, Y1 dengelenmiş, tecviz farkı) altın değerleri.</summary>
    private static (decimal R1, decimal R2, decimal Dopo, decimal Kamu, decimal Y1, decimal Fark) Altin(string kod)
    {
        if (kod == "C")
        {
            var j = SahaProjeleri.ProjeCJson();
            var dop = j.GetProperty("resmiDopSayfasi");
            return (PilotProjesi.Dec(dop, "R1"), PilotProjesi.Dec(dop, "R2_ToplamImarAdaAlani"), PilotProjesi.Dec(dop, "DOPO"),
                PilotProjesi.Dec(dop, "kamuyaAyrilanAlan"), PilotProjesi.Dec(j.GetProperty("dengelemeCetveli"), "Y1"),
                PilotProjesi.Dec(j.GetProperty("metrics"), "tecvizFarki"));
        }

        var m = (kod == "A" ? SahaProjeleri.ProjeAJson() : SahaProjeleri.ProjeBJson()).GetProperty("metrics");
        return (PilotProjesi.Dec(m, "toplamKadastroAlani_R1"), PilotProjesi.Dec(m, "toplamImarTahsis_R2"), PilotProjesi.Dec(m, "dopo"),
            PilotProjesi.Dec(m, "kamuyaAyrilanYol"), PilotProjesi.Dec(m, "kamuyaAyrilanYol"), PilotProjesi.Dec(m, "tecvizFarki"));
    }

    [Theory]
    [MemberData(nameof(Projeler))]
    public async Task Pilot1_Saha_Projesi_Hesaplari_Altin_Veriyle_Birebir_Esit_Olmali(string kod)
    {
        using var alan = await PilotCalismaAlani.AcAsync(Proje(kod));
        var a = Altin(kod);

        var d = alan.Onay.Degerlendir();

        d.EksikGirdiler.Should().BeEmpty();
        d.KritikVar.Should().BeFalse(string.Join("; ", d.Bulgular.Where(b => b.Severity == ErrorSeverity.Critical).Select(b => b.TechnicalMessage)));
        d.Bulgular.Should().NotContain(b => b.Category == ErrorCategory.Topology, "sentetik şerit geometrisi düzenleme alanını tam kaplar");
        d.Dop!.Girdi.R1.Should().Be(a.R1);
        d.Dop.Girdi.R2.Should().Be(a.R2);
        d.Dop.Dopo.Should().Be(a.Dopo);
        d.Dop.KamuyaAyrilanAlanM2.Should().Be(a.Kamu);
        d.Dengeleme!.FarkM2.Should().Be(a.Fark);
        d.Dengeleme.YolTerk.DengelenmisAlanM2.Should().Be(a.Y1);
        d.Dengeleme.ImarParselleri.Should().OnlyContain(p => p.DuzeltmeM2 == 0m, "fark yalnızca Y1'e eklenir (spec 25.4)");
        d.Dengeleme.DengelenmisToplamM2.Should().Be(a.R1);
        d.Parseller.Where(p => p.Geometri.Rol == ParselRolu.Kadastro).Sum(p => p.AlanM2).Should().Be(a.R1 - a.Fark, "Gauss hesap alanı");
    }

    [Fact]
    public async Task Pilot1_ProjeC_R2_Dengeleme_Farki_Uyari_Olarak_Raporlanmali_Dagitilmamali_SORU_014()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeC());
        var j = SahaProjeleri.ProjeCJson();

        var d = alan.Onay.Degerlendir();

        d.Dengeleme!.ImarParselToplamiM2.Should().Be(PilotProjesi.Dec(j.GetProperty("dengelemeCetveli"), "imarParselToplami"));
        d.Dop!.R2DengelemeFarkiM2.Should().Be(PilotProjesi.Dec(j.GetProperty("referansKarari"), "dengelemeParselToplamiFarki_m2"));
        d.Bulgular.Should().ContainSingle(b => b.Code == ErrorCodes.DopR2DengelemeFarki).Which.Severity.Should().Be(ErrorSeverity.Warning);
        d.Dengeleme.DigerKamuAlanlari.Should().ContainSingle().Which.DengelenmisAlanM2.Should().Be(PilotProjesi.Dec(j.GetProperty("metrics"), "parkAlani"));
    }

    [Fact]
    public async Task Pilot1_ProjeB_Musterek_Parsel_Paydalari_Largest_Remainder_Ile_Tam_Kapanmali_SORU_011()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeB());
        var lr = SahaProjeleri.ProjeBDagitim().GetProperty("largestRemainderPayToplamlari");

        var hak = alan.Onay.Degerlendir().Hak!;

        foreach (var parsel in new[] { "101/1", "105/2" })
        {
            var tescil = hak.ImarParselleri.Single(p => p.GeciciKod == parsel);
            tescil.Paydaslar.Should().OnlyContain(s => s.Hisse.Payda == lr.GetProperty(parsel).GetInt64());
            tescil.Paydaslar.Sum(s => (long)s.Hisse.Pay).Should().Be(lr.GetProperty(parsel).GetInt64());
        }

        hak.ImarParselleri.Should().HaveCount(37);
        hak.MalikHaklari.Select(m => m.MalikId).Distinct().Should().HaveCount(40);
    }

    [Theory]
    [MemberData(nameof(Projeler))]
    public async Task Pilot1_Saha_Projesi_Onaylanip_Pilot_Damgali_CKS_Excel_Ve_Fen_Klasoru_Uretilmeli(string kod)
    {
        using var alan = await PilotCalismaAlani.AcAsync(Proje(kod));
        var a = Altin(kod);

        var onay = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);
        var cks = await alan.Cikti.UretAsync(CiktiTuru.CksDopOzeti, PilotCalismaAlani.Muhendis, alan.CiktiYolu("dop.cks"));
        var xlsx = await alan.Cikti.UretAsync(CiktiTuru.Cetveller, PilotCalismaAlani.Muhendis, alan.CiktiYolu("cetveller.xlsx"));
        var pdf = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoru, PilotCalismaAlani.Muhendis, alan.CiktiYolu("fen.pdf"));

        onay.IsSuccess.Should().BeTrue(onay.IsFailure ? onay.Error.TechnicalMessage : string.Empty);
        foreach (var s in new[] { cks, xlsx, pdf })
        {
            s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
            File.Exists(s.Value.DosyaYolu).Should().BeTrue();
        }

        using var akis = File.OpenRead(cks.Value.DosyaYolu);
        var ozet = new CksFileReader().Read(akis).Value;
        ozet.ToplamDuzenlemeyeGirenDopAlinanM2.Should().Be(a.R1);
        ozet.ToplamImarAdaAlaniM2.Should().Be(a.R2);
        ozet.Dopo.Should().Be(a.Dopo);
        ozet.KamuyaAyrilanAlanM2.Should().Be(a.Kamu);
    }
}
