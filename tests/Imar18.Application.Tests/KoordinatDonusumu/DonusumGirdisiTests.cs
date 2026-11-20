using FluentAssertions;
using Imar18.Application.KoordinatDonusumu;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Revizyon;
using Imar18.GeometryEngine.Crs;
using Xunit;

namespace Imar18.Application.Tests.KoordinatDonusumu;

/// <summary>
/// P7.3-C2: ortak nokta / dönüşüm girdisi — komut doğrulaması, RevisionId bağı ve onay düşmesi, satır metni ayrıştırma,
/// DonusumServisi (Helmert/Afin bağımsız referans parametreleri ve ters dönüşüm) ve denetimde koordinat olmaması.
/// </summary>
public sealed class DonusumGirdisiTests
{
    // Bağımsız referans Helmert (I → II): Y2 = a·Y1 + b·X1 + ty ; X2 = −b·Y1 + a·X1 + tx.
    private const decimal Ha = 0.9999950m;
    private const decimal Hb = -0.00000500m;
    private const decimal Htx = 12.50m;
    private const decimal Hty = -20.00m;

    private static DonusumOrtakNoktasi Helmert(string no, decimal y1, decimal x1) =>
        new(no, y1, x1, (Ha * y1) + (Hb * x1) + Hty, (-Hb * y1) + (Ha * x1) + Htx);

    // Bağımsız referans Afin (Netcad .DNS düzeni, SAHA-014): Y2 = A·Y1 + C·X1 + CY ; X2 = B·Y1 + D·X1 + CX
    // → A = 1.0001, C = 0.0002, B = −0.0003, D = 0.9998 (asimetrik: B/C yer değiştirmesini yakalar).
    private static DonusumOrtakNoktasi Afin(string no, decimal y1, decimal x1) =>
        new(no, y1, x1, (1.0001m * y1) + (0.0002m * x1) - 3m, (-0.0003m * y1) + (0.9998m * x1) + 7m);

    private static IReadOnlyList<DonusumOrtakNoktasi> Dortgen(Func<string, decimal, decimal, DonusumOrtakNoktasi> f) =>
    [
        f("P1", 500_000m, 4_000_000m), f("P2", 504_000m, 4_000_500m), f("P3", 503_500m, 4_010_000m), f("P4", 493_000m, 4_003_000m),
    ];

    private static readonly DonusumServisi Servis = new(new HelmertAfinDonusumHesaplayici());

    [Fact]
    public void DonusumServisi_Helmert_Bilinen_Parametreleri_Kesin_Bulmali_Artik_Sifir_Olmali()
    {
        var p = Servis.ParametreleriHesapla(new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Dortgen(Helmert))).Value;

