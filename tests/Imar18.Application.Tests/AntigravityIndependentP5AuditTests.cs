using System.Globalization;
using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;
using Xunit;

namespace Imar18.Application.Tests;

/// <summary>
/// Faz P5 (P5.1–P5.3) Antigravity Bağımsız Test ve Denetim Paketi.
/// <list type="bullet">
/// <item>CommandBus (ProjeKomutYolu): atomik commit, rollback, tek yazıcı senkronizasyonu, kullanıcı kimliği sınırları ve reddedilen girişimlerin audit kaydı.</item>
/// <item>ApprovalService: Kritik hata varken onay engeli (AC-PILOT-05), onay metni tam doğrulaması (AC-ONAY-02), 1 mm köşe hareketi / CRS / proje adı değişiminde RevisionId ile onayın otomatik düşmesi (AC-PILOT-06), 24 saat geri alma penceresi.</item>
/// <item>CiktiServisi: Onaysız girişimlerin ve revizyon uyuşmazlıklarının AuditLog'a OnaysizGirisim olarak yazılması ve ERR-CIKTI-007 ile engellenmesi.</item>
/// </list>
/// </summary>
public sealed class AntigravityIndependentP5AuditTests
{
    private static async Task<(ProjeKomutYolu Yol, BellekProjeDeposu Depo, ApprovalService Onay, SabitZaman Zaman)> HazirProjeAsync()
    {
        var (yol, depo, zaman) = await OrnekProje.TamProjeAsync();
        var onay = new ApprovalService(yol, zaman, OrnekProje.Topoloji);
        return (yol, depo, onay, zaman);
    }

    // =========================================================================
    // 1. CommandBus (ProjeKomutYolu) Bağımsız Sınır ve Atomiklik Testleri
    // =========================================================================

    [Fact]
    public async Task CommandBus_Kullanici_Kimligi_Sinir_Testi_64_Karakter_Kabul_65_Karakter_Ret()
    {
        var (yol, _, _, _) = await HazirProjeAsync();

        var tam64 = new string('m', 64);
        var sonuc64 = await yol.GonderAsync(new ProjeAdiDegistirKomutu("Yeni Ad 64"), tam64);
        sonuc64.IsSuccess.Should().BeTrue("64 karakterlik kullanıcı kimliği izin verilen üst sınırdır.");

        var tam65 = new string('m', 65);
        var act65 = () => yol.GonderAsync(new ProjeAdiDegistirKomutu("Yeni Ad 65"), tam65);
        await act65.Should().ThrowAsync<ArgumentException>("65 karakter kullanıcı kimliği sınır aşımıdır.");
    }

    [Fact]
    public async Task CommandBus_Depo_Yazma_Hatasi_Durumunda_Bellek_Tamamen_Geri_Alinmali()
    {
        var (yol, depo, _, _) = await HazirProjeAsync();
        var baslangicBelge = yol.Belge;
        var baslangicRevizyon = yol.Revizyon;
        var baslangicDenetimSayisi = yol.Denetim.Count;

        depo.SonrakiYazmayiBozdur = true;
        var sonuc = await yol.GonderAsync(new ProjeAdiDegistirKomutu("Bozuk Commit Denemesi"), OrnekProje.Muhendis);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);

        // Bellek durumu mutasyona uğramamış olmalı (Rollback)
        yol.Belge.Should().BeSameAs(baslangicBelge);
        yol.Revizyon.Should().Be(baslangicRevizyon);
        yol.Denetim.Should().HaveCount(baslangicDenetimSayisi);

