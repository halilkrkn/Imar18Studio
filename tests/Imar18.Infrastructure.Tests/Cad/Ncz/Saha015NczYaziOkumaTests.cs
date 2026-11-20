using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// SAHA-015 (SORU-044): Yaklaşık Yer Krokisi <c>.NCZ</c> için tip 5 yazı kayıtları (ekleme noktası Y, X + Windows-1254 metin + sınır
/// kutusu merkezi) ve 2 köşeli açık bölme çizgileri (<c>closed=False</c>). Tüm girdiler sentetiktir (<see cref="NczSentetikDosya"/>);
/// adlar uydurmadır, koordinatlar gerçek konum içermez. Beklenen değerler elle hesaplanır (kutu merkezi = (min + max) / 2).
/// </summary>
public sealed class Saha015NczYaziOkumaTests
{
    private const double X0 = 4_000_000.0;
    private const double Y0 = 500_000.0;

    private static readonly NczFormatAdapter Adaptor = new();

    private static CadLayerDocument Oku(NczSentetikDosya dosya)
    {
        using var akis = dosya.Akis();
        var sonuc = Adaptor.Read(akis);
        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        return sonuc.Value;
    }

    private static (double, double, double)[] Kare(double k0, double d0, double kenar) =>
        [(k0, d0, 0), (k0 + kenar, d0, 0), (k0 + kenar, d0 + kenar, 0), (k0, d0 + kenar, 0), (k0, d0, 0)];

    [Fact]
    public void NczYazi_TIP5_Kaydi_Ekleme_Noktasi_Turkce_Metin_Katman_Ve_Kutu_Merkeziyle_Okunmali()
    {
        var belge = Oku(new NczSentetikDosya().Katman("0").Katman("YAZI")
            .YaziTip5(1, "ÖRNEK MALİK ÇĞŞ", (X0 + 10.25, Y0 + 20.5), (X0 + 10.25, Y0 + 19.5, X0 + 14.75, Y0 + 40.5), donukluk: 0.785f));

        var y = belge.Texts.Should().ContainSingle().Subject;
        y.Text.Should().Be("ÖRNEK MALİK ÇĞŞ", "Windows-1254 Türkçe harfler korunur");
        y.LayerName.Should().Be("YAZI");
        y.Position.Should().Be(new GeodeticPoint(X: 4_000_010.25m, Y: 500_020.5m), "ham kayıt (Kuzey, Doğu) → jeodezik X = Kuzey, Y = Doğu");
        y.Merkez.Should().Be(new GeodeticPoint(X: 4_000_012.5m, Y: 500_030m), "döndürülmüş yazının merkezi sınır kutusunun ortasıdır");
        belge.SkippedEntityCount.Should().Be(0, "çözülen yazı atlanmış sayılmaz");
        belge.Polylines.Should().BeEmpty();
    }

