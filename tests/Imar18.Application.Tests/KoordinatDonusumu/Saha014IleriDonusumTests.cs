using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Revizyon;
using Imar18.GeometryEngine.Crs;
using Xunit;

namespace Imar18.Application.Tests.KoordinatDonusumu;

/// <summary>
/// SAHA-014: <c>.DNS</c> ile Deprem Öncesi → Deprem Sonrası ileri dönüşüm akışı (CommandBus). Bağımsız referans: test içinde
/// yazılan ileri formül <c>Y2 = A·Y1 + C·X1 + CY</c>, <c>X2 = B·Y1 + D·X1 + CX</c> ve el hesabı köşe değerleri. Örnek proje
/// sentetiktir (<see cref="OrnekProje"/>: K1 20 × 10 m, A1 12 × 10 m, Y1 8 × 10 m; Y0 = 500000, X0 = 4400000).
/// </summary>
public sealed class Saha014IleriDonusumTests
{
    internal static readonly DonusumKatsayilari K = new(A: 0.99999m, B: -0.00001m, C: 0.00001m, D: 0.99999m, Cy: -25.5m, Cx: 18.25m);

    /// <summary>Ölçekli (×1.001) sentetik katsayı: alanın yeniden hesaplandığını ayırt edici biçimde gösterir (det = 1.002001).</summary>
    internal static readonly DonusumKatsayilari Olcekli = new(A: 1.001m, B: 0m, C: 0m, D: 1.001m, Cy: 0m, Cx: 0m);

    internal static DonusumGirdisi Girdi() => new(DonusumModeli.Helmert2B, 0.09m,
    [
        new DonusumOrtakNoktasi("N2", 501_000.000m, 4_000_000.000m, 501_009.500m, 3_999_973.240m),
        new DonusumOrtakNoktasi("N1", 500_000.000m, 4_000_000.000m, 500_009.490m, 3_999_973.270m),
        new DonusumOrtakNoktasi("N3", 501_000.000m, 4_001_000.000m, 501_009.500m, 4_000_973.210m),
    ]);

    private static GeodeticPoint Ileri(DonusumKatsayilari k, GeodeticPoint p) => new(
        X: Math.Round((k.B * p.Y) + (k.D * p.X) + k.Cx, 6, MidpointRounding.AwayFromZero),
        Y: Math.Round((k.A * p.Y) + (k.C * p.X) + k.Cy, 6, MidpointRounding.AwayFromZero));

    private static IReadOnlyList<ParselGeometrisi> Ileri(DonusumKatsayilari k, IReadOnlyList<ParselGeometrisi> p) =>
        p.Select(x => x with { Koseler = x.Koseler.Select(v => Ileri(k, v)).ToArray() }).ToArray();

    private static ProjeBelgesi Geometrili() =>
        new ParselGeometrileriAyarlaKomutu(OrnekProje.Geometri()).Uygula(ProjeBelgesi.Yeni("Sentetik")).Value;

    private static void KoselerAyni(IReadOnlyList<ParselGeometrisi> a, IReadOnlyList<ParselGeometrisi> b)
    {
        a.Select(x => (x.ParselId, x.Rol)).Should().Equal(b.Select(x => (x.ParselId, x.Rol)));
        for (var i = 0; i < a.Count; i++)
        {
            a[i].Koseler.Should().Equal(b[i].Koseler, a[i].ParselId);
        }
    }

    [Fact]
    public void IleriDonusum_Tum_Parsel_Koselerini_Ileri_Formulle_Donusturmeli_Orijinalleri_Saklamali()
    {
        var b = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(Geometrili()).Value;

        // El hesabı (K1 ilk köşe Y0 = 500000, X0 = 4400000):
        // Y2 = 0.99999·500000 + 0.00001·4400000 − 25.5 = 499995 + 44 − 25.5 = 500013.5
        // X2 = −0.00001·500000 + 0.99999·4400000 + 18.25 = −5 + 4399956 + 18.25 = 4399969.25
        b.Parseller.Single(p => p.ParselId == "K1").Koseler[0].Should().Be(new GeodeticPoint(X: 4_399_969.25m, Y: 500_013.5m));
        KoselerAyni(b.Parseller, Ileri(K, OrnekProje.Geometri()));
        KoselerAyni(b.DepremOncesiParseller!, OrnekProje.Geometri());
        b.IleriDonusum.Should().Be(K);
        b.Donusum!.OrtakNoktalar.Select(n => n.NoktaNo).Should().Equal("N1", "N2", "N3");
    }

