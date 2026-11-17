using FluentAssertions;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Xunit;

namespace Imar18.Application.Tests.Proje;

/// <summary>
/// P5.1 — CommandBus (<see cref="ProjeKomutYolu"/>): tüm mutasyonlar komut yolundan geçer; başarılı mutasyon ile denetim kaydı
/// tek atomik commit'te yazılır; reddedilen girişimler de denetime girer; kayıt başarısızsa bellek durumu geri alınır (arch.md, AC-IO-01).
/// </summary>
public sealed class ProjeKomutYoluTests
{
    private static async Task<(ProjeKomutYolu Yol, BellekProjeDeposu Depo)> BosAsync()
    {
        var depo = new BellekProjeDeposu();
        var yol = (await ProjeKomutYolu.AcAsync(depo, "Boş Proje", new SabitZaman(DateTimeOffset.UnixEpoch))).Value;
        return (yol, depo);
    }

    [Fact]
    public async Task CommandBus_Yeni_Proje_Acilisi_Denetim_Kaydiyla_Kalici_Olmali()
    {
        var (yol, depo) = await BosAsync();

        yol.Belge.ProjeAdi.Should().Be("Boş Proje");
        yol.Belge.Surum.Should().Be(0);
        depo.CommitSayisi.Should().Be(1);
        depo.Kayit!.Denetim.Should().ContainSingle(d => d.Islem == "ProjeOlustur" && d.Sonuc == AuditSonucu.Basarili);
    }

    [Fact]
    public async Task CommandBus_Basarili_Mutasyon_Durum_Ve_Denetimi_Ayni_Committe_Yazmali()
    {
        var (yol, depo) = await BosAsync();
        var onceki = yol.Revizyon;

        var sonuc = await yol.GonderAsync(new ProjeCrsAyarlaKomutu(OrnekProje.Crs()), OrnekProje.Muhendis);

        sonuc.IsSuccess.Should().BeTrue();
        depo.CommitSayisi.Should().Be(2, "mutasyon ve denetim kaydı ayrı commit'lere bölünmemeli");
        depo.Kayit!.Belge.Crs.Should().NotBeNull();
        depo.Kayit.Belge.Surum.Should().Be(1);
        var kayit = depo.Kayit.Denetim[^1];
        kayit.Islem.Should().Be("ProjeCrsAyarla");
        kayit.Sonuc.Should().Be(AuditSonucu.Basarili);
        kayit.KullaniciId.Should().Be(OrnekProje.Muhendis);
        kayit.OncekiRevizyon.Should().Be(onceki.Deger);
        kayit.SonrakiRevizyon.Should().Be(yol.Revizyon.Deger);
        kayit.SonrakiRevizyon.Should().NotBe(kayit.OncekiRevizyon);
    }

    [Fact]
    public async Task CommandBus_Reddedilen_Komut_Durumu_Degistirmemeli_Ama_Denetime_Yazilmali()
    {
        var (yol, depo) = await BosAsync();
        var oncekiBelge = yol.Belge;
        var bozuk = new ParselGeometrisi("K1", ParselRolu.Kadastro, [new GeodeticPoint(0m, 0m), new GeodeticPoint(1m, 1m)]);

        var sonuc = await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu([bozuk]), OrnekProje.Muhendis);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.TopoDejenerePoligon);
        yol.Belge.Should().BeSameAs(oncekiBelge);
        depo.Kayit!.Belge.Surum.Should().Be(0);
        var kayit = depo.Kayit.Denetim[^1];
        kayit.Sonuc.Should().Be(AuditSonucu.Reddedildi);
        kayit.HataKodu.Should().Be(ErrorCodes.TopoDejenerePoligon);
        kayit.Islem.Should().Be("ParselGeometrileriAyarla");
    }

    [Fact]
    public async Task CommandBus_Yinelenen_Parsel_Kimligi_Reddedilmeli()
    {
        var (yol, _) = await BosAsync();
        var g = OrnekProje.Dikdortgen("A1", ParselRolu.Imar, 0m, 12m);

        var sonuc = await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu([g, g with { Rol = ParselRolu.YolTerk }]), OrnekProje.Muhendis);

        sonuc.Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
    }

    [Fact]
    public async Task CommandBus_Kayit_Basarisiz_Olursa_Bellek_Durumu_Geri_Alinmali()
    {
        var (yol, depo) = await BosAsync();
        var oncekiBelge = yol.Belge;
        var oncekiDenetim = yol.Denetim.Count;
        depo.SonrakiYazmayiBozdur = true;

        var sonuc = await yol.GonderAsync(new ProjeCrsAyarlaKomutu(OrnekProje.Crs()), OrnekProje.Muhendis);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        yol.Belge.Should().BeSameAs(oncekiBelge);
        yol.Denetim.Should().HaveCount(oncekiDenetim);
        depo.Kayit!.Belge.Crs.Should().BeNull();

        (await yol.GonderAsync(new ProjeCrsAyarlaKomutu(OrnekProje.Crs()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CommandBus_Var_Olan_Kayit_Acilista_Yuklenmeli()
    {
        var (yol, depo, _) = await OrnekProje.TamProjeAsync();

        var yeniden = (await ProjeKomutYolu.AcAsync(depo, "kullanılmaz", TimeProvider.System)).Value;

        yeniden.Belge.Should().Be(yol.Belge);
        yeniden.Revizyon.Should().Be(yol.Revizyon);
        yeniden.Denetim.Should().HaveCount(yol.Denetim.Count);
    }

    [Fact]
    public async Task CommandBus_Bozuk_Kayit_Acilmamali_Ve_Uzerine_Yazilmamali()
    {
        var depo = new BellekProjeDeposu { OkumaBozuk = true };

        var sonuc = await ProjeKomutYolu.AcAsync(depo, "x", TimeProvider.System);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        depo.CommitSayisi.Should().Be(0);
    }

    [Fact]
    public async Task CommandBus_Eszamanli_Komutlar_Tek_Yazici_Sirasiyla_Islenmeli()
    {
        var (yol, depo) = await BosAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => yol.GonderAsync(new ProjeAdiDegistirKomutu("Proje " + i), OrnekProje.Muhendis))));

        yol.Belge.Surum.Should().Be(20);
        yol.Denetim.Select(d => d.Sira).Should().Equal(Enumerable.Range(1, 21).Select(i => (long)i));
        depo.Kayit!.Denetim.Should().HaveCount(21);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CommandBus_Kullanici_Kimligi_Bos_Olamaz(string kullanici)
    {
        var (yol, _) = await BosAsync();

        var act = () => yol.GonderAsync(new ProjeAdiDegistirKomutu("x"), kullanici);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CommandBus_Denetim_Kayitlari_Malik_Kimligi_Icermemeli()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();

        yol.Denetim.Should().NotContain(d => (d.Ayrinti ?? string.Empty).Contains("MLK-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommandBus_Ayni_Veriyle_Komut_Revizyonu_Degistirmemeli()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        var rev = yol.Revizyon;

        await yol.GonderAsync(new DopGirdisiAyarlaKomutu(OrnekProje.Dop()), OrnekProje.Muhendis);

        yol.Revizyon.Should().Be(rev);
        yol.Denetim[^1].OncekiRevizyon.Should().Be(yol.Denetim[^1].SonrakiRevizyon);
    }
}
