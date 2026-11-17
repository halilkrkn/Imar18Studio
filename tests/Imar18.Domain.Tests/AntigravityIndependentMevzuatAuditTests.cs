using System.Globalization;
using System.Numerics;
using System.Text.Json;
using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;
using Imar18.Domain.Tests.Mevzuat;
using Xunit;

namespace Imar18.Domain.Tests;

/// <summary>
/// Google Antigravity Bağımsız Test ve Denetim Paketi (Faz P3 Mevzuat ve Hak Eşleştirme Denetimi).
/// 
/// Kapsam (Imar18Studio v2.19 §4.3, §4.4, §4.5, §25, D1, D16, D31, AC-PILOT-03, AC-PILOT-05, AC-PILOT-06):
/// 1. TecvizGirdisi ve TecvizChecker:
///    - Sınır eşitliği (|Gauss - Tapu| == Sınır) durumu (kabul edilmeli).
///    - Sınır aşımı (|Gauss - Tapu| > Sınır) -> ERR-TECVIZ-001 (Critical).
///    - Çift-geçiş (parsel bazlı vs düzenleme toplamı) aykırılığı -> ERR-TECVIZ-002 (Warning) + ERR-TECVIZ-001.
///    - Negatif, sıfır veya tanımsız parametreler -> ERR-TECVIZ-004.
///    - Altın Veri Değerleri: Proje-A (21.02 m²), Proje-B (75.07 m²), Proje-C (53.82 m²) doğrulaması.
/// 2. DopCalculator:
///    - %45 tavan denetimi tam kesir (Fraction) hassasiyetinde yapılmalı (0.45000001 -> ERR-DOP-001).
///    - Sınır eşitliği (tam 0.4500000) kabul edilmeli.
///    - Sıfırdan farklı D1, D2, R3 formül genişletmesi ve kamuya ayrılan alan türetimi (R1 - R2).
///    - Geçersiz girdiler (R1 - D1 &lt;= 0, D2 > R2, R3 > R1 - D1 vb.) -> ERR-DOP-003.
///    - %45 aşım karşılama sırası ve yetersiz kaynak durumu -> ERR-DOP-005 (Critical).
///    - Y1 yol terk dengelemesi önkoşulu (Tecviz.Gecti == false iken ret) ve altın değer Y1 dengelemeleri.
/// 3. FR-F1.12 Hak Eşleştirme ve Tahsis:
///    - AAD Yönetmeliği Md. 17/1-(d): Muvafakatsız paydaşın müstakil parsele tahsis yasağı -> ERR-TAPU-007 (Critical).
///    - Çok kaynaklı aynı malikin müstakil tahsis hakkı (Reviewer High-1).
///    - İmar parseli alan sapması &gt; 1 dm² -> ERR-TAPU-005 (Critical).
///    - Largest Remainder ile dm² payda kapanışı (toplam pay == payda).
/// 4. RevisionId ve Revizyona Bağlı Onay (AC-PILOT-06):
///    - Tecviz, DOP veya Hak Eşleştirme girdisindeki herhangi bir 1 dm² veya oransal değişimde
///      RevisionId'nin değişmesi ve önceki mühendis onayının anında geçersizleşmesi.
/// 5. Proje-B Dağıtım Eşleşmesi ve KVKK Güvencesi:
///    - proje_b_dagitim_eslesmesi.json altın veri setinin 40 malik, 37 imar parseli, 94 satır
///      ve kişisel veri içermeyen anonim formatta olması.
/// </summary>
public sealed class AntigravityIndependentMevzuatAuditTests
{
    // =========================================================================================
    // 1. TECVİZ VE TECVİZ CHECKER BAĞIMSIZ DENETİMLERİ
    // =========================================================================================

