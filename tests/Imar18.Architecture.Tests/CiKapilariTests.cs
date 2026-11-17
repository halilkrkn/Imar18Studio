using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Domain.Revizyon;
using Xunit;

namespace Imar18.Architecture.Tests;

/// <summary>
/// P7.1 — spec §9.3/§9.4 CI kapıları (AC-CI-04/05, AC-PILOT-07/09). Pilotta mevzuat profili tek uzman onaylı sürümdür
/// (TPYS_2022; checksum/imza Faz 14'te — kilitli). Doğrulanmış PARSİD şeması yoktur: çıktı yalnız "deneysel iç format" olarak
/// adlandırılır, sahte uyumluluk iddia edilmez. Fen Klasörü şablon sürümü tanımlı ve pilot taslak olarak işaretlidir.
/// Bu kapılar sürüm/şema tanımları silinir veya resmî gibi gösterilirse CI'ı kırar (gerileme koruması).
/// </summary>
public sealed class CiKapilariTests
{
    /// <summary>Pilotta uzman onaylı tek mevzuat profili (spec §18, STATUS proje sahibi kararları).</summary>
    private static readonly string[] OnayliMevzuatProfilleri = ["TPYS_2022"];

    [Fact]
    public void CI_MevzuatProfili_Onayli_Olmali()
    {
        ProjeRevizyonu.MevzuatProfiliSurumu.Should().NotBeNullOrWhiteSpace();
        OnayliMevzuatProfilleri.Should().Contain(ProjeRevizyonu.MevzuatProfiliSurumu, "onaysız mevzuat profili release'i engeller (AC-CI-04)");
        ApprovalService.MevzuatProfiliSurumu.Should().Be(ProjeRevizyonu.MevzuatProfiliSurumu,
            "onay ekranında gösterilen profil, revizyona giren profille aynı olmalı");
    }

    [Fact]
    public void CI_Parsid_SemaSurumu_Tanimli_Olmali()
    {
        var durum = PilotDamgasi.ParsidSemaDurumu;

        durum.Should().NotBeNullOrWhiteSpace("PARSİD şema durumu tanımsızsa resmî ihracat engellenir (AC-CI-05)");
        durum.Should().StartWith("Deneysel iç format", "doğrulanmış resmî şema yokken çıktı yalnız deneysel iç formattır (AC-PILOT-09)");
        durum.Should().NotContainEquivalentOf("uyumlu", "sahte PARSİD uyumluluğu iddia edilemez (security.md)");
        durum.Should().NotContainEquivalentOf("resmî şema", "resmî şema iddiası yok");
    }

    [Fact]
    public void CI_FenKlasoru_SablonSurumu_Tanimli_Olmali()
    {
        var surum = PilotDamgasi.FenKlasoruSablonSurumu;

        surum.Should().NotBeNullOrWhiteSpace("Fen Klasörü şablon sürümü tanımsızsa resmî ihracat engellenir (AC-CI-05)");
        surum.Should().MatchRegex(@"^[A-Z0-9_]+_v\d+-PILOT-TASLAK$", "pilot şablonu sürümlü ve taslak olarak işaretli olmalı");
        PilotDamgasi.Metin.Should().Be("PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ", "pilot çıktıları damgalıdır (AC-PILOT-04)");
    }
}
