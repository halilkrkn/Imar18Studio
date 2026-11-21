using System.Globalization;
using System.Text;
using Imar18.Application.Cad;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;

namespace Imar18.Application.Girdi;

/// <summary>
/// SAHA-015 girdisi: Yaklaşık Yer Krokisi (projenin aktif koordinat sisteminde — <c>.DNS</c> etkinse <see cref="CadBelgesiDonusumu"/>
/// ile dönüştürülmüş), proje parselleri (aktif), doğrulanmış TAKBİS, Malik Rehberi ve DOPO (yoksa null).
/// </summary>
public sealed record KrokiParselasyonGirdisi(
    CadLayerDocument Kroki,
    IReadOnlyList<ParselGeometrisi> ProjeParselleri,
    IReadOnlyList<KadastroParseli> Takbis,
    MalikRehberi Rehber,
    decimal? Dopo)
{
    /// <summary>SAHA-021: varsayılan başlangıç imar ada numarası.</summary>
    public const int VarsayilanBaslangicAdaNo = 101;

    /// <summary>SAHA-021: başlangıç ada numarasının üst sınırı (parsel kodu 64 karakter sınırının çok altında).</summary>
    public const int EnBuyukBaslangicAdaNo = 9_999_999;

    /// <summary>
    /// SAHA-021: blok etiketi boş, sayısal olmayan veya <c>100</c>'den küçük sıra numarası (<c>"1"</c>, <c>"2"</c>) olan bölünen
    /// blokların yeni imar ada numarası buradan başlar (<c>101</c>, <c>102</c>…); mühendis 4. adımda <c>201</c>, <c>301</c> verebilir.
    /// </summary>
    public int BaslangicAdaNo { get; init; } = VarsayilanBaslangicAdaNo;
}

/// <summary>
/// Kesin alanlı yeni <c>TK_Oluşan</c> parseli. <see cref="Koseler"/> aktif sistemde açık halkadır; <see cref="HedefAlanM2"/> blok
/// toplamına dengelenmiş kesin alan (= 2 ondalık Gauss alanı <see cref="GaussAlanM2"/>), <see cref="TaslakAlanM2"/> mühendisin kabaca
/// çizdiği bölgenin alanıdır. <see cref="Malikler"/> parselin tüm paydaşlarının TAKBİS kimlikleridir (krokideki malik önce; yerel ekran,
/// denetime yazılmaz). SAHA-016: <see cref="Hissedarlar"/> müstakil (tek paydaş, <c>TAM</c>) veya müşterek (dm² pay/payda) dağılımdır.
/// </summary>
public sealed record KrokiParseli(
    string Kod,
    string BlokKimligi,
    IReadOnlyList<GeodeticPoint> Koseler,
    decimal HedefAlanM2,
    decimal GaussAlanM2,
    decimal TaslakAlanM2,
    IReadOnlyList<string> Malikler)
{
    /// <summary>SAHA-016: paydaşlar (alan + tescil hissesi). Boşsa dağıtım yapılmamıştır.</summary>
    public IReadOnlyList<ParselHissedari> Hissedarlar { get; init; } = [];

    /// <summary>Tek paydaşlı (TAM) müstakil parsel.</summary>
    public bool Mustakil => Hissedarlar.Count == 1;

    /// <summary>Etiketteki malik görünümü: baba adısız ad soyad; birden çok malikte "+n"; malik yoksa "Malik ?".</summary>
    public string MalikGorunumu() => Malikler.Count == 0
        ? "Malik ?"
        : MalikEtiketi.Ayristir(Malikler[0]).AdSoyad + (Malikler.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $" +{Malikler.Count - 1}") : string.Empty);

    /// <summary>Kısa mülkiyet durumu (tuval): <c>TAM</c>, <c>Müşterek</c>; dağıtım yoksa boş.</summary>
    public string MulkiyetDurumu() => Hissedarlar.Count == 0 ? string.Empty : Mustakil ? "TAM" : "Müşterek";

    /// <summary>
    /// Önizleme kartı özeti: <c>Müstakil (TAM)</c> veya <c>Müşterek (2 hissedar: AD SOYAD 16040/31477, AD SOYAD 15437/31477)</c>
    /// (baba adısız ad; pay/payda dm²).
    /// </summary>
    public string HisseOzeti() => Hissedarlar.Count switch
    {
        0 => "Dağıtım yok",
        1 => "Müstakil (TAM)",
        _ => string.Create(CultureInfo.InvariantCulture,
            $"Müşterek ({Hissedarlar.Count} hissedar: {string.Join(", ", Hissedarlar.Select(h => MalikEtiketi.Ayristir(h.MalikId).AdSoyad + " " + h.Hisse))})"),
    };

    /// <summary>Önizleme kartı satırı: <c>101/1 • 511.77 m² • Müstakil (TAM)</c>.</summary>
    public string DagilimSatiri() => string.Create(CultureInfo.InvariantCulture, $"{Kod} • {GaussAlanM2:0.00} m² • {HisseOzeti()}");

    /// <summary>Tuval etiketi: <c>Ada/Parsel • Malik Adı • Alan m² • TAM|Müşterek</c>.</summary>
    public string Etiket() => Etiket(Kod, MalikGorunumu(), GaussAlanM2, MulkiyetDurumu());

    /// <summary>Etiket biçimi (uygulanmış katmanda alan projeden yeniden hesaplanınca da kullanılır — reviewer L1).</summary>
    public static string Etiket(string kod, string malik, decimal alanM2, string? durum = null) => string.IsNullOrEmpty(durum)
        ? string.Create(CultureInfo.InvariantCulture, $"{kod} • {malik} • {alanM2:0.00} m²")
        : string.Create(CultureInfo.InvariantCulture, $"{kod} • {malik} • {alanM2:0.00} m² • {durum}");
}

