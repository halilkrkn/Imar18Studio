using System.Globalization;
using System.Numerics;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;

namespace Imar18.Domain.Mevzuat.Tahsis;

/// <summary>Kadastro parseli maliki: kimlik, tapu hissesi ve AAD Md. 17/1-(d) muvafakat durumu.</summary>
public sealed record KadastroMaliki(string MalikId, Fraction Hisse, bool Muvafakatli);

/// <summary>Düzenlemeye giren kadastro parseli (tapu alanı m², en fazla 2 ondalık).</summary>
public sealed record KadastroParseli(string ParselId, decimal TapuAlaniM2, IReadOnlyList<KadastroMaliki> Malikler);

/// <summary>Oluşan imar parseli: geçici kod ve dengelenmiş alan (m², en fazla 2 ondalık).</summary>
public sealed record ImarParseli(string GeciciKod, decimal AlanM2);

/// <summary>Mühendis girdisi eşleştirme satırı: malikin (kadastro parseli kaynaklı) hakkından imar parseline verilen alan.</summary>
public sealed record HakEslestirmeSatiri(string MalikId, string KadastroParselId, string ImarParselKodu, decimal AlanM2);

/// <summary>Hak eşleştirme girdisi (FR-F1.12). DOPO, DopCalculator'ın 7 haneli değeridir.</summary>
public sealed record HakEslestirmeGirdisi(
    decimal Dopo,
    IReadOnlyList<KadastroParseli> KadastroParselleri,
    IReadOnlyList<ImarParseli> ImarParselleri,
    IReadOnlyList<HakEslestirmeSatiri> Eslestirme)
{
    /// <summary>
    /// Girdinin kanonik SHA-256 revizyon kimliği (D16, AC-PILOT-06). Liste sırasından ve decimal ölçeğinden bağımsızdır;
    /// herhangi bir alan, hisse, muvafakat, eşleştirme veya DOPO değişikliği farklı kimlik üretir.
    /// </summary>
    public RevisionId RevizyonKimligi()
    {
        var o = new RevizyonOzetiOlusturucu();
        Yaz(o);
        return o.Olustur();
    }

    /// <summary>Girdiyi kanonik sırayla (kimlik, ardından tüm değerler — yinelenen kimlikte de sıradan bağımsız) özete yazar.</summary>
    /// <exception cref="ArgumentException">Liste veya malik listesi null ise.</exception>
    internal void Yaz(RevizyonOzetiOlusturucu o)
    {
        if (KadastroParselleri is null || ImarParselleri is null || Eslestirme is null
            || KadastroParselleri.Any(k => k?.Malikler is null || k.Malikler.Any(m => m is null))
            || ImarParselleri.Any(p => p is null) || Eslestirme.Any(r => r is null))
        {
            throw new ArgumentException("Revizyon özeti için girdi listeleri ve malik listeleri null olamaz.");
        }

        o.Ekle("tur", "hak-eslestirme").Ekle("dopo", Dopo);

        var kadastro = KadastroParselleri
            .Select(k => (Parsel: k, Malikler: k.Malikler
                .OrderBy(m => m.MalikId, StringComparer.Ordinal).ThenBy(m => m.Hisse).ThenBy(m => m.Muvafakatli).ToArray()))
            .Select(x => (x.Parsel, x.Malikler, Anahtar: string.Join(
                '\u001F', x.Malikler.Select(m => m.MalikId + "\u001E" + m.Hisse + "\u001E" + (m.Muvafakatli ? "1" : "0")))))
            .OrderBy(x => x.Parsel.ParselId, StringComparer.Ordinal)
            .ThenBy(x => x.Parsel.TapuAlaniM2)
            .ThenBy(x => x.Anahtar, StringComparer.Ordinal)
            .ToArray();
        o.Ekle("kadastroSayisi", kadastro.Length);
        foreach (var (k, malikler, _) in kadastro)
        {
            o.Ekle("k.id", k.ParselId).Ekle("k.tapu", k.TapuAlaniM2).Ekle("k.malikSayisi", malikler.Length);
            foreach (var m in malikler)
            {
                o.Ekle("m.id", m.MalikId).Ekle("m.hisse", m.Hisse).Ekle("m.muvafakat", m.Muvafakatli);
            }
        }

        var imar = ImarParselleri
            .OrderBy(p => p.GeciciKod, StringComparer.Ordinal).ThenBy(p => p.AlanM2).ToArray();
        o.Ekle("imarSayisi", imar.Length);
        foreach (var p in imar)
        {
            o.Ekle("i.kod", p.GeciciKod).Ekle("i.alan", p.AlanM2);
        }

        var satirlar = Eslestirme
            .OrderBy(r => r.MalikId, StringComparer.Ordinal)
            .ThenBy(r => r.KadastroParselId, StringComparer.Ordinal)
            .ThenBy(r => r.ImarParselKodu, StringComparer.Ordinal)
            .ThenBy(r => r.AlanM2)
            .ToArray();
        o.Ekle("satirSayisi", satirlar.Length);
        foreach (var r in satirlar)
        {
            o.Ekle("s.malik", r.MalikId).Ekle("s.kadastro", r.KadastroParselId).Ekle("s.imar", r.ImarParselKodu).Ekle("s.alan", r.AlanM2);
        }
    }
}