    [Fact]
    public void Tecviz_Sinir_Tam_Esitliginde_Kabul_Edilmeli_Ve_Gecmeli()
    {
        // |Tapu - Gauss| == Sinir (Örn: 1000.00 - 980.00 = 20.00, sınır = 20.00)
        var tecvizGirdisi = TecvizGirdisi.Olustur(
            olcekPaydaM: 1000,
            uretimYontemi: "Sayısal",
            koordinatKalite: KoordinatKalitesi.ArsivOnayli,
            tecvizSiniriM2: 20.00m,
            kaynakNotu: "Netcad Raporu").Value;

        var parsel = new TecvizParselGirdisi("PARSEL-TAM-SINIR", 1000.00m, 980.00m, tecvizGirdisi);

        var degerlendirme = TecvizChecker.Degerlendir([parsel], null).Value;

        degerlendirme.Gecti.Should().BeTrue("mutlak fark tecviz sınırına tam eşit olduğunda test geçmelidir (|Fark| <= Sınır)");
        degerlendirme.ParselBazliGecti.Should().BeTrue();
        degerlendirme.Bulgular.Should().BeEmpty();
        degerlendirme.KritikVar.Should().BeFalse();
        degerlendirme.ParselSonuclari.Single().Gecti.Should().BeTrue();
        degerlendirme.ParselSonuclari.Single().MutlakFarkM2.Should().Be(20.00m);
    }

    [Fact]
    public void Tecviz_Sinir_0_01_m2_Asildiginda_ERR_TECVIZ_001_Critical_Uretilmeli()
    {
        // |Tapu - Gauss| = 20.01m, sınır = 20.00m -> Aşım
        var tecvizGirdisi = TecvizGirdisi.Olustur(
            1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 20.00m, "TKGM").Value;

        var parsel = new TecvizParselGirdisi("PARSEL-ASIM", 1000.00m, 979.99m, tecvizGirdisi);

        var degerlendirme = TecvizChecker.Degerlendir([parsel], null).Value;

        degerlendirme.Gecti.Should().BeFalse();
        degerlendirme.ParselBazliGecti.Should().BeFalse();
        degerlendirme.KritikVar.Should().BeTrue("tecviz aşımı onayı engelleyen Critical seviyesinde olmalıdır (AC-PILOT-05)");
        var hata = degerlendirme.Bulgular.Should().ContainSingle().Which;
        hata.Code.Should().Be(ErrorCodes.TecvizSiniriAsildi);
        hata.Severity.Should().Be(ErrorSeverity.Critical);
        hata.TechnicalMessage.Should().Contain("PARSEL-ASIM");
    }

    [Fact]
    public void Tecviz_CiftGecis_Parseller_Gecer_Toplam_Aserse_ERR_TECVIZ_002_Ve_ERR_TECVIZ_001_Uretmeli()
    {
        // 2 parsel:
        // P1: Tapu 1000, Gauss 990 -> Fark 10m <= Sınır 15m (Geçti)
        // P2: Tapu 1000, Gauss 990 -> Fark 10m <= Sınır 15m (Geçti)
        // Toplam: Tapu 2000, Gauss 1980 -> Fark 20m > Toplam Sınır 18m (Kaldı!)
        var g1 = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 15.00m, "TKGM").Value;
        var p1 = new TecvizParselGirdisi("P1", 1000.00m, 990.00m, g1);
        var p2 = new TecvizParselGirdisi("P2", 1000.00m, 990.00m, g1);

        var sonuc = TecvizChecker.Degerlendir([p1, p2], duzenlemeToplamTecvizSiniriM2: 18.00m).Value;

        sonuc.Gecti.Should().BeFalse("çift geçiş kuralında toplam sınır aşılmışsa genel sonuç geçmemelidir");
        sonuc.ParselBazliGecti.Should().BeTrue();
        sonuc.ToplamSonucu.Should().NotBeNull();
        sonuc.ToplamSonucu!.Gecti.Should().BeFalse();
        sonuc.TekMesaj.Should().BeFalse("parsel bazlı ve toplam bazlı sonuçlar çeliştiğinde TekMesaj false olmalıdır");