/// <summary>Kroki parselasyon önizlemesi (projeye yazılmamış taslak): yeni parseller, bölünen bloklar, Kroki satırları, farklar ve uyarılar.</summary>
public sealed record KrokiParselasyonOnizleme(
    IReadOnlyList<KrokiParseli> Parseller,
    IReadOnlyList<string> BolunenBloklar,
    IReadOnlyList<KrokiEslesmeGirdisi> Satirlar,
    IReadOnlyList<KrokiFarki> Farklar,
    IReadOnlyList<string> Uyarilar)
{
    /// <summary>SAHA-019: müstakil / müşterek dağılım mühendis tarafından elle düzenlendi (<see cref="KrokiDagilimDuzenleyici"/>).</summary>
    public bool ElleDuzenlendi { get; init; }

    /// <summary>Yeni parsellerin hedef alan toplamı (= bölünen blokların Gauss toplamı).</summary>
    public decimal ToplamAlanM2 => Parseller.Sum(p => p.HedefAlanM2);

    /// <summary>Müstakil (TAM) parsel sayısı.</summary>
    public int MustakilSayisi => Parseller.Count(p => p.Mustakil);

    /// <summary>Müşterek (pay/payda) parsel sayısı.</summary>
    public int MusterekSayisi => Parseller.Count(p => p.Hissedarlar.Count > 1);

    /// <summary>Kısa özet: blok/parsel sayısı, alan toplamı, en büyük sapma ve müstakil/müşterek sayıları.</summary>
    public string Ozet() => string.Create(CultureInfo.InvariantCulture,
        $"{BolunenBloklar.Count} blok · {Parseller.Count} parsel · Σ {ToplamAlanM2:0.00} m² · en büyük sapma {Parseller.Select(p => Math.Abs(p.GaussAlanM2 - p.HedefAlanM2)).DefaultIfEmpty(0m).Max():0.00} m² · {MustakilSayisi} müstakil (TAM) · {MusterekSayisi} müşterek (pay/payda){(ElleDuzenlendi ? " · ✏️ elle düzenlendi" : string.Empty)}");

    /// <summary>Önizleme kartının parsel dağılım satırları (<see cref="KrokiParseli.DagilimSatiri"/>).</summary>
    public IReadOnlyList<string> DagilimSatirlari() => [.. Parseller.Select(p => p.DagilimSatiri())];

    /// <summary>Kroki kutusu metni: özet yorumu, <c>malik;kadastro;imar;alan</c> satırları ve malik bazında fark yorumları.</summary>
    public string KrokiMetni()
    {
        var sb = new StringBuilder();
        sb.Append("# Yaklaşık Yer Krokisi otomatik parselasyon: ").Append(Ozet()).AppendLine();
        foreach (var s in Satirlar)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{s.MalikKodu};{s.KadastroParselKimligi};{s.ImarParselKodu};{s.AlanM2:0.00}").AppendLine();
        }

        foreach (var f in Farklar.Where(f => f.FarkM2 is { } x && x != 0m))
        {
            sb.Append(CultureInfo.InvariantCulture, $"# {f.MalikKodu} · {f.KadastroParselId}: kroki {f.KrokiM2:0.00} m² · net hak {f.NetHakM2:0.00} m² · fark {f.FarkM2:+0.00;-0.00;0.00} m²").AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// SAHA-015 (SORU-044): Yaklaşık Yer Krokisi → kesin alanlı otomatik parselasyon.
/// <list type="number">
/// <item><b>Bloklar</b>: projedeki imar (TK_Oluşan / İmar Adası) parselleri; projede hiç yoksa krokideki <c>TK_OLUSAN</c> rolündeki
/// kapalı poligonlar. Krokideki 2+ köşeli açık çizgiler (ilk → son köşe) orta noktası hangi bloğun kesin içindeyse o bloğun bölme
/// çizgisidir.</item>
/// <item><b>Taslak bölgeler</b>: <see cref="IKrokiBlokBolucu.TaslakBol"/>; her bölgeye içine düşen kroki yazıları (görünen merkez)
/// <see cref="YaklasikYerKrokisiEslestirici"/> çözücüsüyle (TAKBİS kimliği, rehber kişi no / ad, ≥ 5 karakterlik tekil önek, Türkçe
/// büyük/küçük harf) TAKBİS malikine eşlenir.</item>
/// <item><b>Net haklar (SAHA-016)</b>: hak sahibi (malik, kadastro) net hakkı <see cref="HakEslestirmeServisi.GirenAlanM2"/> /
/// <see cref="HakEslestirmeServisi.NetHakM2"/> (DOPO yoksa giren). Katılımcılar: projenin tüm imar blokları bölünüyorsa (tam kapsam)
/// TAKBİS'in tüm malikleri, aksi halde yalnız krokide yazısı olan malikler. Katılımcı net hakları bölünen blokların Σ Gauss alanına
/// (R) <see cref="LargestRemainderAllocator"/> ile 0.01 m² birimde dengelenir (Σ net = R ise birebir aynı; fark uyarılır).</item>
/// <item><b>Hedef alanlar</b>: birden çok bölgedeki malikin hakkı taslak alan oranında bölünür (talep). Blokta Σ talep = blok alanı
/// ise hedef = talep (hepsi müstakil); Σ talep &lt; blok alanı ise yazılı bölgeler talebini aynen alır, kalan yazısız bölgelere taslak
/// alan oranında, yazısız bölge yoksa en büyük talepli bölgeye verilir (açık kapasite tek parselde toplanır); Σ talep &gt; blok alanı
/// ise hedefler blok alanına orantılı (yazısız bölge taslak alanıyla) LR ile dengelenir.</item>
/// <item><b>Kesin bölme</b>: <see cref="IKrokiBlokBolucu.KesinBol"/> her kesimi mühendisin çizgisine paralel kaydırır; her parselin
/// decimal Gauss alanı hedefe <see cref="AlanToleransiM2"/> içinde ve 2 ondalık Gauss alanı hedefe birebir eşit, parseller toplamı
/// blok alanına eşit olmalıdır (aksi <c>ERR-KUANT-001</c>).</item>
/// <item><b>Müstakil + müşterek dağıtım (SAHA-016)</b>: <see cref="MustakilMusterekDagitici"/> önce talepleri parsellere müstakil
/// yerleştirir, artan / krokide olmayan hakları açık kapasiteli parsellere müşterek (dm² pay/payda) aktarır; Σ parsel = parsel alanı ve
/// Σ malik = dengelenmiş net hak 0.00 m² farkla sağlanır. AAD Md. 17/1-(d) muvafakat gerektiren müstakil parseller uyarılır.</item>
/// <item><b>Kodlar</b>: <c>{ada}/{sıra}</c>; ada = blok kimliğinin ilk <c>/</c> öncesi — SAHA-021: <c>≥ 100</c> ada numarasıysa aynen,
/// boş / sayısal olmayan / <c>100</c>'den küçük sıra numarasıysa (<c>"1"</c>, <c>"2"</c>) <see cref="KrokiParselasyonGirdisi.BaslangicAdaNo"/>'dan
/// (<c>101</c>, <c>102</c>…) yeni ada no; sıra taslak bölge sırası (batıdan doğuya).</item>
/// </list>
/// Uyarılar yalnız sayı, ada/parsel kodu ve alan içerir (malik adı yazmaz — KVKK); malik adları yalnız önizleme/tuval etiketinde.
/// </summary>
public static class KrokiParselasyonMotoru
{
    /// <summary>Her yeni parselin Gauss alanı ile hedef alanı arasında izin verilen en büyük fark (m², kullanıcı/saha gereksinimi).</summary>
    public const decimal AlanToleransiM2 = 0.01m;

    /// <summary>Teknik neden: krokide imar bloğunun içinde bölme çizgisi yok (arayüz eski Kip 1/2 akışına döner).</summary>
    public const string BolmeCizgisiYokNedeni = "no dividing line of the sketch lies inside a zoning block";

    /// <summary>Teknik neden: projede ve krokide imar bloğu yok.</summary>
    public const string ImarBlokYokNedeni = "no zoning block to subdivide";

    /// <summary>
    /// Krokinin bölme çizgisi adayları: tam 2 köşeli açık çizgiler (reviewer H1 / architect Y1 — çok köşeli kırık çizgi, ölçü/ok veya
    /// bina çizgisi düzleştirilip kesim sayılmaz; sayısı uyarıda raporlanır).
    /// </summary>
    public static IReadOnlyList<BolmeDogrusu> BolmeCizgileri(CadLayerDocument kroki)
    {
        ArgumentNullException.ThrowIfNull(kroki);
        return kroki.Polylines
            .Where(p => !p.IsClosed && p.Vertices.Count == 2 && p.Vertices[0] != p.Vertices[1])
            .Select(p => new BolmeDogrusu(p.Vertices[0], p.Vertices[1]))
            .ToArray();
    }

    /// <summary>
    /// Kroki parselasyona uygun mu: en az bir 2 köşeli açık çizginin orta noktası bir imar bloğunun (projede yoksa krokideki TK_OLUSAN)
    /// kesin içinde. Değilse arayüz P7.3-B2 Kip 1/2 Kroki satırı akışını kullanır (hata metni aranmaz).
    /// </summary>
    public static bool ParselasyonaUygun(CadLayerDocument kroki, IReadOnlyList<ParselGeometrisi> projeParselleri)
    {
        ArgumentNullException.ThrowIfNull(kroki);
        ArgumentNullException.ThrowIfNull(projeParselleri);
        var bloklar = Bloklar(kroki, projeParselleri).Bloklar;
        return BolmeCizgileri(kroki).Any(c => bloklar.Any(b => CadLabelMatcher.StrictlyInside(Kapali(b.Koseler), Orta(c))));
    }

    private sealed record Blok(string Kimlik, IReadOnlyList<GeodeticPoint> Koseler);

    private sealed record Bolge(int BlokSira, int Sira, IReadOnlyList<GeodeticPoint> Koseler, decimal TaslakAlan, List<string> Malikler)
    {
        /// <summary>Malike çözülen yazıların çapaları (kesin parselde kalıyor mu denetimi — reviewer M2 / architect Y3).</summary>
        public List<GeodeticPoint> Capalar { get; } = [];
    }

    private static GeodeticPoint Orta(BolmeDogrusu c) => new(X: (c.Baslangic.X + c.Bitis.X) / 2m, Y: (c.Baslangic.Y + c.Bitis.Y) / 2m);

    /// <summary>Bloklar: projedeki imar parselleri; yoksa krokideki TK_OLUSAN kapalı poligonları (kimlik: salt sayısal etiket, değilse ADA{n}).</summary>
    private static (List<Blok> Bloklar, bool Krokiden) Bloklar(CadLayerDocument kroki, IReadOnlyList<ParselGeometrisi> projeParselleri)
    {
        var proje = projeParselleri.Where(p => p.Rol == ParselRolu.Imar).Select(p => new Blok(p.ParselId, p.Koseler)).ToList();
        if (proje.Count > 0)
        {
            return (proje, false);
        }

        // Reviewer L2: serbest kroki etiketi kişisel veri içerebilir; yalnız ada no biçimindeki (rakam) etiket kimlik olur.
        var sira = 0;
        var krokiden = kroki.Polylines
            .Where(p => p.IsClosed && p.Vertices.Count >= 4 && CadLayerRoleMap.OtomatikRol(p.LayerName) == CadLayerRole.ZoningParcel)
            .Select(p =>
            {
                sira++;
                var etiket = p.Label.Trim();
                var kimlik = etiket.Length is > 0 and <= 10 && etiket.All(char.IsAsciiDigit) ? etiket : "ADA" + sira.ToString(CultureInfo.InvariantCulture);
                return new Blok(kimlik, p.Vertices.Take(p.Vertices.Count - 1).ToArray());
            })
            .Where(b => PolygonRing.FromOpen(b.Koseler).IsSuccess)
            .ToList();
        return (krokiden, true);
    }

    public static Result<KrokiParselasyonOnizleme> Olustur(KrokiParselasyonGirdisi girdi, IKrokiBlokBolucu bolucu, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(girdi);
        ArgumentNullException.ThrowIfNull(bolucu);
        if (girdi.Kroki is null || girdi.ProjeParselleri is null)
        {
            return Gecersiz("sketch and project parcels are required");
        }

        if (girdi.Takbis is null || girdi.Takbis.Count == 0)
        {
            return Gecersiz("TAKBIS shares are required before the sketch can be subdivided");
        }

        if (girdi.BaslangicAdaNo is < 1 or > KrokiParselasyonGirdisi.EnBuyukBaslangicAdaNo)
        {
            return Gecersiz("starting block number must be between 1 and 9999999");
        }

        var uyarilar = new List<string>();
        var cizgiler = BolmeCizgileri(girdi.Kroki);
        if (cizgiler.Count == 0)
        {
            return Gecersiz(BolmeCizgisiYokNedeni);
        }

        var (bloklar, krokiden) = Bloklar(girdi.Kroki, girdi.ProjeParselleri);
        if (bloklar.Count == 0)
        {
            return Gecersiz(ImarBlokYokNedeni);
        }

        if (krokiden)
        {
            uyarilar.Add("Projede imar parseli yok; krokideki TK_OLUSAN blokları bölündü (onayda projeye eklenir).");
        }

        var cokKoseli = girdi.Kroki.Polylines.Count(p => !p.IsClosed && p.Vertices.Count > 2);
        if (cokKoseli > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture, $"{cokKoseli} çok köşeli açık çizgi bölme çizgisi sayılmadı (yalnız 2 köşeli açık çizgiler bölme çizgisidir)."));
        }

        // Çizgi → blok: orta nokta bloğun kesin içinde.
        var blokCizgileri = bloklar.Select(_ => new List<BolmeDogrusu>()).ToArray();
        var sahipsiz = 0;
        foreach (var c in cizgiler)
        {
            var i = bloklar.FindIndex(b => CadLabelMatcher.StrictlyInside(Kapali(b.Koseler), Orta(c)));
            if (i < 0)
            {
                sahipsiz++;
            }
            else
            {
                blokCizgileri[i].Add(c);
            }
        }

        if (blokCizgileri.All(l => l.Count == 0))
        {
            return Gecersiz(BolmeCizgisiYokNedeni);
        }

        if (sahipsiz > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture, $"{sahipsiz} açık çizgi hiçbir imar bloğunun içinde değil; bölme çizgisi sayılmadı."));
        }

        // Hak sahipleri (HakEslestirmeServisi ile aynı tek adım yuvarlama — architect R2).
        var kodlar = girdi.Takbis.SelectMany(k => k.Malikler.Select(m => m.MalikId)).Distinct(StringComparer.Ordinal).ToList();
        var hak = new Dictionary<(string Malik, string Kadastro), decimal>();
        var mustakilOlabilir = new Dictionary<(string Malik, string Kadastro), bool>();
        foreach (var k in girdi.Takbis)
        {
            foreach (var m in k.Malikler)
            {
                var giren = HakEslestirmeServisi.GirenAlanM2(k.TapuAlaniM2, m.Hisse);
                var net = girdi.Dopo is { } dopo ? HakEslestirmeServisi.NetHakM2(giren, dopo) : giren;
                hak[(m.MalikId, k.ParselId)] = hak.GetValueOrDefault((m.MalikId, k.ParselId)) + net;
                mustakilOlabilir[(m.MalikId, k.ParselId)] = HakEslestirmeServisi.MustakilTahsisEdilebilir(k.Malikler.Count, m.Muvafakatli);
            }
        }

        if (girdi.Dopo is null)
        {
            uyarilar.Add("DOP girdisi yok: hedefler DOP öncesi giren alan oranıyla dengelendi (3. adımda DOP'u uygulayıp krokiyi yeniden yükleyin).");
        }

        // Taslak bölgeler ve malik yazıları.
        var rehber = girdi.Rehber ?? MalikRehberi.Bos;
        var yazilar = (girdi.Kroki.Texts ?? []).Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();
        var kullanilanYazi = new HashSet<CadText>(ReferenceEqualityComparer.Instance);
        var cozulemeyen = 0;
        var bolgeler = new List<Bolge>();
        var taslaklar = new Dictionary<int, KrokiTaslakBolmesi>();
        for (var b = 0; b < bloklar.Count; b++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (blokCizgileri[b].Count == 0)
            {
                continue;
            }

            var taslak = bolucu.TaslakBol(bloklar[b].Koseler, blokCizgileri[b], cancellationToken);
            if (taslak.IsFailure)
            {
                return BlokHatasi(taslak.Error, bloklar[b].Kimlik);
            }

            taslaklar[b] = taslak.Value;
            for (var i = 0; i < taslak.Value.Bolgeler.Count; i++)
            {
                var koseler = taslak.Value.Bolgeler[i];
                var halka = PolygonRing.FromOpen(koseler);
                if (halka.IsFailure)
                {
                    return BlokHatasi(halka.Error, bloklar[b].Kimlik);
                }

                var malikler = new List<string>();
                var capalar = new List<GeodeticPoint>();
                foreach (var y in yazilar.Where(y => CadLabelMatcher.StrictlyInside(Kapali(koseler), y.Capa)))
                {
                    kullanilanYazi.Add(y);
                    if (YaklasikYerKrokisiEslestirici.Coz(y.Text, kodlar, rehber) is { } m)
                    {
                        capalar.Add(y.Capa);
                        if (!malikler.Contains(m, StringComparer.Ordinal))
                        {
                            malikler.Add(m);
                        }
                    }
                    else
                    {
                        cozulemeyen++;
                    }
                }

                malikler.Sort((x, z) => kodlar.IndexOf(x).CompareTo(kodlar.IndexOf(z)));
                var bolge = new Bolge(b, i, koseler, GaussAreaCalculator.Compute(halka.Value).AreaM2Exact, malikler);
                bolge.Capalar.AddRange(capalar);
                bolgeler.Add(bolge);
            }
        }

        if (cozulemeyen > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                $"{cozulemeyen} kroki yazısı TAKBİS malikine eşlenemedi (ad, AD SOYAD (BABA ADI) etiketi, rehber kişi no veya ≥ 5 karakterlik tekil önek değil)."));
        }

        var disarida = yazilar.Count(y => !kullanilanYazi.Contains(y));
        if (disarida > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture, $"{disarida} kroki yazısı bölünen blokların içinde değil; dikkate alınmadı."));
        }

        // SAHA-016: katılımcılar (tam kapsamda TAKBİS'in tümü, aksi halde krokide yazısı olanlar) ve R = bölünen blokların Σ Gauss alanı.
        var tamKapsam = taslaklar.Count == bloklar.Count;
        var blokAlani = taslaklar.Keys.ToDictionary(b => b, b => GaussAreaCalculator.Compute(PolygonRing.FromOpen(bloklar[b].Koseler).Value));
        var r = blokAlani.Values.Sum(a => a.AreaM2);
        var yazili = kodlar.Where(m => bolgeler.Any(g => g.Malikler.Contains(m, StringComparer.Ordinal))).ToHashSet(StringComparer.Ordinal);
        var katilimci = kodlar.Where(m => tamKapsam || yazili.Contains(m)).ToList();
        var hakSahipleri = hak.Where(h => katilimci.Contains(h.Key.Malik, StringComparer.Ordinal))
            .OrderBy(h => h.Key.Kadastro, StringComparer.Ordinal).ThenBy(h => h.Key.Malik, StringComparer.Ordinal)
            .Select(h => (h.Key.Malik, h.Key.Kadastro, Net: h.Value)).ToList();
        var toplamNet = hakSahipleri.Sum(h => h.Net);
        if (hakSahipleri.Count == 0 || toplamNet <= 0m)
        {
            return Gecersiz("no positive owner right to distribute");
        }

        var hamHak = katilimci.ToDictionary(m => m, m => hakSahipleri.Where(h => h.Malik == m).Sum(h => h.Net), StringComparer.Ordinal);

        // Ham talepler: malikin net hakkı; birden çok bölgedeyse taslak alan oranında (LR).
        var pay = new Dictionary<(Bolge Bolge, string Malik), decimal>();
        foreach (var m in katilimci)
        {
            var bolum = BolgelereBol(hamHak[m], bolgeler.Where(g => g.Malikler.Contains(m, StringComparer.Ordinal)).ToList());
            if (bolum.IsFailure)
            {
                return Result<KrokiParselasyonOnizleme>.Failure(bolum.Error);
            }

            foreach (var (g, alan) in bolum.Value)
            {
                pay[(g, m)] = alan;
            }
        }

        // Kıymık emme: tüm bölgeleri yazılı ve Σ talep − blok alanı farkı kıymık eşiğinin altında olan blokta fark blok içinde orantılı
        // dağıtılır (anlamsız dm² müşterek hisse üretilmez — ör. .DNS ölçek farkı); Σ talep = blok alanı ise blok zaten tamdır.
        var emilen = new HashSet<int>();
        var emilenPay = new Dictionary<(Bolge Bolge, string Malik), decimal>();
        foreach (var b in taslaklar.Keys.Order())
        {
            var bb = bolgeler.Where(g => g.BlokSira == b).OrderBy(g => g.Sira).ToList();
            var talep = bb.Sum(g => g.Malikler.Sum(m => pay.GetValueOrDefault((g, m))));
            var fark = blokAlani[b].AreaM2 - talep;
            if (bb.Any(g => g.Malikler.Count == 0) || talep <= 0m || Math.Abs(fark) >= YaklasikYerKrokisiEslestirici.KiymikEsigiM2
                || bb.Any(g => g.Malikler.All(m => pay.GetValueOrDefault((g, m)) <= 0m)))
            {
                continue;
            }

            var bolgeHedefi = Bol(blokAlani[b].AreaM2, bb.Select((g, i) => (Sira(i), g.Malikler.Sum(m => pay.GetValueOrDefault((g, m))))).ToList());
            if (bolgeHedefi.IsFailure)
            {
                return Result<KrokiParselasyonOnizleme>.Failure(bolgeHedefi.Error);
            }

            for (var i = 0; i < bb.Count; i++)
            {
                var malikler = bb[i].Malikler.Where(m => pay.GetValueOrDefault((bb[i], m)) > 0m).ToList();
                var ic = Bol(bolgeHedefi.Value[i].Alan, malikler.Select(m => (m, pay[(bb[i], m)])).ToList());
                if (ic.IsFailure)
                {
                    return Result<KrokiParselasyonOnizleme>.Failure(ic.Error);
                }

                foreach (var (m, alan) in ic.Value)
                {
                    emilenPay[(bb[i], m)] = alan;
                }
            }

            emilen.Add(b);
        }

        // Kalan havuz: emilmeyen blokların alanı ↔ katılımcıların emilen bloklar dışındaki ham hakkı (krokide olmayanlar dahil).
        var kalanAlan = r - emilen.Sum(b => blokAlani[b].AreaM2);
        var kalanHak = katilimci.ToDictionary(m => m, m => hamHak[m] - pay.Where(x => x.Key.Malik == m && emilen.Contains(x.Key.Bolge.BlokSira)).Sum(x => x.Value),
            StringComparer.Ordinal);
        if (emilen.Count > 0 && (kalanHak.Values.Sum() <= 0m) != (kalanAlan <= 0m))
        {
            // Kalan hak yerleşemiyor (ör. krokide olmayan malik varken tüm bloklar emildi): emme kapatılır, tüm havuz dengelenir.
            emilen.Clear();
            emilenPay.Clear();
            kalanAlan = r;
            kalanHak = new Dictionary<string, decimal>(hamHak, StringComparer.Ordinal);
        }

        foreach (var b in emilen.Where(b => blokAlani[b].AreaM2 != bolgeler.Where(g => g.BlokSira == b).Sum(g => g.Malikler.Sum(m => pay.GetValueOrDefault((g, m))))))
        {
            var talep = bolgeler.Where(g => g.BlokSira == b).Sum(g => g.Malikler.Sum(m => pay.GetValueOrDefault((g, m))));
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                $"Blok {bloklar[b].Kimlik}: Σ net hak {talep:0.00} m² ≠ blok alanı {blokAlani[b].AreaM2:0.00} m² (fark {blokAlani[b].AreaM2 - talep:+0.00;-0.00;0.00} m²) kıymık eşiğinin ({YaklasikYerKrokisiEslestirici.KiymikEsigiM2:0.00} m²) altında; fark blok içinde hak oranında dağıtıldı (müşterek aktarım yapılmadı)."));
        }

        foreach (var x in emilenPay)
        {
            pay[x.Key] = x.Value;
        }

        // Kalan havuzun Largest Remainder ile 0.01 m² birimde emilmeyen blokların alanına dengelenmesi.
        var kalanToplam = kalanHak.Values.Sum();
        var dengeliKalan = katilimci.ToDictionary(m => m, _ => 0m, StringComparer.Ordinal);
        if (kalanAlan > 0m)
        {
            var lr = Bol(kalanAlan, katilimci.Select(m => (m, kalanHak[m])).ToList());
            if (lr.IsFailure)
            {
                return Result<KrokiParselasyonOnizleme>.Failure(lr.Error);
            }

            foreach (var (m, alan) in lr.Value)
            {
                dengeliKalan[m] = alan;
            }

            if (kalanToplam != kalanAlan)
            {
                uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Σ net hak {kalanToplam:0.00} m² ≠ bölünen blokların alanı {kalanAlan:0.00} m² (fark {kalanAlan - kalanToplam:+0.00;-0.00;0.00} m²); haklar Largest Remainder ile 0.01 m² birimde blok toplamına dengelendi."));
            }
        }

        // Emilmeyen bloklardaki talepler dengelenmiş kalan haktan yeniden kurulur.
        foreach (var m in katilimci)
        {
            var bolum = BolgelereBol(dengeliKalan[m], bolgeler.Where(g => !emilen.Contains(g.BlokSira) && g.Malikler.Contains(m, StringComparer.Ordinal)).ToList());
            if (bolum.IsFailure)
            {
                return Result<KrokiParselasyonOnizleme>.Failure(bolum.Error);
            }

            foreach (var (g, alan) in bolum.Value)
            {
                pay[(g, m)] = alan;
            }
        }

        // Malikin son hakkı (emilen talepler + dengelenmiş kalan) hak sahiplerine (kadastro kaynakları) net hak oranında bölünür.
        var dengeliHak = new List<(string Malik, string Kadastro, decimal Alan)>();
        foreach (var m in katilimci)
        {
            var son = dengeliKalan[m] + pay.Where(x => x.Key.Malik == m && emilen.Contains(x.Key.Bolge.BlokSira)).Sum(x => x.Value);
            var kaynaklar = hakSahipleri.Where(h => h.Malik == m).ToList();
            var bolum = Bol(son, kaynaklar.Select(h => (h.Kadastro, h.Net)).ToList());
            if (bolum.IsFailure)
            {
                return Result<KrokiParselasyonOnizleme>.Failure(bolum.Error);
            }

            dengeliHak.AddRange(bolum.Value.Select(x => (m, x.Kimlik, x.Alan)));
        }

        var cokParcali = kodlar.Count(m => bolgeler.Count(g => g.Malikler.Contains(m, StringComparer.Ordinal)) > 1);
        if (cokParcali > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture, $"{cokParcali} malik birden çok parçada; net hakkı taslak alan oranında parçalara bölündü."));
        }

        var krokideYok = kodlar.Count(m => !yazili.Contains(m));
        if (krokideYok > 0)
        {
            uyarilar.Add(tamKapsam
                ? string.Create(CultureInfo.InvariantCulture, $"TAKBİS'teki {krokideYok} malik krokide yok; hakkı açık kapasiteli parsellere müstakil/müşterek (pay/payda) olarak aktarıldı.")
                : string.Create(CultureInfo.InvariantCulture, $"TAKBİS'teki {krokideYok} malik krokide yok; krokide bölme çizgisi olmayan imar blokları korunduğundan hakkı bu parselasyonda dağıtılmadı."));
        }

        // Blok bazında hedefler (0.01 m²) ve kesin bölme.
        // Reviewer H2: bölünen blokların kendi kimlikleri serbest kalır; adadaki korunan parsel numaralarından sonra devam edilir.
        var bolunecek = taslaklar.Keys.Select(b => bloklar[b].Kimlik).ToHashSet(StringComparer.Ordinal);
        var mevcutKimlikler = girdi.ProjeParselleri.Select(p => p.ParselId).Where(id => !bolunecek.Contains(id)).ToHashSet(StringComparer.Ordinal);
        var yeniAdalar = YeniAdaNumaralari(taslaklar.Keys.Order().Select(b => AdaKodu(bloklar[b].Kimlik)).ToList(), mevcutKimlikler, girdi.BaslangicAdaNo);

        // Reviewer D6: yeniden numaralama (ve kullanılmış ada no atlaması) sessiz geçmez. Serbest etiket kişisel veri içerebileceğinden
        // yalnız rakamlı etiket yazılır (reviewer L2 ilkesi).
        foreach (var (kod, ada) in yeniAdalar.OrderBy(x => int.Parse(x.Value, NumberStyles.None, CultureInfo.InvariantCulture)))
        {
            var etiket = kod.Length is > 0 and <= 9 && kod.All(char.IsAsciiDigit) ? "\"" + kod + "\" etiketli blok" : "Ada numarası olmayan blok";
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                $"{etiket} yeni imar adası {ada} olarak numaralandı (Başlangıç Ada No {girdi.BaslangicAdaNo}; projede kullanılan ada numaraları atlanır)."));
        }
        var sonrakiNo = new Dictionary<string, int>(StringComparer.Ordinal);
        var kesinler = new List<(string Kod, string Blok, IReadOnlyList<GeodeticPoint> Koseler, decimal Hedef, Bolge Bolge)>();
        var bolunen = new List<string>();
        foreach (var (b, taslak) in taslaklar.OrderBy(t => t.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blok = bloklar[b];
            var blokGauss = blokAlani[b];
            var bb = bolgeler.Where(g => g.BlokSira == b).OrderBy(g => g.Sira).ToList();
            var hedefSonucu = Hedefler(bb, pay, blokGauss.AreaM2);
            if (hedefSonucu.IsFailure)
            {
                return BlokHatasi(hedefSonucu.Error, blok.Kimlik);
            }

            var hedef = hedefSonucu.Value;
            if (hedef.Any(h => h <= 0m))
            {
                return BlokHatasi(GecersizHata("a sketch part received no target area"), blok.Kimlik);
            }

            var talepToplami = bb.Sum(g => g.Malikler.Sum(m => pay.GetValueOrDefault((g, m))));
            if (bb.All(g => g.Malikler.Count > 0) && talepToplami != blokGauss.AreaM2)
            {
                uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Blok {blok.Kimlik}: Σ net hak {talepToplami:0.00} m² ≠ blok alanı {blokGauss.AreaM2:0.00} m² (fark {blokGauss.AreaM2 - talepToplami:+0.00;-0.00;0.00} m²); sığmayan / artan hak müstakil + müşterek (pay/payda) dağıtımla dengelendi."));
            }

            var kesin = bolucu.KesinBol(blok.Koseler, taslak.SiraliCizgiler, hedef, cancellationToken);
            if (kesin.IsFailure)
            {
                return BlokHatasi(kesin.Error, blok.Kimlik);
            }

            var exactToplam = 0m;
            var ada = yeniAdalar.GetValueOrDefault(AdaKodu(blok.Kimlik), AdaKodu(blok.Kimlik));
            for (var i = 0; i < bb.Count; i++)
            {
                var koseler = kesin.Value[i];
                var halka = PolygonRing.FromOpen(koseler);
                if (halka.IsFailure)
                {
                    return BlokHatasi(halka.Error, blok.Kimlik);
                }

                var gauss = GaussAreaCalculator.Compute(halka.Value);
                exactToplam += gauss.AreaM2Exact;
                // Architect R3: hak eşleştirmesi parselin 2 ondalık Gauss alanını kullanır; hedefe birebir eşit olmalıdır.
                if (Math.Abs(gauss.AreaM2Exact - hedef[i]) > AlanToleransiM2 || gauss.AreaM2 != hedef[i])
                {
                    // Reviewer D4: 2 ondalık eşitsizlikte rapor edilen fark yuvarlanmış alanın farkıdır.
                    return AlanSapmasi(gauss.AreaM2 != hedef[i] ? gauss.AreaM2 - hedef[i] : gauss.AreaM2Exact - hedef[i]);
                }

                var kod = SonrakiKod(ada, mevcutKimlikler, sonrakiNo);
                if (kod is null)
                {
                    return BlokHatasi(GecersizHata("new parcel code collides with an existing parcel id"), blok.Kimlik);
                }

                if (bb[i].Capalar.Any(c => !CadLabelMatcher.StrictlyInside(Kapali(koseler), c)))
                {
                    uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{kod}: malik yazısı kesin parsel sınırının dışında kaldı (taslak {bb[i].TaslakAlan:0.00} m² → kesin {hedef[i]:0.00} m²); kesim krokiden belirgin kaydı, mühendis kontrol etmeli."));
                }

                if (bb[i].Malikler.Count == 0)
                {
                    uyarilar.Add($"{kod} parseline TAKBİS malikine eşlenen yazı düşmedi; hedef alanı blok kalanından (taslak alan oranıyla) verildi, krokide olmayan / artan haklar müstakil veya müşterek olarak yazıldı (mühendis kontrol etmeli).");
                }
                else if (bb[i].Malikler.Count > 1)
                {
                    uyarilar.Add(string.Create(CultureInfo.InvariantCulture, $"{kod} parselinde {bb[i].Malikler.Count} malik var (hisseli parsel; alan hak oranında paylaştırıldı)."));
                }

                kesinler.Add((kod, blok.Kimlik, koseler, hedef[i], bb[i]));
            }

            if (Math.Abs(exactToplam - blokGauss.AreaM2Exact) > AlanToleransiM2)
            {
                return AlanSapmasi(exactToplam - blokGauss.AreaM2Exact);
            }

            bolunen.Add(blok.Kimlik);
        }

        // SAHA-016: müstakil (TAM) + müşterek (pay/payda) dağıtım.
        var dagitim = MustakilMusterekDagitici.Dagit(
            kesinler.Select(k => new DagitimParseli(k.Kod, k.Hedef,
                k.Bolge.Malikler.Where(m => pay.GetValueOrDefault((k.Bolge, m)) > 0m).Select(m => new ParselTalebi(m, pay[(k.Bolge, m)])).ToList())
            { Grup = k.Blok }).ToList(),
            dengeliHak.Select(h => new DagitimHakki(h.Malik, h.Kadastro, h.Alan) { MustakilOlabilir = mustakilOlabilir[(h.Malik, h.Kadastro)] }).ToList());
        if (dagitim.IsFailure)
        {
            return Result<KrokiParselasyonOnizleme>.Failure(dagitim.Error);
        }

        if (dagitim.Value.Md17IhlalliParseller.Count > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                $"{dagitim.Value.Md17IhlalliParseller.Count} müstakil parsel ({string.Join(", ", dagitim.Value.Md17IhlalliParseller)}) AAD Md. 17/1-(d) gereği muvafakat gerektiriyor (çok malikli kadastro parselinin muvafakatsız paydaşı tek başına); TAKBİS muvafakatını girin veya krokiyi düzeltin, aksi halde Hak Eşleştirme kritik bulgu verir."));
        }

        var parseller = kesinler.Select((k, i) =>
        {
            var d = dagitim.Value.Parseller[i];
            var gauss = GaussAreaCalculator.Compute(PolygonRing.FromOpen(k.Koseler).Value);
            return new KrokiParseli(k.Kod, k.Blok, k.Koseler, k.Hedef, gauss.AreaM2, decimal.Round(k.Bolge.TaslakAlan, 2, MidpointRounding.AwayFromZero),
                d.Hissedarlar.Select(h => h.MalikId).ToList().AsReadOnly()) { Hissedarlar = d.Hissedarlar };
        }).ToList();
        var satirlar = dagitim.Value.Satirlar.Select(x => new KrokiEslesmeGirdisi(x.MalikId, x.KadastroParselId, x.ParselKodu, x.AlanM2)).ToList();

        return Result<KrokiParselasyonOnizleme>.Success(new KrokiParselasyonOnizleme(
            parseller.AsReadOnly(), bolunen.AsReadOnly(), satirlar.AsReadOnly(), Farklar(satirlar, girdi), uyarilar.AsReadOnly()));
    }

    /// <summary>
    /// SAHA-016 blok hedefleri (0.01 m², Σ = blok alanı): Σ talep = blok ve yazısız bölge yoksa hedef = talep; Σ talep &lt; blok ise
    /// yazılı bölgeler talebini alır, kalan yazısız bölgelere taslak alan oranında (LR), yazısız bölge yoksa en büyük talepli (eşitte ilk)
    /// bölgeye eklenir; aksi halde ağırlıklar (talep; yazısız bölgede taslak alan) blok alanına orantılı LR ile dengelenir.
    /// </summary>
    private static Result<IReadOnlyList<decimal>> Hedefler(IReadOnlyList<Bolge> bb, Dictionary<(Bolge, string), decimal> pay, decimal blokAlani)
    {
        var talep = bb.Select(g => g.Malikler.Sum(m => pay.GetValueOrDefault((g, m)))).ToList();
        var yazisiz = Enumerable.Range(0, bb.Count).Where(i => bb[i].Malikler.Count == 0).ToList();
        var toplam = talep.Sum();
        var hedef = talep.ToList();
        if (toplam == blokAlani && yazisiz.Count == 0)
        {
            return Result<IReadOnlyList<decimal>>.Success(hedef);
        }

        if (toplam > 0m && toplam < blokAlani && talep.Where((_, i) => !yazisiz.Contains(i)).All(t => t > 0m))
        {
            var kalan = blokAlani - toplam;
            if (yazisiz.Count == 0)
            {
                var enBuyuk = talep.IndexOf(talep.Max());
                hedef[enBuyuk] += kalan;
                return Result<IReadOnlyList<decimal>>.Success(hedef);
            }

            var bolum = Bol(kalan, yazisiz.Select(i => (Sira(i), decimal.Round(bb[i].TaslakAlan, 2, MidpointRounding.AwayFromZero))).ToList());
            if (bolum.IsFailure)
            {
                return Result<IReadOnlyList<decimal>>.Failure(bolum.Error);
            }

            // Reviewer D3: kalan yazısız bölgelerin her birine pozitif alan veremeyecek kadar küçükse orantılı dengelemeye düşülür.
            if (bolum.Value.All(x => x.Alan > 0m))
            {
                foreach (var (kimlik, alan) in bolum.Value)
                {
                    hedef[yazisiz.Single(i => Sira(i) == kimlik)] = alan;
                }

                return Result<IReadOnlyList<decimal>>.Success(hedef);
            }
        }

        var agirlik = bb.Select((g, i) => g.Malikler.Count == 0 ? decimal.Round(g.TaslakAlan, 2, MidpointRounding.AwayFromZero) : talep[i]).ToList();
        if (agirlik.Sum() <= 0m)
        {
            agirlik = bb.Select(g => decimal.Round(g.TaslakAlan, 2, MidpointRounding.AwayFromZero)).ToList();
        }

        var orantili = Bol(blokAlani, agirlik.Select((a, i) => (Sira(i), a)).ToList());
        return orantili.IsFailure
            ? Result<IReadOnlyList<decimal>>.Failure(orantili.Error)
            : Result<IReadOnlyList<decimal>>.Success(orantili.Value.Select(x => x.Alan).ToList());
    }

    /// <summary>Malikin alanını bölgelerine taslak alan oranında (LR, 0.01 m²) böler; tek bölgede tamamı; bölge yoksa boş.</summary>
    private static Result<IReadOnlyList<(Bolge Bolge, decimal Alan)>> BolgelereBol(decimal alan, IReadOnlyList<Bolge> bolgeler)
    {
        if (bolgeler.Count == 0)
        {
            return Result<IReadOnlyList<(Bolge, decimal)>>.Success([]);
        }

        var bolum = Bol(alan, bolgeler.Select((g, i) => (Sira(i), g.TaslakAlan)).ToList());
        return bolum.IsFailure
            ? Result<IReadOnlyList<(Bolge, decimal)>>.Failure(bolum.Error)
            : Result<IReadOnlyList<(Bolge, decimal)>>.Success(bolgeler.Select((g, i) => (g, bolum.Value[i].Alan)).ToList());
    }

    /// <summary>Alanı ağırlıklarla (hepsi sıfırsa eşit) 0.01 m² birimde LR böler; tek kalemde alanın tamamı. Sonuç girdi sırasıyla.</summary>
    private static Result<IReadOnlyList<(string Kimlik, decimal Alan)>> Bol(decimal alan, IReadOnlyList<(string Kimlik, decimal Agirlik)> kalemler)
    {
        if (kalemler.Count == 1)
        {
            return Result<IReadOnlyList<(string, decimal)>>.Success([(kalemler[0].Kimlik, alan)]);
        }

        var toplam = kalemler.Sum(k => k.Agirlik);
        var agirliklar = kalemler.Select(k => new AllocationWeight(k.Kimlik, toplam > 0m
            ? Fraction.FromDecimal(k.Agirlik) / Fraction.FromDecimal(toplam)
            : new Fraction(1, kalemler.Count))).ToList();
        var r = LargestRemainderAllocator.AllocateAlanM2(alan, agirliklar);
        return r.IsFailure
            ? Result<IReadOnlyList<(string, decimal)>>.Failure(r.Error)
            : Result<IReadOnlyList<(string, decimal)>>.Success(kalemler.Select(k => (k.Kimlik, r.Value.Single(s => s.Id == k.Kimlik).ToAlanM2())).ToList());
    }

    private static IReadOnlyList<KrokiFarki> Farklar(List<KrokiEslesmeGirdisi> satirlar, KrokiParselasyonGirdisi girdi)
    {
        var sonuc = new List<KrokiFarki>();
        foreach (var k in girdi.Takbis.OrderBy(k => k.ParselId, StringComparer.Ordinal))
        {
            foreach (var m in k.Malikler.OrderBy(m => m.MalikId, StringComparer.Ordinal))
            {
                var kroki = satirlar.Where(s => s.MalikKodu == m.MalikId && s.KadastroParselKimligi == k.ParselId).Sum(s => s.AlanM2);
                decimal? net = girdi.Dopo is { } dopo ? HakEslestirmeServisi.NetHakM2(HakEslestirmeServisi.GirenAlanM2(k.TapuAlaniM2, m.Hisse), dopo) : null;
                sonuc.Add(new KrokiFarki(m.MalikId, k.ParselId, kroki, net));
            }
        }

        return sonuc.AsReadOnly();
    }

    /// <summary>Ada kodu: blok kimliğinin ilk <c>/</c> öncesi (yoksa tamamı).</summary>
    internal static string AdaKodu(string blokKimligi)
    {
        var t = blokKimligi.Trim();
        var i = t.IndexOf('/', StringComparison.Ordinal);
        return i > 0 ? t[..i] : t;
    }

    /// <summary>SAHA-021: imar ada numarası olarak korunan etiket — en çok 9 rakam ve değeri <c>≥ 100</c> (<c>101</c>, <c>436</c>).</summary>
    internal static bool AdaNumarasiMi(string ada) =>
        ada.Length is > 0 and <= 9 && ada.All(char.IsAsciiDigit) && int.Parse(ada, NumberStyles.None, CultureInfo.InvariantCulture) >= 100;

    /// <summary>
    /// SAHA-021: bölünen blokların ada kodu → yeni imar ada numarası. Ada numarası olan (<c>≥ 100</c>) kod aynen kalır (haritada yok).
    /// Boş, sayısal olmayan (<c>ADA1</c>) veya <c>100</c>'den küçük (<c>"1"</c>, <c>"2"</c>) kodlar <paramref name="baslangic"/>'tan
    /// (<c>101</c>) itibaren sıralı numara alır: önce sayısal kodlar değer sırasıyla, sonra diğerleri blok sırasıyla; aynı kod tek ada
    /// olur. Projede (bölünmeyen parsel kimliklerinde) veya korunan bloklarda zaten kullanılan ada numarası atlanır.
    /// </summary>
    public static IReadOnlyDictionary<string, string> YeniAdaNumaralari(IReadOnlyList<string> adaKodlari, IEnumerable<string> mevcutKimlikler, int baslangic)
    {
        var kullanilan = mevcutKimlikler.Select(AdaKodu).Concat(adaKodlari.Where(AdaNumarasiMi))
            .Where(a => a.Length is > 0 and <= 9 && a.All(char.IsAsciiDigit))
            .Select(a => int.Parse(a, NumberStyles.None, CultureInfo.InvariantCulture))
            .ToHashSet();
        var yeniden = adaKodlari.Where(a => !AdaNumarasiMi(a)).Distinct(StringComparer.Ordinal)
            .Select((a, i) => (Kod: a, Sira: i, Sayi: a.Length is > 0 and <= 9 && a.All(char.IsAsciiDigit) ? int.Parse(a, NumberStyles.None, CultureInfo.InvariantCulture) : (int?)null))
            .OrderBy(x => x.Sayi is null).ThenBy(x => x.Sayi).ThenBy(x => x.Sira)
            .ToList();
        var sonuc = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = baslangic;
        foreach (var (kod, _, _) in yeniden)
        {
            while (kullanilan.Contains(n))
            {
                n++;
            }

            kullanilan.Add(n);
            sonuc[kod] = n.ToString(CultureInfo.InvariantCulture);
            n++;
        }

        return sonuc;
    }

    /// <summary>
    /// Adadaki sonraki parsel kodu <c>{ada}/{n}</c>: n, adada korunan parsellerin (<c>ada/sayı</c>) en büyük numarasından sonra başlar ve
    /// bu çalıştırmada ardışık artar. Çakışma veya 64 karakter aşımında null (hata; "~n" eki üretilmez — reviewer H2, architect O5).
    /// </summary>
    private static string? SonrakiKod(string ada, HashSet<string> mevcut, Dictionary<string, int> sonraki)
    {
        if (!sonraki.TryGetValue(ada, out var n))
        {
            var onek = ada + "/";
            n = mevcut.Where(id => id.StartsWith(onek, StringComparison.Ordinal))
                .Select(id => int.TryParse(id.AsSpan(onek.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var x) ? x : 0)
                .DefaultIfEmpty(0).Max() + 1;
        }

        sonraki[ada] = n + 1;
        var kod = string.Create(CultureInfo.InvariantCulture, $"{ada}/{n}");
        return kod.Length > 64 || mevcut.Contains(kod) ? null : kod;
    }

    private static string Sira(int i) => "p" + i.ToString("D4", CultureInfo.InvariantCulture);

    private static IReadOnlyList<GeodeticPoint> Kapali(IReadOnlyList<GeodeticPoint> acik) =>
        acik.Count > 0 && acik[0] != acik[^1] ? [.. acik, acik[0]] : acik;

    private static Result<KrokiParselasyonOnizleme> BlokHatasi(AppError e, string blok) =>
        Result<KrokiParselasyonOnizleme>.Failure(e with { TechnicalMessage = e.TechnicalMessage + " (block " + blok + ")" });

    private static Result<KrokiParselasyonOnizleme> AlanSapmasi(decimal sapma)
    {
        var e = ErrorCatalog.Get(ErrorCodes.KuantAlanSapmasi);
        return Result<KrokiParselasyonOnizleme>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage
                .Replace("{delta}", sapma.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{tolerance}", AlanToleransiM2.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
        });
    }

    private static AppError GecersizHata(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.TapuDagitimGirdisiGecersiz);
        return e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", neden, StringComparison.Ordinal) };
    }

    private static Result<KrokiParselasyonOnizleme> Gecersiz(string neden) => Result<KrokiParselasyonOnizleme>.Failure(GecersizHata(neden));
}

