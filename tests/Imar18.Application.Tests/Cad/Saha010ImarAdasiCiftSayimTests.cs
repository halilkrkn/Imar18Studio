using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tecviz;
using Xunit;

namespace Imar18.Application.Tests.Cad;

/// <summary>
/// SAHA-010 / SORU-054: CAD'de hem İmar Adası hem İmar Parseli katmanı bulunduğunda DOP R2 (Σ imar alanı) çift sayılmamalıdır.
/// Bağımsız referans (elle hesap, tamamen sentetik): kadastro K1 25 m × 50 m = 1250.00 m²; İmar Adası 20 m × 50 m = 1000.00 m²;
/// İmar Parselleri A1 12 m × 50 m = 600.00 m² + A2 8 m × 50 m = 400.00 m² = 1000.00 m²; yol Y1 5 m × 50 m = 250.00 m².
/// Doğru R2 = 1000.00 m² (2000.00 değil) ve DOPO = (1250 − 1000) / 1250 = 0.2000000.
/// </summary>
public sealed class Saha010ImarAdasiCiftSayimTests
{
    private const string Kadastro = "TK_PARSEL_GİREN";
    private const string ImarParseli = "TK_PARSEL_OLUŞAN";
    private const string Yol = "TK_PARSEL_YOLATERK";
    private const string ImarAdasi = "IMAR_ADASI";

    /// <summary>Kuzey (X) 0..50 m, Doğu (Y) <paramref name="y1"/>..<paramref name="y2"/> m dikdörtgeni (kapanış tekrarlı).</summary>
    private static CadPolyline Serit(string katman, string etiket, decimal y1, decimal y2)
    {
        GeodeticPoint P(decimal x, decimal y) => new(OrnekProje.X0 + x, OrnekProje.Y0 + y);
        return new CadPolyline(katman, etiket, null, [P(0m, y1), P(0m, y2), P(50m, y2), P(50m, y1), P(0m, y1)], true, null);
    }

    private static CadLayerDocument Belge(params CadPolyline[] cizgiler) => new(
        CadFormat.Ncz,
        cizgiler.Select(c => c.LayerName).Distinct().Select((n, i) => new CadLayer(i, n)).ToArray(),
        cizgiler,
        [],
        0,
        0,
        0,
        []);

    private static CadPolyline K1 => Serit(Kadastro, "K1", 0m, 25m);

    private static CadPolyline Ada => Serit(ImarAdasi, "ADA-1", 0m, 20m);

    private static CadPolyline A1 => Serit(ImarParseli, "A1", 0m, 12m);

    private static CadPolyline A2 => Serit(ImarParseli, "A2", 12m, 20m);

    private static CadPolyline Y1 => Serit(Yol, "Y1", 20m, 25m);

    private static CadLayerRoleMap Roller(CadLayerRole adaRolu = CadLayerRole.ZoningBlock) =>
        CadLayerRoleMap.Standard.WithAssignment(ImarAdasi, adaRolu);

    private static decimal ImarToplami(ProjeGeometriSonucu s) => s.Parseller
        .Where(p => p.Rol == ParselRolu.Imar)
        .Sum(p => GaussAreaCalculator.Compute(PolygonRing.FromOpen(p.Koseler).Value).AreaM2);

    // ============================ Birim: rol önceliği ============================

    [Fact]
    public void ImarAdasi_Rolu_Turkce_Ad_Ve_Yoksay_Rolu_Tanimli_Olmali()
    {
        Enum.IsDefined(CadLayerRole.ZoningBlock).Should().BeTrue();
        Enum.IsDefined(CadLayerRole.Ignored).Should().BeTrue();
        CadLayerRoleMap.Standard.Resolve(ImarAdasi).Should().Be(CadLayerRole.Unassigned, "standart eşlemeye tahmini ad eklenmedi");
    }

