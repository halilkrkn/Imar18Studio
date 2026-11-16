using System.Text.RegularExpressions;
using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Domain.Tests.Diagnostics;

/// <summary>
/// P1.3 — ErrorCatalog / AppError (spec 5, AC-ERR-01..03). Beklentiler spec 5.2-5.6 metninden turetilmistir.
/// </summary>
public sealed class ErrorCatalogTests
{
    // Spec 5.2 tablosundaki kod oneki -> kategori eslemesi (bagimsiz referans).
    private static readonly Dictionary<string, ErrorCategory> OnekKategori = new(StringComparer.Ordinal)
    {
        ["GIRD"] = ErrorCategory.Input,
        ["CRS"] = ErrorCategory.Crs,
        ["TOPO"] = ErrorCategory.Topology,
        ["TAPU"] = ErrorCategory.Tapu,
        ["TECVIZ"] = ErrorCategory.Tecviz,
        ["DOP"] = ErrorCategory.Dop,
        ["PLAN"] = ErrorCategory.Plan,
        ["KUANT"] = ErrorCategory.Quantization,
        ["CIKTI"] = ErrorCategory.Output,
        ["GUVEN"] = ErrorCategory.Security,
        ["GENEL"] = ErrorCategory.General,
    };

    private static readonly Regex KodBicimi = new(@"^ERR-(?<onek>[A-Z]+)(-[A-Z]+)?-\d{3}$", RegexOptions.CultureInvariant);

    [Fact]
    public void HataKatalogu_Tum_HataKodlari_Benzersiz_Olmali()
    {
        var kodlar = ErrorCatalog.All.Select(e => e.Code).ToList();

        kodlar.Should().NotBeEmpty();
        kodlar.Should().OnlyHaveUniqueItems();
        kodlar.Should().OnlyContain(k => KodBicimi.IsMatch(k), "her kod ERR-<ONEK>-<NNN> bicimindedir (spec 5.3)");
        ErrorCatalog.Version.Should().NotBeNullOrWhiteSpace("AC-ERR-01: katalog surumludur");
    }

    [Fact]
    public void HataKatalogu_Kod_Oneki_Kategori_Ile_Uyumlu_Olmali()
    {
        foreach (var hata in ErrorCatalog.All)
        {
            var onek = KodBicimi.Match(hata.Code).Groups["onek"].Value;

            OnekKategori.Should().ContainKey(onek, $"{hata.Code} onegi spec 5.2'de tanimli olmali");
            hata.Category.Should().Be(OnekKategori[onek], $"{hata.Code} kategorisi onekle uyumlu olmali");
        }
    }

    [Fact]
    public void HataKatalogu_Spec_5_2_Kategorilerinin_Her_Birinde_En_Az_Bir_Hata_Tanimli_Olmali()
    {
        var kategoriler = ErrorCatalog.All.Select(e => e.Category).ToHashSet();

        kategoriler.Should().Contain(OnekKategori.Values);
    }

    [Fact]
    public void HataKatalogu_KritikHatalar_EngineerReview_Isteyebilir()
    {
        var kritikler = ErrorCatalog.All.Where(e => e.Severity == ErrorSeverity.Critical).ToList();

        kritikler.Should().NotBeEmpty();
        kritikler.Should().OnlyContain(e => e.RequiresEngineerReview, "AC-ERR-02: kritik hatalar muhendis incelemesi gerektirir");
    }

    [Fact]
    public void HataKatalogu_KritikHatalar_Audit_Kaydi_Gerektirmeli()
    {
        // Varsayim (SORU-021): Severity == Critical => AuditLogRequired = true, digerleri false.
        ErrorCatalog.All.Where(e => e.Severity == ErrorSeverity.Critical).Should().OnlyContain(e => e.AuditLogRequired);
        ErrorCatalog.All.Where(e => e.Severity != ErrorSeverity.Critical).Should().OnlyContain(e => !e.AuditLogRequired);
        ErrorCatalog.Get("ERR-TOPO-001").AuditLogRequired.Should().BeTrue("spec 5.3 JSON ornegi: auditLogRequired true");
    }

