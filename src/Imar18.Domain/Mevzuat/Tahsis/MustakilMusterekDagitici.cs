using System.Globalization;
using System.Numerics;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Numerics;

namespace Imar18.Domain.Mevzuat.Tahsis;

/// <summary>Krokide parsele düşen malik talebi (malikin net hakkının bu parçaya ayrılan kısmı, m², en fazla 2 ondalık, &gt; 0).</summary>
public sealed record ParselTalebi(string MalikId, decimal AlanM2);

/// <summary>
/// Dağıtılacak yeni imar parseli: kod, kesin alan (m², 2 ondalık, &gt; 0) ve krokideki malik talepleri (boş olabilir).
/// <see cref="Grup"/>: parselin bloğu (aynı bloktaki açık kapasite yakın tahsis için önce seçilir; null ise grup yok).
/// </summary>
public sealed record DagitimParseli(string Kod, decimal AlanM2, IReadOnlyList<ParselTalebi> Talepler)
{
    public string? Grup { get; init; }
}

/// <summary>
/// Hak sahibi (malik + kadastro kaynağı) ve dengelenmiş net hakkı (m², 2 ondalık, ≥ 0). <see cref="MustakilOlabilir"/>: AAD Md. 17/1-(d)
/// gereği bu kaynak tek başına müstakil parsele yazılabilir mi (kadastro tek malikli veya paydaş muvafakatli; bkz.
/// <see cref="HakEslestirmeServisi.MustakilTahsisEdilebilir"/>).
/// </summary>
public sealed record DagitimHakki(string MalikId, string KadastroParselId, decimal AlanM2)
{
    /// <summary>Varsayılan false (güvenli taraf — reviewer D1): çağıran Md. 17/1-(d) durumunu açıkça vermelidir.</summary>
    public bool MustakilOlabilir { get; init; }
}

/// <summary>Dağıtım satırı (Kroki satırı): malikin kadastro kaynağından imar parseline verilen alan.</summary>
public sealed record DagitimSatiri(string MalikId, string KadastroParselId, string ParselKodu, decimal AlanM2);

/// <summary>Parseldeki paydaş: malik, alan ve tescil hissesi (TAM veya dm² pay/payda).</summary>
public sealed record ParselHissedari(string MalikId, decimal AlanM2, TescilHissesi Hisse);

/// <summary>Parselin dağılımı: müstakil (tek paydaş, TAM) veya müşterek (dm² pay/payda).</summary>
public sealed record ParselDagilimi(string Kod, decimal AlanM2, IReadOnlyList<ParselHissedari> Hissedarlar)
{
    /// <summary>Tek (farklı) paydaşlı müstakil parsel.</summary>
    public bool Mustakil => Hissedarlar.Count == 1;
}

/// <summary>
/// Dağıtım sonucu: parsel dağılımları (girdi sırası), satırlar (parsel → paydaş → kaynak sırası) ve Md. 17/1-(d) gereği muvafakat
/// gerektiren müstakil parsellerin kodları (muvafakatsız kaynak kaçınılmaz olarak müstakil parsele düştü; HakEslestirmeServisi
/// ERR-TAPU-007 verir).
/// </summary>
public sealed record MustakilMusterekDagitimSonucu(
    IReadOnlyList<ParselDagilimi> Parseller,
    IReadOnlyList<DagitimSatiri> Satirlar,
    IReadOnlyList<string> Md17IhlalliParseller);

