using System.Globalization;
using Imar18.Domain.Geometry;

namespace Imar18.Application.Cad;

/// <summary>
/// CAD belgesinden proje geometrisine dönüşüm sonucu. <see cref="Uyarilar"/> SAHA-010 çift sayım önlemlerinin Türkçe
/// açıklamalarıdır (Uyarı &amp; Denetim sekmesi; yalnız parsel kimliği içerir, kişisel veri içermez).
/// </summary>
public sealed record ProjeGeometriSonucu(IReadOnlyList<ParselGeometrisi> Parseller, int AtlananSayisi)
{
    public IReadOnlyList<string> Uyarilar { get; init; } = [];

    /// <summary>R2 çift sayımını önlemek için projeye alınmayan geçerli poligon sayısı (ada + yinelenen + kabuk; yalnız sayı denetime gider).</summary>
    public int CiftSayimHaricSayisi { get; init; }
}

/// <summary>
/// İçe aktarılan CAD belgesini mühendis katman–rol eşlemesiyle (SORU-032 b) proje parsel geometrisine çevirir (P5 sihirbaz
/// bağlantısı). Rol eşlemesi: kadastro parseli → Kadastro, imar parseli → İmar, yola terk → YolTerk, park → KamuAlani; diğer
/// roller (Yoksay dahil) parsel değildir. Kapalı olmayan veya <see cref="PolygonRing.FromOpen"/> denetiminden geçmeyen parsel
/// poligonları atlanır ve sayılır (tek bozuk poligon içe aktarmayı düşürmez). Kimlik: etiket; etiket yoksa <c>KATMAN#sıra</c>;
/// yinelenen kimliğe <c>~2</c>, <c>~3</c>… eklenir. Sıra kaynak belge sırasıdır (deterministik).
/// <para>
/// SAHA-010 / SORU-054 (R2 çift sayım önlemi): (1) Projede geçerli İmar Parseli poligonu varsa İmar Adası poligonları projeye
/// alınmaz (R2 yalnız İmar Parsellerinden; ada yalnız tuvalde görsel referans) ve <see cref="AdaVeParselBirlikteUyarisi"/>
/// üretilir; hiç İmar Parseli yoksa İmar Adası poligonları imar rolüyle alınır (R2 = Σ ada). (2) İmar rolünde birebir yinelenen
/// poligonun (aynı köşe çevrimi; yön/başlangıç farkı önemsiz) yalnız ilki, başka imar poligon(lar)ını tümüyle kapsayan ada kabuğu
/// poligonu hiç alınmaz; her ikisi parsel kimliğiyle uyarılır. Kapsama denetimi Decimal köşe-içinde/üzerinde testidir
/// (<see cref="SinirToleransiM"/>); kesin alan/çakışma denetimi topoloji motorundadır.
/// </para>
/// </summary>
public static class ProjeGeometriEslemesi
{
    /// <summary>SORU-054 uyarı metni (proje sahibi talimatındaki birebir ifade).</summary>
    public const string AdaVeParselBirlikteUyarisi =
        "Hem İmar Adası hem İmar Parseli katmanı mevcut; DOP (R2) hesabında çift sayımı önlemek için yalnızca İmar Parseli alanları kullanıldı.";

    /// <summary>Kabuk (kapsama) denetiminde köşenin sınır üzerinde sayılma toleransı (m; topoloji dar şerit toleransıyla aynı 1 cm — uygulama eşiği).</summary>
    public const decimal SinirToleransiM = 0.01m;

    /// <summary>Kabuk sayılmak için kapsanan imar poligonlarının alan oranı alt sınırı (uygulama eşiği, mevzuat değeri değil — SORU-054).</summary>
    public const decimal KabukKapsamaOrani = 0.5m;

    /// <summary>Uyarıda listelenecek en fazla parsel kimliği.</summary>
    private const int UyaridakiEnFazlaKimlik = 10;

