using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Girdi;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>Kullanıcı sayı girdisi: hem '.' hem ',' ondalık ayıracı kabul edilir (binlik ayıraç kabul edilmez).</summary>
internal static class SayiGirdisi
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        var t = (text ?? string.Empty).Trim().Replace(',', '.');
        return t.Length > 0 && t.Count(c => c == '.') <= 1
            && decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    public static string Yaz(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>2. adım satırı: kadastro parseli, Decimal Gauss hesap alanı (salt okunur) ve mühendisin girdiği tapu alanı.</summary>
public sealed partial class TecvizSatiri : ObservableObject
{
    public TecvizSatiri(string parselId, decimal hesapAlaniM2, string tapuAlaniText)
    {
        ParselId = parselId;
        HesapAlaniM2 = hesapAlaniM2;
        _tapuAlaniText = tapuAlaniText;
    }

    public string ParselId { get; }

    public decimal HesapAlaniM2 { get; }

    [ObservableProperty]
    private string _tapuAlaniText;

    /// <summary>P7: alan bazlı hata (kırmızı çerçeve + alt satır); düzenlenince temizlenir.</summary>
    [ObservableProperty]
    private string? _hata;

    partial void OnTapuAlaniTextChanged(string value) => Hata = null;
}

/// <summary>
/// Sihirbaz 2–4. adımlarının canlı motora ve CommandBus'a bağlanması (P6): 2. Tecviz (parsel bazlı tapu alanları + isteğe bağlı
/// düzenleme toplam sınırı, <see cref="TecvizGirdileriAyarlaKomutu"/>), 3. DOP &amp; Y1 dengeleme (R1 = Σ tapu, R2 = dengelenmiş
/// imar toplamı veya mühendis girdisi, D1/D2/R3/ortak katılım, <see cref="DopGirdisiAyarlaKomutu"/>), 4. Hak eşleştirme
/// (TAKBİS hisseleri, kroki satırları, geçici↔tescil eşlemesi, <see cref="HakEslestirmeAyarlaKomutu"/>). Hesap yapılmaz: tüm
/// sonuçlar <see cref="ApprovalService.Degerlendir"/> (Domain motoru) ile arka planda üretilir (50 ms kuralı) ve kullanıcıya
/// yalnızca Türkçe <c>UserMessage</c> gösterilir. Malik kodları anonim kod olmalıdır (TAKBİS doğrulayıcı, KVKK).
/// </summary>
public sealed partial class HesapAdimlariViewModel : ObservableObject
{
    private readonly ProjeCalismaAlani _alan;
    private readonly Func<TecvizGirdisi?> _tecvizGirdisi;
    private bool _dopYuklendi;
    private bool _hakYuklendi;

    // ---- 2. Tecviz ----
    [ObservableProperty]
    private string _toplamTecvizSiniriText = string.Empty;

    [ObservableProperty]
    private string _tecvizOzeti = string.Empty;

    // ---- 3. DOP & Y1 ----
    [ObservableProperty]
    private string _r2Text = string.Empty;

    [ObservableProperty]
    private string _d1Text = "0";

    [ObservableProperty]
    private string _d2Text = "0";

    [ObservableProperty]
    private string _r3Text = "0";

    [ObservableProperty]
    private string _ortakKatilimText = "0";

    [ObservableProperty]
    private string _dopOzeti = string.Empty;

    [ObservableProperty]
    private string _dengelemeOzeti = string.Empty;

    // ---- 4. Hak eşleştirme ----
    [ObservableProperty]
    private string _takbisMetni = string.Empty;

    [ObservableProperty]
    private string _krokiMetni = string.Empty;

    [ObservableProperty]
    private string _kimlikMetni = string.Empty;

    [ObservableProperty]
    private string _hakOzeti = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TecvizUygulaCommand), nameof(DopUygulaCommand), nameof(HakUygulaCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _durumMetni = string.Empty;

    // ---- P7: alan bazlı hata metinleri (null: geçerli; düzenlenince temizlenir) ----
    [ObservableProperty]
    private string? _toplamTecvizSiniriHatasi;

    [ObservableProperty]
    private string? _r2Hatasi;

    [ObservableProperty]
    private string? _d1Hatasi;

    [ObservableProperty]
    private string? _d2Hatasi;

    [ObservableProperty]
    private string? _r3Hatasi;

    [ObservableProperty]
    private string? _ortakKatilimHatasi;

    [ObservableProperty]
    private string? _takbisHatasi;

    [ObservableProperty]
    private string? _krokiHatasi;

    [ObservableProperty]
    private string? _kimlikHatasi;

    partial void OnToplamTecvizSiniriTextChanged(string value) => ToplamTecvizSiniriHatasi = null;

    partial void OnR2TextChanged(string value) => R2Hatasi = null;

    partial void OnD1TextChanged(string value) => D1Hatasi = null;

    partial void OnD2TextChanged(string value) => D2Hatasi = null;

    partial void OnR3TextChanged(string value) => R3Hatasi = null;

    partial void OnOrtakKatilimTextChanged(string value) => OrtakKatilimHatasi = null;

    partial void OnTakbisMetniChanged(string value) => TakbisHatasi = null;

    partial void OnKrokiMetniChanged(string value) => KrokiHatasi = null;

    partial void OnKimlikMetniChanged(string value) => KimlikHatasi = null;

    /// <summary>
    /// P7 "Projeyi Temizle / Sıfırla": 2–4. adım metin girdileri, alan hataları ve tablolar boşaltılır (proje durumu ayrıca
    /// CommandBus sıfırlama komutuyla temizlenir; ardından <see cref="YenileAsync"/> çağrılır).
    /// </summary>
    public void Sifirla()
    {
        _dopYuklendi = false;
        _hakYuklendi = false;
        TecvizSatirlari.Clear();
        ToplamTecvizSiniriText = string.Empty;
        R2Text = string.Empty;
        D1Text = "0";
        D2Text = "0";
        R3Text = "0";
        OrtakKatilimText = "0";
        TakbisMetni = string.Empty;
        KrokiMetni = string.Empty;
        KimlikMetni = string.Empty;
        DurumMetni = string.Empty;
        foreach (var h in new Action[] { () => ToplamTecvizSiniriHatasi = null, () => R2Hatasi = null, () => D1Hatasi = null, () => D2Hatasi = null,
                     () => R3Hatasi = null, () => OrtakKatilimHatasi = null, () => TakbisHatasi = null, () => KrokiHatasi = null, () => KimlikHatasi = null })
        {
            h();
        }
    }

    public HesapAdimlariViewModel(ProjeCalismaAlani alan, Func<TecvizGirdisi?> tecvizGirdisi)
    {
        ArgumentNullException.ThrowIfNull(alan);
        ArgumentNullException.ThrowIfNull(tecvizGirdisi);
        _alan = alan;
        _tecvizGirdisi = tecvizGirdisi;
    }

    /// <summary>Bir adım projeye başarıyla yazıldığında (ana pencere onay ekranını yeniler).</summary>
    public event EventHandler? Uygulandi;

    public ObservableCollection<TecvizSatiri> TecvizSatirlari { get; } = [];

    public ObservableCollection<string> TecvizMesajlari { get; } = [];

    public ObservableCollection<string> DopMesajlari { get; } = [];

    public ObservableCollection<string> HakSatirlari { get; } = [];

    public ObservableCollection<string> HakMesajlari { get; } = [];

    /// <summary>Son değerlendirme (testler ve görünüm).</summary>
    public ProjeDegerlendirmesi? Degerlendirme { get; private set; }

    /// <summary>R1 = Σ tecviz tapu alanı (Application değerlendirmesinden; 2. adım uygulandıktan sonra).</summary>
    public decimal? R1 => Degerlendirme?.R1TapuToplamiM2;

    /// <summary>R2 boş bırakılırsa kullanılacak değer (Application değerlendirmesinden).</summary>
    public decimal? R2Otomatik => Degerlendirme?.OnerilenR2M2;

    /// <summary>Güncel proje durumunu motorda arka planda değerlendirip tüm adım görünümlerini yeniler.</summary>
    public async Task YenileAsync()
    {
        var belge = _alan.Yol.Belge;
        var d = await Task.Run(() => _alan.Onay.Degerlendir()).ConfigureAwait(true);
        Degerlendirme = d;
        TecvizYenile(belge, d);
        DopYenile(belge, d);
        HakYenile(belge, d);
        OnPropertyChanged(nameof(R1));
        OnPropertyChanged(nameof(R2Otomatik));
    }

    // =====================================================================
    // 2. Tecviz
    // =====================================================================

    private void TecvizYenile(ProjeBelgesi belge, ProjeDegerlendirmesi d)
    {
        var eski = TecvizSatirlari.ToDictionary(r => r.ParselId, r => r.TapuAlaniText, StringComparer.Ordinal);
        var kayitli = belge.TecvizParselleri.GroupBy(t => t.ParselId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().TapuAlaniM2, StringComparer.Ordinal);
        TecvizSatirlari.Clear();
        foreach (var p in d.Parseller.Where(p => p.Geometri.Rol == ParselRolu.Kadastro))
        {
            var metin = eski.TryGetValue(p.Geometri.ParselId, out var e) && e.Length > 0
                ? e
                : kayitli.TryGetValue(p.Geometri.ParselId, out var t) ? SayiGirdisi.Yaz(t) : string.Empty;
            TecvizSatirlari.Add(new TecvizSatiri(p.Geometri.ParselId, p.AlanM2, metin));
        }

        if (ToplamTecvizSiniriText.Length == 0 && belge.DuzenlemeToplamTecvizSiniriM2 is { } toplam)
        {
            ToplamTecvizSiniriText = SayiGirdisi.Yaz(toplam);
        }

        TecvizMesajlari.Clear();
        if (d.Tecviz is { } tz)
        {
            var tapu = tz.ParselSonuclari.Sum(s => s.TapuAlaniM2);
            var hesap = tz.ParselSonuclari.Sum(s => s.HesapAlaniM2);
            TecvizOzeti = string.Create(CultureInfo.InvariantCulture,
                $"Σ tapu {tapu:0.00} m² · Σ hesap {hesap:0.00} m² · fark {tapu - hesap:0.00} m² · {(tz.Gecti ? "tecviz içinde" : "TECVİZ AŞILDI")}");
            foreach (var b in tz.Bulgular)
            {
                TecvizMesajlari.Add(b.UserMessage);
            }
        }
        else
        {
            TecvizOzeti = d.EksikGirdiler.Contains("tecviz") ? "Tecviz girdisi bekleniyor." : "Tecviz hesaplanamadı.";
        }
    }

    private bool HazirMi() => !IsBusy;

    [RelayCommand(CanExecute = nameof(HazirMi))]
    private async Task TecvizUygulaAsync()
    {
        var tecviz = _tecvizGirdisi();
        if (tecviz is null)
        {
            DurumMetni = "Önce 1. adımda tecviz girdisini (ölçek, yöntem, sınır, kaynak) doğrulayın.";
            return;
        }

        if (TecvizSatirlari.Count == 0)
        {
            DurumMetni = "Projede kadastro parseli geometrisi yok; önce CAD dosyasını içe aktarın.";
            return;
        }

        // P7: tüm satırlar tek seferde denetlenir; her hatalı kutu kendi altında işaretlenir.
        var parseller = new List<TecvizParselGirdisi>(TecvizSatirlari.Count);
        string? ilkHata = null;
        foreach (var r in TecvizSatirlari)
        {
            if (!SayiGirdisi.TryParse(r.TapuAlaniText, out var tapu))
            {
                r.Hata = string.IsNullOrWhiteSpace(r.TapuAlaniText) ? "Tapu alanı zorunludur." : "Tapu alanı (m²) geçerli bir sayı olmalı.";
                ilkHata ??= $"{r.ParselId}: tapu alanı (m²) geçerli bir sayı olmalı.";
                continue;
            }

            r.Hata = null;
            parseller.Add(new TecvizParselGirdisi(r.ParselId, tapu, r.HesapAlaniM2, tecviz));
        }

        decimal? toplam = null;
        if (!string.IsNullOrWhiteSpace(ToplamTecvizSiniriText))
        {
            if (!SayiGirdisi.TryParse(ToplamTecvizSiniriText, out var t))
            {
                ToplamTecvizSiniriHatasi = "Geçerli bir sayı girin veya boş bırakın.";
                ilkHata ??= "Düzenleme toplam tecviz sınırı (m²) geçerli bir sayı olmalı.";
            }
            else
            {
                toplam = t;
            }
        }

        if (ilkHata is not null)
        {
            DurumMetni = ilkHata;
            return;
        }

        await GonderAsync([new TecvizGirdileriAyarlaKomutu(parseller, toplam)], "Tecviz girdileri projeye yazıldı.").ConfigureAwait(true);
    }

    // =====================================================================
    // 3. DOP & Y1 dengeleme
    // =====================================================================

    private void DopYenile(ProjeBelgesi belge, ProjeDegerlendirmesi d)
    {
        if (!_dopYuklendi && belge.Dop is { } dop)
        {
            _dopYuklendi = true;
            R2Text = SayiGirdisi.Yaz(dop.R2);
            D1Text = SayiGirdisi.Yaz(dop.D1);
            D2Text = SayiGirdisi.Yaz(dop.D2);
            R3Text = SayiGirdisi.Yaz(dop.R3);
            OrtakKatilimText = SayiGirdisi.Yaz(dop.OrtakKatilimM2);
        }

        DengelemeOzeti = d.Dengeleme is { } y
            ? string.Create(CultureInfo.InvariantCulture,
                $"Y1 ({y.YolTerk.Id}): hesap {y.YolTerk.HesapAlaniM2:0.00} + düzeltme {y.YolTerk.DuzeltmeM2:+0.00;-0.00;0.00} = {y.YolTerk.DengelenmisAlanM2:0.00} m² · Σ imar {y.ImarParselToplamiM2:0.00} m²")
            : "Y1 dengelemesi için tek yol terk parseli ve geçen tecviz gerekir.";

        DopMesajlari.Clear();
        if (d.Dop is { } s)
        {
            DopOzeti = string.Create(CultureInfo.InvariantCulture,
                $"DOPO {s.Dopo:0.0000000} · OKO {s.Oko:0.0000000} · kamuya ayrılan {s.KamuyaAyrilanAlanM2:0.00} m² · {(s.TavanAsildi ? $"%45 AŞILDI ({s.AsimM2:0.00} m²)" : "%45 sınırı içinde")}");
            foreach (var b in s.Bulgular)
            {
                DopMesajlari.Add(b.UserMessage);
            }
        }
        else
        {
            DopOzeti = "DOP girdisi bekleniyor.";
        }
    }

    [RelayCommand(CanExecute = nameof(HazirMi))]
    private async Task DopUygulaAsync()
    {
        await YenileAsync().ConfigureAwait(true); // bayat değerlendirme kullanılmaz (reviewer M4)
        if (R1 is not { } r1)
        {
            DurumMetni = "Önce 2. adımda tapu alanlarını uygulayın (R1 = Σ tapu).";
            return;
        }

        decimal r2;
        if (string.IsNullOrWhiteSpace(R2Text))
        {
            if (R2Otomatik is not { } otomatik)
            {
                DurumMetni = "R2 (Σ imar parseli) hesaplanamadı; imar parseli geometrisi gerekir.";
                return;
            }

            r2 = otomatik;
        }
        else if (!SayiGirdisi.TryParse(R2Text, out r2))
        {
            R2Hatasi = "R2 geçerli bir sayı olmalı (boş: otomatik).";
            DurumMetni = "R2 (m²) geçerli bir sayı olmalı.";
            return;
        }

        const string SayiOlmali = "Geçerli bir sayı girin (yoksa 0).";
        var d1Tamam = SayiGirdisi.TryParse(D1Text, out var d1);
        var d2Tamam = SayiGirdisi.TryParse(D2Text, out var d2);
        var r3Tamam = SayiGirdisi.TryParse(R3Text, out var r3);
        var okTamam = SayiGirdisi.TryParse(OrtakKatilimText, out var ok);
        D1Hatasi = d1Tamam ? null : SayiOlmali;
        D2Hatasi = d2Tamam ? null : SayiOlmali;
        R3Hatasi = r3Tamam ? null : SayiOlmali;
        OrtakKatilimHatasi = okTamam ? null : SayiOlmali;
        if (!(d1Tamam && d2Tamam && r3Tamam && okTamam))
        {
            DurumMetni = "D1, D2, R3 ve ortak katılım alanları (m²) geçerli sayılar olmalı (yoksa 0).";
            return;
        }

        var dop = DopGirdisi.Olustur(r1, r2, d1, d2, r3, ok);
        if (dop.IsFailure)
        {
            DurumMetni = dop.Error.UserMessage;
            return;
        }

        _dopYuklendi = true;
        await GonderAsync([new DopGirdisiAyarlaKomutu(dop.Value)], "DOP girdisi projeye yazıldı.").ConfigureAwait(true);
    }

    // =====================================================================
    // 4. Hak eşleştirme
    // =====================================================================

    private void HakYenile(ProjeBelgesi belge, ProjeDegerlendirmesi d)
    {
        if (!_hakYuklendi && belge.HakEslestirme is { } h)
        {
            _hakYuklendi = true;
            TakbisMetni = string.Join(Environment.NewLine, h.KadastroParselleri.SelectMany(k => k.Malikler.Select(m =>
                string.Create(CultureInfo.InvariantCulture, $"{k.ParselId};{m.MalikId};{m.Hisse.Numerator}/{m.Hisse.Denominator};{(m.Muvafakatli ? "E" : "H")}"))));
            KrokiMetni = string.Join(Environment.NewLine, h.Eslestirme.Select(r =>
                string.Create(CultureInfo.InvariantCulture, $"{r.MalikId};{r.KadastroParselId};{r.ImarParselKodu};{r.AlanM2:0.00}")));
            KimlikMetni = string.Join(Environment.NewLine, belge.KimlikCiftleri.Select(c => c.GeciciKod + ";" + c.TescilNo));
        }

        HakSatirlari.Clear();
        HakMesajlari.Clear();
        if (d.Hak is { } hak)
        {
            foreach (var m in hak.MalikHaklari)
            {
                HakSatirlari.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{m.MalikId} · {m.KadastroParselId} · giren {m.GirenAlanM2:0.00} · net hak {m.NetHakM2:0.00} · tahsis {m.TahsisEdilenM2:0.00} · fark {m.FarkM2:+0.00;-0.00;0.00}"));
            }

            foreach (var b in hak.Bulgular)
            {
                HakMesajlari.Add(b.UserMessage);
            }

            HakOzeti = $"{hak.ImarParselleri.Count} imar parseli · {hak.ImarParselleri.Count(p => !p.Mustakil)} hisseli · {hak.MalikHaklari.Count} hak sahibi";
        }
        else
        {
            HakOzeti = "Hak eşleştirme girdisi bekleniyor.";
        }
    }

    [RelayCommand(CanExecute = nameof(HazirMi))]
    private async Task HakUygulaAsync()
    {
        var d = await Task.Run(() => _alan.Onay.Degerlendir()).ConfigureAwait(true); // her uygulamada taze (reviewer M4)
        if (d.Dop is not { } dop)
        {
            DurumMetni = "Önce 3. adımda DOP girdisini uygulayın (hak hesabı DOPO'yu kullanır).";
            return;
        }

        var tapu = _alan.Yol.Belge.TecvizParselleri.GroupBy(t => t.ParselId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().TapuAlaniM2, StringComparer.Ordinal);
        var kadastro = new List<KadastroParseli>();
        foreach (var grup in Satirlar(TakbisMetni).GroupBy(s => s.Length > 0 ? s[0] : string.Empty, StringComparer.Ordinal))
        {
            if (grup.Any(s => s.Length != 4))
            {
                DurumMetni = TakbisHatasi = "TAKBİS satırı biçimi: parselId;malikKodu;pay/payda;E|H";
                return;
            }

            if (!tapu.TryGetValue(grup.Key, out var alan))
            {
                DurumMetni = TakbisHatasi = $"{grup.Key}: 2. adımda tapu alanı girilmemiş kadastro parseli.";
                return;
            }

            var hisseler = new List<TakbisHisseGirdisi>();
            foreach (var s in grup)
            {
                var pay = s[2].Split('/');
                if (pay.Length != 2 || !BigInteger.TryParse(pay[0], NumberStyles.None, CultureInfo.InvariantCulture, out var p)
                    || !BigInteger.TryParse(pay[1], NumberStyles.None, CultureInfo.InvariantCulture, out var q) || s[3] is not ("E" or "H"))
                {
                    DurumMetni = TakbisHatasi = "TAKBİS hissesi pay/payda (tam sayı) ve muvafakat E/H olmalı.";
                    return;
                }

                hisseler.Add(new TakbisHisseGirdisi(s[1], p, q, s[3] == "E"));
            }

            var k = TakbisGirdiDogrulayici.Dogrula(new TakbisParselGirdisi(grup.Key, alan, hisseler));
            if (k.IsFailure)
            {
                DurumMetni = TakbisHatasi = grup.Key + ": " + k.Error.UserMessage;
                return;
            }

            kadastro.Add(k.Value);
        }

        var kroki = new List<KrokiEslesmeGirdisi>();
        foreach (var s in Satirlar(KrokiMetni))
        {
            if (s.Length != 4 || !SayiGirdisi.TryParse(s[3], out var a))
            {
                DurumMetni = KrokiHatasi = "Kroki satırı biçimi: malikKodu;kadastroParselId;imarParselKodu;alan";
                return;
            }

            kroki.Add(new KrokiEslesmeGirdisi(s[0], s[1], s[2], a));
        }

        var satirlar = KrokiGirdiDogrulayici.Dogrula(kroki, kadastro);
        if (satirlar.IsFailure)
        {
            DurumMetni = KrokiHatasi = satirlar.Error.UserMessage;
            return;
        }

        var komutlar = new List<IProjeKomutu> { new HakEslestirmeAyarlaKomutu(new HakEslestirmeGirdisi(dop.Dopo, kadastro, d.HakIcinImarParselleri, satirlar.Value)) };

        var ciftler = new List<ParselKimlikCifti>();
        foreach (var s in Satirlar(KimlikMetni))
        {
            if (s.Length != 2)
            {
                DurumMetni = KimlikHatasi = "Geçici ↔ tescil satırı biçimi: geciciKod;tescilNo";
                return;
            }

            ciftler.Add(new ParselKimlikCifti(s[0], s[1]));
        }

        if (ciftler.Count > 0)
        {
            // İkinci komutun reddi ilkini yarım bırakmasın diye eşleme gönderimden ÖNCE doğrulanır (reviewer M6).
            var eslesme = ParselKimlikEslemesi.Olustur(ciftler);
            if (eslesme.IsFailure)
            {
                DurumMetni = KimlikHatasi = eslesme.Error.UserMessage;
                return;
            }

            komutlar.Add(new ParselKimlikEslemesiAyarlaKomutu(ciftler));
        }

        _hakYuklendi = true;
        await GonderAsync(komutlar, "Hak eşleştirmesi projeye yazıldı.").ConfigureAwait(true);
    }

    private static IEnumerable<string[]> Satirlar(string metin) => (metin ?? string.Empty)
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(s => !s.StartsWith('#'))
        .Select(s => s.Split(';').Select(p => p.Trim()).ToArray());

    /// <summary>Komutları sırayla CommandBus'a gönderir; ilk retçe durur (Türkçe mesaj). Sonra adımları yeniler.</summary>
    private async Task GonderAsync(IReadOnlyList<IProjeKomutu> komutlar, string basariMetni)
    {
        IsBusy = true;
        var durum = basariMetni;
        try
        {
            foreach (var k in komutlar)
            {
                var sonuc = await _alan.Yol.GonderAsync(k, _alan.KullaniciId).ConfigureAwait(true);
                if (sonuc.IsFailure)
                {
                    durum = "Projeye yazılamadı: " + sonuc.Error.UserMessage;
                    break;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        // Yenileme finally dışında: istisnası asıl sonucu maskelemez (reviewer M6).
        await YenileAsync().ConfigureAwait(true);
        DurumMetni = durum;
        Uygulandi?.Invoke(this, EventArgs.Empty);
    }
}
