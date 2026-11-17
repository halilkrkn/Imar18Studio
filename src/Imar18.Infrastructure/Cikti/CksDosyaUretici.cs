using System.Globalization;
using System.Text;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Diagnostics;

namespace Imar18.Infrastructure.Cikti;

/// <summary>
/// Netcad uyumlu <c>.CKS</c> DOP özeti yazıcısı (spec 25.1, P4'ten kalan SORU-033 b). Satır düzeni Netcad örneğiyle aynıdır
/// (çerçeve, <c>Etiket = değer</c>, "Düzenleme Ortaklık Payı Hesabı" bölümü); kodlama UTF-8'dir (spec 25.1: CP857/UTF-8).
/// Pilot damgası çerçevenin üstünde ayrı bir başlık satırıdır — çerçeve içindeki alanları bozmaz; Netcad'in bu satırı kabul
/// edip etmediği doğrulanmadı (STATUS SORU-034). Yazılan dosya <see cref="Cad.Cks.CksFileReader"/> ile birebir geri okunur.
/// Yazmadan önce "Hesap" bölümü aritmetiği doğrulanır; tutarsızsa sahte dosya üretilmez (<c>ERR-CIKTI-003</c>).
/// </summary>
public sealed class CksDosyaUretici : ICiktiUretici
{
    private const int Genislik = 85;

    public CiktiTuru Tur => CiktiTuru.CksDopOzeti;

    public string Uzanti => ".CKS";

    public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paket);
        var ozet = CksOzetiEslemesi.Olustur(paket.Dop);
        return ozet.IsFailure ? Result<byte[]>.Failure(ozet.Error) : Yaz(ozet.Value, paket.PilotDamgasi + " · Revizyon " + paket.RevizyonId);
    }

    /// <summary>Özeti CKS metnine dönüştürür (UTF-8, BOM yok, CRLF).</summary>
    public static Result<byte[]> Yaz(CksDopOzeti o, string damgaSatiri)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(damgaSatiri);

        var dopal = o.ToplamDuzenlemeyeGirenDopAlinanM2;
        if (dopal <= 0m || dopal - o.ToplamImarAdaAlaniM2 != o.KamuyaAyrilanAlanM2
            || decimal.Round(o.KamuyaAyrilanAlanM2 / dopal, 7, MidpointRounding.AwayFromZero) != o.Dopo)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiCadDisaAktarim);
            return Result<byte[]>.Failure(e with
            {
                TechnicalMessage = e.TechnicalMessage.Replace("{format}", "CKS (calculation section arithmetic mismatch)", StringComparison.Ordinal),
            });
        }

        var sb = new StringBuilder();
        void Satir(string s) => sb.Append(s).Append("\r\n");
        string Kutu(string icerik) => "│" + icerik.PadRight(Genislik) + "│";
        string Alan(string etiket, string deger) => Kutu(" " + etiket.PadRight(38) + "= " + deger.PadLeft(11));
        static string M2(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string Oran(decimal v) => v.ToString("0.0000000", CultureInfo.InvariantCulture);
        var cizgi = new string('─', Genislik);

        Satir(damgaSatiri);
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
        Satir(Kutu("Kamuya Ayrılan Alan".PadRight(39) + "= " + M2(dopal).PadLeft(11) + " - " + M2(o.ToplamImarAdaAlaniM2).PadLeft(10) + " = " + M2(o.KamuyaAyrilanAlanM2).PadLeft(10)));
        Satir(Kutu("Düzenleme Ortaklık Payı".PadRight(39) + "= " + M2(o.KamuyaAyrilanAlanM2).PadLeft(11) + " / " + M2(dopal).PadLeft(10) + " = " + Oran(o.Dopo).PadLeft(10)));
        Satir("└" + cizgi + "┘");

        return Result<byte[]>.Success(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString()));
    }
}
