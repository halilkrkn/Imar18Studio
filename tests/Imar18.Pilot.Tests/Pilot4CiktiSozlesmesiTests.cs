using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Revizyon;
using Imar18.Infrastructure.Proje;
using Imar18.Pilot.Tests.Destek;
using UglyToad.PdfPig;
using Xunit;

namespace Imar18.Pilot.Tests;

/// <summary>
/// P6.4 — Pilot 4 çıktı sözleşmesi ve revizyon (AC-PILOT-04..06, AC-PILOT-09). Bağlayıcı sözleşme
/// <c>test-data/synthetic/pilot-4-output-contract/cikti_sozlesmesi.json</c>'dadır; gerçek bileşenlerle (atomik JSON kayıt,
/// ClosedXML, QuestPDF, CKS) Proje-A sentetik geometrisi üzerinde koşturulur.
/// </summary>
public sealed class Pilot4CiktiSozlesmesiTests
{
    private static readonly JsonElement Sozlesme = PilotProjesi.Json("pilot-4-output-contract", "cikti_sozlesmesi.json");

    private static string S(params string[] yol)
    {
        var e = Sozlesme;
        foreach (var p in yol)
        {
            e = e.GetProperty(p);
        }

        return e.GetString()!;
    }

    private static string Bosluksuz(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static async Task<PilotCalismaAlani> OnayliAsync()
    {
        var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeA());
        var onay = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);
        onay.IsSuccess.Should().BeTrue(onay.IsFailure ? onay.Error.TechnicalMessage : string.Empty);
        return alan;
    }

    [Fact]
    public void Pilot4_Uygulama_Sabitleri_Cikti_Sozlesmesiyle_Birebir_Eslesmeli()
    {
        PilotDamgasi.Metin.Should().Be(S("pilotDamgasi"));
        PilotDamgasi.FenKlasoruSablonSurumu.Should().Be(S("fenKlasoruSablonSurumu"));
        PilotDamgasi.ParsidSemaDurumu.Should().Be(S("parsidDurumu"));
        AtomikJsonProjeDeposu.Sema.Should().Be(S("projeKayitSemasi"));
        ProjeRevizyonu.MevzuatProfiliSurumu.Should().Be(S("mevzuatProfili"));
        ProjeRevizyonu.KuralSurumu.Should().Be(S("kuralSurumu"));
        new Version(ErrorCatalog.Version).Should().BeGreaterThanOrEqualTo(new Version(S("enDusukHataKataloguSurumu")));
        ApprovalService.OnayMetni.Should().Be(S("onay", "metin"));
        ApprovalService.GeriAlmaSuresi.Should().Be(TimeSpan.FromHours(Sozlesme.GetProperty("onay").GetProperty("geriAlmaSuresiSaat").GetInt32()));
        S("checksum", "algoritma").Should().Be("SHA-256");
        Sozlesme.GetProperty("checksum").GetProperty("imzaMi").GetBoolean().Should().BeFalse("pilot checksum'u imza değildir (Ed25519 Faz 12)");
    }

    [Fact]
    public void Pilot4_Sozlesme_SHA256_Yan_Dosyasini_Varsayilan_Olarak_Kapali_Tanimlamali_SORU_053()
    {
        var varsayilan = Sozlesme.GetProperty("checksum").GetProperty("yanDosyaVarsayilan").GetBoolean();

        varsayilan.Should().BeFalse("SORU-053: yan .sha256 dosyası isteğe bağlıdır; özet her zaman audit'tedir");
        CiktiSecenekleri.Varsayilan.Sha256YanDosyasiUret.Should().Be(varsayilan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pilot4_Her_Cikti_Turu_Sozlesmedeki_Uzanti_Damga_Ve_SHA256_Ozetiyle_Uretilmeli_Yan_Dosya_Istege_Bagli(bool yanDosya)
    {
        using var alan = await OnayliAsync();
        var rev = alan.Yol.Revizyon.Deger;

        foreach (var c in Sozlesme.GetProperty("ciktilar").EnumerateArray())
        {
            var tur = Enum.Parse<CiktiTuru>(c.GetProperty("tur").GetString()!);
            var uzanti = c.GetProperty("uzanti").GetString()!;
            alan.Cikti.Uzanti(tur).Should().Be(uzanti);
            var yol = alan.CiktiYolu("cikti" + uzanti);
            var yanYol = yol + S("checksum", "yanDosyaUzantisi");

            var sonuc = await alan.Cikti.UretAsync(tur, PilotCalismaAlani.Muhendis, yol, new CiktiSecenekleri(yanDosya));

            sonuc.IsSuccess.Should().BeTrue(sonuc.IsFailure ? sonuc.Error.TechnicalMessage : string.Empty);
            var bayt = await File.ReadAllBytesAsync(yol);
            var sha = Convert.ToHexStringLower(SHA256.HashData(bayt));
            sonuc.Value.Sha256.Should().Be(sha);
            if (yanDosya)
            {
                sonuc.Value.ChecksumDosyasi.Should().Be(yanYol);
                (await File.ReadAllTextAsync(yanYol, Encoding.UTF8)).Should().Be(sha + "  " + Path.GetFileName(yol) + "\n");
            }
            else
            {
                sonuc.Value.ChecksumDosyasi.Should().BeNull();
                File.Exists(yanYol).Should().BeFalse("SORU-053: " + tur + " için yan dosya varsayılan olarak yazılmaz");
            }

            alan.Yol.Denetim[^1].Should().Match<AuditKaydi>(k =>
                k.Islem == "Cikti:" + tur && k.Sonuc == AuditSonucu.Basarili && k.SonrakiRevizyon == rev && k.Ayrinti!.Contains("sha256=" + sha)
                && k.Ayrinti.Contains(yanDosya ? "yanDosya=evet" : "yanDosya=hayir"));
            DamgaVeRevizyonTasimali(tur, bayt, rev, c);
        }
    }

    private static void DamgaVeRevizyonTasimali(CiktiTuru tur, byte[] bayt, string rev, JsonElement sozlesme)
    {
        var damga = PilotDamgasi.Metin;
        switch (tur)
        {
            case CiktiTuru.FenKlasoru:
                using (var pdf = PdfDocument.Open(bayt))
                {
                    pdf.NumberOfPages.Should().BeGreaterThanOrEqualTo(sozlesme.GetProperty("enAzSayfa").GetInt32());
                    foreach (var sayfa in pdf.GetPages())
                    {
                        Bosluksuz(sayfa.Text).Should().Contain(Bosluksuz(damga)).And.Contain(rev);
                    }
                }

                break;
            case CiktiTuru.Cetveller:
                using (var wb = new XLWorkbook(new MemoryStream(bayt)))
                {
                    foreach (var ws in wb.Worksheets)
                    {
                        ws.Cell(1, 1).GetString().Should().Contain(damga, $"{ws.Name} 1. satır");
                    }
                }

                break;
            case CiktiTuru.FenKlasoruZip:
                // P7.3-C1 (sözleşme p7-3e): ZIP içindeki her PDF sayfasının en altında "damga · RevisionId <ilk 16 hex>";
                // büyük çapraz filigran yok; DOP_PILOT.CKS damgasızdır (SORU-039).
                using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bayt)))
                {
                    var pdfler = zip.Entries.Where(e => e.FullName.EndsWith(".pdf", StringComparison.Ordinal)).ToList();
                    pdfler.Count.Should().BeGreaterThanOrEqualTo(sozlesme.GetProperty("enAzPdf").GetInt32());
                    foreach (var giris in pdfler)
                    {
                        using var akis = new MemoryStream();
                        using (var s = giris.Open())
                        {
                            s.CopyTo(akis);
                        }

                        using var pdf = PdfDocument.Open(akis.ToArray());
                        foreach (var sayfa in pdf.GetPages())
                        {
                            Bosluksuz(sayfa.Text).Should().Contain(Bosluksuz(damga + " · RevisionId " + rev[..16]), giris.FullName);
                        }
                    }
                }

                break;
            default:
                // SORU-039 (proje sahibi kararı, sözleşme p7-3d): CKS Netcad bayt düzeninde yazılır — 1. satır boş, damga yok,
                // çerçeve satırları sözleşmedeki bayt genişliğinde; taslak niteliği UI, dosya adı (PILOT) ve audit ile izlenir.
                bayt.Take(2).Should().Equal((byte)0x0D, (byte)0x0A);
                var satirBayt = sozlesme.GetProperty("netcadSatirBayt").GetInt32();
                bayt.Length.Should().Be(2 + (21 * (satirBayt + 2)));
                Imar18.Infrastructure.Cikti.Cks.NetcadCksKodlayici.Coz(bayt).Should().NotContain(damga);
                break;
        }
    }

    [Fact]
    public async Task Pilot4_Tek_Kose_1_mm_Kayinca_Onay_Dusmeli_Cikti_Reddedilip_Denetlenmeli_AC_PILOT_06()
    {
        using var alan = await OnayliAsync();
        var eskiRev = alan.Yol.Revizyon;
        var geometri = alan.Yol.Belge.Parseller.ToList();
        var i = geometri.FindIndex(p => p.Rol == ParselRolu.Kadastro);
        var k = geometri[i].Koseler.ToArray();
        k[1] = k[1] with { Y = k[1].Y + 0.001m };
        geometri[i] = geometri[i] with { Koseler = k };

        (await alan.Yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(geometri), PilotCalismaAlani.Muhendis)).IsSuccess.Should().BeTrue();
        var sonuc = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoru, PilotCalismaAlani.Muhendis, alan.CiktiYolu("fen.pdf"));

        alan.Yol.Revizyon.Should().NotBe(eskiRev);
        alan.Yol.Belge.Onay.Should().BeNull();
        alan.Yol.Denetim.Should().Contain(d => d.Sonuc == AuditSonucu.OnayDustu && d.OncekiRevizyon == eskiRev.Deger);
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiGecerliOnayYok);
        alan.Yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.OnaysizGirisim);
        File.Exists(alan.CiktiYolu("fen.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task Pilot4_Diskten_Yeniden_Acilista_Onay_Ve_Denetim_Korunmali_Kurcalanan_Kayit_Reddedilmeli()
    {
        using var alan = await OnayliAsync();
        var rev = alan.Yol.Revizyon;
        var denetimSayisi = alan.Yol.Denetim.Count;

        (await alan.YenidenAcAsync()).IsSuccess.Should().BeTrue();
        alan.Yol.Revizyon.Should().Be(rev);
        alan.Onay.GecerliOnayVar.Should().BeTrue();
        alan.Yol.Denetim.Should().HaveCount(denetimSayisi);

        // İçerikteki tek bir rakamın değişmesi zarf SHA-256'sını bozar → açılış reddedilir (ERR-CIKTI-009).
        var metin = await File.ReadAllTextAsync(alan.ProjeDosyasi);
        var kurcali = metin.Replace("5000.06", "5000.07", StringComparison.Ordinal);
        kurcali.Should().NotBe(metin);
        await File.WriteAllTextAsync(alan.ProjeDosyasi, kurcali);

        (await alan.YenidenAcAsync()).Error.Code.Should().Be(ErrorCodes.CiktiProjeKaydiHatasi);
    }

    [Fact]
    public async Task Pilot4_Kritik_Topoloji_Hatasinda_Onay_Engellenmeli_AC_PILOT_05()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeA());
        var geometri = alan.Yol.Belge.Parseller.ToList();
        var y1 = geometri.FindIndex(p => p.ParselId == "901/Y1");
        var k = geometri[y1].Koseler.ToArray();
        k[1] = k[1] with { Y = k[1].Y + 1m }; // yol 1 m düzenleme sınırı dışına taşar (100 m² taşma)
        k[2] = k[2] with { Y = k[2].Y + 1m };
        geometri[y1] = geometri[y1] with { Koseler = k };
        (await alan.Yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(geometri), PilotCalismaAlani.Muhendis)).IsSuccess.Should().BeTrue();

        var sonuc = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);

        alan.Onay.Degerlendir().Bulgular.Should().Contain(b => b.Code == ErrorCodes.TopoKadastroDisinaTasma && b.TechnicalMessage.Contains("100.00 m2"));
        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
        alan.Yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task Pilot4_Fen_Klasoru_Ayni_Revizyonda_Bayt_Bayt_Ayni_Uretilmeli_Checksum_Tekrarlanabilir_Olmali()
    {
        using var alan = await OnayliAsync();

        var a = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoru, PilotCalismaAlani.Muhendis, alan.CiktiYolu("fen-1.pdf"));
        var b = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoru, PilotCalismaAlani.Muhendis, alan.CiktiYolu("fen-2.pdf"));

        a.Value.Sha256.Should().Be(b.Value.Sha256);
    }

    [Fact]
    public async Task Pilot4_Onay_Geri_Alininca_Cikti_Yeniden_Engellenmeli()
    {
        using var alan = await OnayliAsync();
        alan.Zaman.Simdi += TimeSpan.FromHours(23);

        (await alan.Onay.GeriAlAsync(PilotCalismaAlani.Muhendis)).IsSuccess.Should().BeTrue();
        var sonuc = await alan.Cikti.UretAsync(CiktiTuru.Cetveller, PilotCalismaAlani.Muhendis, alan.CiktiYolu("c.xlsx"));

        sonuc.Error.Code.Should().Be(ErrorCodes.CiktiGecerliOnayYok);
    }
}
