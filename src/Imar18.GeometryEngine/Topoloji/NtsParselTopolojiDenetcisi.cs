using Imar18.Domain.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Union;

namespace Imar18.GeometryEngine.Topoloji;

/// <summary>
/// <see cref="IParselTopolojiDenetcisi"/>'nin NTS overlay uygulaması (P6, SORU-034 k; SAHA-004). Koordinatlar ortak yerel orijine
/// (tüm köşelerin en küçük X/Y'si) taşınıp eksen takasıyla NTS düzlemine alınır (double hassasiyet kaybını önler).
/// <list type="number">
/// <item>Kadastro çiftleri ve oluşan (imar/yol/kamu) çiftleri arası kesişim alanı → çakışma (parsel çifti bazında).</item>
/// <item>Kadastro boşluğu: birleşimin iç halkaları (delikler) + dışa açık dar şerit boşluklar. Şeritler, birleşimin
/// <see cref="SeritKapanisM"/> ile köşe korumalı (mitre) morfolojik kapanışı (buffer(+ε) → buffer(−ε)) ile bulunur: 2ε'dan
/// dar aralıklar kapanır, kapanış − birleşim alanı boşluktur (reviewer M1). Bileşen bazında raporlanır.</item>
/// <item>Kadastro birleşimi − oluşan birleşimi → kapsama boşluğu (bileşen bazında; en az iki farklı imar adasına komşu bileşen
/// çizilmemiş imar yoludur → <see cref="TopolojiBulguTuru.ImarYoluAlani"/>); oluşan parsel − kadastro birleşimi → kadastro dışına
/// taşma (parsel bazında). Her iki küme de boş değilse yapılır.</item>
/// </list>
/// Her bulgu milimetrik sınır toleransı için sınıflanır (SAHA-004): alan ≤ <see cref="ParselTopolojisi.SinirToleransiAlanM2"/> veya
/// geometri her yerde <see cref="ParselTopolojisi.SinirSeridiGenislikM"/>'den dar şerit (içe tampon onu tamamen siler).
/// Alan, kapıda decimal'e çevrilip önce 6 ondalığa (kayan nokta gürültüsü), sonra 2 ondalığa AwayFromZero yuvarlanır;
/// <see cref="ParselTopolojisi.AlanEsigiM2"/> altı raporlanmaz. NTS'nin geçersiz saydığı veya 3'ten az köşeli halka sessizce atlanmaz:
/// <see cref="TopolojiBulguTuru.AnalizYapilamadi"/> bulgusuyla kimliği döner (fail-safe; değerlendirme hesabı eksik sayar).
/// NTS tipleri public imzaya çıkmaz.
/// </summary>
public sealed class NtsParselTopolojiDenetcisi : IParselTopolojiDenetcisi
{
    private static readonly GeometryFactory Factory = new();

    /// <summary>
    /// Uygulama eşiği (mevzuat değeri değil, SORU-035): kadastro parselleri arasında 2 × 0.05 m = 10 cm'den dar dışa açık şerit
    /// boşluklar kapanışla yakalanır. Daha geniş dışa açık çentikler düzenleme sınırının parçası sayılır (iç delikler her
    /// genişlikte yakalanır).
    /// </summary>
    public const double SeritKapanisM = 0.05;

    /// <summary>Bir bulgu bileşenine "komşu" sayılan parselin en büyük uzaklığı (m; ortak sınırdaki kayan nokta payı).</summary>
    public const double KomsulukToleransiM = 0.01;

    private static readonly double SeritYariGenislikM = (double)ParselTopolojisi.SinirSeridiGenislikM / 2d;

    private static readonly BufferParameters Mitre = new() { JoinStyle = JoinStyle.Mitre, MitreLimit = 10d, EndCapStyle = EndCapStyle.Flat };

    private sealed record Parsel(string Id, ParselRolu Rol, Polygon Poligon);

    /// <inheritdoc />
    public IReadOnlyList<ParselTopolojiBulgusu> Denetle(IReadOnlyList<ParselGeometrisi> parseller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parseller);
        cancellationToken.ThrowIfCancellationRequested();

