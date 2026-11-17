using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cad;
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

    public string LayerName { get; }

    [ObservableProperty]
    private CadLayerRole _role;
}

/// <summary>
/// 5 adimli sihirbaz (roadmap P4.2 / UI referansi): 1. Girdi &amp; Sınır → 2. DOP &amp; Özet → 3. Ada Bölümleme →
/// 4. Tahsis &amp; Hisse → 5. Cetvel &amp; Çıktı. Ilk adim proje CRS/dilim/olcek (<see cref="ProjeCrs"/>),
/// <see cref="TecvizGirdisi"/> (muhendis girdisi) ve katman–rol eslemesini toplar; dogrulama Domain'e birakilir ve
/// kullaniciya yalnizca Turkce <c>UserMessage</c> gosterilir. 5. adim (Cetvel &amp; Çıktı) P5'te <see cref="OnayCiktiViewModel"/> ile baglandi; 2–4. adim is kurallari P6'dadir.
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    private int _currentIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _isInputValid;

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
        ["1. Girdi & Sınır", "2. DOP & Özet", "3. Ada Bölümleme", "4. Tahsis & Hisse", "5. Cetvel & Çıktı"];

    public IReadOnlyList<GeodetikDatum> Datumlar { get; } = Enum.GetValues<GeodetikDatum>();

    public IReadOnlyList<int> OrtaMeridyenler => ProjeCrs.DesteklenenOrtaMeridyenler;

    public IReadOnlyList<CadLayerRole> Roller { get; } = Enum.GetValues<CadLayerRole>();

    public IReadOnlyList<KoordinatKalitesi> Kaliteler { get; } = Enum.GetValues<KoordinatKalitesi>();

    public ObservableCollection<LayerRoleRow> LayerRoles { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>Son basarili dogrulamada uretilen proje CRS'i.</summary>
    public ProjeCrs? Crs { get; private set; }

    /// <summary>Son basarili dogrulamada uretilen tecviz girdisi.</summary>
    public TecvizGirdisi? Tecviz { get; private set; }

    /// <summary>Standart esleme + muhendis secimleri.</summary>
    public CadLayerRoleMap RoleMap => LayerRoles.Aggregate(CadLayerRoleMap.Standard, (m, r) => m.WithAssignment(r.LayerName, r.Role));

    public string CurrentStepTitle => Steps[CurrentIndex];

    /// <summary>Muhendis bir katmanin rolunu degistirdiginde (etiket eslestirmesi ve sahne yeniden kurulmalidir).</summary>
    public event EventHandler? LayerRolesChanged;

    /// <summary>Ilk adim girdileri basariyla dogrulandiginda (P5: CRS CommandBus ile projeye yazilir).</summary>
    public event EventHandler? GirdiDogrulandi;

    /// <summary>Belge katmanlarini yukler; rol onerisi standart eslemeden gelir.</summary>
    public void LoadLayers(IEnumerable<CadLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        foreach (var r in LayerRoles)
        {
            r.PropertyChanged -= OnRowChanged;
        }

        LayerRoles.Clear();
        foreach (var l in layers)
        {
            var row = new LayerRoleRow(l.Name, CadLayerRoleMap.Standard.Resolve(l.Name));
            row.PropertyChanged += OnRowChanged;
            LayerRoles.Add(row);
        }

        Invalidate();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerRoleRow.Role))
        {
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

    partial void OnDatumChanged(GeodetikDatum value) => Invalidate();

    partial void OnOrtaMeridyenChanged(int value) => Invalidate();

    partial void OnEpokTextChanged(string value) => Invalidate();

    partial void OnOlcekPaydasiChanged(int value) => Invalidate();

    partial void OnTecvizOlcekPaydasiChanged(int value) => Invalidate();

    partial void OnTecvizUretimYontemiChanged(string value) => Invalidate();

    partial void OnTecvizKoordinatKalitesiChanged(KoordinatKalitesi value) => Invalidate();

    partial void OnTecvizSiniriTextChanged(string value) => Invalidate();

    partial void OnTecvizKaynakNotuChanged(string value) => Invalidate();

    /// <summary>Ilk adim girdilerini dogrular; hatalar <see cref="Errors"/> listesine Turkce eklenir.</summary>
    public bool Validate()
    {
        Errors.Clear();
        Crs = null;
        Tecviz = null;

        decimal? epok = null;
        if (!string.IsNullOrWhiteSpace(EpokText))
        {
            if (TryParseDecimal(EpokText, out var e))
            {
                epok = e;
            }
            else
            {
                Errors.Add("Epok geçerli bir sayı olmalı (örnek 2005.00).");
            }
        }

        if (Errors.Count == 0)
        {
            var crs = ProjeCrs.Olustur(Datum, OrtaMeridyen, epok, OlcekPaydasi);
            if (crs.IsSuccess)
            {
                Crs = crs.Value;
                foreach (var u in crs.Value.Uyarilar)
                {
                    Errors.Add("Uyarı: " + u.UserMessage);
                }
            }
            else
            {
                Errors.Add(crs.Error.UserMessage);
            }
        }

        if (!TryParseDecimal(TecvizSiniriText, out var sinir))
        {
            Errors.Add("Tecviz sınırı (m²) geçerli bir sayı olmalı.");
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
            }
        }

        if (!LayerRoles.Any(r => r.Role == CadLayerRole.CadastralParcel))
        {
            Errors.Add("En az bir katman 'kadastro parseli (giren)' rolüne atanmalı.");
        }

        if (!LayerRoles.Any(r => r.Role == CadLayerRole.ZoningParcel))
        {
            Errors.Add("En az bir katman 'imar parseli (oluşan)' rolüne atanmalı.");
        }

        IsInputValid = Crs is not null && Tecviz is not null && !Errors.Any(e => !e.StartsWith("Uyarı:", StringComparison.Ordinal));
        if (IsInputValid)
        {
            GirdiDogrulandi?.Invoke(this, EventArgs.Empty);
        }

        return IsInputValid;
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => CurrentIndex++;

    private bool CanGoNext() => CurrentIndex < Steps.Count - 1 && (CurrentIndex > 0 || IsInputValid);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentIndex--;

    private bool CanGoBack() => CurrentIndex > 0;

    partial void OnCurrentIndexChanged(int value) => OnPropertyChanged(nameof(CurrentStepTitle));

    /// <summary>Hem '.' hem ',' ondalik ayiraci kabul edilir (binlik ayirac kabul edilmez).</summary>
    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0m;
        var t = (text ?? string.Empty).Trim().Replace(',', '.');
        return t.Length > 0 && t.Count(c => c == '.') <= 1
            && decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
