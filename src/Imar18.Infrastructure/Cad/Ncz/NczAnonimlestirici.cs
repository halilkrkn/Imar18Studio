using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Infrastructure.Cad.Ncz;

/// <summary>
/// Anonimleştirme ayarları (P6.0, D15, spec 25.7.1). Ofsetler (m) sabit yerel kaydırmadır ve 1 mm katı olmalıdır (ızgara üstü
/// köşe ızgara üstünde kalır); gerçek konumu gizlemek için her eksende en az <see cref="NczAnonimlestirici.EnKucukOfsetM"/>.
/// Ofset değeri gizlidir: kaynak koda, fixture'a veya commit mesajına yazılmaz (yalnız yerel komut satırı girdisi).
/// <paramref name="EtiketEslemesi"/> gerçek etiket → sentetik etiket eşlemesidir (yerel, gitignore'lu dosyadan); eşlenmeyen
/// her boş olmayan etiket görünme sırasıyla <c>{YerTutucuOnek}001</c>, <c>002</c>… olur — gerçek etiket hiçbir koşulda korunmaz.
/// <paramref name="KatmanEslemesi"/>: standart Netcad katman adı listesinde (<see cref="NczAnonimlestirici.StandartKatmanAdlari"/>)
/// olmayan katman adları için yerel eşleme; eşlenmeyenler <c>L001</c>… olur. <paramref name="YazilmisAlaniKoru"/> false ise
/// (varsayılan) poligona yazılmış alan özniteliği (gerçek tapu alanı — yarı tanımlayıcı) kaldırılır.
/// </summary>
public sealed record NczAnonimlestirmeAyarlari(
    decimal OfsetKuzeyM,
    decimal OfsetDoguM,
    IReadOnlyDictionary<string, string>? EtiketEslemesi = null,
    string YerTutucuOnek = "E",
    IReadOnlyDictionary<string, string>? KatmanEslemesi = null,
    bool YazilmisAlaniKoru = false);

/// <summary>Anonimleştirme sonucu: yeni NCZ baytları ve koordinatsız/etiketsiz sayaçlar (rapora yalnız sayılar yazılır).</summary>
public sealed record NczAnonimlestirmeSonucu(
    byte[] Icerik,
    int PoligonSayisi,
    int AtlananKayitSayisi,
    int EslenenEtiketSayisi,
    int YerTutucuEtiketSayisi,
    int BosaltilanXmlAlanSayisi,
    bool PngOnizlemeSilindi);

/// <summary>
/// P6.0 (D15, FR-F6.12) — <c>.NCZ</c>'den anonim koordinat fixture'ı üretir. Kaynak baytlar yerinde yamalanmaz; yalnızca izin
/// listesindeki yapılar baştan yazılır (beyaz liste — bilinmeyen alan sızamaz):
/// <list type="number">
/// <item>Başlık: yalnız yapısal tamsayılar (sürüm imzası, uzunluk, katman tablosu ve varlık akışı ofsetleri). Başlık bölgesindeki
/// Netcad sürüm metni ve <b>kaydeden kullanıcı hesabı adı</b> kopyalanmaz (P4.0 F8).</item>
/// <item>Gömülü PNG önizleme (<c>IHDR..IEND</c>) ve çizgi tipi kütüphanesi kopyalanmaz (silinir).</item>
/// <item><c>&lt;NetmapManager&gt;</c> XML: kaynaktaki tüm alan adları korunur, <b>tüm değerler boşaltılır</b> (kişi, kurum, adres,
/// kayıt no, il/ilçe/mahalle/pafta dahil).</item>
/// <item>Katman tablosu: yalnız standart Netcad katman adları korunur (serbest metin katman adı ada/parsel no veya kişi adı
/// taşıyabilir); diğerleri eşleme veya <c>L001</c>… olur; 8 baytlık öznitelik sıfırlanır.</item>
/// <item>Varlıklar: yalnız tip 7 (poligon/çoklu çizgi); nokta, çizgi, yazı (malik adı/numara taşıyabilir) ve görünüm kayıtları
/// atlanır. Poligon yükünde yalnız tip, katman indeksi, izin listesindeki sınıf (<see cref="IzinliSiniflar"/>; diğerleri boş),
/// eşlenmiş etiket ve (isteğe bağlı) yazılmış alan yazılır; bayraklar yalnız alan bitine indirgenir, diğer baytlar (sınır kutusu
/// dahil) sıfırlanır. Köşeler kayıpsız decimal'e (6 ondalık) çözülüp sabit ofsetle kaydırılır, kot 0 yazılır.</item>
/// </list>
/// Çıktı <see cref="NczParser"/> ile yeniden ayrıştırılıp köşe sayısı ve ofset doğrulanır; ayrıca kaynaktaki her metin parçası
/// (etiket, katman/sınıf adı, XML değeri, başlık bölgesi metinleri; ≥ 3 karakter) çıktıda aranır — bulunursa sonuç döndürülmez
/// (artık metin taraması, reviewer H2).
/// Kaydırma Gauss alanını ve iç açıları değiştirmez (decimal tam aritmetik).
/// </summary>
public static class NczAnonimlestirici
{
    /// <summary>Uygulama güvenlik sınırı (mevzuat değeri değil): her eksende en küçük ofset büyüklüğü (m).</summary>
    public const decimal EnKucukOfsetM = 100_000m;

