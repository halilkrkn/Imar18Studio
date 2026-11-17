using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Numerics;

namespace Imar18.Application.Cikti;

/// <summary>Üretilecek taslak çıktı türü.</summary>
public enum CiktiTuru
{
    /// <summary>ClosedXML .xlsx: Özet, 4'lü alan cetveli ve iki yönlü dağıtım cetveli (spec 25.5–25.6).</summary>
    Cetveller,

    /// <summary>QuestPDF .pdf Fen Klasörü: FR.348 kapak, He/M2/Dm2 alan ve tescil sayfaları, FR.521 rölöve krokisi (spec 25.9).</summary>
    FenKlasoru,

    /// <summary>Netcad uyumlu .CKS DOP özeti (spec 25.1, SORU-033 b).</summary>
    CksDopOzeti,
}

/// <summary>Koordine özet satırı (ITRF-96 / proje CRS): Y Sağa (Doğu), X Yukarı (Kuzey).</summary>
public sealed record KoordinatSatiri(int NoktaNo, decimal Y, decimal X);

/// <summary>Cepheli alan satırı: köşe noktası ve bu köşeden sonrakine kenar uzunluğu (m, 2 ondalık).</summary>
public sealed record CepheSatiri(int NoktaNo, decimal Y, decimal X, decimal CepheM);

/// <summary>
/// Noktalı / cepheli alan cetveli parsel kaydı. <see cref="HesapAlaniM2"/> Decimal Gauss alanı; <see cref="DengelenmisAlanM2"/>
/// Y1 dengelemesi sonrası alan (spec 25.4: yalnız yol terk değişir; dengeleme yoksa hesap alanı); <see cref="Alan"/> Fen Klasörü ve
/// tescil için dengelenmiş alanın He | M2 | Dm2 gösterimidir.
/// </summary>
public sealed record ParselCetvelSatiri(
    string GeciciKod,
    string TescilNo,
    ParselRolu Rol,
    IReadOnlyList<int> NoktaNolari,
    IReadOnlyList<CepheSatiri> Kenarlar,
    decimal HesapAlaniM2,
    decimal DengelenmisAlanM2,
    HeM2Dm2 Alan,
    IReadOnlyList<GeodeticPoint> Koseler);

/// <summary>Dengeleme alan cetveli satırı (spec 25.5 madde 4).</summary>
public sealed record DengelemeSatiri(string GeciciKod, string TescilNo, decimal HesapAlaniM2, decimal DuzeltmeM2, decimal DengelenmisAlanM2);

/// <summary>Dengeleme alan cetveli ve alt toplam kutusu (Tapu / Hesaplanan / Fark / Tecviz).</summary>
public sealed record DengelemeCetveli(IReadOnlyList<DengelemeSatiri> Satirlar, decimal TapuAlaniM2, decimal HesaplananM2, decimal FarkM2, decimal TecvizSiniriM2);

/// <summary>DOPO / OKO özet formu (spec 25.1). Oranlar 7 ondalık; <see cref="DopoOkoIleBirlikte"/> tam kesirden yuvarlanır.</summary>
public sealed record DopOzeti(
    decimal R1,
    decimal D1,
    decimal R2,
    decimal D2,
    decimal R3,
    decimal OrtakKatilimM2,
    decimal DopMiktariM2,
    decimal KamuyaAyrilanM2,
    decimal Dopo,
    decimal Oko,
    decimal DopoOkoIleBirlikte)
{
    public static DopOzeti From(DopSonucu sonuc)
    {
        ArgumentNullException.ThrowIfNull(sonuc);
        var g = sonuc.Girdi;
        var okoTam = Fraction.FromDecimal(g.OrtakKatilimM2) / Fraction.FromDecimal(g.R1 - g.D1);
        return new DopOzeti(g.R1, g.D1, g.R2, g.D2, g.R3, g.OrtakKatilimM2, sonuc.DopMiktariM2, sonuc.KamuyaAyrilanAlanM2,
            sonuc.Dopo, sonuc.Oko, (sonuc.DopoTam + okoTam).ToDecimal(DopCalculator.OranOndalik));
    }
}

/// <summary>Mal Sahipleri Araştırma ve Özet Formu satırı (kadastro parseline sıralı, spec 25.6 madde 1). Malik kodu anonimdir.</summary>
public sealed record MalikDagitimSatiri(
    string KadastroParselId,
    string MalikKodu,
    decimal GirenM2,
    decimal DopKesintisiM2,
    decimal NetHakM2,
    string ImarParseli,
    decimal TahsisM2);

