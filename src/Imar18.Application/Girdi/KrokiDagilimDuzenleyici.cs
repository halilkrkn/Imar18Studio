using System.Globalization;
using System.Numerics;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;

namespace Imar18.Application.Girdi;

/// <summary>SAHA-019: hak sahibi (malik + kadastro kaynağı) ve otomatik dağıtımın dengelenmiş net hakkı (m², 2 ondalık).</summary>
public sealed record KrokiHakHedefi(string MalikId, string KadastroParselId, decimal NetHakM2);

/// <summary>SAHA-019: düzenlenmiş dağılımın canlı denetimi — hak sahibi ve parsel bazında hedef ↔ dağıtılan farkları.</summary>
public sealed record KrokiDagilimDenetimi(
    IReadOnlyList<(KrokiHakHedefi Hedef, decimal DagitilanM2)> Haklar,
    IReadOnlyList<(string ParselKodu, decimal AlanM2, decimal DagitilanM2)> Parseller)
{
    /// <summary>Tüm hak sahipleri ve parseller 0.00 m² farkla kapanıyor mu.</summary>
    public bool Dengeli => Haklar.All(h => h.DagitilanM2 == h.Hedef.NetHakM2) && Parseller.All(p => p.DagitilanM2 == p.AlanM2);
}

/// <summary>
/// SAHA-019 (mühendis talebi): Yaklaşık Yer Krokisi parselasyon önizlemesinde otomatik <see cref="MustakilMusterekDagitici"/>
/// dağılımını mühendisin elle düzenlediği satırlarla (<c>İmar Parseli · Malik · Kadastro Kaynağı · Alan</c>) değiştirir.
/// <list type="bullet">
/// <item><b>Hedefler</b>: otomatik önizlemenin satırlarından hak sahibi (malik, kadastro) başına dengelenmiş net hak (Σ hedef = Σ
/// parsel alanı). Elle düzenleme hakları değiştiremez; yalnız hangi malikin hangi parselde ne kadar alanla (müstakil / müşterek)
/// yer alacağını değiştirir.</item>
/// <item><b>Doğrulama</b>: her satır geçerli parsel, hedeflerde bulunan hak sahibi ve &gt; 0, en fazla 2 ondalık alan taşır; her
/// parselde Σ hisse = parsel alanı ve her hak sahibinde Σ dağıtılan = net hak (0.00 m²) olmalıdır, aksi <c>ERR-TAPU-004</c>.</item>
/// <item><b>Sonuç</b>: geometri, kodlar ve alanlar korunur; parsel hissedarları tek malikte <c>TAM</c>, çok malikte dm² pay/payda
/// (<see cref="TescilHissesi"/>, ortak payda sadeleştirilmez) olur; Kroki satırları ve net hak farkları yenilenir, önizleme
/// <see cref="KrokiParselasyonOnizleme.ElleDuzenlendi"/> işaretlenir. TAKBİS verilirse AAD Md. 17/1-(d) muvafakat uyarısı elle dağılım için
/// yeniden hesaplanır; Hak Eşleştirme ayrıca denetler.</item>
/// </list>
/// Teknik mesajlara malik kimliği yazılmaz (KVKK); yalnız satır/parsel sıra numarası.
/// </summary>
public static class KrokiDagilimDuzenleyici
{
    /// <summary>Elle düzenleme uyarısı (önizleme uyarı listesinin başına eklenir).</summary>
    public const string ElleDuzenlemeUyarisi = "Müşterek / müstakil dağılım mühendis tarafından elle düzenlendi (SAHA-019).";

    /// <summary>TAKBİS verilmediğinde Md. 17/1-(d) denetiminin yapılamadığı uyarısı.</summary>
    public const string Md17DenetlenemediUyarisi =
        "AAD Md. 17/1-(d) muvafakat denetimi elle dağılımda yapılamadı (TAKBİS hisseleri okunamadı); Hak Eşleştirmede denetlenir.";

    /// <summary>Otomatik (veya düzenlenmiş) önizlemenin hak sahibi hedefleri: Kroki satırlarının (malik, kadastro) toplamları.</summary>
    public static IReadOnlyList<KrokiHakHedefi> Hedefler(KrokiParselasyonOnizleme onizleme)
    {
        ArgumentNullException.ThrowIfNull(onizleme);
        return onizleme.Satirlar
            .GroupBy(s => (s.MalikKodu, s.KadastroParselKimligi))
            .Select(g => new KrokiHakHedefi(g.Key.MalikKodu, g.Key.KadastroParselKimligi, g.Sum(s => s.AlanM2)))
            .ToList().AsReadOnly();
    }

