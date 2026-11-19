using System.Globalization;
using Imar18.Application.Cikti;
using Imar18.Domain.Geometry;

namespace Imar18.Infrastructure.Cikti.FenKlasoru;

/// <summary>Cepheli alan hesabı satırı: parselin ilk satırında Ada/Parsel ve hesap alanı, her köşede nokta, Y, X ve cephe.</summary>
internal sealed record CepheliSatir(string AdaParsel, string HesapAlan, int NoktaNo, decimal Y, decimal X, decimal CepheM);

/// <summary>
/// P7.3-C1: Fen Klasörü belgelerinin saf (QuestPDF'siz) veri hazırlığı — ada/parsel ayrıştırma, imar adası gruplama,
/// cepheli alan sayfalama ve parselasyon ölçeği. Tüm hesaplar <c>decimal</c>'dir (InvariantCulture biçim).
/// </summary>
internal static class FenKlasoruVerisi
{
    /// <summary>Cepheli alan hesabı: şerit başına satır ve sayfa başına şerit (yatay A4, örnek pakette yan yana şeritler).</summary>
    public const int CepheliSeritSatiri = 50;

    public const int CepheliSayfaSeridi = 3;

    /// <summary>Parselasyon haritası için standart ölçek paydaları (küçükten büyüğe).</summary>
    public static IReadOnlyList<int> StandartOlcekler { get; } = [100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000, 20000, 25000, 50000, 100000];

    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string F2(decimal v) => v.ToString("0.00", Inv);

    public static string Oran(decimal v) => v.ToString("0.0000000", Inv);

    /// <summary>"101/2" → "101"; '/' yoksa tamamı.</summary>
    public static string AdaNo(string tescilNo)
    {
        ArgumentNullException.ThrowIfNull(tescilNo);
        var i = tescilNo.IndexOf('/', StringComparison.Ordinal);
        return i < 0 ? tescilNo.Trim() : tescilNo[..i].Trim();
    }

    /// <summary>"101/2" → "2"; '/' yoksa tamamı.</summary>
    public static string ParselNo(string tescilNo)
    {
        ArgumentNullException.ThrowIfNull(tescilNo);
        var i = tescilNo.IndexOf('/', StringComparison.Ordinal);
        return i < 0 ? tescilNo.Trim() : tescilNo[(i + 1)..].Trim();
    }

    /// <summary>Gösterim kimliği: tescil no varsa o, yoksa geçici kod.</summary>
    public static string Kimlik(ParselCetvelSatiri p) => p.TescilNo is { Length: > 0 } t && t != "-" ? t : p.GeciciKod;

    /// <summary>Kadastro dışı (imar, yol, kamu) parseller, tescil no sırasıyla.</summary>
    public static IReadOnlyList<ParselCetvelSatiri> HesapParselleri(CiktiPaketi p) => p.Parseller
        .Where(s => s.Rol != ParselRolu.Kadastro)
        .OrderBy(Kimlik, ParselNoKarsilastirici.Instance)
        .ThenBy(s => s.GeciciKod, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// İmar adaları (tescil no'su "ada/parsel" olan imar parsellerinin ada numaraları), sayısal sırada. Tescil Sayfası ve
    /// Rölöve krokisi bu sırayla ada başına ayrı dosyaya bölünür (<c>TESCİL SAYFASI-n</c>, <c>RÖLÖVE-n</c>; SORU-043 varsayımı).
    /// </summary>
    public static IReadOnlyList<string> ImarAdalari(CiktiPaketi p) => p.Parseller
        .Where(s => s.Rol == ParselRolu.Imar && s.TescilNo.Contains('/', StringComparison.Ordinal))
        .Select(s => AdaNo(s.TescilNo))
        .Concat(p.ImarSirali.Where(s => s.TescilNo.Contains('/', StringComparison.Ordinal)).Select(s => AdaNo(s.TescilNo)))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(a => a, ParselNoKarsilastirici.Instance)
        .ToArray();

    /// <summary>Verilen imar adasının imar parselleri.</summary>
    public static IReadOnlyList<ParselCetvelSatiri> AdaParselleri(CiktiPaketi p, string ada) => p.Parseller
        .Where(s => s.Rol == ParselRolu.Imar && string.Equals(AdaNo(s.TescilNo), ada, StringComparison.Ordinal) && s.TescilNo.Contains('/', StringComparison.Ordinal))
        .OrderBy(s => s.TescilNo, ParselNoKarsilastirici.Instance)
        .ToArray();

    /// <summary>
    /// Cepheli alan hesabı sayfaları: sayfa → şerit → satır. Her kadastro dışı parselin her köşesi bir satırdır (köşeden
    /// sonrakine kenar uzunluğu); parsel bloğu şerit/sayfa sınırında bölünebilir. Boş paket için tek boş sayfa döner.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<IReadOnlyList<CepheliSatir>>> CepheliSayfalari(
        CiktiPaketi p, int seritSatiri = CepheliSeritSatiri, int sayfaSeridi = CepheliSayfaSeridi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seritSatiri, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sayfaSeridi, 1);
        var satirlar = new List<CepheliSatir>();
        foreach (var s in HesapParselleri(p))
        {
            for (var i = 0; i < s.Kenarlar.Count; i++)
            {
                var k = s.Kenarlar[i];
                satirlar.Add(new CepheliSatir(i == 0 ? Kimlik(s) : string.Empty, i == 0 ? F2(s.DengelenmisAlanM2) : string.Empty, k.NoktaNo, k.Y, k.X, k.CepheM));
            }
        }