    [Fact]
    public void IleriDonusum_Ayni_DNS_Iki_Kez_Uygulaninca_Koordinatlar_Ikinci_Kez_Kaymamali()
    {
        var komut = new IleriDonusumUygulaKomutu(Girdi(), K);
        var bir = komut.Uygula(Geometrili()).Value;

        var iki = komut.Uygula(bir).Value;

        KoselerAyni(iki.Parseller, bir.Parseller);
        KoselerAyni(iki.DepremOncesiParseller!, OrnekProje.Geometri());
        iki.RevizyonKimligi().Should().Be(bir.RevizyonKimligi());
    }

    [Fact]
    public void IleriDonusum_Farkli_DNS_Yeniden_Uygulaninca_Her_Zaman_Orijinal_Deprem_Oncesinden_Donusturmeli()
    {
        var bir = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(Geometrili()).Value;

        var iki = new IleriDonusumUygulaKomutu(Girdi(), Olcekli).Uygula(bir).Value;

        KoselerAyni(iki.Parseller, Ileri(Olcekli, OrnekProje.Geometri()));
        KoselerAyni(iki.DepremOncesiParseller!, OrnekProje.Geometri());
        iki.IleriDonusum.Should().Be(Olcekli);
    }

    [Fact]
    public void IleriDonusum_Etkinken_ParselGeometrileriAyarla_Girdisi_Deprem_Oncesi_Sayilip_Donusturulmeli_DNS_Once_CAD_Sonra()
    {
        var dnsOnce = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(ProjeBelgesi.Yeni("Sentetik")).Value;
        dnsOnce.Parseller.Should().BeEmpty();
        dnsOnce.DepremOncesiParseller.Should().BeEmpty();

        // Rol değişiminde CAD'den (deprem öncesi) yeniden eşleme aynı komutla gelir: asla dönüşümsüz kalmamalı, çift kaymamalı.
        var cad = new ParselGeometrileriAyarlaKomutu(OrnekProje.Geometri()).Uygula(dnsOnce).Value;
        var tekrar = new ParselGeometrileriAyarlaKomutu(OrnekProje.Geometri()).Uygula(cad).Value;

        KoselerAyni(cad.Parseller, Ileri(K, OrnekProje.Geometri()));
        KoselerAyni(cad.DepremOncesiParseller!, OrnekProje.Geometri());
        KoselerAyni(tekrar.Parseller, cad.Parseller);
    }

    [Fact]
    public void DonusumGirdisiAyarla_Null_Ileri_Donusumu_Kaldirip_Deprem_Oncesi_Geometriye_Donmeli()
    {
        var donusmus = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(Geometrili()).Value;

        var kaldirilmis = new DonusumGirdisiAyarlaKomutu(null).Uygula(donusmus).Value;

        KoselerAyni(kaldirilmis.Parseller, OrnekProje.Geometri());
        kaldirilmis.IleriDonusum.Should().BeNull();
        kaldirilmis.DepremOncesiParseller.Should().BeNull();
        kaldirilmis.Donusum.Should().BeNull();
        kaldirilmis.RevizyonKimligi().Should().Be(Geometrili().RevizyonKimligi(), "dönüşüm kaldırılınca proje DNS öncesi kimliğine döner");
    }

    [Fact]
    public void DonusumGirdisiAyarla_Elle_Ortak_Nokta_Ileri_Donusum_Etkinken_Reddedilmeli_Sessizce_Ters_Kipe_Gecmemeli()
    {
        // Architect K1: yalnız MK düzeltmek için elle "Dönüşümü Uygula" denenirse tüm ileri dönüşüm sessizce kalkmamalı.
        var donusmus = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(Geometrili()).Value;

        var r = new DonusumGirdisiAyarlaKomutu(Girdi() with { Mk = 0.10m }).Uygula(donusmus);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
        r.Error.UserMessage.Should().Contain("Dönüşümü Kaldır");
    }

