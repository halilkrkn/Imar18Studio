using System.Globalization;
using Imar18.Application.Cikti;
using Imar18.Application.Girdi;
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
    public static string Kimlik(ParselCetvelSatiri p) => ImarKodu(p.TescilNo, p.GeciciKod);

    /// <summary>
    /// SAHA-017: imar parselinin Ada/Parsel kodu — tescil no boş veya <c>-</c> ise geçici kod / parsel kimliği (kroki
    /// parselasyonunun <c>1/1</c>, <c>2/3</c> kodları; <c>1/1</c> → Ada <c>1</c>, Parsel <c>1</c>).
    /// </summary>
    public static string ImarKodu(string tescilNo, string geciciKod) =>
        tescilNo is { Length: > 0 } t && t.Trim() != "-" ? t.Trim() : geciciKod.Trim();

    /// <summary>Ada/parsel biçiminde (<c>"1/1"</c>) mi?</summary>
    public static bool AdaParselli(string kod) => kod.Contains('/', StringComparison.Ordinal);

    /// <summary>
    /// SAHA-017 (mühendis teyidi): TK_Oluşan (yeni) parsel köşe noktalarına basılan MK — dönüşüm varsa projenin MK'si, yoksa
    /// <see cref="DonusumGirdisi.VarsayilanMk"/> (0.09). Kadastro (TK_PARSEL) köşe noktalarında MK sütunu boştur.
    /// </summary>
    public static string Mk(CiktiPaketi p) => (p.Donusum?.Girdi.Mk ?? DonusumGirdisi.VarsayilanMk).ToString("0.00", Inv);

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
        .Where(s => s.Rol == ParselRolu.Imar && AdaParselli(Kimlik(s)))
        .Select(s => AdaNo(Kimlik(s)))
        .Concat(p.ImarSirali.Select(s => ImarKodu(s.TescilNo, s.GeciciKod)).Where(AdaParselli).Select(AdaNo))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(a => a, ParselNoKarsilastirici.Instance)
        .ToArray();

    /// <summary>Verilen imar adasının imar parselleri.</summary>
    public static IReadOnlyList<ParselCetvelSatiri> AdaParselleri(CiktiPaketi p, string ada) => p.Parseller
        .Where(s => s.Rol == ParselRolu.Imar && AdaParselli(Kimlik(s)) && string.Equals(AdaNo(Kimlik(s)), ada, StringComparison.Ordinal))
        .OrderBy(Kimlik, ParselNoKarsilastirici.Instance)
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

    /// <summary>
    /// Malik adı ve baba adı: rehberde varsa rehberin ayrı alanları; yoksa SAHA-011 / SORU-055 malik etiketi ayrıştırılır
    /// (<c>AD SOYAD (BABA ADI)</c> / <c>AD SOYAD / BABA ADI</c>; <c> #n</c> eki ad sütununda kalır; eski anonim kod olduğu gibi, baba boş).
    /// </summary>
    public static (string Ad, string Baba) Malik(CiktiPaketi p, string malikKodu)
    {
        var r = p.Rehber.Bul(malikKodu);
        if (r is not null)
        {
            return (r.AdSoyad, r.BabaAdi ?? string.Empty);
        }

        // Reviewer L11: aynı adlı farklı kişiler cetvelde ayırt edilsin diye " #n" eki ad sütununda korunur.
        var (ad, baba) = MalikEtiketi.Ayristir(malikKodu);
        return (ad + MalikEtiketi.EkiAl(malikKodu), baba ?? string.Empty);
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

    /// <summary>Verilen parsellerin köşe noktaları (nokta no sırasıyla, tekil).</summary>
    public static IReadOnlyList<KoordinatSatiri> Noktalar(CiktiPaketi p, IEnumerable<ParselCetvelSatiri> parseller)
    {
        var nolar = parseller.SelectMany(s => s.NoktaNolari).ToHashSet();
        return p.Koordinatlar.Where(k => nolar.Contains(k.NoktaNo)).OrderBy(k => k.NoktaNo).ToArray();
    }

    /// <summary>
    /// SAHA-017 / SAHA-021: Koordine Özet / Rölöve nokta grupları. <c>Olusan</c> = seçili yeni (TK_Oluşan: imar, yol, kamu) parsellerin
    /// köşelerinden kadastro köşesi OLMAYANLAR; <c>Kadastro</c> = kadastro (TK_PARSEL) parsellerinin köşeleri — <paramref name="adaKapsami"/>
    /// açıksa yalnız sınır kutusu seçili parsellerinkiyle kesişen kadastro parselleri. Kadastro köşesiyle çakışan (≤ 0.02 m; paket
    /// üretiminde aynı nokta no'ya bağlanır) TK_Oluşan köşesi yalnız kadastro grubunda, arşiv no'suyla bir kez listelenir (SORU-075,
    /// SORU-078 kapandı).
    /// </summary>
    public static (IReadOnlyList<KoordinatSatiri> Olusan, IReadOnlyList<KoordinatSatiri> Kadastro) NoktaGruplari(
        CiktiPaketi p, IReadOnlyList<ParselCetvelSatiri> secili, bool adaKapsami)
    {
        var tumKadastro = p.Parseller.Where(s => s.Rol == ParselRolu.Kadastro).ToArray();
        IEnumerable<ParselCetvelSatiri> kadastro = tumKadastro;
        if (adaKapsami)
        {
            var k = secili.SelectMany(s => s.Koseler).ToArray();
            var seciliNolar = secili.SelectMany(s => s.NoktaNolari).ToHashSet();
            // Reviewer O3: sınır kutusuyla kesişen VEYA seçili parsellerle ortak nokta no taşıyan (≤ 0.02 m oturtulmuş köşe) kadastro
            // parselleri — ortak köşe hiçbir tablodan düşmez.
            kadastro = k.Length == 0
                ? []
                : kadastro.Where(s => (s.Koseler.Max(v => v.X) >= k.Min(v => v.X) && s.Koseler.Min(v => v.X) <= k.Max(v => v.X)
                    && s.Koseler.Max(v => v.Y) >= k.Min(v => v.Y) && s.Koseler.Min(v => v.Y) <= k.Max(v => v.Y))
                    || s.NoktaNolari.Any(seciliNolar.Contains));
        }

        // SORU-078: satırlar basılan ada göre (arşiv no / sıralı no; sayısal) dizilir, iç nokta sırasına göre değil.
        var kadastroNolari = tumKadastro.SelectMany(s => s.NoktaNolari).ToHashSet();
        var olusan = Noktalar(p, secili.Where(s => s.Rol != ParselRolu.Kadastro)).Where(k => !kadastroNolari.Contains(k.NoktaNo)).ToArray();
        return (AdaGoreSirala(p, olusan), AdaGoreSirala(p, Noktalar(p, kadastro)));
    }

    /// <summary>SORU-078: nokta satırlarını basılan nokta adına göre (sayısal karşılaştırma; eşitte iç nokta no) sıralar.</summary>
    private static KoordinatSatiri[] AdaGoreSirala(CiktiPaketi p, IEnumerable<KoordinatSatiri> noktalar) =>
        noktalar.OrderBy(k => p.NoktaAdi(k.NoktaNo), ParselNoKarsilastirici.Instance).ThenBy(k => k.NoktaNo).ToArray();

    /// <summary>
    /// SAHA-021: KOORDİNE ÖZET / RÖLÖVE iki tabloyla mı basılır — <c>.DNS</c> / ortak noktalı dönüşüm VEYA mühendisin girdiği arşiv onaylı
    /// kadastro noktaları varsa "DEPREM ÖNCESİ / ARŞİV ONAYLI" + "DEPREM SONRASI"; aksi halde tek "KOORDİNE ÖZET".
    /// </summary>
    public static bool IkiTabloMu(CiktiPaketi p) => p.Donusum is not null || p.ArsivNoktalariVar;

    /// <summary>SAHA-021: üst tablo başlığı (datum ile).</summary>
    public static string OncesiBasligi(CiktiPaketi p) => DatumBasligi(p) + " KOORDİNE ÖZET (DEPREM ÖNCESİ / ARŞİV ONAYLI)";

    /// <summary>SAHA-021: alt tablo başlığı (iki tabloda Deprem Sonrası, aksi halde tek tablo).</summary>
    public static string SonrasiBasligi(CiktiPaketi p) => DatumBasligi(p) + (IkiTabloMu(p) ? " KOORDİNE ÖZET (DEPREM SONRASI)" : " KOORDİNE ÖZET");

    /// <summary>
    /// SAHA-017 / SAHA-021: Deprem Öncesi / Arşiv Onaylı tablosu — yalnız kadastro köşeleri (MK yok). Arşiv noktası eşlenen köşede
    /// mühendisin arşiv onaylı Y, X'i; diğerlerinde dönüşüm öncesi koordinat (dönüşüm yoksa CAD köşesinin kendisi — deprem kayması
    /// verilmemiştir). İki tablo basılmıyorsa boş.
    /// </summary>
    public static IReadOnlyList<KoordinatSatiri> OncesiNoktalar(CiktiPaketi p, IReadOnlyList<KoordinatSatiri> kadastro)
    {
        if (!IkiTabloMu(p))
        {
            return [];
        }

        var oncesi = p.Donusum?.DepremOncesi.ToDictionary(k => k.NoktaNo);
        return kadastro
            .Select(k => p.ArsivKoordinatlari.TryGetValue(k.NoktaNo, out var a) ? a
                : oncesi is null ? k
                : oncesi.TryGetValue(k.NoktaNo, out var o) ? o : null)
            .OfType<KoordinatSatiri>()
            .ToArray(); // kadastro listesi NoktaGruplari'nda basılan ada göre sıralıdır (SORU-078)
    }

    /// <summary>
    /// SAHA-021: çizim etiketleri (RÖLÖVE / PARSELASYON HARİTASI). Yola terk parselleri tescil sırasıyla <c>Y1</c>, <c>Y2</c>…; ada/parsel
    /// kodlu imar parseli yalnız parsel no (<c>1</c>, <c>2</c>…; ada no adada bir kez ayrıca basılır); diğerleri gösterim kimliği.
    /// Anahtar geçici kod.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CizimEtiketleri(CiktiPaketi p)
    {
        var sonuc = new Dictionary<string, string>(StringComparer.Ordinal);
        var yol = 0;
        foreach (var s in HesapParselleri(p))
        {
            var kimlik = Kimlik(s);
            sonuc[s.GeciciKod] = s.Rol switch
            {
                ParselRolu.YolTerk => "Y" + (++yol).ToString(Inv),
                ParselRolu.Imar when AdaParselli(kimlik) => ParselNo(kimlik),
                _ => kimlik,
            };
        }

        return sonuc;
    }

    /// <summary>
    /// SAHA-021: imar adası → etiket noktası (ada parsellerinin alan ağırlıklı merkezi; adanın hiçbir parselinin içine düşmüyorsa
    /// en büyük parselin iç noktası). Ada no bu noktaya büyük ve kalın, tek kez yazılır.
    /// </summary>
    public static IReadOnlyList<(string Ada, GeodeticPoint Nokta)> AdaEtiketNoktalari(IEnumerable<ParselCetvelSatiri> parseller)
    {
        var sonuc = new List<(string, GeodeticPoint)>();
        foreach (var g in parseller.Where(s => s.Rol == ParselRolu.Imar && AdaParselli(Kimlik(s)) && s.Koseler.Count >= 3)
                     .GroupBy(s => AdaNo(Kimlik(s)), StringComparer.Ordinal).OrderBy(g => g.Key, ParselNoKarsilastirici.Instance))
        {
            decimal toplam = 0m, n = 0m, e = 0m;
            foreach (var s in g)
            {
                var (a, m) = AlanMerkezi(s.Koseler);
                toplam += a;
                n += a * m.X;
                e += a * m.Y;
            }

            var nokta = toplam == 0m ? IcNokta(g.First().Koseler) : new GeodeticPoint(n / toplam, e / toplam);
            if (!g.Any(s => Icinde(s.Koseler, nokta.X, nokta.Y)))
            {
                nokta = IcNokta(g.OrderByDescending(s => Math.Abs(AlanMerkezi(s.Koseler).Alan)).First().Koseler);
            }

            sonuc.Add((g.Key, nokta));
        }

        return sonuc;
    }

    /// <summary>Mutlak alan ve alan ağırlık merkezi (decimal; dejenere poligonda köşe ortalaması).</summary>
    private static (decimal Alan, GeodeticPoint Merkez) AlanMerkezi(IReadOnlyList<GeodeticPoint> v)
    {
        var (n0, e0) = (v[0].X, v[0].Y);
        decimal a2 = 0m, cn = 0m, ce = 0m;
        for (var i = 0; i < v.Count; i++)
        {
            var (n1, e1) = (v[i].X - n0, v[i].Y - e0);
            var (n2, e2) = (v[(i + 1) % v.Count].X - n0, v[(i + 1) % v.Count].Y - e0);
            var c = (e1 * n2) - (e2 * n1);
            a2 += c;
            ce += (e1 + e2) * c;
            cn += (n1 + n2) * c;
        }

        return a2 == 0m
            ? (0m, new GeodeticPoint(v.Average(k => k.X), v.Average(k => k.Y)))
            : (Math.Abs(a2 / 2m), new GeodeticPoint(n0 + (cn / (3m * a2)), e0 + (ce / (3m * a2))));
    }

    /// <summary>SAHA-017: Deprem Sonrası / tek tablo satırları — önce TK_Oluşan (MK dolu), altına kadastro (MK boş, tire de yok).</summary>
    public static IReadOnlyList<string[]> SonrasiSatirlari(CiktiPaketi p, IReadOnlyList<KoordinatSatiri> olusan, IReadOnlyList<KoordinatSatiri> kadastro)
    {
        var mk = Mk(p);
        return olusan.Select(k => new[] { p.NoktaAdi(k.NoktaNo), F2(k.Y), F2(k.X), mk })
            .Concat(kadastro.Select(k => new[] { p.NoktaAdi(k.NoktaNo), F2(k.Y), F2(k.X), string.Empty }))
            .ToArray();
    }

    /// <summary>
    /// SAHA-017: etiket iç noktası (decimal) — alan ağırlık merkezi poligonun içindeyse o; değilse (konkav / L / U biçimli yol)
    /// yatay ve düşey 9'ar tarama çizgisinde en uzun iç aralığın ortası. Etiket her zaman kendi poligonunun içine düşer.
    /// </summary>
    public static GeodeticPoint IcNokta(IReadOnlyList<GeodeticPoint> v)
    {
        if (v.Count < 3)
        {
            return v.Count == 0 ? new GeodeticPoint(0m, 0m) : v[0];
        }

        var (n0, e0) = (v[0].X, v[0].Y);
        decimal a2 = 0m, cn = 0m, ce = 0m;
        for (var i = 0; i < v.Count; i++)
        {
            var (n1, e1) = (v[i].X - n0, v[i].Y - e0);
            var (n2, e2) = (v[(i + 1) % v.Count].X - n0, v[(i + 1) % v.Count].Y - e0);
            var c = (e1 * n2) - (e2 * n1);
            a2 += c;
            ce += (e1 + e2) * c;
            cn += (n1 + n2) * c;
        }

        if (a2 != 0m)
        {
            var aday = new GeodeticPoint(n0 + (cn / (3m * a2)), e0 + (ce / (3m * a2)));
            if (Icinde(v, aday.X, aday.Y))
            {
                return aday;
            }
        }

        var (mnN, mxN, mnE, mxE) = (v.Min(k => k.X), v.Max(k => k.X), v.Min(k => k.Y), v.Max(k => k.Y));
        var en = -1m;
        var sonuc = v[0];
        for (var t = 1; t <= 9; t++)
        {
            var oran = (t / 10m) + 0.0000137m; // köşeden geçmeyen tarama çizgisi
            foreach (var yatay in new[] { true, false })
            {
                var sabit = yatay ? mnN + ((mxN - mnN) * oran) : mnE + ((mxE - mnE) * oran);
                var kesisim = new List<decimal>();
                for (var i = 0; i < v.Count; i++)
                {
                    var (a, b) = (v[i], v[(i + 1) % v.Count]);
                    var (sa, sb) = yatay ? (a.X, b.X) : (a.Y, b.Y);
                    var (ka, kb) = yatay ? (a.Y, b.Y) : (a.X, b.X);
                    if ((sa > sabit) != (sb > sabit))
                    {
                        kesisim.Add(ka + ((sabit - sa) * (kb - ka) / (sb - sa)));
                    }
                }

                kesisim.Sort();
                for (var i = 0; i + 1 < kesisim.Count; i += 2)
                {
                    if (kesisim[i + 1] - kesisim[i] > en)
                    {
                        en = kesisim[i + 1] - kesisim[i];
                        var orta = (kesisim[i] + kesisim[i + 1]) / 2m;
                        sonuc = yatay ? new GeodeticPoint(sabit, orta) : new GeodeticPoint(orta, sabit);
                    }
                }
            }
        }

        return sonuc;
    }

    /// <summary>Çift-tek kuralıyla nokta poligon içinde mi (decimal; X = Kuzey, Y = Doğu).</summary>
    public static bool Icinde(IReadOnlyList<GeodeticPoint> v, decimal n, decimal e)
    {
        var ic = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
        {
            if ((v[i].X > n) != (v[j].X > n) && e < v[j].Y + ((n - v[j].X) * (v[i].Y - v[j].Y) / (v[i].X - v[j].X)))
            {
                ic = !ic;
            }
        }

        return ic;
    }

    /// <summary>SAHA-017: Parselasyon Haritası kimlik kutusu "Ada/Parsel No" — projedeki kadastro parselleri (ör. <c>901/7, …</c>).</summary>
    public static string KadastroAdaParselleri(CiktiPaketi p)
    {
        var idler = p.KadastroParselleri.Count > 0
            ? p.KadastroParselleri.Select(k => k.ParselId)
            : p.Parseller.Where(s => s.Rol == ParselRolu.Kadastro).Select(s => Kimlik(s));
        return string.Join(", ", idler.Distinct(StringComparer.Ordinal).OrderBy(i => i, ParselNoKarsilastirici.Instance).Select(id =>
        {
            var (ada, parsel, _, _) = Kadastro(p, id);
            return ada == "-" ? parsel : ada + "/" + parsel;
        }));
    }
}
