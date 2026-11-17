using FluentAssertions;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tecviz;
using Xunit;

namespace Imar18.Application.Tests.Onay;

/// <summary>
/// P5.2 — ApprovalService (spec 11, FR-F5.10): kritik hatada onay engeli (AC-PILOT-05 / AC-ONAY-01), onay metni zorunluluğu
/// (AC-ONAY-02), onay ve reddedilen girişimlerin denetimi (AC-ONAY-03), RevisionId bağı ve veri değişince otomatik düşme
/// (AC-PILOT-06; geometri ve CRS dahil), 24 saat içinde geri alma (AC-ONAY-04/05).
/// </summary>
public sealed class ApprovalServiceTests
{
    private static async Task<(ProjeKomutYolu Yol, ApprovalService Onay, SabitZaman Zaman)> HazirAsync()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        return (yol, new ApprovalService(yol, zaman, OrnekProje.Topoloji), zaman);
    }

    [Fact]
    public async Task KullaniciOnayi_Tam_Ve_Kritiksiz_Projede_Revizyona_Bagli_Onay_Verilmeli()
    {
        var (yol, servis, zaman) = await HazirAsync();
        var rev = yol.Revizyon;

        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        sonuc.Value.RevizyonId.Should().Be(rev);
        sonuc.Value.OnaylayanId.Should().Be(OrnekProje.Muhendis);
        sonuc.Value.Zaman.Should().Be(zaman.Simdi);
        yol.Revizyon.Should().Be(rev, "onay içerik revizyonunu değiştirmez");
        servis.GecerliOnayVar.Should().BeTrue();
    }

    [Fact]
    public async Task KullaniciOnayi_AuditLog_Kaydi_Olusturmali()
    {
        var (yol, servis, _) = await HazirAsync();

        await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        var kayit = yol.Denetim[^1];
        kayit.Islem.Should().Be("OnayVer");
        kayit.Sonuc.Should().Be(AuditSonucu.Basarili);
        kayit.SonrakiRevizyon.Should().Be(yol.Revizyon.Deger);
    }

    [Fact]
    public async Task KullaniciOnayi_KritikHata_Var_Ise_Engellenmeli_Ve_Red_Denetlenmeli()
    {
        var (yol, servis, _) = await HazirAsync();
        // R2 = 100 → DOPO = 0.5 > %45 → ERR-DOP-001 (Critical).
        (await yol.GonderAsync(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(200.00m, 100.00m).Value), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        servis.Degerlendir().Bulgular.Should().Contain(b => b.Code == ErrorCodes.DopYuzde45Asildi);

        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
        yol.Belge.Onay.Should().BeNull();
        yol.Denetim[^1].Should().Match<AuditKaydi>(d => d.Islem == "OnayVer" && d.Sonuc == AuditSonucu.Reddedildi && d.HataKodu == ErrorCodes.CiktiOnayKritikHataEngeli);
    }

    [Fact]
    public async Task KullaniciOnayi_Tecviz_Asiminda_Kritik_Engel_Olmali()
    {
        var (yol, servis, _) = await HazirAsync();
        var asim = new[] { new TecvizParselGirdisi("K1", 202.00m, 200.00m, OrnekProje.Tecviz()) };
        (await yol.GonderAsync(new TecvizGirdileriAyarlaKomutu(asim, null), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        (await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Onaylıyorum")]
    public async Task KullaniciOnayi_OnayMetni_Olmadan_Gecilememeli(string metin)
    {
        var (yol, servis, _) = await HazirAsync();

        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, metin);

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayMetniKabulEdilmedi);
        yol.Belge.Onay.Should().BeNull();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task KullaniciOnayi_Hesap_Eksikse_Engellenmeli()
    {
        var depo = new BellekProjeDeposu();
        var yol = (await ProjeKomutYolu.AcAsync(depo, "Eksik", TimeProvider.System)).Value;
        await yol.GonderAsync(new ProjeCrsAyarlaKomutu(OrnekProje.Crs()), OrnekProje.Muhendis);
        var servis = new ApprovalService(yol, TimeProvider.System, OrnekProje.Topoloji);

        var sonuc = await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayIcinHesapEksik);
        servis.Degerlendir().EksikGirdiler.Should().Contain(["geometri", "tecviz", "DOP", "hak eşleştirme"]);
    }

    [Fact]
    public async Task KullaniciOnayi_Hak_Eslestirme_DOPO_DOP_Sonucundan_Farkliysa_Eksik_Sayilmali()
    {
        var (yol, servis, _) = await HazirAsync();
        await yol.GonderAsync(new HakEslestirmeAyarlaKomutu(OrnekProje.Hak() with { Dopo = 0.4000001m }), OrnekProje.Muhendis);

        var d = servis.Degerlendir();

        d.Tamamlandi.Should().BeFalse();
        d.Bulgular.Should().Contain(b => b.Code == ErrorCodes.GirdProjeGirdisiGecersiz);
    }

    [Fact]
    public async Task KullaniciOnayi_VeriDegisirse_Otomatik_Gecersizlesmeli_Geometri_1_mm()
    {
        var (yol, servis, _) = await HazirAsync();
        await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        var geometri = OrnekProje.Geometri().ToArray();
        var k = geometri[0].Koseler.ToArray();
        k[2] = k[2] with { X = k[2].X + 0.001m };
        geometri[0] = geometri[0] with { Koseler = k };

        await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(geometri), OrnekProje.Muhendis);

        yol.Belge.Onay.Should().BeNull();
        servis.GecerliOnayVar.Should().BeFalse();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.OnayDustu);
        yol.Denetim[^2].Sonuc.Should().Be(AuditSonucu.Basarili);
    }

    [Fact]
    public async Task KullaniciOnayi_CRS_Degisirse_Otomatik_Gecersizlesmeli()
    {
        var (yol, servis, _) = await HazirAsync();
        await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        await yol.GonderAsync(new ProjeCrsAyarlaKomutu(ProjeCrs.Olustur(GeodetikDatum.Itrf96, 36, 2005.00m, 1000).Value), OrnekProje.Muhendis);

        servis.GecerliOnayVar.Should().BeFalse();
    }

    [Fact]
    public async Task KullaniciOnayi_Ayni_Veri_Yeniden_Gonderilirse_Onay_Korunmali()
    {
        var (yol, servis, _) = await HazirAsync();
        await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);

        await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(OrnekProje.Geometri()), OrnekProje.Muhendis);

        servis.GecerliOnayVar.Should().BeTrue();
    }

    [Fact]
    public async Task KullaniciOnayi_24Saat_Icinde_GeriAlinabilmeli_Ve_Denetlenmeli()
    {
        var (yol, servis, zaman) = await HazirAsync();
        await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        zaman.Ilerlet(TimeSpan.FromHours(24));

        var sonuc = await servis.GeriAlAsync(OrnekProje.Muhendis);

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.NihaiCiktiUretilmisti.Should().BeFalse();
        yol.Belge.Onay.Should().BeNull();
        yol.Denetim[^1].Should().Match<AuditKaydi>(d => d.Islem == "OnayGeriAl" && d.Sonuc == AuditSonucu.Basarili);
        (await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue("geri alınan revizyon yeniden onaylanabilir");
    }

    [Fact]
    public async Task KullaniciOnayi_24_Saat_Sonra_GeriAlma_Reddedilmeli()
    {
        var (yol, servis, zaman) = await HazirAsync();
        await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        zaman.Ilerlet(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        var sonuc = await servis.GeriAlAsync(OrnekProje.Muhendis);

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayGeriAlinamaz);
        yol.Belge.Onay.Should().NotBeNull();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task KullaniciOnayi_Onay_Yokken_GeriAlma_Reddedilmeli()
    {
        var (_, servis, _) = await HazirAsync();

        (await servis.GeriAlAsync(OrnekProje.Muhendis)).Error.Code.Should().Be(ErrorCodes.CiktiOnayGeriAlinamaz);
    }

    [Fact]
    public async Task OnayEkrani_Spec_11_3_Ozetini_Gostermeli()
    {
        var (yol, servis, _) = await HazirAsync();

        var ekran = servis.OnayEkraniHazirla();

        ekran.ProjeAdi.Should().Be("Sentetik P5 Projesi");
        ekran.RevizyonId.Should().Be(yol.Revizyon.Deger);
        ekran.ParselSayisi.Should().Be(3);
        ekran.ToplamKadastroAlaniM2.Should().Be(200.00m);
        ekran.Dopo.Should().Be(0.4000000m);
        ekran.KritikHataSayisi.Should().Be(0);
        ekran.TecvizUyariSayisi.Should().Be(0);
        ekran.MevzuatProfiliSurumu.Should().Be("TPYS_2022");
        ekran.FenKlasoruSablonSurumu.Should().NotBeNullOrWhiteSpace();
        ekran.ParsidSemaDurumu.Should().Contain("Deneysel");
        ekran.OnayMetni.Should().Be(ApprovalService.OnayMetni);
        ekran.OnayVerilebilir.Should().BeTrue();
        ekran.GecerliOnayVar.Should().BeFalse();
    }

    [Fact]
    public async Task OnayEkrani_Kritik_Hatada_Onay_Verilemez_Gostermeli()
    {
        var (yol, servis, _) = await HazirAsync();
        await yol.GonderAsync(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(200.00m, 100.00m).Value), OrnekProje.Muhendis);

        var ekran = servis.OnayEkraniHazirla();

        ekran.OnayVerilebilir.Should().BeFalse();
        ekran.KritikHataSayisi.Should().BeGreaterThan(0);
        ekran.Engeller.Should().NotBeEmpty();
    }

    [Fact]
    public void ApprovalService_Onay_Metni_Spec_11_4_Ile_Birebir_Olmali()
    {
        ApprovalService.OnayMetni.Should().Be(
            "Bu çıktıyı nihai olarak onaylıyorum. Tecviz, DOP, tahsis ve geometri kontrollerini inceledim. Kritik hata bulunmadığını teyit ediyorum.");
    }
}