        // SAHA-014 gerekçeli beklenti: Netcad düzeninde Y2 = A·Y1 + C·X1 → C = Hb; X2 = B·Y1 + D·X1 → B = −Hb
        // (P7.3-C2 etiketi B/C'yi yer değiştirmiş yazıyordu; gerçek .DNS ile doğrulandı).
        p.A.Should().BeApproximately(Ha, 1e-12m);
        p.B.Should().BeApproximately(-Hb, 1e-12m);
        p.C.Should().BeApproximately(Hb, 1e-12m);
        p.D.Should().BeApproximately(Ha, 1e-12m);
        p.Cy.Should().BeApproximately(Hty, 1e-6m);
        p.Cx.Should().BeApproximately(Htx, 1e-6m);
        p.OlcekKatsayisi!.Value.Should().BeApproximately(0.9999950000125m, 1e-13m, "√(a² + b²) = a + b²/(2a)");
        p.Artiklar.Should().HaveCount(4).And.OnlyContain(a => Math.Abs(a.Vy) < 1e-6m && Math.Abs(a.Vx) < 1e-6m);
        p.OrtalamaHataM!.Value.Should().BeLessThan(1e-6m);
    }

    [Fact]
    public void DonusumServisi_Afin_Bilinen_Parametreleri_Kesin_Bulmali_Olcek_Ve_Donukluk_Tanimsiz_Olmali()
    {
        var p = Servis.ParametreleriHesapla(new DonusumGirdisi(DonusumModeli.Afin2B, 0.09m, Dortgen(Afin))).Value;

        p.A.Should().BeApproximately(1.0001m, 1e-12m);
        p.B.Should().BeApproximately(-0.0003m, 1e-12m);
        p.C.Should().BeApproximately(0.0002m, 1e-12m);
        p.D.Should().BeApproximately(0.9998m, 1e-12m);
        p.Cy.Should().BeApproximately(-3m, 1e-5m);
        p.Cx.Should().BeApproximately(7m, 1e-5m);
        p.OlcekKatsayisi.Should().BeNull();
        p.DonuklukGrad.Should().BeNull();
    }

    [Fact]
    public void DonusumServisi_Deprem_Oncesi_Ters_Donusumle_Bulunmali_Ve_Ileri_Donusumle_Geri_Donmeli()
    {
        var girdi = new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Dortgen(Helmert));
        // Bağımsız referans: öncesi (501234.50, 4005678.25) → ileri dönüşüm → sonrası; servis sonrasından öncesiyi bulmalı.
        var oncesiY = 501_234.50m;
        var oncesiX = 4_005_678.25m;
        var sonrasi = new KoordinatSatiri(7, (Ha * oncesiY) + (Hb * oncesiX) + Hty, (-Hb * oncesiY) + (Ha * oncesiX) + Htx);

        var ozet = Servis.Hesapla(girdi, [sonrasi]).Value;

        ozet.DepremOncesi.Should().ContainSingle().Which.Should().Be(new KoordinatSatiri(7, oncesiY, oncesiX));
        var ileri = ozet.Parametreler.Uygula(new GeodeticPoint(X: oncesiX, Y: oncesiY));
        ileri.Y.Should().BeApproximately(sonrasi.Y, 1e-6m);
        ileri.X.Should().BeApproximately(sonrasi.X, 1e-6m);
    }

    [Fact]
    public void DonusumServisi_Tekrar_Edilebilir_Ve_Nokta_Sirasindan_Bagimsiz_Olmali()
    {
        var a = Servis.ParametreleriHesapla(new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Dortgen(Helmert))).Value;
        var b = Servis.ParametreleriHesapla(new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Dortgen(Helmert).Reverse().ToArray())).Value;

        (b.A, b.B, b.C, b.D, b.Cy, b.Cx).Should().Be((a.A, a.B, a.C, a.D, a.Cy, a.Cx));
    }

    [Theory]
    [InlineData(DonusumModeli.Helmert2B, 1)]
    [InlineData(DonusumModeli.Afin2B, 2)]
    public void DonusumServisi_Yetersiz_Nokta_ERR_CRS_004_Donmeli(DonusumModeli model, int adet)
    {
        var r = Servis.ParametreleriHesapla(new DonusumGirdisi(model, 0.09m, Dortgen(Helmert).Take(adet).ToArray()));

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
    }

    [Fact]
    public void DonusumServisi_Esdogrusal_Afin_Noktalari_ERR_CRS_004_Donmeli()
    {
        var r = Servis.ParametreleriHesapla(new DonusumGirdisi(DonusumModeli.Afin2B, 0.09m,
            [Afin("1", 500_000m, 4_000_000m), Afin("2", 500_100m, 4_000_100m), Afin("3", 500_200m, 4_000_200m)]));

        r.Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
    }

    [Fact]
    public void DonusumParametreleri_Tekil_Matriste_Ters_Donusum_ERR_CRS_004_Donmeli()
    {
        var p = new DonusumParametreleri(DonusumModeli.Afin2B, 1m, 2m, 2m, 4m, 0m, 0m, null, null, null, []);

        p.TersUygula(new GeodeticPoint(1m, 1m)).Error.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
    }

    [Fact]
    public void DonusumMetni_Gecerli_Satirlari_InvariantCulture_Ile_Okumali_Yorum_Ve_Bos_Satiri_Atlamali()
    {
        var r = DonusumMetni.Ayristir("# başlık\r\nP101;500000.57;4000000.88;500000.15;4000000.52\n\n  51 ; -1.5 ; 2 ; 3 ; 4.25  ");

        r.Value.Should().Equal(
            new DonusumOrtakNoktasi("P101", 500000.57m, 4000000.88m, 500000.15m, 4000000.52m),
            new DonusumOrtakNoktasi("51", -1.5m, 2m, 3m, 4.25m));
        DonusumMetni.Ayristir(DonusumMetni.Yaz(r.Value)).Value.Should().Equal(r.Value, "yazma–okuma gidiş-dönüşü kayıpsız");
    }

    [Theory]
    [InlineData("N1;500000,57;4000000.88;500000.15;4000000.52")]
    [InlineData("N1;500000.57;4000000.88;500000.15")]
    [InlineData(";1;2;3;4")]
    [InlineData("N1;1;2;3;4;5")]
    [InlineData("N1;1e3;2;3;4")]
    public void DonusumMetni_Hatali_Satir_Satir_Numarasiyla_Reddedilmeli(string satir)
    {
        var r = DonusumMetni.Ayristir("# yorum\n" + satir);

        r.IsFailure.Should().BeTrue();
        r.Error.UserMessage.Should().Contain("satırı 2").And.Contain("NoktaNo;Y1;X1;Y2;X2");
    }

    [Fact]
    public void DonusumMetni_Bos_Metin_Bos_Liste_Donmeli()
    {
        DonusumMetni.Ayristir(null).Value.Should().BeEmpty();
        DonusumMetni.Ayristir("   \n# yalnız yorum").Value.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    [InlineData("10.01")]
    public void DonusumKomutu_Gecersiz_MK_Reddedilmeli(string mk)
    {
        var g = new DonusumGirdisi(DonusumModeli.Helmert2B, decimal.Parse(mk, System.Globalization.CultureInfo.InvariantCulture), Dortgen(Helmert));

        new DonusumGirdisiAyarlaKomutu(g).Uygula(ProjeBelgesi.Yeni("x")).Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
    }

    [Fact]
    public void DonusumKomutu_Sinirlar_Tekil_Nokta_No_Ve_En_Az_Nokta_Denetlenmeli()
    {
        var bos = ProjeBelgesi.Yeni("x");
        DonusumGirdisiAyarlaKomutu K(DonusumModeli m, decimal mk, IReadOnlyList<DonusumOrtakNoktasi> n) => new(new DonusumGirdisi(m, mk, n));

        K(DonusumModeli.Helmert2B, 10m, Dortgen(Helmert).Take(2).ToArray()).Uygula(bos).IsSuccess.Should().BeTrue("MK üst sınırı 10 m dahil, Helmert en az 2 nokta");
        K(DonusumModeli.Afin2B, 0.01m, Dortgen(Helmert).Take(2).ToArray()).Uygula(bos).IsFailure.Should().BeTrue("Afin en az 3 nokta");
        K(DonusumModeli.Helmert2B, 0.09m, [Helmert("A", 1m, 1m), Helmert(" A ", 2m, 2m)]).Uygula(bos).IsFailure.Should().BeTrue("kırpılmış nokta no tekil olmalı");
        K(DonusumModeli.Helmert2B, 0.09m, [Helmert(" ", 1m, 1m), Helmert("B", 2m, 2m)]).Uygula(bos).IsFailure.Should().BeTrue("boş nokta no");
        K((DonusumModeli)99, 0.09m, Dortgen(Helmert)).Uygula(bos).IsFailure.Should().BeTrue("tanımsız model");
        new DonusumGirdisiAyarlaKomutu(new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, null!)).Uygula(bos).IsFailure.Should().BeTrue();
        K(DonusumModeli.Helmert2B, 0.09m, Enumerable.Range(0, 501).Select(i => Helmert("P" + i, i, i)).ToArray()).Uygula(bos).IsFailure.Should().BeTrue("en çok 500 nokta");
        new DonusumGirdisiAyarlaKomutu(null).Uygula(bos with { Donusum = new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Dortgen(Helmert)) })
            .Value.Donusum.Should().BeNull("null girdi dönüşümü temizler");
        K(DonusumModeli.Helmert2B, 0.09m, [Helmert(" N1 ", 1m, 1m), Helmert("N2", 2m, 2m)]).Uygula(bos).Value.Donusum!.OrtakNoktalar[0].NoktaNo.Should().Be("N1");
    }

    [Fact]
    public void ProjeKunyesiKomutu_Alanlari_Kirpmali_Ve_120_Karakter_Sinirini_Denetlemeli()
    {
        var bos = ProjeBelgesi.Yeni("x");
        var k = new ProjeKunyesi(" İL ", "İLÇE", "MAH", "BEL", "P1", "ID", "ITRF");

        new ProjeKunyesiAyarlaKomutu(k).Uygula(bos).Value.Kunye!.Il.Should().Be("İL");
        new ProjeKunyesiAyarlaKomutu(k with { Ilce = new string('a', 120) }).Uygula(bos).IsSuccess.Should().BeTrue();
        new ProjeKunyesiAyarlaKomutu(k with { Ilce = new string('a', 121) }).Uygula(bos).IsFailure.Should().BeTrue();
        new ProjeKunyesiAyarlaKomutu(k with { Belediye = null! }).Uygula(bos).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void RevisionId_Donusum_Ve_Kunye_Girilmemisse_Eski_Degerini_Korumali_Girilince_Degismeli()
    {
        var belge = ProjeBelgesi.Yeni("Sentetik");
        var eski = ProjeRevizyonu.Hesapla(new ProjeRevizyonGirdisi("Sentetik", null, [], null, [], null, null, []));
        var donusum = new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Dortgen(Helmert));

        belge.RevizyonKimligi().Should().Be(eski, "P7.3 öncesi projelerin onayları düşmemeli");
        var d1 = (belge with { Donusum = donusum }).RevizyonKimligi();
        d1.Should().NotBe(eski);
        (belge with { Donusum = donusum with { Mk = 0.10m } }).RevizyonKimligi().Should().NotBe(d1, "MK değişimi");
        (belge with { Donusum = donusum with { Model = DonusumModeli.Afin2B } }).RevizyonKimligi().Should().NotBe(d1, "model değişimi");
        var kayik = donusum.OrtakNoktalar.ToArray();
        kayik[0] = kayik[0] with { X2 = kayik[0].X2 + 0.001m };
        (belge with { Donusum = donusum with { OrtakNoktalar = kayik } }).RevizyonKimligi().Should().NotBe(d1, "1 mm koordinat değişimi");
        (belge with { Donusum = donusum with { OrtakNoktalar = donusum.OrtakNoktalar.Reverse().ToArray() } }).RevizyonKimligi()
            .Should().Be(d1, "nokta sırası içerik değildir");
        (belge with { Kunye = new ProjeKunyesi("A", "B", "C", "D", "E", "F", "G") }).RevizyonKimligi().Should().NotBe(eski);
    }

    [Fact]
    public async Task Onayli_Projede_Donusum_Degisince_Onay_Dusmeli_Denetimde_Koordinat_Olmamali_AC_PILOT_06()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        yol.Belge.GecerliOnayVar(yol.Revizyon).Should().BeTrue();

        var r = await yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Dortgen(Helmert))), OrnekProje.Muhendis);

        r.IsSuccess.Should().BeTrue();
        yol.Belge.GecerliOnayVar(yol.Revizyon).Should().BeFalse("dönüşüm RevisionId'ye dahildir");
        var kayit = yol.Denetim.Last(k => k.Islem == "DonusumGirdisiAyarla" && k.Sonuc == AuditSonucu.Basarili);
        yol.Denetim.Should().Contain(k => k.Sonuc == AuditSonucu.OnayDustu);
        kayit.Ayrinti.Should().Be("model=Helmert2B;nokta=4");
        string.Join("|", yol.Denetim.Select(k => k.Ayrinti)).Should().NotContain("500000").And.NotContain("4000000");
    }

    [Fact]
    public async Task CiktiPaketi_Donusum_Hesaplanamazsa_Hata_Paket_Uzerinde_Tasinmali_Diger_Ciktilar_Etkilenmemeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var esdogrusal = new DonusumGirdisi(DonusumModeli.Afin2B, 0.09m,
            [Afin("1", 500_000m, 4_000_000m), Afin("2", 500_100m, 4_000_100m), Afin("3", 500_200m, 4_000_200m)]);
        (await yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(esdogrusal), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue("yapısal olarak geçerli");
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji), new HelmertAfinDonusumHesaplayici());
        var hesaplayicisiz = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji));

        // Reviewer M1: paket kurulur (Excel/CKS üretilebilir); hata yalnız dönüşüme bağlı Fen Klasörü .zip'i engeller.
        p.IsSuccess.Should().BeTrue();
        p.Value.Donusum.Should().BeNull();
        p.Value.DonusumHatasi!.Code.Should().Be(ErrorCodes.CrsDonusumHesaplanamadi);
        p.Value.Uyarilar.Should().Contain(u => u.Contains("Dönüşüm hesaplanamadı", StringComparison.Ordinal));
        hesaplayicisiz.IsSuccess.Should().BeTrue();
        hesaplayicisiz.Value.DonusumHatasi.Should().BeNull();
        hesaplayicisiz.Value.Donusum.Should().BeNull();
        hesaplayicisiz.Value.Uyarilar.Should().Contain(u => u.Contains("Dönüşüm hesaplayıcısı", StringComparison.Ordinal));
    }

    [Fact]
    public void DonusumServisi_Afin_Ters_Donusum_Ileri_Donusumle_Geri_Donmeli()
    {
        // Asimetrik Afin matrisi B/C yer değiştirme hatasını yakalar.
        var ozet = Servis.Hesapla(new DonusumGirdisi(DonusumModeli.Afin2B, 0.09m, Dortgen(Afin)), [new KoordinatSatiri(3, 501_000.00m, 4_002_000.00m)]).Value;

        var o = ozet.DepremOncesi.Single();
        var ileri = Afin("x", o.Y, o.X);
        ileri.Y2.Should().BeApproximately(501_000.00m, 0.006m);
        ileri.X2.Should().BeApproximately(4_002_000.00m, 0.006m);
    }

    [Fact]
    public void DonusumServisi_Gurultulu_Noktada_Artik_Hesaplanan_Eksi_Ikinci_Sistem_Olmali()
    {
        // Helmert'e 4 noktada simetrik ±5 cm gürültü: hesaplanan − II. sistem işareti ve Y/X ayrımı.
        var n = Dortgen(Helmert).ToArray();
        n[0] = n[0] with { Y2 = n[0].Y2 + 0.05m };
        n[2] = n[2] with { Y2 = n[2].Y2 - 0.05m };
        var p = Servis.ParametreleriHesapla(new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, n)).Value;

        var a1 = p.Artiklar.Single(a => a.NoktaNo == "P1");
        a1.Vy.Should().BeNegative("II. sistem +5 cm kaydırıldı; V = hesaplanan − II");
        (a1.Y - a1.Vy).Should().Be(n[0].Y2, "artık satırındaki Y hesaplanan koordinattır");
        Math.Abs(a1.Vy).Should().BeGreaterThan(Math.Abs(a1.Vx));
        p.OrtalamaHataM!.Value.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task CiktiPaketi_Ada_Parsel_Bicimsiz_Imar_Tescil_No_Uyari_Uretmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new ParselKimlikEslemesiAyarlaKomutu([new ParselKimlikCifti("A1", "1001"), new ParselKimlikCifti("Y1", "YOL")]), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji)).Value;

        p.Uyarilar.Should().Contain("Tescil no ada/parsel biçiminde değil, tescil sayfasına alınmadı: 1001");
    }

    [Fact]
    public async Task ProjeyiSifirla_Donusum_Ve_Kunyeyi_Temizlemeli()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        await yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(new DonusumGirdisi(DonusumModeli.Helmert2B, 0.09m, Dortgen(Helmert))), OrnekProje.Muhendis);
        await yol.GonderAsync(new ProjeKunyesiAyarlaKomutu(new ProjeKunyesi("A", "B", "C", "D", "E", "F", "G")), OrnekProje.Muhendis);

        (await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        yol.Belge.Donusum.Should().BeNull();
        yol.Belge.Kunye.Should().BeNull();
    }

    [Fact]
    public async Task CiktiPaketi_Kadastro_Ozeti_Hisse_TAM_Veya_Pay_Payda_Ve_Haklar_Tasimali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji)).Value;

        var k1 = p.KadastroParselleri.Should().ContainSingle().Subject;
        k1.ParselId.Should().Be("K1");
        k1.TapuAlaniM2.Should().Be(200.00m);
        k1.Malikler.Select(m => (m.MalikKodu, m.Hisse)).Should().Equal(("MLK-001", "1/2"), ("MLK-002", "1/2"));
        k1.Malikler.Sum(m => m.GirenM2).Should().Be(200.00m);
        k1.Malikler.Sum(m => m.NetHakM2).Should().Be(k1.Malikler.Sum(m => m.GirenM2 - m.DopKesintisiM2));
        CiktiPaketiOlusturucu.HisseMetni(new Domain.Numerics.Fraction(3, 3)).Should().Be("TAM");
    }
}
