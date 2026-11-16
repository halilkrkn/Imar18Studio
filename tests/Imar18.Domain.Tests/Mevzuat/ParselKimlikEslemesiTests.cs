using FluentAssertions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tahsis;
using Xunit;

namespace Imar18.Domain.Tests.Mevzuat;

/// <summary>P3.3 — Geçici (901/A1, 902/1/C1) ↔ kalıcı tescil (101/1) çift yönlü kimlik eşlemesi (spec 25.3, D31 Kural 3).</summary>
public sealed class ParselKimlikEslemesiTests
{
    private static List<ParselKimlikCifti> ProjeACiftleri() =>
        AltinVeri.ProjeA().GetProperty("blocks").EnumerateArray()
            .SelectMany(b => b.GetProperty("parcels").EnumerateArray())
            .Select(p => new ParselKimlikCifti(p.GetProperty("geciciKod").GetString()!, p.GetProperty("tescilNo").GetString()!))
            .ToList();

    [Fact]
    public void ParselKimlik_ProjeA_14_Parsel_Iki_Yonlu_Eslenmeli()
    {
        var e = ParselKimlikEslemesi.Olustur(ProjeACiftleri()).Value;

        e.Ciftler.Should().HaveCount(14);
        e.TryTescilNo("901/C1", out var tescil).Should().BeTrue();
        tescil.Should().Be("103/1");
        e.TryGeciciKod("102/6", out var gecici).Should().BeTrue();
        gecici.Should().Be("901/B6");
    }

    [Fact]
    public void ParselKimlik_Bilinmeyen_Kod_False_Donmeli()
    {
        var e = ParselKimlikEslemesi.Olustur(ProjeACiftleri()).Value;

        e.TryTescilNo("999/Z9", out _).Should().BeFalse();
        e.TryGeciciKod("999/1", out _).Should().BeFalse();
    }

    [Fact]
    public void ParselKimlik_Ayni_Gecici_Kod_Iki_Tescile_Eslenemez()
    {
        ParselKimlikEslemesi.Olustur([new("902/1/C1", "103/1"), new("902/1/C1", "103/2")])
            .Error.Code.Should().Be(ErrorCodes.TapuParselKimlikEslemesiGecersiz);
    }

    [Fact]
    public void ParselKimlik_Ayni_Tescil_Iki_Gecici_Koda_Eslenemez()
    {
        ParselKimlikEslemesi.Olustur([new("902/1/C1", "103/1"), new("902/1/C2", "103/1")])
            .Error.Code.Should().Be(ErrorCodes.TapuParselKimlikEslemesiGecersiz);
    }

    [Fact]
    public void ParselKimlik_Bos_Liste_Veya_Bos_Kod_Reddedilmeli()
    {
        ParselKimlikEslemesi.Olustur([]).Error.Code.Should().Be(ErrorCodes.TapuParselKimlikEslemesiGecersiz);
        ParselKimlikEslemesi.Olustur([new(" ", "103/1")]).Error.Code.Should().Be(ErrorCodes.TapuParselKimlikEslemesiGecersiz);
        ParselKimlikEslemesi.Olustur([new("902/1/C1", "")]).Error.Code.Should().Be(ErrorCodes.TapuParselKimlikEslemesiGecersiz);
    }
}
