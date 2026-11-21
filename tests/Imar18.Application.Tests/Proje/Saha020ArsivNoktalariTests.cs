using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Revizyon;
using Xunit;

namespace Imar18.Application.Tests.Proje;

/// <summary>
/// SAHA-020: arşiv onaylı kadastro (TK_PARSEL) köşe noktaları — köşe listesi (tekil, kadastro kimliği sırası, <c>.DNS</c>'te Deprem
/// Öncesi), <c>.NCZ</c> tip 1 noktasından ≤ 0.02 m varsayılan no, toplu (<c>.ncn</c>) aktarım eşlemesi, komut doğrulaması, RevisionId
/// (boşsa değişmez) ve onayın düşmesi (AC-PILOT-06). Sentetik koordinatlar (konum gerçek değil); beklenen değerler elle verilmiştir.
/// </summary>
public sealed class Saha020ArsivNoktalariTests
{
    private const decimal X0 = OrnekProje.X0;
    private const decimal Y0 = OrnekProje.Y0;

    private static GeodeticPoint P(decimal x, decimal y) => new(X0 + x, Y0 + y);

    /// <summary>K1 (0..10 × 0..20) ve K2 (0..10 × 20..30) ortak kenarlı; A1 imar parseli köşe listesine girmez.</summary>
    private static ProjeBelgesi Belge() => ProjeBelgesi.Yeni("Sentetik SAHA-020") with
    {
        Parseller =
        [
            new ParselGeometrisi("K2", ParselRolu.Kadastro, [P(0, 20), P(0, 30), P(10, 30), P(10, 20)]),
            new ParselGeometrisi("K1", ParselRolu.Kadastro, [P(0, 0), P(0, 20), P(10, 20), P(10, 0)]),
            new ParselGeometrisi("A1", ParselRolu.Imar, [P(0, 0), P(0, 5), P(5, 5), P(5, 0)]),
        ],
    };

    [Fact]
    public void Koseler_Kadastro_Kimligi_Sirasiyla_Tekil_Olmali_Imar_Koselerini_Almamali()
    {
        var k = ArsivNoktalari.Koseler(Belge());

        k.Select(x => (x.Sira, x.KadastroParselId, x.Kose)).Should().Equal(
            (1, "K1", P(0, 0)), (2, "K1", P(0, 20)), (3, "K1", P(10, 20)), (4, "K1", P(10, 0)),
            (5, "K2", P(0, 30)), (6, "K2", P(10, 30)));
    }

    [Fact]
    public void Koseler_Ileri_Donusumde_Deprem_Oncesi_Koordinatlardan_Alinmali()
    {
        var oncesi = new ParselGeometrisi("K1", ParselRolu.Kadastro, [P(1, 1), P(1, 2), P(2, 2)]);
        var b = Belge() with
        {
            IleriDonusum = new DonusumKatsayilari(1m, 0m, 0m, 1m, 0m, 0m),
            DepremOncesiParseller = [oncesi],
        };

        ArsivNoktalari.Koseler(b).Select(x => x.Kose).Should().Equal(P(1, 1), P(1, 2), P(2, 2));
    }

    [Fact]
    public void Varsayilan_Tip1_Noktasi_002m_Icindeyse_Adini_Almali_Disindaysa_Sirali_No_Vermeli()
    {
        CadPoint[] cad =
        [
            new("NOKTA", "2652001", P(0.012m, 0.015m)), // ≈ 0.019 m → eşlenir
            new("NOKTA", "2652002", P(0, 20.021m)), // 0.021 m → eşlenmez
            new("NOKTA", "4360117", P(10, 20)),
            new("NOKTA", "4360117", P(10, 30)), // aynı ad ikinci köşeye verilmez
            new("NOKTA", "123456789012345", P(10, 0)), // 15 karakter → kullanılmaz
        ];

        var v = ArsivNoktalari.Varsayilan(Belge(), cad);

        v.Select(x => x.NoktaNo).Should().Equal("2652001", "2", "4360117", "4", "5", "6");
        v[0].Should().Be(new ArsivNoktaKaydi(1, "2652001", Y0, X0, "K1", Y0, X0), "arşiv Y, X varsayılanı köşenin kendisidir");
    }

    [Fact]
    public void Varsayilan_Sirali_No_CAD_Adiyla_Cakisirsa_Yinelenmemeli()
    {
        var v = ArsivNoktalari.Varsayilan(Belge(), [new CadPoint("NOKTA", "4", P(0, 0))]);

        v.Select(x => x.NoktaNo).Should().OnlyHaveUniqueItems("komut yinelenen no'yu reddeder; varsayılan liste her zaman uygulanabilir olmalı");
        v[0].NoktaNo.Should().Be("4");
        v[3].NoktaNo.Should().Be("K4");
    }