    [Fact]
    public void ProjeGeometriEslemesi_Hem_Ada_Hem_Parsel_Varsa_R2_Yalniz_Imar_Parsellerinden_Olusmali()
    {
        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, A1, A2, Y1), Roller());

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("A1", "A2");
        sonuc.Parseller.Should().NotContain(p => p.ParselId == "ADA-1", "ada poligonu yalnız görsel referans olarak kalır");
        ImarToplami(sonuc).Should().Be(1000.00m, "R2 = 600 + 400; ada (1000) eklenirse 2000 olurdu");
        sonuc.Uyarilar.Should().ContainSingle().Which.Should().Be(ProjeGeometriEslemesi.AdaVeParselBirlikteUyarisi);
        ProjeGeometriEslemesi.AdaVeParselBirlikteUyarisi.Should().Be(
            "Hem İmar Adası hem İmar Parseli katmanı mevcut; DOP (R2) hesabında çift sayımı önlemek için yalnızca İmar Parseli alanları kullanıldı.");
        sonuc.AtlananSayisi.Should().Be(0, "ada poligonu geçersiz değil, bilinçli olarak dışarıda bırakıldı");
    }

    [Fact]
    public void ProjeGeometriEslemesi_Imar_Parseli_Yoksa_R2_Imar_Adasindan_Hesaplanmali()
    {
        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, Y1), Roller());

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("ADA-1");
        ImarToplami(sonuc).Should().Be(1000.00m);
        sonuc.Uyarilar.Should().BeEmpty();
    }

    [Fact]
    public void ProjeGeometriEslemesi_Imar_Parselleri_Yoksay_Rolune_Alininca_R2_Adadan_Gelmeli()
    {
        var roller = Roller().WithAssignment(ImarParseli, CadLayerRole.Ignored);

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, A1, A2, Y1), roller);

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("ADA-1");
        sonuc.Parseller.Should().NotContain(p => p.ParselId == "A1" || p.ParselId == "A2");
        sonuc.AtlananSayisi.Should().Be(0, "Yoksay rolü geçersiz poligon sayılmaz");
        sonuc.Uyarilar.Should().BeEmpty();
    }

    [Fact]
    public void ProjeGeometriEslemesi_Ada_Yoksay_Rolune_Alininca_Uyari_Uretmemeli()
    {
        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, A1, A2, Y1), Roller(CadLayerRole.Ignored));

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("A1", "A2");
        ImarToplami(sonuc).Should().Be(1000.00m);
        sonuc.Uyarilar.Should().BeEmpty();
    }

    [Fact]
    public void ProjeGeometriEslemesi_Ada_Kabugu_Imar_Parseli_Rolundeyse_R2den_Cikarilmali_Ve_Uyarmali()
    {
        // Mühendis ada katmanını yanlışlıkla "İmar Parseli" rolüne atamış: aynı rolde diğer parselleri kapsayan kabuk poligonu.
        var roller = Roller(CadLayerRole.ZoningParcel);

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, A1, A2, Y1), roller);

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("A1", "A2");
        ImarToplami(sonuc).Should().Be(1000.00m);
        sonuc.Uyarilar.Should().ContainSingle().Which.Should()
            .Contain("ADA-1").And.Contain("kapsayan").And.Contain("R2").And.Contain("İmar Adası").And.Contain("Yoksay");
    }

    [Fact]
    public void ProjeGeometriEslemesi_Birebir_Yinelenen_Imar_Parseli_R2ye_Bir_Kez_Girmeli_Ve_Uyarmali()
    {
        // Aynı köşeler, ters yön ve farklı başlangıç noktası (Netcad'de iki kez çizilmiş parsel).
        var a1 = A1;
        var ters = a1.Vertices.Take(4).Reverse().ToList();
        ters = [.. ters.Skip(1), ters[0]];
        var kopya = a1 with { Label = "A1-KOPYA", Vertices = [.. ters, ters[0]] };

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, A1, kopya, A2, Y1), CadLayerRoleMap.Standard);

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("A1", "A2");
        ImarToplami(sonuc).Should().Be(1000.00m);
        sonuc.Uyarilar.Should().ContainSingle().Which.Should().Contain("A1-KOPYA").And.Contain("üst üste").And.Contain("R2");
    }

    [Fact]
    public void ProjeGeometriEslemesi_Bir_Mm_Kaydirilmis_Esit_Alanli_Kopya_Yinelenen_Sayilmali()
    {
        // Reviewer L9: birebir köşe eşitliği aranırsa 1 mm kaydırılmış kopya R2'yi sessizce şişirirdi.
        var kopya = A1 with { Label = "A1-KAYIK", Vertices = [.. A1.Vertices.Select(v => v with { X = v.X + 0.001m })] };

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, A1, kopya, A2, Y1), CadLayerRoleMap.Standard);

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("A1", "A2");
        sonuc.Uyarilar.Should().ContainSingle().Which.Should().Contain("A1-KAYIK");
    }

    [Fact]
    public void ProjeGeometriEslemesi_Gecersiz_Ada_Poligonu_Disarida_Kalinca_Atlanan_Sayilmamali()
    {
        // Reviewer L8: İmar Parseli varken ada yalnız görseldir; açık/bozuk ada "geçersiz parsel" uyarısı üretmemeli.
        var acikAda = Ada with { Label = "ADA-2", IsClosed = false };

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, acikAda, A1, A2, Y1), Roller());

        sonuc.AtlananSayisi.Should().Be(0);
        sonuc.CiftSayimHaricSayisi.Should().Be(1);
    }

    [Fact]
    public void ProjeGeometriEslemesi_Imar_Parsellerinin_Hepsi_Gecersizse_R2_Adadan_Gelmeli()
    {
        var acikA1 = A1 with { IsClosed = false };

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, acikA1, Y1), Roller());

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("ADA-1");
        sonuc.AtlananSayisi.Should().Be(1);
        sonuc.Uyarilar.Should().BeEmpty();
    }

    [Fact]
    public void ProjeGeometriEslemesi_Komsu_Imar_Parselleri_Kabuk_Veya_Yinelenen_Sayilmamali()
    {
        // Ortak kenarlı komşu parseller (A1 | A2) birbirini kapsamaz: uyarı ve dışlama olmamalı (yanlış pozitif yok).
        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, A1, A2, Y1), CadLayerRoleMap.Standard);

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("A1", "A2");
        sonuc.Uyarilar.Should().BeEmpty();
    }

    [Fact]
    public void ProjeGeometriEslemesi_Icbukey_Parselin_Girintisini_Dolduran_Komsu_Kabuk_Sayilmamali()
    {
        // Architect bulgusu: U biçimli A1 (30 × 30 − 10 × 20 girinti = 700 m²) ve girintiyi tam dolduran A2 (10 × 20 = 200 m²).
        // A2'nin tüm köşeleri A1'in sınırındadır ama A2 A1'in içinde değildir: A1 kabuk sayılıp R2'den düşmemeli.
        static GeodeticPoint P(decimal x, decimal y) => new(OrnekProje.X0 + x, OrnekProje.Y0 + y);
        var u = new CadPolyline(ImarParseli, "A1", null,
            [P(0, 0), P(0, 30), P(30, 30), P(30, 20), P(10, 20), P(10, 10), P(30, 10), P(30, 0), P(0, 0)], true, null);
        var girinti = new CadPolyline(ImarParseli, "A2", null, [P(10, 10), P(10, 20), P(30, 20), P(30, 10), P(10, 10)], true, null);

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(u, girinti), CadLayerRoleMap.Standard);

        sonuc.Parseller.Select(p => p.ParselId).Should().Equal("A1", "A2");
        ImarToplami(sonuc).Should().Be(900.00m);
        sonuc.Uyarilar.Should().BeEmpty();
    }

    [Fact]
    public void ProjeGeometriEslemesi_Kucuk_Kiymik_Iceren_Gercek_Parsel_Kabuk_Sayilmamali()
    {
        // Büyük parsel içinde yanlışlıkla çizilmiş 1 m² kıymık: kapsama oranı %50'nin altında → büyük parsel R2'den düşmez
        // (çakışmayı topoloji denetimi bildirir).
        static GeodeticPoint P(decimal x, decimal y) => new(OrnekProje.X0 + x, OrnekProje.Y0 + y);
        var kiymik = new CadPolyline(ImarParseli, "K", null, [P(5, 5), P(5, 6), P(6, 6), P(6, 5), P(5, 5)], true, null);

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(A1, kiymik), CadLayerRoleMap.Standard);

        sonuc.Parseller.Select(p => p.ParselId).Should().Equal("A1", "K");
        sonuc.Uyarilar.Should().BeEmpty();
    }

    [Fact]
    public void ProjeGeometriEslemesi_Dislanan_Ada_Parsel_Kimliklerini_Kaydirmamali()
    {
        // Ada etiketi parsel etiketiyle aynı olsa bile dışlanan ada "~2" ekine yol açmaz (hak eşleştirme geçici kodları korunur).
        var ada = Ada with { Label = "A1" };

        var sonuc = ProjeGeometriEslemesi.Olustur(Belge(K1, ada, A1, A2, Y1), Roller());

        sonuc.Parseller.Where(p => p.Rol == ParselRolu.Imar).Select(p => p.ParselId).Should().Equal("A1", "A2");
    }

    [Fact]
    public void ProjeGeometriEslemesi_Cift_Sayim_Uyarisi_Deterministik_Olmali()
    {
        var a = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, A1, A2, Y1), Roller(CadLayerRole.ZoningParcel));
        var b = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, A1, A2, Y1), Roller(CadLayerRole.ZoningParcel));

        a.Parseller.Should().BeEquivalentTo(b.Parseller, o => o.WithStrictOrdering());
        a.Uyarilar.Should().Equal(b.Uyarilar);
    }

    // ============================ Entegrasyon: CAD → proje → DOP ============================

    private static async Task<ProjeDegerlendirmesi> DegerlendirAsync(CadLayerRoleMap roller)
    {
        var geometri = ProjeGeometriEslemesi.Olustur(Belge(K1, Ada, A1, A2, Y1), roller);
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Sentetik SAHA-010", zaman)).Value;
        IProjeKomutu[] komutlar =
        [
            new ProjeCrsAyarlaKomutu(OrnekProje.Crs()),
            new ParselGeometrileriAyarlaKomutu(geometri.Parseller),
            new TecvizGirdileriAyarlaKomutu([new TecvizParselGirdisi("K1", 1250.00m, 1250.00m, OrnekProje.Tecviz())], null),
        ];
        foreach (var k in komutlar)
        {
            (await yol.GonderAsync(k, OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        }

        // 3. adım "R2 boş: otomatik" yolu: önerilen R2 ile DOP girdisi yazılır.
        var oneri = ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji);
        var r2 = oneri.OnerilenR2M2!.Value;
        (await yol.GonderAsync(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(oneri.R1TapuToplamiM2!.Value, r2).Value), OrnekProje.Muhendis))
            .IsSuccess.Should().BeTrue();
        return ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji);
    }

    [Theory]
    [InlineData(CadLayerRole.ZoningBlock)]
    [InlineData(CadLayerRole.ZoningParcel)]
    [InlineData(CadLayerRole.Ignored)]
    public async Task Entegrasyon_Ada_Ve_Parsel_Birlikte_DOP_R2_1000_Ve_DOPO_Dogru_Olmali(CadLayerRole adaRolu)
    {
        var d = await DegerlendirAsync(Roller(adaRolu));

        d.R1TapuToplamiM2.Should().Be(1250.00m);
        d.OnerilenR2M2.Should().Be(1000.00m, "R2 2000 değil 1000 olmalı (çift sayım yok)");
        d.Dop.Should().NotBeNull();
        d.Dop!.Girdi.R2.Should().Be(1000.00m);
        d.Dop.Dopo.Should().Be(0.2000000m, "(1250 − 1000) / 1250");
        d.Dop.KamuyaAyrilanAlanM2.Should().Be(250.00m);
        d.Dengeleme!.ImarParselToplamiM2.Should().Be(1000.00m);
        d.Bulgular.Should().NotContain(b => b.Severity == ErrorSeverity.Critical, "ada kabuğu imar parselleriyle çakışma üretmemeli");
    }
}
