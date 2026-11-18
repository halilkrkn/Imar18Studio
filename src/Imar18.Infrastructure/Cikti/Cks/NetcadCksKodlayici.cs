using System.Text;
using Imar18.Domain.Diagnostics;

namespace Imar18.Infrastructure.Cikti.Cks;

/// <summary>
/// Netcad <c>.CKS</c> karışık kodlaması (P7.3-D, SAHA-008, SORU-039). Gerçek Netcad <c>DOP.CKS</c> dosyasının bayt incelemesi:
/// Türkçe harfler ve ASCII Windows-1254, çerçeve karakterleri OEM/CP437 baytlarıyla yazılır (┌DA ─C4 ┐BF │B3 ├C3 ┤B4 └C0 ┘D9).
/// Tek bir kod sayfası bu dosyayı üretemez; bu yüzden karakter karakter eşlenir. Eşlenemeyen karakter sessizce <c>?</c> olmaz,
/// <c>ERR-CIKTI-003</c> döner. Çözme aynı tablonun tersidir (çerçeve baytları 1254'te Ú Ä ¿ ³ … harflerine denk gelir; CKS'te bu
/// harfler kullanılmadığından çerçeve olarak çözülür).
/// </summary>
public static class NetcadCksKodlayici
{
    private static readonly Dictionary<char, byte> CerceveBaytlari = new()
    {
        ['┌'] = 0xDA, ['─'] = 0xC4, ['┐'] = 0xBF, ['│'] = 0xB3, ['├'] = 0xC3, ['┤'] = 0xB4, ['└'] = 0xC0, ['┘'] = 0xD9,
    };

    private static readonly Dictionary<byte, char> CerceveKarakterleri = CerceveBaytlari.ToDictionary(p => p.Value, p => p.Key);

    private static readonly Encoding Turkce = CodePagesEncodingProvider.Instance.GetEncoding(
        1254, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback)
        ?? throw new InvalidOperationException("Windows-1254 kod sayfası bulunamadı.");

    /// <summary>Metni Netcad CKS baytlarına çevirir; 1254 veya çerçeve tablosunda olmayan karakter varsa hata döner.</summary>
    public static Result<byte[]> Kodla(string metin)
    {
        ArgumentNullException.ThrowIfNull(metin);
        var bayt = new List<byte>(metin.Length);
        var tek = new char[1];
        for (var i = 0; i < metin.Length; i++)
        {
            var c = metin[i];
            if (CerceveBaytlari.TryGetValue(c, out var b))
            {
                bayt.Add(b);
                continue;
            }

            // Vekil (surrogate), kontrol (C0/C1; satır sonunu yazıcı ekler) ve çerçeve baytına çakışan 1254 harfleri reddedilir.
            if (char.IsSurrogate(c) || char.IsControl(c) || CerceveKarakterleri.ContainsKey(TekBayt(c)))
            {
                return Hata(c);
            }

            tek[0] = c;
            try
            {
                bayt.AddRange(Turkce.GetBytes(tek));
            }
            catch (EncoderFallbackException)
            {
                return Hata(c);
            }
        }

        return Result<byte[]>.Success([.. bayt]);
    }

    /// <summary>Netcad CKS baytlarını metne çevirir (çerçeve baytları kutu karakterine, diğerleri Windows-1254).</summary>
    public static string Coz(ReadOnlySpan<byte> bayt)
    {
        var sb = new StringBuilder(bayt.Length);
        foreach (var b in bayt)
        {
            sb.Append(CerceveKarakterleri.TryGetValue(b, out var c) ? c : Turkce.GetString([b]));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Çerçeve baytına denk gelen 1254 karakterini (Ú Ä ¿ ³ Ã ´ À Ù) bulmak için: bu karakterler yazılırsa çözümde çerçeveye
    /// dönüşeceğinden gidiş-dönüş bozulur; bu yüzden reddedilir. Diğer karakterler için 0 döner.
    /// </summary>
    private static byte TekBayt(char c)
    {
        try
        {
            var b = Turkce.GetBytes([c]);
            return b.Length == 1 ? b[0] : (byte)0;
        }
        catch (EncoderFallbackException)
        {
            return 0;
        }
    }

    private static Result<byte[]> Hata(char c)
    {
        var e = ErrorCatalog.Get(ErrorCodes.CiktiCadDisaAktarim);
        return Result<byte[]>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage.Replace(
                "{format}", $"CKS (character U+{(int)c:X4} not representable in Netcad Windows-1254/CP437 encoding)", StringComparison.Ordinal),
        });
    }
}
