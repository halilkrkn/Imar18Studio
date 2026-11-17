using System.Text.Json;
using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tecviz;
using Xunit;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// P3.2 — Y1 (yol terk) dengelemesi (spec 25.4): tecviz içindeki fark = Tapu − Σ Hesap yalnızca Y1'e eklenir,
/// imar parsellerinin düzeltmesi +0.00 kalır; Σ dengelenmiş = Tapu. Önkoşul: TecvizChecker değerlendirmesi geçmiş olmalı
/// (parsel bazlı esas + çift-geçiş, SORU-013/016). Altın veri değerleri değiştirilmez.
/// </summary>
public sealed class YolTerkDengelemesiTests
{
    private static DengelemeKalemi K(string id, decimal alan) => new(id, alan);

    private static TecvizParselGirdisi TP(string id, decimal tapu, decimal hesap, decimal sinir) =>
        new(id, tapu, hesap, TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, sinir, "sentetik").Value);

    private static TecvizDegerlendirmesi Tecviz(decimal tapu, decimal hesap, decimal sinir) =>
        TecvizChecker.Degerlendir([TP("duzenleme", tapu, hesap, sinir)], sinir).Value;

    private static YolTerkDengelemeSonucu Basarili(Result<YolTerkDengelemeSonucu> s)
    {
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : "");
        return s.Value;
    }

    private static IReadOnlyList<DengelemeKalemi> ProjeAParselleri(JsonElement json) =>
        json.GetProperty("blocks").EnumerateArray()
            .SelectMany(b => b.GetProperty("parcels").EnumerateArray())
            .Select(p => K(p.GetProperty("geciciKod").GetString()!, p.Dec("area")))
            .ToList();

    [Fact]
    public void Y1Dengeleme_ProjeA_Fark_012_Yalniz_Y1_e_Eklenmeli_Ve_DOP_ile_Tutarli_Olmali()
    {
        var json = AltinVeri.ProjeA();
        var m = json.GetProperty("metrics");
        var fark = m.Dec("tecvizFarki");
        var y1Hesap = m.Dec("kamuyaAyrilanYol") - fark; // 1078.78
        var tecviz = Tecviz(m.Dec("toplamKadastroAlani_R1"), m.Dec("hesaplananAlan"), m.Dec("tecvizSiniri_NetcadRaporu"));

        var s = Basarili(YolTerkDengelemesi.Dengele(ProjeAParselleri(json), K("901/Y1", y1Hesap), [], tecviz));

        s.HesapToplamiM2.Should().Be(m.Dec("hesaplananAlan"));
        s.FarkM2.Should().Be(fark);
        s.YolTerk.DuzeltmeM2.Should().Be(0.12m);
        s.YolTerk.DengelenmisAlanM2.Should().Be(1078.90m);
        s.ImarParselleri.Should().HaveCount(14).And.OnlyContain(p => p.DuzeltmeM2 == 0m && p.DengelenmisAlanM2 == p.HesapAlaniM2);
        s.ImarParselToplamiM2.Should().Be(m.Dec("toplamImarTahsis_R2"));
        s.DengelenmisToplamM2.Should().Be(m.Dec("toplamKadastroAlani_R1"));

        // DOP ile tutarlilik: kamuya ayrilan (R1 - R2) = dengelenmis Y1
        var dop = DopCalculator.Hesapla(DopGirdisi.Olustur(s.DengelenmisToplamM2, s.ImarParselToplamiM2).Value, s.ImarParselToplamiM2).Value;
        dop.KamuyaAyrilanAlanM2.Should().Be(s.YolTerk.DengelenmisAlanM2);
        dop.Dopo.Should().Be(m.Dec("dopo"));
    }

    [Fact]
    public void Y1Dengeleme_ProjeB_Fark_020_Y1_5367_04_Olmali()
    {
        var json = AltinVeri.ProjeB();
        var m = json.GetProperty("metrics");
        var adalar = json.GetProperty("blocks").EnumerateArray()
            .Select(b => K("ada-" + b.GetProperty("imarAda").GetInt32(), b.Dec("totalArea")))
            .ToList();
        var tecviz = Tecviz(m.Dec("toplamKadastroAlani_R1"), m.Dec("hesaplananAlan"), m.Dec("tecvizSiniri_NetcadRaporu"));

        var s = Basarili(YolTerkDengelemesi.Dengele(
            adalar, K("902/1/Y1", m.Dec("kamuyaAyrilanYol") - m.Dec("tecvizFarki")), [], tecviz));

        s.HesapToplamiM2.Should().Be(m.Dec("hesaplananAlan"));
        s.YolTerk.DengelenmisAlanM2.Should().Be(m.Dec("kamuyaAyrilanYol"));
        s.ImarParselToplamiM2.Should().Be(m.Dec("toplamImarTahsis_R2"));
    }

    [Fact]
    public void Y1Dengeleme_ProjeC_PARK_Dengelemeye_Katilmamali_Fark_197_Y1_e_Eklenmeli()
    {
        var json = AltinVeri.ProjeC();
        var m = json.GetProperty("metrics");
        var c = json.GetProperty("dengelemeCetveli");
        var tecviz = Tecviz(m.Dec("toplamKadastroAlani_R1"), m.Dec("hesaplananAlan"), m.Dec("tecvizSiniri_NetcadRaporu"));

        var s = Basarili(YolTerkDengelemesi.Dengele(
            [K("imar-parselleri", c.Dec("imarParselToplami"))],
            K("903/1/Y1", c.Dec("Y1") - m.Dec("tecvizFarki")),
            [K("903/1/PARK", c.Dec("PARK"))],
            tecviz));

        s.HesapToplamiM2.Should().Be(m.Dec("hesaplananAlan"));
        s.FarkM2.Should().Be(1.97m);
        s.YolTerk.DengelenmisAlanM2.Should().Be(c.Dec("Y1"));
        s.DigerKamuAlanlari.Should().ContainSingle().Which.DuzeltmeM2.Should().Be(0m);
        s.DengelenmisToplamM2.Should().Be(m.Dec("toplamKadastroAlani_R1"));
    }

    [Fact]
    public void Y1Dengeleme_Negatif_Fark_Y1_den_Dusulmeli()
    {
        var s = Basarili(YolTerkDengelemesi.Dengele([K("A1", 800m)], K("Y1", 200.50m), [], Tecviz(1000.00m, 1000.50m, 1m)));

        s.FarkM2.Should().Be(-0.50m);
        s.YolTerk.DengelenmisAlanM2.Should().Be(200.00m);
        s.DengelenmisToplamM2.Should().Be(1000.00m);
    }

    [Fact]
    public void Y1Dengeleme_Fark_Tecviz_Sinirina_Esitse_Dengelenmeli()
    {
        Basarili(YolTerkDengelemesi.Dengele([K("A1", 800m)], K("Y1", 200m), [], Tecviz(1001m, 1000m, 1m)))
            .YolTerk.DengelenmisAlanM2.Should().Be(201m);
    }

    [Fact]
    public void Y1Dengeleme_Fark_Tecviz_Sinirini_Asarsa_ERR_TECVIZ_001_Ile_Dengelenmemeli()
    {
        var s = YolTerkDengelemesi.Dengele([K("A1", 800m)], K("Y1", 200m), [], Tecviz(1001.01m, 1000m, 1m));

        s.IsFailure.Should().BeTrue();
        s.Error.Code.Should().Be(ErrorCodes.TecvizSiniriAsildi);
    }

    [Fact]
    public void YolTerk_Parsel_Bazli_Tecviz_Gecmediyse_Toplam_Gecse_Bile_Dengeleme_Reddedilmeli()
    {
        // A: +15 (sinir 10) ; B: -14 (sinir 20) ; toplam +1 (sinir 30) -> parsel bazli esas, dengelenmez
        var tecviz = TecvizChecker.Degerlendir([TP("A", 1015m, 1000m, 10m), TP("B", 986m, 1000m, 20m)], 30m).Value;
        tecviz.ToplamSonucu!.Gecti.Should().BeTrue();

        var s = YolTerkDengelemesi.Dengele([K("A1", 1800m)], K("Y1", 200m), [], tecviz);

        s.Error.Code.Should().Be(ErrorCodes.TecvizSiniriAsildi);
    }

    [Fact]
    public void Y1Dengeleme_Tecviz_Hesap_Toplami_Dengeleme_Kalemleriyle_Uyusmazsa_Reddedilmeli()
    {
        YolTerkDengelemesi.Dengele([K("A1", 800m)], K("Y1", 200m), [], Tecviz(1000m, 999m, 5m))
            .Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void Y1Dengeleme_Dengelenmis_Y1_Pozitif_Kalmazsa_Reddedilmeli()
    {
        // hesap 800.40, tapu 800.00 -> fark -0.40 -> Y1 = 0.40 - 0.40 = 0
        YolTerkDengelemesi.Dengele([K("A1", 800m)], K("Y1", 0.40m), [], Tecviz(800.00m, 800.40m, 5m))
            .Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void Y1Dengeleme_Yinelenen_Kimlik_Reddedilmeli()
    {
        var tecviz = Tecviz(1000m, 1000m, 1m);
        YolTerkDengelemesi.Dengele([K("A1", 400m), K("A1", 400m)], K("Y1", 200m), [], tecviz)
            .Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
        YolTerkDengelemesi.Dengele([K("A1", 800m)], K("A1", 200m), [], tecviz)
            .Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void Y1Dengeleme_Bos_Imar_Parseli_Veya_Pozitif_Olmayan_Alan_Reddedilmeli()
    {
        var tecviz = Tecviz(1000m, 1000m, 1m);
        YolTerkDengelemesi.Dengele([], K("Y1", 1000m), [], tecviz).Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
        YolTerkDengelemesi.Dengele([K("A1", 0m)], K("Y1", 1000m), [], tecviz).Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void Y1Dengeleme_Null_Tecviz_Degerlendirmesi_Reddedilmeli()
    {
        var act = () => YolTerkDengelemesi.Dengele([K("A1", 800m)], K("Y1", 200m), [], null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
