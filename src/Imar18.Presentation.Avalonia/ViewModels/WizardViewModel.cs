using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tecviz;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>Sihirbazdaki tek katman–rol satiri (muhendis secimi, STATUS SORU-032 b).</summary>
public sealed partial class LayerRoleRow : ObservableObject
{
    public LayerRoleRow(string layerName, CadLayerRole role)
    {
        LayerName = layerName;
        _role = role;
    }

    /// <summary>Tüm roller (her satır ComboBox'ı kendi DataContext'inden bağlar; P7 seçili rol görünürlüğü düzeltmesi).</summary>
    internal static readonly IReadOnlyList<CadLayerRole> TumRoller = Enum.GetValues<CadLayerRole>();

    public string LayerName { get; }

    /// <summary>Seçilebilir roller (ekranda Türkçe görünen adla).</summary>
    public IReadOnlyList<CadLayerRole> Roller => TumRoller;

    [ObservableProperty]
    private CadLayerRole _role;
}

/// <summary>P7: üst adım çubuğundaki tıklanabilir sihirbaz adımı (numaralı rozet, tamamlandı onayı, bağlantı çizgisi).</summary>
public sealed partial class SihirbazAdimi : ObservableObject
{
    public SihirbazAdimi(int index, string baslik)
    {
        Index = index;
        Baslik = baslik;
        var nokta = baslik.IndexOf(". ", StringComparison.Ordinal);
        Ad = nokta > 0 ? baslik[(nokta + 2)..] : baslik;
    }

    public int Index { get; }

    /// <summary>Numaralı tam başlık (ör. "2. Tecviz").</summary>
    public string Baslik { get; }

    /// <summary>Rozet yanındaki numarasız ad (ör. "Tecviz").</summary>
    public string Ad { get; }

    /// <summary>1'den başlayan adım numarası.</summary>
    public int Numara => Index + 1;

    /// <summary>İlk adımın solunda bağlantı çizgisi çizilmez.</summary>
    public bool BaglantiVar => Index > 0;

    /// <summary>Rozet içeriği: tamamlanan adımda onay işareti, diğerlerinde numara.</summary>
    public string RozetMetni => IsTamam ? "✓" : Numara.ToString(CultureInfo.InvariantCulture);

    /// <summary>Soldaki bağlantı çizgisi yeşil mi (önceki adım tamamlandı: bu adım aktif veya tamam).</summary>
    public bool BagTamam => IsActive || IsTamam;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BagTamam))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RozetMetni), nameof(BagTamam))]
    private bool _isTamam;
}

