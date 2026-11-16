using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tecviz;
using Xunit;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>
/// P3.1 — TPYS Md. 8(2) arşiv onaylı YARDIMCI hesaplayıcı (spec 4.3.2; varsayılan değildir).
/// Kademe tablosu ürün içinde SABİT DEĞİLDİR, girdidir. Buradaki tablo spec 4.3.2'deki
/// TASLAK (yönetmelik görselinden doğrulanmamış) değerlerdir ve yalnızca formülü sınamak için test verisidir.
/// Beklenen değerler: ΔA = Σ r_k · max(0, min(A, u_k) − l_k), elle hesaplandı.
/// </summary>
public sealed class ArsivOnayliTecvizYardimcisiTests
{
    private static TecvizKademeTablosu TaslakTablo() => TecvizKademeTablosu.Olustur(
    [
        new TecvizKademesi(0m, 10m, 0.05m),
        new TecvizKademesi(10m, 100m, 0.02m),
        new TecvizKademesi(100m, 500m, 0.01m),
        new TecvizKademesi(500m, 1000m, 0.005m),
        new TecvizKademesi(1000m, 5000m, 0.004m),
        new TecvizKademesi(5000m, 25000m, 0.003m),
        new TecvizKademesi(25000m, null, 0.0015m),
    ]).Value;

    [Theory]
    [InlineData("0", "0")]
    [InlineData("10", "0.50")]
    [InlineData("100", "2.30")]
    [InlineData("500", "6.30")]
    [InlineData("750", "7.55")]
    [InlineData("1000", "8.80")]
    [InlineData("5000", "24.80")]
    [InlineData("25000", "84.80")]
    [InlineData("30000", "92.30")]
    public void ArsivOnayliYardimci_Kumulatif_Kademe_Fonksiyonu_Elle_Hesaplanan_Degeri_Vermeli(string alan, string beklenen)
    {
        var sonuc = ArsivOnayliTecvizYardimcisi.Hesapla(decimal.Parse(alan, System.Globalization.CultureInfo.InvariantCulture), TaslakTablo());

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.Should().Be(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ArsivOnayliYardimci_Sonuc_2_Ondalik_AwayFromZero_Yuvarlanmali()
    {
        // 12.345 m2: 0.5 + 2.345*0.02 = 0.5469 -> 0.55
        ArsivOnayliTecvizYardimcisi.Hesapla(12.345m, TaslakTablo()).Value.Should().Be(0.55m);
    }

    [Fact]
    public void ArsivOnayliYardimci_Negatif_Alan_Reddedilmeli()
    {
        ArsivOnayliTecvizYardimcisi.Hesapla(-1m, TaslakTablo()).Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void KademeTablosu_Bos_Tablo_Reddedilmeli()
    {
        TecvizKademeTablosu.Olustur([]).Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void KademeTablosu_Sifirdan_Baslamayan_Tablo_Reddedilmeli()
    {
        TecvizKademeTablosu.Olustur([new TecvizKademesi(5m, null, 0.01m)])
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void KademeTablosu_Bosluklu_Kademeler_Reddedilmeli()
    {
        TecvizKademeTablosu.Olustur([new TecvizKademesi(0m, 10m, 0.05m), new TecvizKademesi(20m, null, 0.01m)])
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void KademeTablosu_Son_Kademe_Acik_Uclu_Olmali_Ara_Kademe_Acik_Olamaz()
    {
        TecvizKademeTablosu.Olustur([new TecvizKademesi(0m, 10m, 0.05m)])
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
        TecvizKademeTablosu.Olustur([new TecvizKademesi(0m, null, 0.05m), new TecvizKademesi(10m, null, 0.01m)])
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void KademeTablosu_Negatif_Oran_Veya_Ters_Aralik_Reddedilmeli()
    {
        TecvizKademeTablosu.Olustur([new TecvizKademesi(0m, null, -0.01m)])
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
        TecvizKademeTablosu.Olustur([new TecvizKademesi(0m, 0m, 0.01m), new TecvizKademesi(0m, null, 0.01m)])
            .Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }
}
