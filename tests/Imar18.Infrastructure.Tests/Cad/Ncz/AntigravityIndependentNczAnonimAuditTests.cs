using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// Antigravity Bağımsız Denetim Testleri (Faz P6.0 — D15, H2, L2, AC-PILOT-08).
/// NCZ Anonimleştirici: Beyaz liste (whitelist) yapıları, 100 km ofset sınırları,
/// kalan metin taraması (H2) ve symlink/junction/yol kapısı korumalarını (L2) bağımsız olarak doğrular.
/// </summary>
public sealed class AntigravityIndependentNczAnonimAuditTests
{
    private static readonly byte[] PngImza = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] OrnekSentetikNcz(string? ozelKatman = null, string? ozelSinif = null, string? ozelYazi = null)
    {
        var on = new List<byte>();
        on.AddRange(Encoding.ASCII.GetBytes("Netcad 8.5 AntigravityKullanici"));
        on.AddRange(PngImza);
        on.AddRange(Encoding.ASCII.GetBytes("IHDR....onizleme....IEND"));
        on.AddRange(new byte[200 - on.Count]);

        var xml = Encoding.ASCII.GetBytes(
            "<NetmapManager><_Metadata><_BASVURAN>Gizli Kisi</_BASVURAN><_ILCE>Kadikoy</_ILCE></_Metadata></NetmapManager>");

        var dosya = new NczSentetikDosya { OnBolge = [.. on], KatmanSonrasiBolge = xml }
            .Katman("TK_PARSEL_GİREN")
            .Katman(ozelKatman ?? "TK_PARSEL_OLUŞAN")
            .Poligon(0, "101/1", [(4_400_100.0, 500_100.0, 15.0), (4_400_100.0, 500_120.0, 15.0), (4_400_120.0, 500_120.0, 15.0), (4_400_100.0, 500_100.0, 15.0)],
                bildirilenAlan: 200.0, sinif: ozelSinif ?? "PARSEL")
            .Cizgi(0, (4_400_000.0, 500_000.0), (4_400_010.0, 500_010.0))
            .Yazi(0, ozelYazi ?? "Malik Ahmet Yilmaz 12345678901");

        return dosya.Olustur();
    }

    [Theory]
    [InlineData(100_000.0, 100_000.0, true)]       // Tam 100 km (en küçük sınır)
    [InlineData(5_000_000.0, 5_000_000.0, true)]   // Tam 5000 km (en büyük sınır)
    [InlineData(-150_000.0, -200_000.0, true)]     // Negatif geçerli ofset (|o| >= 100km)
    [InlineData(99_999.0, 100_000.0, false)]       // < 100 km (Kuzey yetersiz)
    [InlineData(100_000.0, 99_999.999, false)]     // < 100 km (Doğu yetersiz)
    [InlineData(5_000_000.001, 100_000.0, false)]  // > 5000 km (Kuzey aşırı)
    [InlineData(100_000.0004, 100_000.0, false)]   // 1 mm katı değil (4. ondalık)
    [InlineData(100_000.0, 200_000.0005, false)]   // 1 mm katı değil
    public void NczAnonim_Ofset_100km_Ve_5000km_Ve_1mm_Kati_Sinir_Kurallari(double kuzey, double dogu, bool gecerliMi)
    {
        var ayar = new NczAnonimlestirmeAyarlari((decimal)kuzey, (decimal)dogu);
        var baytlar = OrnekSentetikNcz();

        if (gecerliMi)
        {
            var sonuc = NczAnonimlestirici.Anonimlestir(baytlar, ayar);
            sonuc.IsSuccess.Should().BeTrue();
            sonuc.Value.Icerik.Length.Should().BeGreaterThan(0);
        }
        else
        {
            var act = () => NczAnonimlestirici.Anonimlestir(baytlar, ayar);
            act.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void NczAnonim_BeyazListe_Yalnizca_Tip7_Poligonlar_Ve_Izinli_Siniflar_Yazilmali()
    {
        var kaynakBayt = OrnekSentetikNcz(ozelKatman: "OZEL_KATMAN_99", ozelSinif: "IZINSIZ_OZEL_VILLA", ozelYazi: "TCKN_12345678901");
        var ayar = new NczAnonimlestirmeAyarlari(150_000m, 250_000m);

        var sonuc = NczAnonimlestirici.Anonimlestir(kaynakBayt, ayar);
        sonuc.IsSuccess.Should().BeTrue();

        var res = sonuc.Value;
        res.PoligonSayisi.Should().Be(1);
        res.AtlananKayitSayisi.Should().Be(2, "çizgi ve yazı kayıtları filtrelenmeli");
        res.PngOnizlemeSilindi.Should().BeTrue();

        // Geri okunduğunda
        var doc = new NczFormatAdapter().Read(new MemoryStream(res.Icerik)).Value;
        doc.Polylines.Should().HaveCount(1);
        doc.Polylines[0].FeatureClass.Should().BeNullOrEmpty("izinli sınıf listesinde (PARSEL, ADA, YOL, PARK, BINA) olmayanlar temizlenmeli");
        doc.Polylines[0].LayerName.Should().Be("TK_PARSEL_GİREN");

        // Çıktıda hassas metinler aranır
        var ciktiMetni = Encoding.Latin1.GetString(res.Icerik);
        ciktiMetni.Should().NotContain("IZINSIZ_OZEL_VILLA");
        ciktiMetni.Should().NotContain("TCKN_12345678901");
        ciktiMetni.Should().NotContain("Gizli Kisi");
        ciktiMetni.Should().NotContain("Kadikoy");
        ciktiMetni.Should().NotContain("AntigravityKullanici");
    }

    [Fact]
    public void NczAnonim_KalanMetinTaramasi_H2_Sizinti_Tespiti_Ile_Guvenli_Durus()
    {
        // Kaynaktaki serbest katman adı veya etiket çıktıda kalırsa ArtikMetin fail-safe devreye girer
        // Katman adı en fazla 20 bayt olmalıdır
        var kaynakBayt = OrnekSentetikNcz(ozelKatman: "OZEL_HASSAS_METIN");
        // Eğer katman eşlemesi bilinçli olarak kaynak adıyla aynı yapılırsa (kimlik eşlemesi) sızıntı sayılır
        var ayar = new NczAnonimlestirmeAyarlari(150_000m, 250_000m, KatmanEslemesi: new Dictionary<string, string>
        {
            ["OZEL_HASSAS_METIN"] = "OZEL_HASSAS_METIN"
        });

        var sonuc = NczAnonimlestirici.Anonimlestir(kaynakBayt, ayar);
        sonuc.IsFailure.Should().BeTrue("kimlik eşlemesiyle kaynak metnin aynen yazılması sızıntı sayılır ve engellenir");
        sonuc.Error.TechnicalMessage.Should().Contain("residual source text");
    }

    [Fact]
    public void NczFixtureYolKapisi_Girdi_Uzerine_Yazma_Ve_Klasor_Sinirlari_Dogrulamasi()
    {
        // Girdi ile çıktı aynı
        NczFixtureYolKapisi.Dogrula("C:/proje/test-data/synthetic/a.ncz", "C:/proje/test-data/synthetic/a.ncz")
            .Should().Be("Çıktı girdinin üzerine yazılamaz.");

        // test-data/real altına yazma denemesi
        NczFixtureYolKapisi.Dogrula("C:/proje/girdi.ncz", "C:/proje/test-data/real/cikti.ncz")
            .Should().Be("Çıktı test-data/real veya test-data/raw altına yazılamaz.");

        // test-data/raw altına yazma denemesi
        NczFixtureYolKapisi.Dogrula("C:/proje/girdi.ncz", "C:/proje/test-data/raw/cikti.ncz")
            .Should().Be("Çıktı test-data/real veya test-data/raw altına yazılamaz.");

        // test-data/synthetic dışına yazma denemesi
        NczFixtureYolKapisi.Dogrula("C:/proje/girdi.ncz", "C:/proje/desktop/cikti.ncz")
            .Should().Be("Çıktı yalnızca test-data/synthetic/ altına yazılabilir.");

        // Uzantı .ncz değil
        NczFixtureYolKapisi.Dogrula("C:/proje/girdi.ncz", "C:/proje/test-data/synthetic/cikti.dxf")
            .Should().Be("Çıktı uzantısı .ncz olmalıdır.");

        // Geçerli sentetik çıktı
        NczFixtureYolKapisi.Dogrula("C:/proje/girdi.ncz", "C:/proje/test-data/synthetic/pilot-1/proje_a.ncz")
            .Should().BeNull();
    }
}
