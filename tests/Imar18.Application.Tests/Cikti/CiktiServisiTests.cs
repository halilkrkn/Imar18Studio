using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Application.Tests.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Numerics;
using Xunit;

namespace Imar18.Application.Tests.Cikti;

/// <summary>
/// P5.3 — Çıktı sözleşmesi (spec 14.2/14.4, AC-PILOT-04..06, AC-FEN-04/05): çıktı yalnızca güncel revizyona bağlı geçerli onayla
/// üretilir; onaysız girişim ve üretim hatası denetime yazılır; başarılı çıktı kaydı ancak SHA-256 checksum hesaplanıp dosya
/// yazıldıktan sonra oluşur. Paket içeriği (4'lü alan, iki yönlü dağıtım, özet) sentetik örnekten elle doğrulanmıştır.
/// </summary>
public sealed class CiktiServisiTests
{
    private sealed class SahteUretici(CiktiTuru tur, Func<CiktiPaketi, Result<byte[]>> uret) : ICiktiUretici
    {
        public CiktiTuru Tur => tur;

        public string Uzanti => ".bin";

        public CiktiPaketi? SonPaket { get; private set; }

        public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default)
        {
            SonPaket = paket;
            return uret(paket);
        }
    }

    private sealed class BellekYazici(BellekProjeDeposu depo) : ICiktiYazici
    {
        public Dictionary<string, byte[]> Dosyalar { get; } = new(StringComparer.Ordinal);

        public List<int> YazimdakiCommitSayisi { get; } = [];

        public bool Bozuk { get; set; }

        public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default)
        {
            if (Bozuk)
            {
                throw new IOException("disk dolu");
            }

            YazimdakiCommitSayisi.Add(depo.CommitSayisi);
            Dosyalar[yol] = icerik.ToArray();
            return Task.CompletedTask;
        }

        public Task SilAsync(string yol, CancellationToken cancellationToken = default)
        {
            Dosyalar.Remove(yol);
            return Task.CompletedTask;
        }
    }

    private static readonly byte[] Icerik = Encoding.UTF8.GetBytes("taslak-pdf-baytlari");

    private static async Task<(ProjeKomutYolu Yol, BellekProjeDeposu Depo, ApprovalService Onay, CiktiServisi Servis, SahteUretici Uretici, BellekYazici Yazici, SabitZaman Zaman)> HazirAsync(
        bool onayla = true,
        Func<CiktiPaketi, Result<byte[]>>? uret = null)
    {
        var (yol, depo, zaman) = await OrnekProje.TamProjeAsync();
        var onay = new ApprovalService(yol, zaman, OrnekProje.Topoloji);
        if (onayla)
        {
            (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        }

        var uretici = new SahteUretici(CiktiTuru.FenKlasoru, uret ?? (_ => Result<byte[]>.Success(Icerik)));
        var yazici = new BellekYazici(depo);
        return (yol, depo, onay, new CiktiServisi(yol, [uretici], yazici, OrnekProje.Topoloji), uretici, yazici, zaman);
    }

    [Fact]
    public async Task FenKlasoru_OnayOlmadan_Uretilmemeli_Ve_Onaysiz_Girisim_Denetlenmeli()
    {
        var h = await HazirAsync(onayla: false);

        var sonuc = await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "cikti.pdf");

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiGecerliOnayYok);
        h.Yazici.Dosyalar.Should().BeEmpty();
        h.Uretici.SonPaket.Should().BeNull();
        h.Yol.Denetim[^1].Should().Match<AuditKaydi>(d =>
            d.Sonuc == AuditSonucu.OnaysizGirisim && d.HataKodu == ErrorCodes.CiktiGecerliOnayYok && d.Islem == "Cikti:FenKlasoru");
    }

    [Fact]
    public async Task FenKlasoru_Veri_Degisip_Onay_Dustukten_Sonra_Uretilmemeli()
    {
        var h = await HazirAsync();
        await h.Yol.GonderAsync(new ProjeAdiDegistirKomutu("Değişti"), OrnekProje.Muhendis);

        (await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "cikti.pdf")).Error.Code.Should().Be(ErrorCodes.CiktiGecerliOnayYok);
    }

    [Fact]
    public async Task FenKlasoru_Onayli_Uretimde_Checksum_Dosyasi_Ve_Basarili_Denetim_Olusmali()
    {
        var h = await HazirAsync();
        var beklenenSha = Convert.ToHexStringLower(SHA256.HashData(Icerik));

        var sonuc = await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "klasor/fen.pdf");

        sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
        sonuc.Value.Sha256.Should().Be(beklenenSha);
        sonuc.Value.Boyut.Should().Be(Icerik.Length);
        sonuc.Value.Revizyon.Should().Be(h.Yol.Revizyon);
        h.Yazici.Dosyalar["klasor/fen.pdf"].Should().Equal(Icerik);
        Encoding.UTF8.GetString(h.Yazici.Dosyalar["klasor/fen.pdf.sha256"]).Should().Be(beklenenSha + "  fen.pdf\n");
        var kayit = h.Yol.Denetim[^1];
        kayit.Should().Match<AuditKaydi>(d => d.Islem == "Cikti:FenKlasoru" && d.Sonuc == AuditSonucu.Basarili && d.SonrakiRevizyon == h.Yol.Revizyon.Deger);
        kayit.Ayrinti.Should().Contain("sha256=" + beklenenSha);
    }

    [Fact]
    public async Task FenKlasoru_PdfChecksum_Olmadan_BasariliCikti_AuditLog_Kaydi_Olusmamali()
    {
        var h = await HazirAsync();
        var commitOnce = h.Depo.CommitSayisi;

        await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf");

        h.Yazici.YazimdakiCommitSayisi.Should().OnlyContain(c => c == commitOnce, "dosya ve checksum yazılmadan başarılı denetim commit'i olmamalı");
        h.Depo.CommitSayisi.Should().Be(commitOnce + 1);
    }

    [Fact]
    public async Task FenKlasoru_Uretici_Hatasi_Reddedilen_Girisim_Olarak_Denetlenmeli()
    {
        var h = await HazirAsync(uret: _ => Result<byte[]>.Failure(ErrorCatalog.Get(ErrorCodes.CiktiFenKlasoruSablon)));

        var sonuc = await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf");

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiFenKlasoruSablon);
        h.Yazici.Dosyalar.Should().BeEmpty();
        h.Yol.Denetim[^1].Should().Match<AuditKaydi>(d => d.Sonuc == AuditSonucu.Reddedildi && d.HataKodu == ErrorCodes.CiktiFenKlasoruSablon);
    }

    [Fact]
    public async Task FenKlasoru_Yazma_Hatasinda_Basarili_Kayit_Olusmamali()
    {
        var h = await HazirAsync();
        h.Yazici.Bozuk = true;

        var sonuc = await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf");

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
        h.Yol.Denetim.Should().NotContain(d => d.Islem == "Cikti:FenKlasoru" && d.Sonuc == AuditSonucu.Basarili);
        h.Yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task FenKlasoru_Tanimsiz_Cikti_Turu_Reddedilmeli()
    {
        var h = await HazirAsync();

        (await h.Servis.UretAsync(CiktiTuru.Cetveller, OrnekProje.Muhendis, "c.xlsx")).Error.Code.Should().Be(ErrorCodes.CiktiFenKlasoruSablon);
    }

    [Fact]
    public async Task KullaniciOnayi_Cikti_Uretildikten_Sonra_GeriAlinirsa_Revizyon_Gerekli_Uyarisi_Vermeli()
    {
        var h = await HazirAsync();
        await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf");

        var geriAl = await h.Onay.GeriAlAsync(OrnekProje.Muhendis);

        geriAl.Value.NihaiCiktiUretilmisti.Should().BeTrue();
        h.Yazici.Dosyalar.Should().ContainKey("fen.pdf", "geri alma eski çıktıyı silmez");
    }

    [Fact]
    public async Task CiktiPaketi_Pilot_Damgasi_Revizyon_Ve_Onay_Bilgisi_Tasimali()
    {
        var h = await HazirAsync();

        await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf");
        var p = h.Uretici.SonPaket!;

        p.PilotDamgasi.Should().Be("PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ");
        p.RevizyonId.Should().Be(h.Yol.Revizyon.Deger);
        p.OnaylayanId.Should().Be(OrnekProje.Muhendis);
        p.OnayZamani.Should().Be(h.Zaman.Simdi);
        p.ProjeAdi.Should().Be("Sentetik P5 Projesi");
        p.OlcekPaydasi.Should().Be(1000);
        p.CrsAciklamasi.Should().Contain("ITRF96").And.Contain("33");
    }

    [Fact]
    public async Task CiktiPaketi_Koordine_Ozet_Ortak_Noktalari_Tek_Numarayla_Vermeli()
    {
        var p = (await PaketAsync());

        p.Koordinatlar.Should().HaveCount(6, "K1, A1 ve Y1 köşeleri 6 farklı noktadır");
        p.Koordinatlar.Select(k => k.NoktaNo).Should().Equal(1, 2, 3, 4, 5, 6);
        p.Koordinatlar[0].Should().Be(new KoordinatSatiri(1, OrnekProje.Y0, OrnekProje.X0));
        var k1 = p.Parseller.Single(x => x.GeciciKod == "K1");
        var a1 = p.Parseller.Single(x => x.GeciciKod == "A1");
        k1.NoktaNolari.Should().Equal(1, 2, 3, 4);
        a1.NoktaNolari[0].Should().Be(1, "A1'in ilk köşesi K1'in ilk köşesiyle aynıdır");
    }

    [Fact]
    public async Task CiktiPaketi_Cepheli_Alan_Kenar_Uzunluklari_Ve_HeM2Dm2_Dogru_Olmali()
    {
        var p = await PaketAsync();

        var k1 = p.Parseller.Single(x => x.GeciciKod == "K1");
        k1.Kenarlar.Select(k => k.CepheM).Should().Equal(20.00m, 10.00m, 20.00m, 10.00m);
        k1.HesapAlaniM2.Should().Be(200.00m);
        var a1 = p.Parseller.Single(x => x.GeciciKod == "A1");
        a1.TescilNo.Should().Be("101/1");
        a1.Alan.Should().Be(new HeM2Dm2(0, 120, 0));
        p.Parseller.Select(x => x.Rol).Should().Equal(ParselRolu.Kadastro, ParselRolu.Imar, ParselRolu.YolTerk);
    }

    [Fact]
    public async Task CiktiPaketi_Dengeleme_Cetveli_Y1_Uzerinde_Dengelenmeli()
    {
        var p = await PaketAsync();

        p.Dengeleme.Should().NotBeNull();
        p.Dengeleme!.TapuAlaniM2.Should().Be(200.00m);
        p.Dengeleme.HesaplananM2.Should().Be(200.00m);
        p.Dengeleme.FarkM2.Should().Be(0m);
        p.Dengeleme.TecvizSiniriM2.Should().Be(1.00m);
        p.Dengeleme.Satirlar.Should().BeEquivalentTo(
            [new DengelemeSatiri("A1", "101/1", 120.00m, 0m, 120.00m), new DengelemeSatiri("Y1", "YOL", 80.00m, 0m, 80.00m)],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task CiktiPaketi_Ozet_DOP_Ve_Iki_Yonlu_Dagitim_Dogru_Olmali()
    {
        var p = await PaketAsync();

        p.Dop.Should().BeEquivalentTo(new DopOzeti(200.00m, 0m, 120.00m, 0m, 0m, 0m, 80.00m, 80.00m, 0.4000000m, 0.0000000m, 0.4000000m));
        p.MalikSirali.Should().BeEquivalentTo(
            [
                new MalikDagitimSatiri("K1", "MLK-001", 100.00m, 40.00m, 60.00m, "101/1", 60.00m),
                new MalikDagitimSatiri("K1", "MLK-002", 100.00m, 40.00m, 60.00m, "101/1", 60.00m),
            ],
            o => o.WithStrictOrdering());
        p.ImarSirali.Should().HaveCount(2);
        p.ImarSirali.Should().OnlyContain(s => s.TescilNo == "101/1" && s.Hisse == "6000/12000" && s.AlanM2 == 60.00m && s.ParselAlaniM2 == 120.00m);
        p.ImarSirali.Select(s => s.MalikKodu).Should().Equal("MLK-001", "MLK-002");
    }

    [Fact]
    public void CksEslemesi_Pilot_1_Sentetik_DOP_Ornegini_Vermeli()
    {
        // test-data/synthetic/pilot-1-concave-parcel/DOP_Ornek.CKS (anonim) ile aynı değerler.
        var dop = DopCalculator.Hesapla(DopGirdisi.Olustur(8699.44m, 7620.54m).Value).Value;

        var cks = CksOzetiEslemesi.Olustur(DopOzeti.From(dop)).Value;

        cks.ToplamDuzenlemeyeGirenDopAlinanM2.Should().Be(8699.44m);
        cks.ToplamImarAdaAlaniM2.Should().Be(7620.54m);
        cks.ToplamTahsisM2.Should().Be(7620.54m);
        cks.KamuyaAyrilanAlanM2.Should().Be(1078.90m);
        cks.Dopo.Should().Be(0.1240195m);
        cks.DopoOkoIleBirlikte.Should().Be(0.1240195m);
        cks.MaksimumDopOrani.Should().Be(0.45m);
        cks.ToplamKadastroParselAlaniM2.Should().Be(8699.44m);
    }

    [Fact]
    public void CksEslemesi_D1_D2_R3_Sifirdan_Farkliysa_Reddedilmeli()
    {
        var dop = DopCalculator.Hesapla(DopGirdisi.Olustur(1000m, 600m, d1: 100m).Value).Value;

        var sonuc = CksOzetiEslemesi.Olustur(DopOzeti.From(dop));

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiCadDisaAktarim);
    }

    private static async Task<CiktiPaketi> PaketAsync()
    {
        var h = await HazirAsync();
        (await h.Servis.UretAsync(CiktiTuru.FenKlasoru, OrnekProje.Muhendis, "fen.pdf")).IsSuccess.Should().BeTrue();
        return h.Uretici.SonPaket!;
    }
}
