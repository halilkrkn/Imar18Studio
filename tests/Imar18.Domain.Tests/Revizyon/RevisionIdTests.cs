using FluentAssertions;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;
using Xunit;

namespace Imar18.Domain.Tests.Revizyon;

/// <summary>
/// P3.3 — RevisionId (SHA-256) ve onay bağı (D16, AC-PILOT-06): aynı veri aynı kimliği, herhangi bir parsel/hisse/alan
/// değişikliği farklı kimliği üretir; onay yalnızca bağlandığı revizyon için geçerlidir.
/// </summary>
public sealed class RevisionIdTests
{
    private static HakEslestirmeGirdisi Girdi(decimal a2Alani = 400.00m, decimal dopo = 0.2m) => new(
        dopo,
        [
            new KadastroParseli("K1", 1000.00m,
                [new KadastroMaliki("M1", new Fraction(1, 2), true), new KadastroMaliki("M2", new Fraction(1, 2), false)]),
        ],
        [new ImarParseli("A1", 400.00m), new ImarParseli("A2", a2Alani)],
        [new HakEslestirmeSatiri("M1", "K1", "A1", 400.00m), new HakEslestirmeSatiri("M2", "K1", "A2", a2Alani)]);

    [Fact]
    public void RevisionId_Ayni_Veri_Ayni_64_Haneli_Kucuk_Harf_Hex_Uretmeli()
    {
        var a = Girdi().RevizyonKimligi();
        var b = Girdi().RevizyonKimligi();

        a.Should().Be(b);
        a.Deger.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void RevisionId_Alan_1_dm2_Degisirse_Farkli_Olmali()
    {
        Girdi(400.01m).RevizyonKimligi().Should().NotBe(Girdi().RevizyonKimligi());
    }

    [Fact]
    public void RevisionId_DOPO_Degisirse_Farkli_Olmali()
    {
        Girdi(dopo: 0.2000001m).RevizyonKimligi().Should().NotBe(Girdi().RevizyonKimligi());
    }

    [Fact]
    public void RevisionId_Hisse_Veya_Muvafakat_Degisirse_Farkli_Olmali()
    {
        var g = Girdi();
        var muvafakatsiz = g with
        {
            KadastroParselleri =
            [
                new KadastroParseli("K1", 1000.00m,
                    [new KadastroMaliki("M1", new Fraction(1, 2), false), new KadastroMaliki("M2", new Fraction(1, 2), false)]),
            ],
        };

        muvafakatsiz.RevizyonKimligi().Should().NotBe(g.RevizyonKimligi());
    }

    [Fact]
    public void RevisionId_Girdi_Sirasi_Ve_Decimal_Olcegi_Kimligi_Degistirmemeli()
    {
        var g = Girdi();
        var tersVeOlcekli = g with
        {
            ImarParselleri = [new ImarParseli("A2", 400.0m), new ImarParseli("A1", 400m)],
            Eslestirme = g.Eslestirme.Reverse().ToList(),
        };

        tersVeOlcekli.RevizyonKimligi().Should().Be(g.RevizyonKimligi());
    }

    [Fact]
    public void RevizyonOzeti_Alan_Sinirlari_Belirsizlik_Uretmemeli()
    {
        var a = new RevizyonOzetiOlusturucu().Ekle("ab", "c").Olustur();
        var b = new RevizyonOzetiOlusturucu().Ekle("a", "bc").Olustur();

        a.Should().NotBe(b);
    }

    [Fact]
    public void MuhendisOnayi_Bagli_Oldugu_Revizyonda_Gecerli_Veri_Degisince_Otomatik_Gecersiz_Olmali()
    {
        var onay = MuhendisOnayi.Olustur(Girdi().RevizyonKimligi(), "MUH-01", new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));

        onay.GecerliMi(Girdi().RevizyonKimligi()).Should().BeTrue();
        onay.GecerliMi(Girdi(400.01m).RevizyonKimligi()).Should().BeFalse("AC-PILOT-06: veri değişince onay düşer");
    }

    [Fact]
    public void MuhendisOnayi_Bos_Onaylayan_Reddedilmeli()
    {
        var act = () => MuhendisOnayi.Olustur(Girdi().RevizyonKimligi(), " ", DateTimeOffset.UnixEpoch);

        act.Should().Throw<ArgumentException>();
    }

    private static RevisionId Hesap(decimal tecvizSiniri = 21.02m, string kaynak = "Netcad", decimal r3 = 0m, decimal? toplam = null)
    {
        var tecviz = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, tecvizSiniri, kaynak).Value;
        return HesapRevizyonu.Hesapla(
            Girdi(),
            DopGirdisi.Olustur(1000m, 800m, r3: r3).Value,
            [new TecvizParselGirdisi("K1", 1000m, 999.88m, tecviz)],
            toplam);
    }

    [Fact]
    public void HesapRevizyonu_Tecviz_Siniri_Kaynak_Notu_DOP_Girdisi_Veya_Toplam_Sinir_Degisirse_Onay_Dusmeli()
    {
        var temel = Hesap();
        Hesap().Should().Be(temel);

        var onay = MuhendisOnayi.Olustur(temel, "MUH-01", DateTimeOffset.UnixEpoch);
        onay.GecerliMi(Hesap(tecvizSiniri: 21.03m)).Should().BeFalse();
        onay.GecerliMi(Hesap(kaynak: "TKGM")).Should().BeFalse();
        onay.GecerliMi(Hesap(r3: 1m)).Should().BeFalse();
        onay.GecerliMi(Hesap(toplam: 21.02m)).Should().BeFalse();
        onay.GecerliMi(Hesap()).Should().BeTrue();
    }

    [Fact]
    public void RevisionId_Yinelenen_Kimlikte_Bile_Girdi_Sirasindan_Bagimsiz_Olmali()
    {
        var g = Girdi();
        var a = g with { ImarParselleri = [new ImarParseli("A1", 1m), new ImarParseli("A1", 2m)] };
        var b = g with { ImarParselleri = [new ImarParseli("A1", 2m), new ImarParseli("A1", 1m)] };

        a.RevizyonKimligi().Should().Be(b.RevizyonKimligi());
    }

    [Fact]
    public void RevisionId_Null_Malik_Listesi_ArgumentException_Vermeli()
    {
        var g = Girdi() with { KadastroParselleri = [new KadastroParseli("K1", 1m, null!)] };

        var act = () => g.RevizyonKimligi();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RevisionId_Gecersiz_Hex_Ile_Kurulamamali()
    {
        RevisionId.TryParse("xyz", out _).Should().BeFalse();
        RevisionId.TryParse(new string('A', 64), out _).Should().BeFalse("küçük harf kanonik biçimdir");
        RevisionId.TryParse(new string('a', 64), out var id).Should().BeTrue();
        id.Deger.Should().HaveLength(64);
    }
}
