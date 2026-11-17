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
/// P6.0 (D15, spec 25.7.1) — NCZ anonim fixture aracı. Girdi tamamen sentetik NCZ'dir; "kişisel" alanlar bariz sahte
/// yer tutuculardır (gerçek veri yok, AC-PILOT-08). Beklenen köşeler = kaynak köşe + ofset (decimal, elle).
/// </summary>
public sealed class NczAnonimlestiriciTests
{
    private const string SahteKullanici = "sentetik.kullanici.hesabi";
    private const string SahteKisi = "SAHTE_BASVURAN_KISI_ADI";
    private const string SahteAdres = "SAHTE_KURUM_ADRESI_MAH_SK";
    private const string SahteMahalle = "SAHTE_MAHALLE_ADI";
    private const string SahteYazi = "SAHTE_MALIK_YAZISI";
    private static readonly byte[] PngImza = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly NczAnonimlestirmeAyarlari Ayar = new(-4_000_000.000m, -400_000.000m);

    private static byte[] Kaynak()
    {
        var on = new List<byte>();
        on.AddRange(Encoding.ASCII.GetBytes("Netcad 8.5 " + SahteKullanici));
        on.AddRange(PngImza);
        on.AddRange(Encoding.ASCII.GetBytes("IHDR....sahte-onizleme....IEND"));
        on.AddRange(new byte[200 - on.Count]);
        var xml = Encoding.ASCII.GetBytes(
            $"<NetmapManager><_Metadata surum=\"SAHTE_OZNITELIK\"><!-- SAHTE_YORUM --><_BASVURAN_KISI>{SahteKisi}</_BASVURAN_KISI>" +
            $"<_KURUM_ADRESI>{SahteAdres}</_KURUM_ADRESI>\r\n  <_MAH>{SahteMahalle}</_MAH><_PARSEL>999</_PARSEL><_BOS/></_Metadata></NetmapManager>");

        var dosya = new NczSentetikDosya { OnBolge = [.. on], KatmanSonrasiBolge = xml }
            .Katman("TK_PARSEL_GİREN")
            .Katman("TK_PARSEL_OLUŞAN")
            .Poligon(0, "777/5", [(4_400_100.123456, 500_200.5, 12.5), (4_400_100.123456, 500_230.5, 12.5), (4_400_140.123456, 500_230.5, 12.5), (4_400_100.123456, 500_200.5, 12.5)], bildirilenAlan: 600.0, sinif: "PARSEL")
            .Poligon(1, "777/A1", [(4_400_100.0, 500_200.0, 0), (4_400_100.0, 500_210.0, 0), (4_400_110.0, 500_210.0, 0), (4_400_110.0, 500_200.0, 0), (4_400_100.0, 500_200.0, 0)])
            .Poligon(1, "777/A2", [(4_400_100.0, 500_210.0, 0), (4_400_100.0, 500_220.0, 0), (4_400_110.0, 500_220.0, 0), (4_400_100.0, 500_210.0, 0)])
            .Cizgi(0, (4_400_000.0, 500_000.0), (4_400_001.0, 500_001.0))
            .Yazi(0, SahteYazi);
        return dosya.Olustur();
    }