    [Fact]
    public void ParselGeometrileriAyarla_Aktif_Sistem_Girdisi_Ileri_Donusum_Etkinken_Reddedilmeli_Yoksa_Aynen_Yazilmali()
    {
        var donusmus = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(Geometrili()).Value;
        var aktif = new ParselGeometrileriAyarlaKomutu(donusmus.Parseller) { Sistem = ParselKoordinatSistemi.Aktif };

        aktif.Uygula(donusmus).IsFailure.Should().BeTrue("aktif sistemde üretilen geometrinin orijinali türetilemez (çift dönüşüm riski)");
        KoselerAyni(aktif.Uygula(ProjeBelgesi.Yeni("Sentetik")).Value.Parseller, donusmus.Parseller);
    }

    [Fact]
    public void IleriDonusum_Iki_Farkli_Orijinal_Kose_Ayni_Noktaya_Dusurse_Reddedilmeli()
    {
        // 6 ondalık yuvarlamada 0.4 µm ayrık iki köşe aynı sonuca düşer: nokta no ↔ deprem öncesi eşlemesi bozulurdu (architect Y1).
        var g = OrnekProje.Geometri().ToArray();
        var kayik = g[2].Koseler.ToArray();
        kayik[0] = new GeodeticPoint(X: OrnekProje.X0 + 0m, Y: OrnekProje.Y0 + 12.0000004m);
        var orj = new ParselGeometrileriAyarlaKomutu([g[0], g[1]]).Uygula(ProjeBelgesi.Yeni("Sentetik")).Value
            with { Parseller = [g[0], g[1], g[2] with { Koseler = kayik }] };

        var r = new IleriDonusumUygulaKomutu(Girdi(), new DonusumKatsayilari(1m, 0m, 0m, 1m, 0m, 0m)).Uygula(orj);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
    }

    [Fact]
    public async Task KomutYolu_Ileri_Modda_Orijinalle_Tutarsiz_Parseller_Merkezi_Denetimde_Reddedilmeli()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new IleriDonusumUygulaKomutu(Girdi(), K), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var once = yol.Belge;

        var r = await yol.GonderAsync(new SahteParselKomutu(), OrnekProje.Muhendis);

        r.IsFailure.Should().BeTrue("Parseller = İleri(DepremOncesiParseller) değişmezi CommandBus'ta denetlenir (architect K2)");
        yol.Belge.Should().BeSameAs(once);
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    /// <summary>İleri modda aktif geometriyi orijinalleri güncellemeden değiştiren hatalı (gelecekteki) komut benzetimi.</summary>
    private sealed record SahteParselKomutu : IProjeKomutu
    {
        public string Ad => "SahteParsel";

