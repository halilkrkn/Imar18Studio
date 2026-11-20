using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;

namespace Imar18.Domain.Revizyon;

/// <summary>
/// P3 hesap revizyonu (D16, AC-PILOT-06): tecviz girdileri (sınır, ölçek, yöntem, kalite, kaynak notu, toplam sınır),
/// DOP girdisi (R1, D1, R2, D2, R3, ortak katılım) ve hak eşleştirme girdisinin tek SHA-256 kimliği.
/// Bunlardan herhangi biri değişirse kimlik değişir ve <see cref="MuhendisOnayi"/> geçersizleşir.
/// Geometri ve CRS dahil proje kimliği P5.2'de <see cref="ProjeRevizyonu"/>'dur.
/// </summary>
public static class HesapRevizyonu
{
    public static RevisionId Hesapla(
        HakEslestirmeGirdisi hakEslestirme,
        DopGirdisi dop,
        IReadOnlyCollection<TecvizParselGirdisi> tecvizParselleri,
        decimal? duzenlemeToplamTecvizSiniriM2)
    {
        ArgumentNullException.ThrowIfNull(hakEslestirme);
        ArgumentNullException.ThrowIfNull(dop);
        ArgumentNullException.ThrowIfNull(tecvizParselleri);

        var o = new RevizyonOzetiOlusturucu().Ekle("tur", "p3-hesap");
        DopYaz(o, dop);
        TecvizYaz(o, tecvizParselleri, duzenlemeToplamTecvizSiniriM2);
        hakEslestirme.Yaz(o);
        return o.Olustur();
    }

    internal static void DopYaz(RevizyonOzetiOlusturucu o, DopGirdisi dop) =>
        o.Ekle("dop.r1", dop.R1).Ekle("dop.d1", dop.D1).Ekle("dop.r2", dop.R2).Ekle("dop.d2", dop.D2)
            .Ekle("dop.r3", dop.R3).Ekle("dop.ortak", dop.OrtakKatilimM2);

    internal static void TecvizYaz(
        RevizyonOzetiOlusturucu o,
        IReadOnlyCollection<TecvizParselGirdisi> tecvizParselleri,
        decimal? duzenlemeToplamTecvizSiniriM2)
    {
        if (tecvizParselleri.Any(t => t is null))
        {
            throw new ArgumentException("Tecviz parsel girdisi null olamaz.", nameof(tecvizParselleri));
        }

        var tecviz = tecvizParselleri
            .Select(t => (Parsel: t, Anahtar: t.Tecviz is null
                ? "-"
                : string.Join(
                    '\u001F',
                    t.Tecviz.OlcekPaydaM.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    t.Tecviz.UretimYontemi,
                    t.Tecviz.KoordinatKalite.ToString(),
                    Numerics.Fraction.FromDecimal(t.Tecviz.TecvizSiniriM2).ToString(),
                    t.Tecviz.KaynakNotu)))
            .OrderBy(x => x.Parsel.ParselId, StringComparer.Ordinal)
            .ThenBy(x => x.Parsel.TapuAlaniM2)
            .ThenBy(x => x.Parsel.HesapAlaniM2)
            .ThenBy(x => x.Anahtar, StringComparer.Ordinal)
            .Select(x => x.Parsel)
            .ToArray();
        o.Ekle("tecvizSayisi", tecviz.Length);
        foreach (var t in tecviz)
        {
            o.Ekle("t.id", t.ParselId).Ekle("t.tapu", t.TapuAlaniM2).Ekle("t.hesap", t.HesapAlaniM2).Ekle("t.girdiVar", t.Tecviz is not null);
            if (t.Tecviz is { } g)
            {
                o.Ekle("t.olcek", g.OlcekPaydaM).Ekle("t.yontem", g.UretimYontemi).Ekle("t.kalite", g.KoordinatKalite.ToString())
                    .Ekle("t.sinir", g.TecvizSiniriM2).Ekle("t.kaynak", g.KaynakNotu);
            }
        }

        o.Ekle("t.toplamVar", duzenlemeToplamTecvizSiniriM2.HasValue);
        if (duzenlemeToplamTecvizSiniriM2 is { } toplam)
        {
            o.Ekle("t.toplam", toplam);
        }
    }
}