    [Fact]
    public void NczYazi_Kroki_Duzeni_2_Blok_6_Acik_Bolme_Cizgisi_Ve_8_Yazi_Eksiksiz_Okunmali()
    {
        var d = new NczSentetikDosya().Katman("0").Katman("02_PARSEL_ALN_YENI").Katman("YOL").Katman("TK_OLUSAN")
            .Poligon(1, "K1", Kare(X0, Y0, 100))
            .Poligon(2, "YOL", [(X0 + 40, Y0, 0), (X0 + 46, Y0, 0), (X0 + 46, Y0 + 100, 0), (X0 + 40, Y0 + 100, 0), (X0 + 40, Y0, 0)])
            .Poligon(3, "2", [(X0 + 46, Y0, 0), (X0 + 100, Y0, 0), (X0 + 100, Y0 + 100, 0), (X0 + 46, Y0 + 100, 0), (X0 + 46, Y0, 0)])
            .Poligon(3, "1", [(X0, Y0, 0), (X0 + 40, Y0, 0), (X0 + 40, Y0 + 100, 0), (X0, Y0 + 100, 0), (X0, Y0, 0)]);
        foreach (var dogu in new[] { 33.0, 66.0 })
        {
            d.Poligon(0, "BOLME", [(X0 + 46, Y0 + dogu, 0), (X0 + 100, Y0 + dogu + 2, 0)]);
        }

        foreach (var dogu in new[] { 20.0, 40.0, 60.0, 80.0 })
        {
            d.Poligon(0, "BOLME", [(X0, Y0 + dogu, 0), (X0 + 40, Y0 + dogu + 1, 0)]);
        }

        var yazilar = new[] { "MALİK BİR", "MALİK İKİ", "MALİK ÜÇ", "MALİK DÖRT", "MALİK BEŞ", "MALİK ALTI", "MALİK YEDİ", "MALİK SEKİZ" };
        for (var i = 0; i < yazilar.Length; i++)
        {
            d.YaziTip5(0, yazilar[i], (X0 + 50 + i, Y0 + 5 + (10 * i)), donukluk: 0.6f);
        }

        var belge = Oku(d);

        belge.Polylines.Count(p => p.IsClosed).Should().Be(4, "1 kadastro + 1 yol + 2 TK_OLUSAN bloğu");
        belge.Polylines.Count(p => p.LayerName == "TK_OLUSAN" && p.IsClosed).Should().Be(2);
        var acik = belge.Polylines.Where(p => !p.IsClosed).ToList();
        acik.Should().HaveCount(6).And.OnlyContain(p => p.LayerName == "0" && p.Vertices.Count == 2);
        belge.Texts.Select(t => t.Text).Should().Equal(yazilar, "8 tip 5 yazısı kayıt sırasıyla okunur");
        belge.Texts.Should().OnlyContain(t => t.Merkez != null);
        belge.SkippedEntityCount.Should().Be(0);
    }

    [Fact]
    public void NczYazi_Uzunlugu_Kayit_Boyuyla_Uyusmayan_Yazi_Dosyayi_Dusurmemeli_Atlanip_Sayilmali()
    {
        var belge = Oku(new NczSentetikDosya().Katman("0")
            .Poligon(0, "K1", Kare(X0, Y0, 10))
            .YaziTip5(0, "BOZUK", (X0 + 1, Y0 + 1), uzunlukOverride: 40)
            .YaziTip5(0, "FAZLA", (X0 + 2, Y0 + 2), fazlaBayt: 3));

        belge.Polylines.Should().ContainSingle("yardımcı yazı bozukluğu geometri okumasını düşürmez");
        belge.Texts.Should().BeEmpty();
        belge.SkippedEntityCount.Should().Be(2, "çözülemeyen yazılar sessiz kaybolmaz, atlanan sayılır");
    }

    [Fact]
    public void NczYazi_Genisletilmis_0x16_Yazi_Kaydi_Farkli_Duzende_Oldugu_Icin_Atlanmali()
    {
        var belge = Oku(new NczSentetikDosya().Katman("0").YaziTip5(0, "GENİŞ", (X0, Y0), bayrak: 0x16));

        belge.Texts.Should().BeEmpty("spike §2.4: 0x16 yazı kayıtları farklı düzendedir");
        belge.SkippedEntityCount.Should().Be(1);
    }

    [Fact]
    public void NczYazi_Sonlu_Olmayan_Ekleme_Noktasi_Atlanmali()
    {
        var belge = Oku(new NczSentetikDosya().Katman("0").YaziTip5(0, "NAN", (double.NaN, Y0)).YaziTip5(0, "SONSUZ", (X0, double.PositiveInfinity)));

        belge.Texts.Should().BeEmpty();
        belge.SkippedEntityCount.Should().Be(2);
    }

