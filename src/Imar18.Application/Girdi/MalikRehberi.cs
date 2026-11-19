using System.Globalization;
using System.Text;
using Imar18.Domain.Numerics;

namespace Imar18.Application.Girdi;

/// <summary>Malik Rehberi kaydı: anonim malik kodu ↔ ad-soyad, baba adı, TKGM kişi sistem no (yalnız yerel; KVKK).</summary>
public sealed record MalikRehberKaydi(string MalikKodu, string AdSoyad, string? BabaAdi, string? KisiSistemNo);

/// <summary>Kadastro parselinin tapu kütüğü bilgisi (Fen Klasörü cetvelleri: Cilt/Sayfa, İl/İlçe/Mahalle, Ada/Parsel, AT alanı).</summary>
public sealed record KadastroTapuKaydi(
    string KadastroParselId, string? Cilt, string? Sayfa, string? Il, string? Ilce, string? Mahalle, string? Ada, string? Parsel, decimal? AtYuzolcumM2);

/// <summary>
/// P7.3-B1: yerel Malik Rehberi. Hesap motoru anonim kodla çalışır; ad/baba adı/Cilt/Sayfa yalnız çıktı cetvellerine basılmak
/// için proje dosyasında tutulur. RevisionId'ye, denetim kaydına ve hata mesajlarına girmez (SORU-040 c).
/// </summary>
public sealed record MalikRehberi(IReadOnlyList<MalikRehberKaydi> Malikler, IReadOnlyList<KadastroTapuKaydi> Parseller)
{
    public static MalikRehberi Bos { get; } = new([], []);

    public MalikRehberKaydi? Bul(string malikKodu) =>
        Malikler.FirstOrDefault(m => string.Equals(m.MalikKodu, malikKodu, StringComparison.Ordinal));

    public KadastroTapuKaydi? Parsel(string kadastroParselId) =>
        Parseller.FirstOrDefault(p => string.Equals(p.KadastroParselId, kadastroParselId, StringComparison.Ordinal));
}

/// <summary>webtapu aktarımı sonucu: TAKBİS hisse satırları ve güncellenmiş Malik Rehberi.</summary>
public sealed record WebtapuAktarimi(IReadOnlyList<TakbisHisseGirdisi> Hisseler, MalikRehberi Rehber);

/// <summary>
/// P7.3-B1 (SAHA-006): webtapu tapu kaydını TAKBİS hisselerine ve Malik Rehberi'ne çevirir (saf mantık, G/Ç yok).
/// <list type="bullet">
/// <item>Yalnız aktif (terkin edilmemiş) satırlar aktarılır; muvafakat webtapu'da olmadığı için <c>H</c> (SORU-040 a).</item>
/// <item>Malik kodu: rehberde aynı kişi sistem no'lu (yoksa aynı normalize ad + baba adlı) kişi varsa onun kodu; değilse
/// rehberdeki ve <c>ayrilmisKodlar</c>'daki (TAKBİS'te elle girilmiş başka kişiler) en büyük <c>Mnn</c> kodundan devam eden yeni
/// kod (M01, M02 …; belge sırası) — TAKBİS anonim kod biçimine uyar ve ayrılmış kodlarla çakışmaz.</item>
/// <item>Parsel kaydı (Cilt/Sayfa vb.) kadastro parsel kimliğine göre eklenir ya da güncellenir.</item>
/// </list>
/// </summary>
public static class WebtapuAktarici
{
    public static WebtapuAktarimi Aktar(TapuKaydi kayit, string kadastroParselId, MalikRehberi mevcut, IReadOnlyCollection<string>? ayrilmisKodlar = null)
    {
        ArgumentNullException.ThrowIfNull(kayit);
        ArgumentException.ThrowIfNullOrWhiteSpace(kadastroParselId);
        ArgumentNullException.ThrowIfNull(mevcut);

        var parselId = kadastroParselId.Trim();
        var malikler = mevcut.Malikler.ToList();
        var ayrilmis = new HashSet<string>((ayrilmisKodlar ?? []).Select(k => k.Trim()), StringComparer.OrdinalIgnoreCase);
        var sonraki = malikler.Select(m => m.MalikKodu).Concat(ayrilmis).Select(KodNumarasi).DefaultIfEmpty(0).Max() + 1;
        var hisseler = new List<(string Kod, Fraction Hisse)>();
        foreach (var s in kayit.AktifMalikler)
        {
            var varolan = malikler.FirstOrDefault(m => AyniKisi(m, s));
            string kod;
            if (varolan is not null)
            {
                kod = varolan.MalikKodu;
            }
            else
            {
                do
                {
                    kod = "M" + sonraki.ToString("00", CultureInfo.InvariantCulture);
                    sonraki++;
                }
                while (ayrilmis.Contains(kod));

                malikler.Add(new MalikRehberKaydi(kod, Normalize(s.AdSoyad), BosIseNull(s.BabaAdi), BosIseNull(s.KisiSistemNo)));
            }

            // Aynı kişinin aynı parseldeki birden çok satırı tek hissede toplanır (tam rasyonel).
            var i = hisseler.FindIndex(h => string.Equals(h.Kod, kod, StringComparison.Ordinal));
            var hisse = new Fraction(s.Pay, s.Payda);
            if (i < 0)
            {
                hisseler.Add((kod, hisse));
            }
            else
            {
                hisseler[i] = (kod, hisseler[i].Hisse + hisse);
            }
        }

        var parseller = mevcut.Parseller.Where(p => !string.Equals(p.KadastroParselId, parselId, StringComparison.Ordinal)).ToList();
        parseller.Add(new KadastroTapuKaydi(parselId, kayit.Cilt, kayit.Sayfa, kayit.Il, kayit.Ilce, kayit.Mahalle, kayit.Ada, kayit.Parsel, kayit.AtYuzolcumM2));
        var takbis = hisseler.Select(h => new TakbisHisseGirdisi(h.Kod, h.Hisse.Numerator, h.Hisse.Denominator, Muvafakatli: false)).ToList();
        return new WebtapuAktarimi(takbis.AsReadOnly(), new MalikRehberi(malikler.AsReadOnly(), parseller.AsReadOnly()));
    }

