using System.Globalization;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Presentation.Avalonia.Gorunum;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7 UI revizyonu — %100 Türkçe arayüz kuralı: ekrana yansıyan hiçbir enum değeri ham İngilizce/ASCII kod adıyla
/// gösterilmez. Beklenen adlar proje sahibinin P7 talimatındaki karşılıklardır (bağımsız referans).
/// </summary>
public sealed class TurkceAdlarTests
{
    [Theory]
    [InlineData(CadLayerRole.Unassigned, "Atanmamış")]
    [InlineData(CadLayerRole.CadastralParcel, "Kadastro Parseli")]
    [InlineData(CadLayerRole.ZoningParcel, "TK_Oluşan")]
    [InlineData(CadLayerRole.RoadDedication, "Yola Terk")]
    [InlineData(CadLayerRole.RegulationBoundary, "Düzenleme Sınırı")]
    [InlineData(CadLayerRole.BlockEdge, "Ada Kenarı")]
    [InlineData(CadLayerRole.Park, "Park Alanı")]
    [InlineData(CadLayerRole.CadastralLine, "Kadastro Hattı")]
    [InlineData(CadLayerRole.NewParcelPoint, "Yeni Parsel Noktası")]
    [InlineData(CadLayerRole.RegisteredPoint, "Tescilli Nokta")]
    [InlineData(CadLayerRole.ZoningBlock, "İmar Adası")]
    [InlineData(CadLayerRole.Ignored, "Yoksay")]
    public void TurkceAdlar_Katman_Rolu_Turkce_Gosterilmeli(CadLayerRole rol, string beklenen)
    {
        TurkceAdlar.Ad(rol).Should().Be(beklenen);
    }

    [Theory]
    [InlineData(GeodetikDatum.Itrf96, "ITRF96 (TM 3°)")]
    [InlineData(GeodetikDatum.Ed50, "ED50 (TM 3° · Hayford)")]
    public void TurkceAdlar_Datum_Turkce_Gosterilmeli(GeodetikDatum datum, string beklenen)
    {
        TurkceAdlar.Ad(datum).Should().Be(beklenen);
    }

    [Theory]
    [InlineData(KoordinatKalitesi.ArsivOnayli, "Arşiv Onaylı")]
    [InlineData(KoordinatKalitesi.Gecici, "Geçici Koordinatlı")]
    public void TurkceAdlar_Koordinat_Kalitesi_Turkce_Gosterilmeli(KoordinatKalitesi kalite, string beklenen)
    {
        TurkceAdlar.Ad(kalite).Should().Be(beklenen);
    }

    [Theory]
    [InlineData(CiktiTuru.Cetveller, "Excel Cetvelleri")]
    [InlineData(CiktiTuru.FenKlasoru, "Fen Klasörü")]
    [InlineData(CiktiTuru.CksDopOzeti, "CKS DOP Özeti")]
    public void TurkceAdlar_Cikti_Turu_Turkce_Gosterilmeli(CiktiTuru tur, string beklenen)
    {
        TurkceAdlar.Ad(tur).Should().Be(beklenen);
    }

    [Fact]
    public void TurkceAdlar_Tum_Enum_Degerleri_Ham_Kod_Adindan_Farkli_Ve_Benzersiz_Olmali()
    {
        static void Denetle<T>(Func<T, string> ad) where T : struct, Enum
        {
            var adlar = Enum.GetValues<T>().Select(v => (Kod: v.ToString(), Ad: ad(v))).ToList();
            adlar.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Ad) && x.Ad != x.Kod, typeof(T).Name + " her değer için Türkçe ad taşımalı");
            adlar.Select(x => x.Ad).Should().OnlyHaveUniqueItems();
        }

        Denetle<CadLayerRole>(TurkceAdlar.Ad);
        Denetle<GeodetikDatum>(TurkceAdlar.Ad);
        Denetle<KoordinatKalitesi>(TurkceAdlar.Ad);
        Denetle<CiktiTuru>(TurkceAdlar.Ad);
        Denetle<CadFormat>(TurkceAdlar.Ad);
    }

    [Fact]
    public void TurkceAdlar_Orta_Meridyen_Derece_Ile_Gosterilmeli()
    {
        TurkceAdlar.OrtaMeridyen(30).Should().Be("DOM 30°");
    }

    [Fact]
    public void TurkceAdConverter_Bilinen_Enumlari_Cevirmeli_Digerlerini_Degistirmemeli()
    {
        var c = TurkceAdConverter.Ornek;

        c.Convert(CadLayerRole.ZoningParcel, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("TK_Oluşan");
        c.Convert(KoordinatKalitesi.Gecici, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("Geçici Koordinatlı");
        c.Convert(GeodetikDatum.Itrf96, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("ITRF96 (TM 3°)");
        c.Convert(33, typeof(string), "meridyen", CultureInfo.InvariantCulture).Should().Be("DOM 33°");
        c.Convert("101/1", typeof(string), null, CultureInfo.InvariantCulture).Should().Be("101/1");
        c.Convert(null, typeof(string), null, CultureInfo.InvariantCulture).Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("ABCDEF0123456789ABCDEF", "ABCDEF01…")]
    [InlineData("ABC", "ABC")]
    [InlineData("", "—")]
    public void TurkceAdlar_Revizyon_Rozeti_Kisa_Gosterilmeli(string tam, string beklenen)
    {
        TurkceAdlar.KisaRevizyon(tam).Should().Be(beklenen);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void SayiBicimi_Makine_Kulturunden_Bagimsiz_Nokta_Ayiracla_Yazmali(string kultur)
    {
        var c = Ceviriciler.Sayi;
        var k = CultureInfo.GetCultureInfo(kultur);

        c.Convert(1600m, typeof(string), "0.00 m²", k).Should().Be("1600.00 m²");
        c.Convert(0.4123456m, typeof(string), "0.0000000", k).Should().Be("0.4123456");
        c.Convert(90d, typeof(string), "0.0°", k).Should().Be("90.0°");
        c.Convert(null, typeof(string), "0.00 m²", k).Should().Be("—");
    }
}