    [Fact]
    public void Varsayilan_Projede_Kayitli_Arsiv_Noktasini_Korumali()
    {
        var b = Belge();
        var kayitli = ArsivNoktalari.Varsayilan(b, [])[2] with { NoktaNo = "777", ArsivY = 1m, ArsivX = 2m };
        b = b with { ArsivKadastroNoktalari = [kayitli] };

        var v = ArsivNoktalari.Varsayilan(b, [new CadPoint("NOKTA", "999", P(10, 20))]);

        v[2].Should().Be(kayitli, "kayıtlı numara ve arşiv koordinatı CAD noktasından önceliklidir");
    }

    [Fact]
    public void TopluAktar_Koordinatla_Esleyip_Bulamazsa_Yalniz_Verilmis_Nokta_No_Ile_Eslemeli_Eslesmeyeni_Raporlamali()
    {
        var kayitlar = ArsivNoktalari.Varsayilan(Belge(), []).ToList();
        kayitlar[2] = kayitlar[2] with { NoktaNo = "4360117" }; // mühendisin/CAD'in verdiği no
        ArsivNoktaGirdisi[] satirlar =
        [
            new("901001", Y0 + 0.01m, X0 - 0.01m), // köşe 1'e 0.014 m
            new("4360117", Y0 + 20.5m, X0 + 10.3m), // köşe 3'e 0.58 m → no ile eşlenir (arşiv Y, X satırdan)
            new("5", Y0 + 90m, X0 + 90m), // uzak; "5" köşe 5'in VARSAYILAN no'su → eşlenmez (reviewer Y3)
            new("901999", Y0 + 500m, X0 + 500m), // eşleşmez
        ];

        var s = ArsivNoktalari.TopluAktar(kayitlar, satirlar);

        s.Eslenen.Should().Be(2);
        s.Eslenemeyen.Should().Equal("5", "901999");
        s.Kayitlar[0].Should().Be(kayitlar[0] with { NoktaNo = "901001", ArsivY = Y0 + 0.01m, ArsivX = X0 - 0.01m });
        s.Kayitlar[2].Should().Be(kayitlar[2] with { ArsivY = Y0 + 20.5m, ArsivX = X0 + 10.3m });
        s.Kayitlar[2].Kose.Should().Be(P(10, 20), "eşleme anahtarı (köşe) değişmez");
        s.Kayitlar[4].Should().Be(kayitlar[4], "varsayılan no'lu köşenin arşiv koordinatı ezilmez");
        s.Kayitlar.Should().HaveCount(6);
    }

    [Fact]
    public void TopluAktar_Satir_Sirasindan_Bagimsiz_Ayni_Sonucu_Vermeli_Koordinat_Eslemesi_Oncelikli()
    {
        var kayitlar = ArsivNoktalari.Varsayilan(Belge(), []).ToList();
        kayitlar[2] = kayitlar[2] with { NoktaNo = "4360117" };
        ArsivNoktaGirdisi uzakAyniNo = new("4360117", Y0 + 70m, X0 + 70m);
        ArsivNoktaGirdisi tamKoordinat = new("901003", Y0 + 20m, X0 + 10m);

        var a = ArsivNoktalari.TopluAktar(kayitlar, [uzakAyniNo, tamKoordinat]);
        var b = ArsivNoktalari.TopluAktar(kayitlar, [tamKoordinat, uzakAyniNo]);

        a.Kayitlar.Should().Equal(b.Kayitlar);
        a.Kayitlar[2].NoktaNo.Should().Be("901003", "koordinat eşlemesi no yedeğinden önce gelir");
        a.Eslenemeyen.Should().Equal("4360117");
    }

    [Fact]
    public void Varsayilan_Gecersiz_Bicimli_CAD_Adini_Kullanmamali()
    {
        var v = ArsivNoktalari.Varsayilan(Belge(), [new CadPoint("NOKTA", "Ş1", P(0, 0)), new CadPoint("NOKTA", "A B", P(0, 20))]);

        v.Select(x => x.NoktaNo).Take(2).Should().Equal("1", "2");
    }

    [Fact]
    public void Komut_Kosesinden_100m_Uzak_Arsiv_Koordinatini_YX_Yer_Degistirmesi_Diye_Reddetmeli()
    {
        var k = ArsivNoktalari.Varsayilan(Belge(), []).ToList();
        k[0] = k[0] with { ArsivY = k[0].KoseX, ArsivX = k[0].KoseY }; // Y ile X yer değiştirmiş

        var r = new ArsivNoktalariAyarlaKomutu(k).Uygula(Belge());

        r.IsFailure.Should().BeTrue();
        r.Error.TechnicalMessage.Should().Contain("archive point #1 coordinate is more than 100 m from its corner");
        var sinirda = ArsivNoktalari.Varsayilan(Belge(), []).ToList();
        sinirda[0] = sinirda[0] with { ArsivY = sinirda[0].KoseY + 100m };
        new ArsivNoktalariAyarlaKomutu(sinirda).Uygula(Belge()).IsSuccess.Should().BeTrue("tam 100 m sınırdadır");
    }

