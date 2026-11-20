using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Imar18.Presentation.Avalonia.Gorunum;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>
/// P7 UX (kutucuklu tablolar): noktalı virgüllü metin satırının düzenlenebilir tablo satırı. Satır metne geri yazılırken
/// sayısal hücreler <see cref="SayiGirdiAyristirici"/> ile okunur ve <c>.</c> (InvariantCulture) biçiminde yazılır; geçersiz hücre
/// ham haliyle yazılır (metin ayrıştırıcısı satırı reddeder) ve <see cref="Hata"/> satır altında gösterilir. Tablonun
/// gösteremediği fazla alanlar <see cref="Ek"/>'te korunur (metin ↔ tablo gidiş-dönüşünde kaybolmaz) ve satır hatası olarak
/// gösterilir. Metinden gelip hiç düzenlenmeyen satır metne <see cref="Ham"/> haliyle aynen yazılır (tahminle düzeltilmez).
/// </summary>
public abstract partial class TabloSatiri : ObservableObject
{
    /// <summary>Satır hatası (geçersiz sayısal hücre); null: geçerli veya eksik.</summary>
    [ObservableProperty]
    private string? _hata;

    /// <summary>Tablo sütunlarına sığmayan fazla metin alanları (aynen geri yazılır).</summary>
    internal IReadOnlyList<string> Ek { get; init; } = [];

    /// <summary>Metinden kurulduğu andaki satır metni; herhangi bir hücre düzenlenince null olur.</summary>
    internal string? Ham { get; set; }

    /// <summary>Tüm hücreler boş mu (boş satır metne yazılmaz).</summary>
    public abstract bool Bos { get; }

    /// <summary>Satırın metin karşılığı (hücreler <c>;</c> ile).</summary>
    internal abstract IEnumerable<string> Alanlar();

    /// <summary>Düzenlenmemiş satır metindeki haliyle, düzenlenmiş satır hücrelerden yazılır.</summary>
    internal string MetinSatiri() => Ham ?? string.Join(";", Alanlar().Concat(Ek));

    /// <summary>Hücre değiştiğinde sayısal hücreler yeniden denetlenir.</summary>
    protected abstract void Denetle();

    /// <summary>Hücre özelliği mi (hata ve hata bayrakları değil): yalnız hücre değişimi denetim ve metin senkronu tetikler.</summary>
    internal static bool HucreMi(string? ozellik) =>
        ozellik is { } ad && ad != nameof(Hata) && !ad.EndsWith("Hatali", StringComparison.Ordinal);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        // Denetim olay yayınından ÖNCE: senkron/canlı önizleme güncel Hata'yı görür.
        if (HucreMi(e.PropertyName))
        {
            Ham = null;
            Denetle();
        }

        base.OnPropertyChanged(e);
    }

    /// <summary>
    /// Metin hücresi hatası: <c>;</c> ve satır sonu metin biçimini böler; ilk sütunda baştaki <c>#</c> satırı yorum yapar
    /// (satır hesaptan sessizce düşerdi). Geçerliyse null.
    /// </summary>
    protected static string? MetinHucresiHatasi(string sutun, string? hucre, bool ilkSutun = false)
    {
        var h = hucre ?? string.Empty;
        if (h.Contains(';', StringComparison.Ordinal) || h.Contains('\r', StringComparison.Ordinal) || h.Contains('\n', StringComparison.Ordinal))
        {
            return sutun + ": ';' ve satır sonu kullanılamaz.";
        }

        return ilkSutun && h.TrimStart().StartsWith('#') ? sutun + ": '#' ile başlayamaz (satır yorum sayılır ve hesaptan düşer)." : null;
    }

    /// <summary>Tablonun gösteremediği fazla alan varsa satır hatası (metin ayrıştırıcısı satırı reddeder).</summary>
    protected string? EkHatasi() => Ek.Count == 0
        ? null
        : "Satırda fazla alan var (" + string.Join(";", Ek) + "); 'Metin / Toplu Yapıştır' alanında düzeltin.";

    /// <summary>Sayısal hücrenin metne yazılacak biçimi: geçerliyse InvariantCulture, değilse kırpılmış ham metin.</summary>
    protected static string Sayi(string? hucre) =>
        SayiGirdiAyristirici.TryParseDecimal(hucre, out var d) ? d.ToString(CultureInfo.InvariantCulture) : (hucre ?? string.Empty).Trim();

    /// <summary>Dolu ama geçersiz sayısal hücre mi.</summary>
    protected static bool SayiHatali(string? hucre) => !string.IsNullOrWhiteSpace(hucre) && !SayiGirdiAyristirici.TryParseDecimal(hucre, out _);

    /// <summary>Dolu ama geçersiz hücrenin Türkçe mesajı (<paramref name="sutun"/> önekli).</summary>
    protected static string SayiHatasi(string sutun, string? hucre)
    {
        SayiGirdiAyristirici.TryParseDecimal(hucre, out _, out var hata);
        return sutun + ": " + hata;
    }

    protected static string Kirp(string? hucre) => (hucre ?? string.Empty).Trim();

    /// <summary><c>;</c> ile bölünmüş metin satırının <paramref name="i"/>. alanı (yoksa boş).</summary>
    internal static string Parca(IReadOnlyList<string> p, int i) => i < p.Count ? p[i] : string.Empty;

    /// <summary><paramref name="sutunSayisi"/>'ndan sonraki alanlar (tablonun gösteremediği).</summary>
    internal static IReadOnlyList<string> Fazla(IReadOnlyList<string> p, int sutunSayisi) => p.Count > sutunSayisi ? p.Skip(sutunSayisi).ToList() : [];
}