    /// <summary>Korunan standart Netcad katman adları (spec 25.7; rol eşlemesi için gereklidir, kişisel veri içermez).</summary>
    public static readonly IReadOnlySet<string> StandartKatmanAdlari = new HashSet<string>(StringComparer.Ordinal)
    {
        "0", "TK_PARSEL_GİREN", "TK_PARSEL_OLUŞAN", "TK_PARSEL_YOLATERK", "DÜZENLEME_SINIRI", "ADA_KENARLT", "PLAN_ADAKENARI",
        "PL_PARK", "KADASTRO_HAT", "NOKTA_PARSEL_YENI", "TESCİLLİ_NOKTA",
    };

    /// <summary>Korunan genişletilmiş kayıt sınıf adları; diğer (serbest metin) sınıflar boşaltılır.</summary>
    public static readonly IReadOnlySet<string> IzinliSiniflar = new HashSet<string>(StringComparer.Ordinal)
    {
        "PARSEL", "ADA", "YOL", "PARK", "BINA", "BİNA",
    };

    private const int EnKisaTaramaUzunlugu = 3;

    /// <summary>Uygulama güvenlik sınırı: en büyük ofset büyüklüğü (m); decimal/double kesinlik zarfı korunur.</summary>
    public const decimal EnBuyukOfsetM = 5_000_000m;

    private const byte SectionMarker = 0x17;
    private const byte BosBolumIsaretcisi = 0x0D; // P7.3-C3 spike: h[10]'da [0x0D][int32 0] (7/7 dosya)
    internal const int HeaderConst9 = 562; // P4.0: 6 gercek dosyada sabit (yapisal); P7.3-C3: PNG uzunluk isaretcisinin ofseti