    [Fact]
    public void HataKatalogu_Z_Sifir_Disi_Kot_Yatay_Izdusume_Indirgendi_Uyarisi_Tanimli_Olmali()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.GirdZKoduIndirgendi);

        hata.Code.Should().Be("ERR-GIRD-004");
        hata.Severity.Should().Be(ErrorSeverity.Warning);
        hata.Category.Should().Be(ErrorCategory.Input);
        hata.RequiresEngineerReview.Should().BeFalse();
    }

    [Fact]
    public void HataKatalogu_KullaniciMesaji_TeknikMesajdan_Ayri_Olmali()
    {
        foreach (var hata in ErrorCatalog.All)
        {
            hata.UserMessage.Should().NotBeNullOrWhiteSpace(hata.Code);
            hata.TechnicalMessage.Should().NotBeNullOrWhiteSpace(hata.Code);
            hata.RecommendedAction.Should().NotBeNullOrWhiteSpace(hata.Code);
            hata.UserMessage.Should().NotBe(hata.TechnicalMessage, $"{hata.Code}: kullanici mesaji teknik mesajdan ayri olmali");
        }
    }

    [Fact]
    public void HataKatalogu_KullaniciMesaji_Teknik_Detay_Icermemeli()
    {
        // AC-ERR-03: kod, istisna adi, yer tutucu veya sinif/yontem izi gibi teknik iz yok.
        var yasakliIzler = new[] { "Exception", "{", "}", "StackTrace", "NullReference", "System.", "Imar18.", "NTS", "ERR-" };

        foreach (var hata in ErrorCatalog.All)
        {
            foreach (var iz in yasakliIzler)
            {
                hata.UserMessage.Should().NotContain(iz, $"{hata.Code} kullanici mesaji teknik iz tasimamali");
            }
        }
    }

    [Fact]
    public void HataKatalogu_BilinmeyenHata_GenelKodla_Dondurmeli()
    {
        var hata = ErrorCatalog.Get("ERR-YOK-999");

        hata.Code.Should().Be(ErrorCodes.GenelBeklenmeyen);
        hata.Category.Should().Be(ErrorCategory.General);
        ErrorCatalog.Get(null).Code.Should().Be(ErrorCodes.GenelBeklenmeyen);
        ErrorCatalog.Get("").Code.Should().Be(ErrorCodes.GenelBeklenmeyen);
        ErrorCatalog.TryGet("ERR-YOK-999", out _).Should().BeFalse();
    }

    [Fact]
    public void HataKatalogu_Bilinen_Kod_Aynen_Dondurmeli()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.TopoKendiUzerindeKesisim);

        hata.Code.Should().Be("ERR-TOPO-001");
        ErrorCatalog.TryGet("ERR-TOPO-001", out var bulunan).Should().BeTrue();
        bulunan.Should().Be(hata);
    }

    [Fact]
    public void HataKatalogu_Spec_5_3_Ornegi_ERR_TOPO_001_Birebir_Tanimli_Olmali()
    {
        var hata = ErrorCatalog.Get("ERR-TOPO-001");

        hata.Severity.Should().Be(ErrorSeverity.Critical);
        hata.Category.Should().Be(ErrorCategory.Topology);
        hata.UserMessage.Should().Be("Parsel sınırı kendi üzerinde kesişiyor.");
        hata.TechnicalMessage.Should().Be("Self-intersection detected in polygon {parcelId}.");
        hata.RecommendedAction.Should().Be("Topoloji Onar ekranını kullanın veya CAD (DXF/DWG/NCZ) köşe sırasını kontrol edin.");
        hata.Retryable.Should().BeFalse();
        hata.RequiresEngineerReview.Should().BeTrue();
    }

    [Theory]
    [InlineData("ERR-DOP-001", ErrorSeverity.Critical)]
    [InlineData("ERR-TECVIZ-001", ErrorSeverity.Critical)]
    [InlineData("ERR-KUANT-001", ErrorSeverity.Critical)]
    [InlineData("ERR-GIRD-NCZ-001", ErrorSeverity.Error)]
    [InlineData("ERR-CIKTI-001", ErrorSeverity.Critical)]
    [InlineData("ERR-GUVEN-001", ErrorSeverity.Critical)]
    public void HataKatalogu_Spec_Veya_Gorevde_Adi_Gecen_Kodlar_Tanimli_Olmali(string kod, ErrorSeverity seviye)
    {
        ErrorCatalog.TryGet(kod, out var hata).Should().BeTrue($"{kod} katalogda tanimli olmali");
        hata.Severity.Should().Be(seviye);
    }

    [Theory]
    [InlineData("ERR-TOPO-003", ErrorSeverity.Critical, ErrorCategory.Topology)]
    [InlineData("ERR-TOPO-004", ErrorSeverity.Critical, ErrorCategory.Topology)]
    [InlineData("ERR-PLAN-003", ErrorSeverity.Error, ErrorCategory.Plan)]
    [InlineData("ERR-PLAN-004", ErrorSeverity.Error, ErrorCategory.Plan)]
    [InlineData("ERR-PLAN-005", ErrorSeverity.Error, ErrorCategory.Plan)]
    [InlineData("ERR-GIRD-005", ErrorSeverity.Error, ErrorCategory.Input)]
    [InlineData("ERR-CRS-003", ErrorSeverity.Error, ErrorCategory.Crs)]
    public void HataKatalogu_P2_Geometri_Kodlari_Tanimli_Olmali(string kod, ErrorSeverity seviye, ErrorCategory kategori)
    {
        ErrorCatalog.TryGet(kod, out var hata).Should().BeTrue($"{kod} katalogda tanimli olmali");
        hata.Severity.Should().Be(seviye);
        hata.Category.Should().Be(kategori);
        ErrorCatalog.Version.Should().Be("1.2.0");
    }

    [Fact]
    public void HataKatalogu_ERR_DOP_001_Yuzde_45_Asimini_Anlatmali()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.DopYuzde45Asildi);

        hata.UserMessage.Should().Contain("%45");
        hata.Category.Should().Be(ErrorCategory.Dop);
    }

    [Fact]
    public void HataKatalogu_ErrorCodes_Sabitleri_Katalogdaki_Kodlarla_Birebir_Eslesmeli()
    {
        var sabitler = typeof(ErrorCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        sabitler.Should().OnlyHaveUniqueItems();
        sabitler.Should().BeEquivalentTo(ErrorCatalog.All.Select(e => e.Code));
    }

    [Fact]
    public void HataKatalogu_All_Disaridan_Degistirilememeli()
    {
        ErrorCatalog.All.Should().NotBeOfType<AppError[]>();
        ((System.Collections.IList)ErrorCatalog.All).IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void HataKatalogu_Teknik_Mesaj_Yer_Tutucusu_With_Ile_Doldurulabilmeli()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.TopoKendiUzerindeKesisim);

        var doldurulmus = hata with { TechnicalMessage = hata.TechnicalMessage.Replace("{parcelId}", "902/1/C1") };

        doldurulmus.TechnicalMessage.Should().Be("Self-intersection detected in polygon 902/1/C1.");
        doldurulmus.Code.Should().Be(hata.Code);
    }
}

