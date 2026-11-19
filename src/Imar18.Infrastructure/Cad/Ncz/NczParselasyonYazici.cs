using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Imar18.Application.Cikti;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Infrastructure.Cad.Ncz;

/// <summary>
/// P7.3-C3 (SAHA-007; spec'te Faz 15 olan NCZ yazımı saha talebiyle öne çekildi): onaylı revizyondan Netcad 8.5
/// <c>PARSELASYON HARİTASI.NCZ</c> üretir. Kayıt düzeni: <c>docs/spikes/P7.3_NCZ_Yazici_Spike.md</c>. Netcad'de açılışı manuel
/// olarak doğrulanana kadar <b>deneysel</b>dir (SORU-042).
/// <list type="bullet">
/// <item><b>Şablon modu</b> (<see cref="CiktiPaketi.NczSablonu"/> varsa): başlık bölgesi, katman tablosu, font/sembol blokları, XML
/// ve şablon kayıtları bayt bayt korunur. Eksik <see cref="OlusanKatmani"/>, <see cref="YolTerkKatmani"/>, <see cref="NoktaKatmani"/>
/// katmanları eklenir (29 bayt/katman; <c>h[7]</c>, <c>h[8]</c>, <c>h[10]</c>, <c>h[11]</c> kayar). Bu katmanlardaki şablon kayıtları
/// ve köşe dizisi (döngüsel/ters yönden bağımsız) projedeki kadastro dışı bir parselle aynı olan şablon poligonları yeniden
/// yazıldığı için düşer; <see cref="GirenKatmani"/> ve projedeki kadastro halkalarıyla aynı poligonlar asla düşmez. Yeni kayıtlar
/// sondaki tip-14 kaydının önüne eklenir; <c>h[1]</c>, ofset 81 sayacı ve 49..80 çizim kapsamı yeniden hesaplanır.</item>
/// <item><b>İskelet modu</b> (<c>.DXF</c>/<c>.DWG</c> kaynağı veya şablonsuz proje): <see cref="NczAnonimlestirici.Iskelet"/> üzerinden
/// katmanlar <c>0, TK_PARSEL_GİREN, TK_PARSEL_OLUŞAN, TK_PARSEL_YOLATERK, NOKTA_PARSEL_YENI</c>; kadastro parselleri de yazılır.</item>
/// <item>Yeni kayıtlar: imar parseli <c>0x16</c> sınıf <c>PARSEL</c>, yol/kamu/kadastro <c>0x15</c>; bayrak <c>0x13</c> (kapalı + alan),
/// yazılmış alan = dengelenmiş (tescil) alanı; etiket = tescil no (yoksa geçici kod); tip-5 ada/parsel yazısı parselin içine
/// (Decimal ağırlık merkezi, konkavda iç nokta); tip-1 köşe noktaları adı = Koordine Özet nokta no; <c>0</c> katmanına
/// <see cref="PilotDamgasi.Metin"/> ve RevisionId yazısı (içerik damgası; SORU-050).</item>
/// <item>Etiket 14 baytı aşar veya Windows-1254'te gösterilemezse sessiz kırpma yerine <c>ERR-CIKTI-003</c> (architect C4).</item>
/// <item>Çıktı <see cref="NczParser"/> ile yeniden okunur; yazılan her poligon katman/etiket/köşe ile bulunamazsa sonuç döndürülmez.</item>
/// </list>
/// Aynı paket aynı baytları üretir (zaman damgası yok).
/// </summary>
public static class NczParselasyonYazici
{
    /// <summary>Fen Klasörü .zip içindeki giriş adı (mühendisin örnek paketiyle aynı).</summary>
    public const string DosyaAdi = "PARSELASYON HARİTASI.NCZ";

    public const string GirenKatmani = "TK_PARSEL_GİREN";
    public const string OlusanKatmani = "TK_PARSEL_OLUŞAN";
    public const string YolTerkKatmani = "TK_PARSEL_YOLATERK";
    public const string NoktaKatmani = "NOKTA_PARSEL_YENI";

    /// <summary>Spike §2.5: imar parseli genişletilmiş kayıt sınıfı.</summary>
    public const string ImarSinifi = "PARSEL";