/// <summary>1. adım ortak nokta satırı: <c>NoktaNo;Y1;X1;Y2;X2</c> (I = deprem öncesi, II = deprem sonrası).</summary>
public sealed partial class OrtakNoktaSatirVm : TabloSatiri
{
    [ObservableProperty]
    private string _noktaNo = string.Empty;

    [ObservableProperty]
    private string _y1 = string.Empty;

    [ObservableProperty]
    private string _x1 = string.Empty;

    [ObservableProperty]
    private string _y2 = string.Empty;

    [ObservableProperty]
    private string _x2 = string.Empty;

    [ObservableProperty]
    private bool _y1Hatali;

    [ObservableProperty]
    private bool _x1Hatali;

    [ObservableProperty]
    private bool _y2Hatali;

    [ObservableProperty]
    private bool _x2Hatali;

    public override bool Bos => string.IsNullOrWhiteSpace(NoktaNo) && string.IsNullOrWhiteSpace(Y1) && string.IsNullOrWhiteSpace(X1)
        && string.IsNullOrWhiteSpace(Y2) && string.IsNullOrWhiteSpace(X2);

    /// <summary>Tüm hücreler dolu mu (canlı önizleme yalnız tamamlanmış satırlarla yapılır).</summary>
    public bool Tamam => !string.IsNullOrWhiteSpace(NoktaNo) && !string.IsNullOrWhiteSpace(Y1) && !string.IsNullOrWhiteSpace(X1)
        && !string.IsNullOrWhiteSpace(Y2) && !string.IsNullOrWhiteSpace(X2);

    internal static OrtakNoktaSatirVm Metinden(IReadOnlyList<string> p)
    {
        var s = new OrtakNoktaSatirVm { Ek = Fazla(p, 5) };
        s.NoktaNo = Parca(p, 0);
        s.Y1 = Parca(p, 1);
        s.X1 = Parca(p, 2);
        s.Y2 = Parca(p, 3);
        s.X2 = Parca(p, 4);
        return s;
    }

    internal override IEnumerable<string> Alanlar() => [Kirp(NoktaNo), Sayi(Y1), Sayi(X1), Sayi(Y2), Sayi(X2)];

    protected override void Denetle()
    {
        Y1Hatali = SayiHatali(Y1);
        X1Hatali = SayiHatali(X1);
        Y2Hatali = SayiHatali(Y2);
        X2Hatali = SayiHatali(X2);
        Hata = MetinHucresiHatasi("Nokta No", NoktaNo, ilkSutun: true)
            ?? (Y1Hatali ? SayiHatasi("Y1", Y1)
            : X1Hatali ? SayiHatasi("X1", X1)
            : Y2Hatali ? SayiHatasi("Y2", Y2)
            : X2Hatali ? SayiHatasi("X2", X2)
            : EkHatasi());
    }
}

/// <summary>4. adım TAKBİS hisse satırı: <c>kadastroParseli;malik;pay/payda;E|H</c> (malik: <c>AD SOYAD (BABA ADI)</c> veya kod — SORU-055).</summary>
public sealed partial class TakbisSatirVm : TabloSatiri
{
    /// <summary>Muvafakat seçenekleri: E (muvafakatli), H (muvafakatsiz).</summary>
    public static IReadOnlyList<string> MuvafakatSecenekleri { get; } = ["E", "H"];

    [ObservableProperty]
    private string _kadastroParseli = string.Empty;

    [ObservableProperty]
    private string _malikKodu = string.Empty;

    [ObservableProperty]
    private string _hisse = string.Empty;

    [ObservableProperty]
    private bool _hisseHatali;

    [ObservableProperty]
    private bool _muvafakatHatali;

