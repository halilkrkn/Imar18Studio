using System.Text.RegularExpressions;
using FluentAssertions;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Domain.Revizyon;
using Xunit;

namespace Imar18.Architecture.Tests;

/// <summary>
/// P7.1 — spec §9.3/§9.4 CI kapıları (AC-CI-04/05, AC-PILOT-07/09, SORU-038). Pilotta mevzuat profili tek uzman onaylı sürümdür
/// (TPYS_2022; checksum/imza Faz 14'te — kilitli). Doğrulanmış PARSİD şeması yoktur: çıktı yalnız "deneysel iç format" olarak
/// adlandırılır, sahte uyumluluk iddia edilmez. Fen Klasörü şablon sürümü tanımlı ve pilot taslak olarak işaretlidir.
/// RTM kaynağı <c>docs/Imar18Studio_Spesifikasyon_v2_19.md</c> §15–§16'dır: Pilot kapsamındaki (Faz P0–P7) tüm FR ve AC
/// satırlarında boş olmayan bir test veya TC referansı bulunur; Faz 11–20 satırları <c>Pilot Sonrası (Faz 11..20)</c> /
/// <c>Pilot Dışı / Faz 11+</c> olarak işaretli kalır.
/// Bu kapılar sürüm/şema tanımları veya RTM eşlemeleri bozulursa CI'ı kırar (gerileme koruması).
/// </summary>
public sealed class CiKapilariTests
{
    /// <summary>Pilotta uzman onaylı tek mevzuat profili (spec §18, STATUS proje sahibi kararları).</summary>
    private static readonly string[] OnayliMevzuatProfilleri = ["TPYS_2022"];

    private static readonly Regex PilotEvreDeseni = new(@"^Pilot\s*\(P[0-7]", RegexOptions.Compiled);
    private static readonly Regex Faz11SonrasiEvreDeseni = new(
        @"^(Pilot\s+Sonrası\s*\(Faz\s*(1[1-9]|20)\)|Pilot\s+Dışı\s*/\s*Faz\s*11\+)$",
        RegexOptions.Compiled);
    private static readonly Regex FrKoduDeseni = new(@"\bFR-F\d+\.\d+\b", RegexOptions.Compiled);
    private static readonly Regex AcKoduDeseni = new(@"\bAC-[A-Z0-9]+-\d+", RegexOptions.Compiled);
    private static readonly Regex TcVeyaTestDeseni = new(@"\b(TC-[A-Z0-9]+-\d+|[A-Za-z_][A-Za-z0-9_]*Tests?)\b", RegexOptions.Compiled);
    private static readonly Regex Bolum16FrMaddeDeseni = new(@"^\s*-\s*\*\*(FR-F\d+\.\d+)\b", RegexOptions.Compiled);

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