    /// <summary>Spike §1: katman tablosundan sonraki bölgeyi gösteren başlık alanları (h[8], h[10], h[11]); h[4]/h[9] tablodan öncedir.</summary>
    private static readonly int[] KayanBasliklar = [8, NczParser.HeaderEntityStream - 1, NczParser.HeaderEntityStream];

    private const string SifirKatmani = "0";
    private const int KapsamOfseti = 49;
    private const int SayacOfseti = 81;
    private const int BaslikBolgesiEnAz = SayacOfseti + 4;
    private const int PngIsaretcisiOfseti = NczAnonimlestirici.HeaderConst9;
    private const byte PngIsaretcisi = 0x0E;
    private const string SurumMetni = "8.5.0.1654"; // spike: PNG işaretçisinden hemen önceki Pascal sürüm metni

    private const byte NoktaTipi = 1;
    private const byte YaziTipi = 5;
    private const byte SonKayitTipi = 14;
    private const int OrtakBaslikBoyutu = 81;
    private const int KatmanBaytOfseti = 2;
    private const int KatmanOfseti = 30;
    private const int RefOfseti = 3;
    private const int KutuOfseti = 45;
    private const int YaziGenislikOfseti = 41;
    private const int NoktaBoyutu = 104;
    private const int NoktaAdiAlani = 15;
    private const int YaziMetinOfseti = 92;
    private const byte YaziStili = (byte)'M';
    private const int PoligonBayragi = 0x13; // 0x10 sabit | 0x01 kapalı | 0x02 alan (spike §2.5)

    /// <summary>Uygulama sınırı: yazı yüksekliği (m) = 2 × ölçek paydası / 1000, [1, 10] aralığında (1/1000 → 2.0).</summary>
    private const decimal TemelYaziYuksekligi = 2m;

    /// <summary>Spike §2.4: sınır kutusu genişliği ≈ 0.8 × yükseklik × karakter sayısı.</summary>
    private const decimal KarakterGenislikOrani = 0.8m;

    /// <summary>Spike §1: <c>0</c> katmanında gözlenen öznitelik (kişisel veri değildir).</summary>
    private static readonly byte[] VarsayilanKatmanOzniteligi = [0x01, 0x00, 0x01, 0x00, 0xFD, 0x80, 0x12, 0x00];