    private string _muvafakat = "H";

    /// <summary>
    /// <c>E</c> / <c>H</c>. Görünümdeki seçim kutusunun listede olmayan değer için geri yazdığı <c>null</c> yok sayılır; böylece
    /// metinden gelen geçersiz değer sessizce silinmez (metin ayrıştırıcısı açık hata verir).
    /// </summary>
    public string Muvafakat
    {
        get => _muvafakat;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _muvafakat, value);
            }
        }
    }

    public override bool Bos => string.IsNullOrWhiteSpace(KadastroParseli) && string.IsNullOrWhiteSpace(MalikKodu) && string.IsNullOrWhiteSpace(Hisse);

    internal static TakbisSatirVm Metinden(IReadOnlyList<string> p)
    {
        var s = new TakbisSatirVm { Ek = Fazla(p, 4) };
        s.KadastroParseli = Parca(p, 0);
        s.MalikKodu = Parca(p, 1);
        s.Hisse = Parca(p, 2);
        s.Muvafakat = Parca(p, 3);
        return s;
    }

    internal override IEnumerable<string> Alanlar() => [Kirp(KadastroParseli), Kirp(MalikKodu), Kirp(Hisse), Kirp(Muvafakat)];

    protected override void Denetle()
    {
        var h = Kirp(Hisse);
        var p = h.Split('/');
        HisseHatali = h.Length > 0 && (p.Length != 2
            || !BigInteger.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            || !BigInteger.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var payda) || payda.IsZero);
        MuvafakatHatali = Kirp(Muvafakat) is { Length: > 0 } m && m is not ("E" or "H");
        Hata = MetinHucresiHatasi("Kadastro Parseli", KadastroParseli, ilkSutun: true)
            ?? MetinHucresiHatasi("Malik", MalikKodu)
            ?? (HisseHatali ? "Hisse: pay/payda tam sayı olmalı ve payda 0 olmamalı (ör. 1/2)."
            : MuvafakatHatali ? "Muvafakat: E veya H olmalı."
            : EkHatasi());
    }
}

/// <summary>4. adım Kroki (tahsis) satırı: <c>malikKodu;kadastroParseli;imarParseli;alan</c>.</summary>
public sealed partial class KrokiSatirVm : TabloSatiri
{
    [ObservableProperty]
    private string _malikKodu = string.Empty;

    [ObservableProperty]
    private string _kadastroParseli = string.Empty;

    [ObservableProperty]
    private string _imarParseli = string.Empty;

    [ObservableProperty]
    private string _alan = string.Empty;

    [ObservableProperty]
    private bool _alanHatali;

    public override bool Bos => string.IsNullOrWhiteSpace(MalikKodu) && string.IsNullOrWhiteSpace(KadastroParseli)
        && string.IsNullOrWhiteSpace(ImarParseli) && string.IsNullOrWhiteSpace(Alan);

    internal static KrokiSatirVm Metinden(IReadOnlyList<string> p)
    {
        var s = new KrokiSatirVm { Ek = Fazla(p, 4) };
        s.MalikKodu = Parca(p, 0);
        s.KadastroParseli = Parca(p, 1);
        s.ImarParseli = Parca(p, 2);
        s.Alan = Parca(p, 3);
        return s;
    }

    internal override IEnumerable<string> Alanlar() => [Kirp(MalikKodu), Kirp(KadastroParseli), Kirp(ImarParseli), Sayi(Alan)];

    protected override void Denetle()
    {
        AlanHatali = SayiHatali(Alan);
        Hata = MetinHucresiHatasi("Malik", MalikKodu, ilkSutun: true)
            ?? MetinHucresiHatasi("Kadastro Parseli", KadastroParseli)
            ?? MetinHucresiHatasi("İmar Parseli", ImarParseli)
            ?? (AlanHatali ? SayiHatasi("Alan (m²)", Alan) : EkHatasi());
    }
}

/// <summary>
/// Tablo ↔ metin iki yönlü senkronu. Metin değişince satırlar yeniden kurulur (metne geri yazılmaz); satır eklenince,
/// silinince veya hücre değişince metin satırlardan yeniden yazılır (satırlar yeniden kurulmaz). Boş satırlar metne yazılmaz ve
/// boş satır eklemek/silmek metni değiştirmez; düzenlenmeyen satırlar aynen yazılır. <c>#</c> yorum satırları (ör. "# Yaklaşık
/// Yer Krokisi …") tabloda gösterilmez ama yerlerinde (izleyen satırın önünde) korunur; satırı silinen yorum bir sonraki satıra
/// geçer. Böylece mevcut metin tabanlı komutlar ve testler değişmeden çalışır.
/// </summary>
/// <typeparam name="T">Satır türü.</typeparam>
internal sealed class TabloMetinSenkronu<T> where T : TabloSatiri
{
    private readonly ObservableCollection<T> _satirlar;
    private readonly Func<IReadOnlyList<string>, T> _satirKur;
    private readonly Action<string> _metniYaz;
    private readonly Action? _tabloDegisti;
    private readonly Dictionary<T, List<string>> _oncekiYorumlar = [];
    private List<string> _sondakiYorumlar = [];
    private bool _yukleniyor;
    private bool _yaziliyor;