    [Theory]
    [InlineData(1e15)]
    [InlineData(-7e27)]
    public void Komut_Asiri_Buyuk_Koordinati_Istisnasiz_Reddetmeli(double deger)
    {
        var k = ArsivNoktalari.Varsayilan(Belge(), []).ToList();
        k[0] = k[0] with { ArsivY = (decimal)deger };

        var r = new ArsivNoktalariAyarlaKomutu(k).Uygula(Belge());

        r.IsFailure.Should().BeTrue("decimal taşması istisna yerine ret olmalı");
        ArsivNoktalari.Uzaklik(new GeodeticPoint(0m, (decimal)deger), P(0, 0)).Should().Be(ArsivNoktalari.TasmaSiniriM);
    }

    [Fact]
    public void Komut_Gecerli_Kayitlari_Sira_Ile_Yazmali_Ayrintida_Yalniz_Sayi_Olmali()
    {
        var kayitlar = ArsivNoktalari.Varsayilan(Belge(), []).Reverse().ToList();
        var komut = new ArsivNoktalariAyarlaKomutu(kayitlar);

        var r = komut.Uygula(Belge());

        r.IsSuccess.Should().BeTrue();
        r.Value.ArsivKadastroNoktalari.Select(k => k.Sira).Should().Equal(1, 2, 3, 4, 5, 6);
        komut.Ayrinti.Should().Be("noktaSayisi=6");
        new ArsivNoktalariAyarlaKomutu([]).Uygula(r.Value).Value.ArsivKadastroNoktalari.Should().BeEmpty("boş liste temizler");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 901001")]
    [InlineData("901 001")]
    [InlineData("123456789012345")]
    [InlineData("NOKTA*1")]
    [InlineData("ŞİŞLİ1")]
    public void Komut_Gecersiz_Nokta_No_Reddetmeli(string no)
    {
        var kayitlar = ArsivNoktalari.Varsayilan(Belge(), []).ToList();
        kayitlar[0] = kayitlar[0] with { NoktaNo = no };

        var r = new ArsivNoktalariAyarlaKomutu(kayitlar).Uygula(Belge());

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
        r.Error.TechnicalMessage.Should().Contain("archive point #1");
    }

    [Fact]
    public void Komut_Yinelenen_Nokta_No_Ve_Yinelenen_Kose_Reddetmeli()
    {
        var k = ArsivNoktalari.Varsayilan(Belge(), []).ToList();

        var ayniNo = k.ToList();
        ayniNo[1] = ayniNo[1] with { NoktaNo = ayniNo[0].NoktaNo };
        var r1 = new ArsivNoktalariAyarlaKomutu(ayniNo).Uygula(Belge());
        r1.IsFailure.Should().BeTrue();
        r1.Error.TechnicalMessage.Should().Contain("archive point #2 number is duplicated");

        var ayniKose = k.ToList();
        ayniKose[1] = ayniKose[1] with { KoseX = ayniKose[0].KoseX, KoseY = ayniKose[0].KoseY };
        new ArsivNoktalariAyarlaKomutu(ayniKose).Uygula(Belge()).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void RevisionId_Bos_Arsivde_Degismemeli_Nokta_No_Ya_Da_Koordinat_Degisince_Degismeli()
    {
        var b = Belge();
        var k = ArsivNoktalari.Varsayilan(b, []);
        var dolu = b with { ArsivKadastroNoktalari = k };

        (b with { ArsivKadastroNoktalari = [] }).RevizyonKimligi().Should().Be(b.RevizyonKimligi(), "arşivsiz eski projelerin kimliği değişmez");
        dolu.RevizyonKimligi().Should().NotBe(b.RevizyonKimligi());
        (b with { ArsivKadastroNoktalari = [.. k.Reverse()] }).RevizyonKimligi().Should().Be(dolu.RevizyonKimligi(), "özet sıra numarasıyla kanoniktir");
        (b with { ArsivKadastroNoktalari = [k[0] with { NoktaNo = "X1" }, .. k.Skip(1)] }).RevizyonKimligi().Should().NotBe(dolu.RevizyonKimligi());
        (b with { ArsivKadastroNoktalari = [k[0] with { ArsivX = k[0].ArsivX + 0.01m }, .. k.Skip(1)] }).RevizyonKimligi().Should().NotBe(dolu.RevizyonKimligi());
    }

    [Fact]
    public async Task Onay_Sonrasi_Arsiv_Noktasi_Degisince_Onay_Dusmeli_Sifirla_Temizlemeli_AC_PILOT_06()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        yol.Belge.GecerliOnayVar(yol.Belge.RevizyonKimligi()).Should().BeTrue();

        var kayitlar = ArsivNoktalari.Varsayilan(yol.Belge, []).Select(k => k with { NoktaNo = "90100" + k.Sira }).ToList();
        (await yol.GonderAsync(new ArsivNoktalariAyarlaKomutu(kayitlar), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        yol.Belge.GecerliOnayVar(yol.Belge.RevizyonKimligi()).Should().BeFalse("arşiv no Fen Klasörü'ne basılır; değişince onay düşer");
        yol.Denetim.Should().Contain(d => d.Islem == "ArsivNoktalariAyarla" && d.Ayrinti == "noktaSayisi=4");
        (await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        yol.Belge.ArsivKadastroNoktalari.Should().BeEmpty();
    }
}
