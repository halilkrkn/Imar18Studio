using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Revizyon;
using Imar18.Pilot.Tests.Destek;
using UglyToad.PdfPig;
using Xunit;

namespace Imar18.Pilot.Tests;

/// <summary>
/// Antigravity Bağımsız Denetim Testleri (Faz P6 — Pilot 1–4 Entegrasyon, Güvenlik ve Sözleşme Denetimi).
/// AC-PILOT-01..06 kabul kriterlerini ve çıktı sözleşmesini uçtan uca doğrular.
/// </summary>
public sealed class AntigravityIndependentPilotP6AuditTests
{
    [Fact]
    public async Task Pilot_P6_1_ProjeA_Altin_Veri_Ve_Uctan_Uca_Onay_Dogrulamasi()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeA());
        var degerlendirme = alan.Onay.Degerlendir();

        degerlendirme.Tamamlandi.Should().BeTrue();
        degerlendirme.KritikVar.Should().BeFalse();
        degerlendirme.Dop.Should().NotBeNull();
        degerlendirme.Dop!.Dopo.Should().Be(0.1240195m);

        // Mühendis onayı verilir
        var onaySonuc = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);
        onaySonuc.IsSuccess.Should().BeTrue();
        alan.Onay.GecerliOnayVar.Should().BeTrue();
    }

    [Fact]
    public async Task Pilot_P6_2_DOP_Yuzde45_Asimi_Kritik_Onay_Engeli_Olmali()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeA());

        // DOP girdisini %45'i aşacak şekilde (örneğin R2 = 4000 m2 -> kamu = 4699.44 -> DOPO = 4699.44 / 8699.44 = 0.54 > 0.45) güncelle
        var asiriDop = DopGirdisi.Olustur(r1: 8699.44m, r2: 4000.00m).Value;

        var komutSonuc = await alan.Yol.GonderAsync(new DopGirdisiAyarlaKomutu(asiriDop), PilotCalismaAlani.Muhendis);
        komutSonuc.IsSuccess.Should().BeTrue();

        var d = alan.Onay.Degerlendir();
        d.KritikVar.Should().BeTrue();
        d.Bulgular.Should().Contain(b => b.Code == ErrorCodes.DopYuzde45Asildi && b.Severity == ErrorSeverity.Critical);

        // Onay denemesi
        var onay = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);
        onay.IsFailure.Should().BeTrue();
        onay.Error.Code.Should().Be(ErrorCodes.CiktiOnayKritikHataEngeli);
        alan.Onay.GecerliOnayVar.Should().BeFalse();
    }

    [Fact]
    public async Task Pilot_P6_3_Musterek_Parselde_Dm2_Payda_Ve_LargestRemainder_Kapanisi()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeB());
        var d = alan.Onay.Degerlendir();

        d.Tamamlandi.Should().BeTrue();
        d.Hak.Should().NotBeNull();

        // Proje-B'de 37 imar parselinden müstakil olanlar TAM, müşterek olanların tamamında hisse paydası dm2 (alan x 100) olmalı ve paylar toplamı paydaya eşit olmalı
        foreach (var parsel in d.Hak!.ImarParselleri)
        {
            if (parsel.Mustakil)
            {
                parsel.Paydaslar.Single().Hisse.Tam.Should().BeTrue($"Parsel {parsel.GeciciKod} müstakil tahsis olduğundan TAM olmalı");
                continue;
            }

            var beklenenPayda = (System.Numerics.BigInteger)(parsel.AlanM2 * 100m);
            var toplamPay = System.Numerics.BigInteger.Zero;
            foreach (var p in parsel.Paydaslar)
            {
                p.Hisse.Payda.Should().Be(beklenenPayda, $"Müşterek parsel {parsel.GeciciKod} hisse paydası dm2 (alan*100) olmalı");
                toplamPay += p.Hisse.Pay;
            }
            toplamPay.Should().Be(beklenenPayda, $"Müşterek parsel {parsel.GeciciKod} paylar toplamı paydaya tam eşit olmalı (LR kapanışı)");
        }
    }

    [Fact]
    public async Task Pilot_P6_4_1mm_Kose_Kaymasinda_Onay_Otomatik_Dusmeli_Ve_PDF_Ligatur_Duzeltmesi()
    {
        using var alan = await PilotCalismaAlani.AcAsync(SahaProjeleri.ProjeA());

        // Önce geçerli onay ver
        var onay = await alan.Onay.OnaylaAsync(PilotCalismaAlani.Muhendis, ApprovalService.OnayMetni);
        onay.IsSuccess.Should().BeTrue();
        alan.Onay.GecerliOnayVar.Should().BeTrue();
        var onayliRevizyon = alan.Yol.Revizyon;

        // Fen Klasörü üret
        var pdfHedef = Path.Combine(alan.Klasor, "denetim_fen_klasoru.pdf");
        var cikti = await alan.Cikti.UretAsync(CiktiTuru.FenKlasoru, PilotCalismaAlani.Muhendis, pdfHedef);
        cikti.IsSuccess.Should().BeTrue();

        // PDF içerisinde ligatürsüz RevisionId metninin bulunabildiğini doğrula (ff/fi ligatür düzeltmesi)
        using (var pdf = PdfDocument.Open(pdfHedef))
        {
            pdf.NumberOfPages.Should().BeGreaterThan(0);
            var sayfa1Metni = pdf.GetPage(1).Text;
            sayfa1Metni.Should().Contain(onayliRevizyon.Deger[..8], "RevisionId başlığı PDF metninde doğrudan kopyalanabilir olmalı");
            sayfa1Metni.Should().Contain("PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ");
        }

        // 1 mm köşe değişikliği yap (geometri mutasyonu)
        var p = alan.Yol.Belge.Parseller[0];
        var yeniKoseler = p.Koseler.Select((k, i) => i == 0 ? new GeodeticPoint(k.X + 0.001m, k.Y) : k).ToList();
        var yeniParsel = new ParselGeometrisi(p.ParselId, p.Rol, yeniKoseler);
        var yeniGeometriler = alan.Yol.Belge.Parseller.Select(g => g.ParselId == p.ParselId ? yeniParsel : g).ToArray();

        var degisim = await alan.Yol.GonderAsync(new ParselGeometrileriAyarlaKomutu(yeniGeometriler), PilotCalismaAlani.Muhendis);
        degisim.IsSuccess.Should().BeTrue();

        // Doğrulama: Revizyon değişmeli, onay düşmeli ve denetim kaydına OnayDustu yazılmalı
        alan.Yol.Revizyon.Should().NotBe(onayliRevizyon);
        alan.Onay.GecerliOnayVar.Should().BeFalse("1 mm köşe kaymasında onay otomatik düşmelidir (AC-PILOT-06)");
        alan.Yol.Denetim.Should().Contain(d => d.Sonuc == AuditSonucu.OnayDustu);
    }
}