/// <summary>Hak sahibinin (kadastro parseli + malik) giren alanı, DOP kesintisi, net hakkı ve toplam tahsisi (m²).</summary>
public sealed record MalikHakki(
    string KadastroParselId,
    string MalikId,
    bool Muvafakatli,
    decimal GirenAlanM2,
    decimal DopKesintisiM2,
    decimal NetHakM2,
    decimal TahsisEdilenM2)
{
    /// <summary>Tahsis − net hak (m²).</summary>
    public decimal FarkM2 => TahsisEdilenM2 - NetHakM2;
}

/// <summary>
/// Tescile esas dağıtım satırı: imar parselindeki paydaş (malik bazında birleştirilmiş), hakkın geldiği kadastro parselleri,
/// dm² alanı ve tescil hissesi (TAM veya pay/payda).
/// </summary>
public sealed record TescilSatiri(string MalikId, IReadOnlyList<string> KadastroParselleri, decimal AlanM2, TescilHissesi Hisse);

/// <summary>İmar parseli tescili (İmar parseline sıralı dağıtım cetveli, spec 25.6).</summary>
public sealed record ImarParselTescili(string GeciciKod, decimal AlanM2, IReadOnlyList<TescilSatiri> Paydaslar)
{
    /// <summary>Tek (farklı) maliki olan müstakil ("TAM") parsel.</summary>
    public bool Mustakil => Paydaslar.Count == 1;
}

/// <summary>Hak eşleştirme sonucu ve bulgular (Critical bulgu onayı engeller — AC-PILOT-05).</summary>
public sealed record HakEslestirmeSonucu(
    IReadOnlyList<MalikHakki> MalikHaklari,
    IReadOnlyList<ImarParselTescili> ImarParselleri,
    IReadOnlyList<AppError> Bulgular)
{
    public bool KritikVar => Bulgular.Any(b => b.Severity == ErrorSeverity.Critical);
}

/// <summary>
/// FR-F1.12 hak eşleştirme, tahsis matrisi ve Md. 17/1-(d) hisse çözümlemesi (ShareResolutionService / AllocationMatrixService).
/// <list type="number">
/// <item>Giren alan: tapu alanı hisselere Largest Remainder ile dm² olarak dağıtılır (Σ = tapu alanı).</item>
/// <item>Net hak (spec 4.4): DOP_i = round(giren_i × DOPO, 2, AwayFromZero), net_i = giren_i − DOP_i.</item>
/// <item>Her imar parseli: Σ eşleştirme = parsel alanı ± 1 dm² (aksi ERR-TAPU-005); dm² payda Largest Remainder ile tam kapanır.</item>
/// <item>Md. 17/1-(d): çok malikli kadastro parselinden muvafakatsız paydaş tek başına (tek farklı malikli, müstakil) parsele tahsis
/// edilemez (ERR-TAPU-007). Müstakillik farklı <c>MalikId</c> sayısına göre belirlenir; aynı malikin farklı kadastro kaynaklı
/// satırları tescilde birleştirilir.</item>
/// <item>Her hak sahibi: |tahsis − net hak| &gt; 1 dm² ise ERR-TAPU-006 uyarısı (mühendis incelemesi).</item>
/// </list>
/// Teknik mesajlara malik kimliği yazılmaz (KVKK); yalnızca sıra numarası ve parsel kodu yazılır. Çıktı girdi sırasından bağımsızdır.
/// </summary>
public static class HakEslestirmeServisi
{
    /// <summary>İmar parseli eşleştirme toplamı ile parsel alanı arasında izin verilen fark (spec 25.2: ±1 dm²).</summary>
    public const int IzinVerilenParselFarkiDm2 = 1;

    private const char Ayirac = '\u001F';