/// <summary>
/// SAHA-016 (SORU-066): müstakil (TAM) + müşterek (dm² pay/payda) akıllı hisse dağıtımı. Tüm hesap tam sayı dm²'dir (BigInteger).
/// <list type="number">
/// <item><b>Ön koşul</b>: Σ parsel alanı = Σ net hak (dm², 0.00 m² fark); aksi ERR-TAPU-004. Her malikin Σ talebi hakkını aşamaz.</item>
/// <item><b>Aşama 1 — müstakil yerleştirme</b>: parselin talep toplamı alanına sığıyorsa talepler aynen verilir, kalan alan açık
/// kapasitedir; sığmıyorsa alan talepler oranında <see cref="LargestRemainderAllocator"/> ile bölünür (eşit kalanda büyük talep, sonra
/// küçük <c>MalikId</c> Ordinal — talep/TAKBİS sırasından bağımsız; reviewer Y1).</item>
/// <item><b>Aşama 2 — müşterek aktarım</b>: her malikin artanı (net hak − Aşama 1; krokide olmayan malikte hakkın tamamı) büyükten
/// küçüğe (eşitte <see cref="StringComparer.Ordinal"/> malik kimliği) açık kapasiteye yazılır: önce malikin zaten bulunduğu parsel,
/// sonra artanın tamamını alabilen aynı blok (<see cref="DagitimParseli.Grup"/>) parseli, sonra en büyük açık kapasiteli parsel (eşitte
/// girdi sırası). Σ artan = Σ açık kapasite olduğundan dağıtım tam kapanır.</item>
/// <item><b>Aşama 3 — kadastro kaynakları</b>: önce müstakil parseller, sonra müşterek parseller işlenir; malikin payı kaynaklarına
/// ardışık bölünür (müstakil parselde önce <see cref="DagitimHakki.MustakilOlabilir"/> kaynaklar — Md. 17/1-(d); ardından kadastro
/// kimliği Ordinal). Her hak sahibinin satır toplamı net hakkına tam eşittir.</item>
/// </list>
/// Tescil: tek paydaşlı parsel <c>TAM</c>, aksi <c>pay/payda</c> (payda = parsel dm²; <see cref="TescilHissesi"/>). Teknik mesajlara
/// malik kimliği yazılmaz (KVKK); yalnız sıra numarası. Çıktı hak listesinin sırasından bağımsızdır.
/// </summary>
public static class MustakilMusterekDagitici
{
    public static Result<MustakilMusterekDagitimSonucu> Dagit(IReadOnlyList<DagitimParseli> parseller, IReadOnlyList<DagitimHakki> haklar)
    {
        ArgumentNullException.ThrowIfNull(parseller);
        ArgumentNullException.ThrowIfNull(haklar);
        if (parseller.Count == 0)
        {
            return Gecersiz("no parcels to distribute");
        }

        if (haklar.Count == 0)
        {
            return Gecersiz("no rights to distribute");
        }

        // ---- Parseller ----
        var n = parseller.Count;
        var alan = new BigInteger[n];
        var kodlar = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            var p = parseller[i];
            if (p is null || string.IsNullOrWhiteSpace(p.Kod) || p.Talepler is null || !kodlar.Add(p.Kod))
            {
                return Gecersiz($"invalid or duplicate parcel #{Sayi(i + 1)}");
            }

            if (!Dm2(p.AlanM2, out alan[i]) || alan[i].Sign <= 0)
            {
                return Gecersiz($"parcel #{Sayi(i + 1)} area must be positive with at most 2 decimals");
            }
        }

        // ---- Hak sahipleri ----
        var kaynaklar = new Dictionary<string, List<Kaynak>>(StringComparer.Ordinal);
        var anahtarlar = new HashSet<(string, string)>();
        var toplamHak = BigInteger.Zero;
        for (var j = 0; j < haklar.Count; j++)
        {
            var h = haklar[j];
            if (h is null || string.IsNullOrWhiteSpace(h.MalikId) || string.IsNullOrWhiteSpace(h.KadastroParselId)
                || !anahtarlar.Add((h.MalikId, h.KadastroParselId)))
            {
                return Gecersiz($"invalid or duplicate right holder #{Sayi(j + 1)}");
            }

            if (!Dm2(h.AlanM2, out var dm2) || dm2.Sign < 0)
            {
                return Gecersiz($"right #{Sayi(j + 1)} must be non-negative with at most 2 decimals");
            }

            if (!kaynaklar.TryGetValue(h.MalikId, out var liste))
            {
                kaynaklar[h.MalikId] = liste = [];
            }

            liste.Add(new Kaynak(h.KadastroParselId, dm2, h.MustakilOlabilir));
            toplamHak += dm2;
        }

        if (alan.Aggregate(BigInteger.Zero, (t, a) => t + a) != toplamHak)
        {
            return Gecersiz("total parcel area must equal total rights");
        }

        foreach (var liste in kaynaklar.Values)
        {
            liste.Sort((x, y) => StringComparer.Ordinal.Compare(x.Kadastro, y.Kadastro));
        }

        var malikHakki = kaynaklar.ToDictionary(k => k.Key, k => k.Value.Aggregate(BigInteger.Zero, (t, x) => t + x.Kalan), StringComparer.Ordinal);

