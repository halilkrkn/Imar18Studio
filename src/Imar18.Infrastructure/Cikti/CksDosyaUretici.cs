using System.Globalization;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Diagnostics;
using Imar18.Infrastructure.Cikti.Cks;

namespace Imar18.Infrastructure.Cikti;

/// <summary>
/// Netcad uyumlu <c>.CKS</c> DOP özeti yazıcısı (spec 25.1, SAHA-008, SORU-039). Bayt düzeni gerçek Netcad <c>DOP.CKS</c> ile
/// aynıdır: satır 1 boş, ardından 21 satır tam <see cref="SatirBayt"/> bayt (çerçeve, <c>Etiket = değer</c>, "Düzenleme Ortaklık
/// Payı Hesabı" bölümü), CRLF; Türkçe harfler Windows-1254, çerçeve CP437 baytı (<see cref="NetcadCksKodlayici"/>).
/// Pilot damgası CKS'e yazılmaz (proje sahibi kararı: Netcad 1. satırdaki metnin üstüne çerçeve çiziyor); bu yüzden dosyanın
/// içeriği resmî Netcad dosyasından ayırt edilemez — taslak niteliği UI'daki damga, önerilen dosya adındaki <c>PILOT</c> ibaresi
/// ve RevisionId'li audit kaydıyla izlenir (<c>.sha256</c> yalnız bütünlük içindir). Yazılan dosya <see cref="Cad.Cks.CksFileReader"/>
/// ile birebir geri okunur. Yazmadan önce "Hesap" bölümü aritmetiği doğrulanır; tutarsızsa veya bir satır 87 baytı aşarsa sahte
/// dosya üretilmez (<c>ERR-CIKTI-003</c>).
/// </summary>
public sealed class CksDosyaUretici : ICiktiUretici
{
    /// <summary>Çerçeve satırı bayt genişliği (│ + 85 + │), gerçek Netcad dosyasıyla aynı.</summary>
    public const int SatirBayt = 87;

    private const int Genislik = SatirBayt - 2;

    public CiktiTuru Tur => CiktiTuru.CksDopOzeti;

    public string Uzanti => ".CKS";

    public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paket);
        var ozet = CksOzetiEslemesi.Olustur(paket.Dop);
        return ozet.IsFailure ? Result<byte[]>.Failure(ozet.Error) : Yaz(ozet.Value);
    }

    /// <summary>Özeti Netcad CKS baytlarına dönüştürür (1254 + CP437, CRLF, satır 1 boş).</summary>
    public static Result<byte[]> Yaz(CksDopOzeti o)
    {
        ArgumentNullException.ThrowIfNull(o);

        var dopal = o.ToplamDuzenlemeyeGirenDopAlinanM2;
        if (dopal <= 0m || dopal - o.ToplamImarAdaAlaniM2 != o.KamuyaAyrilanAlanM2
            || decimal.Round(o.KamuyaAyrilanAlanM2 / dopal, 7, MidpointRounding.AwayFromZero) != o.Dopo)
        {
            return Hata("calculation section arithmetic mismatch");
        }

        var satirlar = new List<string>(22);
        void Satir(string s) => satirlar.Add(s);
        string Kutu(string icerik) => "│" + icerik.PadRight(Genislik) + "│";
        string? tasma = null;
        string Sutun(string deger, int genislik)
        {
            // Sabit genişlikli Netcad sütunu: taşan değer sütunları kaydırır → sessiz bozuk düzen yerine hata.
            tasma ??= deger.Length > genislik ? $"value of {deger.Length} chars exceeds {genislik}-char column" : null;
            return deger.PadLeft(genislik);
        }

        string Alan(string etiket, string deger) => Kutu(" " + etiket.PadRight(38) + "= " + Sutun(deger, 11));
        static string M2(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string Oran(decimal v) => v.ToString("0.0000000", CultureInfo.InvariantCulture);
        var cizgi = new string('─', Genislik);

        Satir(string.Empty);
        Satir("┌" + cizgi + "┐");
        Satir(Alan("Toplam Tahsis", M2(o.ToplamTahsisM2)));
        Satir(Alan("Toplam İmar Ada Alanı", M2(o.ToplamImarAdaAlaniM2)));
        Satir(Alan("Toplam Ortak Katılım Alanı", M2(o.ToplamOrtakKatilimAlaniM2)));
        Satir(Alan("Toplam Düzenlemeye Giren (Dopal)", M2(o.ToplamDuzenlemeyeGirenDopAlinanM2)));
        Satir(Alan("Toplam Düzenlemeye Giren (Dopalma)", M2(o.ToplamDuzenlemeyeGirenDopAlinmayanM2)));
        Satir(Alan("Toplam Kamulaştırılan Alan", M2(o.ToplamKamulastirilanAlanM2)));
        Satir(Alan("Toplam Bağışlanan Alan", M2(o.ToplamBagislananAlanM2)));
        Satir(Alan("Toplam Kadastro Parsel Alanı", M2(o.ToplamKadastroParselAlaniM2)));
        Satir(Alan("Toplam Pay", M2(o.ToplamPayM2)));
        Satir("├" + cizgi + "┤");
        Satir(Alan("Ortak Katılım Oranı", Oran(o.OrtakKatilimOrani)));
        Satir(Alan("Düzenleme Ortaklık Payı Oranı (DOPO)", Oran(o.Dopo)));
        Satir(Alan("DOPO (OKO ile birlikte)", Oran(o.DopoOkoIleBirlikte)));
        Satir(Alan("Maksimum Dop Oranı", o.MaksimumDopOrani.ToString("0.00", CultureInfo.InvariantCulture)));
        Satir("├" + cizgi + "┤");
        Satir(Kutu("Düzenleme Ortaklık Payı Hesabı"));
        Satir("├" + cizgi + "┤");
        Satir(Kutu("Kamuya Ayrılan Alan".PadRight(39) + "= " + Sutun(M2(dopal), 11) + " - " + Sutun(M2(o.ToplamImarAdaAlaniM2), 10) + " = " + Sutun(M2(o.KamuyaAyrilanAlanM2), 10)));
        Satir(Kutu("Düzenleme Ortaklık Payı".PadRight(39) + "= " + Sutun(M2(o.KamuyaAyrilanAlanM2), 11) + " / " + Sutun(M2(dopal), 10) + " = " + Sutun(Oran(o.Dopo), 10)));
        Satir("└" + cizgi + "┘");
        if (tasma is not null)
        {
            return Hata(tasma);
        }

        var dosya = new List<byte>(2 + (21 * (SatirBayt + 2)));
        for (var i = 0; i < satirlar.Count; i++)
        {
            var bayt = NetcadCksKodlayici.Kodla(satirlar[i]);
            if (bayt.IsFailure)
            {
                return bayt;
            }

            if (i > 0 && bayt.Value.Length != SatirBayt)
            {
                return Hata($"line {i + 1} is {bayt.Value.Length} bytes, expected {SatirBayt}");
            }

            dosya.AddRange(bayt.Value);
            dosya.Add(0x0D);
            dosya.Add(0x0A);
        }

        return Result<byte[]>.Success([.. dosya]);
    }

    private static Result<byte[]> Hata(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.CiktiCadDisaAktarim);
        return Result<byte[]>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage.Replace("{format}", "CKS (" + neden + ")", StringComparison.Ordinal),
        });
    }
}