        // Bir sonraki geçerli işlem sorunsuz yürütülebilmeli
        var basariliSonuc = await yol.GonderAsync(new ProjeAdiDegistirKomutu("Basarili Ad"), OrnekProje.Muhendis);
        basariliSonuc.IsSuccess.Should().BeTrue();
        yol.Belge.ProjeAdi.Should().Be("Basarili Ad");
    }

    [Fact]
    public async Task CommandBus_Denetim_Sira_Numaralari_Deliksiz_Ve_Monoton_Artmali()
    {
        var (yol, _, _, _) = await HazirProjeAsync();

        // Paralel olarak 15 farklı geçerli komut gönder
        var gorevler = Enumerable.Range(1, 15)
            .Select(i => Task.Run(() => yol.GonderAsync(new ProjeAdiDegistirKomutu($"Paralel Proje Adı {i}"), $"muhendis-{i:00}")))
            .ToArray();

        await Task.WhenAll(gorevler);

        var siralar = yol.Denetim.Select(d => d.Sira).ToList();
        for (var i = 0; i < siralar.Count; i++)
        {
            siralar[i].Should().Be(i + 1, "Denetim sıra numaraları 1'den başlayıp boşluksuz artmalıdır.");
        }
    }

    private sealed record YetkisizOnayMutasyonKomutu : IProjeKomutu
    {
        public string Ad => "YetkisizOnayEnjeksiyonu";
        public string? Ayrinti => null;
        public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut) =>
            Result<ProjeBelgesi>.Success(mevcut with { Onay = MuhendisOnayi.Olustur(mevcut.RevizyonKimligi(), "hacker", DateTimeOffset.UtcNow) });
    }

    [Fact]
    public async Task CommandBus_IOnayKomutu_Olmayan_Komutla_Onay_Alanini_Degistirme_Girisimi_Engellenmeli()
    {
        var (yol, _, _, _) = await HazirProjeAsync();
        var oncekiOnay = yol.Belge.Onay;

        var sonuc = await yol.GonderAsync(new YetkisizOnayMutasyonKomutu(), OrnekProje.Muhendis);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
        yol.Belge.Onay.Should().Be(oncekiOnay);

        var sonAudit = yol.Denetim[^1];
        sonAudit.Islem.Should().Be("YetkisizOnayEnjeksiyonu");
        sonAudit.Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    // =========================================================================
    // 2. ApprovalService & AC-PILOT-05 Kritik Hata Onay Engeli Testleri
    // =========================================================================

    [Fact]
    public async Task ApprovalService_Kendini_Kesen_Parsel_Var_Ise_Onay_Kritik_Hata_Ile_Engellenmeli()
    {
        var (yol, _, onay, _) = await HazirProjeAsync();

        // K1 parselini kendini kesen eşit olmayan loblu (papyon) geometriye dönüştür
        var kumSaati = new ParselGeometrisi("K1", ParselRolu.Kadastro,
        [
            new GeodeticPoint(OrnekProje.X0, OrnekProje.Y0),
            new GeodeticPoint(OrnekProje.X0 + 20m, OrnekProje.Y0 + 10m),
            new GeodeticPoint(OrnekProje.X0 + 20m, OrnekProje.Y0),
            new GeodeticPoint(OrnekProje.X0, OrnekProje.Y0 + 4m)
        ]);

        var geomList = OrnekProje.Geometri().Select(g => g.ParselId == "K1" ? kumSaati : g).ToArray();
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(geomList), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var deg = onay.Degerlendir();
        deg.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TopoKendiUzerindeKesisim && b.Severity == ErrorSeverity.Critical);

        var sonuc = await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
        yol.Belge.Onay.Should().BeNull();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task ApprovalService_DOP_Yuzde_45_Asildiginda_Kritik_Hata_Ile_Onay_Engellenmeli()
    {
        var (yol, _, onay, _) = await HazirProjeAsync();

        // R1 = 200, R2 = 90 => Kamu = 110 => DOPO = 110 / 200 = 0.55 > 0.45 (Kritik DOP aşımı)
        var dopGirdisi = DopGirdisi.Olustur(200.00m, 90.00m).Value;
        (await yol.GonderAsync(new DopGirdisiAyarlaKomutu(dopGirdisi), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var deg = onay.Degerlendir();
        deg.Bulgular.Should().Contain(b => b.Code == ErrorCodes.DopYuzde45Asildi && b.Severity == ErrorSeverity.Critical);

        var sonuc = await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
    }

    [Fact]
    public async Task ApprovalService_Tecviz_Siniri_Asildiginda_Kritik_Hata_Ile_Onay_Engellenmeli()
    {
        var (yol, _, onay, _) = await HazirProjeAsync();

        // Tecviz sınırı 1.00 m², tapu 200 m², hesap 202 m² (|202-200| = 2.00 > 1.00)
        var asimTecviz = new[] { new TecvizParselGirdisi("K1", 200.00m, 202.00m, OrnekProje.Tecviz()) };
        (await yol.GonderAsync(new TecvizGirdileriAyarlaKomutu(asimTecviz, null), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var deg = onay.Degerlendir();
        deg.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TecvizSiniriAsildi && b.Severity == ErrorSeverity.Critical);

        var sonuc = await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
    }

    [Fact]
    public async Task ApprovalService_Md17_1d_Hukmu_Ihlalinde_Kritik_Hata_Ile_Onay_Engellenmeli()
    {
        var (yol, _, onay, _) = await HazirProjeAsync();

        // MLK-001 ve MLK-002 müşterek paydaş, MLK-002'nin muvafakati yok (false) ve tek başına müstakil parsele veriliyor
        var ihlalliHak = new HakEslestirmeGirdisi(
            0.4m,
            [new KadastroParseli("K1", 200.00m, [new KadastroMaliki("MLK-001", new Fraction(1, 2), true), new KadastroMaliki("MLK-002", new Fraction(1, 2), false)])],
            [new ImarParseli("A1", 60.00m), new ImarParseli("A2", 60.00m)],
            [new HakEslestirmeSatiri("MLK-001", "K1", "A1", 60.00m), new HakEslestirmeSatiri("MLK-002", "K1", "A2", 60.00m)]);

        (await yol.GonderAsync(new HakEslestirmeAyarlaKomutu(ihlalliHak), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        var deg = onay.Degerlendir();
        deg.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TapuMuvafakatsizMustakilTahsis && b.Severity == ErrorSeverity.Critical);

        var sonuc = await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
    }

    [Fact]
    public async Task ApprovalService_Onay_Metni_Harfi_Harfine_Eslesmeli_Kucuk_Sapmalar_Reddedilmeli()
    {
        var (_, _, onay, _) = await HazirProjeAsync();

        // 1. Boş metin
        (await onay.OnaylaAsync(OrnekProje.Muhendis, "")).Error.Code.Should().Be(ErrorCodes.CiktiOnayMetniKabulEdilmedi);

        // 2. Sondaki nokta eksik
        var noktasiz = ApprovalService.OnayMetni.TrimEnd('.');
        (await onay.OnaylaAsync(OrnekProje.Muhendis, noktasiz)).Error.Code.Should().Be(ErrorCodes.CiktiOnayMetniKabulEdilmedi);

        // 3. Başta/sonda fazladan boşluk
        (await onay.OnaylaAsync(OrnekProje.Muhendis, " " + ApprovalService.OnayMetni)).Error.Code.Should().Be(ErrorCodes.CiktiOnayMetniKabulEdilmedi);

        // 4. Birebir doğru metin
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // 3. AC-PILOT-06 Onayın RevisionId Bağı ve Otomatik Düşme Testleri
    // =========================================================================

    [Fact]
    public async Task ApprovalService_Geometride_1mm_Kose_Hareketi_RevisionId_Degistirip_Onayi_Dusurmeli()
    {
        var (yol, _, onay, _) = await HazirProjeAsync();
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        onay.GecerliOnayVar.Should().BeTrue();
        var ilkRevizyon = yol.Revizyon;

        // İmar parselinin 1 köşesini sadece 1 mm (0.001 m) Doğuya kaydır
        var geom = OrnekProje.Geometri().ToArray();
        var a1Koseler = geom[1].Koseler.ToArray();
        a1Koseler[1] = a1Koseler[1] with { Y = a1Koseler[1].Y + 0.001m };
        geom[1] = geom[1] with { Koseler = a1Koseler };

        var gonder = await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(geom), OrnekProje.Muhendis);
        gonder.IsSuccess.Should().BeTrue();

        yol.Revizyon.Should().NotBe(ilkRevizyon, "1 mm köşe hareketi yeni bir SHA-256 RevisionId üretmelidir.");
        yol.Belge.Onay.Should().BeNull("Veri değiştiğinde onay derhal null yapılmalıdır.");
        onay.GecerliOnayVar.Should().BeFalse();

        var sonAudit = yol.Denetim[^1];
        sonAudit.Sonuc.Should().Be(AuditSonucu.OnayDustu);
        sonAudit.OncekiRevizyon.Should().Be(ilkRevizyon.Deger);
        sonAudit.SonrakiRevizyon.Should().Be(yol.Revizyon.Deger);
    }

    [Fact]
    public async Task ApprovalService_CRS_Parametresi_Degistiginde_Onay_Otomatik_Dusmeli()
    {
        var (yol, _, onay, _) = await HazirProjeAsync();
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        // Dilim meridyenini 33'ten 30'a değiştir
        var yeniCrs = ProjeCrs.Olustur(GeodetikDatum.Itrf96, 30, 2005.00m, 1000).Value;
        (await yol.GonderAsync(new ProjeCrsAyarlaKomutu(yeniCrs), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        onay.GecerliOnayVar.Should().BeFalse();
        yol.Belge.Onay.Should().BeNull();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.OnayDustu);
    }

    [Fact]
    public async Task ApprovalService_Proje_Adi_Degistiginde_Onay_Otomatik_Dusmeli()
    {
        var (yol, _, onay, _) = await HazirProjeAsync();
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        (await yol.GonderAsync(new ProjeAdiDegistirKomutu("Yeni Proje Başlığı"), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        onay.GecerliOnayVar.Should().BeFalse();
        yol.Belge.Onay.Should().BeNull();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.OnayDustu);
    }

    // =========================================================================
    // 4. Onay Geri Alma (24 Saat Penceresi) Testleri
    // =========================================================================

    [Fact]
    public async Task ApprovalService_23_Saat_59_Dakikada_Geri_Alma_Basarili_24_Saat_1_Saniyede_Engellenmeli()
    {
        var (yol, _, onay, zaman) = await HazirProjeAsync();
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        // 23 saat 59 dakika ilerlet
        zaman.Ilerlet(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        var geriAl1 = await onay.GeriAlAsync(OrnekProje.Muhendis);
        geriAl1.IsSuccess.Should().BeTrue("24 saat dolmadan geri alma geçerli olmalıdır.");
        yol.Belge.Onay.Should().BeNull();

        // Tekrar onayla
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        // 24 saat 1 saniye ilerlet
        zaman.Ilerlet(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var geriAl2 = await onay.GeriAlAsync(OrnekProje.Muhendis);
        geriAl2.IsFailure.Should().BeTrue();
        geriAl2.Error.Code.Should().Be(ErrorCodes.CiktiOnayGeriAlinamaz);
        yol.Belge.Onay.Should().NotBeNull("24 saat geçtikten sonra onay geri alınamamalıdır.");
    }

    // =========================================================================
    // 5. CiktiServisi ve Onaysız Girişim Audit Kaydı Testleri
    // =========================================================================

    private sealed class DummyCiktiUretici : ICiktiUretici
    {
        public CiktiTuru Tur => CiktiTuru.FenKlasoru;
        public string Uzanti => ".pdf";
        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default) =>
            Result<byte[]>.Success([0x25, 0x50, 0x44, 0x46]); // %PDF
    }

    private sealed class BellekCiktiYazici : ICiktiYazici
    {
        public Dictionary<string, byte[]> Dosyalar { get; } = new(StringComparer.Ordinal);
        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default)
        {
            Dosyalar[yol] = icerik.ToArray();
            return Task.CompletedTask;
        }
        public Task SilAsync(string yol, CancellationToken cancellationToken = default)
        {
            Dosyalar.Remove(yol);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CiktiServisi_Onaysiz_Uretim_Denemesinde_OnaysizGirisim_AuditLogu_Ve_Hata_Donmeli()
    {
        var (yol, _, _, _) = await HazirProjeAsync();
        yol.Belge.Onay.Should().BeNull(); // Onay verilmedi!

        var uretici = new DummyCiktiUretici();
        var yazici = new BellekCiktiYazici();
        var ciktiServisi = new CiktiServisi(yol, [uretici], yazici, OrnekProje.Topoloji);

        var sonuc = await ciktiServisi.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "test_cikti.pdf");

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiGecerliOnayYok);
        yazici.Dosyalar.Should().BeEmpty();

        var sonKayit = yol.Denetim[^1];
        sonKayit.Sonuc.Should().Be(AuditSonucu.OnaysizGirisim);
        sonKayit.HataKodu.Should().Be(ErrorCodes.CiktiGecerliOnayYok);
        sonKayit.Islem.Should().Be("Cikti:FenKlasoru");
    }
}