    /// <summary>Canlı denetim: hedeflere ve parsel alanlarına göre dağıtılan toplamlar (geçersiz/eşleşmeyen satırlar toplamlara girmez).</summary>
    public static KrokiDagilimDenetimi Denetle(
        KrokiParselasyonOnizleme onizleme, IReadOnlyList<KrokiHakHedefi> hedefler, IReadOnlyList<KrokiEslesmeGirdisi> satirlar)
    {
        ArgumentNullException.ThrowIfNull(onizleme);
        ArgumentNullException.ThrowIfNull(hedefler);
        ArgumentNullException.ThrowIfNull(satirlar);
        var gecerli = satirlar.Where(s => s is not null).ToList();
        var haklar = hedefler.Select(h => (h, gecerli
                .Where(s => s.MalikKodu == h.MalikId && s.KadastroParselKimligi == h.KadastroParselId)
                .Sum(s => s.AlanM2)))
            .ToList();
        var parseller = onizleme.Parseller.Select(p => (p.Kod, p.HedefAlanM2, gecerli.Where(s => s.ImarParselKodu == p.Kod).Sum(s => s.AlanM2))).ToList();
        return new KrokiDagilimDenetimi(haklar.AsReadOnly(), parseller.AsReadOnly());
    }

    /// <summary>Düzenlenmiş satırları önizlemeye uygular (bkz. sınıf açıklaması). Başarısızlıkta önizleme değişmez.</summary>
    /// <param name="takbis">
    /// Reviewer O4: verilirse AAD Md. 17/1-(d) muvafakat denetimi elle dağılım için yeniden yapılır (çok malikli kadastro parselinin
    /// muvafakatsız paydaşı tek başına müstakil parsele düşerse motorla aynı uyarı); null ise <see cref="Md17DenetlenemediUyarisi"/>.
    /// </param>
    public static Result<KrokiParselasyonOnizleme> Uygula(
        KrokiParselasyonOnizleme onizleme, IReadOnlyList<KrokiHakHedefi> hedefler, IReadOnlyList<KrokiEslesmeGirdisi> satirlar,
        IReadOnlyList<KadastroParseli>? takbis = null)
    {
        ArgumentNullException.ThrowIfNull(onizleme);
        ArgumentNullException.ThrowIfNull(hedefler);
        ArgumentNullException.ThrowIfNull(satirlar);
        if (satirlar.Count == 0)
        {
            return Gecersiz("no distribution rows");
        }

        var parselSira = onizleme.Parseller.Select((p, i) => (p.Kod, i)).ToDictionary(x => x.Kod, x => x.i, StringComparer.Ordinal);
        var hedef = new Dictionary<(string, string), decimal>();
        foreach (var h in hedefler)
        {
            hedef[(h.MalikId, h.KadastroParselId)] = h.NetHakM2;
        }

        // Aynı (parsel, malik, kaynak) satırları birleştirilir; satır sırası korunur.
        var birlesik = new List<KrokiEslesmeGirdisi>();
        for (var i = 0; i < satirlar.Count; i++)
        {
            var s = satirlar[i];
            var no = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (s is null || string.IsNullOrWhiteSpace(s.ImarParselKodu) || !parselSira.ContainsKey(s.ImarParselKodu))
            {
                return Gecersiz($"row #{no} zoning parcel is not in the preview");
            }

            if (string.IsNullOrWhiteSpace(s.MalikKodu) || string.IsNullOrWhiteSpace(s.KadastroParselKimligi)
                || !hedef.ContainsKey((s.MalikKodu, s.KadastroParselKimligi)))
            {
                return Gecersiz($"row #{no} owner/cadastral source is not a right holder of the preview");
            }

            if (s.AlanM2 <= 0m || decimal.Round(s.AlanM2, 2) != s.AlanM2)
            {
                return Gecersiz($"row #{no} area must be positive with at most 2 decimals");
            }

            var j = birlesik.FindIndex(x => x.ImarParselKodu == s.ImarParselKodu && x.MalikKodu == s.MalikKodu && x.KadastroParselKimligi == s.KadastroParselKimligi);
            if (j >= 0)
            {
                birlesik[j] = birlesik[j] with { AlanM2 = birlesik[j].AlanM2 + s.AlanM2 };
            }
            else
            {
                birlesik.Add(s);
            }
        }

        var denetim = Denetle(onizleme, hedefler, birlesik);
        var acikParsel = denetim.Parseller.Select((p, i) => (p, i)).FirstOrDefault(x => x.p.DagitilanM2 != x.p.AlanM2);
        if (acikParsel != default)
        {
            return Gecersiz($"parcel #{(acikParsel.i + 1).ToString(CultureInfo.InvariantCulture)} shares do not sum to its area");
        }

        var acikHak = denetim.Haklar.Select((h, i) => (h, i)).FirstOrDefault(x => x.h.DagitilanM2 != x.h.Hedef.NetHakM2);
        if (acikHak != default)
        {
            return Gecersiz($"right holder #{(acikHak.i + 1).ToString(CultureInfo.InvariantCulture)} distributed area does not equal the net right");
        }

        // Satırlar parsel sırasıyla (aynı parselde girdi sırasıyla) dizilir.
        var sirali = birlesik.Select((s, i) => (s, i)).OrderBy(x => parselSira[x.s.ImarParselKodu]).ThenBy(x => x.i).Select(x => x.s).ToList();
        var yeniParseller = new List<KrokiParseli>(onizleme.Parseller.Count);
        foreach (var p in onizleme.Parseller)
        {
            var paydaslar = sirali.Where(s => s.ImarParselKodu == p.Kod)
                .GroupBy(s => s.MalikKodu, StringComparer.Ordinal)
                .Select(g => (Malik: g.Key, Alan: g.Sum(s => s.AlanM2)))
                .ToList();
            var paydaDm2 = TescilHissesi.PaydaDm2(p.HedefAlanM2);
            var hissedarlar = paydaslar.Select(x => new ParselHissedari(x.Malik, x.Alan, TescilHissesi.Olustur(
                    paydaslar.Count == 1 ? Fraction.One : new Fraction(Dm2(x.Alan), paydaDm2), p.HedefAlanM2, paydaslar.Count)))
                .ToList();
            yeniParseller.Add(p with
            {
                Malikler = hissedarlar.Select(h => h.MalikId).ToList().AsReadOnly(),
                Hissedarlar = hissedarlar.AsReadOnly(),
            });
        }

        var uyarilar = new List<string> { ElleDuzenlemeUyarisi };
        if (takbis is null)
        {
            uyarilar.Add(Md17DenetlenemediUyarisi);
        }
        else
        {
            var kaynaklar = takbis.ToDictionary(k => k.ParselId, StringComparer.Ordinal);
            var ihlalli = yeniParseller.Where(p => p.Mustakil && sirali.Any(s => s.ImarParselKodu == p.Kod
                    && kaynaklar.TryGetValue(s.KadastroParselKimligi, out var k)
                    && k.Malikler.FirstOrDefault(m => m.MalikId == s.MalikKodu) is { } m
                    && !HakEslestirmeServisi.MustakilTahsisEdilebilir(k.Malikler.Count, m.Muvafakatli)))
                .Select(p => p.Kod).ToList();
            if (ihlalli.Count > 0)
            {
                uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{ihlalli.Count} müstakil parsel ({string.Join(", ", ihlalli)}) AAD Md. 17/1-(d) gereği muvafakat gerektiriyor (çok malikli kadastro parselinin muvafakatsız paydaşı tek başına); TAKBİS muvafakatını girin veya dağılımı düzeltin, aksi halde Hak Eşleştirme kritik bulgu verir."));
            }
        }

        // Otomatik dağılımın Md. 17/1-(d) uyarısı elle dağılımda geçersizdir (yukarıda yeniden hesaplandı).
        uyarilar.AddRange(onizleme.Uyarilar.Where(u => u != ElleDuzenlemeUyarisi && u != Md17DenetlenemediUyarisi && !u.Contains("Md. 17/1-(d)", StringComparison.Ordinal)));
        var farklar = onizleme.Farklar
            .Select(f => f with { KrokiM2 = sirali.Where(s => s.MalikKodu == f.MalikKodu && s.KadastroParselKimligi == f.KadastroParselId).Sum(s => s.AlanM2) })
            .ToList();

        return Result<KrokiParselasyonOnizleme>.Success(onizleme with
        {
            Parseller = yeniParseller.AsReadOnly(),
            Satirlar = sirali.AsReadOnly(),
            Farklar = farklar.AsReadOnly(),
            Uyarilar = uyarilar.AsReadOnly(),
            ElleDuzenlendi = true,
        });
    }

    private static BigInteger Dm2(decimal alanM2) => (Fraction.FromDecimal(alanM2) * Fraction.FromInteger(100)).Numerator;

    private static Result<KrokiParselasyonOnizleme> Gecersiz(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.TapuDagitimGirdisiGecersiz);
        return Result<KrokiParselasyonOnizleme>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", neden, StringComparison.Ordinal) });
    }
}
