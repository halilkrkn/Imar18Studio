using System.Globalization;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.GeometryEngine.Subdivision;

/// <summary>
/// SAHA-015 (SORU-044): <see cref="IKrokiBlokBolucu"/> uygulaması — Yaklaşık Yer Krokisi bloklarının mühendis çizgilerine paralel
/// kesin alanlı bölünmesi.
/// <list type="bullet">
/// <item>Hesap bloğun ilk köşesine ötelemeli yerel çerçevede (<c>x = Doğu</c>, <c>y = Kuzey</c>) <c>double</c> ile yapılır; köşeler her
/// adımda decimal kaynaktan yeniden türetilir (birikimli kayma yok).</item>
/// <item>Sıra: çizgi doğrultularının (işaret hizalı) ortalamasına dik ve Doğu'ya bakan normal üzerinde çizgi orta noktalarının
/// izdüşümü. Her çizgi ortalamadan en çok <see cref="EnBuyukAciSapmasiDerece"/> sapabilir (uygulama eşiği); aksi hâlde kroki
/// "paralel bölme" değildir ve reddedilir.</item>
/// <item>Kesim: kalan halka, çizginin kendi normaline göre yarı düzlemle ikiye ayrılır (alçak taraf = sıradaki parça). Doğru
/// halkayı tam iki noktada kesmelidir (sıfırdan farklı işaret geçişi = 2); daha fazlası konkav/çok parçalı kesimdir
/// (<c>ERR-TOPO-004</c>), hiç geçiş yoksa çizgi bloğu bölmüyordur (<c>ERR-PLAN-003</c>). Doğru üzerindeki köşe iki parçada da kalır.</item>
/// <item>Kesin bölmede kesim doğrusu kroki çizgisine paralel kaydırılır; alçak taraf alanı A(c) monoton olduğundan ikili arama
/// <see cref="YakinsamaToleransiM2"/> içinde hedefe yakınsar (en çok <see cref="EnFazlaIterasyon"/> adım).</item>
/// <item>Yeni kesim köşeleri mutlak koordinatta 6 ondalığa yuvarlanır (1 mm kuantizasyon yapılmaz — SORU-025/032, SORU-064);
/// her parça <see cref="PolygonRing.FromOpen"/> ve NTS <c>IsValid</c> denetiminden geçmezse sonuç döndürülmez.</item>
/// </list>
/// Sınıf durumsuzdur; NTS tipleri dışarı çıkmaz.
/// </summary>
public sealed class KrokiBlokBolucu : IKrokiBlokBolucu
{
    /// <summary>Uygulama eşiği (mevzuat değeri değil): bir çizginin ortalama doğrultudan en büyük sapması (derece).</summary>
    public const double EnBuyukAciSapmasiDerece = 30d;

    /// <summary>Uygulama eşiği: ikili arama yakınsama toleransı (m², çift hassasiyet).</summary>
    public const double YakinsamaToleransiM2 = 1e-7;

    /// <summary>Uygulama güvenlik sınırı: ikili arama en fazla adım sayısı.</summary>
    public const int EnFazlaIterasyon = 200;

    /// <summary>Σ hedef ile blok alanı arasında kabul edilen en büyük fark (m²; 2 ondalığa yuvarlanmış hedeflerin payı).</summary>
    public const decimal HedefToplamToleransiM2 = 0.01m;

    /// <summary>En kısa kroki çizgisi (m).</summary>
    private const double EnKisaCizgiM = 0.01;

    /// <summary>Doğruya bu uzaklıktan yakın köşe "doğru üzerinde" sayılır (m).</summary>
    private const double DogruUzeriEpsilonM = 1e-9;

    private readonly record struct Kose(GeodeticPoint Nokta, double X, double Y);

    private readonly record struct Halka(Kose Kose, bool DogruUzerinde);

    private readonly record struct Cizgi(BolmeDogrusu Kaynak, double Ax, double Ay, double Ux, double Uy, double Nx, double Ny)
    {
        /// <summary>Kirişin (çizginin blok içindeki parçası) orta noktasının normal üzerindeki izdüşümü (architect Y2: segment ortası değil).</summary>
        public double OrtaIzdusum(double nx, double ny) => ((Ax + (Ux * KirisOrta)) * nx) + ((Ay + (Uy * KirisOrta)) * ny);

        public double Uzunluk { get; init; }

        /// <summary>Kirişin başlangıç/bitiş parametresi (A + t·U); taslakta blok sınırına kırpılmış, aksi hâlde kroki segmenti.</summary>
        public double KirisBas { get; init; }

        public double KirisSon { get; init; }

        public double KirisOrta => (KirisBas + KirisSon) / 2d;

        public double AciDerece => Math.Atan2(Uy, Ux) * 180d / Math.PI;
    }