    /// <summary>PNG dosya imzası (89 50 4E 47 0D 0A 1A 0A).</summary>
    private static ReadOnlySpan<byte> PngImzasi => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly TimeSpan RegexSuresi = TimeSpan.FromSeconds(1);
    private static readonly Regex XmlEtiketi = new("<([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant, RegexSuresi);
    private static readonly Regex XmlYorumVeCdata = new(@"<!--.*?-->|<!\[CDATA\[.*?\]\]>|<\?.*?\?>", RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexSuresi);
    private static readonly Regex XmlOznitelikDegeri = new("=\\s*(\"[^\"]*\"|'[^']*')", RegexOptions.CultureInvariant, RegexSuresi);
    private static readonly Regex XmlMetin = new(">[^<]*<", RegexOptions.CultureInvariant, RegexSuresi);
    private static readonly Regex XmlBeyazListe = new(
        "^(<(/?[A-Za-z_][A-Za-z0-9_]*)(\\s+[A-Za-z_][A-Za-z0-9_:]*=\"\")*\\s*/?>)+$", RegexOptions.CultureInvariant, RegexSuresi);

    public static Result<NczAnonimlestirmeSonucu> Anonimlestir(
        ReadOnlySpan<byte> kaynak, NczAnonimlestirmeAyarlari ayarlar, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ayarlar);
        AyarlariDogrula(ayarlar);
        cancellationToken.ThrowIfCancellationRequested();

        // Kaynak once tam ayristirilir: bozuk dosyadan fixture uretilmez.
        var dogrulama = NczParser.Parse(kaynak, cancellationToken);
        if (dogrulama.IsFailure)
        {
            return Result<NczAnonimlestirmeSonucu>.Failure(dogrulama.Error);
        }

        Span<int> h = stackalloc int[NczParser.HeaderIntCount];
        for (var i = 0; i < NczParser.HeaderIntCount; i++)
        {
            h[i] = BinaryPrimitives.ReadInt32LittleEndian(kaynak[(1 + (4 * i))..]);
        }

        var kaynakKatmanlari = KatmanKayitlari(kaynak, h[NczParser.HeaderLayerTable]);
        var katmanYerTutucu = 0;
        var katmanAdlari = kaynakKatmanlari.Select(k =>
        {
            if (StandartKatmanAdlari.Contains(k))
            {
                return k;
            }

            if (ayarlar.KatmanEslemesi is not null && ayarlar.KatmanEslemesi.TryGetValue(k, out var e))
            {
                return e;
            }

            katmanYerTutucu++;
            return "L" + katmanYerTutucu.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
        }).ToList();
        var xml = BosaltilmisXml(kaynak, out var xmlAlanSayisi, out var xmlDegerleri);
        var png = kaynak.IndexOf(PngImzasi) >= 0;

        var etiketler = new Dictionary<string, string>(StringComparer.Ordinal);
        var eslenen = 0;
        var yerTutucu = 0;
        var poligonlar = new List<(bool Genisletilmis, byte[] Yuk)>();
        var kaynakSiniflari = new List<string>();
        var atlanan = 0;
        var position = h[NczParser.HeaderEntityStream];
        while (position < kaynak.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var flag = kaynak[position];
            var size = BinaryPrimitives.ReadInt32LittleEndian(kaynak[(position + 1)..]);
            var payload = kaynak.Slice(position + NczParser.RecordHeaderSize, size);
            position += NczParser.RecordHeaderSize + size;
            if (size == 0)
            {
                continue;
            }

            if (flag is not (NczParser.EntityRecord or NczParser.ExtendedEntityRecord) || payload[0] != NczParser.PolylineType)
            {
                atlanan++;
                continue;
            }

            var genis = flag == NczParser.ExtendedEntityRecord;
            poligonlar.Add((genis, Poligon(payload, genis, ayarlar, Etiket, kaynakSiniflari)));
        }

        var icerik = Yaz(katmanAdlari, xml, poligonlar);
        var kontrol = Kontrol(dogrulama.Value, icerik, ayarlar, katmanAdlari, cancellationToken)
            ?? ArtikMetin(kaynak, h[NczParser.HeaderLayerTable], dogrulama.Value, kaynakKatmanlari, xmlDegerleri, icerik, katmanAdlari, etiketler.Values);
        if (kontrol is not null)
        {
            return Result<NczAnonimlestirmeSonucu>.Failure(NczParser.Fail(kontrol).Error);
        }

        return Result<NczAnonimlestirmeSonucu>.Success(
            new NczAnonimlestirmeSonucu(icerik, poligonlar.Count, atlanan, eslenen, yerTutucu, xmlAlanSayisi, png));

        string Etiket(string gercek)
        {
            if (gercek.Length == 0)
            {
                return gercek;
            }

            if (etiketler.TryGetValue(gercek, out var hazir))
            {
                return hazir;
            }

            string yeni;
            if (ayarlar.EtiketEslemesi is not null && ayarlar.EtiketEslemesi.TryGetValue(gercek, out var eslenmis))
            {
                yeni = eslenmis;
                eslenen++;
            }
            else
            {
                yerTutucu++;
                yeni = ayarlar.YerTutucuOnek + yerTutucu.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            }

            etiketler[gercek] = yeni;
            return yeni;
        }
    }

