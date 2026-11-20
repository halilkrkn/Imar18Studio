using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Imar18.Application.Girdi;

/// <summary>
/// SAHA-011 / SORU-055 (proje sahibi kararı, 20 Kasım 2026): TAKBİS / Kroki malik alanı. Uygulama çevrimdışı çalıştığı için yerel
/// ekranlarda malik <c>AD SOYAD (BABA ADI)</c> (baba adı yoksa <c>AD SOYAD</c>) biçiminde gösterilir ve kabul edilir; eski anonim
/// kodlar (<c>M01</c>, <c>Malik-01</c>) geçerli kalır. KVKK sınırı: 11 haneli T.C. Kimlik No (ayırıcılı veya metne gömülü)
/// içeren değer reddedilir; denetim kaydına ve teknik mesajlara malik değeri yazılmaz (yalnız sayaç / genel neden).
/// </summary>
public static partial class MalikEtiketi
{
    /// <summary>Malik alanı en fazla karakter sayısı (aynı adlı farklı kişi eki " #n" dahil).</summary>
    public const int EnFazlaUzunluk = 100;

    /// <summary>Üretilen etiketin ek (" #n") için boş bırakılan taban uzunluğu sınırı.</summary>
    private const int TabanEnFazlaUzunluk = EnFazlaUzunluk - 8;

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    [GeneratedRegex(@"\s+#\d+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex EkDeseni();

    /// <summary>T.C. Kimlik No reddi için rakam sayısı eşiği (11 hane).</summary>
    public const int KimlikNoHaneSayisi = 11;

    /// <summary>
    /// Metin T.C. Kimlik No içerebilir mi: ayırıcılardan bağımsız olarak toplam rakam sayısı ≥ 11 (reviewer H1: <c>123/456/789/01</c>,
    /// <c>123 - 456 - 789 - 01</c>, <c>AD (123,456,789,01)</c>, rakamlara yapışık <c>M0112345678901</c> dahil). Bilinçli olarak katıdır:
    /// meşru bir malik adı/kodu 11 rakam taşımaz. Düzenli ifade kullanılmaz (zaman aşımı istisnası girdiyi taşıyamaz).
    /// </summary>
    public static bool KimlikNumarasiIceriyor(string? deger) => deger is not null && deger.Count(char.IsDigit) >= KimlikNoHaneSayisi;

    /// <summary>
    /// Geçerli malik alanı: kırpılmış 1–<see cref="EnFazlaUzunluk"/> karakter; harf (Türkçe dahil), rakam, boşluk ve
    /// <c>- _ . ( ) / ' , &amp; #</c>; <c>#</c> ile başlamaz (TAKBİS/Kroki metninde yorum satırıdır); <c>;</c>, denetim ve biçim
    /// karakterleri (ör. U+202E, U+001F) yok; T.C. Kimlik No içermez.
    /// </summary>
    public static bool GecerliMi(string? deger)
    {
        var t = deger?.Trim() ?? string.Empty;
        return t.Length is >= 1 and <= EnFazlaUzunluk
            && t[0] != '#'
            && t.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '(' or ')' or '/' or '\'' or ',' or '&' or '#')
            && !KimlikNumarasiIceriyor(t);
    }

    /// <summary>webtapu ad ve baba adından ekran etiketi: <c>AD SOYAD (BABA ADI)</c>; baba adı boşsa <c>AD SOYAD</c> (Türkçe büyük harf).</summary>
    public static string Olustur(string adSoyad, string? babaAdi)
    {
        ArgumentNullException.ThrowIfNull(adSoyad);
        var ad = Normalize(adSoyad);
        var baba = Normalize(babaAdi ?? string.Empty);
        var etiket = baba.Length == 0 ? ad : ad + " (" + baba + ")";
        return etiket.Length <= TabanEnFazlaUzunluk ? etiket : etiket[..TabanEnFazlaUzunluk].TrimEnd();
    }

    /// <summary>Aynı adlı farklı kişi eki: <c>ETİKET #n</c> (n ≥ 2).</summary>
    public static string Ekli(string etiket, int sira) =>
        etiket + " #" + sira.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Karşılaştırma anahtarı: Türkçe büyük harf, tek boşluk, <c>İ</c> → <c>I</c> katlaması (reviewer M3: NCZ'deki ASCII
    /// <c>MALIK-01</c> ≡ TAKBİS <c>Malik-01</c>); <c>AD / BABA</c> ve <c>AD(BABA)</c> → <c>AD (BABA)</c>; <c> #n</c> eki korunur. Kroki ↔ TAKBİS eşleşmesi ve parsel içi yinelenen malik denetimi bu anahtarla yapılır; saklanan kimlik yazıldığı gibi kalır.
    /// </summary>
    public static string Anahtar(string deger)
    {
        ArgumentNullException.ThrowIfNull(deger);
        var t = deger.Trim();
        var ek = EkDeseni().Match(t);
        var (ad, baba) = Bol(ek.Success ? t[..ek.Index] : t);
        var a = Normalize(ad);
        var b = Normalize(baba ?? string.Empty);
        return ((b.Length == 0 ? a : a + " (" + b + ")") + (ek.Success ? " " + ek.Value.Trim() : string.Empty)).Replace('İ', 'I');
    }

    /// <summary>Etiketin aynı adlı farklı kişi eki (<c> #n</c>) ya da boş metin.</summary>
    public static string EkiAl(string etiket)
    {
        ArgumentNullException.ThrowIfNull(etiket);
        var m = EkDeseni().Match(etiket.Trim());
        return m.Success ? " " + m.Value.Trim() : string.Empty;
    }

    /// <summary>
    /// Etiketi ad soyad ve baba adına ayırır (<c>AD (BABA)</c> veya <c>AD / BABA</c>; <c> #n</c> eki atılır; yazım korunur, yalnız
    /// boşluklar sadeleşir). Baba adı olmayan etiket ve eski kodlarda (<c>M01</c>) baba adı null.
    /// </summary>
    public static (string AdSoyad, string? BabaAdi) Ayristir(string etiket)
    {
        ArgumentNullException.ThrowIfNull(etiket);
        var t = EkDeseni().Replace(etiket.Trim(), string.Empty);
        var (ad, baba) = Bol(t);
        var b = Sadelestir(baba ?? string.Empty);
        return (Sadelestir(ad), b.Length == 0 ? null : b);
    }

    private static (string Ad, string? Baba) Bol(string t)
    {
        var ac = t.LastIndexOf('(');
        if (ac > 0 && t.EndsWith(')'))
        {
            return (t[..ac], t[(ac + 1)..^1]);
        }

        var b = t.LastIndexOf('/');
        return b > 0 && b < t.Length - 1 ? (t[..b], t[(b + 1)..]) : (t, null);
    }

    private static string Sadelestir(string metin) => string.Join(' ', metin.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Türkçe büyük harf ve tek boşluk normalizasyonu.</summary>
    public static string Normalize(string metin)
    {
        ArgumentNullException.ThrowIfNull(metin);
        var sb = new StringBuilder(metin.Length);
        foreach (var parca in metin.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(parca.ToUpper(Tr));
        }

        return sb.ToString();
    }
}
