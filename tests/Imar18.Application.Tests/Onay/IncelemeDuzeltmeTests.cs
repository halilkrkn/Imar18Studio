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

namespace Imar18.Application.Tests.Onay;

/// <summary>
/// P5 bağımsız inceleme (reviewer) bulgularının regresyon testleri: H1 onay alanının yalnızca ApprovalService ile değişmesi,
/// H2 kendini kesen parsel kritik engeli, H3 alan çapraz tutarlılığı ve Y1 dengelenmiş alanın çıktıya basılması,
/// M1/M2 çıktı denetim izi bütünlüğü, M3 geri almanın kilit altında yakalanması.
/// </summary>
public sealed class IncelemeDuzeltmeTests
{
    private sealed record YetkisizOnayKomutu(DateTimeOffset Zaman) : IProjeKomutu
    {
        public string Ad => "SahteOnay";

        public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut) =>
            Result<ProjeBelgesi>.Success(mevcut with { Onay = MuhendisOnayi.Olustur(mevcut.RevizyonKimligi(), "x", Zaman) });
    }

    [Fact]
    public async Task CommandBus_Onay_Alanini_Degistiren_Yetkisiz_Komut_Reddedilmeli_Ve_Denetlenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        await yol.GonderAsync(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(200.00m, 100.00m).Value), OrnekProje.Muhendis);

        var sonuc = await yol.GonderAsync(new YetkisizOnayKomutu(zaman.Simdi), OrnekProje.Muhendis);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.GirdProjeGirdisiGecersiz);
        yol.Belge.Onay.Should().BeNull();
        yol.Denetim[^1].Should().Match<AuditKaydi>(d => d.Islem == "SahteOnay" && d.Sonuc == AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task KullaniciOnayi_Kendini_Kesen_Parsel_Varsa_Kritik_Engel_Olmali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var g = OrnekProje.Geometri().ToArray();
        // Papyon (eşit olmayan loblar): 1 → 2 → 3 → 4 kenarları kesişir.
        g[1] = new ParselGeometrisi("A1", ParselRolu.Imar,
        [
            new GeodeticPoint(OrnekProje.X0, OrnekProje.Y0), new GeodeticPoint(OrnekProje.X0 + 10m, OrnekProje.Y0 + 12m),
            new GeodeticPoint(OrnekProje.X0 + 10m, OrnekProje.Y0), new GeodeticPoint(OrnekProje.X0, OrnekProje.Y0 + 4m),
        ]);
        (await yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(g), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var servis = new ApprovalService(yol, zaman);

        servis.Degerlendir().Bulgular.Should().Contain(b => b.Code == ErrorCodes.TopoKendiUzerindeKesisim && b.Severity == ErrorSeverity.Critical);
        (await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
    }

    [Fact]
    public async Task KullaniciOnayi_Hak_Imar_Alani_Geometriden_Farkliysa_Engellenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var hak = OrnekProje.Hak();
        await yol.GonderAsync(new HakEslestirmeAyarlaKomutu(hak with
        {
            ImarParselleri = [new ImarParseli("A1", 120.01m)],
            Eslestirme = [new HakEslestirmeSatiri("MLK-001", "K1", "A1", 60.01m), new HakEslestirmeSatiri("MLK-002", "K1", "A1", 60.00m)],
        }), OrnekProje.Muhendis);

        var d = new ApprovalService(yol, zaman).Degerlendir();

        d.Tamamlandi.Should().BeFalse();
        d.EksikGirdiler.Should().Contain("alan tutarlılığı");
    }

    [Fact]
    public async Task KullaniciOnayi_Tecviz_Hesap_Alani_Kadastro_Gauss_Alanindan_Farkliysa_Engellenmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        await yol.GonderAsync(new TecvizGirdileriAyarlaKomutu([new TecvizParselGirdisi("K1", 200.00m, 199.99m, OrnekProje.Tecviz())], null), OrnekProje.Muhendis);

        new ApprovalService(yol, zaman).Degerlendir().EksikGirdiler.Should().Contain("alan tutarlılığı");
    }

    /// <summary>Tapu 200.50, hesap 200.00 (fark +0.50 ≤ tecviz 1.00) → Y1 80.00 + 0.50 = 80.50; DOPO = 80.50 / 200.50 = 0.4014963.</summary>
    private static async Task<(ProjeKomutYolu Yol, SabitZaman Zaman)> FarkliTapuluProjeAsync()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        await yol.GonderAsync(new TecvizGirdileriAyarlaKomutu([new TecvizParselGirdisi("K1", 200.50m, 200.00m, OrnekProje.Tecviz())], null), OrnekProje.Muhendis);
        await yol.GonderAsync(new DopGirdisiAyarlaKomutu(DopGirdisi.Olustur(200.50m, 120.00m).Value), OrnekProje.Muhendis);
        await yol.GonderAsync(new HakEslestirmeAyarlaKomutu(new HakEslestirmeGirdisi(
            0.4014963m,
            [new KadastroParseli("K1", 200.50m, [new KadastroMaliki("MLK-001", new Fraction(1, 2), true), new KadastroMaliki("MLK-002", new Fraction(1, 2), false)])],
            [new ImarParseli("A1", 120.00m)],
            [new HakEslestirmeSatiri("MLK-001", "K1", "A1", 60.00m), new HakEslestirmeSatiri("MLK-002", "K1", "A1", 60.00m)])), OrnekProje.Muhendis);
        return (yol, zaman);
    }

    [Fact]
    public async Task FenKlasoru_Alan_Tablosu_Y1_Dengelenmis_Alani_Gostermeli_Tecviz_Farki_Sifirdan_Farkli()
    {
        var (yol, zaman) = await FarkliTapuluProjeAsync();
        var servis = new ApprovalService(yol, zaman);
        var d = servis.Degerlendir();
        d.Tamamlandi.Should().BeTrue(string.Join(" | ", d.EksikGirdiler.Concat(d.Bulgular.Select(b => b.TechnicalMessage))));
        (await servis.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        var p = CiktiPaketiOlusturucu.Olustur(yol.Belge, servis.Degerlendir()).Value;

        var y1 = p.Parseller.Single(s => s.GeciciKod == "Y1");
        y1.HesapAlaniM2.Should().Be(80.00m);
        y1.DengelenmisAlanM2.Should().Be(80.50m);
        y1.Alan.Should().Be(new HeM2Dm2(0, 80, 50));
        p.Parseller.Single(s => s.GeciciKod == "A1").Alan.Should().Be(new HeM2Dm2(0, 120, 0), "imar parseli dengelenmez (+0.00)");
        p.Parseller.Where(s => s.Rol != ParselRolu.Kadastro).Sum(s => s.DengelenmisAlanM2).Should().Be(200.50m, "toplam tapu alanına eşit olmalı");
    }

    // ---------------- M1 / M2: çıktı denetim izi ----------------

    private sealed class SahteUretici(Func<CiktiPaketi, Result<byte[]>> uret) : ICiktiUretici
    {
        public CiktiTuru Tur => CiktiTuru.FenKlasoru;

        public string Uzanti => ".pdf";

        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default) => uret(paket);
    }

    private sealed class Yazici : ICiktiYazici
    {
        public Dictionary<string, byte[]> Dosyalar { get; } = new(StringComparer.Ordinal);

        public Func<string, bool> BozukMu { get; set; } = _ => false;

        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default)
        {
            if (BozukMu(yol))
            {
                throw new IOException("disk dolu");
            }

            Dosyalar[yol] = icerik.ToArray();
            return Task.CompletedTask;
        }

        public Task SilAsync(string yol, CancellationToken cancellationToken = default)
        {
            Dosyalar.Remove(yol);
            return Task.CompletedTask;
        }
    }

    private static async Task<(ProjeKomutYolu Yol, ApprovalService Onay)> OnayliAsync()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var onay = new ApprovalService(yol, zaman);
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        return (yol, onay);
    }

    [Fact]
    public async Task FenKlasoru_Checksum_Yazimi_Basarisizsa_Ana_Dosya_Silinmeli()
    {
        var (yol, _) = await OnayliAsync();
        var yazici = new Yazici { BozukMu = y => y.EndsWith(".sha256", StringComparison.Ordinal) };
        var servis = new CiktiServisi(yol, [new SahteUretici(_ => Result<byte[]>.Success([1]))], yazici);

        var sonuc = await servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf");

        sonuc.IsFailure.Should().BeTrue();
        yazici.Dosyalar.Should().BeEmpty("denetim izi olmayan çıktı diskte kalmamalı");
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task FenKlasoru_Uretici_Istisnasi_Reddedilen_Girisim_Olarak_Denetlenmeli()
    {
        var (yol, _) = await OnayliAsync();
        var servis = new CiktiServisi(yol, [new SahteUretici(_ => throw new InvalidOperationException("beklenmeyen"))], new Yazici());

        var sonuc = await servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf");

        sonuc.IsFailure.Should().BeTrue();
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task FenKlasoru_Iptal_Edilirse_Dosya_Kalmamali_Ve_Red_Denetlenmeli()
    {
        var (yol, _) = await OnayliAsync();
        using var cts = new CancellationTokenSource();
        var yazici = new Yazici();
        var servis = new CiktiServisi(yol, [new SahteUretici(_ =>
        {
            cts.Cancel();
            return Result<byte[]>.Success([1]);
        })], yazici);

        var act = () => servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf", cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        yazici.Dosyalar.Should().BeEmpty();
        yol.Denetim[^1].Should().Match<AuditKaydi>(d => d.Sonuc == AuditSonucu.Reddedildi && d.Islem == "Cikti:FenKlasoru");
    }

    [Fact]
    public async Task FenKlasoru_Uretim_Sirasinda_Onay_Duserse_Basarili_Kayit_Olusmamali()
    {
        var (yol, _) = await OnayliAsync();
        var yazici = new Yazici();
        var servis = new CiktiServisi(yol, [new SahteUretici(_ =>
        {
            yol.GonderAsync(new ProjeAdiDegistirKomutu("Araya giren değişiklik"), OrnekProje.Muhendis).GetAwaiter().GetResult();
            return Result<byte[]>.Success([1]);
        })], yazici);

        var sonuc = await servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf");

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiGecerliOnayYok);
        yazici.Dosyalar.Should().BeEmpty();
        yol.Denetim.Should().NotContain(d => d.Islem == "Cikti:FenKlasoru" && d.Sonuc == AuditSonucu.Basarili);
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.OnaysizGirisim);
    }

    [Fact]
    public async Task KullaniciOnayi_GeriAlma_Geri_Alinan_Onayin_Revizyonunu_Dondurmeli()
    {
        var (yol, onay) = await OnayliAsync();
        var rev = yol.Revizyon;

        var sonuc = await onay.GeriAlAsync(OrnekProje.Muhendis);

        sonuc.Value.Revizyon.Should().Be(rev);
    }

    [Fact]
    public async Task KullaniciOnayi_Tam_24_Saatte_GeriAlinabilmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var onay = new ApprovalService(yol, zaman);
        await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni);
        zaman.Ilerlet(ApprovalService.GeriAlmaSuresi);

        (await onay.GeriAlAsync(OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
    }
}