    private static void AyarlariDogrula(NczAnonimlestirmeAyarlari a)
    {
        foreach (var (ad, o) in new[] { ("OfsetKuzeyM", a.OfsetKuzeyM), ("OfsetDoguM", a.OfsetDoguM) })
        {
            if (decimal.Round(o, 3) != o)
            {
                throw new ArgumentException("Ofset 1 mm (0.001 m) katı olmalıdır.", ad);
            }

            if (Math.Abs(o) < EnKucukOfsetM || Math.Abs(o) > EnBuyukOfsetM)
            {
                throw new ArgumentException($"Ofset büyüklüğü {EnKucukOfsetM} m ile {EnBuyukOfsetM} m arasında olmalıdır.", ad);
            }
        }

        if (string.IsNullOrWhiteSpace(a.YerTutucuOnek) || !a.YerTutucuOnek.All(char.IsAsciiLetterOrDigit) || a.YerTutucuOnek.Length > 8)
        {
            throw new ArgumentException("Yer tutucu öneki 1–8 ASCII harf/rakam olmalıdır.", nameof(a));
        }

        foreach (var hedef in a.EtiketEslemesi?.Values ?? [])
        {
            if (hedef is null || NczParser.Turkish.GetByteCount(hedef) > NczParser.LabelFieldSize - 1)
            {
                throw new ArgumentException("Sentetik etiket boş olamaz ve en fazla 14 bayt olmalıdır.", nameof(a));
            }
        }

        foreach (var hedef in a.KatmanEslemesi?.Values ?? [])
        {
            if (string.IsNullOrWhiteSpace(hedef) || NczParser.Turkish.GetByteCount(hedef) > NczParser.LayerNameCapacity)
            {
                throw new ArgumentException("Sentetik katman adı boş olamaz ve en fazla 20 bayt olmalıdır.", nameof(a));
            }
        }
    }

    private static List<string> KatmanKayitlari(ReadOnlySpan<byte> d, int tablo)
    {
        int sayi = BinaryPrimitives.ReadInt16LittleEndian(d[(tablo + NczParser.LayerCountOffset)..]);
        var ilk = tablo + NczParser.LayerTableHeaderSize;
        var liste = new List<string>(sayi);
        for (var i = 0; i < sayi; i++)
        {
            var kayit = d.Slice(ilk + (NczParser.LayerRecordSize * i), NczParser.LayerRecordSize);
            liste.Add(NczParser.ReadPascal(kayit[..(1 + NczParser.LayerNameCapacity)])!);
        }

        return liste;
    }

