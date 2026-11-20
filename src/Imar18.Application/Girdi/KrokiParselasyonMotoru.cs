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
    decimal? Dopo);

/// <summary>
/// Kesin alanlı yeni <c>TK_Oluşan</c> parseli. <see cref="Koseler"/> aktif sistemde açık halkadır; <see cref="HedefAlanM2"/> blok
/// toplamına dengelenmiş kesin hak alanı, <see cref="GaussAlanM2"/> geometrinin 2 ondalık Gauss alanı, <see cref="TaslakAlanM2"/>
/// mühendisin kabaca çizdiği bölgenin alanıdır. <see cref="Malikler"/> TAKBİS kimlikleridir (yerel ekran; denetime yazılmaz).
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
    /// <summary>Etiketteki malik görünümü: baba adısız ad soyad; birden çok malikte "+n"; malik yoksa "Malik ?".</summary>
    public string MalikGorunumu() => Malikler.Count == 0
        ? "Malik ?"
        : MalikEtiketi.Ayristir(Malikler[0]).AdSoyad + (Malikler.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $" +{Malikler.Count - 1}") : string.Empty);

    /// <summary>Tuval etiketi: <c>Ada/Parsel • Malik Adı • Alan m²</c>.</summary>
    public string Etiket() => Etiket(Kod, MalikGorunumu(), GaussAlanM2);

    /// <summary>Etiket biçimi (uygulanmış katmanda alan projeden yeniden hesaplanınca da kullanılır — reviewer L1).</summary>
    public static string Etiket(string kod, string malik, decimal alanM2) => string.Create(CultureInfo.InvariantCulture, $"{kod} • {malik} • {alanM2:0.00} m²");
}

