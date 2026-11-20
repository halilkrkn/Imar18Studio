using System.Text;
using FluentAssertions;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Infrastructure.Proje;
using Xunit;

namespace Imar18.Infrastructure.Tests.Proje;

/// <summary>
/// P5.1 — Atomik yerel proje kaydı (FR-F6.10, AC-IO-01): durum + denetim tek dosyada, geçici dosya + atomik yer değiştirme
/// ile yazılır; yazma yarıda kesilirse önceki kayıt bozulmaz; içerik SHA-256 ile doğrulanır, bozuk kayıt sessizce açılmaz.
/// </summary>
public sealed class AtomikJsonProjeDeposuTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "imar18-p5-" + Guid.NewGuid().ToString("N"));

    public AtomikJsonProjeDeposuTests() => Directory.CreateDirectory(_klasor);

    private string Yol => Path.Combine(_klasor, "proje.imar18-pilot.json");

    public void Dispose()
    {
        if (Directory.Exists(_klasor))
        {
            Directory.Delete(_klasor, recursive: true);
        }
    }

    private async Task<ProjeKomutYolu> TamProjeyiDiskeYazAsync(AtomikJsonProjeDeposu depo)
    {
        var yol = (await ProjeKomutYolu.AcAsync(depo, "Sentetik P5 Projesi", new SabitZaman(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero)))).Value;
        foreach (var k in OrnekProje.TamKomutlar())
        {
            (await yol.GonderAsync(k, OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        }

        return yol;
    }

    [Fact]
    public async Task AtomikKayit_Malik_Rehberi_Gidis_Donus_Korunmali_P73_B1()
    {
        var yol = await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        var rehber = new Imar18.Application.Girdi.MalikRehberi(
            [new("M01", "AYŞE YILMAZ", "ALİ", "800000001"), new("M02", "MEHMET ÇAKIR", null, null)],
            [new("K1", "12", "345", "ÖRNEKİL", "ÖRNEKİLÇE", "ÇAMLIK Mah.", "901", "7", 1000.00m)]);
        (await yol.GonderAsync(new MalikRehberiAyarlaKomutu(rehber), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var yuklenen = (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value;

        yuklenen.Belge.MalikRehberi.Should().BeEquivalentTo(rehber);
        yuklenen.Belge.RevizyonKimligi().Should().Be(yol.Belge.RevizyonKimligi());
    }

    [Fact]
    public async Task AtomikKayit_Donusum_Ve_Kunye_Gidis_Donus_Korunmali_RevisionId_Ayni_Kalmali_P73_C2()
    {
        var yol = await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        var donusum = Cikti.FenKlasoruZipTests.Donusum();
        var kunye = new Imar18.Domain.Revizyon.ProjeKunyesi("SENTETİKİL", "ÖRNEKİLÇE", "DENEME", "ÖRNEK BELEDİYESİ", "N99-X-01, N99-X-02", "99999", "ITRF-96 / TM3 33");
        (await yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(donusum), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol.GonderAsync(new ProjeKunyesiAyarlaKomutu(kunye), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var yuklenen = (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value;

        yuklenen.Belge.Donusum.Should().BeEquivalentTo(donusum);
        yuklenen.Belge.Kunye.Should().Be(kunye);
        yuklenen.Belge.RevizyonKimligi().Should().Be(yol.Belge.RevizyonKimligi());
    }

    [Fact]
    public async Task AtomikKayit_Donusum_Ve_Kunyesiz_Eski_Kayit_Null_Ile_Acilmali_P73_C2()
    {
        var yol = await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));

        var yuklenen = (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value;

        yuklenen.Belge.Donusum.Should().BeNull();
        yuklenen.Belge.Kunye.Should().BeNull();
        yuklenen.Belge.RevizyonKimligi().Should().Be(yol.Belge.RevizyonKimligi());
    }

    [Fact]
    public async Task AtomikKayit_Rehbersiz_Eski_Kayit_Bos_Rehberle_Acilmali_P73_B1()
    {
        // P7.3 öncesi kayıtlar "rehber" alanı içermez: geriye uyumlu olarak boş rehberle açılmalı.
        await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        File.ReadAllText(Yol, Encoding.UTF8).Should().NotContain("\"Rehber\":{", "rehber boşken alan yazılmaz");

        var yuklenen = (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value;

        yuklenen.Belge.MalikRehberi.Malikler.Should().BeEmpty();
        yuklenen.Belge.MalikRehberi.Parseller.Should().BeEmpty();
    }

    [Fact]
    public async Task AtomikKayit_Tum_Proje_Durumu_Ve_Denetim_Gidis_Donus_Korunmali()
    {
        var yol = await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));

        var yeniden = (await ProjeKomutYolu.AcAsync(new AtomikJsonProjeDeposu(Yol), "x")).Value;

        yeniden.Revizyon.Should().Be(yol.Revizyon, "geometri, CRS, DOP, tecviz, hak ve kimlik eşlemesi kayıpsız okunmalı");
        yeniden.Belge.Surum.Should().Be(yol.Belge.Surum);
        yeniden.Belge.ProjeAdi.Should().Be("Sentetik P5 Projesi");
        yeniden.Denetim.Should().BeEquivalentTo(yol.Denetim);
    }

    [Fact]
    public async Task AtomikKayit_Onay_Gidis_Donus_Korunmali()
    {
        var depo = new AtomikJsonProjeDeposu(Yol);
        var yol = await TamProjeyiDiskeYazAsync(depo);
        var onay = Imar18.Domain.Revizyon.MuhendisOnayi.Olustur(yol.Revizyon, OrnekProje.Muhendis, new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        (await depo.KaydetAsync(new ProjeKaydi(yol.Belge with { Onay = onay }, yol.Denetim))).IsSuccess.Should().BeTrue();

        var yuklenen = (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value;

        yuklenen.Belge.Onay.Should().Be(onay);
    }

    [Fact]
    public async Task AtomikKayit_Yer_Degistirme_Oncesi_Cokme_Onceki_Kaydi_Bozmamali()
    {
        var depo = new AtomikJsonProjeDeposu(Yol);
        var yol = await TamProjeyiDiskeYazAsync(depo);
        var oncekiIcerik = await File.ReadAllBytesAsync(Yol);
        depo.YerDegistirmeOncesi = () => throw new IOException("Benzetilmiş çökme");

        var sonuc = await yol.GonderAsync(new ProjeAdiDegistirKomutu("Değişmiş Ad"), OrnekProje.Muhendis);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        (await File.ReadAllBytesAsync(Yol)).Should().Equal(oncekiIcerik);
        yol.Belge.ProjeAdi.Should().Be("Sentetik P5 Projesi");

        depo.YerDegistirmeOncesi = null;
        (await yol.GonderAsync(new ProjeAdiDegistirKomutu("Değişmiş Ad"), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value.Belge.ProjeAdi.Should().Be("Değişmiş Ad");
    }

    [Fact]
    public async Task AtomikKayit_Ikinci_Yazimda_Yedek_Dosya_Olusmali_Ve_Gecici_Dosya_Kalmamali()
    {
        await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));

        File.Exists(Yol + ".bak").Should().BeTrue();
        File.Exists(Yol + ".tmp").Should().BeFalse();
        (await new AtomikJsonProjeDeposu(Yol + ".bak").YukleAsync()).IsSuccess.Should().BeTrue("yedek de geçerli bir önceki kayıttır");
    }

    [Fact]
    public async Task AtomikKayit_Icerik_Degistirilmisse_Checksum_Hatasi_Vermeli()
    {
        await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        var metin = await File.ReadAllTextAsync(Yol, Encoding.UTF8);
        await File.WriteAllTextAsync(Yol, metin.Replace("Sentetik P5 Projesi", "Sentetik P5 Projesİ", StringComparison.Ordinal), new UTF8Encoding(false));

        var sonuc = await new AtomikJsonProjeDeposu(Yol).YukleAsync();

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        sonuc.Error.TechnicalMessage.Should().Contain("checksum");
    }

    [Fact]
    public async Task AtomikKayit_NCZ_Sablonu_Gidis_Donus_Korunmali_RevisionId_Ayni_Kalmali_P73_C3()
    {
        var yol = await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        var (sablon, _) = Cad.Ncz.NczParselasyonYaziciTests.SentetikSablon();
        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(sablon), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var yuklenen = (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value;

        yuklenen.Belge.NczSablonu!.Icerik.ToArray().Should().Equal(sablon);
        yuklenen.Belge.NczSablonu.Sha256.Should().Be(yol.Belge.NczSablonu!.Sha256);
        yuklenen.Belge.RevizyonKimligi().Should().Be(yol.Belge.RevizyonKimligi());
    }

    [Fact]
    public async Task AtomikKayit_NCZ_Sablon_Ozeti_Uyusmazsa_Zarf_Gecerli_Olsa_Da_Reddedilmeli_P73_C3()
    {
        var yol = await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        var (sablon, _) = Cad.Ncz.NczParselasyonYaziciTests.SentetikSablon();
        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(sablon), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var metin = await File.ReadAllTextAsync(Yol, Encoding.UTF8);
        var bas = metin.IndexOf("\"icerik\":", StringComparison.Ordinal) + "\"icerik\":".Length;
        var icerik = metin[bas..^1].Replace(yol.Belge.NczSablonu!.Sha256, new string('0', 64), StringComparison.Ordinal);
        var zarf = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(icerik)));
        var eskiZarf = System.Text.RegularExpressions.Regex.Match(metin, "\"icerikSha256\":\"([0-9a-f]{64})\"").Groups[1].Value;
        await File.WriteAllTextAsync(Yol, metin[..bas].Replace(eskiZarf, zarf, StringComparison.Ordinal) + icerik + "}", new UTF8Encoding(false));

        var sonuc = await new AtomikJsonProjeDeposu(Yol).YukleAsync();

        sonuc.IsFailure.Should().BeTrue("şablon baytları saklanan SHA-256 ile doğrulanır");
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        sonuc.Error.TechnicalMessage.Should().Contain("ncz template");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"sema\":\"baska-sema\"}")]
    public async Task AtomikKayit_Bozuk_Dosya_Istisnasiz_Reddedilmeli(string icerik)
    {
        await File.WriteAllTextAsync(Yol, icerik);

        var sonuc = await new AtomikJsonProjeDeposu(Yol).YukleAsync();

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
    }

    [Fact]
    public async Task AtomikKayit_Dosyada_Pilot_Damgasi_Bulunmali()
    {
        await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));

        (await File.ReadAllTextAsync(Yol, Encoding.UTF8)).Should().Contain("PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ");
    }

    /// <summary>İçeriği değiştirir ve zarf özetini yeniden hesaplar (anahtarsız pilot checksum'unu atlatan elle düzenleme benzetimi).</summary>
    private async Task OzetiYenidenHesaplaAsync(Func<string, string> degistir)
    {
        var metin = await File.ReadAllTextAsync(Yol, Encoding.UTF8);
        var bas = metin.IndexOf("\"icerik\":", StringComparison.Ordinal) + "\"icerik\":".Length;
        var icerik = degistir(metin[bas..^1]);
        var ozet = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(icerik)));
        var zarf = metin[..bas];
        var ozetBas = zarf.IndexOf("\"icerikSha256\":\"", StringComparison.Ordinal) + "\"icerikSha256\":\"".Length;
        zarf = zarf[..ozetBas] + ozet + zarf[(ozetBas + 64)..];
        await File.WriteAllTextAsync(Yol, zarf + icerik + "}", new UTF8Encoding(false));
    }

    [Fact]
    public async Task AtomikKayit_Gecerli_Ozetli_Ama_Null_Kimlikli_Kayit_ERR_CIKTI_009_Donmeli()
    {
        await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        await OzetiYenidenHesaplaAsync(i => i.Replace("\"Kod\":\"A1\"", "\"Kod\":null", StringComparison.Ordinal));

        (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        (await ProjeKomutYolu.AcAsync(new AtomikJsonProjeDeposu(Yol), "x")).Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi, "açılış çökmemeli");
    }

    private async Task<ProjeKomutYolu> DnsliProjeyiDiskeYazAsync()
    {
        var yol = await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        (await yol.GonderAsync(new IleriDonusumUygulaKomutu(Cikti.FenKlasoruZipTests.Donusum(), Cikti.FenKlasoruZipTests.DnsK), OrnekProje.Muhendis))
            .IsSuccess.Should().BeTrue();
        return yol;
    }

    [Fact]
    public async Task AtomikKayit_DNS_Ileri_Donusum_Gidis_Donus_Korunmali_v2_Semasi_RevisionId_Ayni_SAHA_014()
    {
        var yol = await DnsliProjeyiDiskeYazAsync();

        var yuklenen = (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value;

        (await File.ReadAllTextAsync(Yol, Encoding.UTF8)).Should().StartWith("{\"sema\":\"" + AtomikJsonProjeDeposu.SemaIleriDonusumlu + "\"");
        yuklenen.Belge.IleriDonusum.Should().Be(Cikti.FenKlasoruZipTests.DnsK);
        yuklenen.Belge.DepremOncesiParseller.Should().BeEquivalentTo(OrnekProje.Geometri(), o => o.WithStrictOrdering());
        yuklenen.Belge.Parseller.Should().BeEquivalentTo(yol.Belge.Parseller, o => o.WithStrictOrdering());
        yuklenen.Belge.RevizyonKimligi().Should().Be(yol.Belge.RevizyonKimligi());

        // Dönüşüm kaldırılınca v1 şemasına dönülür (eski kayıtlarla aynı biçim).
        (await yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(null), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(Yol, Encoding.UTF8)).Should().StartWith("{\"sema\":\"" + AtomikJsonProjeDeposu.Sema + "\"");
        (await new AtomikJsonProjeDeposu(Yol).YukleAsync()).Value.Belge.Parseller.Should().BeEquivalentTo(OrnekProje.Geometri(), o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task AtomikKayit_DNS_Projede_Elle_Degistirilmis_Aktif_Kose_Yuklemede_Reddedilmeli_SAHA_014()
    {
        var yol = await DnsliProjeyiDiskeYazAsync();
        var aktifY = yol.Belge.Parseller[0].Koseler[0].Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var degisik = (yol.Belge.Parseller[0].Koseler[0].Y + 0.001m).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await OzetiYenidenHesaplaAsync(i => i.Replace(aktifY, degisik, StringComparison.Ordinal));

        var sonuc = await new AtomikJsonProjeDeposu(Yol).YukleAsync();

        sonuc.IsFailure.Should().BeTrue("aktif geometri orijinallerin ileri dönüşümü olmalı (architect K2)");
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        sonuc.Error.TechnicalMessage.Should().Contain("forward transformation invariant");
    }

    [Fact]
    public async Task AtomikKayit_v1_Semasinda_Ileri_Donusum_Icerigi_Reddedilmeli_SAHA_014()
    {
        await DnsliProjeyiDiskeYazAsync();
        var metin = await File.ReadAllTextAsync(Yol, Encoding.UTF8);
        await File.WriteAllTextAsync(Yol, metin.Replace(AtomikJsonProjeDeposu.SemaIleriDonusumlu, AtomikJsonProjeDeposu.Sema, StringComparison.Ordinal), new UTF8Encoding(false));

        var sonuc = await new AtomikJsonProjeDeposu(Yol).YukleAsync();

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        sonuc.Error.TechnicalMessage.Should().Contain("schema");
    }

    [Fact]
    public async Task AtomikKayit_Yinelenen_Parsel_Kimligi_Yuklemede_Reddedilmeli()
    {
        await TamProjeyiDiskeYazAsync(new AtomikJsonProjeDeposu(Yol));
        await OzetiYenidenHesaplaAsync(i => i.Replace("\"Id\":\"Y1\"", "\"Id\":\"A1\"", StringComparison.Ordinal));

        var sonuc = await new AtomikJsonProjeDeposu(Yol).YukleAsync();

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        sonuc.Error.TechnicalMessage.Should().Contain("command validation");
    }

    [Fact]
    public async Task AtomikCiktiYazici_Uzerine_Yazmada_Yedek_Dosya_Birakmamali()
    {
        var yol = Path.Combine(_klasor, "fen.pdf");
        var yazici = new Imar18.Infrastructure.Cikti.AtomikCiktiYazici();

        await yazici.YazAsync(yol, new byte[] { 1 });
        await yazici.YazAsync(yol, new byte[] { 2 });

        File.Exists(yol + ".bak").Should().BeFalse();
        (await File.ReadAllBytesAsync(yol)).Should().Equal(2);
    }

    [Fact]
    public async Task AtomikKayit_Dosya_Yoksa_VarMi_False_Olmali()
    {
        (await new AtomikJsonProjeDeposu(Yol).VarMiAsync()).Should().BeFalse();
    }
}
