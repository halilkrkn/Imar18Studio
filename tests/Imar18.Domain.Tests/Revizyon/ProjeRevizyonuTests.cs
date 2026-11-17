using FluentAssertions;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;
using Xunit;

namespace Imar18.Domain.Tests.Revizyon;

/// <summary>
/// P5.2 — Proje revizyon kimliği (AC-PILOT-06, spec 11.2 madde 9): P3 hesap girdilerine ek olarak geometri (köşe koordinatları,
/// parsel rolü), proje CRS'i, geçici↔tescil kimlik eşlemesi ve proje adı da kimliğe dahildir. Parsel listesi sırası ve decimal
/// ölçeği kimliği değiştirmez; herhangi bir koordinatın 1 mm oynaması, CRS dilimi/epok/ölçek değişimi farklı kimlik üretir.
/// </summary>
public sealed class ProjeRevizyonuTests
{
    private static ProjeCrs Crs(int meridyen = 33, decimal? epok = 2005.00m, int olcek = 1000) =>
        ProjeCrs.Olustur(GeodetikDatum.Itrf96, meridyen, epok, olcek).Value;

    private static ParselGeometrisi Kare(string id, ParselRolu rol, decimal x0, decimal y0, decimal kenar = 10m) => new(
        id,
        rol,
        [new GeodeticPoint(x0, y0), new GeodeticPoint(x0, y0 + kenar), new GeodeticPoint(x0 + kenar, y0 + kenar), new GeodeticPoint(x0 + kenar, y0)]);

    private static ProjeRevizyonGirdisi Girdi(
        string ad = "Sentetik Proje",
        IReadOnlyList<ParselGeometrisi>? geometri = null,
        IReadOnlyList<ParselKimlikCifti>? kimlik = null,
        ProjeCrs? crs = null)
    {
        var hak = new HakEslestirmeGirdisi(
            0.5m,
            [new KadastroParseli("K1", 100.00m, [new KadastroMaliki("M1", Fraction.One, true)])],
            [new ImarParseli("A1", 25.00m)],
            [new HakEslestirmeSatiri("M1", "K1", "A1", 25.00m)]);
        return new ProjeRevizyonGirdisi(
            ad,
            crs ?? Crs(),
            geometri ?? [Kare("K1", ParselRolu.Kadastro, 4_500_000m, 500_000m), Kare("A1", ParselRolu.Imar, 4_500_000m, 500_000m, 5m)],
            DopGirdisi.Olustur(100.00m, 25.00m).Value,
            [new TecvizParselGirdisi("K1", 100.00m, 100.00m, null)],
            null,
            hak,
            kimlik ?? [new ParselKimlikCifti("A1", "101/1")]);
    }