/// <summary>
/// Proje revizyon girdisi (P5.2, AC-PILOT-06, spec 11.2 madde 9): proje adı, CRS (datum, dilim, epok, ölçek), parsel
/// geometrileri (rol + köşeler), DOP, tecviz, hak eşleştirme ve geçici↔tescil kimlik eşlemesi. Henüz girilmemiş bileşen
/// <c>null</c>'dır; "yok" ile "boş" ayırt edilir.
/// </summary>
public sealed record ProjeRevizyonGirdisi(
    string ProjeAdi,
    ProjeCrs? Crs,
    IReadOnlyList<ParselGeometrisi> Geometri,
    DopGirdisi? Dop,
    IReadOnlyList<TecvizParselGirdisi> TecvizParselleri,
    decimal? DuzenlemeToplamTecvizSiniriM2,
    HakEslestirmeGirdisi? HakEslestirme,
    IReadOnlyList<ParselKimlikCifti> KimlikEslemesi)
{
    /// <summary>
    /// P7.3-C2: ortak noktalı dönüşüm girdisi. Yalnız girilmişse özete yazılır (P7.3 öncesi projelerin RevisionId'si değişmez).
    /// </summary>
    public DonusumGirdisi? Donusum { get; init; }

    /// <summary>P7.3-C2: proje künyesi (resmî çıktı başlıkları). Yalnız girilmişse özete yazılır.</summary>
    public ProjeKunyesi? Kunye { get; init; }

    /// <summary>
    /// SORU-046: Malik Rehberi içeriğinin SHA-256 özeti (Application hesaplar; ad/baba adı Domain'e ve denetime girmez). Yalnız
    /// rehber doluysa verilir → rehbersiz projelerin RevisionId'si değişmez.
    /// </summary>
    public string? MalikRehberiOzeti { get; init; }

    /// <summary>
    /// P7.3-C3: kaynak <c>.NCZ</c> şablonunun SHA-256 özeti (Application hesaplar). Şablon <c>PARSELASYON HARİTASI.NCZ</c> baytlarını
    /// belirler; yalnız varsa özete yazılır → şablonsuz projelerin RevisionId'si değişmez.
    /// </summary>
    public string? NczSablonuOzeti { get; init; }

    /// <summary>SAHA-014: <c>.DNS</c> ileri dönüşüm katsayıları. Yalnız doluysa özete yazılır (DNS'siz projelerin RevisionId'si değişmez).</summary>
    public DonusumKatsayilari? IleriDonusum { get; init; }

    /// <summary>
    /// SAHA-014: Deprem Öncesi (orijinal CAD) parseller; Fen Klasörü'ne basıldıkları için <see cref="IleriDonusum"/> doluyken özete
    /// girer (6 ondalık yuvarlama çoka-bir olduğundan aktif geometriden türetilemez).
    /// </summary>
    public IReadOnlyList<ParselGeometrisi>? DepremOncesiGeometri { get; init; }
}

/// <summary>
/// Proje revizyon kimliği (SHA-256, şema <c>p5-proje</c>). Parsel listesi kimliğe göre sıralanır (girdi sırasından bağımsız);
/// köşe dizisi ise olduğu gibi yazılır (başlangıç köşesi veya yön değişikliği veri değişikliği sayılır — muhafazakâr kural).
/// Ondalıklar sadeleştirilmiş kesir biçimindedir (1.10 ≡ 1.1).
/// </summary>
public static class ProjeRevizyonu
{
    /// <summary>Pilot mevzuat profili (tek uzman onaylı profil — spec 18).</summary>
    public const string MevzuatProfiliSurumu = "TPYS_2022";

    /// <summary>Hesap kuralları sürümü: Domain/Application hesap kuralları değiştiğinde artırılır; eski onaylar düşer.</summary>
    public const string KuralSurumu = "p6-1"; // P6: parseller arası topoloji (çakışma/boşluk/taşma) onay kuralı eklendi

