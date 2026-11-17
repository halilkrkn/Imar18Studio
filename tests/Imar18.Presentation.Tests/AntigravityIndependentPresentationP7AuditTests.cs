using System.Globalization;
using Avalonia.Styling;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.Gorunum;
using Imar18.Presentation.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// Antigravity Bağımsız Denetim ve Sınır Testleri (Faz P7 — UI Revizyonu).
/// Kapsam:
/// 1. CadSceneRenderer: poligon-etiket hizalaması, L/konkav parsel centroid doğrulaması, perde (swipe) gizleme sınırları.
/// 2. OsnapCozucu: Uç, Kesişim, Orta, Dik öncelik hiyerarşisi, dik ayak kenar sınırı, paralel kenarlar ve mm hassasiyeti.
/// 3. TurkceAdlar ve TurkceAdConverter: %100 Türkçe enum dönüşümleri, DOM meridyen formatı, kısa revizyon rozeti.
/// 4. Koyu Stüdyo / Açık Pafta tema geçişi ve ViewModel senkronizasyonu.
/// 5. 5 sekmeli canlı cetvel ve bağlama göre sağ panel görünürlüğü/parsel işlemleri filtresi.
/// </summary>
public sealed class AntigravityIndependentPresentationP7AuditTests
{
    private static GeodeticPoint P(decimal x, decimal y) => new(X: x, Y: y);

    private sealed class SahteAdaptor : ICadFormatAdapter
    {
        public CadFormat Format => CadFormat.Ncz;
        public bool SupportsExtension(string extension) => true;
        public Imar18.Domain.Diagnostics.Result<CadLayerDocument> Read(Stream source, CancellationToken cancellationToken = default) =>
            Imar18.Domain.Diagnostics.Result<CadLayerDocument>.Success(new CadLayerDocument(CadFormat.Ncz, [], [], [], 0, 0, 0, []));
    }