public sealed class ResultTests
{
    [Fact]
    public void Result_Basari_Deger_Tasimali_Ve_Hata_Tasimamali()
    {
        var sonuc = Result<int>.Success(42);

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.IsFailure.Should().BeFalse();
        sonuc.Value.Should().Be(42);
        var act = () => sonuc.Error;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Result_Hata_AppError_Tasimali_Ve_Deger_Vermemeli()
    {
        var hata = ErrorCatalog.Get(ErrorCodes.TapuHisseToplamiBirDegil);

        var sonuc = Result<string>.Failure(hata);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Should().Be(hata);
        var act = () => sonuc.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Result_Hata_Null_AppError_Reddedilmeli()
    {
        var act = () => Result<int>.Failure(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Result_Basari_Null_Deger_Reddedilmeli()
    {
        var act = () => Result<string>.Success(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Result_TryGetValue_Basarida_Deger_Hatada_False_Vermeli()
    {
        Result<int>.Success(7).TryGetValue(out var deger).Should().BeTrue();
        deger.Should().Be(7);
        Result<int>.Failure(ErrorCatalog.Get(null)).TryGetValue(out _).Should().BeFalse();
    }

    [Fact]
    public void Result_Default_Deger_Basarisiz_Sayilmali_Ve_Genel_Hata_Tasimali()
    {
        var sonuc = default(Result<int>);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be(ErrorCodes.GenelBeklenmeyen);
    }
}