        var ilgili = parseller.Where(p => p is not null && (p.Rol == ParselRolu.Kadastro || ParselTopolojisi.OlusanMi(p.Rol))).ToList();
        var gecerli = ilgili.Where(p => p.Koseler is { Count: >= 3 }).ToList();
        var analizDisi = ilgili.Where(p => p.Koseler is not { Count: >= 3 }).Select(p => p.ParselId).ToList();
        if (gecerli.Count == 0)
        {
            return Sirala(analizDisi.Count == 0 ? [] : [new ParselTopolojiBulgusu(TopolojiBulguTuru.AnalizYapilamadi, Sirali(analizDisi), 0m)]);
        }

        var orijin = new GeodeticPoint(gecerli.SelectMany(p => p.Koseler).Min(k => k.X), gecerli.SelectMany(p => p.Koseler).Min(k => k.Y));
        var kadastro = new List<Parsel>();
        var olusan = new List<Parsel>();
        foreach (var p in gecerli)
        {
            var poligon = Poligon(p.Koseler, orijin);
            if (poligon is null)
            {
                // Fail-safe: NTS'nin geçersiz saydığı halka sessizce atlanmaz (çakışmayı gizleyebilir); analiz eksik sayılır.
                analizDisi.Add(p.ParselId);
                continue;
            }

            (p.Rol == ParselRolu.Kadastro ? kadastro : olusan).Add(new Parsel(p.ParselId, p.Rol, poligon));
        }