    [Fact]
    public void CI_RTM_Eksiksiz_Olmali()
    {
        var yol = SpesifikasyonYolunuBul();
        var satirlar = File.ReadAllLines(yol);

        var bolum15 = BolumSatirlariniAl(satirlar, "## 15.", "## 16.");
        var bolum16 = BolumSatirlariniAl(satirlar, "## 16.", "## 17.");

        bolum15.Should().NotBeEmpty("RTM tablosu docs/Imar18Studio_Spesifikasyon_v2_19.md §15 içinde bulunmalı (SORU-038)");
        bolum16.Should().NotBeEmpty("Yeni FR listesi docs/Imar18Studio_Spesifikasyon_v2_19.md §16 içinde bulunmalı (SORU-038)");

        var rtmSatirlari = RtmTablosunuAyristir(bolum15);
        rtmSatirlari.Should().NotBeEmpty("§15 RTM tablosunda izlenebilirlik satırları bulunmalı");

        var pilotSatirlari = new List<RtmSatiri>();
        var faz11SonrasiSatirlari = new List<RtmSatiri>();

        foreach (var satir in rtmSatirlari)
        {
            var pilotMu = PilotEvreDeseni.IsMatch(satir.Evre);
            var faz11SonrasiMi = Faz11SonrasiEvreDeseni.IsMatch(satir.Evre);

            (pilotMu || faz11SonrasiMi).Should().BeTrue(
                $"her RTM satırı ya Pilot (Faz P0–P7) ya da 'Pilot Sonrası (Faz 11..20)' / 'Pilot Dışı / Faz 11+' olarak işaretlenmeli (FR='{satir.FrKodu}', Evre='{satir.Evre}')");

            FrKoduDeseni.IsMatch(satir.FrKodu).Should().BeTrue(
                $"RTM satırında geçerli bir FR kodu bulunmalı (satır FR='{satir.FrKodu}')");
            AcKoduDeseni.IsMatch(satir.KabulKriteri).Should().BeTrue(
                $"RTM satırında geçerli bir AC kabul kriteri bulunmalı (FR='{satir.FrKodu}', AC='{satir.KabulKriteri}')");

            if (pilotMu)
            {
                pilotSatirlari.Add(satir);
                satir.TestKodu.Should().NotBeNullOrWhiteSpace(
                    $"Pilot kapsamındaki FR/AC satırında boş olmayan test veya TC referansı bulunmalı (FR='{satir.FrKodu}', AC='{satir.KabulKriteri}')");
                TcVeyaTestDeseni.IsMatch(satir.TestKodu).Should().BeTrue(
                    $"Pilot kapsamındaki FR/AC satırında geçerli bir TC veya test referansı bulunmalı (FR='{satir.FrKodu}', TC='{satir.TestKodu}')");
                satir.CekirdekModul.Should().NotBeNullOrWhiteSpace(
                    $"Pilot kapsamındaki RTM satırında çekirdek modül tanımlı olmalı (FR='{satir.FrKodu}')");
            }
            else
            {
                faz11SonrasiSatirlari.Add(satir);
            }
        }

        pilotSatirlari.Should().NotBeEmpty("Pilot (Faz P0–P7) kapsamı RTM satırları bulunmalı");
        faz11SonrasiSatirlari.Should().NotBeEmpty("Faz 11–20 satırları Pilot Dışı / Faz 11+ olarak işaretli kalmalı");

        var rtmFrEslemesi = rtmSatirlari
            .SelectMany(s => FrKoduDeseni.Matches(s.FrKodu).Select(m => (Fr: m.Value, Satir: s)))
            .ToLookup(x => x.Fr, x => x.Satir, StringComparer.Ordinal);

        var bolum16Frler = bolum16
            .Select(s => (Satir: s, Eslesme: Bolum16FrMaddeDeseni.Match(s)))
            .Where(x => x.Eslesme.Success)
            .Select(x => (Fr: x.Eslesme.Groups[1].Value, Metin: x.Satir))
            .ToList();

        bolum16Frler.Should().NotBeEmpty("§16 altında FR maddeleri bulunmalı");

        foreach (var (fr, metin) in bolum16Frler)
        {
            var eslesenSatirlar = rtmFrEslemesi[fr].ToList();
            eslesenSatirlar.Should().NotBeEmpty(
                $"§16'da tanımlanan her FR kodu §15 RTM tablosunda yer almalı (eksik FR='{fr}')");

            var yalnizFaz11Sonrasi = !metin.Contains("Pilot", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(metin, @"\bFaz\s*P[0-7]", RegexOptions.IgnoreCase);

            if (yalnizFaz11Sonrasi)
            {
                eslesenSatirlar.Should().OnlyContain(
                    s => Faz11SonrasiEvreDeseni.IsMatch(s.Evre),
                    $"§16'da yalnız Faz 11–20 kapsamındaki {fr} kodu §15'te de Pilot Sonrası / Pilot Dışı olarak işaretli kalmalı");
            }
            else
            {
                eslesenSatirlar.Should().OnlyContain(
                    s => TcVeyaTestDeseni.IsMatch(s.TestKodu) && AcKoduDeseni.IsMatch(s.KabulKriteri),
                    $"§16'da Pilot kapsamına giren {fr} kodu §15'te geçerli TC/test ve AC referansına sahip olmalı");
            }
        }
    }

    private static string SpesifikasyonYolunuBul()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null)
        {
            var aday = Path.Combine(dizin.FullName, "docs", "Imar18Studio_Spesifikasyon_v2_19.md");
            if (File.Exists(aday))
            {
                return aday;
            }

            dizin = dizin.Parent;
        }

        throw new FileNotFoundException("RTM kaynağı olan docs/Imar18Studio_Spesifikasyon_v2_19.md bulunamadı.");
    }

    private static List<string> BolumSatirlariniAl(string[] satirlar, string baslangicBaslik, string bitisBaslik)
    {
        var sonuc = new List<string>();
        var icerde = false;

        foreach (var satir in satirlar)
        {
            if (satir.StartsWith(baslangicBaslik, StringComparison.Ordinal))
            {
                icerde = true;
                continue;
            }

            if (icerde && satir.StartsWith(bitisBaslik, StringComparison.Ordinal))
            {
                break;
            }

            if (icerde)
            {
                sonuc.Add(satir);
            }
        }

        return sonuc;
    }

    private static List<RtmSatiri> RtmTablosunuAyristir(List<string> bolum15Satirlari)
    {
        var tabloSatirlari = bolum15Satirlari
            .Select(s => s.Trim())
            .Where(s => s.StartsWith('|') && s.EndsWith('|'))
            .ToList();

        tabloSatirlari.Count.Should().BeGreaterThanOrEqualTo(3, "§15 RTM tablosu başlık, ayraç ve veri satırları içermeli");

        var basliklar = HucreleriAyir(tabloSatirlari[0]);
        basliklar.Should().HaveCount(7, "§15 RTM tablosu 7 sütunlu olmalı");
        basliklar[2].Should().Contain("FR");
        basliklar[3].Should().Contain("TC");
        basliklar[5].Should().Contain("Kabul Kriteri");
        basliklar[6].Should().Be("Evre");

        var sonuc = new List<RtmSatiri>();
        foreach (var satir in tabloSatirlari.Skip(2))
        {
            var hucreler = HucreleriAyir(satir);
            hucreler.Should().HaveCount(7, $"RTM satırı 7 sütun içermeli: {satir}");
            sonuc.Add(new RtmSatiri(
                FrKodu: hucreler[2],
                TestKodu: hucreler[3],
                CekirdekModul: hucreler[4],
                KabulKriteri: hucreler[5],
                Evre: hucreler[6]));
        }

        return sonuc;
    }

    private static string[] HucreleriAyir(string markdownSatir) =>
        markdownSatir[1..^1].Split('|').Select(h => h.Trim()).ToArray();

    private sealed record RtmSatiri(
        string FrKodu,
        string TestKodu,
        string CekirdekModul,
        string KabulKriteri,
        string Evre);
}