    /// <exception cref="ArgumentException">Liste null veya null öğe içeriyorsa.</exception>
    public static RevisionId Hesapla(ProjeRevizyonGirdisi girdi)
    {
        ArgumentNullException.ThrowIfNull(girdi);
        if (girdi.ProjeAdi is null || girdi.Geometri is null || girdi.TecvizParselleri is null || girdi.KimlikEslemesi is null
            || girdi.Geometri.Any(p => p?.ParselId is null || p.Koseler is null) || girdi.KimlikEslemesi.Any(k => k is null))
        {
            throw new ArgumentException("Proje revizyon girdisinde null liste veya öğe olamaz.", nameof(girdi));
        }

        // Kural/profil/katalog sürümleri de "parametre"dir (spec 11.2 madde 9, P5 reviewer M5): değişirlerse onay düşer.
        var o = new RevizyonOzetiOlusturucu().Ekle("tur", "p5-proje")
            .Ekle("profil", MevzuatProfiliSurumu).Ekle("kural", KuralSurumu).Ekle("katalog", Diagnostics.ErrorCatalog.Version)
            .Ekle("ad", girdi.ProjeAdi);

        o.Ekle("crsVar", girdi.Crs is not null);
        if (girdi.Crs is { } crs)
        {
            o.Ekle("crs.id", crs.CrsId).Ekle("crs.datum", crs.Datum.ToString()).Ekle("crs.meridyen", crs.OrtaMeridyenDerece)
                .Ekle("crs.epokVar", crs.Epok.HasValue).Ekle("crs.olcek", crs.OlcekPaydasi);
            if (crs.Epok is { } epok)
            {
                o.Ekle("crs.epok", epok);
            }
        }

        var parseller = girdi.Geometri
            .OrderBy(p => p.ParselId, StringComparer.Ordinal)
            .ThenBy(p => p.Rol)
            .ThenBy(p => string.Join(';', p.Koseler.Select(k => Numerics.Fraction.FromDecimal(k.X) + "," + Numerics.Fraction.FromDecimal(k.Y))), StringComparer.Ordinal)
            .ToArray();
        o.Ekle("geoSayisi", parseller.Length);
        GeometriYaz(o, "g", parseller);

        o.Ekle("dopVar", girdi.Dop is not null);
        if (girdi.Dop is { } dop)
        {
            HesapRevizyonu.DopYaz(o, dop);
        }

        HesapRevizyonu.TecvizYaz(o, girdi.TecvizParselleri, girdi.DuzenlemeToplamTecvizSiniriM2);

        o.Ekle("hakVar", girdi.HakEslestirme is not null);
        girdi.HakEslestirme?.Yaz(o);

        var kimlik = girdi.KimlikEslemesi
            .OrderBy(k => k.GeciciKod, StringComparer.Ordinal).ThenBy(k => k.TescilNo, StringComparer.Ordinal).ToArray();
        o.Ekle("kimlikSayisi", kimlik.Length);
        foreach (var k in kimlik)
        {
            o.Ekle("k.gecici", k.GeciciKod).Ekle("k.tescil", k.TescilNo);
        }

        // P7.3-C2: girilmemiş bileşen özete hiç yazılmaz → mevcut projelerin RevisionId'si korunur.
        if (girdi.Donusum is { } d)
        {
            if (d.OrtakNoktalar is null || d.OrtakNoktalar.Any(n => n?.NoktaNo is null))
            {
                throw new ArgumentException("Dönüşüm ortak nokta listesi veya nokta no null olamaz.", nameof(girdi));
            }

            // Reviewer L10: enum adı yerine sabit kod (enum yeniden adlandırılırsa kimlikler değişmesin).
            // SAHA-014: PDF katsayı etiketleri Netcad .DNS düzenine (Y2 = A·Y1 + C·X1 + CY) düzeltildi; dönüşümlü projelerin
            // basılı içeriği değiştiği için onayları bir kez düşer (muhafazakâr, AC-PILOT-06).
            o.Ekle("donusum.duzen", "netcad-dns");
            o.Ekle("donusum.model", d.Model == DonusumModeli.Helmert2B ? "H2B" : d.Model == DonusumModeli.Afin2B ? "A2B" : "?" + ((int)d.Model).ToString(System.Globalization.CultureInfo.InvariantCulture)).Ekle("donusum.mk", d.Mk).Ekle("donusum.noktaSayisi", d.OrtakNoktalar.Count);
            foreach (var n in d.OrtakNoktalar.OrderBy(n => n.NoktaNo, StringComparer.Ordinal))
            {
                o.Ekle("dn.no", n.NoktaNo).Ekle("dn.y1", n.Y1).Ekle("dn.x1", n.X1).Ekle("dn.y2", n.Y2).Ekle("dn.x2", n.X2);
            }
        }

        // SAHA-014: ileri dönüşüm yalnız uygulanmışsa yazılır; yuvarlama kuralı sürümü de parametredir.
        if (girdi.IleriDonusum is { } ileri)
        {
            if (girdi.DepremOncesiGeometri is not { } orj || orj.Any(p => p?.ParselId is null || p.Koseler is null))
            {
                throw new ArgumentException("İleri dönüşümde Deprem Öncesi parsel listesi null olamaz.", nameof(girdi));
            }

            o.Ekle("ileri.surum", "r6-afz").Ekle("ileri.a", ileri.A).Ekle("ileri.b", ileri.B).Ekle("ileri.c", ileri.C)
                .Ekle("ileri.d", ileri.D).Ekle("ileri.cy", ileri.Cy).Ekle("ileri.cx", ileri.Cx).Ekle("ileri.geoSayisi", orj.Count);
            GeometriYaz(o, "og", orj.OrderBy(p => p.ParselId, StringComparer.Ordinal).ThenBy(p => p.Rol)
                .ThenBy(p => string.Join(';', p.Koseler.Select(k => Numerics.Fraction.FromDecimal(k.X) + "," + Numerics.Fraction.FromDecimal(k.Y))), StringComparer.Ordinal)
                .ToArray());
        }

        if (girdi.MalikRehberiOzeti is { } rehber)
        {
            o.Ekle("rehber.sha256", rehber);
        }

        if (girdi.NczSablonuOzeti is { } ncz)
        {
            o.Ekle("ncz.sha256", ncz);
        }

        if (girdi.Kunye is { } kunye)
        {
            foreach (var (ad, deger) in kunye.Alanlar())
            {
                o.Ekle("kunye." + ad, deger ?? throw new ArgumentException("Künye alanı null olamaz.", nameof(girdi)));
            }
        }

        return o.Olustur();
    }

    private static void GeometriYaz(RevizyonOzetiOlusturucu o, string onek, IReadOnlyList<ParselGeometrisi> parseller)
    {
        foreach (var p in parseller)
        {
            o.Ekle(onek + ".id", p.ParselId).Ekle(onek + ".rol", p.Rol.ToString()).Ekle(onek + ".koseSayisi", p.Koseler.Count);
            foreach (var k in p.Koseler)
            {
                o.Ekle(onek + ".x", k.X).Ekle(onek + ".y", k.Y);
            }
        }
    }
}