    public Result<KrokiTaslakBolmesi> TaslakBol(IReadOnlyList<GeodeticPoint> blok, IReadOnlyList<BolmeDogrusu> cizgiler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blok);
        ArgumentNullException.ThrowIfNull(cizgiler);
        cancellationToken.ThrowIfCancellationRequested();

        var hazirlik = Hazirla(blok, cizgiler);
        if (hazirlik.Hata is not null)
        {
            return Result<KrokiTaslakBolmesi>.Failure(hazirlik.Hata);
        }

        var (orijin, kalan, sirali) = (hazirlik.Orijin, hazirlik.Halka!, hazirlik.Cizgiler!);
        var bolgeler = new List<IReadOnlyList<GeodeticPoint>>(sirali.Count + 1);
        for (var i = 0; i < sirali.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var c = sirali[i];
            var bol = Bol(kalan, c.Nx, c.Ny, (c.Ax * c.Nx) + (c.Ay * c.Ny), orijin, i, c);
            if (bol.Hata is not null)
            {
                return Result<KrokiTaslakBolmesi>.Failure(bol.Hata);
            }

            bolgeler.Add(bol.Alcak!.Select(k => k.Nokta).ToArray());
            kalan = bol.Yuksek!;
        }

        bolgeler.Add(kalan.Select(k => k.Nokta).ToArray());
        return Result<KrokiTaslakBolmesi>.Success(new KrokiTaslakBolmesi(sirali.Select(c => c.Kaynak).ToArray(), bolgeler.AsReadOnly()));
    }

    public Result<IReadOnlyList<IReadOnlyList<GeodeticPoint>>> KesinBol(
        IReadOnlyList<GeodeticPoint> blok, IReadOnlyList<BolmeDogrusu> siraliCizgiler, IReadOnlyList<decimal> hedefAlanlarM2, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blok);
        ArgumentNullException.ThrowIfNull(siraliCizgiler);
        ArgumentNullException.ThrowIfNull(hedefAlanlarM2);
        cancellationToken.ThrowIfCancellationRequested();

        var hazirlik = Hazirla(blok, siraliCizgiler, siralama: false);
        if (hazirlik.Hata is not null)
        {
            return Hata<IReadOnlyList<IReadOnlyList<GeodeticPoint>>>(hazirlik.Hata);
        }

        if (hedefAlanlarM2.Count != siraliCizgiler.Count + 1)
        {
            return Gecersiz<IReadOnlyList<IReadOnlyList<GeodeticPoint>>>("target count must be cut count + 1");
        }

        if (hedefAlanlarM2.Any(h => h <= 0m))
        {
            return Gecersiz<IReadOnlyList<IReadOnlyList<GeodeticPoint>>>("target areas must be positive");
        }

        var blokAlani = GaussAreaCalculator.Compute(PolygonRing.FromOpen(blok).Value).AreaM2Exact;
        if (Math.Abs(hedefAlanlarM2.Sum() - blokAlani) > HedefToplamToleransiM2)
        {
            return Gecersiz<IReadOnlyList<IReadOnlyList<GeodeticPoint>>>("sum of target areas must equal the block area within 0.01 m2");
        }

        var (orijin, kalan, cizgiler) = (hazirlik.Orijin, hazirlik.Halka!, hazirlik.Cizgiler!);
        var parcalar = new List<IReadOnlyList<GeodeticPoint>>(cizgiler.Count + 1);
        for (var i = 0; i < cizgiler.Count; i++)
        {
            var c = cizgiler[i];
            var hedef = (double)hedefAlanlarM2[i];
            var izdusum = kalan.Select(k => (k.X * c.Nx) + (k.Y * c.Ny)).ToArray();
            double lo = izdusum.Min(), hi = izdusum.Max();
            if (hedef >= AlcakAlan(kalan, c.Nx, c.Ny, hi) - YakinsamaToleransiM2)
            {
                return Gecersiz<IReadOnlyList<IReadOnlyList<GeodeticPoint>>>(string.Create(CultureInfo.InvariantCulture, $"target of part #{i + 1} does not fit in the remaining block"));
            }

            double? bulunan = null;
            var adim = 0;
            while (adim < EnFazlaIterasyon)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var orta = (lo + hi) / 2d;
                if (orta <= lo || orta >= hi)
                {
                    break;
                }

                adim++;
                var alan = AlcakAlan(kalan, c.Nx, c.Ny, orta);
                if (Math.Abs(alan - hedef) <= YakinsamaToleransiM2)
                {
                    bulunan = orta;
                    break;
                }

                if (alan < hedef)
                {
                    lo = orta;
                }
                else
                {
                    hi = orta;
                }
            }

            // Çift aralığı tükendiyse son orta nokta kabul edilir (sapma decimal doğrulamasında ölçülür).
            var kesim = bulunan ?? ((lo + hi) / 2d);
            if (bulunan is null && Math.Abs(AlcakAlan(kalan, c.Nx, c.Ny, kesim) - hedef) > 1e-4)
            {
                var e = ErrorCatalog.Get(ErrorCodes.PlanHedefAlanaYakinsanamadi);
                return Hata<IReadOnlyList<IReadOnlyList<GeodeticPoint>>>(e with
                {
                    TechnicalMessage = e.TechnicalMessage.Replace("{iterations}", adim.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                });
            }

            var bol = Bol(kalan, c.Nx, c.Ny, kesim, orijin, i, c);
            if (bol.Hata is not null)
            {
                return Hata<IReadOnlyList<IReadOnlyList<GeodeticPoint>>>(bol.Hata);
            }

            parcalar.Add(bol.Alcak!.Select(k => k.Nokta).ToArray());
            kalan = bol.Yuksek!;
        }

        parcalar.Add(kalan.Select(k => k.Nokta).ToArray());
        return Result<IReadOnlyList<IReadOnlyList<GeodeticPoint>>>.Success(parcalar.AsReadOnly());
    }

    private sealed record Hazirlik(GeodeticPoint Orijin, List<Kose>? Halka, List<Cizgi>? Cizgiler, AppError? Hata);

    /// <summary>Blok ve çizgileri doğrular, yerel çerçeveye taşır ve (istenirse) sıralar; normaller Doğu'ya bakan ortalama normalle hizalanır.</summary>
    private static Hazirlik Hazirla(IReadOnlyList<GeodeticPoint> blok, IReadOnlyList<BolmeDogrusu> cizgiler, bool siralama = true)
    {
        var halka = PolygonRing.FromOpen(blok);
        if (halka.IsFailure)
        {
            return new Hazirlik(default, null, null, halka.Error);
        }

        var orijin = blok[0];
        if (!NtsValidity.IsValid(blok, orijin))
        {
            return new Hazirlik(orijin, null, null, HataOlustur(ErrorCodes.TopoKendiUzerindeKesisim, ("{parcelId}", "sketch block")));
        }

        if (cizgiler.Count == 0 || cizgiler.Any(c => c is null))
        {
            return new Hazirlik(orijin, null, null, GecersizHata("at least one dividing line is required"));
        }

        var liste = new List<Cizgi>(cizgiler.Count);
        foreach (var c in cizgiler)
        {
            var (ax, ay) = Yerel(c.Baslangic, orijin);
            var (bx, by) = Yerel(c.Bitis, orijin);
            var uzunluk = Math.Sqrt(((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay)));
            if (!double.IsFinite(uzunluk) || uzunluk < EnKisaCizgiM)
            {
                return new Hazirlik(orijin, null, null, GecersizHata("dividing line is shorter than 0.01 m"));
            }

            var (ux, uy) = ((bx - ax) / uzunluk, (by - ay) / uzunluk);
            liste.Add(new Cizgi(c, ax, ay, ux, uy, -uy, ux) { Uzunluk = uzunluk, KirisBas = 0d, KirisSon = uzunluk });
        }

        // Ortalama doğrultu (ilk çizgiye işaret hizalı) ve Doğu'ya (eşitlikte Kuzey'e) bakan ortalama normal.
        double sx = 0, sy = 0;
        foreach (var c in liste)
        {
            var isaret = (c.Ux * liste[0].Ux) + (c.Uy * liste[0].Uy) < 0 ? -1d : 1d;
            sx += isaret * c.Ux;
            sy += isaret * c.Uy;
        }

        var boy = Math.Sqrt((sx * sx) + (sy * sy));
        var (mx, my) = (sx / boy, sy / boy);
        var (nx, ny) = (-my, mx);
        if (nx < -1e-12 || (Math.Abs(nx) <= 1e-12 && ny < 0))
        {
            (nx, ny) = (-nx, -ny);
        }

        var esik = Math.Cos(EnBuyukAciSapmasiDerece * Math.PI / 180d);
        for (var i = 0; i < liste.Count; i++)
        {
            var c = liste[i];
            if (Math.Abs((c.Ux * mx) + (c.Uy * my)) < esik - 1e-12)
            {
                return new Hazirlik(orijin, null, null, GecersizHata("dividing lines are not approximately parallel (deviation above 30 degrees)"));
            }

            liste[i] = (c.Nx * nx) + (c.Ny * ny) < 0 ? c with { Nx = -c.Nx, Ny = -c.Ny } : c;
        }

        var yerelBlok = blok.Select(p => Yerel(p, orijin)).ToArray();
        if (siralama)
        {
            // Architect Y2: her çizgi blok sınırına kırpılır (orta noktasını içeren kiriş); kirişler blok içinde kesişemez.
            for (var i = 0; i < liste.Count; i++)
            {
                if (Kiris(yerelBlok, liste[i]) is not { } kiris)
                {
                    return new Hazirlik(orijin, null, null, GecersizHata("dividing line midpoint is not inside the block"));
                }

                liste[i] = liste[i] with { KirisBas = kiris.Bas, KirisSon = kiris.Son };
            }

            for (var i = 0; i < liste.Count; i++)
            {
                for (var j = i + 1; j < liste.Count; j++)
                {
                    if (KirislerKesisir(liste[i], liste[j]))
                    {
                        return new Hazirlik(orijin, null, null, GecersizHata("dividing lines cross each other inside the block"));
                    }
                }
            }

            liste = [.. liste.OrderBy(c => c.OrtaIzdusum(nx, ny))];
            for (var i = 1; i < liste.Count; i++)
            {
                if (Math.Abs(liste[i].OrtaIzdusum(nx, ny) - liste[i - 1].OrtaIzdusum(nx, ny)) < 1e-6)
                {
                    return new Hazirlik(orijin, null, null, GecersizHata("two dividing lines coincide"));
                }
            }
        }

        return new Hazirlik(orijin, blok.Select(p => KoseOlustur(p, orijin)).ToList(), liste, null);
    }

    /// <summary>
    /// Çizginin sonsuz doğrusunun blok içindeki, segment orta noktasını içeren kirişi (A + t·U parametreleri); orta nokta blok dışındaysa null.
    /// Doğrunun sınırla kesişim parametreleri sıralanır; çift indeksli aralıklar blok içidir.
    /// </summary>
    private static (double Bas, double Son)? Kiris((double X, double Y)[] blok, Cizgi c)
    {
        var t = new List<double>();
        for (var i = 0; i < blok.Length; i++)
        {
            var (px, py) = blok[i];
            var (qx, qy) = blok[(i + 1) % blok.Length];
            var (ex, ey) = (qx - px, qy - py);
            var payda = (c.Ux * ey) - (c.Uy * ex);
            if (Math.Abs(payda) < 1e-15)
            {
                continue;
            }

            var (wx, wy) = (px - c.Ax, py - c.Ay);
            var tt = ((wx * ey) - (wy * ex)) / payda;
            var ss = ((wx * c.Uy) - (wy * c.Ux)) / payda;
            if (ss >= 0d && ss < 1d)
            {
                t.Add(tt);
            }
        }

        t.Sort();
        var tekil = new List<double>();
        foreach (var v in t)
        {
            if (tekil.Count == 0 || v - tekil[^1] > 1e-9)
            {
                tekil.Add(v);
            }
        }

        var orta = c.Uzunluk / 2d;
        for (var k = 0; k + 1 < tekil.Count; k += 2)
        {
            if (tekil[k] <= orta && orta <= tekil[k + 1])
            {
                return (tekil[k], tekil[k + 1]);
            }
        }

        return null;
    }

    /// <summary>İki kiriş birbirini iç noktada (uçlar hariç) kesiyor mu.</summary>
    private static bool KirislerKesisir(Cizgi a, Cizgi b)
    {
        var payda = (a.Ux * b.Uy) - (a.Uy * b.Ux);
        if (Math.Abs(payda) < 1e-12)
        {
            return false;
        }

        var (wx, wy) = (b.Ax - a.Ax, b.Ay - a.Ay);
        var ta = ((wx * b.Uy) - (wy * b.Ux)) / payda;
        var tb = ((wx * a.Uy) - (wy * a.Ux)) / payda;
        const double Pay = 1e-6;
        return ta > a.KirisBas + Pay && ta < a.KirisSon - Pay && tb > b.KirisBas + Pay && tb < b.KirisSon - Pay;
    }

    private sealed record Bolme(List<Kose>? Alcak, List<Kose>? Yuksek, AppError? Hata);

    /// <summary>Halkayı <c>x·n = c</c> doğrusuyla ikiye ayırır (alçak taraf <c>x·n ≤ c</c>).</summary>
    private static Bolme Bol(List<Kose> halka, double nx, double ny, double c, GeodeticPoint orijin, int sira, Cizgi cizgi)
    {
        var n = halka.Count;
        var s = new double[n];
        var isaret = new int[n];
        for (var i = 0; i < n; i++)
        {
            s[i] = (halka[i].X * nx) + (halka[i].Y * ny) - c;
            isaret[i] = s[i] < -DogruUzeriEpsilonM ? -1 : s[i] > DogruUzeriEpsilonM ? 1 : 0;
        }

        var sifirsiz = isaret.Where(x => x != 0).ToArray();
        var gecis = 0;
        for (var i = 0; i < sifirsiz.Length; i++)
        {
            if (sifirsiz[i] != sifirsiz[(i + 1) % sifirsiz.Length])
            {
                gecis++;
            }
        }

        if (gecis > 2)
        {
            return new Bolme(null, null, HataOlustur(ErrorCodes.TopoKonkavKesimCokParcali,
                ("{angle}", string.Create(CultureInfo.InvariantCulture, $"{cizgi.AciDerece:0.###} (sketch cut #{sira + 1})"))));
        }

        if (gecis < 2)
        {
            return new Bolme(null, null, GecersizHata(string.Create(CultureInfo.InvariantCulture, $"sketch cut #{sira + 1} does not split the remaining block")));
        }

        var alcak = new List<Halka>(n + 2);
        var yuksek = new List<Halka>(n + 2);
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            if (isaret[i] <= 0)
            {
                alcak.Add(new Halka(halka[i], isaret[i] == 0));
            }

            if (isaret[i] >= 0)
            {
                yuksek.Add(new Halka(halka[i], isaret[i] == 0));
            }

            if (isaret[i] * isaret[j] < 0)
            {
                var lambda = s[i] / (s[i] - s[j]);
                var x = halka[i].X + (lambda * (halka[j].X - halka[i].X));
                var y = halka[i].Y + (lambda * (halka[j].Y - halka[i].Y));
                var kose = KoseOlustur(Mutlak(x, y, orijin), orijin);
                alcak.Add(new Halka(kose, true));
                yuksek.Add(new Halka(kose, true));
            }
        }

        var a = Temizle(alcak);
        var b = Temizle(yuksek);
        foreach (var parca in new[] { a, b })
        {
            var noktalar = parca.Select(k => k.Nokta).ToArray();
            var r = PolygonRing.FromOpen(noktalar);
            if (r.IsFailure)
            {
                return new Bolme(null, null, HataOlustur(ErrorCodes.TopoDejenerePoligon,
                    ("{reason}", string.Create(CultureInfo.InvariantCulture, $"sketch cut #{sira + 1} produced a piece without area"))));
            }

            if (!NtsValidity.IsValid(noktalar, orijin))
            {
                return new Bolme(null, null, HataOlustur(ErrorCodes.TopoKendiUzerindeKesisim,
                    ("{parcelId}", string.Create(CultureInfo.InvariantCulture, $"sketch piece #{sira + 1}"))));
            }
        }

        return new Bolme(a, b, null);
    }

    /// <summary>Ardışık (dairesel) yinelenen noktaları birleştirir; doğru üzerindeki dizilerde yalnız uçları tutar (sıfır genişlikli çıkıntı temizliği).</summary>
    private static List<Kose> Temizle(List<Halka> halka)
    {
        var tekil = new List<Halka>(halka.Count);
        foreach (var h in halka)
        {
            if (tekil.Count > 0 && tekil[^1].Kose.Nokta == h.Kose.Nokta)
            {
                tekil[^1] = tekil[^1] with { DogruUzerinde = tekil[^1].DogruUzerinde || h.DogruUzerinde };
                continue;
            }

            tekil.Add(h);
        }

        while (tekil.Count > 1 && tekil[0].Kose.Nokta == tekil[^1].Kose.Nokta)
        {
            tekil[0] = tekil[0] with { DogruUzerinde = tekil[0].DogruUzerinde || tekil[^1].DogruUzerinde };
            tekil.RemoveAt(tekil.Count - 1);
        }

        var ilkDisi = tekil.FindIndex(h => !h.DogruUzerinde);
        if (ilkDisi < 0)
        {
            return tekil.Select(h => h.Kose).ToList();
        }

        var donmus = tekil.Skip(ilkDisi).Concat(tekil.Take(ilkDisi)).ToList();
        var sonuc = new List<Kose>(donmus.Count);
        var i = 0;
        while (i < donmus.Count)
        {
            if (!donmus[i].DogruUzerinde)
            {
                sonuc.Add(donmus[i].Kose);
                i++;
                continue;
            }

            var bas = i;
            while (i < donmus.Count && donmus[i].DogruUzerinde)
            {
                i++;
            }

            sonuc.Add(donmus[bas].Kose);
            if (i - 1 > bas)
            {
                sonuc.Add(donmus[i - 1].Kose);
            }
        }

        return sonuc;
    }

    /// <summary>Halkanın <c>x·n ≤ c</c> yarı düzlemindeki alanı (Sutherland–Hodgman; konkavda da alan doğrudur, c ile monoton artar).</summary>
    private static double AlcakAlan(List<Kose> halka, double nx, double ny, double c)
    {
        var n = halka.Count;
        var xs = new List<double>(n + 4);
        var ys = new List<double>(n + 4);
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            var si = (halka[i].X * nx) + (halka[i].Y * ny) - c;
            var sj = (halka[j].X * nx) + (halka[j].Y * ny) - c;
            if (si <= 0d)
            {
                xs.Add(halka[i].X);
                ys.Add(halka[i].Y);
            }

            if ((si < 0d && sj > 0d) || (si > 0d && sj < 0d))
            {
                var lambda = si / (si - sj);
                xs.Add(halka[i].X + (lambda * (halka[j].X - halka[i].X)));
                ys.Add(halka[i].Y + (lambda * (halka[j].Y - halka[i].Y)));
            }
        }

        if (xs.Count < 3)
        {
            return 0d;
        }

        var iki = 0d;
        for (var i = 0; i < xs.Count; i++)
        {
            var j = (i + 1) % xs.Count;
            iki += (xs[i] * ys[j]) - (xs[j] * ys[i]);
        }

        return Math.Abs(iki) / 2d;
    }

    private static (double X, double Y) Yerel(GeodeticPoint p, GeodeticPoint orijin) => ((double)(p.Y - orijin.Y), (double)(p.X - orijin.X));

    private static Kose KoseOlustur(GeodeticPoint p, GeodeticPoint orijin)
    {
        var (x, y) = Yerel(p, orijin);
        return new Kose(p, x, y);
    }

    /// <summary>Yerel (Doğu, Kuzey) çift değerini mutlak jeodezik noktaya 6 ondalık (AwayFromZero) çevirir.</summary>
    private static GeodeticPoint Mutlak(double x, double y, GeodeticPoint orijin) => new(
        X: decimal.Round(orijin.X + (decimal)y, PolygonRing.MaxScale, MidpointRounding.AwayFromZero),
        Y: decimal.Round(orijin.Y + (decimal)x, PolygonRing.MaxScale, MidpointRounding.AwayFromZero));

    private static AppError HataOlustur(string kod, params (string Yer, string Deger)[] ayrinti)
    {
        var e = ErrorCatalog.Get(kod);
        var mesaj = e.TechnicalMessage;
        foreach (var (yer, deger) in ayrinti)
        {
            mesaj = mesaj.Replace(yer, deger, StringComparison.Ordinal);
        }

        return e with { TechnicalMessage = mesaj };
    }

    private static AppError GecersizHata(string neden) => HataOlustur(ErrorCodes.PlanBolmeGirdisiGecersiz, ("{reason}", neden));

    private static Result<T> Gecersiz<T>(string neden) => Result<T>.Failure(GecersizHata(neden));

    private static Result<T> Hata<T>(AppError e) => Result<T>.Failure(e);
}