        var seritler = satirlar.Chunk(seritSatiri).Select(c => (IReadOnlyList<CepheliSatir>)c).ToList();
        if (seritler.Count == 0)
        {
            seritler.Add([]);
        }

        return seritler.Chunk(sayfaSeridi).Select(c => (IReadOnlyList<IReadOnlyList<CepheliSatir>>)c).ToArray();
    }

    /// <summary>
    /// Haritanın kâğıt alanına (mm) sığdığı en küçük standart ölçek paydası. Hiçbiri sığmazsa en büyük standart payda.
    /// </summary>
    public static int OlcekSec(decimal genislikM, decimal yukseklikM, decimal alanGenislikMm, decimal alanYukseklikMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alanGenislikMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alanYukseklikMm);
        foreach (var o in StandartOlcekler)
        {
            var kagitM = o / 1000m;
            if (genislikM <= alanGenislikMm * kagitM && yukseklikM <= alanYukseklikMm * kagitM)
            {
                return o;
            }
        }

        return StandartOlcekler[^1];
    }

    /// <summary>Koordinat ızgara aralığı (m): ölçekte kâğıtta yaklaşık 3 cm'den küçük olmayan "yuvarlak" aralık.</summary>
    public static int IzgaraAraligiM(int olcek)
    {
        foreach (var a in new[] { 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 5000 })
        {
            if (a * 1000m / olcek >= 30m)
            {
                return a;
            }
        }

        return 10000;
    }

    /// <summary>Malik adı (rehberde varsa ad soyad, yoksa anonim kod) ve baba adı.</summary>
    public static (string Ad, string Baba) Malik(CiktiPaketi p, string malikKodu)
    {
        var r = p.Rehber.Bul(malikKodu);
        return r is null ? (malikKodu, string.Empty) : (r.AdSoyad, r.BabaAdi ?? string.Empty);
    }

    /// <summary>Kadastro parsel kimliğinden ada ve parsel (rehberdeki tapu kaydı varsa öncelikli).</summary>
    public static (string Ada, string Parsel, string Cilt, string Sayfa) Kadastro(CiktiPaketi p, string kadastroParselId)
    {
        var id = kadastroParselId.Trim();
        var t = p.Rehber.Parsel(id);
        var ada = t?.Ada is { Length: > 0 } a ? a : id.Contains('/', StringComparison.Ordinal) ? AdaNo(id) : "-";
        var parsel = t?.Parsel is { Length: > 0 } pr ? pr : ParselNo(id);
        return (ada, parsel, t?.Cilt ?? string.Empty, t?.Sayfa ?? string.Empty);
    }

    /// <summary>Koordine özet için datum başlığı ("ITRF-96" / "ED-50").</summary>
    public static string DatumBasligi(CiktiPaketi p) => p.CrsAciklamasi.StartsWith("ED50", StringComparison.OrdinalIgnoreCase) ? "ED-50" : "ITRF-96";

    /// <summary>Verilen parsellerin köşe noktaları (nokta no sırasıyla, tekil) ve deprem öncesi karşılıkları.</summary>
    public static IReadOnlyList<KoordinatSatiri> Noktalar(CiktiPaketi p, IEnumerable<ParselCetvelSatiri> parseller)
    {
        var nolar = parseller.SelectMany(s => s.NoktaNolari).ToHashSet();
        return p.Koordinatlar.Where(k => nolar.Contains(k.NoktaNo)).OrderBy(k => k.NoktaNo).ToArray();
    }

    public static IReadOnlyList<KoordinatSatiri> OncesiNoktalar(CiktiPaketi p, IReadOnlyList<KoordinatSatiri> sonrasi)
    {
        if (p.Donusum is null)
        {
            return [];
        }

        var nolar = sonrasi.Select(k => k.NoktaNo).ToHashSet();
        return p.Donusum.DepremOncesi.Where(k => nolar.Contains(k.NoktaNo)).OrderBy(k => k.NoktaNo).ToArray();
    }
}