/// <summary>Kroki parselasyon önizlemesi (projeye yazılmamış taslak): yeni parseller, bölünen bloklar, Kroki satırları, farklar ve uyarılar.</summary>
public sealed record KrokiParselasyonOnizleme(
    IReadOnlyList<KrokiParseli> Parseller,
    IReadOnlyList<string> BolunenBloklar,
    IReadOnlyList<KrokiEslesmeGirdisi> Satirlar,
    IReadOnlyList<KrokiFarki> Farklar,
    IReadOnlyList<string> Uyarilar)
{
    /// <summary>Yeni parsellerin hedef alan toplamı (= bölünen blokların Gauss toplamı).</summary>
    public decimal ToplamAlanM2 => Parseller.Sum(p => p.HedefAlanM2);

    /// <summary>Kısa özet: blok/parsel sayısı ve alan toplamı.</summary>
    public string Ozet() => string.Create(CultureInfo.InvariantCulture,
        $"{BolunenBloklar.Count} blok · {Parseller.Count} parsel · Σ {ToplamAlanM2:0.00} m² · en büyük sapma {Parseller.Select(p => Math.Abs(p.GaussAlanM2 - p.HedefAlanM2)).DefaultIfEmpty(0m).Max():0.00} m²");

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
/// <item><b>Hedef alanlar</b>: malik net hakkı = Σ (giren − DOP) (HakEslestirmeServisi yuvarlaması; DOPO yoksa giren). Birden çok
/// bölgedeki malikin hakkı taslak alan oranında bölünür; bölge ağırlığı malik paylarının toplamıdır (malik yoksa taslak alan). Her
/// blokta ağırlıklar <see cref="LargestRemainderAllocator"/> ile blok Gauss alanına 0.01 m² birimde tam dengelenir.</item>
/// <item><b>Kesin bölme</b>: <see cref="IKrokiBlokBolucu.KesinBol"/> her kesimi mühendisin çizgisine paralel kaydırır; her parselin
/// decimal Gauss alanı hedefe <see cref="AlanToleransiM2"/> içinde, parseller toplamı blok alanına eşit olmalıdır (aksi
/// <c>ERR-KUANT-001</c>).</item>
/// <item><b>Kodlar</b>: <c>{ada}/{sıra}</c>; ada = blok kimliğinin ilk <c>/</c> öncesi; sıra taslak bölge sırası (batıdan doğuya).</item>
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

        // Malik hakları (HakEslestirmeServisi ile aynı yuvarlama).
        var kodlar = girdi.Takbis.SelectMany(k => k.Malikler.Select(m => m.MalikId)).Distinct(StringComparer.Ordinal).ToList();
        var hak = new Dictionary<(string Malik, string Kadastro), decimal>();
        foreach (var k in girdi.Takbis)
        {
            foreach (var m in k.Malikler)
            {
                var giren = decimal.Round((Fraction.FromDecimal(k.TapuAlaniM2) * m.Hisse).ToDecimal(6), 2, MidpointRounding.AwayFromZero);
                var net = girdi.Dopo is { } dopo ? giren - decimal.Round(giren * dopo, 2, MidpointRounding.AwayFromZero) : giren;
                hak[(m.MalikId, k.ParselId)] = hak.GetValueOrDefault((m.MalikId, k.ParselId)) + net;
            }
        }

        var malikHakki = kodlar.ToDictionary(m => m, m => hak.Where(h => h.Key.Malik == m).Sum(h => h.Value), StringComparer.Ordinal);
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

        // Birden çok bölgedeki malikin hakkı taslak alan oranında bölünür.
        var pay = new Dictionary<(Bolge Bolge, string Malik), decimal>();
        foreach (var m in kodlar)
        {
            var mb = bolgeler.Where(g => g.Malikler.Contains(m, StringComparer.Ordinal)).ToList();
            if (mb.Count == 0)
            {
                continue;
            }

            if (mb.Count == 1)
            {
                pay[(mb[0], m)] = malikHakki[m];
                continue;
            }

            var toplamTaslak = Fraction.FromDecimal(mb.Sum(g => g.TaslakAlan));
            var bolum = LargestRemainderAllocator.AllocateAlanM2(malikHakki[m],
                mb.Select((g, i) => new AllocationWeight(Sira(i), Fraction.FromDecimal(g.TaslakAlan) / toplamTaslak)).ToList());
            if (bolum.IsFailure)
            {
                return Result<KrokiParselasyonOnizleme>.Failure(bolum.Error);
            }

            for (var i = 0; i < mb.Count; i++)
            {
                pay[(mb[i], m)] = bolum.Value.Single(s => s.Id == Sira(i)).ToAlanM2();
            }
        }

        var cokParcali = kodlar.Count(m => bolgeler.Count(g => g.Malikler.Contains(m, StringComparer.Ordinal)) > 1);
        if (cokParcali > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture, $"{cokParcali} malik birden çok parçada; net hakkı taslak alan oranında parçalara bölündü."));
        }

        var krokideYok = kodlar.Count(m => !bolgeler.Any(g => g.Malikler.Contains(m, StringComparer.Ordinal)));
        if (krokideYok > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture, $"TAKBİS'teki {krokideYok} malik krokide yok; hakkı bu parselasyonda dağıtılmadı."));
        }

        // Blok bazında hedefler (LR, 0.01 m²) ve kesin bölme.
        // Reviewer H2: bölünen blokların kendi kimlikleri serbest kalır; adadaki korunan parsel numaralarından sonra devam edilir.
        var bolunecek = taslaklar.Keys.Select(b => bloklar[b].Kimlik).ToHashSet(StringComparer.Ordinal);
        var mevcutKimlikler = girdi.ProjeParselleri.Select(p => p.ParselId).Where(id => !bolunecek.Contains(id)).ToHashSet(StringComparer.Ordinal);
        var sonrakiNo = new Dictionary<string, int>(StringComparer.Ordinal);
        var parseller = new List<KrokiParseli>();
        var satirlar = new List<KrokiEslesmeGirdisi>();
        var bolunen = new List<string>();
        foreach (var (b, taslak) in taslaklar.OrderBy(t => t.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blok = bloklar[b];
            var blokGauss = GaussAreaCalculator.Compute(PolygonRing.FromOpen(blok.Koseler).Value);
            var bb = bolgeler.Where(g => g.BlokSira == b).OrderBy(g => g.Sira).ToList();
            var agirlik = bb.Select(g => g.Malikler.Count == 0
                ? decimal.Round(g.TaslakAlan, 2, MidpointRounding.AwayFromZero)
                : g.Malikler.Sum(m => pay[(g, m)])).ToList();
            if (agirlik.Sum() <= 0m)
            {
                agirlik = bb.Select(g => decimal.Round(g.TaslakAlan, 2, MidpointRounding.AwayFromZero)).ToList();
            }

            var toplamAgirlik = Fraction.FromDecimal(agirlik.Sum());
            var hedefler = LargestRemainderAllocator.AllocateAlanM2(blokGauss.AreaM2,
                agirlik.Select((a, i) => new AllocationWeight(Sira(i), Fraction.FromDecimal(a) / toplamAgirlik)).ToList());
            if (hedefler.IsFailure)
            {
                return Result<KrokiParselasyonOnizleme>.Failure(hedefler.Error);
            }

            var hedef = bb.Select((_, i) => hedefler.Value.Single(s => s.Id == Sira(i)).ToAlanM2()).ToList();
            if (hedef.Any(h => h <= 0m))
            {
                return BlokHatasi(GecersizHata("a sketch part received no target area"), blok.Kimlik);
            }

            if (bb.All(g => g.Malikler.Count > 0) && agirlik.Sum() != blokGauss.AreaM2)
            {
                uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Blok {blok.Kimlik}: Σ net hak {agirlik.Sum():0.00} m² ≠ blok alanı {blokGauss.AreaM2:0.00} m² (fark {blokGauss.AreaM2 - agirlik.Sum():+0.00;-0.00;0.00} m²); hedefler blok alanına orantılı dengelendi."));
            }

            var kesin = bolucu.KesinBol(blok.Koseler, taslak.SiraliCizgiler, hedef, cancellationToken);
            if (kesin.IsFailure)
            {
                return BlokHatasi(kesin.Error, blok.Kimlik);
            }

            var exactToplam = 0m;
            var ada = AdaKodu(blok.Kimlik);
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
                if (Math.Abs(gauss.AreaM2Exact - hedef[i]) > AlanToleransiM2)
                {
                    return AlanSapmasi(gauss.AreaM2Exact - hedef[i]);
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
                parseller.Add(new KrokiParseli(kod, blok.Kimlik, koseler, hedef[i], gauss.AreaM2, decimal.Round(bb[i].TaslakAlan, 2, MidpointRounding.AwayFromZero), bb[i].Malikler.AsReadOnly()));
                if (bb[i].Malikler.Count == 0)
                {
                    uyarilar.Add($"{kod} parseline TAKBİS malikine eşlenen yazı düşmedi; hedef alanı taslak alan oranıyla verildi, Kroki satırı üretilmedi (mühendis tamamlamalı).");
                    continue;
                }

                if (bb[i].Malikler.Count > 1)
                {
                    uyarilar.Add(string.Create(CultureInfo.InvariantCulture, $"{kod} parselinde {bb[i].Malikler.Count} malik var (hisseli parsel; alan hak oranında paylaştırıldı)."));
                }

                var satir = Satirlar(kod, hedef[i], bb[i], pay, hak);
                if (satir.IsFailure)
                {
                    return Result<KrokiParselasyonOnizleme>.Failure(satir.Error);
                }

                satirlar.AddRange(satir.Value);
            }

            if (Math.Abs(exactToplam - blokGauss.AreaM2Exact) > AlanToleransiM2)
            {
                return AlanSapmasi(exactToplam - blokGauss.AreaM2Exact);
            }

            bolunen.Add(blok.Kimlik);
        }

        return Result<KrokiParselasyonOnizleme>.Success(new KrokiParselasyonOnizleme(
            parseller.AsReadOnly(), bolunen.AsReadOnly(), satirlar.AsReadOnly(), Farklar(satirlar, girdi), uyarilar.AsReadOnly()));
    }

    /// <summary>Parsel hedefi malik(ler)e (hak payı oranında), malik payı kadastro parsellerine (net hak oranında) LR ile bölünür.</summary>
    private static Result<IReadOnlyList<KrokiEslesmeGirdisi>> Satirlar(
        string kod, decimal hedef, Bolge bolge, Dictionary<(Bolge, string), decimal> pay, Dictionary<(string Malik, string Kadastro), decimal> hak)
    {
        var sonuc = new List<KrokiEslesmeGirdisi>();
        var malikAlani = Bol(hedef, bolge.Malikler.Select(m => (m, pay[(bolge, m)])).ToList());
        if (malikAlani.IsFailure)
        {
            return Result<IReadOnlyList<KrokiEslesmeGirdisi>>.Failure(malikAlani.Error);
        }

        foreach (var (malik, alan) in malikAlani.Value)
        {
            var parseller = hak.Where(h => h.Key.Malik == malik).OrderBy(h => h.Key.Kadastro, StringComparer.Ordinal).Select(h => (h.Key.Kadastro, h.Value)).ToList();
            var kadastroAlani = Bol(alan, parseller);
            if (kadastroAlani.IsFailure)
            {
                return Result<IReadOnlyList<KrokiEslesmeGirdisi>>.Failure(kadastroAlani.Error);
            }

            sonuc.AddRange(kadastroAlani.Value.Where(k => k.Alan > 0m).Select(k => new KrokiEslesmeGirdisi(malik, k.Kimlik, kod, k.Alan)));
        }

        return Result<IReadOnlyList<KrokiEslesmeGirdisi>>.Success(sonuc);
    }

    /// <summary>Alanı ağırlıklarla (hepsi sıfırsa eşit) 0.01 m² birimde LR böler; tek kalemde alanın tamamı.</summary>
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
                decimal? net = null;
                if (girdi.Dopo is { } dopo)
                {
                    var giren = decimal.Round((Fraction.FromDecimal(k.TapuAlaniM2) * m.Hisse).ToDecimal(6), 2, MidpointRounding.AwayFromZero);
                    net = giren - decimal.Round(giren * dopo, 2, MidpointRounding.AwayFromZero);
                }

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
        : string.Create(CultureInfo.InvariantCulture, $"parselSayisi={Onizleme.Parseller.Count};blokSayisi={Onizleme.BolunenBloklar.Count};kaynak=yaklasikYerKrokisi");

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