    public static Result<HakEslestirmeSonucu> Eslestir(HakEslestirmeGirdisi girdi)
    {
        ArgumentNullException.ThrowIfNull(girdi);
        ArgumentNullException.ThrowIfNull(girdi.KadastroParselleri);
        ArgumentNullException.ThrowIfNull(girdi.ImarParselleri);
        ArgumentNullException.ThrowIfNull(girdi.Eslestirme);

        if (girdi.Dopo < 0m || girdi.Dopo >= 1m)
        {
            return Gecersiz("DOPO must be in [0, 1)");
        }

        // ---- 1. Kadastro parselleri ve giren alanlar (LR, dm²) ----
        var kadastro = girdi.KadastroParselleri.ToArray();
        if (kadastro.Length == 0)
        {
            return Gecersiz("no cadastral parcels supplied");
        }

        var haklar = new SortedDictionary<string, HakSahibi>(StringComparer.Ordinal);
        var malikSayisi = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < kadastro.Length; i++)
        {
            var k = kadastro[i];
            if (k is null || string.IsNullOrWhiteSpace(k.ParselId) || k.Malikler is null || k.Malikler.Count == 0)
            {
                return Gecersiz($"invalid cadastral parcel #{i + 1}");
            }

            if (!malikSayisi.TryAdd(k.ParselId, k.Malikler.Count))
            {
                return Gecersiz($"duplicate cadastral parcel #{i + 1}");
            }

            if (!PozitifDm2(k.TapuAlaniM2, out _))
            {
                return Gecersiz($"cadastral parcel #{i + 1} area must be positive with at most 2 decimals");
            }

            var dagitim = LargestRemainderAllocator.AllocateAlanM2(
                k.TapuAlaniM2, k.Malikler.Select(m => new AllocationWeight(m?.MalikId ?? string.Empty, m?.Hisse ?? Fraction.Zero)).ToList());
            if (dagitim.IsFailure)
            {
                return Result<HakEslestirmeSonucu>.Failure(dagitim.Error);
            }

            var muvafakat = k.Malikler.ToDictionary(m => m.MalikId, m => m.Muvafakatli, StringComparer.Ordinal);
            foreach (var pay in dagitim.Value)
            {
                haklar.Add(Anahtar(k.ParselId, pay.Id), new HakSahibi(k.ParselId, pay.Id, muvafakat[pay.Id], pay.Units));
            }
        }

        // ---- 2. İmar parselleri ----
        var imar = girdi.ImarParselleri.ToArray();
        if (imar.Length == 0)
        {
            return Gecersiz("no zoning parcels supplied");
        }

        var imarDm2 = new SortedDictionary<string, BigInteger>(StringComparer.Ordinal);
        for (var i = 0; i < imar.Length; i++)
        {
            var p = imar[i];
            if (p is null || string.IsNullOrWhiteSpace(p.GeciciKod) || !PozitifDm2(p.AlanM2, out var dm2))
            {
                return Gecersiz($"invalid zoning parcel #{i + 1}");
            }

            if (!imarDm2.TryAdd(p.GeciciKod, dm2))
            {
                return Gecersiz($"duplicate zoning parcel #{i + 1}");
            }
        }

