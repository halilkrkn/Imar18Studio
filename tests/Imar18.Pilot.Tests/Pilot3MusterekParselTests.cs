using FluentAssertions;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.Pilot.Tests.Destek;
using Xunit;

namespace Imar18.Pilot.Tests;

/// <summary>
/// P6.3 — Pilot 3 müşterek parsel (AC-PILOT-03): Proje-B (902/1, 40 malik, 37 imar parseli, 94 satır — anonim SORU-015
/// fixture'ı) CommandBus üzerinden. Bağımsız referans: her müşterek parselde payda = alan × 100 (dm²) ve Σ pay = payda
/// (Largest Remainder, SORU-011: 101/1 → 353974, 105/2 → 36965); toplam alan korunumu; girdi sırasından bağımsız deterministik sonuç.
/// </summary>
public sealed class Pilot3MusterekParselTests
{
    [Fact]
    public async Task Pilot3_Her_Musterek_Parselde_Dm2_Payda_Ve_Pay_Toplami_Tam_Kapanmali()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeB());

        var hak = alan.Onay.Degerlendir().Hak!;

        foreach (var p in hak.ImarParselleri)
        {
            if (p.Mustakil)
            {
                p.Paydaslar.Single().Hisse.Tam.Should().BeTrue($"{p.GeciciKod} müstakil (TAM)");
                continue;
            }

            var payda = (long)(p.AlanM2 * 100m);
            p.Paydaslar.Should().OnlyContain(s => s.Hisse.Payda == payda, $"{p.GeciciKod} dm² paydası");
            p.Paydaslar.Sum(s => (long)s.Hisse.Pay).Should().Be(payda, $"{p.GeciciKod} Σ pay = payda");
            p.Paydaslar.Sum(s => s.AlanM2).Should().Be(p.AlanM2);
        }

        hak.ImarParselleri.Where(p => !p.Mustakil).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Pilot3_Toplam_Tahsis_R2_ye_Esit_Ve_Hisseler_Tam_Kesirle_Bir_Olmali()
    {
        var proje = SahaProjeleri.ProjeB();
        using var alan = await PilotCalismaAlani.AcAsync(proje);

        var hak = alan.Onay.Degerlendir().Hak!;

        Fraction.Sum(proje.Kadastro.Single().Malikler.Select(m => m.Hisse)).Should().Be(Fraction.One);
        hak.ImarParselleri.SelectMany(p => p.Paydaslar).Sum(s => s.AlanM2)
            .Should().Be(PilotProjesi.Dec(SahaProjeleri.ProjeBJson().GetProperty("metrics"), "toplamImarTahsis_R2"));
        hak.MalikHaklari.Sum(m => m.TahsisEdilenM2).Should().Be(hak.ImarParselleri.Sum(p => p.AlanM2));
    }

    [Fact]
    public async Task Pilot3_Girdi_Sirasi_Degisse_De_RevisionId_Ve_Tescil_Dagitimi_Ayni_Olmali()
    {
        var ileri = SahaProjeleri.ProjeB();
        var geri = new PilotProjesi
        {
            Ad = ileri.Ad,
            Kadastro = [ileri.Kadastro[0] with { Malikler = [.. ileri.Kadastro[0].Malikler.Reverse()] }],
            Olusan = ileri.Olusan,
            TecvizSiniriM2 = ileri.TecvizSiniriM2,
            HakSatirlari = [.. ileri.HakSatirlari!.Reverse()],
        };
        using var a = await PilotCalismaAlani.AcAsync(ileri);
        using var b = await PilotCalismaAlani.AcAsync(geri);

        static IEnumerable<string> Dagitim(ProjeDegerlendirmesi d) => d.Hak!.ImarParselleri
            .SelectMany(p => p.Paydaslar.Select(s => $"{p.GeciciKod}|{s.MalikId}|{s.AlanM2}|{s.Hisse.Pay}/{s.Hisse.Payda}"));

        a.Yol.Revizyon.Should().Be(b.Yol.Revizyon);
        Dagitim(a.Onay.Degerlendir()).Should().Equal(Dagitim(b.Onay.Degerlendir()));
    }

    [Fact]
    public async Task Pilot3_Diskten_Yeniden_Acilan_Proje_Ayni_Dagitimi_Uretmeli()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeB());
        var once = alan.Onay.Degerlendir().Hak!.ImarParselleri.SelectMany(p => p.Paydaslar).Select(s => s.Hisse.ToString()).ToList();
        var rev = alan.Yol.Revizyon;

        (await alan.YenidenAcAsync()).IsSuccess.Should().BeTrue();

        alan.Yol.Revizyon.Should().Be(rev);
        alan.Onay.Degerlendir().Hak!.ImarParselleri.SelectMany(p => p.Paydaslar).Select(s => s.Hisse.ToString()).Should().Equal(once);
    }

    [Fact]
    public async Task Pilot3_Muvafakatsiz_Paydas_Tek_Basina_Mustakil_Parsele_Tahsis_Edilirse_Onay_Engellenmeli()
    {
        var proje = SahaProjeleri.ProjeB();
        var mustakilMalik = proje.HakSatirlari!.GroupBy(r => r.ImarParselKodu).First(g => g.Select(r => r.MalikId).Distinct().Count() == 1).First().MalikId;
        var degisik = new PilotProjesi
        {
            Ad = proje.Ad,
            Kadastro = [proje.Kadastro[0] with { Malikler = [.. proje.Kadastro[0].Malikler.Select(m => m.MalikId == mustakilMalik ? m with { Muvafakatli = false } : m)] }],
            Olusan = proje.Olusan,
            TecvizSiniriM2 = proje.TecvizSiniriM2,
            HakSatirlari = proje.HakSatirlari,
        };
        using var alan = await PilotCalismaAlani.AcAsync(degisik);

        var sonuc = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);

        alan.Onay.Degerlendir().Bulgular.Should().Contain(b => b.Code == ErrorCodes.TapuMuvafakatsizMustakilTahsis);
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
        alan.Yol.Denetim.Should().NotContain(k => k.Ayrinti != null && k.Ayrinti.Contains(mustakilMalik, StringComparison.Ordinal), "denetim kaydı malik kimliği taşımaz (KVKK)");
    }
}