    public static ProjeGeometriSonucu Olustur(CadLayerDocument belge, CadLayerRoleMap roller)
    {
        ArgumentNullException.ThrowIfNull(belge);
        ArgumentNullException.ThrowIfNull(roller);

        var adaylar = new List<(string Katman, string? Etiket, ParselRolu Rol, bool Ada, GeodeticPoint[] Koseler)>();
        var atlanan = 0;
        foreach (var c in belge.Polylines)
        {
            var katmanRolu = roller.Resolve(c.LayerName);
            ParselRolu? rol = katmanRolu switch
            {
                CadLayerRole.CadastralParcel => ParselRolu.Kadastro,
                CadLayerRole.ZoningParcel or CadLayerRole.ZoningBlock => ParselRolu.Imar,
                CadLayerRole.RoadDedication => ParselRolu.YolTerk,
                CadLayerRole.Park => ParselRolu.KamuAlani,
                _ => null,
            };
            if (rol is null)
            {
                continue;
            }

            var koseler = c.Vertices.Count > 1 && c.Vertices[0] == c.Vertices[^1] ? c.Vertices.Take(c.Vertices.Count - 1).ToArray() : c.Vertices.ToArray();
            if (!c.IsClosed || PolygonRing.FromOpen(koseler).IsFailure)
            {
                atlanan++;
                adaylar.Add((c.LayerName, null, rol.Value, katmanRolu == CadLayerRole.ZoningBlock, [])); // sıra numarası korunur (deterministik kimlik)
                continue;
            }

            adaylar.Add((c.LayerName, c.Label, rol.Value, katmanRolu == CadLayerRole.ZoningBlock, koseler));
        }

        // SORU-054 (1): İmar Parseli varsa İmar Adası adayları kimlik üretiminden ÖNCE çıkarılır (parsel kimlikleri kaymaz).
        var uyarilar = new List<string>();
        var haricAda = 0;
        if (adaylar.Any(a => a.Ada && a.Koseler.Length > 0) && adaylar.Any(a => !a.Ada && a.Rol == ParselRolu.Imar && a.Koseler.Length > 0))
        {
            haricAda = adaylar.Count(a => a.Ada && a.Koseler.Length > 0);
            atlanan -= adaylar.Count(a => a.Ada && a.Koseler.Length == 0); // reviewer L8: yalnız görsel adanın bozukluğu "atlanan" sayılmaz
            adaylar.RemoveAll(a => a.Ada);
            uyarilar.Add(AdaVeParselBirlikteUyarisi);
        }

        // Kimlikler kaynak sırayla (katman içi sıra geçersizler dahil) üretilir.
        var kullanilan = new Dictionary<string, int>(StringComparer.Ordinal);
        var katmanSayaci = new Dictionary<string, int>(StringComparer.Ordinal);
        var gecerli = new List<ParselGeometrisi>();
        foreach (var a in adaylar)
        {
            var sira = katmanSayaci[a.Katman] = katmanSayaci.GetValueOrDefault(a.Katman) + 1;
            if (a.Koseler.Length == 0)
            {
                continue;
            }

            var temel = string.IsNullOrWhiteSpace(a.Etiket) ? a.Katman + "#" + sira.ToString(CultureInfo.InvariantCulture) : a.Etiket.Trim();
            var adet = kullanilan[temel] = kullanilan.GetValueOrDefault(temel) + 1;
            var id = adet == 1 ? temel : temel + "~" + adet.ToString(CultureInfo.InvariantCulture);
            gecerli.Add(new ParselGeometrisi(id.Length > 64 ? id[..64] : id, a.Rol, a.Koseler));
        }

        var imar = gecerli.Where(p => p.Rol == ParselRolu.Imar).ToList();
        var yinelenen = new HashSet<ParselGeometrisi>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < imar.Count; i++)
        {
            for (var j = 0; j < i && !yinelenen.Contains(imar[i]); j++)
            {
                if (!yinelenen.Contains(imar[j]) && AyniCevrim(imar[i].Koseler, imar[j].Koseler))
                {
                    yinelenen.Add(imar[i]);
                }
            }
        }