/// <summary>
/// SAHA-015: onaylanan kroki parselasyon önizlemesini projenin parsel listesine çevirir (<see cref="ParselGeometrileriAyarlaKomutu"/>).
/// Bölünen imar blokları yerinde yeni parsellerle değiştirilir; diğer parseller (kadastro, yol, bölünmeyen bloklar) korunur.
/// <c>.DNS</c> ileri dönüşümü etkinse komut Deprem Öncesi (<see cref="ParselKoordinatSistemi.KaynakCad"/>) girdisi bekler: korunan
/// parseller <see cref="ProjeBelgesi.DepremOncesiParseller"/>'den, yeni köşeler ise mevcut köşeyse orijinalinden, değilse
/// <see cref="DonusumKatsayilari.TersUygula"/> ile verilir ve ileri dönüşüm sonrası alanlar hedefe karşı yeniden doğrulanır.
/// </summary>
public static class KrokiParselasyonUygulayici
{
    public static Result<ParselGeometrileriAyarlaKomutu> KomutOlustur(ProjeBelgesi belge, KrokiParselasyonOnizleme onizleme)
    {
        ArgumentNullException.ThrowIfNull(belge);
        ArgumentNullException.ThrowIfNull(onizleme);
        if (onizleme.Parseller.Count == 0)
        {
            return Gecersiz("preview has no parcels");
        }

        var k = belge.IleriDonusum;
        var kaynak = k is null ? belge.Parseller : belge.DepremOncesiParseller ?? belge.Parseller;
        var bolunen = onizleme.BolunenBloklar.ToHashSet(StringComparer.Ordinal);
        var projedekiBloklar = belge.Parseller.Where(p => p.Rol == ParselRolu.Imar && bolunen.Contains(p.ParselId)).Select(p => p.ParselId).ToHashSet(StringComparer.Ordinal);
        if (belge.Parseller.Any(p => p.Rol == ParselRolu.Imar) && projedekiBloklar.Count != bolunen.Count)
        {
            return Gecersiz("subdivided block is no longer in the project (preview is stale)");
        }

        // Aktif → Deprem Öncesi köşe eşlemesi (yalnız ileri dönüşümde; architect O2: yalnız ilgili bölünen bloğun kendi köşeleri) —
        // blok köşesi birebir orijinaline döner, yeni kesim köşesi ters dönüşümle verilir.
        var orijinal = new Dictionary<string, Dictionary<GeodeticPoint, GeodeticPoint>>(StringComparer.Ordinal);
        if (k is not null)
        {
            for (var i = 0; i < belge.Parseller.Count && i < kaynak.Count; i++)
            {
                var aktif = belge.Parseller[i];
                if (aktif.Rol != ParselRolu.Imar || !bolunen.Contains(aktif.ParselId))
                {
                    continue;
                }

                var eslem = new Dictionary<GeodeticPoint, GeodeticPoint>();
                for (var j = 0; j < aktif.Koseler.Count && j < kaynak[i].Koseler.Count; j++)
                {
                    eslem.TryAdd(aktif.Koseler[j], kaynak[i].Koseler[j]);
                }

                orijinal[aktif.ParselId] = eslem;
            }
        }

        var yeni = new List<ParselGeometrisi>();
        foreach (var p in onizleme.Parseller)
        {
            var eslem = orijinal.GetValueOrDefault(p.BlokKimligi);
            var koseler = k is null ? p.Koseler : p.Koseler.Select(v => eslem is not null && eslem.TryGetValue(v, out var o) ? o : k.TersUygula(v)).ToArray();
            if (k is not null)
            {
                var ileri = PolygonRing.FromOpen(koseler.Select(k.Uygula).ToArray());
                if (ileri.IsFailure)
                {
                    return Result<ParselGeometrileriAyarlaKomutu>.Failure(ileri.Error);
                }

                var sapma = GaussAreaCalculator.Compute(ileri.Value).AreaM2Exact - p.HedefAlanM2;
                if (Math.Abs(sapma) > KrokiParselasyonMotoru.AlanToleransiM2)
                {
                    var e = ErrorCatalog.Get(ErrorCodes.KuantAlanSapmasi);
                    return Result<ParselGeometrileriAyarlaKomutu>.Failure(e with
                    {
                        TechnicalMessage = e.TechnicalMessage
                            .Replace("{delta}", sapma.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                            .Replace("{tolerance}", KrokiParselasyonMotoru.AlanToleransiM2.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                    });
                }
            }

            yeni.Add(new ParselGeometrisi(p.Kod, ParselRolu.Imar, koseler));
        }

        var liste = new List<ParselGeometrisi>();
        var eklenen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < kaynak.Count; i++)
        {
            var aktif = belge.Parseller[i];
            if (aktif.Rol == ParselRolu.Imar && bolunen.Contains(aktif.ParselId))
            {
                if (eklenen.Add(aktif.ParselId))
                {
                    liste.AddRange(yeni.Where((_, j) => onizleme.Parseller[j].BlokKimligi == aktif.ParselId));
                }

                continue;
            }

            liste.Add(kaynak[i]);
        }

        // Krokiden gelen (projede olmayan) bloklar sona eklenir.
        liste.AddRange(yeni.Where((_, j) => !projedekiBloklar.Contains(onizleme.Parseller[j].BlokKimligi)));
        return Result<ParselGeometrileriAyarlaKomutu>.Success(new ParselGeometrileriAyarlaKomutu(liste) { KrokiParselasyonu = true });
    }

    private static Result<ParselGeometrileriAyarlaKomutu> Gecersiz(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz);
        return Result<ParselGeometrileriAyarlaKomutu>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage.Replace("{command}", "KrokiParselasyonu", StringComparison.Ordinal).Replace("{reason}", neden, StringComparison.Ordinal),
        });
    }
}