        // ---- Talepler ----
        var talepler = new List<(string Malik, BigInteger Dm2)>[n];
        var talepToplami = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            talepler[i] = [];
            foreach (var t in parseller[i].Talepler)
            {
                if (t is null || string.IsNullOrWhiteSpace(t.MalikId) || !malikHakki.ContainsKey(t.MalikId)
                    || talepler[i].Exists(x => string.Equals(x.Malik, t.MalikId, StringComparison.Ordinal)))
                {
                    return Gecersiz($"unknown or duplicate owner claim in parcel #{Sayi(i + 1)}");
                }

                if (!Dm2(t.AlanM2, out var dm2) || dm2.Sign <= 0)
                {
                    return Gecersiz($"claim in parcel #{Sayi(i + 1)} must be positive with at most 2 decimals");
                }

                talepler[i].Add((t.MalikId, dm2));
                talepToplami[t.MalikId] = talepToplami.GetValueOrDefault(t.MalikId) + dm2;
            }
        }

        if (talepToplami.Any(t => t.Value > malikHakki[t.Key]))
        {
            return Gecersiz("an owner's claims exceed the owner's right");
        }

        // ---- Aşama 1: müstakil yerleştirme ----
        var paylar = new List<Pay>[n];
        var bos = new BigInteger[n];
        var verilen = malikHakki.Keys.ToDictionary(m => m, _ => BigInteger.Zero, StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            paylar[i] = [];
            var c = talepler[i].Aggregate(BigInteger.Zero, (t, x) => t + x.Dm2);
            if (c <= alan[i])
            {
                paylar[i].AddRange(talepler[i].Select(x => new Pay(x.Malik, x.Dm2)));
                bos[i] = alan[i] - c;
            }
            else
            {
                var lr = LargestRemainderAllocator.Allocate(alan[i],
                    talepler[i].Select(x => new AllocationWeight(x.Malik, new Fraction(x.Dm2, c))).ToList());
                if (lr.IsFailure)
                {
                    return Result<MustakilMusterekDagitimSonucu>.Failure(lr.Error);
                }

                for (var k = 0; k < talepler[i].Count; k++)
                {
                    var u = lr.Value.Single(s => string.Equals(s.Id, talepler[i][k].Malik, StringComparison.Ordinal)).Units;
                    if (u.Sign > 0)
                    {
                        paylar[i].Add(new Pay(talepler[i][k].Malik, u));
                    }
                }

                bos[i] = BigInteger.Zero;
            }

            foreach (var p in paylar[i])
            {
                verilen[p.Malik] += p.Dm2;
            }
        }

        // ---- Aşama 2: müşterek aktarım (büyük artan → küçük kimlik; önce kendi parseli, sonra en büyük açık kapasite) ----
        var artanlar = malikHakki
            .Select(m => (Malik: m.Key, Artan: m.Value - verilen[m.Key]))
            .Where(x => x.Artan.Sign > 0)
            .OrderByDescending(x => x.Artan)
            .ThenBy(x => x.Malik, StringComparer.Ordinal)
            .ToList();
        foreach (var (malik, artan) in artanlar)
        {
            var kalan = artan;
            var gruplar = Enumerable.Range(0, n)
                .Where(i => parseller[i].Grup is not null && paylar[i].Exists(p => string.Equals(p.Malik, malik, StringComparison.Ordinal)))
                .Select(i => parseller[i].Grup!)
                .ToHashSet(StringComparer.Ordinal);
            while (kalan.Sign > 0)
            {
                var hedef = -1;
                for (var i = 0; i < n; i++)
                {
                    if (bos[i].Sign <= 0)
                    {
                        continue;
                    }

                    if (hedef < 0 || Once(i, hedef))
                    {
                        hedef = i;
                    }
                }

                if (hedef < 0)
                {
                    return Gecersiz("open capacity exhausted before all rights were placed");
                }

                var al = BigInteger.Min(kalan, bos[hedef]);
                var mevcut = paylar[hedef].FindIndex(p => string.Equals(p.Malik, malik, StringComparison.Ordinal));
                if (mevcut >= 0)
                {
                    paylar[hedef][mevcut] = paylar[hedef][mevcut] with { Dm2 = paylar[hedef][mevcut].Dm2 + al };
                }
                else
                {
                    paylar[hedef].Add(new Pay(malik, al));
                }

                bos[hedef] -= al;
                kalan -= al;
            }

            // Öncelik: (1) malikin zaten bulunduğu parsel, (2) kalanın tamamını alan aynı blok parseli, (3) büyük açık kapasite, (4) sıra.
            bool Once(int i, int j)
            {
                var kendiI = Kendi(i);
                var kendiJ = Kendi(j);
                if (kendiI != kendiJ)
                {
                    return kendiI;
                }

                var blokI = AyniBlokVeSigar(i);
                var blokJ = AyniBlokVeSigar(j);
                return blokI != blokJ ? blokI : bos[i] > bos[j];
            }

            bool Kendi(int i) => paylar[i].Exists(p => string.Equals(p.Malik, malik, StringComparison.Ordinal));

            bool AyniBlokVeSigar(int i) => bos[i] >= kalan && parseller[i].Grup is { } g && gruplar.Contains(g);
        }

        // ---- Aşama 3: kadastro kaynakları (önce müstakil parseller — Md. 17/1-(d) güvenli kaynak önce) ve tescil ----
        var parselSatirlari = new List<DagitimSatiri>[n];
        var ihlalli = new SortedSet<int>();
        foreach (var i in Enumerable.Range(0, n).OrderBy(i => paylar[i].Count == 1 ? 0 : 1).ThenBy(i => i))
        {
            parselSatirlari[i] = [];
            var mustakil = paylar[i].Count == 1;
            foreach (var p in paylar[i])
            {
                var kalan = p.Dm2;
                var sirali = mustakil ? kaynaklar[p.Malik].OrderBy(k => k.MustakilOlabilir ? 0 : 1).ToList() : kaynaklar[p.Malik];
                foreach (var k in sirali)
                {
                    if (kalan.Sign <= 0)
                    {
                        break;
                    }

                    var al = BigInteger.Min(kalan, k.Kalan);
                    if (al.Sign <= 0)
                    {
                        continue;
                    }

                    parselSatirlari[i].Add(new DagitimSatiri(p.Malik, k.Kadastro, parseller[i].Kod, M2(al)));
                    if (mustakil && !k.MustakilOlabilir)
                    {
                        ihlalli.Add(i);
                    }

                    k.Kalan -= al;
                    kalan -= al;
                }
            }
        }

        var satirlar = parselSatirlari.SelectMany(l => l).ToList();
        var dagilim = new List<ParselDagilimi>(n);
        for (var i = 0; i < n; i++)
        {
            var alanM2 = M2(alan[i]);
            var hissedarlar = paylar[i]
                .Select(p => new ParselHissedari(p.Malik, M2(p.Dm2),
                    TescilHissesi.Olustur(paylar[i].Count == 1 ? Fraction.One : new Fraction(p.Dm2, alan[i]), alanM2, paylar[i].Count)))
                .ToList();
            dagilim.Add(new ParselDagilimi(parseller[i].Kod, alanM2, hissedarlar.AsReadOnly()));
        }

        return Result<MustakilMusterekDagitimSonucu>.Success(new MustakilMusterekDagitimSonucu(
            dagilim.AsReadOnly(), satirlar.AsReadOnly(), ihlalli.Select(i => parseller[i].Kod).ToList().AsReadOnly()));
    }

    private sealed record Pay(string Malik, BigInteger Dm2);

    private sealed class Kaynak(string kadastro, BigInteger kalan, bool mustakilOlabilir)
    {
        public string Kadastro { get; } = kadastro;

        public bool MustakilOlabilir { get; } = mustakilOlabilir;

        public BigInteger Kalan { get; set; } = kalan;
    }

    private static bool Dm2(decimal alanM2, out BigInteger dm2)
    {
        var f = Fraction.FromDecimal(alanM2) * Fraction.FromInteger(100);
        dm2 = f.Numerator;
        return f.Denominator.IsOne;
    }

    private static decimal M2(BigInteger dm2) => new Fraction(dm2, 100).ToDecimal(2);

    private static string Sayi(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static Result<MustakilMusterekDagitimSonucu> Gecersiz(string neden) =>
        HataSablonu.Basarisiz<MustakilMusterekDagitimSonucu>(ErrorCodes.TapuDagitimGirdisiGecersiz, ("reason", neden));
}