        public Imar18.Domain.Diagnostics.Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut) =>
            Imar18.Domain.Diagnostics.Result<ProjeBelgesi>.Success(mevcut with { Parseller = OrnekProje.Geometri() });
    }

    [Fact]
    public void IleriDonusum_Elle_Ters_Kip_Etkinken_Reddedilmeli_Kaldirinca_Uygulanabilmeli()
    {
        // Reviewer M1: elle (ters) kipte proje noktaları "deprem sonrası" beyan edilmiştir; DNS sessizce ikinci kez kaydırmamalı.
        var elle = new DonusumGirdisiAyarlaKomutu(Girdi()).Uygula(Geometrili()).Value;
        elle.DonusumKipi.Should().Be(DonusumKipi.ElleTers);

        var r = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(elle);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
        r.Error.UserMessage.Should().Contain("Dönüşümü Kaldır");
        var kaldirilmis = new DonusumGirdisiAyarlaKomutu(null).Uygula(elle).Value;
        kaldirilmis.DonusumKipi.Should().Be(DonusumKipi.Yok);
        var ileri = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(kaldirilmis).Value;
        ileri.DonusumKipi.Should().Be(DonusumKipi.IleriDns);
        KoselerAyni(ileri.Parseller, Ileri(K, OrnekProje.Geometri()));
    }

    [Theory]
    [InlineData("70000000000000000000000000000", "0", "0")] // A = D = 7E+28 → determinant taşardı
    [InlineData("1", "79000000000000000000000000000", "0")] // CY ≈ 7.9E+28 → ileri dönüşüm taşardı
    [InlineData("2.5", "0", "0")] // |A| > 2
    [InlineData("1", "0", "-10000001")] // |CX| > 1e7 m
    public async Task KomutYolu_Tasmali_Veya_Sinir_Disi_Katsayida_Istisna_Sizmamali_Red_Denetlenmeli(string a, string cy, string cx)
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var ad = decimal.Parse(a, inv);
        var k = new DonusumKatsayilari(ad, 0m, 0m, ad, decimal.Parse(cy, inv), decimal.Parse(cx, inv));
        var once = yol.Belge;

        var r = await yol.GonderAsync(new IleriDonusumUygulaKomutu(Girdi(), k), OrnekProje.Muhendis);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
        yol.Belge.Should().BeSameAs(once);
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public void DonusumOzeti_Ileri_Eslesmeyen_Aktif_Nokta_ERR_CRS_004_Donmeli()
    {
        var aktif = new[] { new KoordinatSatiri(1, 500_013.5m, 4_399_969.25m), new KoordinatSatiri(2, 1m, 2m) };

        var r = Imar18.Application.KoordinatDonusumu.DonusumOzeti.Ileri(Girdi(), K, OrnekProje.Geometri(), aktif);

        r.IsFailure.Should().BeTrue("eşleşmeyen aktif nokta sessizce yanlış Deprem Öncesi koordinatla basılmamalı");
        r.Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
        Imar18.Application.KoordinatDonusumu.DonusumOzeti.Ileri(Girdi(), K, OrnekProje.Geometri(), aktif[..1]).Value.DepremOncesi
            .Should().Equal(new KoordinatSatiri(1, OrnekProje.Y0, OrnekProje.X0));
    }

    [Fact]
    public void ProjeyiSifirla_Ileri_Donusumu_Ve_Orijinalleri_Temizlemeli()
    {
        var donusmus = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(Geometrili()).Value;

        var b = new ProjeyiSifirlaKomutu().Uygula(donusmus).Value;

        b.IleriDonusum.Should().BeNull();
        b.DepremOncesiParseller.Should().BeNull();
        b.Parseller.Should().BeEmpty();
        b.RevizyonKimligi().Should().Be(ProjeBelgesi.Yeni("Sentetik").RevizyonKimligi());
    }

    [Fact]
    public void IleriDonusum_Gecersiz_Katsayi_Veya_Girdi_Reddedilmeli()
    {
        var b = Geometrili();

        new IleriDonusumUygulaKomutu(Girdi(), K with { B = 0.99999m, C = 0.99999m }).Uygula(b).Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi, "tekil matris");
        new IleriDonusumUygulaKomutu(Girdi(), null!).Uygula(b).IsFailure.Should().BeTrue();
        new IleriDonusumUygulaKomutu(null!, K).Uygula(b).IsFailure.Should().BeTrue();
        new IleriDonusumUygulaKomutu(Girdi() with { OrtakNoktalar = Girdi().OrtakNoktalar.Take(1).ToArray() }, K).Uygula(b).IsFailure.Should().BeTrue("Helmert en az 2 nokta");
        new IleriDonusumUygulaKomutu(Girdi() with { Mk = 0m }, K).Uygula(b).IsFailure.Should().BeTrue("0 < MK ≤ 10");
    }

    [Fact]
    public void IleriDonusum_Olcekli_Katsayida_Alanlar_Yeniden_Hesaplanmali_Ve_Bayat_Tecviz_Alani_Yakalanmali()
    {
        var b = new IleriDonusumUygulaKomutu(Girdi(), Olcekli).Uygula(Geometrili()).Value;
        b = new TecvizGirdileriAyarlaKomutu(OrnekProje.TecvizParselleri(), null).Uygula(b).Value;

        var d = ProjeDegerlendirici.Degerlendir(b, OrnekProje.Topoloji);

        // 200.00 · 1.001² = 200.4002 → 200.40 m² (decimal Gauss, 2 ondalık)
        d.Parseller.Single(p => p.Geometri.ParselId == "K1").AlanM2.Should().Be(200.40m);
        d.EksikGirdiler.Should().Contain(e => e.Contains("alan tutarlılığı", StringComparison.Ordinal), "dönüşüm öncesi girilmiş hesap alanı (200.00) bayat");
    }

    [Fact]
    public void RevisionId_Ileri_Donusum_Yoksa_Eski_Degerini_Korumali_Varsa_Katsayi_Ve_Orijinale_Bagli_Olmali()
    {
        var g = Geometrili();
        var eski = ProjeRevizyonu.Hesapla(new ProjeRevizyonGirdisi("Sentetik", null, OrnekProje.Geometri(), null, [], null, null, []));
        g.RevizyonKimligi().Should().Be(eski, "DNS'siz projelerin RevisionId'si ve onayı korunur");

        var d = new IleriDonusumUygulaKomutu(Girdi(), K).Uygula(g).Value;
        d.RevizyonKimligi().Should().NotBe(eski);
        (d with { IleriDonusum = K with { Cy = -25.4m } }).RevizyonKimligi().Should().NotBe(d.RevizyonKimligi(), "katsayı değişimi");
        var kayik = d.DepremOncesiParseller!.ToArray();
        kayik[0] = kayik[0] with { Koseler = kayik[0].Koseler.Select((v, i) => i == 0 ? v with { X = v.X + 0.000001m } : v).ToArray() };
        (d with { DepremOncesiParseller = kayik }).RevizyonKimligi().Should().NotBe(d.RevizyonKimligi(), "deprem öncesi koordinat PDF'e basılır");
    }

    [Fact]
    public async Task Onayli_Projede_DNS_Uygulaninca_Onay_Dusmeli_Denetimde_Koordinat_Olmamali_AC_PILOT_06()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var onay = new ApprovalService(yol, zaman, OrnekProje.Topoloji);
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        var onceki = yol.Belge.RevizyonKimligi();

        var r = await yol.GonderAsync(new IleriDonusumUygulaKomutu(Girdi(), K), OrnekProje.Muhendis);

        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        yol.Belge.RevizyonKimligi().Should().NotBe(onceki);
        yol.Belge.GecerliOnayVar(yol.Belge.RevizyonKimligi()).Should().BeFalse();
        yol.Denetim.Should().Contain(a => a.Sonuc == AuditSonucu.OnayDustu);
        var kayit = yol.Denetim.Last(a => a.Islem == "IleriDonusumUygula" && a.Sonuc == AuditSonucu.Basarili);
        kayit.Ayrinti.Should().Be("model=Helmert2B;nokta=3");
        string.Join("|", yol.Denetim.Select(a => a.Ayrinti)).Should().NotContain("500013").And.NotContain("0.99999").And.NotContain("N1");
    }

    [Fact]
    public async Task CiktiPaketi_Ileri_Donusumde_Deprem_Oncesi_Orijinal_Sonrasi_Aktif_Koordinatlar_Ve_DNS_Katsayilari_Olmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new IleriDonusumUygulaKomutu(Girdi(), K), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        // Bayat alan girdileri (dönüşüm alanı değiştirir) aktif geometriden yeniden girilir — mühendis akışı.
        var d0 = ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji);
        var k1 = d0.Parseller.Single(p => p.Geometri.ParselId == "K1").AlanM2;
        (await yol.GonderAsync(new TecvizGirdileriAyarlaKomutu([new Imar18.Domain.Mevzuat.Tecviz.TecvizParselGirdisi("K1", 200.00m, k1, OrnekProje.Tecviz())], null), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var onay = new ApprovalService(yol, zaman, OrnekProje.Topoloji);
        var o = await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        o.IsSuccess.Should().BeTrue(o.IsFailure ? o.Error.TechnicalMessage + " " + string.Join(", ", ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji).EksikGirdiler) : string.Empty);

        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji), new HelmertAfinDonusumHesaplayici()).Value;

        p.Donusum.Should().NotBeNull();
        p.Donusum!.IleriDonusum.Should().BeTrue();
        p.DonusumHatasi.Should().BeNull();
        (p.Donusum.Parametreler.A, p.Donusum.Parametreler.B, p.Donusum.Parametreler.C, p.Donusum.Parametreler.D, p.Donusum.Parametreler.Cy, p.Donusum.Parametreler.Cx)
            .Should().Be((0.99999m, -0.00001m, 0.00001m, 0.99999m, -25.5m, 18.25m), "DNS katsayıları birebir");
        p.Donusum.Parametreler.Artiklar.Select(a => (a.NoktaNo, a.Vy, a.Vx)).Should().Equal(("N1", 0.010m, -0.020m), ("N2", -0.010m, 0m), ("N3", 0m, 0.020m));

        // Deprem Sonrası = aktif (dönüştürülmüş) koordinatlar; Deprem Öncesi = aynı nokta numarasıyla orijinal CAD koordinatı.
        var oncesi = p.Donusum.DepremOncesi.ToDictionary(k => k.NoktaNo);
        p.Koordinatlar.Should().HaveCount(oncesi.Count);
        foreach (var s in p.Koordinatlar)
        {
            var o1 = oncesi[s.NoktaNo];
            var ileri = Ileri(K, new GeodeticPoint(X: o1.X, Y: o1.Y));
            (ileri.Y, ileri.X).Should().Be((s.Y, s.X), "nokta " + s.NoktaNo);
        }

        p.Koordinatlar.Should().Contain(k => k.Y == 500_013.5m && k.X == 4_399_969.25m);
        oncesi.Values.Should().Contain(k => k.Y == OrnekProje.Y0 && k.X == OrnekProje.X0);
    }

    [Fact]
    public async Task CiktiPaketi_Ileri_Donusumde_NCZ_Sablonu_Korunmali_Katsayilarla_Ileri_Donusum_Istenmeli_SAHA_017()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var sablon = NczSablonuTests.SentetikNcz();
        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(sablon), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol.GonderAsync(new IleriDonusumUygulaKomutu(Girdi(), new DonusumKatsayilari(1m, 0m, 0m, 1m, 0.10m, -0.20m)), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji), new HelmertAfinDonusumHesaplayici()).Value;

        // SAHA-017 (mühendis talebi): şablon iskelet moduna düşürülmez; yazıcı şablon kayıtlarını aynı .DNS katsayılarıyla taşır.
        p.NczSablonu.Should().NotBeNull();
        p.NczSablonIleriDonusumu.Should().Be(new DonusumKatsayilari(1m, 0m, 0m, 1m, 0.10m, -0.20m));
        p.Uyarilar.Should().Contain(u => u.Contains("PARSELASYON HARİTASI.NCZ", StringComparison.Ordinal) && u.Contains("Deprem Öncesi", StringComparison.Ordinal));
    }

    [Fact]
    public void CadBelgesiDonusumu_Tum_Cizgi_Ve_Yazi_Konumlarini_Donusturmeli_Etiket_Ve_Katmani_Korumali()
    {
        var belge = new CadLayerDocument(CadFormat.Ncz, [new CadLayer(0, "TK_OLUSAN")],
            [new CadPolyline("TK_OLUSAN", "101/1", null, [new GeodeticPoint(X: 4_400_000m, Y: 500_000m), new GeodeticPoint(X: 4_400_010m, Y: 500_000m)], false, 12.5m)],
            [new CadText("TK_OLUSAN", "101/1", new GeodeticPoint(X: 4_400_000m, Y: 500_000m))], 0, 0, 0, []);

        var d = CadBelgesiDonusumu.Uygula(belge, K);

        d.Polylines[0].Vertices[0].Should().Be(new GeodeticPoint(X: 4_399_969.25m, Y: 500_013.5m));
        d.Polylines[0].Vertices[1].Should().Be(Ileri(K, new GeodeticPoint(X: 4_400_010m, Y: 500_000m)));
        d.Texts[0].Position.Should().Be(new GeodeticPoint(X: 4_399_969.25m, Y: 500_013.5m));
        (d.Polylines[0].LayerName, d.Polylines[0].Label, d.Polylines[0].IsClosed).Should().Be(("TK_OLUSAN", "101/1", false));
        d.Polylines[0].DeclaredAreaM2.Should().BeNull("kaynak alan özniteliği deprem öncesine aittir");
        d.Layers.Should().Equal(belge.Layers);
        CadBelgesiDonusumu.Uygula(belge, null).Should().BeSameAs(belge, "dönüşüm yoksa belge aynen kullanılır");
    }
}