    /// <summary>
    /// TAKBİS metnindeki (<c>parsel;malik;pay/payda;E|H</c>) <paramref name="kadastroParselId"/> satırlarını yenileriyle
    /// değiştirir; diğer parsellerin satırları ve yorumlar (<c>#</c>) korunur, yeni satırlar sona eklenir.
    /// </summary>
    public static string TakbisMetniniGuncelle(string mevcutMetin, string kadastroParselId, IReadOnlyList<TakbisHisseGirdisi> hisseler)
    {
        ArgumentNullException.ThrowIfNull(mevcutMetin);
        ArgumentNullException.ThrowIfNull(hisseler);
        var parselId = kadastroParselId.Trim();
        var satirlar = mevcutMetin.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(s => s.Trim().Length > 0)
            .Where(s => s.TrimStart().StartsWith('#') || !string.Equals(s.Split(';')[0].Trim(), parselId, StringComparison.Ordinal))
            .ToList();
        satirlar.AddRange(hisseler.Select(h => string.Create(CultureInfo.InvariantCulture,
            $"{parselId};{h.MalikKodu};{h.Pay}/{h.Payda};{(h.Muvafakatli ? "E" : "H")}")));
        return string.Join(Environment.NewLine, satirlar);
    }

    /// <summary>
    /// Proje kadastro parsel kimlikleri içinde webtapu Ada/Parsel'ine karşılık geleni önerir: önce tam "ada/parsel", sonra
    /// yalnız parsel no, sonra "…/parsel" ile biten tek kimlik; bulunamazsa null (mühendis seçer).
    /// </summary>
    public static string? KadastroParseliOner(TapuKaydi kayit, IReadOnlyList<string> projeKadastroParselleri)
    {
        ArgumentNullException.ThrowIfNull(kayit);
        ArgumentNullException.ThrowIfNull(projeKadastroParselleri);
        var adaParsel = kayit.Ada + "/" + kayit.Parsel;
        return projeKadastroParselleri.FirstOrDefault(p => string.Equals(p.Trim(), adaParsel, StringComparison.Ordinal))
            ?? projeKadastroParselleri.FirstOrDefault(p => string.Equals(p.Trim(), kayit.Parsel, StringComparison.Ordinal))
            ?? Tek(projeKadastroParselleri.Where(p => p.Trim().EndsWith("/" + kayit.Parsel, StringComparison.Ordinal)));
    }

    /// <summary>Türkçe büyük harf ve tek boşluk normalizasyonu (ad karşılaştırması için).</summary>
    public static string Normalize(string metin)
    {
        ArgumentNullException.ThrowIfNull(metin);
        var sb = new StringBuilder(metin.Length);
        foreach (var parca in metin.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(parca.ToUpper(CultureInfo.GetCultureInfo("tr-TR")));
        }

        return sb.ToString();
    }

    private static string? Tek(IEnumerable<string> adaylar)
    {
        var liste = adaylar.Take(2).ToList();
        return liste.Count == 1 ? liste[0] : null;
    }

    private static bool AyniKisi(MalikRehberKaydi r, TapuMalikSatiri s) =>
        !string.IsNullOrWhiteSpace(s.KisiSistemNo) && !string.IsNullOrWhiteSpace(r.KisiSistemNo)
            ? string.Equals(r.KisiSistemNo, s.KisiSistemNo.Trim(), StringComparison.Ordinal)
            : string.Equals(Normalize(r.AdSoyad), Normalize(s.AdSoyad), StringComparison.Ordinal)
              && string.Equals(Normalize(r.BabaAdi ?? string.Empty), Normalize(s.BabaAdi ?? string.Empty), StringComparison.Ordinal);

    private static int KodNumarasi(string kod) =>
        kod.Length > 1 && kod[0] == 'M' && int.TryParse(kod.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string? BosIseNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