/// <summary>
/// SAHA-015 (reviewer M1 / architect O1): mühendisin "✅ Parselasyon Çizimini Onayla ve Uygula" eylemi. Önizleme anındaki
/// <see cref="ProjeBelgesi.RevizyonKimligi"/> ile güncel revizyon aynı olmalıdır (bayat önizleme reddi); parsel listesi
/// <see cref="KrokiParselasyonUygulayici"/> ile CommandBus içinde kurulur ve <see cref="ParselGeometrileriAyarlaKomutu"/> denetiminden
/// geçer. Böylece her ret (bayat önizleme, DNS sonrası alan sapması, geçersiz halka) denetim kaydına yazılır. Ayrıntıda yalnız sayılar.
/// </summary>
public sealed record KrokiParselasyonuUygulaKomutu(KrokiParselasyonOnizleme Onizleme, Domain.Revizyon.RevisionId OnizlemeRevizyonu) : IProjeKomutu
{
    /// <summary>Teknik neden: önizlemeden sonra proje içeriği değişti.</summary>
    public const string BayatNedeni = "project changed after the sketch preview (stale preview)";

    public string Ad => "KrokiParselasyonuUygula";

    public string? Ayrinti => Onizleme is null
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"parselSayisi={Onizleme.Parseller.Count};blokSayisi={Onizleme.BolunenBloklar.Count};kaynak=yaklasikYerKrokisi{(Onizleme.ElleDuzenlendi ? ";dagilim=elle" : string.Empty)}");

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        ArgumentNullException.ThrowIfNull(mevcut);
        if (Onizleme is null || OnizlemeRevizyonu is null)
        {
            return KomutHatasi.Gecersiz(Ad, "preview is missing");
        }

        if (mevcut.RevizyonKimligi() != OnizlemeRevizyonu)
        {
            return KomutHatasi.Gecersiz(Ad, BayatNedeni);
        }

        var komut = KrokiParselasyonUygulayici.KomutOlustur(mevcut, Onizleme);
        return komut.IsFailure ? Result<ProjeBelgesi>.Failure(komut.Error) : komut.Value.Uygula(mevcut);
    }
}
