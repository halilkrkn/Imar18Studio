using FluentAssertions;
using Imar18.Domain.Geometry;
using Imar18.GeometryEngine.Topoloji;
using Xunit;

namespace Imar18.GeometryEngine.Tests.Topoloji;

/// <summary>
/// P6 (SORU-034 k): parseller arası çakışma / boşluk / kadastro dışına taşma analizi (NTS overlay). Tüm geometriler
/// eksenlere paralel dikdörtgenlerdir; beklenen alanlar elle (en × boy) hesaplanmıştır. Yerel X = Kuzey, Y = Doğu.
/// </summary>
public sealed class NtsParselTopolojiDenetcisiTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;
    private static readonly NtsParselTopolojiDenetcisi Denetci = new();

    /// <summary>X ∈ [x1, x2] (Kuzey), Y ∈ [y1, y2] (Doğu) dikdörtgeni.</summary>
    private static ParselGeometrisi D(string id, ParselRolu rol, decimal y1, decimal y2, decimal x1 = 0m, decimal x2 = 10m) => new(
        id, rol,
        [new GeodeticPoint(X0 + x1, Y0 + y1), new GeodeticPoint(X0 + x1, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y1)]);

    [Fact]
    public void Topoloji_Olusan_Parseller_Kadastroyu_Tam_Kapliyorsa_Bulgu_Olmamali()
    {
        var b = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 12m), D("K2", ParselRolu.Kadastro, 12m, 20m),
            D("A1", ParselRolu.Imar, 0m, 9m), D("A2", ParselRolu.Imar, 9m, 15m), D("Y1", ParselRolu.YolTerk, 15m, 18m), D("P1", ParselRolu.KamuAlani, 18m, 20m)]);

        b.Should().BeEmpty();
    }

    [Fact]
    public void Topoloji_Cakisan_Imar_Parselleri_Cakisma_Alaniyla_Raporlanmali()
    {
        // A1 [0,12] ve A2 [10,20]: çakışma 2 m × 10 m = 20.00 m²; birleşim kadastroya eşit (boşluk/taşma yok).
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("A2", ParselRolu.Imar, 10m, 20m), D("A1", ParselRolu.Imar, 0m, 12m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["A1", "A2"], 20.00m), o => o.WithStrictOrdering());
    }

    [Fact]
    public void Topoloji_Yol_Ile_Imar_Parseli_Cakismasi_De_Olusan_Cakismasidir()
    {
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, 12.5m), D("Y1", ParselRolu.YolTerk, 12m, 20m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanCakismasi, ["A1", "Y1"], 5.00m));
    }

    [Fact]
    public void Topoloji_Olusan_Parseller_Arasinda_Bosluk_Kapsama_Boslugu_Olarak_Raporlanmali()
    {
        // A1 [0,12], Y1 [13,20]: 1 m × 10 m = 10.00 m² karşılanmayan düzenleme alanı.
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, 12m), D("Y1", ParselRolu.YolTerk, 13m, 20m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.OlusanKapsamaBoslugu, ["A1", "K1", "Y1"], 10.00m));
    }

    [Fact]
    public void Topoloji_Kadastro_Sinirini_Asan_Olusan_Parsel_Tasma_Olarak_Raporlanmali()
    {
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, 12m), D("Y1", ParselRolu.YolTerk, 12m, 21.5m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["K1", "Y1"], 15.00m)); // SAHA-004: taşan parsel + komşu kadastro
    }

    [Fact]
    public void Topoloji_Cakisan_Kadastro_Parselleri_Raporlanmali()
    {
        // K1 [0,11], K2 [9,20]: 2 × 10 = 20.00 m² çakışma; oluşan birleşimi kadastro birleşimine eşit.
        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 11m), D("K2", ParselRolu.Kadastro, 9m, 20m), D("A1", ParselRolu.Imar, 0m, 20m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroCakismasi, ["K1", "K2"], 20.00m));
    }

    [Fact]
    public void Topoloji_Kadastro_Parselleri_Arasindaki_Ic_Bosluk_Ve_Ustunu_Kaplayan_Imar_Parseli_Raporlanmali()
    {
        // Dört kadastro parseli 10 × 10 karenin ortasında 4 × 4 = 16.00 m² delik bırakır; tek imar parseli tüm kareyi kaplar.
        var b = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 10m, 0m, 3m), D("K2", ParselRolu.Kadastro, 0m, 10m, 7m, 10m),
            D("K3", ParselRolu.Kadastro, 0m, 3m, 3m, 7m), D("K4", ParselRolu.Kadastro, 7m, 10m, 3m, 7m),
            D("A1", ParselRolu.Imar, 0m, 10m, 0m, 10m)]);

        b.Should().BeEquivalentTo(
            [
                new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroBoslugu, ["K1", "K2", "K3", "K4"], 16.00m),
                new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroDisinaTasma, ["A1", "K1", "K2", "K3", "K4"], 16.00m),
            ],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void Topoloji_Esik_Altindaki_Kiymiklar_Raporlanmamali_Esikteki_Raporlanmali()
    {
        // 0.0004 m × 10 m = 0.004 m² → 0.00 (raporlanmaz); 0.0005 m × 10 m = 0.005 m² → 0.01 (AwayFromZero, raporlanır).
        Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, 12.0004m), D("A2", ParselRolu.Imar, 12m, 20m)])
            .Should().BeEmpty();
        Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, 12.0005m), D("A2", ParselRolu.Imar, 12m, 20m)])
            .Should().ContainSingle().Which.AlanM2.Should().Be(0.01m);
        ParselTopolojisi.AlanEsigiM2.Should().Be(0.01m);
    }

    [Fact]
    public void Topoloji_Bulgu_Sirasi_Girdi_Sirasindan_Bagimsiz_Ve_Deterministik_Olmali()
    {
        ParselGeometrisi[] parseller =
        [
            D("K1", ParselRolu.Kadastro, 0m, 11m), D("K2", ParselRolu.Kadastro, 9m, 20m),
            D("B1", ParselRolu.Imar, 0m, 12m), D("A1", ParselRolu.Imar, 11m, 19m),
        ];

        var ileri = Denetci.Denetle(parseller);
        var geri = Denetci.Denetle([.. parseller.Reverse()]);

        ileri.Should().BeEquivalentTo(geri, o => o.WithStrictOrdering());
        ileri.Select(b => b.Tur).Should().Equal(TopolojiBulguTuru.KadastroCakismasi, TopolojiBulguTuru.OlusanCakismasi, TopolojiBulguTuru.OlusanKapsamaBoslugu);
        ileri[1].ParselIdleri.Should().Equal("A1", "B1");
    }

    [Fact]
    public void Topoloji_Bos_Veya_Tek_Rollu_Girdide_Kapsama_Analizi_Yapilmamali()
    {
        Denetci.Denetle([]).Should().BeEmpty();
        Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m)]).Should().BeEmpty();
        Denetci.Denetle([D("A1", ParselRolu.Imar, 0m, 12m), D("A2", ParselRolu.Imar, 11m, 20m)])
            .Should().ContainSingle().Which.Tur.Should().Be(TopolojiBulguTuru.OlusanCakismasi);
    }

    [Fact]
    public void Topoloji_Gecersiz_Halka_Sessizce_Atlanmamali_Analiz_Yapilamadi_Bulgusu_Uretmeli_Istisna_Atmamali()
    {
        // Fail-safe: NTS'nin geçersiz saydığı halka çakışmayı gizleyebilir; atlanırsa analiz eksik sayılmalıdır.
        var papyon = new ParselGeometrisi("A9", ParselRolu.Imar,
            [new GeodeticPoint(X0, Y0), new GeodeticPoint(X0 + 10m, Y0 + 10m), new GeodeticPoint(X0 + 10m, Y0), new GeodeticPoint(X0, Y0 + 10m)]);
        var ikiKose = new ParselGeometrisi("A8", ParselRolu.Imar, [new GeodeticPoint(X0, Y0), new GeodeticPoint(X0 + 1m, Y0)]);

        var b = Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, 20m), papyon, ikiKose]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ParselTopolojiBulgusu(TopolojiBulguTuru.AnalizYapilamadi, ["A8", "A9"], 0m), o => o.WithStrictOrdering());
    }

    [Fact]
    public void Topoloji_Iptal_Belirteci_Dikkate_Alinmali()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 20m), D("A1", ParselRolu.Imar, 0m, 20m)], cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }
}