    /// <summary>
    /// Kaynak <c>&lt;NetmapManager&gt;</c> bloğunu yapısı (iç içe alanlar dahil) korunarak, tüm değerleri boşaltılmış hâlde
    /// döndürür: eleman metinleri, öznitelik değerleri, yorum/CDATA/işlem talimatları silinir. Sonuç yalnızca
    /// <c>&lt;ad&gt;</c>, <c>&lt;/ad&gt;</c>, <c>&lt;ad/&gt;</c> ve <c>ad=""</c> belirteçlerinden oluşmalıdır (beyaz liste); aksi hâlde
    /// alan adlarından düz boş liste üretilir. Kaynakta blok yoksa boş blok yazılır.
    /// </summary>
    private static byte[] BosaltilmisXml(ReadOnlySpan<byte> d, out int alanSayisi, out List<string> degerler)
    {
        const string Kapanis = "</NetmapManager>";
        const string BosBlok = "<NetmapManager>" + Kapanis;
        degerler = [];
        alanSayisi = 0;
        var bas = d.IndexOf("<NetmapManager"u8);
        var son = bas < 0 ? -1 : d[bas..].IndexOf("</NetmapManager>"u8);
        if (bas < 0 || son <= 0)
        {
            return Encoding.ASCII.GetBytes(BosBlok);
        }

        var metin = Encoding.Latin1.GetString(d.Slice(bas, son + Kapanis.Length));
        List<string> adlar;
        string bos;
        try
        {
            // Artık metin taraması için kaynak değerler (eleman metni, öznitelik değeri, yorum/CDATA içeriği) toplanır.
            degerler.AddRange(XmlMetin.Matches(metin).Select(m => m.Value.Trim('<', '>').Trim()));
            degerler.AddRange(XmlOznitelikDegeri.Matches(metin).Select(m => m.Groups[1].Value.Trim('"', '\'').Trim()));
            degerler.AddRange(XmlYorumVeCdata.Matches(metin).Select(m => m.Value));

            // Alan adları yorum/CDATA silindikten SONRA toplanır (yorum içindeki "<Ad>" yedek yola sızmaz — reviewer L3).
            bos = XmlYorumVeCdata.Replace(metin, string.Empty);
            adlar = XmlEtiketi.Matches(bos).Select(m => m.Groups[1].Value)
                .Where(a => a != "NetmapManager").Distinct(StringComparer.Ordinal).ToList();
            bos = XmlOznitelikDegeri.Replace(bos, "=\"\"");
            bos = XmlMetin.Replace(bos, "><");
            if (XmlBeyazListe.IsMatch(bos))
            {
                alanSayisi = adlar.Count;
                return Encoding.ASCII.GetBytes(bos);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return Encoding.ASCII.GetBytes(BosBlok);
        }

        alanSayisi = adlar.Count;

        var sb = new StringBuilder("<NetmapManager>");
        foreach (var ad in adlar)
        {
            sb.Append('<').Append(ad).Append("></").Append(ad).Append('>');
        }

        return Encoding.ASCII.GetBytes(sb.Append(Kapanis).ToString());
    }

    private static byte[] Poligon(
        ReadOnlySpan<byte> p, bool genisletilmis, NczAnonimlestirmeAyarlari a, Func<string, string> etiket, List<string> kaynakSiniflari)
    {
        var yeni = new List<byte>(p.Length);
        var sabit = new byte[NczParser.PolylineStringsOffset];
        int katman = BinaryPrimitives.ReadInt16LittleEndian(p[NczParser.PolylineLayerOffset..]);
        sabit[0] = NczParser.PolylineType;
        sabit[1] = NczParser.PolylineType;
        sabit[2] = (byte)katman;
        BinaryPrimitives.WriteInt16LittleEndian(sabit.AsSpan(NczParser.PolylineLayerOffset), (short)katman);
        yeni.AddRange(sabit);

        var o = NczParser.PolylineStringsOffset;
        if (genisletilmis)
        {
            var sinif = NczParser.ReadPascal(p.Slice(o, NczParser.ExtendedClassFieldSize))!;
            kaynakSiniflari.Add(sinif);
            yeni.AddRange(Pascal(IzinliSiniflar.Contains(sinif.Trim()) ? sinif.Trim() : string.Empty, NczParser.ExtendedClassFieldSize));
            o += NczParser.ExtendedClassFieldSize;
        }

        yeni.AddRange(Pascal(etiket(NczParser.ReadPascal(p.Slice(o, NczParser.LabelFieldSize))!), NczParser.LabelFieldSize));
        o += NczParser.LabelFieldSize;
        var bayrak = BinaryPrimitives.ReadInt32LittleEndian(p[o..]);
        var alanBayt = new byte[NczParser.PolylineFlagsSize + NczParser.DeclaredAreaSize];
        if (a.YazilmisAlaniKoru && (bayrak & NczParser.DeclaredAreaFlag) != 0)
        {
            BinaryPrimitives.WriteInt32LittleEndian(alanBayt, NczParser.DeclaredAreaFlag);
            p.Slice(o + NczParser.PolylineFlagsSize, NczParser.DeclaredAreaSize).CopyTo(alanBayt.AsSpan(NczParser.PolylineFlagsSize));
        }

        yeni.AddRange(alanBayt);
        o += NczParser.PolylineFlagsSize + NczParser.DeclaredAreaSize;

        var kose = new byte[NczParser.VertexSize];
        for (; o < p.Length; o += NczParser.VertexSize)
        {
            var v = p.Slice(o, NczParser.VertexSize);
            Ieee754Decimal.TryDecode(BinaryPrimitives.ReadUInt64LittleEndian(v), NczParser.CoordinateScale, out var kuzey);
            Ieee754Decimal.TryDecode(BinaryPrimitives.ReadUInt64LittleEndian(v[8..]), NczParser.CoordinateScale, out var dogu);
            BinaryPrimitives.WriteDoubleLittleEndian(kose, (double)(kuzey + a.OfsetKuzeyM));
            BinaryPrimitives.WriteDoubleLittleEndian(kose.AsSpan(8), (double)(dogu + a.OfsetDoguM));
            BinaryPrimitives.WriteDoubleLittleEndian(kose.AsSpan(16), 0d);
            yeni.AddRange(kose);
        }

        return [.. yeni];
    }

    private static byte[] Pascal(string metin, int genislik)
    {
        var alan = new byte[genislik];
        var b = NczParser.Turkish.GetBytes(metin);
        var n = Math.Min(b.Length, genislik - 1);
        alan[0] = (byte)n;
        b.AsSpan(0, n).CopyTo(alan.AsSpan(1));
        return alan;
    }

    private static byte[] Yaz(List<string> katmanlar, byte[] xml, List<(bool Genisletilmis, byte[] Yuk)> poligonlar) =>
        Iskelet(katmanlar, xml, poligonlar.Select(p => (p.Genisletilmis ? NczParser.ExtendedEntityRecord : NczParser.EntityRecord, p.Yuk)));

    /// <summary>
    /// Beyaz liste NCZ iskeleti: başlık + (isteğe bağlı) başlık bölgesi + katman tablosu + XML + <c>[0x17]</c> ile başlayan kayıt
    /// akışı. P6.0 anonimleştiricisi varsayılanlarla çağırır (çıktısı değişmez); P7.3-C3 parselasyon yazıcısı spike düzenindeki
    /// başlık bölgesini (<paramref name="baslikBolgesi"/>, <paramref name="h4"/>), katman özniteliğini ve <c>h[10]</c>'daki
    /// <c>[0x0D][0]</c> boş bölüm işaretçisini (<paramref name="bolumIsaretcisi"/>) ekler.
    /// </summary>
    internal static byte[] Iskelet(
        IReadOnlyList<string> katmanlar,
        byte[] xml,
        IEnumerable<(byte Bayrak, byte[] Yuk)> kayitlar,
        byte[]? baslikBolgesi = null,
        int h4 = 0,
        byte[]? katmanOzniteligi = null,
        bool bolumIsaretcisi = false)
    {
        var d = new List<byte> { NczParser.Signature };
        d.AddRange(new byte[4 * NczParser.HeaderIntCount]);
        d.AddRange(baslikBolgesi ?? []);

        var katmanOfseti = d.Count;
        var tabloBasligi = new byte[NczParser.LayerTableHeaderSize];
        BinaryPrimitives.WriteInt16LittleEndian(tabloBasligi.AsSpan(NczParser.LayerCountOffset), (short)katmanlar.Count);
        d.AddRange(tabloBasligi);
        var oznitelik = katmanOzniteligi ?? new byte[NczParser.LayerRecordSize - 1 - NczParser.LayerNameCapacity];
        foreach (var ad in katmanlar)
        {
            d.AddRange(Pascal(ad, 1 + NczParser.LayerNameCapacity));
            d.AddRange(oznitelik); // anonimleştiricide sıfırlanır
        }

        var katmanSonu = d.Count - NczParser.LayerTableEndBias;
        d.AddRange(xml);
        if (bolumIsaretcisi)
        {
            d.Add(BosBolumIsaretcisi);
            d.AddRange(new byte[4]);
        }

        var varlikOfseti = d.Count;
        d.Add(SectionMarker);
        d.AddRange(new byte[4]);
        var boyut = new byte[4];
        foreach (var (bayrak, p) in kayitlar)
        {
            d.Add(bayrak);
            BinaryPrimitives.WriteInt32LittleEndian(boyut, p.Length);
            d.AddRange(boyut);
            d.AddRange(p);
        }

        var bayt = d.ToArray();
        int[] baslik = [NczParser.HeaderConst0, bayt.Length, NczParser.HeaderConst2, katmanOfseti, h4, 0, 0, katmanSonu, 0, HeaderConst9, varlikOfseti - 5, varlikOfseti];
        for (var i = 0; i < baslik.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bayt.AsSpan(1 + (4 * i)), baslik[i]);
        }

        return bayt;
    }

