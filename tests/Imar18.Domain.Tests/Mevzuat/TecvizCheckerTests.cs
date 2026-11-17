using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tecviz;
using Xunit;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// P3.1 — TecvizGirdisi + TecvizChecker (FR-F1.4 / TC-TECVIZ-001, spec 4.3.1, SORU-013/016 kapandı:
/// parsel bazlı girdi esas; toplam sınır da girilmişse çift-geçiş şartı).
/// Beklenen değerler elle hesaplanmıştır: fark = Tapu − Hesap, kontrol |fark| &lt;= sınır.
/// </summary>
public sealed class TecvizCheckerTests
{
    private static TecvizGirdisi Girdi(decimal sinir) =>
        TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, sinir, "Netcad Dengeleme Raporu (sentetik)").Value;

    private static TecvizParselGirdisi P(string id, decimal tapu, decimal hesap, decimal? sinir) =>
        new(id, tapu, hesap, sinir is null ? null : Girdi(sinir.Value));

    private static TecvizDegerlendirmesi Basarili(Result<TecvizDegerlendirmesi> sonuc)
    {
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.Code : "basarili olmali");
        return sonuc.Value;
    }

    // ---------- TecvizGirdisi ----------

    [Fact]
    public void TecvizGirdisi_Gecerli_Muhendis_Girdisi_Aynen_Saklanmali()
    {
        var sonuc = TecvizGirdisi.Olustur(500, "Grafikten Sayısallaştırma", KoordinatKalitesi.Gecici, 21.02m, "TKGM Fen Kayıt");

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.OlcekPaydaM.Should().Be(500);
        sonuc.Value.UretimYontemi.Should().Be("Grafikten Sayısallaştırma");
        sonuc.Value.KoordinatKalite.Should().Be(KoordinatKalitesi.Gecici);
        sonuc.Value.TecvizSiniriM2.Should().Be(21.02m);
        sonuc.Value.KaynakNotu.Should().Be("TKGM Fen Kayıt");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void TecvizGirdisi_Sifir_Veya_Negatif_Sinir_ERR_TECVIZ_004_Ile_Reddedilmeli(double sinir)
    {
        var sonuc = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, (decimal)sinir, "kaynak");

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void TecvizGirdisi_Pozitif_Olmayan_Olcek_Paydasi_Reddedilmeli(int olcek)
    {
        TecvizGirdisi.Olustur(olcek, "Sayısal", KoordinatKalitesi.ArsivOnayli, 1m, "kaynak")
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Theory]
    [InlineData("", "kaynak")]
    [InlineData("Sayısal", " ")]
    public void TecvizGirdisi_Bos_Uretim_Yontemi_Veya_Kaynak_Notu_Reddedilmeli(string yontem, string kaynak)
    {
        TecvizGirdisi.Olustur(1000, yontem, KoordinatKalitesi.ArsivOnayli, 1m, kaynak)
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void TecvizGirdisi_Tanimsiz_Koordinat_Kalitesi_Reddedilmeli()
    {
        TecvizGirdisi.Olustur(1000, "Sayısal", (KoordinatKalitesi)99, 1m, "kaynak")
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    // ---------- Tek parsel: sınır ve işaret ----------

    [Fact]
    public void TecvizChecker_Fark_Sinira_Tam_Esitse_Gecmeli()
    {
        var d = Basarili(TecvizChecker.Degerlendir([P("101/1", 100.00m, 98.00m, 2.00m)], null));

        d.Gecti.Should().BeTrue();
        d.ParselSonuclari.Should().ContainSingle().Which.FarkM2.Should().Be(2.00m);
        d.Bulgular.Should().BeEmpty();
    }

    [Fact]
    public void TecvizChecker_Fark_Siniri_1_Santimetrekare_Asarsa_ERR_TECVIZ_001_Critical_Vermeli()
    {
        var d = Basarili(TecvizChecker.Degerlendir([P("101/1", 100.00m, 97.99m, 2.00m)], null));

        d.Gecti.Should().BeFalse();
        d.ParselSonuclari[0].Gecti.Should().BeFalse();
        var hata = d.Bulgular.Should().ContainSingle().Which;
        hata.Code.Should().Be(ErrorCodes.TecvizSiniriAsildi);
        hata.Severity.Should().Be(ErrorSeverity.Critical);
        hata.TechnicalMessage.Should().Contain("2.01").And.Contain("2.00");
        d.KritikVar.Should().BeTrue();
    }

    [Fact]
    public void TecvizChecker_Hesap_Tapudan_Buyukse_Mutlak_Fark_Kullanilmali_Isaret_Korunmali()
    {
        var d = Basarili(TecvizChecker.Degerlendir([P("101/1", 100.00m, 101.50m, 1.50m)], null));

        var s = d.ParselSonuclari[0];
        s.FarkM2.Should().Be(-1.50m);
        s.MutlakFarkM2.Should().Be(1.50m);
        s.Gecti.Should().BeTrue();
    }

    [Fact]
    public void TecvizChecker_Sifir_Fark_Gecmeli()
    {
        Basarili(TecvizChecker.Degerlendir([P("1", 500m, 500m, 0.01m)], null)).Gecti.Should().BeTrue();
    }

    // ---------- Altın veri: Netcad referans tecviz sınırı (mühendis girdisi) ----------

    public static TheoryData<string> Projeler => new() { "A", "B", "C" };

    [Theory]
    [MemberData(nameof(Projeler))]
    public void TecvizChecker_Altin_Veri_Duzenleme_Toplami_Netcad_Siniri_Icinde_Kalmali(string proje)
    {
        var json = proje switch { "A" => AltinVeri.ProjeA(), "B" => AltinVeri.ProjeB(), _ => AltinVeri.ProjeC() };
        var m = json.GetProperty("metrics");
        var tapu = m.Dec("toplamKadastroAlani_R1");
        var hesap = m.Dec("hesaplananAlan");
        var sinir = m.Dec("tecvizSiniri_NetcadRaporu");

        var d = Basarili(TecvizChecker.Degerlendir([P("duzenleme", tapu, hesap, sinir)], sinir));

        d.Gecti.Should().BeTrue();
        d.ParselSonuclari[0].FarkM2.Should().Be(m.Dec("tecvizFarki"));
        d.ToplamSonucu!.FarkM2.Should().Be(m.Dec("tecvizFarki"));
        d.TekMesaj.Should().BeTrue();
    }

    // ---------- Çok parsel: çift-geçiş şartı (SORU-013/016) ----------

    [Fact]
    public void TecvizChecker_Cok_Parsel_Toplam_Girilmemisse_Yalniz_Parsel_Bazli_Kontrol_Yapmali()
    {
        var d = Basarili(TecvizChecker.Degerlendir(
            [P("1", 1000m, 999m, 2m), P("2", 2000m, 2001m, 2m)], null));

        d.Gecti.Should().BeTrue();
        d.ParselSonuclari.Should().HaveCount(2);
        d.ToplamSonucu.Should().BeNull();
        d.TekMesaj.Should().BeTrue();
    }

    [Fact]
    public void TecvizChecker_Cok_Parsel_Toplam_Hesabi_Parsel_Toplamlarindan_Yapilmali()
    {
        // Tapu toplam 3000, hesap toplam 2995 -> fark 5 > 4 sinir
        var d = Basarili(TecvizChecker.Degerlendir(
            [P("1", 1000m, 998m, 3m), P("2", 2000m, 1997m, 3m)], 4m));

        d.ToplamSonucu!.TapuAlaniM2.Should().Be(3000m);
        d.ToplamSonucu.HesapAlaniM2.Should().Be(2995m);
        d.ToplamSonucu.FarkM2.Should().Be(5m);
        d.ToplamSonucu.Gecti.Should().BeFalse();
    }

    [Fact]
    public void TecvizChecker_Parseller_Gecip_Toplam_Kalirsa_Cift_Gecis_Sarti_Saglanmamali_Ve_Iki_Sonuc_Gosterilmeli()
    {
        var d = Basarili(TecvizChecker.Degerlendir(
            [P("1", 1000m, 998m, 3m), P("2", 2000m, 1997m, 3m)], 4m));

        d.ParselSonuclari.Should().OnlyContain(s => s.Gecti);
        d.Gecti.Should().BeFalse("toplam sınır da girildiğinde çift-geçiş şartı uygulanır");
        d.TekMesaj.Should().BeFalse();
        d.Bulgular.Select(b => b.Code).Should().BeEquivalentTo(
            [ErrorCodes.TecvizSiniriAsildi, ErrorCodes.TecvizParselToplamSonucuFarkli]);
    }

    [Fact]
    public void TecvizChecker_Parsel_Kalip_Toplam_Gecerse_Parsel_Bazli_Esas_Oldugundan_Gecmemeli()
    {
        // parsel 1: fark +4 > 3 ; parsel 2: fark -4 ; toplam fark 0 <= 10
        var d = Basarili(TecvizChecker.Degerlendir(
            [P("1", 1000m, 996m, 3m), P("2", 2000m, 2004m, 5m)], 10m));

        d.ToplamSonucu!.Gecti.Should().BeTrue();
        d.Gecti.Should().BeFalse();
        d.TekMesaj.Should().BeFalse();
        d.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TecvizSiniriAsildi)
            .And.Contain(b => b.Code == ErrorCodes.TecvizParselToplamSonucuFarkli && b.Severity == ErrorSeverity.Warning);
    }

    [Fact]
    public void TecvizChecker_Her_Iki_Kontrol_Gecerse_Tek_Mesaj_Ve_Bulgu_Olmamali()
    {
        var d = Basarili(TecvizChecker.Degerlendir(
            [P("1", 1000m, 999m, 3m), P("2", 2000m, 1999m, 3m)], 4m));

        d.Gecti.Should().BeTrue();
        d.TekMesaj.Should().BeTrue();
        d.Bulgular.Should().BeEmpty();
    }

    [Fact]
    public void TecvizChecker_Parsel_Girdisi_Eksikse_ERR_TECVIZ_003_Vermeli_Ve_Gecmemeli()
    {
        var d = Basarili(TecvizChecker.Degerlendir(
            [P("1", 1000m, 999m, 3m), P("2", 2000m, 1999m, null)], 4m));

        d.Gecti.Should().BeFalse();
        d.ParselSonuclari.Should().ContainSingle(s => s.KontrolId == "1");
        d.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TecvizGirdisiEksik && b.TechnicalMessage.Contains("2"));
    }

    [Fact]
    public void TecvizChecker_Asim_Mesaji_Parsel_Kodunu_Icermeli()
    {
        var d = Basarili(TecvizChecker.Degerlendir([P("902/1", 1000m, 990m, 3m), P("902/2", 10m, 10m, 1m)], null));

        d.Bulgular.Should().ContainSingle().Which.TechnicalMessage.Should().Contain("902/1");
    }

    [Fact]
    public void TecvizChecker_Girdi_Eksikken_ERR_TECVIZ_002_Uretilmemeli()
    {
        var d = Basarili(TecvizChecker.Degerlendir([P("1", 1000m, 999m, 3m), P("2", 2000m, 1999m, null)], 4m));

        d.ToplamSonucu!.Gecti.Should().BeTrue();
        d.Bulgular.Should().NotContain(b => b.Code == ErrorCodes.TecvizParselToplamSonucuFarkli);
        d.Gecti.Should().BeFalse();
    }

    // ---------- Geçersiz girdiler ----------

    [Fact]
    public void TecvizChecker_Bos_Parsel_Listesi_Reddedilmeli()
    {
        TecvizChecker.Degerlendir([], null).Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void TecvizChecker_Yinelenen_Parsel_Kimligi_Reddedilmeli()
    {
        TecvizChecker.Degerlendir([P("1", 1m, 1m, 1m), P("1", 2m, 2m, 1m)], null)
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void TecvizChecker_Pozitif_Olmayan_Alan_Reddedilmeli(int tapu, int hesap)
    {
        TecvizChecker.Degerlendir([P("1", tapu, hesap, 1m)], null)
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TecvizChecker_Pozitif_Olmayan_Toplam_Sinir_Reddedilmeli(int toplam)
    {
        TecvizChecker.Degerlendir([P("1", 10m, 10m, 1m)], toplam)
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void TecvizChecker_Ayni_Girdiyle_Tekrar_Edilebilir_Sonuc_Uretmeli()
    {
        var girdi = new[] { P("2", 2000m, 1999m, 3m), P("1", 1000m, 996m, 3m) };

        var a = Basarili(TecvizChecker.Degerlendir(girdi, 4m));
        var b = Basarili(TecvizChecker.Degerlendir(girdi.Reverse().ToArray(), 4m));

        a.ParselSonuclari.Should().BeEquivalentTo(b.ParselSonuclari, o => o.WithStrictOrdering());
        a.ParselSonuclari.Select(s => s.KontrolId).Should().Equal("1", "2");
        a.Bulgular.Should().BeEquivalentTo(b.Bulgular, o => o.WithStrictOrdering());
    }
}