/// <summary>P6 reviewer M1: dışa açık dar şerit boşluklar ve konkav kadastroda yanlış pozitif olmaması.</summary>
public sealed class NtsParselTopolojiSeritBosluguTests
{
    private const decimal X0 = 4_400_000m;
    private const decimal Y0 = 500_000m;
    private static readonly NtsParselTopolojiDenetcisi Denetci = new();

    private static ParselGeometrisi D(string id, ParselRolu rol, decimal y1, decimal y2, decimal x1 = 0m, decimal x2 = 10m) => new(
        id, rol,
        [new GeodeticPoint(X0 + x1, Y0 + y1), new GeodeticPoint(X0 + x1, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y2), new GeodeticPoint(X0 + x2, Y0 + y1)]);

    [Fact]
    public void Topoloji_Disa_Acik_Kadastro_Serit_Boslugu_Raporlanmali_Olusan_Ayni_Boslugu_Tasisa_Da()
    {
        // K1 [0,10], K2 [10.05,20]: 0.05 m × 10 m = 0.50 m² şerit; A1/A2 aynı boşluğu taşır (kapsama farkı yok).
        var b = Denetci.Denetle([
            D("K1", ParselRolu.Kadastro, 0m, 10m), D("K2", ParselRolu.Kadastro, 10.05m, 20m),
            D("A1", ParselRolu.Imar, 0m, 10m), D("A2", ParselRolu.Imar, 10.05m, 20m)]);

        b.Should().ContainSingle().Which.Should().BeEquivalentTo(new ParselTopolojiBulgusu(TopolojiBulguTuru.KadastroBoslugu, ["K1", "K2"], 0.50m));
    }

    [Fact]
    public void Topoloji_Olusan_Parsel_Yokken_De_Kadastro_Serit_Boslugu_Raporlanmali()
    {
        Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 10m), D("K2", ParselRolu.Kadastro, 10.02m, 20m)])
            .Should().ContainSingle().Which.AlanM2.Should().Be(0.20m);
    }

    [Fact]
    public void Topoloji_Konkav_L_Kadastro_Ve_Genis_Ayrik_Parseller_Bosluk_Sayilmamali()
    {
        var l = new ParselGeometrisi("K1", ParselRolu.Kadastro,
            [new GeodeticPoint(X0, Y0), new GeodeticPoint(X0, Y0 + 40m), new GeodeticPoint(X0 + 40m, Y0 + 40m),
             new GeodeticPoint(X0 + 40m, Y0 + 24m), new GeodeticPoint(X0 + 16m, Y0 + 24m), new GeodeticPoint(X0 + 16m, Y0)]);

        Denetci.Denetle([l]).Should().BeEmpty("mitre kapanış konkav köşede alan eklemez");
        Denetci.Denetle([D("K1", ParselRolu.Kadastro, 0m, 10m), D("K2", ParselRolu.Kadastro, 11m, 20m)])
            .Should().BeEmpty("10 cm'den geniş dışa açık aralık düzenleme sınırının parçasıdır (SORU-035)");
    }
}