    /// <summary>Sessiz <c>?</c> yerine istisna: gösterilemeyen karakter tescil no'yu bozmasın.</summary>
    private static readonly Encoding Kati = CodePagesEncodingProvider.Instance.GetEncoding(1254, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        ?? throw new InvalidOperationException("Windows-1254 encoding is unavailable.");

    public static Result<byte[]> Yaz(CiktiPaketi paket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paket);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var yazim = paket.NczSablonu is { } s ? Sablonlu(paket, s.Icerik.Span, cancellationToken) : Iskelet(paket, cancellationToken);
            Dogrula(yazim, cancellationToken);
            return Result<byte[]>.Success(yazim.Bayt);
        }
        catch (YazimHatasi ex)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiCadDisaAktarim);
            return Result<byte[]>.Failure(e with
            {
                TechnicalMessage = e.TechnicalMessage.Replace("{format}", "NCZ (" + ex.Message + ")", StringComparison.Ordinal),
            });
        }
    }

    /// <summary>Yazılan poligonun beklenen geri okuma değerleri (doğrulama için).</summary>
    private sealed record BeklenenPoligon(string Katman, string Etiket, IReadOnlyList<GeodeticPoint> Kapali);

    private sealed record Yazim(byte[] Bayt, int KorunanPoligon, List<BeklenenPoligon> Beklenen)
    {
        /// <summary>Şablon modunda şablondan kopyalanması gereken proje kadastro halkaları (kanonik; reviewer H2).</summary>
        public IReadOnlyList<List<(decimal N, decimal E)>> Kadastro { get; init; } = [];
    }

    /// <summary>Yapısal yazım hatası (iletisi yalnız yapısal neden taşır; koordinat/etiket yazılmaz).</summary>
    private sealed class YazimHatasi(string neden) : Exception(neden);

    // ---------------------------------------------------------------- iskelet modu

    private static Yazim Iskelet(CiktiPaketi p, CancellationToken ct)
    {
        string[] katmanlar = [SifirKatmani, GirenKatmani, OlusanKatmani, YolTerkKatmani, NoktaKatmani];
        var indeks = katmanlar.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i, StringComparer.Ordinal);
        var beklenen = new List<BeklenenPoligon>();
        var kayitlar = YeniKayitlar(p, indeks, sablonlu: false, beklenen, ct);

        // Spike §1: 49..561 sabit başlık bölgesi (kapsam, sayaç, sürüm metni; kullanıcı adı yazılmaz), 562'de boş PNG işaretçisi.
        var bolge = new byte[PngIsaretcisiOfseti + 5 - NczParser.HeaderSize];
        var surum = Encoding.ASCII.GetBytes(SurumMetni);
        var surumOfseti = PngIsaretcisiOfseti - NczParser.HeaderSize - 1 - surum.Length;
        bolge[surumOfseti] = (byte)surum.Length;
        surum.CopyTo(bolge, surumOfseti + 1);
        bolge[PngIsaretcisiOfseti - NczParser.HeaderSize] = PngIsaretcisi;

        var bayt = NczAnonimlestirici.Iskelet(katmanlar, "<NetmapManager></NetmapManager>"u8.ToArray(), kayitlar,
            bolge, PngIsaretcisiOfseti + 5, VarsayilanKatmanOzniteligi, bolumIsaretcisi: true);
        BaslikBolgesiniGuncelle(bayt, null, kayitlar.Select(k => k.Yuk));
        return new Yazim(bayt, 0, beklenen);
    }

    // ---------------------------------------------------------------- şablon modu

    private static Yazim Sablonlu(CiktiPaketi p, ReadOnlySpan<byte> s, CancellationToken ct)
    {
        var ayristirma = NczParser.Parse(s, ct);
        if (ayristirma.IsFailure)
        {
            throw new YazimHatasi("template does not parse");
        }

        Span<int> h = stackalloc int[NczParser.HeaderIntCount];
        for (var i = 0; i < NczParser.HeaderIntCount; i++)
        {
            h[i] = BinaryPrimitives.ReadInt32LittleEndian(s[(1 + (4 * i))..]);
        }

        var tablo = h[NczParser.HeaderLayerTable];
        var katmanlar = ayristirma.Value.Layers.Select(l => l.Name).ToList();
        var eskiSayi = katmanlar.Count;
        var tabloSonu = tablo + NczParser.LayerTableHeaderSize + (NczParser.LayerRecordSize * eskiSayi);
        var yonetilen = new HashSet<int>();
        foreach (var ad in new[] { OlusanKatmani, YolTerkKatmani, NoktaKatmani })
        {
            var i = katmanlar.IndexOf(ad);
            if (i >= 0)
            {
                yonetilen.Add(i);
            }
            else
            {
                katmanlar.Add(ad);
            }
        }

        var indeks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = katmanlar.Count - 1; i >= 0; i--)
        {
            indeks[katmanlar[i]] = i; // yinelenen adda ilk indeks
        }

        indeks.TryAdd(SifirKatmani, 0); // damga: "0" yoksa ilk katman
        var giren = katmanlar.IndexOf(GirenKatmani);
        var eklenen = katmanlar.Count - eskiSayi;
        if (katmanlar.Count > short.MaxValue)
        {
            throw new YazimHatasi("layer table overflow");
        }

        var beklenen = new List<BeklenenPoligon>();
        var yeni = YeniKayitlar(p, indeks, sablonlu: true, beklenen, ct);

        // Şablon kayıtları: yönetilen katmanlar ve projedeki kadastro dışı parselle aynı halka düşer; kadastro korunur.
        var yeniden = p.Parseller.Where(x => x.Rol != ParselRolu.Kadastro).Select(x => Kanonik(x.Koseler)).ToList();
        var kadastro = p.Parseller.Where(x => x.Rol == ParselRolu.Kadastro).Select(x => Kanonik(x.Koseler)).ToList();
        var korunan = new List<(byte Bayrak, byte[] Yuk)>();
        var korunanPoligon = 0;
        for (var pos = h[NczParser.HeaderEntityStream]; pos < s.Length;)
        {
            ct.ThrowIfCancellationRequested();
            var bayrak = s[pos];
            var n = BinaryPrimitives.ReadInt32LittleEndian(s[(pos + 1)..]);
            var yuk = s.Slice(pos + NczParser.RecordHeaderSize, n);
            pos += NczParser.RecordHeaderSize + n;
            var varlik = bayrak is NczParser.EntityRecord or NczParser.ExtendedEntityRecord && n > KatmanOfseti + 1;
            if (varlik)
            {
                int katman = BinaryPrimitives.ReadInt16LittleEndian(yuk[KatmanOfseti..]);
                var poligon = yuk[0] == NczParser.PolylineType;
                var halka = poligon ? Kanonik(SablonKoseleri(yuk, bayrak == NczParser.ExtendedEntityRecord)) : null;
                var korunmali = katman == giren || (halka is not null && kadastro.Any(k => k.SequenceEqual(halka)));
                var dus = !korunmali && (yonetilen.Contains(katman) || (halka is not null && yeniden.Any(k => k.SequenceEqual(halka))));
                if (dus)
                {
                    continue;
                }

                korunanPoligon += poligon ? 1 : 0;
            }

            korunan.Add((bayrak, yuk.ToArray()));
        }

        // Spike §2: son kayıt tip 14 ise yeni kayıtlar onun önüne eklenir.
        var ekleme = korunan.Count > 0 && korunan[^1].Bayrak is NczParser.EntityRecord or NczParser.ExtendedEntityRecord
            && korunan[^1].Yuk.Length > 0 && korunan[^1].Yuk[0] == SonKayitTipi
            ? korunan.Count - 1
            : korunan.Count;
        korunan.InsertRange(ekleme, yeni);

        // Bayt düzeni: [başlık + başlık bölgesi + katman tablosu] + yeni katmanlar + [font/sembol/XML/0x0D] + akış.
        var d = new List<byte>(s.Length + (eklenen * NczParser.LayerRecordSize) + yeni.Sum(k => k.Yuk.Length + 5));
        d.AddRange(s[..tabloSonu].ToArray());
        BinaryPrimitives.WriteInt16LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(d).Slice(tablo + NczParser.LayerCountOffset), (short)katmanlar.Count);
        var oznitelik = katmanlar.IndexOf(SifirKatmani) is var z and >= 0 && z < eskiSayi
            ? s.Slice(tablo + NczParser.LayerTableHeaderSize + (NczParser.LayerRecordSize * z) + 1 + NczParser.LayerNameCapacity, NczParser.LayerRecordSize - 1 - NczParser.LayerNameCapacity).ToArray()
            : VarsayilanKatmanOzniteligi;
        foreach (var ad in katmanlar.Skip(eskiSayi))
        {
            d.AddRange(Pascal(ad, 1 + NczParser.LayerNameCapacity));
            d.AddRange(oznitelik);
        }

        d.AddRange(s[tabloSonu..h[NczParser.HeaderEntityStream]].ToArray());
        var boyut = new byte[4];
        foreach (var (bayrak, yuk) in korunan)
        {
            d.Add(bayrak);
            BinaryPrimitives.WriteInt32LittleEndian(boyut, yuk.Length);
            d.AddRange(boyut);
            d.AddRange(yuk);
        }

        var bayt = d.ToArray();
        var kayma = eklenen * NczParser.LayerRecordSize;
        for (var i = 0; i < NczParser.HeaderIntCount; i++)
        {
            var deger = h[i];
            if (i == NczParser.HeaderFileLength)
            {
                deger = bayt.Length;
            }
            else if (i == NczParser.HeaderLayerTableEnd)
            {
                deger = tabloSonu + kayma - NczParser.LayerTableEndBias;
            }
            else if (KayanBasliklar.Contains(i) && deger >= tabloSonu)
            {
                deger += kayma; // yalnız h[8], h[10], h[11] (spike: katman tablosundan sonraki mutlak ofsetler)
            }

            BinaryPrimitives.WriteInt32LittleEndian(bayt.AsSpan(1 + (4 * i)), deger);
        }

        if (tablo >= BaslikBolgesiEnAz)
        {
            var eskiKapsam = Kapsam(s);
            var varliklar = korunan.Where(k => k.Bayrak is NczParser.EntityRecord or NczParser.ExtendedEntityRecord && k.Yuk.Length > 0).Select(k => k.Yuk).ToList();

            // Eski kapsam geçersizse (ör. sıfır dolgu) tüm varlıklar, geçerliyse yalnız yeniler kapsamı genişletir (küçültülmez).
            BaslikBolgesiniGuncelle(bayt, eskiKapsam, varliklar, eskiKapsam is null ? varliklar : yeni.Select(k => k.Yuk));
        }

        return new Yazim(bayt, korunanPoligon, beklenen) { Kadastro = kadastro };
    }

    /// <summary>Şablondaki 49..80 kapsamı; geçersizse (min ≥ max, ör. sıfır dolgu) null.</summary>
    private static double[]? Kapsam(ReadOnlySpan<byte> s)
    {
        var k = new double[4];
        for (var i = 0; i < 4; i++)
        {
            k[i] = BinaryPrimitives.ReadDoubleLittleEndian(s[(KapsamOfseti + (8 * i))..]);
        }

        return k.All(double.IsFinite) && k[0] < k[2] && k[1] < k[3] ? k : null;
    }

    /// <summary>
    /// Ofset 81 = varlık sayısı − 1 ve 49..80 kapsam = eski kapsam ∪ yeni varlıkların sınır kutusu (spike §1). İskelet modunda
    /// <paramref name="sayilanlar"/> tüm varlıklardır; şablonda eski kapsam küçültülmez.
    /// </summary>
    private static void BaslikBolgesiniGuncelle(byte[] d, double[]? eski, IEnumerable<byte[]> sayilanlar, IEnumerable<byte[]>? kapsamaGirenler = null)
    {
        var liste = sayilanlar.ToList();
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(SayacOfseti), Math.Max(0, liste.Count - 1));
        var k = eski ?? [double.MaxValue, double.MaxValue, double.MinValue, double.MinValue];
        foreach (var y in kapsamaGirenler ?? liste)
        {
            if (y.Length < KutuOfseti + 32 || y[0] == SonKayitTipi)
            {
                continue;
            }

            k[0] = Math.Min(k[0], BinaryPrimitives.ReadDoubleLittleEndian(y.AsSpan(KutuOfseti)));
            k[1] = Math.Min(k[1], BinaryPrimitives.ReadDoubleLittleEndian(y.AsSpan(KutuOfseti + 8)));
            k[2] = Math.Max(k[2], BinaryPrimitives.ReadDoubleLittleEndian(y.AsSpan(KutuOfseti + 16)));
            k[3] = Math.Max(k[3], BinaryPrimitives.ReadDoubleLittleEndian(y.AsSpan(KutuOfseti + 24)));
        }

        if (k[0] <= k[2] && k[1] <= k[3])
        {
            for (var i = 0; i < 4; i++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(d.AsSpan(KapsamOfseti + (8 * i)), k[i]);
            }
        }
    }

    // ---------------------------------------------------------------- yeni kayıtlar

    private static List<(byte Bayrak, byte[] Yuk)> YeniKayitlar(
        CiktiPaketi p, IReadOnlyDictionary<string, int> indeks, bool sablonlu, List<BeklenenPoligon> beklenen, CancellationToken ct)
    {
        var h = Math.Clamp(TemelYaziYuksekligi * p.OlcekPaydasi / 1000m, 1m, 10m);
        var kayitlar = new List<(byte, byte[])>();
        var noktalar = new SortedDictionary<int, GeodeticPoint>();
        foreach (var x in p.Parseller)
        {
            ct.ThrowIfCancellationRequested();
            if (x.Koseler.Count < 3 || (x.NoktaNolari.Count != x.Koseler.Count && x.Rol != ParselRolu.Kadastro))
            {
                throw new YazimHatasi("parcel ring is incomplete");
            }

            if (x.Rol == ParselRolu.Kadastro && sablonlu)
            {
                continue; // kadastro şablondan aynen kopyalanır
            }

            var (katman, genis) = x.Rol switch
            {
                ParselRolu.Kadastro => (GirenKatmani, false),
                ParselRolu.Imar => (OlusanKatmani, true),
                ParselRolu.YolTerk => (YolTerkKatmani, false),
                _ => (OlusanKatmani, false),
            };
            var etiket = x.Rol == ParselRolu.Kadastro || x.TescilNo != "-" ? x.TescilNo : x.GeciciKod;
            var k = Katman(indeks, katman);
            kayitlar.Add((genis ? NczParser.ExtendedEntityRecord : NczParser.EntityRecord, Poligon(k, genis ? ImarSinifi : null, etiket, x.DengelenmisAlanM2, x.Koseler)));
            beklenen.Add(new BeklenenPoligon(katman, etiket, [.. x.Koseler, x.Koseler[0]]));
            var (n, e) = IcNokta(x.Koseler);
            kayitlar.Add((NczParser.EntityRecord, Yazi(k, etiket, n, e, h, ortala: true)));

            if (x.Rol != ParselRolu.Kadastro)
            {
                for (var i = 0; i < x.Koseler.Count; i++)
                {
                    noktalar.TryAdd(x.NoktaNolari[i], x.Koseler[i]);
                }
            }
        }

        var nk = Katman(indeks, NoktaKatmani);
        foreach (var (no, v) in noktalar)
        {
            kayitlar.Add((NczParser.EntityRecord, Nokta(nk, no.ToString(CultureInfo.InvariantCulture), v)));
        }

        // İçerik damgası (SORU-050): "0" katmanında, parsellerin sol üstünde iki satır.
        var tum = p.Parseller.SelectMany(x => x.Koseler).ToList();
        if (tum.Count > 0)
        {
            var (ustN, solE) = (tum.Max(v => v.X) + (3m * h), tum.Min(v => v.Y));
            var sk = Katman(indeks, SifirKatmani);
            kayitlar.Add((NczParser.EntityRecord, Yazi(sk, p.PilotDamgasi, ustN + (1.5m * h), solE, h, ortala: false)));
            kayitlar.Add((NczParser.EntityRecord, Yazi(sk, "RevisionId " + p.RevizyonId, ustN, solE, h, ortala: false)));
        }

        return kayitlar;
    }

    private static int Katman(IReadOnlyDictionary<string, int> indeks, string ad)
    {
        var i = indeks[ad];
        return i <= byte.MaxValue ? i : throw new YazimHatasi("layer index exceeds 255");
    }

    private static byte[] OrtakBaslik(byte tip, int katman, int boyut, decimal refN, decimal refE, decimal mnN, decimal mnE, decimal mxN, decimal mxE)
    {
        var y = new byte[boyut];
        y[0] = tip;
        y[1] = tip;
        y[KatmanBaytOfseti] = (byte)katman;
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(RefOfseti), (double)refN);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(RefOfseti + 8), (double)refE);
        BinaryPrimitives.WriteInt16LittleEndian(y.AsSpan(KatmanOfseti), (short)katman);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(KutuOfseti), (double)mnN);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(KutuOfseti + 8), (double)mnE);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(KutuOfseti + 16), (double)mxN);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(KutuOfseti + 24), (double)mxE);
        return y;
    }

    /// <summary>Tip 7: ref (0, 0), sınır kutusu, [sınıf 28], etiket 15, bayrak 0x13, yazılmış alan, köşeler + kapanış (kot 0).</summary>
    private static byte[] Poligon(int katman, string? sinif, string etiket, decimal alan, IReadOnlyList<GeodeticPoint> koseler)
    {
        var sabit = OrtakBaslikBoyutu + (sinif is null ? 0 : NczParser.ExtendedClassFieldSize) + NczParser.LabelFieldSize + NczParser.PolylineFlagsSize + NczParser.DeclaredAreaSize;
        var y = OrtakBaslik(NczParser.PolylineType, katman, sabit + (NczParser.VertexSize * (koseler.Count + 1)), 0m, 0m,
            koseler.Min(v => v.X), koseler.Min(v => v.Y), koseler.Max(v => v.X), koseler.Max(v => v.Y));
        var o = OrtakBaslikBoyutu;
        if (sinif is not null)
        {
            Pascal(sinif, NczParser.ExtendedClassFieldSize).CopyTo(y, o);
            o += NczParser.ExtendedClassFieldSize;
        }

        Pascal(etiket, NczParser.LabelFieldSize).CopyTo(y, o);
        o += NczParser.LabelFieldSize;
        BinaryPrimitives.WriteInt32LittleEndian(y.AsSpan(o), PoligonBayragi);
        BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(o + NczParser.PolylineFlagsSize), (double)alan);
        o += NczParser.PolylineFlagsSize + NczParser.DeclaredAreaSize;
        for (var i = 0; i <= koseler.Count; i++)
        {
            var v = koseler[i % koseler.Count];
            BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(o), (double)v.X);
            BinaryPrimitives.WriteDoubleLittleEndian(y.AsSpan(o + 8), (double)v.Y);
            o += NczParser.VertexSize; // kot 0
        }

        return y;
    }

    /// <summary>Tip 1 (104 bayt): konum = ref, sınır kutusu min = max, 15 baytlık nokta adı, kot 0.</summary>
    private static byte[] Nokta(int katman, string ad, GeodeticPoint v)
    {
        var y = OrtakBaslik(NoktaTipi, katman, NoktaBoyutu, v.X, v.Y, v.X, v.Y, v.X, v.Y);
        Pascal(ad, NoktaAdiAlani).CopyTo(y, OrtakBaslikBoyutu);
        return y;
    }

    /// <summary>Tip 5 (93 + uzunluk): ref = sol-alt ekleme noktası, genişlik çarpanı 1, yükseklik, dönüklük 0, stil 'M', metin.</summary>
    private static byte[] Yazi(int katman, string metin, decimal n, decimal e, decimal h, bool ortala)
    {
        var b = Bayt(metin, byte.MaxValue);
        var w = KarakterGenislikOrani * h * metin.Length;
        var (refN, refE) = ortala ? (n - (h / 2m), e - (w / 2m)) : (n, e);
        var y = OrtakBaslik(YaziTipi, katman, YaziMetinOfseti + 1 + b.Length, refN, refE, refN, refE, refN + h, refE + w);
        BinaryPrimitives.WriteSingleLittleEndian(y.AsSpan(YaziGenislikOfseti), 1f);
        BinaryPrimitives.WriteSingleLittleEndian(y.AsSpan(OrtakBaslikBoyutu), (float)h);
        y[YaziMetinOfseti - 3] = YaziStili;
        y[YaziMetinOfseti] = (byte)b.Length;
        b.CopyTo(y, YaziMetinOfseti + 1);
        return y;
    }

    private static byte[] Pascal(string metin, int genislik)
    {
        var b = Bayt(metin, genislik - 1);
        var alan = new byte[genislik];
        alan[0] = (byte)b.Length;
        b.CopyTo(alan, 1);
        return alan;
    }

    private static byte[] Bayt(string metin, int enFazla)
    {
        byte[] b;
        try
        {
            b = Kati.GetBytes(metin);
        }
        catch (EncoderFallbackException)
        {
            throw new YazimHatasi("text is not representable in Windows-1254");
        }

        return b.Length <= enFazla
            ? b
            : throw new YazimHatasi("text exceeds field capacity of " + enFazla.ToString(CultureInfo.InvariantCulture) + " bytes");
    }

    // ---------------------------------------------------------------- geometri (Decimal)

    /// <summary>Kapanışsız, döngüsel kaydırma ve yönden bağımsız kanonik köşe dizisi (6 ondalığa normalize).</summary>
    private static List<(decimal N, decimal E)> Kanonik(IReadOnlyList<GeodeticPoint> koseler)
    {
        var v = koseler.Select(k => (N: decimal.Round(k.X, 6), E: decimal.Round(k.Y, 6))).ToList();
        if (v.Count > 1 && v[0] == v[^1])
        {
            v.RemoveAt(v.Count - 1);
        }

        if (v.Count == 0)
        {
            return v;
        }

        static List<(decimal N, decimal E)> EnKucukDondurme(List<(decimal N, decimal E)> l)
        {
            var bas = 0;
            for (var i = 1; i < l.Count; i++)
            {
                if (l[i].N < l[bas].N || (l[i].N == l[bas].N && l[i].E < l[bas].E))
                {
                    bas = i;
                }
            }

            return [.. l.Skip(bas), .. l.Take(bas)];
        }

        var ileri = EnKucukDondurme(v);
        var ters = EnKucukDondurme(Enumerable.Reverse(v).ToList());
        for (var i = 0; i < ileri.Count; i++)
        {
            var c = ileri[i].N != ters[i].N ? ileri[i].N.CompareTo(ters[i].N) : ileri[i].E.CompareTo(ters[i].E);
            if (c != 0)
            {
                return c < 0 ? ileri : ters;
            }
        }

        return ileri;
    }

    private static IReadOnlyList<GeodeticPoint> SablonKoseleri(ReadOnlySpan<byte> yuk, bool genis)
    {
        var o = NczParser.PolylineStringsOffset + (genis ? NczParser.ExtendedClassFieldSize : 0) + NczParser.LabelFieldSize + NczParser.PolylineFlagsSize + NczParser.DeclaredAreaSize;
        var liste = new List<GeodeticPoint>();
        for (; o + NczParser.VertexSize <= yuk.Length; o += NczParser.VertexSize)
        {
            if (Ieee754Decimal.TryDecode(BinaryPrimitives.ReadUInt64LittleEndian(yuk[o..]), NczParser.CoordinateScale, out var n)
                && Ieee754Decimal.TryDecode(BinaryPrimitives.ReadUInt64LittleEndian(yuk[(o + 8)..]), NczParser.CoordinateScale, out var e))
            {
                liste.Add(new GeodeticPoint(n, e));
            }
        }

        return liste;
    }

    /// <summary>Etiket noktası: Decimal alan ağırlık merkezi; poligon dışında kalırsa (konkav) yatay tarama ile en geniş iç aralığın ortası.</summary>
    private static (decimal N, decimal E) IcNokta(IReadOnlyList<GeodeticPoint> v)
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

        if (a2 != 0m)
        {
            var aday = (N: n0 + (cn / (3m * a2)), E: e0 + (ce / (3m * a2)));
            if (Icinde(v, aday.N, aday.E))
            {
                return (decimal.Round(aday.N, 3), decimal.Round(aday.E, 3));
            }
        }

        var orta = ((v.Min(k => k.X) + v.Max(k => k.X)) / 2m) + 0.000137m; // köşeden geçmeyen tarama çizgisi
        var kesisim = new List<decimal>();
        for (var i = 0; i < v.Count; i++)
        {
            var (a, b) = (v[i], v[(i + 1) % v.Count]);
            if ((a.X > orta) != (b.X > orta))
            {
                kesisim.Add(a.Y + ((orta - a.X) * (b.Y - a.Y) / (b.X - a.X)));
            }
        }

        kesisim.Sort();
        var (en, sol) = (-1m, v[0].Y);
        for (var i = 0; i + 1 < kesisim.Count; i += 2)
        {
            if (kesisim[i + 1] - kesisim[i] > en)
            {
                (en, sol) = (kesisim[i + 1] - kesisim[i], kesisim[i]);
            }
        }

        return (decimal.Round(orta, 3), decimal.Round(sol + (Math.Max(en, 0m) / 2m), 3));
    }

    private static bool Icinde(IReadOnlyList<GeodeticPoint> v, decimal n, decimal e)
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

    // ---------------------------------------------------------------- doğrulama

    private static void Dogrula(Yazim y, CancellationToken ct)
    {
        var okunan = NczParser.Parse(y.Bayt, ct);
        if (okunan.IsFailure)
        {
            throw new YazimHatasi("written file does not parse");
        }

        var poligonlar = okunan.Value.Polylines;
        if (poligonlar.Count != y.KorunanPoligon + y.Beklenen.Count)
        {
            throw new YazimHatasi("written polygon count mismatch");
        }

        foreach (var b in y.Beklenen)
        {
            if (!poligonlar.Any(p => p.LayerName == b.Katman && p.Label == b.Etiket && p.Vertices.SequenceEqual(b.Kapali)))
            {
                throw new YazimHatasi("written polygon does not read back exactly");
            }
        }

        // Reviewer H2: şablon projeyle uyuşmuyorsa (kadastro şablonda yok) eksik/yanlış harita sessizce üretilmez.
        var halkalar = poligonlar.Select(p => Kanonik(p.Vertices)).ToList();
        if (y.Kadastro.Any(k => !halkalar.Any(h => h.SequenceEqual(k))))
        {
            throw new YazimHatasi("template does not contain the project's cadastral parcels");
        }
    }
}