        // Bulgularda hem toplam aşım hatası (Critical) hem de sonuç farkı uyarısı (Warning) olmalıdır
        sonuc.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TecvizSiniriAsildi && b.Severity == ErrorSeverity.Critical);
        sonuc.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TecvizParselToplamSonucuFarkli && b.Severity == ErrorSeverity.Warning);
    }

    [Fact]
    public void Tecviz_CiftGecis_Parseller_Aser_Toplam_Gecerse_ERR_TECVIZ_002_Ve_ERR_TECVIZ_001_Uretmeli()
    {
        // P1: Tapu 1000, Gauss 980 -> Fark +20m > Sınır 15m (Kaldı)
        // P2: Tapu 1000, Gauss 1020 -> Fark -20m > Sınır 15m (Kaldı)
        // Toplam: Tapu 2000, Gauss 2000 -> Fark 0m <= Toplam Sınır 25m (Geçti!)
        var g1 = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 15.00m, "TKGM").Value;
        var p1 = new TecvizParselGirdisi("P1", 1000.00m, 980.00m, g1);
        var p2 = new TecvizParselGirdisi("P2", 1000.00m, 1020.00m, g1);

        var sonuc = TecvizChecker.Degerlendir([p1, p2], duzenlemeToplamTecvizSiniriM2: 25.00m).Value;

        sonuc.Gecti.Should().BeFalse("parsellerden biri bile aşsa düzenleme tecvizden geçmemelidir");
        sonuc.ParselBazliGecti.Should().BeFalse();
        sonuc.ToplamSonucu!.Gecti.Should().BeTrue();
        sonuc.TekMesaj.Should().BeFalse();
        sonuc.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TecvizSiniriAsildi);
        sonuc.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TecvizParselToplamSonucuFarkli);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void TecvizChecker_Sifir_Veya_Negatif_Toplam_Sinir_ERR_TECVIZ_004_Vermeli(double sinir)
    {
        var g = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 15.00m, "TKGM").Value;
        var p = new TecvizParselGirdisi("P1", 100.00m, 100.00m, g);

        var sonuc = TecvizChecker.Degerlendir([p], (decimal)sinir);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void TecvizChecker_Mukerrer_Parsel_Id_ERR_TECVIZ_004_Vermeli()
    {
        var g = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 15.00m, "TKGM").Value;
        var p1 = new TecvizParselGirdisi("AYNI-ID", 100.00m, 100.00m, g);
        var p2 = new TecvizParselGirdisi("AYNI-ID", 200.00m, 200.00m, g);

        var sonuc = TecvizChecker.Degerlendir([p1, p2], null);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.TecvizGirdisiGecersiz);
    }

    [Fact]
    public void Tecviz_AltinDegerler_ProjeA_ProjeB_ProjeC_Netcad_Sinirlari_Icinde_Gecmeli()
    {
        // Proje-A: Netcad raporu tecviz sınırı = 21.02 m², Tapu = 8699.44 m², Gauss = 8699.32 m², Fark = 0.12 m²
        var gA = TecvizGirdisi.Olustur(1000, "Fotogrametrik", KoordinatKalitesi.ArsivOnayli, 21.02m, "Netcad Proje-A Raporu").Value;
        var pA = new TecvizParselGirdisi("901/1", 8699.44m, 8699.32m, gA);
        var resA = TecvizChecker.Degerlendir([pA], null).Value;
        resA.Gecti.Should().BeTrue();
        resA.ParselSonuclari.Single().MutlakFarkM2.Should().Be(0.12m);

        // Proje-B: Netcad raporu tecviz sınırı = 75.07 m², Tapu = 26629.48 m², Gauss = 26629.28 m², Fark = 0.20 m²
        var gB = TecvizGirdisi.Olustur(1000, "Fotogrametrik", KoordinatKalitesi.ArsivOnayli, 75.07m, "Netcad Proje-B Raporu").Value;
        var pB = new TecvizParselGirdisi("902/1", 26629.48m, 26629.28m, gB);
        var resB = TecvizChecker.Degerlendir([pB], null).Value;
        resB.Gecti.Should().BeTrue();
        resB.ParselSonuclari.Single().MutlakFarkM2.Should().Be(0.20m);

        // Proje-C: Netcad raporu tecviz sınırı = 53.82 m², Tapu = 14485.35 m², Gauss = 14483.38 m², Fark = 1.97 m²
        var gC = TecvizGirdisi.Olustur(1000, "Fotogrametrik", KoordinatKalitesi.ArsivOnayli, 53.82m, "Netcad Proje-C Raporu").Value;
        var pC = new TecvizParselGirdisi("903/1", 14485.35m, 14483.38m, gC);
        var resC = TecvizChecker.Degerlendir([pC], null).Value;
        resC.Gecti.Should().BeTrue();
        resC.ParselSonuclari.Single().MutlakFarkM2.Should().Be(1.97m);
    }

    // =========================================================================================
    // 2. DOP CALCULATOR BAĞIMSIZ DENETİMLERİ
    // =========================================================================================

    [Fact]
    public void DopCalculator_Tam_0_4500000_Sinirinda_Tavan_Asilmamis_Sayilmali()
    {
        // R1 = 10000 m², R2 = 5500 m² -> DOP Miktarı = 4500 m²
        // DOPO = 4500 / 10000 = 0.4500000
        var girdi = DopGirdisi.Olustur(r1: 10000m, r2: 5500m).Value;
        var sonuc = DopCalculator.Hesapla(girdi).Value;

        sonuc.Dopo.Should().Be(0.4500000m);
        sonuc.TavanAsildi.Should().BeFalse("tam %45 tavan değerinde tavan aşılmamış olmalıdır");
        sonuc.AsimM2.Should().Be(0m);
        sonuc.KritikVar.Should().BeFalse();
        sonuc.Bulgular.Should().BeEmpty();
    }

    [Fact]
    public void DopCalculator_Tam_Kesirde_0_45_i_Aşan_Ama_Yuvarlaninca_0_45_Kalan_Deger_ERR_DOP_001_Uretmeli()
    {
        // R1 = 100_000_000 m², R2 = 54_999_999 m²
        // DOP = 45_000_001 m²
        // DOPO = 45_000_001 / 100_000_000 = 0.45000001
        // 7 hane yuvarlamada 0.4500000 olur ama tam kesir > 0.45'tir! (SORU-031 b kararı)
        var girdi = DopGirdisi.Olustur(r1: 100000000m, r2: 54999999m).Value;
        var sonuc = DopCalculator.Hesapla(girdi).Value;

        sonuc.TavanAsildi.Should().BeTrue("tam kesir %45'in üzerinde olduğu için aşım tespit edilmelidir");
        sonuc.KritikVar.Should().BeTrue();
        sonuc.Bulgular.Should().ContainSingle(b => b.Code == ErrorCodes.DopYuzde45Asildi && b.Severity == ErrorSeverity.Critical);
    }

    [Fact]
    public void DopCalculator_Sifirdan_Farkli_D1_D2_R3_Genisletilmis_Formulu_Matematiksel_Olarak_Uygulamali()
    {
        // DOPO = (R1 - D1 - R2 + D2 + R3) / (R1 - D1)
        // R1 = 15000, D1 = 3000 -> Payda = 12000
        // R2 = 8000, D2 = 500, R3 = 250
        // Pay = 12000 - 8000 + 500 + 250 = 4750
        // DOPO = 4750 / 12000 = 0.395833333... -> 7 hane: 0.3958333
        // Kamuya ayrılan alan = R1 - R2 = 15000 - 8000 = 7000 m² (türetilir)
        var girdi = DopGirdisi.Olustur(
            r1: 15000m,
            r2: 8000m,
            d1: 3000m,
            d2: 500m,
            r3: 250m,
            ortakKatilimM2: 120m).Value;

        var sonuc = DopCalculator.Hesapla(girdi).Value;

        sonuc.Dopo.Should().Be(0.3958333m);
        sonuc.DopMiktariM2.Should().Be(4750m);
        sonuc.KamuyaAyrilanAlanM2.Should().Be(7000m);
        sonuc.Oko.Should().Be(0.0100000m); // 120 / 12000 = 0.01
        sonuc.TavanAsildi.Should().BeFalse();
    }

    [Theory]
    [InlineData(1000, 500, 1000)] // R1 - D1 = 0
    [InlineData(1000, 500, 1200)] // R1 - D1 < 0
    public void DopGirdisi_R1_Eksi_D1_Pozitif_Olmadiginda_ERR_DOP_003_Vermeli(decimal r1, decimal r2, decimal d1)
    {
        var res = DopGirdisi.Olustur(r1, r2, d1: d1);
        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void DopGirdisi_D2_R2yi_Astiginda_ERR_DOP_003_Vermeli()
    {
        var res = DopGirdisi.Olustur(r1: 1000m, r2: 500m, d2: 500.01m);
        res.IsFailure.Should().BeTrue();
        res.Error.Code.Should().Be(ErrorCodes.DopGirdisiGecersiz);
    }

    [Fact]
    public void DopCalculator_AsimKarsila_Kaynak_Yetersizse_ERR_DOP_005_Critical_Uretmeli()
    {
        // %50 DOP -> Aşım = 500 m²
        var girdi = DopGirdisi.Olustur(r1: 10000m, r2: 5000m).Value;
        var sonuc = DopCalculator.Hesapla(girdi).Value;
        sonuc.TavanAsildi.Should().BeTrue();
        sonuc.AsimM2.Should().Be(500.00m);

        // Kaynaklar: Toplam 350 m² (500 m²'yi karşılamaz, 150 m² açık kalır)
        var kaynaklar = new DopAsimKaynaklari(
            TescilHariciM2: 100m,
            BelediyeMulkiyetiM2: 150m,
            MuvafakatliKamuTasinmaziM2: 50m,
            KamulastirmaM2: 50m);

        var planSonuc = DopCalculator.AsimKarsila(sonuc, kaynaklar);
        planSonuc.IsSuccess.Should().BeTrue();
        var plan = planSonuc.Value;

        plan.KarsilanamayanM2.Should().Be(150.00m);
        plan.Bulgular.Should().ContainSingle(b => b.Code == ErrorCodes.DopAsimKarsilanamadi && b.Severity == ErrorSeverity.Critical);
    }

    [Fact]
    public void YolTerkDengelemesi_Tecviz_Gecmediginde_Hata_Vermeli()
    {
        // Başarısız tecviz
        var tecvizGirdi = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 1.00m, "TKGM").Value;
        var parsel = new TecvizParselGirdisi("P1", 1000m, 950m, tecvizGirdi); // 50m sapma > 1m sınır -> Başarısız
        var tecvizDegerlendirme = TecvizChecker.Degerlendir([parsel], null).Value;
        tecvizDegerlendirme.Gecti.Should().BeFalse();

        var imarParselleri = new[] { new DengelemeKalemi("A1", 750m) };
        var yolTerk = new DengelemeKalemi("Y1", 200m);

        var dengelemeSonuc = YolTerkDengelemesi.Dengele(imarParselleri, yolTerk, [], tecvizDegerlendirme);

        dengelemeSonuc.IsFailure.Should().BeTrue("tecvizden geçmeyen projede Y1 yol terk dengelemesi yapılamaz");
        dengelemeSonuc.Error.Code.Should().Be(ErrorCodes.TecvizSiniriAsildi);
    }

    [Fact]
    public void YolTerkDengelemesi_AltinVeri_ProjeA_ProjeB_ProjeC_Y1_Degerlerine_Farki_Tam_Eklemeli()
    {
        // Proje-A: Netcad Dengeleme cetveli Y1 = 1078.90 m² (Gauss 1078.78 + 0.12 fark)
        var tecvizGirdiA = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 21.02m, "Netcad").Value;
        var tecvizA = TecvizChecker.Degerlendir([new TecvizParselGirdisi("901/1", 8699.44m, 8699.32m, tecvizGirdiA)], 21.02m).Value;
        var imarA = new[] { new DengelemeKalemi("901/A1", 7620.54m) };
        var y1A = YolTerkDengelemesi.Dengele(imarA, new DengelemeKalemi("901/Y1", 1078.78m), [], tecvizA).Value;
        y1A.YolTerk.DengelenmisAlanM2.Should().Be(1078.90m);
        y1A.YolTerk.DuzeltmeM2.Should().Be(0.12m);

        // Proje-B: Netcad Dengeleme cetveli Y1 = 5367.04 m² (Gauss 5366.84 + 0.20 fark)
        var tecvizGirdiB = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 75.07m, "Netcad").Value;
        var tecvizB = TecvizChecker.Degerlendir([new TecvizParselGirdisi("902/1", 26629.48m, 26629.28m, tecvizGirdiB)], 75.07m).Value;
        var imarB = new[] { new DengelemeKalemi("902/A1", 21262.44m) };
        var y1B = YolTerkDengelemesi.Dengele(imarB, new DengelemeKalemi("902/Y1", 5366.84m), [], tecvizB).Value;
        y1B.YolTerk.DengelenmisAlanM2.Should().Be(5367.04m);
        y1B.YolTerk.DuzeltmeM2.Should().Be(0.20m);

        // Proje-C: Netcad Dengeleme cetveli Y1 = 2026.25 m² (Gauss 2024.28 + 1.97 fark)
        var tecvizGirdiC = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 53.82m, "Netcad").Value;
        var tecvizC = TecvizChecker.Degerlendir([new TecvizParselGirdisi("903/1", 14485.35m, 14483.38m, tecvizGirdiC)], 53.82m).Value;
        var imarC = new[] { new DengelemeKalemi("903/A1", 12059.10m), new DengelemeKalemi("903/PARK", 400.00m) };
        var y1C = YolTerkDengelemesi.Dengele(imarC, new DengelemeKalemi("903/Y1", 2024.28m), [], tecvizC).Value;
        y1C.YolTerk.DengelenmisAlanM2.Should().Be(2026.25m);
        y1C.YolTerk.DuzeltmeM2.Should().Be(1.97m);
    }

    // =========================================================================================
    // 3. FR-F1.12 HAK EŞLEŞTİRME VE TAHİS BAĞIMSIZ DENETİMLERİ
    // =========================================================================================

    [Fact]
    public void HakEslestirme_Madde17_1d_Muvafakatsiz_Paydas_Tek_Basina_Parsele_Verilirse_ERR_TAPU_007_Critical_Uretmeli()
    {
        // K1 parselinde M1 (muvafakatli değil) ve M2 var.
        // M1 tek başına A1 imar parseline tahsis edilirse AAD Md. 17/1-(d) ihlalidir!
        var kadastro = new KadastroParseli("K1", 1000.00m,
        [
            new KadastroMaliki("M1", new Fraction(1, 2), Muvafakatli: false),
            new KadastroMaliki("M2", new Fraction(1, 2), Muvafakatli: false),
        ]);

        var imarA1 = new ImarParseli("A1", 400.00m);
        var imarA2 = new ImarParseli("A2", 400.00m);

        var satirlar = new[]
        {
            new HakEslestirmeSatiri("M1", "K1", "A1", 400.00m), // M1 tek başına A1'de!
            new HakEslestirmeSatiri("M2", "K1", "A2", 400.00m),
        };

        var sonuc = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(
            Dopo: 0.20m,
            KadastroParselleri: [kadastro],
            ImarParselleri: [imarA1, imarA2],
            Eslestirme: satirlar)).Value;

        sonuc.KritikVar.Should().BeTrue("muvafakatsız paydaşın tek başına tahsisi onayı engelleyen Critical olmalıdır");
        sonuc.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TapuMuvafakatsizMustakilTahsis && b.Severity == ErrorSeverity.Critical);
    }

    [Fact]
    public void HakEslestirme_Madde17_1d_Ayni_Malik_Iki_Kadastro_Parselinden_Gelse_Bile_Birlestirilip_Mustakil_Olabilmeli()
    {
        // Malik M1, hem K1 (tek malik) hem K2 (tek malik) sahibi.
        // A1 parseline hem K1 hem K2'den tahsis yapılıyor.
        // Paydaş sayısı farklı MalikId'ye göre 1 olmalı ve tek satırda birleştirilmeli.
        var k1 = new KadastroParseli("K1", 500.00m, [new KadastroMaliki("M1", Fraction.One, Muvafakatli: true)]);
        var k2 = new KadastroParseli("K2", 500.00m, [new KadastroMaliki("M1", Fraction.One, Muvafakatli: true)]);
        var a1 = new ImarParseli("A1", 800.00m);

        var satirlar = new[]
        {
            new HakEslestirmeSatiri("M1", "K1", "A1", 400.00m),
            new HakEslestirmeSatiri("M1", "K2", "A1", 400.00m),
        };

        var sonuc = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(
            Dopo: 0.20m,
            KadastroParselleri: [k1, k2],
            ImarParselleri: [a1],
            Eslestirme: satirlar)).Value;

        sonuc.Bulgular.Should().BeEmpty();
        var tescilA1 = sonuc.ImarParselleri.Single(p => p.GeciciKod == "A1");
        tescilA1.Mustakil.Should().BeTrue();
        tescilA1.Paydaslar.Should().HaveCount(1, "aynı malikin satırları birleştirilmelidir");
        tescilA1.Paydaslar.Single().Hisse.ToString().Should().Be("TAM");
        tescilA1.Paydaslar.Single().AlanM2.Should().Be(800.00m);
    }

    [Fact]
    public void HakEslestirme_ImarParseli_Tahsis_Toplami_1dm2_den_Fazla_Saparsa_ERR_TAPU_005_Critical_Uretmeli()
    {
        // Parsel alanı 400.00 m², tahsis toplamı 400.03 m² (+3 dm² fark > 1 dm² tolerans)
        var k1 = new KadastroParseli("K1", 500.00m, [new KadastroMaliki("M1", Fraction.One, Muvafakatli: true)]);
        var a1 = new ImarParseli("A1", 400.00m);
        var satirlar = new[] { new HakEslestirmeSatiri("M1", "K1", "A1", 400.03m) };

        var sonuc = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(
            0.20m, [k1], [a1], satirlar)).Value;

        sonuc.KritikVar.Should().BeTrue();
        sonuc.Bulgular.Should().Contain(b => b.Code == ErrorCodes.TapuImarParselDagitimToplamiUyumsuz && b.Severity == ErrorSeverity.Critical);
    }

    [Fact]
    public void HakEslestirme_LargestRemainder_Pay_Kapanisi_dm2_Bazinda_Tam_Esitlenmeli()
    {
        // Hisseli bir imar parselinde (Örn: 3 malikli, parsel 3539.74 m², payda 353974)
        // Her bir malike tahsis yapılır ve oluşan payların toplamı tam payda (353974) olmalıdır.
        var k1 = new KadastroParseli("K1", 5000.00m,
        [
            new KadastroMaliki("M1", new Fraction(1, 3), Muvafakatli: true),
            new KadastroMaliki("M2", new Fraction(1, 3), Muvafakatli: true),
            new KadastroMaliki("M3", new Fraction(1, 3), Muvafakatli: true),
        ]);

        var a1 = new ImarParseli("A1", 3539.74m); // Payda: 353974 dm²

        // 3539.74 / 3 = 1179.913333...
        // 1179.91 + 1179.91 + 1179.92 = 3539.74 m²
        var satirlar = new[]
        {
            new HakEslestirmeSatiri("M1", "K1", "A1", 1179.91m),
            new HakEslestirmeSatiri("M2", "K1", "A1", 1179.91m),
            new HakEslestirmeSatiri("M3", "K1", "A1", 1179.92m),
        };

        var sonuc = HakEslestirmeServisi.Eslestir(new HakEslestirmeGirdisi(
            0.20m, [k1], [a1], satirlar)).Value;

        var tescilA1 = sonuc.ImarParselleri.Single(p => p.GeciciKod == "A1");
        tescilA1.Paydaslar.Should().HaveCount(3);

        BigInteger toplamPay = 0;
        BigInteger beklenenPayda = 353974;

        foreach (var paydas in tescilA1.Paydaslar)
        {
            paydas.Hisse.Payda.Should().Be(beklenenPayda);
            toplamPay += paydas.Hisse.Pay;
        }

        toplamPay.Should().Be(beklenenPayda, "Largest Remainder payda kapanışında paylar toplamı tam paydaya eşit olmalıdır");
    }

    // =========================================================================================
    // 4. REVISION ID VE ONAYIN DÜŞMESİ (AC-PILOT-06)
    // =========================================================================================

    [Fact]
    public void RevisionId_Veri_Degistiginde_Onay_Otomatik_Gecersizlesmeli_AC_PILOT_06()
    {
        var k1 = new KadastroParseli("K1", 1000.00m, [new KadastroMaliki("M1", Fraction.One, Muvafakatli: true)]);
        var a1 = new ImarParseli("A1", 800.00m);
        var s1 = new[] { new HakEslestirmeSatiri("M1", "K1", "A1", 800.00m) };
        var hakGirdisi = new HakEslestirmeGirdisi(0.20m, [k1], [a1], s1);

        var dopGirdisi = DopGirdisi.Olustur(1000m, 800m).Value;
        var tecvizGirdisi = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 20.00m, "TKGM").Value;
        var tecvizParsel = new TecvizParselGirdisi("K1", 1000m, 990m, tecvizGirdisi);

        // 1. Durum: Revizyon hesaplanır ve mühendis onaylar
        var revizyon1 = HesapRevizyonu.Hesapla(hakGirdisi, dopGirdisi, [tecvizParsel], null);
        var onay = MuhendisOnayi.Olustur(revizyon1, "MUHENDIS-ODASI-12345", DateTimeOffset.UtcNow);

        onay.GecerliMi(revizyon1).Should().BeTrue("aynı revizyon kimliğinde onay geçerli olmalıdır");

        // 2. Durum (A): Tecviz sınırı 20.00m'den 20.05m'ye değişti
        var tecvizGirdisi2 = TecvizGirdisi.Olustur(1000, "Sayısal", KoordinatKalitesi.ArsivOnayli, 20.05m, "TKGM").Value;
        var tecvizParsel2 = new TecvizParselGirdisi("K1", 1000m, 990m, tecvizGirdisi2);
        var revizyonTecvizDegisti = HesapRevizyonu.Hesapla(hakGirdisi, dopGirdisi, [tecvizParsel2], null);

        revizyonTecvizDegisti.Should().NotBe(revizyon1);
        onay.GecerliMi(revizyonTecvizDegisti).Should().BeFalse("tecviz sınırı değiştiğinde onay otomatik olarak DÜŞMELİDİR (AC-PILOT-06)");

        // 3. Durum (B): DOP R1 alanı 1000.00m'den 1000.01m'ye (1 dm²) değişti
        var dopGirdisi2 = DopGirdisi.Olustur(1000.01m, 800m).Value;
        var revizyonDopDegisti = HesapRevizyonu.Hesapla(hakGirdisi, dopGirdisi2, [tecvizParsel], null);

        revizyonDopDegisti.Should().NotBe(revizyon1);
        onay.GecerliMi(revizyonDopDegisti).Should().BeFalse("DOP alanı 1 dm² değiştiğinde onay otomatik olarak DÜŞMELİDİR (AC-PILOT-06)");

        // 4. Durum (C): İmar tahsis alanı 1 dm² değişti
        var a1Degisti = new ImarParseli("A1", 800.01m);
        var s1Degisti = new[] { new HakEslestirmeSatiri("M1", "K1", "A1", 800.01m) };
        var hakGirdisiDegisti = new HakEslestirmeGirdisi(0.20m, [k1], [a1Degisti], s1Degisti);
        var revizyonTahsisDegisti = HesapRevizyonu.Hesapla(hakGirdisiDegisti, dopGirdisi, [tecvizParsel], null);

        revizyonTahsisDegisti.Should().NotBe(revizyon1);
        onay.GecerliMi(revizyonTahsisDegisti).Should().BeFalse("tahsis alanı 1 dm² değiştiğinde onay otomatik olarak DÜŞMELİDİR (AC-PILOT-06)");
    }

    // =========================================================================================
    // 5. PROJE-B FİXTURE VE KVKK ANONİMLİK DENETİMİ
    // =========================================================================================

    [Fact]
    public void ProjeB_Fixture_KVKK_Anonimlik_Ve_Tam_Dagitim_Denetimi()
    {
        var json = AltinVeri.ProjeBDagitim();

        // 1. Alan bütünlüğü
        json.GetProperty("kadastroParsel").GetString().Should().Be("902/1");
        json.GetProperty("toplamKadastroAlaniM2").GetDecimal().Should().Be(26629.48m);
        json.GetProperty("toplamImarTahsisAlaniM2").GetDecimal().Should().Be(21262.44m);
        json.GetProperty("dopo").GetDecimal().Should().Be(0.2015451m);
        json.GetProperty("imarParselSayisi").GetInt32().Should().Be(37);
        json.GetProperty("toplamTahsisSatirSayisi").GetInt32().Should().Be(94);

        // 2. KVKK Denetimi: Malik kodları sadece 'Malik-XX' formatında olmalı, gerçek isim/kimlik olmamalı
        var eslesmeler = json.GetProperty("eslesmeler").EnumerateArray().ToList();
        foreach (var satir in eslesmeler)
        {
            var malikKodu = satir.GetProperty("malikKodu").GetString()!;
            malikKodu.Should().MatchRegex(@"^Malik-\d{2}$", "KVKK uyarınca malik kodları yalnızca anonim 'Malik-NN' biçiminde olmalıdır");

            var imarParsel = satir.GetProperty("imarParsel").GetString()!;
            imarParsel.Should().MatchRegex(@"^\d{3}/\d{1,2}$", "imar ada/parsel formatı standart olmalıdır (örn: 101/1)");
        }

        // 3. Toplam 40 farklı malik kodu bulunmalı
        var malikler = eslesmeler.Select(e => e.GetProperty("malikKodu").GetString()).Distinct().ToList();
        malikler.Should().HaveCount(40, "Proje-B'de tam 40 farklı anonim malik bulunmalıdır");
    }
}