    private static string? Kontrol(
        Application.Cad.CadLayerDocument kaynak, byte[] icerik, NczAnonimlestirmeAyarlari a, List<string> katmanAdlari, CancellationToken ct)
    {
        var cikti = NczParser.Parse(icerik, ct);
        if (cikti.IsFailure)
        {
            return "anonymized output does not parse";
        }

        var k = kaynak.Polylines;
        var c = cikti.Value.Polylines;
        if (k.Count != c.Count)
        {
            return "anonymized polygon count mismatch";
        }

        for (var i = 0; i < k.Count; i++)
        {
            var beklenenKatman = katmanAdlari[kaynak.Layers.First(l => l.Name == k[i].LayerName).Index];
            if (k[i].Vertices.Count != c[i].Vertices.Count || c[i].LayerName != beklenenKatman)
            {
                return "anonymized polygon structure mismatch";
            }

            for (var j = 0; j < k[i].Vertices.Count; j++)
            {
                var (s, t) = (k[i].Vertices[j], c[i].Vertices[j]);
                if (t.X != s.X + a.OfsetKuzeyM || t.Y != s.Y + a.OfsetDoguM)
                {
                    return "anonymized vertex offset is not exact";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Artık metin taraması (fail-safe): kaynaktaki her metin parçası — etiketler, standart dışı katman adları, izinsiz sınıf adları,
    /// XML değerleri ve başlık bölgesindeki (kullanıcı adı, sürüm, çizgi tipi) metin dizileri — çıktı baytlarında (Windows-1254 ve
    /// UTF-8) aranır. Yalnızca araç tarafından bilinçli yazılan bir metnin (korunan katman/sınıf, sentetik etiket, XML alan adı)
    /// içinde geçen eşleşmeler zararsız sayılır; başka her eşleşme sonucu iptal eder.
    /// </summary>
    private static string? ArtikMetin(
        ReadOnlySpan<byte> kaynak, int katmanTablosu, Application.Cad.CadLayerDocument belge, List<string> kaynakKatmanlari,
        List<string> xmlDegerleri, byte[] cikti, List<string> katmanAdlari, IEnumerable<string> sentetikEtiketler)
    {
        var hassas = new List<string>();
        hassas.AddRange(belge.Polylines.Select(p => p.Label));
        hassas.AddRange(belge.Texts.Select(t => t.Text)); // SAHA-015: tip 5 yazılar (malik adı olabilir) artık çözülüyor; kayıtları atlanır
        hassas.AddRange(belge.Polylines.Select(p => p.FeatureClass ?? string.Empty).Where(s => !IzinliSiniflar.Contains(s.Trim())));
        hassas.AddRange(kaynakKatmanlari.Where(k => !StandartKatmanAdlari.Contains(k)));
        hassas.AddRange(xmlDegerleri);
        var baslik = Encoding.Latin1.GetString(kaynak[NczParser.HeaderSize..Math.Max(NczParser.HeaderSize, katmanTablosu)]);
        hassas.AddRange(Regex.Matches(baslik, "[\x20-\x7E\x80-\xFF]{4,}", RegexOptions.CultureInvariant, RegexSuresi).Select(m => m.Value));

        // Kimlik eşlemesi (gerçek ad → aynı ad) "bilinçli yazım" sayılmaz: kaynak etiket/katman adına eşit yazılanlar listeden çıkar.
        var kaynakAdlari = belge.Polylines.Select(p => p.Label).Concat(kaynakKatmanlari.Where(k => !StandartKatmanAdlari.Contains(k)))
            .ToHashSet(StringComparer.Ordinal);
        var yazilan = katmanAdlari.Concat(IzinliSiniflar).Concat(sentetikEtiketler).Where(y => !kaynakAdlari.Contains(y)).Append("NetmapManager")
            .Concat(XmlEtiketi.Matches(Encoding.ASCII.GetString(cikti)).Select(m => m.Groups[1].Value)).ToList();
        foreach (var s in hassas.Select(s => s.Trim()).Where(s => s.Length >= EnKisaTaramaUzunlugu).Distinct(StringComparer.Ordinal))
        {
            if (yazilan.Any(y => y.Contains(s, StringComparison.Ordinal)))
            {
                continue;
            }

            if (cikti.AsSpan().IndexOf(NczParser.Turkish.GetBytes(s)) >= 0 || cikti.AsSpan().IndexOf(Encoding.UTF8.GetBytes(s)) >= 0)
            {
                return "residual source text found in anonymized output";
            }
        }

        return null;
    }
}

/// <summary>
/// P6.0 fixture çıktı yolu kapısı (AC-PILOT-08): anonim çıktı yalnızca bir <c>test-data/synthetic/</c> klasörü altına,
/// <c>.ncz</c> uzantısıyla yazılır; <c>test-data/real/</c> veya <c>test-data/raw/</c> altına ve girdinin üzerine yazılamaz.
/// </summary>
public static class NczFixtureYolKapisi
{
    /// <summary>Geçerliyse null, değilse Türkçe neden döner.</summary>
    public static string? Dogrula(string girdiYolu, string ciktiYolu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(girdiYolu);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciktiYolu);
        var girdi = Normal(girdiYolu);
        var cikti = Normal(ciktiYolu);
        if (string.Equals(girdi, cikti, StringComparison.OrdinalIgnoreCase))
        {
            return "Çıktı girdinin üzerine yazılamaz.";
        }

        if (cikti.Contains("/test-data/real/", StringComparison.OrdinalIgnoreCase) || cikti.Contains("/test-data/raw/", StringComparison.OrdinalIgnoreCase))
        {
            return "Çıktı test-data/real veya test-data/raw altına yazılamaz.";
        }

        if (!cikti.Contains("/test-data/synthetic/", StringComparison.OrdinalIgnoreCase))
        {
            return "Çıktı yalnızca test-data/synthetic/ altına yazılabilir.";
        }

        return string.Equals(Path.GetExtension(cikti), ".ncz", StringComparison.OrdinalIgnoreCase) ? null : "Çıktı uzantısı .ncz olmalıdır.";
    }

    private static string Normal(string yol) => Path.GetFullPath(yol).Replace('\\', '/');
}

/// <summary>
/// P6.0 araç akışı: yol kapısı → girdi okuma → bellekte anonimleştirme → atomik yazma (yedeksiz). Var olan çıktı yalnızca
/// <paramref name="uzerineYaz"/> ile değiştirilir. Hata mesajları dosya içeriği veya koordinat taşımaz.
/// </summary>
public static class NczFixtureUretici
{
    public static async Task<Result<NczAnonimlestirmeSonucu>> UretAsync(
        string girdiYolu, string ciktiYolu, NczAnonimlestirmeAyarlari ayarlar, bool uzerineYaz = false, CancellationToken cancellationToken = default)
    {
        var neden = NczFixtureYolKapisi.Dogrula(girdiYolu, ciktiYolu);
        if (neden is null && (BaglantiMi(ciktiYolu) || BaglantiMi(girdiYolu)))
        {
            // Sembolik bağ / junction üzerinden girdinin ezilmesi veya kapı dışına yazma engellenir (reviewer L2).
            neden = "Girdi veya çıktı yolu sembolik bağ olamaz.";
        }

        if (neden is null && File.Exists(ciktiYolu) && !uzerineYaz)
        {
            neden = "Çıktı dosyası zaten var (üzerine yazmak için açıkça izin verin).";
        }

        if (neden is not null)
        {
            var e = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz);
            return Result<NczAnonimlestirmeSonucu>.Failure(e with
            {
                UserMessage = neden,
                TechnicalMessage = e.TechnicalMessage.Replace("{command}", "ncz-fixture", StringComparison.Ordinal).Replace("{reason}", "output path rejected", StringComparison.Ordinal),
            });
        }

        var kaynak = await File.ReadAllBytesAsync(girdiYolu, cancellationToken).ConfigureAwait(false);
        var sonuc = NczAnonimlestirici.Anonimlestir(kaynak, ayarlar, cancellationToken);
        if (sonuc.IsSuccess)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ciktiYolu))!);
            await Proje.AtomikDosyaYazici.YazAsync(ciktiYolu, sonuc.Value.Icerik, null, cancellationToken, yedekTut: false).ConfigureAwait(false);
        }

        return sonuc;
    }

    private static bool BaglantiMi(string yol)
    {
        for (var d = new FileInfo(Path.GetFullPath(yol)) as FileSystemInfo; d is not null; d = d is FileInfo f ? f.Directory : ((DirectoryInfo)d).Parent)
        {
            if (d.Exists && d.LinkTarget is not null)
            {
                return true;
            }
        }

        return false;
    }
}