        // ---- 3. Eşleştirme satırları ----
        var satirlar = girdi.Eslestirme.ToArray();
        var parselSatirlari = imarDm2.Keys.ToDictionary(k => k, _ => new List<(string HakAnahtari, BigInteger Dm2)>(), StringComparer.Ordinal);
        var satirKimlikleri = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < satirlar.Length; i++)
        {
            var s = satirlar[i];
            if (s is null || string.IsNullOrWhiteSpace(s.MalikId) || string.IsNullOrWhiteSpace(s.KadastroParselId)
                || string.IsNullOrWhiteSpace(s.ImarParselKodu))
            {
                return Gecersiz($"empty id at assignment row #{i + 1}");
            }

            var hakAnahtari = Anahtar(s.KadastroParselId, s.MalikId);
            if (!haklar.ContainsKey(hakAnahtari))
            {
                return HataSablonu.Basarisiz<HakEslestirmeSonucu>(ErrorCodes.TapuMalikEslesmedi, ("ownerId", $"at assignment row #{i + 1}"));
            }

            if (!parselSatirlari.TryGetValue(s.ImarParselKodu, out var liste))
            {
                return Gecersiz($"unknown zoning parcel at assignment row #{i + 1}");
            }

            if (!PozitifDm2(s.AlanM2, out var dm2))
            {
                return Gecersiz($"assignment row #{i + 1} area must be positive with at most 2 decimals");
            }

            if (!satirKimlikleri.Add(hakAnahtari + Ayirac + s.ImarParselKodu))
            {
                return Gecersiz($"duplicate assignment row #{i + 1}");
            }

            liste.Add((hakAnahtari, dm2));
        }

        var sira = haklar.Keys.Select((k, n) => (k, n + 1)).ToDictionary(x => x.k, x => x.Item2, StringComparer.Ordinal);
        var tahsis = haklar.Keys.ToDictionary(k => k, _ => BigInteger.Zero, StringComparer.Ordinal);
        var bulgular = new List<AppError>();
        var tesciller = new List<ImarParselTescili>();

        // ---- 4. İmar parseli bazında doğrulama, Md. 17/1-(d) ve dm² tescil ----
        foreach (var (kod, payda) in imarDm2)
        {
            var rows = parselSatirlari[kod].OrderBy(r => r.HakAnahtari, StringComparer.Ordinal).ToList();

            var malikIdleri = rows.Select(r => haklar[r.HakAnahtari].MalikId).Distinct(StringComparer.Ordinal).ToList();
            if (malikIdleri.Count == 1)
            {
                var ihlal = rows.FirstOrDefault(r =>
                    malikSayisi[haklar[r.HakAnahtari].KadastroParselId] > 1 && !haklar[r.HakAnahtari].Muvafakatli);
                if (ihlal.HakAnahtari is not null)
                {
                    bulgular.Add(HataSablonu.Doldur(ErrorCodes.TapuMuvafakatsizMustakilTahsis,
                        ("ownerIndex", Sayi(sira[ihlal.HakAnahtari])), ("parcelId", kod)));
                }
            }

            var toplam = rows.Aggregate(BigInteger.Zero, (t, r) => t + r.Dm2);
            var fark = toplam - payda;
            if (rows.Count == 0 || BigInteger.Abs(fark) > IzinVerilenParselFarkiDm2)
            {
                bulgular.Add(HataSablonu.Doldur(ErrorCodes.TapuImarParselDagitimToplamiUyumsuz,
                    ("parcelId", kod), ("deltaDm2", fark.ToString(CultureInfo.InvariantCulture)),
                    ("allowedDm2", IzinVerilenParselFarkiDm2.ToString(CultureInfo.InvariantCulture))));
                continue;
            }

            var lr = LargestRemainderAllocator.Allocate(
                payda, rows.Select(r => new AllocationWeight(r.HakAnahtari, new Fraction(r.Dm2, toplam))).ToList());
            if (lr.IsFailure)
            {
                return Result<HakEslestirmeSonucu>.Failure(lr.Error);
            }

            var alanM2 = new Fraction(payda, 100).ToDecimal(2);
            foreach (var pay in lr.Value)
            {
                tahsis[pay.Id] += pay.Units;
            }

            // Tescil: aynı malikin farklı kadastro kaynaklı payları birleştirilir; paydaş sayısı = farklı malik sayısı.
            var paydaslar = lr.Value
                .GroupBy(pay => haklar[pay.Id].MalikId, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g =>
                {
                    var units = g.Aggregate(BigInteger.Zero, (t, x) => t + x.Units);
                    var hisse = malikIdleri.Count == 1 ? Fraction.One : new Fraction(units, payda);
                    return new TescilSatiri(
                        g.Key,
                        g.Select(x => haklar[x.Id].KadastroParselId).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                        new Fraction(units, 100).ToDecimal(2),
                        TescilHissesi.Olustur(hisse, alanM2, malikIdleri.Count));
                })
                .ToList();

            tesciller.Add(new ImarParselTescili(kod, alanM2, paydaslar));
        }

        // ---- 5. Net hak (spec 4.4) ve malik toplam kontrolü ----
        var malikHaklari = new List<MalikHakki>(haklar.Count);
        foreach (var (anahtar, hak) in haklar)
        {
            var giren = new Fraction(hak.GirenDm2, 100).ToDecimal(2);
            var dop = Math.Round(giren * girdi.Dopo, 2, MidpointRounding.AwayFromZero);
            var net = giren - dop;
            var tahsisM2 = new Fraction(tahsis[anahtar], 100).ToDecimal(2);
            var mh = new MalikHakki(hak.KadastroParselId, hak.MalikId, hak.Muvafakatli, giren, dop, net, tahsisM2);
            malikHaklari.Add(mh);

            if (Math.Abs(mh.FarkM2) > 0.01m)
            {
                bulgular.Add(HataSablonu.Doldur(ErrorCodes.TapuMalikTahsisiNetHaktanFarkli,
                    ("ownerIndex", Sayi(sira[anahtar])),
                    ("deltaDm2", (mh.FarkM2 * 100m).ToString("0", CultureInfo.InvariantCulture))));
            }
        }

        return Result<HakEslestirmeSonucu>.Success(new HakEslestirmeSonucu(malikHaklari, tesciller, bulgular));
    }

    private static string Anahtar(string kadastroParselId, string malikId) => kadastroParselId + Ayirac + malikId;

    private static string Sayi(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static bool PozitifDm2(decimal alanM2, out BigInteger dm2)
    {
        var f = Fraction.FromDecimal(alanM2) * Fraction.FromInteger(100);
        dm2 = f.Numerator;
        return f.Sign > 0 && f.Denominator.IsOne;
    }

    private static Result<HakEslestirmeSonucu> Gecersiz(string neden) =>
        HataSablonu.Basarisiz<HakEslestirmeSonucu>(ErrorCodes.TapuDagitimGirdisiGecersiz, ("reason", neden));

    private sealed record HakSahibi(string KadastroParselId, string MalikId, bool Muvafakatli, BigInteger GirenDm2);
}