/// <summary>Tescile Esas Dağıtım Cetveli satırı (imar parseline sıralı, spec 25.6 madde 2): hisse <c>TAM</c> veya dm² paydalı.</summary>
public sealed record TescilDagitimSatiri(
    string TescilNo,
    string GeciciKod,
    decimal ParselAlaniM2,
    HeM2Dm2 ParselAlani,
    string MalikKodu,
    string KadastroParselleri,
    decimal AlanM2,
    string Hisse);

/// <summary>
/// Çıktı üreticilerinin tek girdisi (Application DTO; NTS/double yok). Onaylı revizyonun değişmez anlık görüntüsünden üretilir;
/// her çıktı <see cref="PilotDamgasi"/> ve <see cref="RevizyonId"/> taşır.
/// </summary>
public sealed record CiktiPaketi(
    string ProjeAdi,
    string RevizyonId,
    string OnaylayanId,
    DateTimeOffset OnayZamani,
    string PilotDamgasi,
    string SablonSurumu,
    string CrsAciklamasi,
    int OlcekPaydasi,
    IReadOnlyList<KoordinatSatiri> Koordinatlar,
    IReadOnlyList<ParselCetvelSatiri> Parseller,
    DengelemeCetveli? Dengeleme,
    DopOzeti Dop,
    IReadOnlyList<MalikDagitimSatiri> MalikSirali,
    IReadOnlyList<TescilDagitimSatiri> ImarSirali,
    IReadOnlyList<string> Uyarilar);

/// <summary>Taslak çıktı üreticisi (Infrastructure: ClosedXML, QuestPDF, CKS). Hata durumunda istisna yerine Result döner.</summary>
public interface ICiktiUretici
{
    CiktiTuru Tur { get; }

    /// <summary>Noktalı dosya uzantısı.</summary>
    string Uzanti { get; }

    Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default);
}

/// <summary>Atomik çıktı dosyası yazıcısı (Infrastructure).</summary>
public interface ICiktiYazici
{
    Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default);

    Task SilAsync(string yol, CancellationToken cancellationToken = default);
}

/// <summary>
/// DOP özetinin Netcad <c>.CKS</c> alanlarına eşlemesi (SORU-033 a onaylı: Dopal → R1, İmar Ada Alanı → R2, Dopalma → R3;
/// diğer alanlar SORU-034 varsayımı: Toplam Tahsis = R2, Kadastro Parsel Alanı = Toplam Pay = R1, Kamulaştırılan = Bağışlanan = 0).
/// CKS biçiminde D1/D2 alanı yoktur; D1, D2 veya R3 sıfırdan farklıysa sahte uyum üretilmez (<c>ERR-CIKTI-003</c>).
/// </summary>
public static class CksOzetiEslemesi
{
    public static Result<CksDopOzeti> Olustur(DopOzeti dop)
    {
        ArgumentNullException.ThrowIfNull(dop);
        if (dop.D1 != 0m || dop.D2 != 0m || dop.R3 != 0m)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiCadDisaAktarim);
            return Result<CksDopOzeti>.Failure(e with
            {
                TechnicalMessage = e.TechnicalMessage.Replace("{format}", "CKS (D1, D2 and R3 must be zero; CKS has no D1/D2 fields)", StringComparison.Ordinal),
            });
        }

        return Result<CksDopOzeti>.Success(new CksDopOzeti(
            ToplamTahsisM2: dop.R2,
            ToplamImarAdaAlaniM2: dop.R2,
            ToplamOrtakKatilimAlaniM2: dop.OrtakKatilimM2,
            ToplamDuzenlemeyeGirenDopAlinanM2: dop.R1,
            ToplamDuzenlemeyeGirenDopAlinmayanM2: dop.R3,
            ToplamKamulastirilanAlanM2: 0m,
            ToplamBagislananAlanM2: 0m,
            ToplamKadastroParselAlaniM2: dop.R1,
            ToplamPayM2: dop.R1,
            OrtakKatilimOrani: dop.Oko,
            Dopo: dop.Dopo,
            DopoOkoIleBirlikte: dop.DopoOkoIleBirlikte,
            MaksimumDopOrani: DopCalculator.YasalUstSinir,
            KamuyaAyrilanAlanM2: dop.KamuyaAyrilanM2));
    }
}