/// <summary>
/// 5 adimli sihirbaz (P6, proje sahibi tanimi): 1. Girdi &amp; Sınır → 2. Tecviz → 3. DOP &amp; Y1 Dengeleme →
/// 4. Hak Eşleştirme &amp; Hisse → 5. Onay &amp; Çıktı. Ilk adim proje CRS/dilim/olcek (<see cref="ProjeCrs"/>),
/// <see cref="TecvizGirdisi"/> (muhendis girdisi) ve katman–rol eslemesini toplar; dogrulama Domain'e birakilir ve
/// kullaniciya yalnizca Turkce <c>UserMessage</c> gosterilir. P7 cilası: her alanın kendi hata metni (kırmızı çerçeve +
/// alt satır) vardır ve alan düzenlenince temizlenir; doğrulama kartı hata/uyarı sayısını rozetle gösterir.
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    private const string UyariOneki = "Uyarı:";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(AdimaGitCommand))]
    private int _currentIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(AdimaGitCommand))]
    [NotifyPropertyChangedFor(nameof(DogrulamaBasarili))]
    private bool _isInputValid;

    /// <summary>En az bir doğrulama yapıldı mı (sonuç kartı ancak o zaman gösterilir).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DogrulamaBasarili))]
    private bool _dogrulamaYapildi;

    // ---- Alan bazlı hata metinleri (null: alan geçerli) ----
    [ObservableProperty]
    private string? _epokHatasi;

    [ObservableProperty]
    private string? _olcekHatasi;

    [ObservableProperty]
    private string? _crsHatasi;

    [ObservableProperty]
    private string? _tecvizOlcekHatasi;

    [ObservableProperty]
    private string? _tecvizUretimYontemiHatasi;

    [ObservableProperty]
    private string? _tecvizSiniriHatasi;

    [ObservableProperty]
    private string? _tecvizKaynakNotuHatasi;

    [ObservableProperty]
    private string? _katmanRolHatasi;

    public WizardViewModel()
    {
        AdimOgeleri = Steps.Select((s, i) => new SihirbazAdimi(i, s)).ToList().AsReadOnly();
        AdimDurumlariniYenile();
        Errors.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HataSayisi));
            OnPropertyChanged(nameof(UyariSayisi));
            OnPropertyChanged(nameof(DogrulamaOzeti));
        };
    }

    /// <summary>P7: üst çubuktaki tıklanabilir adımlar.</summary>
    public IReadOnlyList<SihirbazAdimi> AdimOgeleri { get; }

    /// <summary>Sağ panel yalnızca 5. adımda onay ve çıktı kartlarını gösterir.</summary>
    public bool SagPanelOnayModu => CurrentIndex == Steps.Count - 1;

    /// <summary>1–4. adımlarda sağ panel parsel işlemleri ve sade özet kartını gösterir.</summary>
    public bool SagPanelOzetModu => !SagPanelOnayModu;

    [ObservableProperty]
    private GeodetikDatum _datum = GeodetikDatum.Itrf96;

    [ObservableProperty]
    private int _ortaMeridyen = 30;

    [ObservableProperty]
    private string _epokText = "2005.00";

    [ObservableProperty]
    private int _olcekPaydasi = 1000;

    [ObservableProperty]
    private int _tecvizOlcekPaydasi = 1000;

    [ObservableProperty]
    private string _tecvizUretimYontemi = string.Empty;

    [ObservableProperty]
    private KoordinatKalitesi _tecvizKoordinatKalitesi = KoordinatKalitesi.ArsivOnayli;

    [ObservableProperty]
    private string _tecvizSiniriText = string.Empty;

    [ObservableProperty]
    private string _tecvizKaynakNotu = string.Empty;

    public IReadOnlyList<string> Steps { get; } =
        ["1. Girdi & Sınır", "2. Tecviz", "3. DOP & Y1 Dengeleme", "4. Hak Eşleştirme & Hisse", "5. Onay & Çıktı"];

    public IReadOnlyList<GeodetikDatum> Datumlar { get; } = Enum.GetValues<GeodetikDatum>();

    public IReadOnlyList<int> OrtaMeridyenler => ProjeCrs.DesteklenenOrtaMeridyenler;

    public IReadOnlyList<CadLayerRole> Roller => LayerRoleRow.TumRoller;

    public IReadOnlyList<KoordinatKalitesi> Kaliteler { get; } = Enum.GetValues<KoordinatKalitesi>();

    public ObservableCollection<LayerRoleRow> LayerRoles { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>Doğrulama kartı rozeti: hata (engel) sayısı.</summary>
    public int HataSayisi => Errors.Count(e => !e.StartsWith(UyariOneki, StringComparison.Ordinal));

    /// <summary>Doğrulama kartı rozeti: uyarı sayısı.</summary>
    public int UyariSayisi => Errors.Count(e => e.StartsWith(UyariOneki, StringComparison.Ordinal));

    /// <summary>Son doğrulama başarılıysa (yeşil onay kartı).</summary>
    public bool DogrulamaBasarili => DogrulamaYapildi && IsInputValid;

    /// <summary>Doğrulama kartı başlığı ("2 hata · 1 uyarı" / "Girdiler geçerli").</summary>
    public string DogrulamaOzeti => HataSayisi == 0
        ? (UyariSayisi == 0 ? "Tüm girdiler geçerli" : string.Create(CultureInfo.InvariantCulture, $"Geçerli · {UyariSayisi} uyarı"))
        : string.Create(CultureInfo.InvariantCulture, $"{HataSayisi} hata · {UyariSayisi} uyarı");

    /// <summary>Son basarili dogrulamada uretilen proje CRS'i.</summary>
    public ProjeCrs? Crs { get; private set; }

    /// <summary>Son basarili dogrulamada uretilen tecviz girdisi.</summary>
    public TecvizGirdisi? Tecviz { get; private set; }

    /// <summary>Standart esleme + muhendis secimleri.</summary>
    public CadLayerRoleMap RoleMap => LayerRoles.Aggregate(CadLayerRoleMap.Standard, (m, r) => m.WithAssignment(r.LayerName, r.Role));

    public string CurrentStepTitle => Steps[CurrentIndex];

    /// <summary>Sihirbaz başlığındaki ilerleme rozeti ("Adım 1 / 5").</summary>
    public string AdimSayaci => string.Create(CultureInfo.InvariantCulture, $"Adım {CurrentIndex + 1} / {Steps.Count}");

    /// <summary>"Girdileri Doğrula" düğmesi sabit alt çubukta yalnız 1. adımda görünür.</summary>
    public bool DogrulamaGorunur => CurrentIndex == 0;

    public bool IsStep1 => CurrentIndex == 0;

    public bool IsStep2 => CurrentIndex == 1;

    public bool IsStep3 => CurrentIndex == 2;

    public bool IsStep4 => CurrentIndex == 3;

    public bool IsStep5 => CurrentIndex == 4;

    /// <summary>Muhendis bir katmanin rolunu degistirdiginde (etiket eslestirmesi ve sahne yeniden kurulmalidir).</summary>
    public event EventHandler? LayerRolesChanged;

    /// <summary>Ilk adim girdileri basariyla dogrulandiginda (P5: CRS CommandBus ile projeye yazilir).</summary>
    public event EventHandler? GirdiDogrulandi;

    /// <summary>Belge katmanlarini yukler; rol onerisi standart eslemeden gelir.</summary>
    public void LoadLayers(IEnumerable<CadLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        KatmanlariTemizle();
        foreach (var l in layers)
        {
            var row = new LayerRoleRow(l.Name, CadLayerRoleMap.Standard.Resolve(l.Name));
            row.PropertyChanged += OnRowChanged;
            LayerRoles.Add(row);
        }

        KatmanRolHatasi = null;
        Invalidate();
    }

    /// <summary>
    /// P7 "Projeyi Temizle / Sıfırla": katmanlar, doğrulama sonuçları ve tüm 1. adım girdileri varsayılana döner; sihirbaz 1. adıma
    /// geçer.
    /// </summary>
    public void Sifirla()
    {
        KatmanlariTemizle();
        Datum = GeodetikDatum.Itrf96;
        OrtaMeridyen = 30;
        EpokText = "2005.00";
        OlcekPaydasi = 1000;
        TecvizOlcekPaydasi = 1000;
        TecvizUretimYontemi = string.Empty;
        TecvizKoordinatKalitesi = KoordinatKalitesi.ArsivOnayli;
        TecvizSiniriText = string.Empty;
        TecvizKaynakNotu = string.Empty;
        Errors.Clear();
        AlanHatalariniTemizle();
        DogrulamaYapildi = false;
        Invalidate();
        CurrentIndex = 0;
    }

    private void KatmanlariTemizle()
    {
        foreach (var r in LayerRoles)
        {
            r.PropertyChanged -= OnRowChanged;
        }

        LayerRoles.Clear();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerRoleRow.Role))
        {
            KatmanRolHatasi = null;
            Invalidate();
            LayerRolesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Girdi degisti: onceki dogrulama gecersizdir (eski CRS/tecviz ile ilerlenemez).</summary>
    private void Invalidate()
    {
        IsInputValid = false;
        Crs = null;
        Tecviz = null;
    }

    private void AlanHatalariniTemizle()
    {
        EpokHatasi = null;
        OlcekHatasi = null;
        CrsHatasi = null;
        TecvizOlcekHatasi = null;
        TecvizUretimYontemiHatasi = null;
        TecvizSiniriHatasi = null;
        TecvizKaynakNotuHatasi = null;
        KatmanRolHatasi = null;
    }

    partial void OnDatumChanged(GeodetikDatum value)
    {
        CrsHatasi = null;
        EpokHatasi = null;
        Invalidate();
    }

    partial void OnOrtaMeridyenChanged(int value)
    {
        CrsHatasi = null;
        Invalidate();
    }

    partial void OnEpokTextChanged(string value)
    {
        EpokHatasi = null;
        Invalidate();
    }

    partial void OnOlcekPaydasiChanged(int value)
    {
        OlcekHatasi = null;
        Invalidate();
    }

    partial void OnTecvizOlcekPaydasiChanged(int value)
    {
        TecvizOlcekHatasi = null;
        Invalidate();
    }

    partial void OnTecvizUretimYontemiChanged(string value)
    {
        TecvizUretimYontemiHatasi = null;
        Invalidate();
    }

    partial void OnTecvizKoordinatKalitesiChanged(KoordinatKalitesi value) => Invalidate();

    partial void OnTecvizSiniriTextChanged(string value)
    {
        TecvizSiniriHatasi = null;
        Invalidate();
    }

    partial void OnTecvizKaynakNotuChanged(string value)
    {
        TecvizKaynakNotuHatasi = null;
        Invalidate();
    }

    /// <summary>
    /// Ilk adim girdilerini dogrular; hatalar <see cref="Errors"/> listesine Turkce eklenir ve ilgili alanın hata metnine yazılır
    /// (kurallar Domain'dedir; alan eşlemesi yalnız gösterim içindir).
    /// </summary>
    public bool Validate()
    {
        Errors.Clear();
        AlanHatalariniTemizle();
        Crs = null;
        Tecviz = null;
        DogrulamaYapildi = true;

        decimal? epok = null;
        var epokOkundu = true;
        if (!string.IsNullOrWhiteSpace(EpokText))
        {
            if (TryParseDecimal(EpokText, out var e))
            {
                epok = e;
            }
            else
            {
                epokOkundu = false;
                EpokHatasi = "Epok geçerli bir sayı olmalı (örnek 2005.00).";
                Errors.Add(EpokHatasi);
            }
        }

        if (epokOkundu)
        {
            var crs = ProjeCrs.Olustur(Datum, OrtaMeridyen, epok, OlcekPaydasi);
            if (crs.IsSuccess)
            {
                Crs = crs.Value;
                foreach (var u in crs.Value.Uyarilar)
                {
                    Errors.Add(UyariOneki + " " + u.UserMessage);
                }
            }
            else
            {
                Errors.Add(crs.Error.UserMessage);
                if (OlcekPaydasi <= 0)
                {
                    OlcekHatasi = "Pafta ölçeği paydası 0'dan büyük olmalı.";
                }
                else if (crs.Error.Code == ErrorCodes.CrsEksik || epok is not null)
                {
                    EpokHatasi = crs.Error.UserMessage;
                }
                else
                {
                    CrsHatasi = crs.Error.UserMessage;
                }
            }
        }

        if (!TryParseDecimal(TecvizSiniriText, out var sinir))
        {
            TecvizSiniriHatasi = "Tecviz sınırı (m²) geçerli bir sayı olmalı.";
            Errors.Add(TecvizSiniriHatasi);
            TecvizAlanlariniIsaretle(null);
        }
        else
        {
            var t = TecvizGirdisi.Olustur(TecvizOlcekPaydasi, TecvizUretimYontemi, TecvizKoordinatKalitesi, sinir, TecvizKaynakNotu);
            if (t.IsSuccess)
            {
                Tecviz = t.Value;
            }
            else
            {
                Errors.Add("Tecviz girdisi: " + t.Error.UserMessage);
                TecvizAlanlariniIsaretle(sinir);
            }
        }

        var katman = new List<string>();
        if (!LayerRoles.Any(r => r.Role == CadLayerRole.CadastralParcel))
        {
            katman.Add("En az bir katman 'kadastro parseli (giren)' rolüne atanmalı.");
        }

        if (!LayerRoles.Any(r => r.Role == CadLayerRole.ZoningParcel))
        {
            katman.Add("En az bir katman 'imar parseli (oluşan)' rolüne atanmalı.");
        }

        foreach (var k in katman)
        {
            Errors.Add(k);
        }

        KatmanRolHatasi = katman.Count == 0 ? null : string.Join(" ", katman);

        IsInputValid = Crs is not null && Tecviz is not null && !Errors.Any(e => !e.StartsWith(UyariOneki, StringComparison.Ordinal));
        OnPropertyChanged(nameof(DogrulamaOzeti));
        if (IsInputValid)
        {
            GirdiDogrulandi?.Invoke(this, EventArgs.Empty);
        }

        return IsInputValid;
    }

    /// <summary>Tecviz girdisinin boş/geçersiz alanlarını ayrı ayrı işaretler (Domain ilk hatada durur; kullanıcı hepsini görür).</summary>
    private void TecvizAlanlariniIsaretle(decimal? sinir)
    {
        if (TecvizOlcekPaydasi <= 0)
        {
            TecvizOlcekHatasi = "Ölçek paydası 0'dan büyük olmalı.";
        }

        if (string.IsNullOrWhiteSpace(TecvizUretimYontemi))
        {
            TecvizUretimYontemiHatasi = "Üretim yöntemi boş bırakılamaz (ör. Sayısallaştırma).";
        }

        if (sinir is <= 0m)
        {
            TecvizSiniriHatasi = "Tecviz sınırı 0'dan büyük olmalı.";
        }

        if (string.IsNullOrWhiteSpace(TecvizKaynakNotu))
        {
            TecvizKaynakNotuHatasi = "Kaynak notu boş bırakılamaz (ör. TKGM / Netcad dengeleme raporu).";
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => CurrentIndex++;

    private bool CanGoNext() => CurrentIndex < Steps.Count - 1 && (CurrentIndex > 0 || IsInputValid);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentIndex--;

    private bool CanGoBack() => CurrentIndex > 0;

    /// <summary>P7: adım çubuğundan doğrudan geçiş. Geri dönüş serbesttir; ileri atlama 1. adım doğrulamasını gerektirir.</summary>
    [RelayCommand(CanExecute = nameof(AdimaGidilebilir))]
    private void AdimaGit(SihirbazAdimi? adim)
    {
        if (adim is not null && AdimaGidilebilir(adim))
        {
            CurrentIndex = adim.Index;
        }
    }

    private bool AdimaGidilebilir(SihirbazAdimi? adim) =>
        adim is not null && adim.Index != CurrentIndex && adim.Index >= 0 && adim.Index < Steps.Count
        && (adim.Index < CurrentIndex || IsInputValid);

    partial void OnCurrentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStepTitle));
        OnPropertyChanged(nameof(AdimSayaci));
        OnPropertyChanged(nameof(DogrulamaGorunur));
        foreach (var ad in new[] { nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsStep5), nameof(SagPanelOnayModu), nameof(SagPanelOzetModu) })
        {
            OnPropertyChanged(ad);
        }

        AdimDurumlariniYenile();
    }

    private void AdimDurumlariniYenile()
    {
        foreach (var a in AdimOgeleri)
        {
            a.IsActive = a.Index == CurrentIndex;
            a.IsTamam = a.Index < CurrentIndex;
        }
    }

    /// <summary>Hem '.' hem ',' ondalik ayiraci kabul edilir (binlik ayirac kabul edilmez).</summary>
    private static bool TryParseDecimal(string? text, out decimal value) => SayiGirdisi.TryParse(text, out value);
}