        var tekil = imar.Where(p => !yinelenen.Contains(p))
            .Select(p => (Parsel: p, Alan: Alan(p), Kutu: Kutu(p.Koseler)))
            .ToList();
        // SORU-054 (2): kabuk = içinde tümüyle kalan (köşeler, kenar orta noktaları ve köşe ortalaması P içinde/üzerinde) daha
        // küçük imar poligonlarının toplam alanı P alanının en az KabukKapsamaOrani kadarı olan poligon (içbükey parselin girintisini
        // dolduran komşu parsel kapsanmış sayılmaz: açık kenarının orta noktası P dışındadır).
        var kabuklar = tekil
            .Where(p => tekil
                .Where(q => !ReferenceEquals(p.Parsel, q.Parsel) && q.Alan < p.Alan && KutuIcinde(q.Kutu, p.Kutu) && Kapsar(p.Parsel.Koseler, q.Parsel.Koseler))
                .Sum(q => q.Alan) >= p.Alan * KabukKapsamaOrani)
            .Select(p => p.Parsel)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        if (yinelenen.Count > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                $"İmar parseli rolünde birebir üst üste çakışan (yinelenen) {yinelenen.Count} poligon DOP (R2) hesabına bir kez sayıldı; kopyalar alınmadı: {Kimlikler(yinelenen.Cast<ParselGeometrisi>())}."));
        }

        if (kabuklar.Count > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                $"İmar parseli rolünde başka imar parsellerini kapsayan {kabuklar.Count} ada kabuğu poligonu DOP (R2) hesabından çıkarıldı (çift sayım önlendi): {Kimlikler(kabuklar.Cast<ParselGeometrisi>())}. Ada sınırıysa katmanını 'İmar Adası' veya 'Yoksay' rolüne alın."));
        }

        var parseller = gecerli.Where(p => !yinelenen.Contains(p) && !kabuklar.Contains(p)).ToList();
        return new ProjeGeometriSonucu(parseller, atlanan)
        {
            Uyarilar = uyarilar.AsReadOnly(),
            CiftSayimHaricSayisi = haricAda + yinelenen.Count + kabuklar.Count,
        };
    }

    private static string Kimlikler(IEnumerable<ParselGeometrisi> parseller)
    {
        var idler = parseller.Select(p => p.ParselId).ToList();
        return string.Join(", ", idler.Take(UyaridakiEnFazlaKimlik)) + (idler.Count > UyaridakiEnFazlaKimlik ? " …" : string.Empty);
    }

    /// <summary>İç poligonun köşeleri, kenar orta noktaları ve köşe ortalaması dış halkanın içinde/üzerinde mi.</summary>
    private static bool Kapsar(IReadOnlyList<GeodeticPoint> dis, IReadOnlyList<GeodeticPoint> ic)
    {
        var n = ic.Count;
        var ortalama = new GeodeticPoint(ic.Sum(k => k.X) / n, ic.Sum(k => k.Y) / n);
        return ic.All(k => IcindeVeyaUzerinde(dis, k))
            && Enumerable.Range(0, n).All(i => IcindeVeyaUzerinde(dis, new GeodeticPoint((ic[i].X + ic[(i + 1) % n].X) / 2m, (ic[i].Y + ic[(i + 1) % n].Y) / 2m)))
            && IcindeVeyaUzerinde(dis, ortalama);
    }

    private static decimal Alan(ParselGeometrisi p) => GaussAreaCalculator.Compute(PolygonRing.FromOpen(p.Koseler).Value).AreaM2;

    private static (decimal MinX, decimal MinY, decimal MaxX, decimal MaxY) Kutu(IReadOnlyList<GeodeticPoint> k) =>
        (k.Min(p => p.X), k.Min(p => p.Y), k.Max(p => p.X), k.Max(p => p.Y));

    /// <summary>İç kutu dış kutunun içinde mi (tolerans payıyla) — kabuk denetimi ön elemesi.</summary>
    private static bool KutuIcinde((decimal MinX, decimal MinY, decimal MaxX, decimal MaxY) ic, (decimal MinX, decimal MinY, decimal MaxX, decimal MaxY) dis) =>
        ic.MinX >= dis.MinX - SinirToleransiM && ic.MinY >= dis.MinY - SinirToleransiM
        && ic.MaxX <= dis.MaxX + SinirToleransiM && ic.MaxY <= dis.MaxY + SinirToleransiM;

    /// <summary>
    /// İki açık köşe dizisi aynı çevrim mi (başlangıç noktası ve yönden bağımsız; köşeler <see cref="SinirToleransiM"/> içinde eşit —
    /// reviewer L9: 1 mm kaydırılmış kopya da yinelenen sayılır).
    /// </summary>
    private static bool AyniCevrim(IReadOnlyList<GeodeticPoint> a, IReadOnlyList<GeodeticPoint> b)
    {
        var n = a.Count;
        if (n != b.Count || n == 0)
        {
            return false;
        }

        for (var kaydirma = 0; kaydirma < n; kaydirma++)
        {
            if (!Yakin(b[kaydirma], a[0]))
            {
                continue;
            }

            var ileri = true;
            var geri = true;
            for (var i = 0; i < n && (ileri || geri); i++)
            {
                ileri &= Yakin(a[i], b[(kaydirma + i) % n]);
                geri &= Yakin(a[i], b[((kaydirma - i) % n + n) % n]);
            }

            if (ileri || geri)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Yakin(GeodeticPoint a, GeodeticPoint b) =>
        Math.Abs(a.X - b.X) <= SinirToleransiM && Math.Abs(a.Y - b.Y) <= SinirToleransiM;

    /// <summary>Nokta açık halkanın içinde ya da sınırına <see cref="SinirToleransiM"/> yakınlığında mı (Decimal ışın atma).</summary>
    private static bool IcindeVeyaUzerinde(IReadOnlyList<GeodeticPoint> halka, GeodeticPoint nokta)
    {
        var o = halka[0];
        var px = nokta.X - o.X;
        var py = nokta.Y - o.Y;
        var icinde = false;
        for (var i = 0; i < halka.Count; i++)
        {
            var a = halka[i];
            var b = halka[(i + 1) % halka.Count];
            var (ax, ay, bx, by) = (a.X - o.X, a.Y - o.Y, b.X - o.X, b.Y - o.Y);
            if (KenarUzerinde(ax, ay, bx, by, px, py))
            {
                return true;
            }

            if ((ay > py) != (by > py) && px < ((bx - ax) * (py - ay) / (by - ay)) + ax)
            {
                icinde = !icinde;
            }
        }

        return icinde;
    }

    private static bool KenarUzerinde(decimal ax, decimal ay, decimal bx, decimal by, decimal px, decimal py)
    {
        const decimal Tolerans2 = SinirToleransiM * SinirToleransiM;
        if (((px - ax) * (px - ax)) + ((py - ay) * (py - ay)) <= Tolerans2 || ((px - bx) * (px - bx)) + ((py - by) * (py - by)) <= Tolerans2)
        {
            return true; // uç noktaya tolerans içinde yakın
        }

        // İzdüşüm kenar içinde (0 ≤ t ≤ 1) ve dik uzaklık² = çapraz² / uzunluk² ≤ tolerans².
        var (dx, dy) = (bx - ax, by - ay);
        var uzunluk2 = (dx * dx) + (dy * dy);
        var izdusum = (dx * (px - ax)) + (dy * (py - ay));
        if (uzunluk2 == 0m || izdusum < 0m || izdusum > uzunluk2)
        {
            return false;
        }

        var capraz = (dx * (py - ay)) - (dy * (px - ax));
        return capraz * capraz <= Tolerans2 * uzunluk2;
    }
}
