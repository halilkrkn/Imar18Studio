using Imar18.Domain.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Union;

namespace Imar18.GeometryEngine.Topoloji;

/// <summary>
/// <see cref="IParselTopolojiDenetcisi"/>'nin NTS overlay uygulaması (P6, SORU-034 k). Koordinatlar ortak yerel orijine
/// (tüm köşelerin en küçük X/Y'si) taşınıp eksen takasıyla NTS düzlemine alınır (double hassasiyet kaybını önler).
/// <list type="number">
/// <item>Kadastro çiftleri ve oluşan (imar/yol/kamu) çiftleri arası kesişim alanı → çakışma.</item>
/// <item>Kadastro boşluğu: birleşimin iç halkaları (delikler) + dışa açık dar şerit boşluklar. Şeritler, birleşimin
/// <see cref="SeritKapanisM"/> ile köşe korumalı (mitre) morfolojik kapanışı (buffer(+ε) → buffer(−ε)) ile bulunur: 2ε'dan
/// dar aralıklar kapanır, kapanış − birleşim alanı boşluktur (reviewer M1).</item>
/// <item>Kadastro birleşimi − oluşan birleşimi → kapsama boşluğu; oluşan − kadastro → kadastro dışına taşma
/// (her iki küme de boş değilse).</item>
/// </list>
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

    private static readonly BufferParameters Mitre = new() { JoinStyle = JoinStyle.Mitre, MitreLimit = 10d, EndCapStyle = EndCapStyle.Flat };

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
        var kadastro = new List<(string Id, Polygon Poligon)>();
        var olusan = new List<(string Id, Polygon Poligon)>();
        foreach (var p in gecerli)
        {
            var poligon = Poligon(p.Koseler, orijin);
            if (poligon is null)
            {
                // Fail-safe: NTS'nin geçersiz saydığı halka sessizce atlanmaz (çakışmayı gizleyebilir); analiz eksik sayılır.
                analizDisi.Add(p.ParselId);
                continue;
            }

            if (p.Rol == ParselRolu.Kadastro)
            {
                kadastro.Add((p.ParselId, poligon));
            }
            else if (ParselTopolojisi.OlusanMi(p.Rol))
            {
                olusan.Add((p.ParselId, poligon));
            }
        }

        var bulgular = new List<ParselTopolojiBulgusu>();
        try
        {
            Cakismalar(kadastro, TopolojiBulguTuru.KadastroCakismasi, bulgular, cancellationToken);
            Cakismalar(olusan, TopolojiBulguTuru.OlusanCakismasi, bulgular, cancellationToken);

            var kadastroBirlesimi = Birlesim(kadastro);
            if (kadastroBirlesimi is not null)
            {
                Ekle(bulgular, TopolojiBulguTuru.KadastroBoslugu, kadastro.Select(k => k.Id), KadastroBoslukAlani(kadastroBirlesimi));
            }

            var olusanBirlesimi = Birlesim(olusan);
            if (kadastroBirlesimi is not null && olusanBirlesimi is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hepsi = kadastro.Select(k => k.Id).Concat(olusan.Select(o => o.Id)).ToArray();
                Ekle(bulgular, TopolojiBulguTuru.OlusanKapsamaBoslugu, hepsi, kadastroBirlesimi.Difference(olusanBirlesimi).Area);
                Ekle(bulgular, TopolojiBulguTuru.KadastroDisinaTasma, hepsi, olusanBirlesimi.Difference(kadastroBirlesimi).Area);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-safe (reviewer M3): NTS'in TopologyException dışındaki istisnaları (ArgumentException, InvalidOperationException,
            // decimal OverflowException vb.) da onay komutunu çökertmez; analiz eksik sayılır ve ret denetime yazılır.
            return [new ParselTopolojiBulgusu(TopolojiBulguTuru.AnalizYapilamadi, [], 0m)];
        }

        if (analizDisi.Count > 0)
        {
            bulgular.Add(new ParselTopolojiBulgusu(TopolojiBulguTuru.AnalizYapilamadi, Sirali(analizDisi), 0m));
        }

        return Sirala(bulgular);
    }

    /// <summary>İç delikler (dolgu) + dar dışa açık şeritler (mitre kapanışı) − birleşim.</summary>
    private static double KadastroBoslukAlani(Geometry birlesim)
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
        return doldurulmus.Difference(birlesim).Area;
    }

    private static string[] Sirali(IEnumerable<string> idler) => idler.Order(StringComparer.Ordinal).ToArray();

    private static ParselTopolojiBulgusu[] Sirala(IEnumerable<ParselTopolojiBulgusu> bulgular) => bulgular
        .OrderBy(b => b.Tur)
        .ThenBy(b => string.Join('\u001F', b.ParselIdleri), StringComparer.Ordinal)
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

    private static void Cakismalar(
        List<(string Id, Polygon Poligon)> kume, TopolojiBulguTuru tur, List<ParselTopolojiBulgusu> bulgular, CancellationToken ct)
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

                Ekle(bulgular, tur, [a.Id, b.Id], a.Poligon.Intersection(b.Poligon).Area);
            }
        }
    }

    private static Geometry? Birlesim(List<(string Id, Polygon Poligon)> kume) =>
        kume.Count == 0 ? null : CascadedPolygonUnion.Union(kume.Select(k => (Geometry)k.Poligon).ToArray());

    private static void Ekle(List<ParselTopolojiBulgusu> bulgular, TopolojiBulguTuru tur, IEnumerable<string> idler, double alan)
    {
        if (!double.IsFinite(alan) || alan <= 0d)
        {
            return;
        }

        var m2 = decimal.Round(decimal.Round((decimal)alan, 6, MidpointRounding.AwayFromZero), 2, MidpointRounding.AwayFromZero);
        if (m2 >= ParselTopolojisi.AlanEsigiM2)
        {
            bulgular.Add(new ParselTopolojiBulgusu(tur, idler.Order(StringComparer.Ordinal).ToArray(), m2));
        }
    }
}