    private static CanvasScene Sahne(params CadPolyline[] polylines) =>
        CanvasScene.From(new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "TK_PARSEL_GİREN"), new CadLayer(1, "TK_PARSEL_OLUŞAN")], polylines, [], 0, 0, 0, []), CadLayerRoleMap.Standard);

    #region 1. CadSceneRenderer (Poligon-Etiket Hizalaması ve Görünürlük Sınır Testleri)

    [Fact]
    public void CadSceneRenderer_Farkli_Oteleme_Ve_Olcekte_Etiket_Poligon_Merkezine_Tam_Hizalanmali()
    {
        // 100 m x 50 m dikdörtgen imar parseli: (4000000, 500000) -> (4000100, 5000050)
        // Alan merkezi: X = 4000050 m (Kuzey), Y = 5000025 m (Doğu)
        var polyline = new CadPolyline(
            "TK_PARSEL_OLUŞAN",
            "301/1",
            null,
            [P(4_000_000m, 500_000m), P(4_000_100m, 500_000m), P(4_000_100m, 500_050m), P(4_000_000m, 500_050m), P(4_000_000m, 500_000m)],
            true,
            null);

        var vm = new CadCanvasViewModel { IzgaraGoster = false, EtiketGoster = true };
        vm.SetViewport(1000, 800);
        vm.LoadScene(Sahne(polyline));

        // Rastgele pan ve zoom uygula
        vm.Pan(125, -75);
        vm.ZoomAt(500, 400, 1.5);

        var durum = CadRenderState.From(vm, 1000, 800);
        var etiketler = CadSceneRenderer.EtiketKonumlari(durum);

        etiketler.Should().ContainSingle();
        var etiket = etiketler[0];
        etiket.Metin.Should().Be("301/1");
        etiket.Kadastro.Should().BeFalse();

        // Kamera dönüşümü ile etiket konumu birebir aynı olmalı
        var beklenenEkran = vm.Camera.WorldToScreen(P(4_000_050m, 500_025m));
        etiket.X.Should().BeApproximately((float)beklenenEkran.X, 0.5f);
        etiket.Y.Should().BeApproximately((float)beklenenEkran.Y, 0.5f);
    }

    [Fact]
    public void CadSceneRenderer_Konkav_L_Adada_Etiket_Poligonun_Agirlik_Merkezinde_Olmali()
    {
        // L şeklinde parsel (kuzey: X, doğu: Y):
        // (0,0) -> (0,60) -> (30,60) -> (30,30) -> (60,30) -> (60,0) -> (0,0)
        // İki dikdörtgenden oluşur: Alt (30 x 60, merkez 15, 30, alan 1800) + Üst (30 x 30, merkez 45, 15, alan 900)
        // Toplam alan: 2700 m²
        // Centroid X: (1800 * 15 + 900 * 45) / 2700 = (27000 + 40500) / 2700 = 67500 / 2700 = 25 m
        // Centroid Y: (1800 * 30 + 900 * 15) / 2700 = (54000 + 13500) / 2700 = 67500 / 2700 = 25 m
        var lParsel = new CadPolyline(
            "TK_PARSEL_OLUŞAN",
            "L-PARSEL",
            null,
            [P(0, 0), P(0, 60), P(30, 60), P(30, 30), P(60, 30), P(60, 0), P(0, 0)],
            true,
            null);

        var vm = new CadCanvasViewModel { IzgaraGoster = false, EtiketGoster = true };
        vm.SetViewport(800, 600);
        vm.LoadScene(Sahne(lParsel));

        var durum = CadRenderState.From(vm, 800, 600);
        var etiketler = CadSceneRenderer.EtiketKonumlari(durum);

        etiketler.Should().ContainSingle();
        var beklenen = vm.Camera.WorldToScreen(P(25, 25));
        etiketler[0].X.Should().BeApproximately((float)beklenen.X, 0.5f);
        etiketler[0].Y.Should().BeApproximately((float)beklenen.Y, 0.5f);
    }

    [Fact]
    public void CadSceneRenderer_Ekranda_Metinden_Kucuk_Kalan_Poligon_Etiketi_Gizlenmeli()
    {
        // Çok küçük parsel: 1m x 1m. Uzak zoom'da piksel boyutu < 10 px olur.
        var minikParsel = new CadPolyline(
            "TK_PARSEL_OLUŞAN",
            "COK_UZUN_PARSEL_NUMARASI_123456789",
            null,
            [P(0, 0), P(0, 1), P(1, 1), P(1, 0), P(0, 0)],
            true,
            null);

        var vm = new CadCanvasViewModel { IzgaraGoster = true, EtiketGoster = true };
        vm.SetViewport(800, 600);
        vm.LoadScene(Sahne(minikParsel));

        // Aşırı uzaklaş (scale çok küçük olsun)
        vm.ZoomAt(400, 300, 0.005);

        var durum = CadRenderState.From(vm, 800, 600);
        var etiketler = CadSceneRenderer.EtiketKonumlari(durum);

        etiketler.Should().BeEmpty("ekranda metinden küçük kalan parselin etiketi kalabalık yapmaması için gizlenmeli");
    }

    [Fact]
    public void CadSceneRenderer_Perde_Kaydirildiginda_Imar_Etiketleri_Perde_Saginda_Gizlenmeli_Kadastro_Etiketi_Gorunmeli()
    {
        // Batıda (Y = 10..30) ve Doğuda (Y = 70..90) iki parsel
        var kadastroBati = new CadPolyline(
            "TK_PARSEL_GİREN",
            "KAD-BATI",
            null,
            [P(0, 10), P(0, 30), P(40, 30), P(40, 10), P(0, 10)],
            true,
            null);

        var imarDogu = new CadPolyline(
            "TK_PARSEL_OLUŞAN",
            "IMAR-DOGU",
            null,
            [P(0, 70), P(0, 90), P(40, 90), P(40, 70), P(0, 70)],
            true,
            null);

        var vm = new CadCanvasViewModel { IzgaraGoster = false, EtiketGoster = true };
        vm.SetViewport(800, 600);
        vm.LoadScene(Sahne(kadastroBati, imarDogu));

        // Perdeyi %20'ye çek (yalnızca batı tarafı imar gösterir, doğu kadastro tarafıdır)
        vm.SwipePercent = 20;

        var durum = CadRenderState.From(vm, 800, 600);
        var etiketler = CadSceneRenderer.EtiketKonumlari(durum);

        etiketler.Should().Contain(e => e.Metin == "KAD-BATI" && e.Kadastro);
        etiketler.Should().NotContain(e => e.Metin == "IMAR-DOGU", "perdenin sağında kalan imar etiketleri çizilmemelidir");
    }

    [Fact]
    public void CadSceneRenderer_EtiketGoster_False_Oldugunda_Hicbir_Etiket_Uretilmemeli()
    {
        var parsel = new CadPolyline(
            "TK_PARSEL_OLUŞAN",
            "101/1",
            null,
            [P(0, 0), P(0, 100), P(100, 100), P(100, 0), P(0, 0)],
            true,
            null);

        var vm = new CadCanvasViewModel { IzgaraGoster = false, EtiketGoster = false };
        vm.SetViewport(800, 600);
        vm.LoadScene(Sahne(parsel));

        var durum = CadRenderState.From(vm, 800, 600);
        var etiketler = CadSceneRenderer.EtiketKonumlari(durum);

        etiketler.Should().BeEmpty();
    }

    #endregion

    #region 2. OsnapCozucu (Uç, Orta, Dik, Kesişim ve Öncelik Hiyerarşisi Sınır Testleri)

    [Fact]
    public void OsnapCozucu_Uc_Kesisim_Orta_Dik_Hiyerarsisi_Dogrulanmali()
    {
        // Kenar 1: (0, 0) -> (10, 10) (Orta nokta: 5, 5)
        // Kenar 2: (0, 10) -> (10, 0) (Orta nokta: 5, 5)
        // Bu iki kenar tam (5, 5) noktasında kesişir!
        // (5, 5) noktası hem Kenar 1'in ortası, hem Kenar 2'nin ortası, hem de Kesişim noktasıdır.
        var k1 = new CadPolyline("TK_PARSEL_OLUŞAN", "K1", null, [P(0, 0), P(10, 10)], false, null);
        var k2 = new CadPolyline("TK_PARSEL_OLUŞAN", "K2", null, [P(0, 10), P(10, 0)], false, null);
        var sahne = Sahne(k1, k2);

        // Durum A: Kesişim ve Orta aynı anda açık -> Kesişim (sıra 1) Orta'dan (sıra 2) öncelikli olmalı!
        var sonucKesisim = OsnapCozucu.Coz(sahne, P(5.05m, 4.95m), 1m, OsnapKipi.Orta | OsnapKipi.Kesisim);
        sonucKesisim.Should().NotBeNull();
        sonucKesisim!.Kip.Should().Be(OsnapKipi.Kesisim, "Kesişim, Kenar Ortasından daha önceliklidir");
        sonucKesisim.Nokta.Should().Be(P(5, 5));

        // Durum B: Uç ve Kesişim aynı anda açık. İmleç hem köşe (0, 0) hem başka bir kesişime yakınsa Uç (sıra 0) Kesişim'den (sıra 1) önceliklidir.
        var k3 = new CadPolyline("TK_PARSEL_OLUŞAN", "K3", null, [P(0, 0), P(0, 5)], false, null);
        var sahne2 = Sahne(k1, k3); // (0, 0) noktası hem k1'in hem k3'ün uç noktasıdır.
        var sonucUc = OsnapCozucu.Coz(sahne2, P(0.05m, 0.05m), 1m, OsnapKipi.Uc | OsnapKipi.Kesisim);
        sonucUc.Should().NotBeNull();
        sonucUc!.Kip.Should().Be(OsnapKipi.Uc, "Uç nokta, Kesişim noktasından daha önceliklidir");
        sonucUc.Nokta.Should().Be(P(0, 0));

        // Durum C: Orta ve Dik aynı anda açık.
        // Kenar: (0, 0) -> (0, 10). Orta nokta: (0, 5).
        // İmleç (0.1, 5) noktasındayken dik ayak da (0, 5) çıkar, orta nokta da (0, 5) çıkar.
        var kDikey = new CadPolyline("TK_PARSEL_OLUŞAN", "KD", null, [P(0, 0), P(0, 10)], false, null);
        var sahneDikey = Sahne(kDikey);
        var sonucOrta = OsnapCozucu.Coz(sahneDikey, P(0.1m, 5m), 1m, OsnapKipi.Orta | OsnapKipi.Dik);
        sonucOrta.Should().NotBeNull();
        sonucOrta!.Kip.Should().Be(OsnapKipi.Orta, "Orta nokta, Dik ayaktan daha önceliklidir");
        sonucOrta.Nokta.Should().Be(P(0, 5));
    }

    [Fact]
    public void OsnapCozucu_Kenar_Disina_Dusen_Izdusumde_Dik_Ayak_Yakalanmamali()
    {
        // Kenar: (0, 0) -> (0, 10)
        // İmleç: (2, 15) noktasında. Y = 15 kenarın bittiği Y = 10'un dışındadır (t = 1.5 > 1).
        var k = new CadPolyline("TK_PARSEL_OLUŞAN", "K", null, [P(0, 0), P(0, 10)], false, null);
        var sahne = Sahne(k);

        var sonuc = OsnapCozucu.Coz(sahne, P(2m, 15m), 5m, OsnapKipi.Dik);
        sonuc.Should().BeNull("doğru parçasının dışına düşen dik izdüşümler yakalanmamalıdır");
    }

    [Fact]
    public void OsnapCozucu_Paralel_Veya_Cakisik_Kenarlarda_Kesisim_Uretmemeli()
    {
        // İki paralel kenar:
        var k1 = new CadPolyline("TK_PARSEL_OLUŞAN", "K1", null, [P(0, 0), P(10, 0)], false, null);
        var k2 = new CadPolyline("TK_PARSEL_OLUŞAN", "K2", null, [P(0, 2), P(10, 2)], false, null);
        var sahne = Sahne(k1, k2);

        var sonuc = OsnapCozucu.Coz(sahne, P(5m, 1m), 3m, OsnapKipi.Kesisim);
        sonuc.Should().BeNull("paralel kenarlar kesişmez");
    }

    [Fact]
    public void OsnapCozucu_Sifir_Veya_Negatif_Toleransta_Null_Donmeli()
    {
        var k = new CadPolyline("TK_PARSEL_OLUŞAN", "K", null, [P(0, 0), P(10, 10)], false, null);
        var sahne = Sahne(k);

        OsnapCozucu.Coz(sahne, P(0, 0), 0m, OsnapKipi.Uc).Should().BeNull();
        OsnapCozucu.Coz(sahne, P(0, 0), -1m, OsnapKipi.Uc).Should().BeNull();
        OsnapCozucu.Coz(sahne, P(0, 0), 1m, OsnapKipi.Yok).Should().BeNull();
    }

    [Fact]
    public void OsnapCozucu_Milimetre_Yuvarlamasi_Ve_AwayFromZero_Hassasiyeti()
    {
        // Orta nokta tam 0.0005 m fark üreten bir kenar
        // A = (0, 0), B = (0.001m, 0.001m) -> Orta = (0.0005m, 0.0005m)
        // AwayFromZero ile 3 ondalık yuvarlama (0.001m, 0.001m) olmalı
        var k = new CadPolyline("TK_PARSEL_OLUŞAN", "K", null, [P(0, 0), P(0.001m, 0.001m)], false, null);
        var sahne = Sahne(k);

        var sonuc = OsnapCozucu.Coz(sahne, P(0.0005m, 0.0005m), 1m, OsnapKipi.Orta);
        sonuc.Should().NotBeNull();
        sonuc!.Nokta.X.Should().Be(0.001m);
        sonuc.Nokta.Y.Should().Be(0.001m);
    }

    #endregion

    #region 3. TurkceGorunenAdlar ve TurkceAdConverter (%100 Türkçe Enum Dönüşümü)

    [Fact]
    public void TurkceAdlar_Tum_Enum_Degerlerini_Eksiksiz_Turkceye_Cevirmeli()
    {
        // 1. CadLayerRole
        TurkceAdlar.Ad(CadLayerRole.Unassigned).Should().Be("Atanmamış");
        TurkceAdlar.Ad(CadLayerRole.CadastralParcel).Should().Be("Kadastro Parseli");
        TurkceAdlar.Ad(CadLayerRole.ZoningParcel).Should().Be("İmar Parseli");
        TurkceAdlar.Ad(CadLayerRole.RoadDedication).Should().Be("Yola Terk");
        TurkceAdlar.Ad(CadLayerRole.RegulationBoundary).Should().Be("Düzenleme Sınırı");
        TurkceAdlar.Ad(CadLayerRole.BlockEdge).Should().Be("Ada Kenarı");
        TurkceAdlar.Ad(CadLayerRole.Park).Should().Be("Park Alanı");
        TurkceAdlar.Ad(CadLayerRole.CadastralLine).Should().Be("Kadastro Hattı");
        TurkceAdlar.Ad(CadLayerRole.NewParcelPoint).Should().Be("Yeni Parsel Noktası");
        TurkceAdlar.Ad(CadLayerRole.RegisteredPoint).Should().Be("Tescilli Nokta");
        TurkceAdlar.Ad((CadLayerRole)999).Should().Be("Bilinmeyen Rol");

        // 2. GeodetikDatum
        TurkceAdlar.Ad(GeodetikDatum.Itrf96).Should().Be("ITRF96 (TM 3°)");
        TurkceAdlar.Ad(GeodetikDatum.Ed50).Should().Be("ED50 (TM 3° · Hayford)");
        TurkceAdlar.Ad((GeodetikDatum)999).Should().Be("Bilinmeyen Datum");

        // 3. KoordinatKalitesi
        TurkceAdlar.Ad(KoordinatKalitesi.ArsivOnayli).Should().Be("Arşiv Onaylı");
        TurkceAdlar.Ad(KoordinatKalitesi.Gecici).Should().Be("Geçici Koordinatlı");
        TurkceAdlar.Ad((KoordinatKalitesi)999).Should().Be("Bilinmeyen Kalite");

        // 4. CiktiTuru
        TurkceAdlar.Ad(CiktiTuru.Cetveller).Should().Be("Excel Cetvelleri");
        TurkceAdlar.Ad(CiktiTuru.FenKlasoru).Should().Be("Fen Klasörü");
        TurkceAdlar.Ad(CiktiTuru.CksDopOzeti).Should().Be("CKS DOP Özeti");
        TurkceAdlar.Ad((CiktiTuru)999).Should().Be("Bilinmeyen Çıktı");

        // 5. CadFormat
        TurkceAdlar.Ad(CadFormat.Ncz).Should().Be("NCZ (Netcad)");
        TurkceAdlar.Ad(CadFormat.Dxf).Should().Be("DXF (AutoCAD)");
        TurkceAdlar.Ad(CadFormat.Dwg).Should().Be("DWG (AutoCAD)");
        TurkceAdlar.Ad((CadFormat)999).Should().Be("Bilinmeyen Biçim");
    }

    [Fact]
    public void TurkceAdlar_OrtaMeridyen_Ve_KisaRevizyon_Sinir_Degerleri()
    {
        TurkceAdlar.OrtaMeridyen(30).Should().Be("DOM 30°");
        TurkceAdlar.OrtaMeridyen(33).Should().Be("DOM 33°");

        TurkceAdlar.KisaRevizyon(null).Should().Be("—");
        TurkceAdlar.KisaRevizyon(string.Empty).Should().Be("—");
        TurkceAdlar.KisaRevizyon("abcd").Should().Be("abcd");
        TurkceAdlar.KisaRevizyon("12345678").Should().Be("12345678");
        TurkceAdlar.KisaRevizyon("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855").Should().Be("e3b0c442…");
    }

    [Fact]
    public void TurkceAdConverter_Parametreli_Ve_Tek_Yonlu_Calismali()
    {
        var conv = TurkceAdConverter.Ornek;

        conv.Convert(36, typeof(string), "meridyen", CultureInfo.InvariantCulture).Should().Be("DOM 36°");
        conv.Convert(CadLayerRole.ZoningParcel, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("İmar Parseli");
        conv.Convert(null, typeof(string), null, CultureInfo.InvariantCulture).Should().Be(string.Empty);
        conv.Convert(123.45m, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("123.45");

        var act = () => conv.ConvertBack("İmar Parseli", typeof(CadLayerRole), null, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>().WithMessage("*tek yönlüdür*");
    }

    #endregion

    #region 4. Koyu Stüdyo / Açık Pafta Tema Geçişi ve ViewModel Senkronizasyonu

    [Fact]
    public void CadCanvasViewModel_AcikPafta_Degistiginde_RenderVersion_Artmali()
    {
        var vm = new CadCanvasViewModel();
        var v1 = vm.RenderVersion;

        vm.AcikPafta = true;
        vm.RenderVersion.Should().BeGreaterThan(v1);

        var durum = CadRenderState.From(vm, 800, 600);
        durum.AcikPafta.Should().BeTrue();
    }

    [Fact]
    public void MainWindowViewModel_TemaDegistir_Komutu_Canvas_Ile_Senkron_Calismali()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor()]));

        vm.AcikPafta.Should().BeFalse();
        vm.Canvas.AcikPafta.Should().BeFalse();
        vm.TemaEtiketi.Should().Be("☀️ Açık Pafta");

        vm.TemaDegistirCommand.Execute(null);

        vm.AcikPafta.Should().BeTrue();
        vm.Canvas.AcikPafta.Should().BeTrue();
        vm.TemaEtiketi.Should().Be("🌙 Koyu Stüdyo");

        vm.TemaDegistirCommand.Execute(null);

        vm.AcikPafta.Should().BeFalse();
        vm.Canvas.AcikPafta.Should().BeFalse();
        vm.TemaEtiketi.Should().Be("☀️ Açık Pafta");
    }

    #endregion

    #region 5. 5 Sekmeli Canlı Cetvel ve Sağ Panel Görünürlük Sınır Testleri

    [Fact]
    public void MainWindowViewModel_Sol_Sag_Alt_Paneller_Bagimsiz_Acilip_Kapanabilmeli()
    {
        var vm = new MainWindowViewModel(new CadImportService([new SahteAdaptor()]));

        vm.SolPanelAcik.Should().BeTrue();
        vm.SagPanelAcik.Should().BeTrue();
        vm.AltPanelAcik.Should().BeTrue();
        vm.AltPanelEtiketi.Should().Be("▼ Cetveli Gizle");

        vm.SolPanelDegistirCommand.Execute(null);
        vm.SolPanelAcik.Should().BeFalse();

        vm.SagPanelDegistirCommand.Execute(null);
        vm.SagPanelAcik.Should().BeFalse();

        vm.AltPanelDegistirCommand.Execute(null);
        vm.AltPanelAcik.Should().BeFalse();
        vm.AltPanelEtiketi.Should().Be("▲ Cetveli Göster");

        // Geri açma
        vm.AltPanelDegistirCommand.Execute(null);
        vm.AltPanelAcik.Should().BeTrue();
        vm.AltPanelEtiketi.Should().Be("▼ Cetveli Gizle");
    }

    [Fact]
    public void CadCanvasViewModel_ParselIslemleri_Yalnizca_Gercek_Komuta_Bagli_Ogeleri_Listelemeli()
    {
        var vm = new CadCanvasViewModel();

        // Toplam 9 menü öğesi var
        vm.ContextMenuItems.Should().HaveCount(9);

        // ParselIslemleri listesinde yalnızca Bagli == true olan 4 komut olmalıdır:
        // cutHandle, zoomSelection, clearSelection, fitAll
        vm.ParselIslemleri.Should().HaveCount(4);
        vm.ParselIslemleri.Select(m => m.Id).Should().Equal("cutHandle", "zoomSelection", "clearSelection", "fitAll");

        // Fen klasörü bağlandığında ParselIslemleri 5 öğeye çıkmalı
        var sahteRelay = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        vm.BaglaFenKlasoru(sahteRelay);

        vm.ParselIslemleri.Should().HaveCount(5);
        vm.ParselIslemleri.Select(m => m.Id).Should().Equal("fenPreview", "cutHandle", "zoomSelection", "clearSelection", "fitAll");
    }

    #endregion
}
