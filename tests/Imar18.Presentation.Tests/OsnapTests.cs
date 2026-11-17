using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.ViewModels;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P7 OSNAP (nesne yakalama) çubuğu: Uç, Orta, Dik 90° ve Kesişim. Yakalama yalnız imleç koordinatını ve işaretini
/// etkiler; geometriye yazmaz. Beklenen noktalar elle hesaplanmıştır (X = Kuzey, Y = Doğu).
/// </summary>
public sealed class OsnapTests
{
    private static GeodeticPoint P(decimal x, decimal y) => new(X: x, Y: y);

    private static CanvasScene Sahne(params CadPolyline[] p) =>
        CanvasScene.From(new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "TK_PARSEL_OLUŞAN")], p, [], 0, 0, 0, []), CadLayerRoleMap.Standard);

    private static CadPolyline Cizgi(params GeodeticPoint[] v) => new("TK_PARSEL_OLUŞAN", string.Empty, null, v, false, null);

    [Fact]
    public void Osnap_Uc_Kipinde_En_Yakin_Koseye_Yakalamali()
    {
        var s = Sahne(Cizgi(P(0, 0), P(0, 10)));

        var r = OsnapCozucu.Coz(s, P(0.3m, 0.2m), 1m, OsnapKipi.Uc);

        r.Should().NotBeNull();
        r!.Nokta.Should().Be(P(0, 0));
        r.Kip.Should().Be(OsnapKipi.Uc);
    }

    [Fact]
    public void Osnap_Orta_Kipinde_Kenar_Orta_Noktasina_Yakalamali()
    {
        var s = Sahne(Cizgi(P(0, 0), P(0, 10)));

        var r = OsnapCozucu.Coz(s, P(0.2m, 5.1m), 1m, OsnapKipi.Uc | OsnapKipi.Orta);

        r!.Nokta.Should().Be(P(0, 5));
        r.Kip.Should().Be(OsnapKipi.Orta);
    }

    [Fact]
    public void Osnap_Dik_Kipinde_Kenara_Dik_Ayak_Noktasina_Yakalamali()
    {
        var s = Sahne(Cizgi(P(0, 0), P(0, 10)));

        var r = OsnapCozucu.Coz(s, P(0.4m, 3m), 1m, OsnapKipi.Dik);

        r!.Nokta.Should().Be(P(0, 3));
        r.Kip.Should().Be(OsnapKipi.Dik);
    }

    [Fact]
    public void Osnap_Kesisim_Kipinde_Iki_Kenarin_Kesisimine_Yakalamali()
    {
        var s = Sahne(Cizgi(P(0, 0), P(10, 10)), Cizgi(P(0, 10), P(10, 0)));

        var r = OsnapCozucu.Coz(s, P(5.2m, 4.9m), 1m, OsnapKipi.Kesisim);

        r!.Nokta.Should().Be(P(5, 5));
        r.Kip.Should().Be(OsnapKipi.Kesisim);
    }

    [Fact]
    public void Osnap_Uc_Orta_Ve_Dik_Birlikte_Aciksa_Uc_Oncelikli_Olmali()
    {
        var s = Sahne(Cizgi(P(0, 0), P(0, 1)));

        var r = OsnapCozucu.Coz(s, P(0.1m, 0.4m), 1m, OsnapKipi.Uc | OsnapKipi.Orta | OsnapKipi.Dik);

        r!.Kip.Should().Be(OsnapKipi.Uc);
        r.Nokta.Should().Be(P(0, 0));
    }

    [Fact]
    public void Osnap_Tolerans_Disinda_Veya_Kip_Kapaliyken_Yakalamamali()
    {
        var s = Sahne(Cizgi(P(0, 0), P(0, 10)));

        OsnapCozucu.Coz(s, P(5m, 5m), 1m, OsnapKipi.Uc | OsnapKipi.Orta | OsnapKipi.Dik | OsnapKipi.Kesisim).Should().BeNull();
        OsnapCozucu.Coz(s, P(0.1m, 0.1m), 1m, OsnapKipi.Yok).Should().BeNull();
        OsnapCozucu.Coz(CanvasScene.Empty, P(0, 0), 1m, OsnapKipi.Uc).Should().BeNull();
        OsnapCozucu.Coz(s, P(0, 0), 0m, OsnapKipi.Uc).Should().BeNull("sıfır tolerans geçersiz");
    }

    [Fact]
    public void Osnap_Tuval_Imlec_Metnine_Yakalanan_Noktayi_Ve_Kipi_Yazmali()
    {
        var vm = new CadCanvasViewModel();
        vm.SetViewport(400, 400);
        vm.LoadScene(Sahne(Cizgi(P(4_000_000m, 500_000m), P(4_000_000m, 500_100m))));
        vm.OsnapUc = true;
        var uc = vm.Camera.WorldToScreen(P(4_000_000m, 500_000m));

        vm.PointerMoved(uc.X + 3, uc.Y - 2);

        vm.Yakalama.Should().NotBeNull();
        vm.Yakalama!.Nokta.Should().Be(P(4_000_000m, 500_000m));
        vm.CursorText.Should().Contain("Y: 500000.000").And.Contain("X: 4000000.000").And.Contain("Uç");
    }

    [Fact]
    public void Osnap_Tum_Kipler_Kapaliyken_Imlec_Ham_Koordinati_Gostermeli()
    {
        var vm = new CadCanvasViewModel();
        vm.SetViewport(400, 400);
        vm.LoadScene(Sahne(Cizgi(P(4_000_000m, 500_000m), P(4_000_000m, 500_100m))));
        vm.OsnapUc = vm.OsnapOrta = vm.OsnapDik = vm.OsnapKesisim = false;
        var uc = vm.Camera.WorldToScreen(P(4_000_000m, 500_000m));

        vm.PointerMoved(uc.X + 3, uc.Y - 2);

        vm.Yakalama.Should().BeNull();
        vm.CursorText.Should().NotContain("OSNAP");
    }
}