        var bulgular = new List<ParselTopolojiBulgusu>();
        var kiymiklar = new Kiymiklar();
        try
        {
            Cakismalar(kadastro, TopolojiBulguTuru.KadastroCakismasi, bulgular, kiymiklar, cancellationToken);
            Cakismalar(olusan, TopolojiBulguTuru.OlusanCakismasi, bulgular, kiymiklar, cancellationToken);

            var kadastroBirlesimi = Birlesim(kadastro);
            if (kadastroBirlesimi is not null)
            {
                foreach (var parca in Parcalar(KadastroBosluklari(kadastroBirlesimi)))
                {
                    Ekle(bulgular, kiymiklar, TopolojiBulguTuru.KadastroBoslugu, Komsular(parca, kadastro), parca);
                }
            }

            var olusanBirlesimi = Birlesim(olusan);
            if (kadastroBirlesimi is not null && olusanBirlesimi is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hepsi = kadastro.Concat(olusan).ToList();
                foreach (var parca in Parcalar(kadastroBirlesimi.Difference(olusanBirlesimi)))
                {
                    var komsu = hepsi.Where(p => KomsuMu(p, parca)).ToList();
                    var tur = ImarYoluMu(komsu) ? TopolojiBulguTuru.ImarYoluAlani : TopolojiBulguTuru.OlusanKapsamaBoslugu;
                    Ekle(bulgular, kiymiklar, tur, komsu.Select(p => p.Id), parca);
                }

                foreach (var o in olusan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tasma = o.Poligon.Difference(kadastroBirlesimi);
                    if (!tasma.IsEmpty)
                    {
                        Ekle(bulgular, kiymiklar, TopolojiBulguTuru.KadastroDisinaTasma, Komsular(tasma, kadastro).Append(o.Id), tasma);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-safe (reviewer M3): NTS'in TopologyException dışındaki istisnaları (ArgumentException, InvalidOperationException,
            // decimal OverflowException vb.) da onay komutunu çökertmez; analiz eksik sayılır ve ret denetime yazılır.
            return [new ParselTopolojiBulgusu(TopolojiBulguTuru.AnalizYapilamadi, [], 0m)];
        }

        kiymiklar.Ekle(bulgular);
        if (analizDisi.Count > 0)
        {
            bulgular.Add(new ParselTopolojiBulgusu(TopolojiBulguTuru.AnalizYapilamadi, Sirali(analizDisi), 0m));
        }

        return Sirala(bulgular);
    }

    /// <summary>
    /// Reviewer M1: raporlama eşiği (0.01 m²) altında kalıp tek tek düşen bileşenlerin tür bazında toplamı. Toplam eşiği aşarsa
    /// ilgili tüm parsellerle tek bulgu olarak eklenir (çok sayıda kıymık sessizce kaybolmaz); sınır toleransı yalnız toplam
    /// <see cref="ParselTopolojisi.SinirToleransiAlanM2"/> içindeyse geçerlidir.
    /// </summary>
    private sealed class Kiymiklar
    {
        private readonly SortedDictionary<TopolojiBulguTuru, (HashSet<string> Idler, double Alan)> _tur = [];

        public void Biriktir(TopolojiBulguTuru tur, IEnumerable<string> idler, double alan)
        {
            if (!_tur.TryGetValue(tur, out var k))
            {
                k = (new HashSet<string>(StringComparer.Ordinal), 0d);
            }

            k.Idler.UnionWith(idler);
            _tur[tur] = (k.Idler, k.Alan + alan);
        }

        public void Ekle(List<ParselTopolojiBulgusu> bulgular)
        {
            foreach (var (tur, k) in _tur)
            {
                var m2 = M2(k.Alan);
                if (m2 >= ParselTopolojisi.AlanEsigiM2)
                {
                    // Kıymık toplamı imar yolu sayılmaz: boşluk olarak raporlanır (fail-safe).
                    var t = tur == TopolojiBulguTuru.ImarYoluAlani ? TopolojiBulguTuru.OlusanKapsamaBoslugu : tur;
                    bulgular.Add(new ParselTopolojiBulgusu(t, Sirali(k.Idler), m2, m2 <= ParselTopolojisi.SinirToleransiAlanM2));
                }
            }
        }
    }

    /// <summary>İç delikler (dolgu) + dar dışa açık şeritler (mitre kapanışı) − birleşim.</summary>
    private static Geometry KadastroBosluklari(Geometry birlesim)
    {
        var dolu = new List<Geometry>();
        for (var i = 0; i < birlesim.NumGeometries; i++)
        {
            if (birlesim.GetGeometryN(i) is Polygon parca)
            {
                dolu.Add(Factory.CreatePolygon(parca.Shell));
            }
        }

        var kapanis = BufferOp.Buffer(BufferOp.Buffer(birlesim, SeritKapanisM, Mitre), -SeritKapanisM, Mitre);
        var doldurulmus = CascadedPolygonUnion.Union([.. dolu, kapanis]);
        return doldurulmus.Difference(birlesim);
    }

    /// <summary>Alanlı (poligon) bileşenler; çizgi/nokta artıkları atlanır.</summary>
    private static IEnumerable<Geometry> Parcalar(Geometry g)
    {
        for (var i = 0; i < g.NumGeometries; i++)
        {
            var parca = g.GetGeometryN(i);
            if (parca is Polygon or MultiPolygon && parca.Area > 0d)
            {
                yield return parca;
            }
        }
    }

    private static bool KomsuMu(Parsel p, Geometry g) =>
        p.Poligon.EnvelopeInternal.Distance(g.EnvelopeInternal) <= KomsulukToleransiM && p.Poligon.Distance(g) <= KomsulukToleransiM;

    private static IEnumerable<string> Komsular(Geometry g, IEnumerable<Parsel> kume) => kume.Where(p => KomsuMu(p, g)).Select(p => p.Id);

    /// <summary>
    /// Bileşene komşu imar parselleri en az iki farklı imar adası kodu taşıyorsa çizilmemiş imar yoludur (SAHA-004). Reviewer H1:
    /// bileşen çizilmiş bir yol terk / kamu alanı parseline değiyorsa yol zaten çizilmiştir; kalan boşluk imar yolu sayılmaz (kritik
    /// boşluk). Alanın beklenen yol alanıyla (R1 − R2) uyumu Application'da ayrıca denetlenir.
    /// </summary>
    private static bool ImarYoluMu(List<Parsel> komsular) =>
        !komsular.Any(p => p.Rol is ParselRolu.YolTerk or ParselRolu.KamuAlani) && komsular
        .Where(p => p.Rol == ParselRolu.Imar)
        .Select(p => ParselTopolojisi.ImarAdasi(p.Id))
        .Where(a => a is not null)
        .Distinct(StringComparer.Ordinal)
        .Skip(1)
        .Any();

    /// <summary>Milimetrik sınır dokunması: içe tampon (yarım şerit genişliği) geometriyi tamamen siliyorsa dar şerittir.</summary>
    private static bool DarSerit(Geometry g) => g.Buffer(-SeritYariGenislikM).Area <= 1e-9;

    private static string[] Sirali(IEnumerable<string> idler) => idler.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static ParselTopolojiBulgusu[] Sirala(IEnumerable<ParselTopolojiBulgusu> bulgular) => bulgular
        .OrderBy(b => b.Tur)
        .ThenBy(b => string.Join('\u001F', b.ParselIdleri), StringComparer.Ordinal)
        .ThenBy(b => b.AlanM2)
        .ThenBy(b => b.SinirToleransiIcinde)
        .ToArray();

    private static Polygon? Poligon(IReadOnlyList<GeodeticPoint> koseler, GeodeticPoint orijin)
    {
        var acik = koseler.Count > 1 && koseler[0] == koseler[^1] ? koseler.Take(koseler.Count - 1).ToArray() : koseler.ToArray();
        if (acik.Length < 3)
        {
            return null;
        }

        var coords = new Coordinate[acik.Length + 1];
        for (var i = 0; i < acik.Length; i++)
        {
            coords[i] = CoordinateAxisSwapAdapter.ToNts(acik[i], orijin);
        }

        coords[^1] = coords[0].Copy();
        var poligon = Factory.CreatePolygon(coords);
        return poligon.IsValid && poligon.Area > 0d ? poligon : null;
    }

    private static void Cakismalar(List<Parsel> kume, TopolojiBulguTuru tur, List<ParselTopolojiBulgusu> bulgular, Kiymiklar kiymiklar, CancellationToken ct)
    {
        for (var i = 0; i < kume.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (var j = i + 1; j < kume.Count; j++)
            {
                var (a, b) = (kume[i], kume[j]);
                if (!a.Poligon.EnvelopeInternal.Intersects(b.Poligon.EnvelopeInternal))
                {
                    continue;
                }

                Ekle(bulgular, kiymiklar, tur, [a.Id, b.Id], a.Poligon.Intersection(b.Poligon));
            }
        }
    }

    private static Geometry? Birlesim(List<Parsel> kume) =>
        kume.Count == 0 ? null : CascadedPolygonUnion.Union(kume.Select(k => (Geometry)k.Poligon).ToArray());

    private static decimal M2(double alan) =>
        decimal.Round(decimal.Round((decimal)alan, 6, MidpointRounding.AwayFromZero), 2, MidpointRounding.AwayFromZero);

    private static void Ekle(
        List<ParselTopolojiBulgusu> bulgular, Kiymiklar kiymiklar, TopolojiBulguTuru tur, IEnumerable<string> idler, Geometry geometri)
    {
        var alan = geometri.Area;
        if (!double.IsFinite(alan) || alan <= 0d)
        {
            return;
        }

        var m2 = M2(alan);
        if (m2 < ParselTopolojisi.AlanEsigiM2)
        {
            kiymiklar.Biriktir(tur, idler, alan);
            return;
        }

        // İmar yolu bilgi bulgusudur; tolerans sınıflaması yalnızca engel olabilecek bulgulara uygulanır. Reviewer H2: dar şerit
        // kuralı alan üst sınırıyla (SinirSeridiAzamiAlanM2) sınırlıdır; uzun ama dar büyük taşma/çakışma engel kalır.
        var tolerans = tur != TopolojiBulguTuru.ImarYoluAlani
            && (m2 <= ParselTopolojisi.SinirToleransiAlanM2 || (m2 <= ParselTopolojisi.SinirSeridiAzamiAlanM2 && DarSerit(geometri)));
        bulgular.Add(new ParselTopolojiBulgusu(tur, Sirali(idler), m2, tolerans));
    }
}