    public TabloMetinSenkronu(ObservableCollection<T> satirlar, Func<IReadOnlyList<string>, T> satirKur, Action<string> metniYaz, Action? tabloDegisti = null)
    {
        _satirlar = satirlar;
        _satirKur = satirKur;
        _metniYaz = metniYaz;
        _tabloDegisti = tabloDegisti;
        _satirlar.CollectionChanged += KoleksiyonDegisti;
    }

    /// <summary>Metin değişti: satırları yeniden kurar (tablodan yazılırken çağrılırsa yok sayılır).</summary>
    public void MetindenYukle(string? metin)
    {
        if (_yaziliyor)
        {
            return;
        }

        _yukleniyor = true;
        try
        {
            foreach (var s in _satirlar)
            {
                s.PropertyChanged -= SatirDegisti;
            }

            _satirlar.Clear();
            _oncekiYorumlar.Clear();
            var bekleyen = new List<string>();
            foreach (var satir in (metin ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (satir.StartsWith('#'))
                {
                    bekleyen.Add(satir);
                    continue;
                }

                var s = _satirKur(satir.Split(';').Select(x => x.Trim()).ToList());
                s.Ham = satir;
                if (bekleyen.Count > 0)
                {
                    _oncekiYorumlar[s] = bekleyen;
                    bekleyen = [];
                }

                _satirlar.Add(s);
            }

            _sondakiYorumlar = bekleyen;
        }
        finally
        {
            _yukleniyor = false;
        }
    }

    /// <summary>Satırlardan metni yazar (satırlar yeniden kurulmaz).</summary>
    public void TablodanYaz()
    {
        if (_yukleniyor)
        {
            return;
        }

        _yaziliyor = true;
        try
        {
            var cikti = new List<string>();
            foreach (var s in _satirlar)
            {
                if (_oncekiYorumlar.TryGetValue(s, out var yorumlar))
                {
                    cikti.AddRange(yorumlar);
                }

                if (!s.Bos)
                {
                    cikti.Add(s.MetinSatiri());
                }
            }

            cikti.AddRange(_sondakiYorumlar);
            _metniYaz(string.Join(Environment.NewLine, cikti));
        }
        finally
        {
            _yaziliyor = false;
        }

        _tabloDegisti?.Invoke();
    }

    /// <summary>Tüm satırlar hücrelerden yeniden yazılır (ör. metin alanından çıkınca virgüllü ondalıklar <c>.</c> biçimine çevrilir).</summary>
    public void NormalizeEt()
    {
        foreach (var s in _satirlar)
        {
            s.Ham = null;
        }

        TablodanYaz();
    }

    private void KoleksiyonDegisti(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var metinEtkilenir = false;
        foreach (var s in e.OldItems?.OfType<T>() ?? [])
        {
            s.PropertyChanged -= SatirDegisti;
            metinEtkilenir |= !s.Bos;
            if (_oncekiYorumlar.Remove(s, out var yorumlar))
            {
                metinEtkilenir = true;
                var sonraki = e.OldStartingIndex >= 0 && e.OldStartingIndex < _satirlar.Count ? _satirlar[e.OldStartingIndex] : null;
                if (sonraki is null)
                {
                    _sondakiYorumlar.InsertRange(0, yorumlar);
                }
                else
                {
                    _oncekiYorumlar[sonraki] = [.. yorumlar, .. _oncekiYorumlar.GetValueOrDefault(sonraki) ?? []];
                }
            }
        }

        foreach (var s in e.NewItems?.OfType<T>() ?? [])
        {
            s.PropertyChanged += SatirDegisti;
            metinEtkilenir |= !s.Bos;
        }

        // Boş satır eklemek/silmek metni (ve ona bağlı önizlemeleri) değiştirmez.
        if (metinEtkilenir || e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Move or NotifyCollectionChangedAction.Replace)
        {
            TablodanYaz();
        }
    }

    private void SatirDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is T s && TabloSatiri.HucreMi(e.PropertyName) && _satirlar.Contains(s))
        {
            TablodanYaz();
        }
    }
}