    [Fact]
    public void ProjeRevizyonu_Ayni_Veri_Ayni_Kimligi_Uretmeli()
    {
        ProjeRevizyonu.Hesapla(Girdi()).Should().Be(ProjeRevizyonu.Hesapla(Girdi()));
        ProjeRevizyonu.Hesapla(Girdi()).Deger.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ProjeRevizyonu_Kose_1_mm_Kayarsa_Kimlik_Degismeli()
    {
        var k1 = Kare("K1", ParselRolu.Kadastro, 4_500_000m, 500_000m);
        var koseler = k1.Koseler.ToArray();
        koseler[2] = koseler[2] with { X = koseler[2].X + 0.001m };
        var geometri = new[] { k1 with { Koseler = koseler }, Kare("A1", ParselRolu.Imar, 4_500_000m, 500_000m, 5m) };

        ProjeRevizyonu.Hesapla(Girdi(geometri: geometri)).Should().NotBe(ProjeRevizyonu.Hesapla(Girdi()));
    }

    [Fact]
    public void ProjeRevizyonu_Parsel_Rolu_Degisirse_Kimlik_Degismeli()
    {
        var geometri = new[] { Kare("K1", ParselRolu.Kadastro, 4_500_000m, 500_000m), Kare("A1", ParselRolu.YolTerk, 4_500_000m, 500_000m, 5m) };

        ProjeRevizyonu.Hesapla(Girdi(geometri: geometri)).Should().NotBe(ProjeRevizyonu.Hesapla(Girdi()));
    }

    [Theory]
    [InlineData(36, "2005.00", 1000)]
    [InlineData(33, "2010.00", 1000)]
    [InlineData(33, "2005.00", 5000)]
    public void ProjeRevizyonu_CRS_Dilim_Epok_Veya_Olcek_Degisirse_Kimlik_Degismeli(int meridyen, string epok, int olcek)
    {
        var farkli = Girdi(crs: Crs(meridyen, decimal.Parse(epok, System.Globalization.CultureInfo.InvariantCulture), olcek));

        ProjeRevizyonu.Hesapla(farkli).Should().NotBe(ProjeRevizyonu.Hesapla(Girdi()));
    }

    [Fact]
    public void ProjeRevizyonu_CRS_Yok_Ile_Var_Ayirt_Edilmeli()
    {
        ProjeRevizyonu.Hesapla(Girdi() with { Crs = null }).Should().NotBe(ProjeRevizyonu.Hesapla(Girdi()));
    }

    [Fact]
    public void ProjeRevizyonu_Tescil_Kimlik_Eslemesi_Veya_Proje_Adi_Degisirse_Kimlik_Degismeli()
    {
        var baz = ProjeRevizyonu.Hesapla(Girdi());

        ProjeRevizyonu.Hesapla(Girdi(kimlik: [new ParselKimlikCifti("A1", "101/2")])).Should().NotBe(baz);
        ProjeRevizyonu.Hesapla(Girdi(ad: "Başka Proje")).Should().NotBe(baz);
    }

    [Fact]
    public void ProjeRevizyonu_DOP_Tecviz_Veya_Hak_Eslestirme_Degisirse_Kimlik_Degismeli()
    {
        var baz = ProjeRevizyonu.Hesapla(Girdi());

        ProjeRevizyonu.Hesapla(Girdi() with { Dop = DopGirdisi.Olustur(100.00m, 25.01m).Value }).Should().NotBe(baz);
        ProjeRevizyonu.Hesapla(Girdi() with { Dop = null }).Should().NotBe(baz);
        ProjeRevizyonu.Hesapla(Girdi() with { HakEslestirme = null }).Should().NotBe(baz);
        ProjeRevizyonu.Hesapla(Girdi() with { DuzenlemeToplamTecvizSiniriM2 = 5m }).Should().NotBe(baz);
    }

    [Fact]
    public void ProjeRevizyonu_Parsel_Sirasi_Ve_Decimal_Olcegi_Kimligi_Degistirmemeli()
    {
        var g = Girdi();
        var ters = g.Geometri.Reverse()
            .Select(p => p with { Koseler = p.Koseler.Select(k => new GeodeticPoint(k.X * 1.000m, k.Y * 1.0m)).ToArray() })
            .ToArray();

        ProjeRevizyonu.Hesapla(g with { Geometri = ters }).Should().Be(ProjeRevizyonu.Hesapla(g));
    }

    [Fact]
    public void ProjeRevizyonu_Kose_Dizisi_Baslangici_Degisirse_Kimlik_Degismeli()
    {
        // Muhafazakâr kural: köşe dizisinin başlangıcı veya yönü değişirse veri değişmiş sayılır ve onay düşer.
        var g = Girdi();
        var ilk = g.Geometri[0];
        var donmus = ilk with { Koseler = ilk.Koseler.Skip(1).Append(ilk.Koseler[0]).ToArray() };

        ProjeRevizyonu.Hesapla(g with { Geometri = [donmus, g.Geometri[1]] }).Should().NotBe(ProjeRevizyonu.Hesapla(g));
    }

    [Fact]
    public void ProjeRevizyonu_Null_Liste_Reddedilmeli()
    {
        var act = () => ProjeRevizyonu.Hesapla(Girdi() with { Geometri = null! });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProjeRevizyonu_P3_Hesap_Revizyonundan_Ayri_Sema_Kullanmali()
    {
        var g = Girdi();
        var p3 = HesapRevizyonu.Hesapla(g.HakEslestirme!, g.Dop!, g.TecvizParselleri, g.DuzenlemeToplamTecvizSiniriM2);

        ProjeRevizyonu.Hesapla(g).Should().NotBe(p3);
    }
}