    private static NczAnonimlestirmeSonucu Basarili(Result<NczAnonimlestirmeSonucu> s)
    {
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.TechnicalMessage : string.Empty);
        return s.Value;
    }

    private static CadLayerDocument Oku(byte[] bayt) => new NczFormatAdapter().Read(new MemoryStream(bayt, writable: false)).Value;

    private static bool Iceriyor(byte[] bayt, string metin) => bayt.AsSpan().IndexOf(Encoding.ASCII.GetBytes(metin)) >= 0;

    [Fact]
    public void NczAnonim_Koseler_Sabit_Ofsetle_Tam_Kaydirilmali_Alan_Korunmali_Kot_Sifirlanmali()
    {
        var kaynak = Oku(Kaynak());

        var cikti = Oku(Basarili(NczAnonimlestirici.Anonimlestir(Kaynak(), Ayar)).Icerik);

        cikti.Polylines.Should().HaveCount(3);
        cikti.Polylines[0].Vertices[0].Should().Be(new GeodeticPoint(X: 400_100.123456m, Y: 100_200.5m));
        for (var i = 0; i < 3; i++)
        {
            cikti.Polylines[i].Vertices.Should().Equal(kaynak.Polylines[i].Vertices.Select(v => new GeodeticPoint(v.X - 4_000_000m, v.Y - 400_000m)));
            cikti.Polylines[i].LayerName.Should().Be(kaynak.Polylines[i].LayerName);
            cikti.Polylines[i].DeclaredAreaM2.Should().BeNull("yazılmış (tapu) alanı varsayılan olarak kaldırılır");
            cikti.Polylines[i].FeatureClass.Should().Be(kaynak.Polylines[i].FeatureClass);
            var kaynakAlan = GaussAreaCalculator.Compute(PolygonRing.FromClosed(kaynak.Polylines[i].Vertices).Value).AreaM2Exact;
            GaussAreaCalculator.Compute(PolygonRing.FromClosed(cikti.Polylines[i].Vertices).Value).AreaM2Exact.Should().Be(kaynakAlan);
        }

        cikti.Warnings.Should().NotContain(w => w.Code == ErrorCodes.GirdZKoduIndirgendi, "kot 0 yazılır");
        kaynak.Warnings.Should().Contain(w => w.Code == ErrorCodes.GirdZKoduIndirgendi);
    }

    [Fact]
    public void NczAnonim_Kullanici_Adi_PNG_Ve_XML_Degerleri_Ciktida_Bulunmamali()
    {
        var s = Basarili(NczAnonimlestirici.Anonimlestir(Kaynak(), Ayar));

        foreach (var gizli in new[] { SahteKullanici, SahteKisi, SahteAdres, SahteMahalle, SahteYazi, "Netcad 8.5", "IHDR", "IEND", "999", "SAHTE_OZNITELIK", "SAHTE_YORUM" })
        {
            Iceriyor(s.Icerik, gizli).Should().BeFalse($"'{gizli}' anonim çıktıda kalmamalı");
        }

        s.Icerik.AsSpan().IndexOf(PngImza).Should().Be(-1);
        s.PngOnizlemeSilindi.Should().BeTrue();
        Iceriyor(s.Icerik,
                "<NetmapManager><_Metadata surum=\"\"><_BASVURAN_KISI></_BASVURAN_KISI><_KURUM_ADRESI></_KURUM_ADRESI><_MAH></_MAH>" +
                "<_PARSEL></_PARSEL><_BOS/></_Metadata></NetmapManager>")
            .Should().BeTrue("iç içe yapı ve alan adları korunur, tüm değerler boşaltılır");
        s.BosaltilanXmlAlanSayisi.Should().Be(6);
    }

    [Fact]
    public void NczAnonim_Etiketler_Eslemeyle_Veya_Sirali_Yer_Tutucuyla_Degismeli_Gercek_Etiket_Kalmamali()
    {
        var ayar = Ayar with { EtiketEslemesi = new Dictionary<string, string>(StringComparer.Ordinal) { ["777/5"] = "901/1" } };

        var s = Basarili(NczAnonimlestirici.Anonimlestir(Kaynak(), ayar));
        var cikti = Oku(s.Icerik);

        cikti.Polylines.Select(p => p.Label).Should().Equal("901/1", "E001", "E002");
        s.EslenenEtiketSayisi.Should().Be(1);
        s.YerTutucuEtiketSayisi.Should().Be(2);
        Iceriyor(s.Icerik, "777/").Should().BeFalse();
    }

    [Fact]
    public void NczAnonim_Yalniz_Poligonlar_Tasinmali_Cizgi_Ve_Yazi_Kayitlari_Atlanmali()
    {
        var s = Basarili(NczAnonimlestirici.Anonimlestir(Kaynak(), Ayar));

        s.PoligonSayisi.Should().Be(3);
        s.AtlananKayitSayisi.Should().Be(2);
        Oku(s.Icerik).SkippedEntityCount.Should().Be(0);
    }

    [Fact]
    public void NczAnonim_Katman_Adindaki_Bayat_Baytlar_Kopyalanmamali()
    {
        var kaynak = Kaynak();
        var tablo = NczSentetikDosya.BaslikOku(kaynak, 3);
        var ilkKatmanAdi = tablo + 22;
        Encoding.ASCII.GetBytes("BAYATSIR").CopyTo(kaynak, ilkKatmanAdi + 1 + 16); // uzunluk baytından sonraki kapasite içinde

        var s = Basarili(NczAnonimlestirici.Anonimlestir(kaynak, Ayar));

        Iceriyor(s.Icerik, "BAYATSIR").Should().BeFalse();
        Oku(s.Icerik).Layers.Select(l => l.Name).Should().Equal("TK_PARSEL_GİREN", "TK_PARSEL_OLUŞAN");
    }

    [Fact]
    public void NczAnonim_Yazilmis_Alan_Yalniz_Acik_Ayarla_Korunmali()
    {
        var cikti = Oku(Basarili(NczAnonimlestirici.Anonimlestir(Kaynak(), Ayar with { YazilmisAlaniKoru = true })).Icerik);

        cikti.Polylines[0].DeclaredAreaM2.Should().Be(600m);
    }

    [Fact]
    public void NczAnonim_Standart_Disi_Katman_Adi_Ve_Serbest_Metin_Sinif_Ciktida_Bulunmamali()
    {
        var kaynak = new NczSentetikDosya()
            .Katman("SAHTE_MAHALLE_412ADA").Katman("TK_PARSEL_OLUŞAN").Katman("SAHTE_KISI_KATMANI")
            .Poligon(0, "P1", [(4_400_100.0, 500_200.0, 0), (4_400_100.0, 500_210.0, 0), (4_400_110.0, 500_210.0, 0), (4_400_100.0, 500_200.0, 0)], sinif: "SAHTE AHMET HISSESI")
            .Poligon(1, "P2", [(4_400_100.0, 500_210.0, 0), (4_400_100.0, 500_220.0, 0), (4_400_110.0, 500_220.0, 0), (4_400_100.0, 500_210.0, 0)], sinif: "PARSEL")
            .Olustur();
        var ayar = Ayar with { KatmanEslemesi = new Dictionary<string, string> { ["SAHTE_KISI_KATMANI"] = "SENTETIK_K" } };

        var s = Basarili(NczAnonimlestirici.Anonimlestir(kaynak, ayar));
        var cikti = Oku(s.Icerik);

        cikti.Layers.Select(l => l.Name).Should().Equal("L001", "TK_PARSEL_OLUŞAN", "SENTETIK_K");
        cikti.Polylines.Select(p => p.FeatureClass).Should().Equal(string.Empty, "PARSEL");
        cikti.Polylines.Select(p => p.LayerName).Should().Equal("L001", "TK_PARSEL_OLUŞAN");
        foreach (var gizli in new[] { "SAHTE_MAHALLE_412ADA", "SAHTE_KISI_KATMANI", "SAHTE AHMET HISSESI", "412ADA", "AHMET" })
        {
            Iceriyor(s.Icerik, gizli).Should().BeFalse($"'{gizli}' anonim çıktıda kalmamalı");
        }
    }

    [Fact]
    public void NczAnonim_Gercek_Etiketi_Kendine_Eslemek_Artik_Metin_Taramasiyla_Reddedilmeli()
    {
        var ayar = Ayar with { EtiketEslemesi = new Dictionary<string, string> { ["777/A1"] = "777/A1" } };

        var s = NczAnonimlestirici.Anonimlestir(Kaynak(), ayar);

        s.IsFailure.Should().BeTrue();
        s.Error.Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
        s.Error.TechnicalMessage.Should().Contain("residual source text").And.NotContain("777");
    }

    [Fact]
    public void NczAnonim_XML_Yorumundaki_Alan_Adi_Yedek_Yola_Sizmamali()
    {
        // Tırnaksız öznitelik beyaz listeyi bozar → yedek (düz) yol; yorumdaki "<_SAHTEGIZLI>" alan adı listesine girmemeli.
        var xml = Encoding.ASCII.GetBytes("<NetmapManager><!-- <_SAHTEGIZLI> --><_MAH a=b>SAHTE_DEGER</_MAH></NetmapManager>");
        var kaynak = new NczSentetikDosya { KatmanSonrasiBolge = xml }.Katman("TK_PARSEL_OLUŞAN")
            .Poligon(0, "P1", [(4_400_100.0, 500_200.0, 0), (4_400_100.0, 500_210.0, 0), (4_400_110.0, 500_210.0, 0), (4_400_100.0, 500_200.0, 0)])
            .Olustur();

        var s = Basarili(NczAnonimlestirici.Anonimlestir(kaynak, Ayar));

        Iceriyor(s.Icerik, "<NetmapManager><_MAH></_MAH></NetmapManager>").Should().BeTrue();
        Iceriyor(s.Icerik, "SAHTEGIZLI").Should().BeFalse();
        Iceriyor(s.Icerik, "SAHTE_DEGER").Should().BeFalse();
    }

    [Fact]
    public void NczAnonim_Ayni_Girdide_Bayt_Bayt_Deterministik_Olmali()
    {
        Basarili(NczAnonimlestirici.Anonimlestir(Kaynak(), Ayar)).Icerik
            .Should().Equal(Basarili(NczAnonimlestirici.Anonimlestir(Kaynak(), Ayar)).Icerik);
    }

    [Theory]
    [InlineData(-4_000_000.0005, -400_000)]   // 1 mm katı değil
    [InlineData(99_999.999, 500_000)]         // gizleme için çok küçük (< 100 km)
    [InlineData(0, 500_000)]
    [InlineData(500_000, 5_000_000.001)]      // kesinlik zarfı dışı
    public void NczAnonim_Gecersiz_Ofset_Reddedilmeli(double kuzey, double dogu)
    {
        var act = () => NczAnonimlestirici.Anonimlestir(Kaynak(), new NczAnonimlestirmeAyarlari((decimal)kuzey, (decimal)dogu));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NczAnonim_Bozuk_Kaynak_ERR_GIRD_NCZ_001_Ile_Reddedilmeli()
    {
        var bozuk = Kaynak()[..^3];

        NczAnonimlestirici.Anonimlestir(bozuk, Ayar).Error.Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
    }

    [Fact]
    public void NczAnonim_Cok_Uzun_Sentetik_Etiket_Reddedilmeli()
    {
        var ayar = Ayar with { EtiketEslemesi = new Dictionary<string, string> { ["777/5"] = new string('X', 15) } };

        var act = () => NczAnonimlestirici.Anonimlestir(Kaynak(), ayar);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("C:/depo/test-data/real/p1/A.NCZ", "C:/depo/test-data/synthetic/pilot-1-concave-parcel/a.ncz", null)]
    [InlineData("C:/depo/test-data/real/p1/A.NCZ", "C:/depo/test-data/real/p1/b.ncz", "real")]
    [InlineData("C:/depo/test-data/real/p1/A.NCZ", "C:/depo/test-data/raw/b.ncz", "real")]
    [InlineData("C:/depo/test-data/real/p1/A.NCZ", "C:/depo/docs/b.ncz", "synthetic")]
    [InlineData("C:/depo/test-data/real/p1/A.NCZ", "C:/depo/test-data/synthetic/b.dxf", ".ncz")]
    [InlineData("C:/depo/test-data/synthetic/a.ncz", "C:/depo/test-data/synthetic/a.ncz", "üzerine")]
    public void NczAnonim_Cikti_Yolu_Yalniz_Synthetic_Altina_Izin_Vermeli(string girdi, string cikti, string? beklenenNeden)
    {
        var neden = NczFixtureYolKapisi.Dogrula(girdi, cikti);

        if (beklenenNeden is null)
        {
            neden.Should().BeNull();
        }
        else
        {
            neden.Should().Contain(beklenenNeden);
        }
    }

    [Fact]
    public void NczAnonim_Ciktida_Baslik_Bolgesi_Bos_Olmali_Katman_Tablosu_Hemen_Basliktan_Sonra_Baslamali()
    {
        var s = Basarili(NczAnonimlestirici.Anonimlestir(Kaynak(), Ayar));

        BinaryPrimitives.ReadInt32LittleEndian(s.Icerik.AsSpan(1 + (4 * 3))).Should().Be(49);
        BinaryPrimitives.ReadInt32LittleEndian(s.Icerik.AsSpan(1 + (4 * 4))).Should().Be(0, "PNG önizleme sonu ofseti (h[4]) yok");
    }

    [Fact]
    public async Task NczFixture_Arac_Akisi_Synthetic_Altina_Yedeksiz_Yazmali_Var_Olani_Izinsiz_Ezmemeli()
    {
        var kok = Path.Combine(Path.GetTempPath(), "imar18-p60-" + Guid.NewGuid().ToString("N"));
        try
        {
            var girdi = Path.Combine(kok, "test-data", "real", "p", "kaynak.ncz");
            Directory.CreateDirectory(Path.GetDirectoryName(girdi)!);
            await File.WriteAllBytesAsync(girdi, Kaynak());
            var cikti = Path.Combine(kok, "test-data", "synthetic", "pilot-1-concave-parcel", "anonim.ncz");

            var ilk = await NczFixtureUretici.UretAsync(girdi, cikti, Ayar);
            var ikinci = await NczFixtureUretici.UretAsync(girdi, cikti, Ayar);
            var izinli = await NczFixtureUretici.UretAsync(girdi, cikti, Ayar, uzerineYaz: true);
            var yanlis = await NczFixtureUretici.UretAsync(girdi, Path.Combine(kok, "test-data", "real", "p", "x.ncz"), Ayar);

            ilk.IsSuccess.Should().BeTrue();
            (await File.ReadAllBytesAsync(cikti)).Should().Equal(ilk.Value.Icerik);
            ikinci.IsFailure.Should().BeTrue();
            ikinci.Error.UserMessage.Should().Contain("zaten var");
            izinli.IsSuccess.Should().BeTrue();
            yanlis.Error.UserMessage.Should().Contain("real");
            Directory.GetFiles(Path.GetDirectoryName(cikti)!).Should().ContainSingle("yedek (.bak) veya geçici dosya bırakılmaz");
            File.Exists(Path.Combine(kok, "test-data", "real", "p", "x.ncz")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(kok, recursive: true);
        }
    }
}