    [Fact]
    public void NczYazi_Katman_Indeksi_Aralik_Disindaysa_Atlanmali()
    {
        var belge = Oku(new NczSentetikDosya().Katman("0").YaziTip5(0, "KATMANSIZ", (X0, Y0), katmanAlaniOverride: 7));

        belge.Texts.Should().BeEmpty();
        belge.SkippedEntityCount.Should().Be(1);
    }

    [Fact]
    public void NczYazi_Sinir_Kutusu_Gecersizse_Merkez_Bos_Konum_Okunmali()
    {
        var belge = Oku(new NczSentetikDosya().Katman("0")
            .YaziTip5(0, "TERS KUTU", (X0 + 1, Y0 + 2), (X0 + 5, Y0 + 2, X0 + 1, Y0 + 9))
            .YaziTip5(0, "NAN KUTU", (X0 + 1, Y0 + 2), (double.NaN, Y0, X0 + 1, Y0 + 1)));

        belge.Texts.Should().HaveCount(2).And.OnlyContain(t => t.Merkez == null && t.Position == new GeodeticPoint(4_000_001m, 500_002m));
    }

    [Fact]
    public void NczYazi_Ekleme_Noktasi_Sinir_Kutusu_Disindaysa_Merkez_Kullanilmamali()
    {
        // Architect O3: ekleme noktası döndürülmüş yazıda da kutunun içindedir; 20 m dışarıdaki kutu bozuk sayılır.
        var belge = Oku(new NczSentetikDosya().Katman("0")
            .YaziTip5(0, "UZAK KUTU", (X0 + 1, Y0 + 1), (X0 + 21, Y0 + 21, X0 + 23, Y0 + 30)));

        var y = belge.Texts.Should().ContainSingle().Subject;
        y.Merkez.Should().BeNull();
        y.Capa.Should().Be(y.Position);
    }

    [Fact]
    public void NczYazi_Bos_Metinli_Yazi_Okunmali_Ancak_Bos_Metin_Tasimali()
    {
        var belge = Oku(new NczSentetikDosya().Katman("0").YaziTip5(0, string.Empty, (X0, Y0)));

        belge.Texts.Should().ContainSingle().Which.Text.Should().BeEmpty();
    }

    [Fact]
    public void NczYazi_Ileri_Donusum_Yazi_Konumunu_Ve_Merkezini_Ayni_Katsayilarla_Donusturmeli()
    {
        var belge = Oku(new NczSentetikDosya().Katman("0")
            .YaziTip5(0, "MALİK", (X0 + 10, Y0 + 20), (X0 + 10, Y0 + 20, X0 + 12, Y0 + 30)));
        var k = new DonusumKatsayilari(A: 1m, B: 0m, C: 0m, D: 1m, Cy: 0.25m, Cx: -0.5m);

        var donmus = CadBelgesiDonusumu.Uygula(belge, k).Texts.Single();

        donmus.Position.Should().Be(new GeodeticPoint(X: 4_000_009.5m, Y: 500_020.25m));
        donmus.Merkez.Should().Be(new GeodeticPoint(X: 4_000_010.5m, Y: 500_025.25m));
    }

    [Fact]
    public void NczAnonimlestirici_TIP5_Yazi_Metni_Anonim_Ciktida_Bulunmamali()
    {
        var kaynak = new NczSentetikDosya().Katman("TK_PARSEL_GİREN")
            .Poligon(0, "K1", Kare(X0, Y0, 10))
            .YaziTip5(0, "UYDURMA KİŞİ ADI", (X0 + 1, Y0 + 1))
            .Olustur();

        var sonuc = NczAnonimlestirici.Anonimlestir(kaynak, new NczAnonimlestirmeAyarlari(-3_000_000.000m, -200_000.000m));

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.Icerik.AsSpan().IndexOf(System.Text.Encoding.Latin1.GetBytes("UYDURMA")).Should().Be(-1);
        Adaptor.Read(new MemoryStream(sonuc.Value.Icerik, writable: false)).Value.Texts.Should().BeEmpty("anonim çıktı yalnız poligon taşır");
    }
}
