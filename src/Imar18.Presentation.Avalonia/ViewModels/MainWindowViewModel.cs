using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cad;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.Gorunum;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>Alt paneldeki canli parsel tablosu satiri.</summary>
public sealed record ParcelRow(string LayerName, string Role, string Label, decimal? AreaM2);

/// <summary>
/// Ana pencere ViewModel'i (P4.3). Yalnizca Application katmaniyla calisir (<see cref="CadImportService"/>);
/// Infrastructure adaptorleri Bootstrapper'da baglanir (NetArchTest). Ice aktarma arka planda, ilerleme ve iptal ile
/// yurur; DXF/DWG icin etiket–poligon eslestirmesi (P4.2) sihirbazdaki rol eslemesiyle yapilir.
/// P5: proje calisma alani verildiyse ice aktarilan parsel geometrisi ve dogrulanan CRS <see cref="ProjeKomutYolu"/>
/// (CommandBus) ile projeye yazilir; 5. adim <see cref="OnayCiktiViewModel"/>'dir ve Fen Klasoru baglam menusu onaya baglanir.
/// P6: 2–4. adimlar (tecviz, DOP &amp; Y1, hak eslestirme) <see cref="HesapAdimlariViewModel"/> ile canli motora baglidir.
/// P7 cilası: ayrı stüdyo (uygulama) ve pafta (yalnız çizim) temaları, yalnız hesabı etkileyen içe aktarma uyarılarının sayılması
/// (Z=0 indirgeme / 1 mm ızgara notu bilgi satırıdır), Projeyi Temizle / Sıfırla, panoya kopyalama ve canlı Katman → Rol eşlemesi.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// Projeyi/hesabı bozmayan rutin içe aktarma notları (uyarı sayısına girmez, bilgi satırıdır): Z=0 yatay düzleme indirgeme
    /// (<c>ERR-GIRD-004</c>) ve 1 mm ızgara dışı köşe bilgisi (<c>ERR-GIRD-006</c>; koordinatlar korunur, kuantizasyon yapılmaz).
    /// </summary>
    internal static readonly IReadOnlySet<string> RutinIceAktarmaKodlari = new HashSet<string>(StringComparer.Ordinal)
    {
        ErrorCodes.GirdZKoduIndirgendi,
        ErrorCodes.GirdIzgaraDisiKose,
    };

    private readonly CadImportService _importService;
    private readonly ProjeCalismaAlani? _proje;
    private CancellationTokenSource? _importCts;
    private CadLayerDocument? _lastDocument;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelImportCommand), nameof(ProjeyiTemizleCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusText = "Hazır — CAD dosyası açın (Ctrl+O) veya komut çubuğunu kullanın (Ctrl+K).";

    /// <summary>P7: Koyu Stüdyo (false) / Açık Stüdyo (true) — tüm uygulama penceresi, paneller ve kartlar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemaEtiketi))]
    private bool _acikStudyo;

    /// <summary>P7: Koyu Pafta (false) / Açık Pafta (true) — yalnız çizim tuvali (stüdyo temasından bağımsız değiştirilebilir).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaftaEtiketi))]
    private bool _acikPafta;

    [ObservableProperty]
    private bool _solPanelAcik = true;

    [ObservableProperty]
    private bool _sagPanelAcik = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AltPanelEtiketi))]
    private bool _altPanelAcik = true;

    /// <summary>Stüdyo tema anahtarının etiketi (geçilecek tema).</summary>
    public string TemaEtiketi => AcikStudyo ? "🌙 Koyu Stüdyo" : "☀️ Açık Stüdyo";

    /// <summary>Pafta (çizim) anahtarının etiketi (geçilecek pafta).</summary>
    public string PaftaEtiketi => AcikPafta ? "🌑 Koyu Pafta" : "📄 Açık Pafta";

    /// <summary>Alt canlı cetvel panelinin aç/kapat etiketi.</summary>
    public string AltPanelEtiketi => AltPanelAcik ? "▼ Cetveli Gizle" : "▲ Cetveli Göster";

    /// <summary>"Uyarı &amp; Denetim" sekmesindeki sayı: hesabı/topolojiyi etkileyen içe aktarma + topoloji uyarıları.</summary>
    public int DenetimUyariSayisi => Warnings.Count + (OnayCikti?.TopolojiUyariSayisi ?? 0);

    partial void OnAcikPaftaChanged(bool value) => Canvas.AcikPafta = value;

    /// <summary>Stüdyo temasını değiştirir; pafta da aynı temaya eşlenir (sonra pafta anahtarıyla ayrıca değiştirilebilir).</summary>
    [RelayCommand]
    private void TemaDegistir()
    {
        AcikStudyo = !AcikStudyo;
        AcikPafta = AcikStudyo;
    }

    /// <summary>Yalnız çizim paftasını açık/koyu yapar (stüdyo teması değişmez).</summary>
    [RelayCommand]
    private void PaftaDegistir() => AcikPafta = !AcikPafta;

    [RelayCommand]
    private void SolPanelDegistir() => SolPanelAcik = !SolPanelAcik;

    [RelayCommand]
    private void SagPanelDegistir() => SagPanelAcik = !SagPanelAcik;

    [RelayCommand]
    private void AltPanelDegistir() => AltPanelAcik = !AltPanelAcik;

    /// <param name="importService">Ana CAD içe aktarımı.</param>
    /// <param name="proje">Proje çalışma alanı (yoksa P4 davranışı).</param>
    /// <param name="krokiImportService">P7.3-B2: Yaklaşık Yer Krokisi için ayrı servis örneği (ana içe aktarımın arka plan
    /// okumasıyla çakışmaz — reviewer M5); verilmezse <paramref name="importService"/> kullanılır.</param>
    public MainWindowViewModel(CadImportService importService, ProjeCalismaAlani? proje = null, CadImportService? krokiImportService = null)
    {
        ArgumentNullException.ThrowIfNull(importService);
        _importService = importService;
        _proje = proje;

        Palette.Register(new PaletteCommand("openFile", "CAD Dosyası Aç (.ncz / .dxf / .dwg)", "import iceri aktar ncz dxf dwg", () => OpenFileRequested?.Invoke(this, EventArgs.Empty)));
        Palette.Register(new PaletteCommand("clearProject", "Projeyi Temizle / Sıfırla", "temizle sifirla yeni bos reset harita", () => ProjeyiTemizleCommand.Execute(null), () => ProjeyiTemizleCommand.CanExecute(null)));
        Palette.Register(new PaletteCommand("fitAll", "Tümünü Sığdır", "zoom extents ortala", Canvas.FitAll));
        Palette.Register(new PaletteCommand("swipeCadastre", "Kadastro Görünümü (%0)", "eski sinirlandirma swipe", () => Canvas.SwipePercent = 0));
        Palette.Register(new PaletteCommand("swipeZoning", "İmar Görünümü (%100)", "yeni parselasyon swipe", () => Canvas.SwipePercent = 100));
        Palette.Register(new PaletteCommand("toggleCutHandle", "Kesim Tutamacını Göster/Gizle", "bisection kesim aci", () => Canvas.CutHandle.IsVisible = !Canvas.CutHandle.IsVisible));
        Palette.Register(new PaletteCommand("nextStep", "Sonraki Sihirbaz Adımı", "ileri wizard", () => Wizard.NextCommand.Execute(null), () => Wizard.NextCommand.CanExecute(null)));
        Palette.Register(new PaletteCommand("previousStep", "Önceki Sihirbaz Adımı", "geri wizard", () => Wizard.BackCommand.Execute(null), () => Wizard.BackCommand.CanExecute(null)));
        Palette.Register(new PaletteCommand("cancelImport", "İçe Aktarmayı İptal Et", "durdur vazgec", CancelImport, () => IsBusy));
        Palette.Register(new PaletteCommand("toggleTheme", "Koyu / Açık Stüdyo Teması", "tema studyo pafta acik koyu kagit gece", TemaDegistir));
        Palette.Register(new PaletteCommand("togglePaper", "Koyu / Açık Pafta (yalnız çizim)", "pafta kagit tuval cizim beyaz", PaftaDegistir));
        Palette.Register(new PaletteCommand("applyRoles", "Katman → Rol Eşlemesini Uygula", "katman rol esleme uygula", () => EslemeyiUygulaCommand.Execute(null)));
        Palette.Register(new PaletteCommand("toggleLeft", "Sol Paneli Aç/Kapat", "sihirbaz girdi panel", SolPanelDegistir));
        Palette.Register(new PaletteCommand("toggleRight", "Sağ Denetçi Panelini Aç/Kapat", "denetci parsel islemleri panel", SagPanelDegistir));
        Palette.Register(new PaletteCommand("toggleBottom", "Alt Canlı Cetveli Aç/Kapat", "cetvel tablo alt panel", AltPanelDegistir));
        Palette.Register(new PaletteCommand("toggleLabels", "Parsel Etiketlerini Göster/Gizle", "etiket yazi numara", () => Canvas.EtiketGoster = !Canvas.EtiketGoster));
        Palette.Register(new PaletteCommand("toggleGrid", "Izgarayı Göster/Gizle", "izgara grid", () => Canvas.IzgaraGoster = !Canvas.IzgaraGoster));
        Wizard.LayerRolesChanged += (_, _) => LastReapplyTask = ReapplyAsync();
        Warnings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DenetimUyariSayisi));
        Canvas.NoktaSecildi += (_, metin) => PanoyaKopyala(metin, "📍 OSNAP noktası seçildi ve panoya kopyalandı: " + metin);

        // P7.3-A (SAHA-005): tuvalden katman → rol ataması sihirbaz satırına yazılır; mevcut LayerRolesChanged → ReapplyAsync →
        // ParselGeometrileriAyarlaKomutu zinciri (CommandBus + audit) aynen çalışır. Liste 🎯 ↔ tuval vurgusu senkron.
        Canvas.KatmanRolAtamaIstendi += (_, a) =>
        {
            if (!Wizard.RolAta(a.KatmanAdi, a.Rol))
            {
                StatusText = $"'{a.KatmanAdi}' katmanı eşleme listesinde yok; rol atanmadı.";
            }
        };
        Wizard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WizardViewModel.VurguluKatmanAdi))
            {
                Canvas.IsaretliKatman = Wizard.VurguluKatmanAdi;
            }
            else if (e.PropertyName == nameof(WizardViewModel.CurrentIndex))
            {
                RolAtamaEtkinliginiYenile();
            }
        };
        Wizard.LayerRoles.CollectionChanged += (_, _) => RolAtamaEtkinliginiYenile();

        if (proje is not null)
        {
            OnayCikti = new OnayCiktiViewModel(proje);
            OnayCikti.TopolojiNotlari.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DenetimUyariSayisi));
            Hesap = new HesapAdimlariViewModel(proje, () => Wizard.Tecviz, krokiImportService ?? importService);
            Palette.Register(new PaletteCommand("importWebtapu", "webtapu PDF Yükle (TAKBİS + Malik Rehberi)", "tapu kaydi webtapu pdf takbis malik hisse",
                () => Hesap.WebtapuIsteCommand.Execute(null), () => Hesap.WebtapuIsteCommand.CanExecute(null)));
            Palette.Register(new PaletteCommand("importSketch", "Yaklaşık Yer Krokisi Yükle (.NCZ)", "kroki yaklasik yer kullanim ncz hak",
                () => Hesap.KrokiNczIsteCommand.Execute(null), () => Hesap.KrokiNczIsteCommand.CanExecute(null)));
            Kunye = new KunyeDonusumViewModel(proje);
            Kunye.Uygulandi += (_, _) => LastProjeTask = OnayCikti.YenileAsync();

            // SAHA-014: .DNS uygulanınca/kaldırılınca tuval Deprem Sonrası/Öncesi koordinatlarla yenilenir; 2–4. adım görünümleri tazelenir.
            Kunye.IleriDonusumDegisti += (_, _) =>
            {
                LastReapplyTask = DonusumSahnesiniYenileAsync();
                LastProjeTask = Hesap.YenileAsync();
            };
            Hesap.Uygulandi += (_, _) =>
            {
                Kunye.OneriyiYenile();
                LastProjeTask = OnayCikti.YenileAsync();
            };
            Hesap.TakbiseAktarildi += (_, _) => Kunye.WebtapuAktarildi();

            // SAHA-015: kroki parselasyon katmanı (taslak / uygulanmış) tuvalde CAD sahnesinin üstüne çizilir.
            Hesap.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HesapAdimlariViewModel.ParselasyonKatmani))
                {
                    Canvas.ParselasyonKatmani = Hesap.ParselasyonKatmani;
                }
            };
            LastProjeTask = Hesap.YenileAsync();
            var fen = new RelayCommand(() => OnayCikti.FenKlasoruZipIsteCommand.Execute(null), () => OnayCikti.GecerliOnayVar);
            Canvas.BaglaFenKlasoru(fen);
            OnayCikti.OnayDurumuDegisti += (_, _) => fen.NotifyCanExecuteChanged();
            Palette.Register(new PaletteCommand("approve", "Mühendis Onayı Ver (güncel revizyon)", "onay revizyon approval", () => OnayCikti.OnaylaCommand.Execute(null), () => OnayCikti.OnaylaCommand.CanExecute(null)));
            Palette.Register(new PaletteCommand("revokeApproval", "Onayı Geri Al (24 saat)", "geri al onay iptal", () => OnayCikti.GeriAlCommand.Execute(null), () => OnayCikti.GeriAlCommand.CanExecute(null)));
            Palette.Register(new PaletteCommand("exportXlsx", "Excel Cetvelleri Üret (.xlsx)", "cetvel excel 4lu alan dagitim ozet", () => OnayCikti.CetvelIsteCommand.Execute(null)));
            Palette.Register(new PaletteCommand("exportZip", "Fen Klasörü Üret (.zip — ayrı PDF'ler)", "fen klasoru zip pdf tescil rolove cepheli koordine dop ozet", () => OnayCikti.FenKlasoruZipIsteCommand.Execute(null)));
            Palette.Register(new PaletteCommand("exportPdf", "Fen Klasörü Üret (.pdf)", "fen klasoru pdf fr348 fr521 he m2 dm2", () => OnayCikti.FenKlasoruIsteCommand.Execute(null)));
            Palette.Register(new PaletteCommand("exportCks", "DOP Özeti Yaz (.CKS)", "cks netcad dop oko", () => OnayCikti.CksIsteCommand.Execute(null)));
            Wizard.GirdiDogrulandi += (_, _) =>
            {
                // Aynı CRS yeniden doğrulanınca gereksiz denetim kaydı/dosya yazımı yapılmaz (reviewer L5).
                if (Wizard.Crs is { } crs && !AyniCrs(proje.Yol.Belge.Crs, crs))
                {
                    LastProjeTask = ProjeyeGonderAsync(new ProjeCrsAyarlaKomutu(crs));
                }
            };
        }
    }

    private static bool AyniCrs(Imar18.Domain.Geometry.ProjeCrs? a, Imar18.Domain.Geometry.ProjeCrs b) =>
        a is not null && a.Datum == b.Datum && a.OrtaMeridyenDerece == b.OrtaMeridyenDerece && a.Epok == b.Epok && a.OlcekPaydasi == b.OlcekPaydasi;

    /// <summary>Son CommandBus gonderimi (testler ve gorunum bekleyebilir).</summary>
    public Task LastProjeTask { get; private set; } = Task.CompletedTask;

    /// <summary>P7.3-C2: 1. adım Proje Künyesi &amp; Dönüşüm kartı (proje çalışma alanı yoksa null).</summary>
    public KunyeDonusumViewModel? Kunye { get; }

    /// <summary>Sihirbaz 5. adim: onay ve cikti (proje calisma alani yoksa null — P4 davranisi).</summary>
    public OnayCiktiViewModel? OnayCikti { get; }

    /// <summary>Sihirbaz 2–4. adimlar: tecviz, DOP &amp; Y1, hak eslestirme (proje calisma alani yoksa null).</summary>
    public HesapAdimlariViewModel? Hesap { get; }

    /// <summary>Son rol degisikligiyle baslatilan yeniden eslestirme (testler ve gorunum bekleyebilir).</summary>
    public Task LastReapplyTask { get; private set; } = Task.CompletedTask;

    /// <summary>Gorunumun dosya secici acmasi istendiginde (komut cubugu / Ctrl+O).</summary>
    public event EventHandler? OpenFileRequested;

    /// <summary>P7: görünüm metni sistem panosuna kopyalar (platform işi görünümdedir).</summary>
    public event EventHandler<string>? PanoyaKopyalaIstendi;

    public CadCanvasViewModel Canvas { get; } = new();

    public WizardViewModel Wizard { get; } = new();

    public CommandPaletteViewModel Palette { get; } = new();

    /// <summary>Hesabı veya topolojiyi gerçekten etkileyen içe aktarma uyarıları (sayıya girer).</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>P7: rutin içe aktarma bilgi notları (Z=0 indirgeme, 1 mm ızgara notu) — uyarı sayısına girmez.</summary>
    public ObservableCollection<string> BilgiNotlari { get; } = [];

    public ObservableCollection<ParcelRow> Parcels { get; } = [];

    /// <summary>P7: uyarı, hata, denetim ve özet metinlerini panoya kopyalar ("⧉" düğmesi ve sağ tık "Kopyala").</summary>
    [RelayCommand]
    private void Kopyala(string? metin)
    {
        if (!string.IsNullOrWhiteSpace(metin))
        {
            PanoyaKopyala(metin, "📋 Panoya kopyalandı: " + (metin.Length > 80 ? metin[..80] + "…" : metin));
        }
    }

    private void PanoyaKopyala(string metin, string durum)
    {
        PanoyaKopyalaIstendi?.Invoke(this, metin);
        StatusText = durum;
    }

    /// <summary>Akisi arka planda ice aktarir. Akis cagirana aittir; tamamlaninca kapatilmaz.</summary>
    public async Task ImportAsync(Stream source, string extension)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(extension);
        if (IsBusy)
        {
            return;
        }

        _importCts = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        StatusText = "İçe aktarılıyor…";
        // Gec gelen ilerleme bildirimleri (tamamlandiktan sonra) yok sayilir.
        var progress = new Progress<CadImportProgress>(p =>
        {
            if (IsBusy)
            {
                ProgressPercent = p.Percent;
            }
        });
        try
        {
            // P7.3-C3: .NCZ baytları PARSELASYON HARİTASI.NCZ şablonu olarak saklanacağı için önce belleğe alınır.
            // Reviewer M2: yalnız boyutu bilinen ve şablon sınırı içindeki akış belleğe alınır (sınırsız kopyalama yok).
            byte[]? ncz = null;
            var sablonSiniriAsildi = false;
            if (_proje is not null && NczMi(extension) && source.CanSeek)
            {
                if (source.Length - source.Position > NczSablonu.EnBuyukBoyut)
                {
                    sablonSiniriAsildi = true;
                }
                else
                {
                    using var tampon = new MemoryStream();
                    await source.CopyToAsync(tampon, _importCts.Token).ConfigureAwait(true);
                    ncz = tampon.ToArray();
                    source = new MemoryStream(ncz, writable: false);
                }
            }

            var result = await _importService.ImportAsync(source, extension, progress, _importCts.Token).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusText = "İçe aktarılamadı: " + result.Error.UserMessage;
                return; // başarısız içe aktarma şablonu değiştirmez
            }

            Wizard.LoadLayers(result.Value.Layers);
            _lastDocument = result.Value;
            var sablonNotu = await NczSablonunuGuncelleAsync(ncz, sablonSiniriAsildi).ConfigureAwait(true);
            await ApplyAsync(result.Value).ConfigureAwait(true);
            if (sablonNotu is not null)
            {
                Warnings.Add(sablonNotu);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "İçe aktarma iptal edildi.";
        }
        finally
        {
            ProgressPercent = 100;
            IsBusy = false;
            _importCts.Dispose();
            _importCts = null;
        }

        // SAHA-014 (reviewer M4): içe aktarma sürerken .DNS uygulandı/kaldırıldıysa ertelenen sahne yenilemesi şimdi yapılır.
        if (_sahneYenilemeBekliyor)
        {
            _sahneYenilemeBekliyor = false;
            LastReapplyTask = DonusumSahnesiniYenileAsync();
            await LastReapplyTask.ConfigureAwait(true);
        }
    }

    private bool _sahneYenilemeBekliyor;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelImport() => _importCts?.Cancel();

    [RelayCommand]
    private void TogglePalette() => Palette.Toggle();

    [RelayCommand]
    private void RequestOpenFile() => OpenFileRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void FitAll() => Canvas.FitAll();

    /// <summary>
    /// P7.3-A: pencere Esc kısayolu — komut çubuğu açıksa kapatır, değilse tuvalden rol atama modundan çıkar. Odak tuvaldeyse
    /// Esc önce tuvalin OnKeyDown'ında işlenir (rol modundan çıkar, Handled); odak başka denetimdeyse bu pencere KeyBinding'i
    /// devreye girer. Komut çubuğu açıkken odak arama kutusundadır, bu yüzden Esc yalnız çubuğu kapatır.
    /// </summary>
    [RelayCommand]
    private void Esc()
    {
        if (Palette.IsOpen)
        {
            Palette.Close();
        }
        else
        {
            Canvas.RolAtamaModundanCik();
        }
    }

    /// <summary>
    /// P7: Katman → Rol eşlemesini açıkça uygular (rol değişikliği zaten anında uygulanır; düğme yeniden hesaplamayı ve projeye
    /// yazımı garanti eder): tuval renkleri, Parseller tablosu ve proje geometrisi güncellenir.
    /// </summary>
    [RelayCommand]
    private async Task EslemeyiUygulaAsync()
    {
        if (_lastDocument is null)
        {
            StatusText = "Önce bir CAD dosyası açın (Ctrl+O); Katman → Rol eşlemesi içe aktarılan katmanlara uygulanır.";
            return;
        }

        LastReapplyTask = ReapplyAsync();
        await LastReapplyTask.ConfigureAwait(true);
        StatusText = "Katman → Rol eşlemesi uygulandı · " + StatusText;
    }

    private bool TemizlenebilirMi() => !IsBusy;

    /// <summary>
    /// P7 "Projeyi Temizle / Sıfırla": önce proje durumu CommandBus sıfırlama komutuyla boşaltılır (denetime yazılır, onay düşer);
    /// komut reddedilirse (ör. kayıt hatası) arayüz temizlenmez ve hata gösterilir (reviewer M3). Başarılıysa harita, tablolar,
    /// uyarılar ve sihirbaz girdileri temizlenir; tuval ızgaralı boş paftaya döner.
    /// </summary>
    [RelayCommand(CanExecute = nameof(TemizlenebilirMi))]
    private async Task ProjeyiTemizleAsync()
    {
        Palette.Close();
        if (_proje is not null)
        {
            var sonuc = await _proje.Yol.GonderAsync(new ProjeyiSifirlaKomutu(), _proje.KullaniciId).ConfigureAwait(true);
            if (sonuc.IsFailure)
            {
                StatusText = "Proje temizlenemedi: " + sonuc.Error.UserMessage;
                return;
            }
        }

        _lastDocument = null;
        Canvas.LoadScene(CanvasScene.Empty);
        Canvas.IzgaraGoster = true;
        Parcels.Clear();
        Warnings.Clear();
        BilgiNotlari.Clear();
        Wizard.Sifirla();
        Hesap?.Sifirla();
        Kunye?.Sifirla();
        if (OnayCikti is not null)
        {
            await OnayCikti.YenileAsync().ConfigureAwait(true);
        }

        if (Hesap is not null)
        {
            await Hesap.YenileAsync().ConfigureAwait(true);
        }

        StatusText = "🧹 Proje temizlendi — yeni CAD dosyası açmaya hazır (Ctrl+O).";
    }

    private async Task ReapplyAsync()
    {
        if (_lastDocument is { } d && !IsBusy)
        {
            await ApplyAsync(d, kameraKoru: true).ConfigureAwait(true);
        }
    }

    private int _uygulamaNesli;

    /// <summary>P7.3-A: tuvalden rol atama yalnız 1. adımda ve katmanlar yüklüyken açıktır.</summary>
    private void RolAtamaEtkinliginiYenile() => Canvas.RolAtamaEtkin = Wizard.IsStep1 && Wizard.LayerRoles.Count > 0;

    /// <summary>SAHA-014: projede uygulanmış <c>.DNS</c> ileri dönüşüm katsayıları (yoksa null; tuval Deprem Öncesi = CAD gösterilir).</summary>
    private DonusumKatsayilari? GuncelIleriDonusum => _proje?.Yol.Belge.IleriDonusum;

    /// <summary>
    /// SAHA-014: <c>.DNS</c> uygulanınca/kaldırılınca tuval ve Parseller tablosu Deprem Sonrası/Öncesi koordinatlarla yenilenir
    /// (eşleme kaynak CAD'den yeniden kurulur; proje geometrisi değişmediyse komut gönderilmez). İçe aktarma sürüyorsa yenileme
    /// ertelenir ve içe aktarma bitince yapılır (reviewer M4); ayrıca <see cref="ApplyAsync"/> güncel katsayıları sonuçta yeniden denetler.
    /// </summary>
    private async Task DonusumSahnesiniYenileAsync()
    {
        if (IsBusy)
        {
            _sahneYenilemeBekliyor = true;
            return;
        }

        if (_lastDocument is { } d)
        {
            await ApplyAsync(d, kameraKoru: true, yalnizDegistiyse: true).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Etiket eslestirmesi ve Gauss alanlari (O(poligon·yazi·kose)) arka planda hesaplanir; yalnizca sonuc arayuze
    /// aktarilir (50 ms kurali). Eslestirme rol bazlidir: once imar, sonra kadastro (ust uste binen katmanlar
    /// birbirinin yazisini "paylasilan" saymaz).
    /// </summary>
    private async Task ApplyAsync(CadLayerDocument document, bool kameraKoru = false, bool yalnizDegistiyse = false)
    {
        // P7.3-A (reviewer H1): art arda rol atamasında arka plan hesapları ters sırada bitebilir; yalnız en son başlatılan
        // eşleme sahneye ve projeye (ParselGeometrileriAyarlaKomutu) yazılır, eskimiş sonuç sessizce atılır.
        var nesil = ++_uygulamaNesli;
        var roles = Wizard.RoleMap;
        var projeli = _proje is not null;
        var katsayilar = GuncelIleriDonusum;
        var (scene, notlar, format, layerCount, matched, geometri) = await Task.Run(() =>
        {
            var c = Compute(document, roles);
            // SAHA-010 (architect M6): kabuk/yinelenen denetimi O(n²) olduğundan eşleme de arka planda yapılır (50 ms kuralı).
            // SAHA-014: proje geometrisi kaynak CAD'den (Deprem Öncesi) kurulur — komut ileri dönüştürür; tuval sahnesi Deprem Sonrası.
            return (SahneKur(c.Scene, c.Matched, roles, katsayilar), c.Notlar, c.Format, c.LayerCount, c.Matched,
                projeli ? ProjeGeometriEslemesi.Olustur(c.Matched, roles) : null);
        }).ConfigureAwait(true);

        // SAHA-014 (architect O3): hesap sürerken .DNS uygulandı/kaldırıldıysa sahne güncel katsayılarla yeniden kurulur.
        (scene, katsayilar) = await GuncelSahneAsync(nesil, scene, matched, roles, katsayilar).ConfigureAwait(true);
        if (nesil != _uygulamaNesli)
        {
            return;
        }

        Warnings.Clear();
        BilgiNotlari.Clear();
        foreach (var n in notlar)
        {
            (RutinIceAktarmaKodlari.Contains(n.Code) || n.Severity == ErrorSeverity.Info ? BilgiNotlari : Warnings).Add(n.UserMessage);
        }

        SahneyiYukle(scene, kameraKoru);

        if (_proje is not null && geometri is not null)
        {
            if (geometri.AtlananSayisi > 0)
            {
                Warnings.Add($"{geometri.AtlananSayisi} parsel poligonu geçersiz/açık olduğu için projeye alınmadı.");
            }

            foreach (var u in geometri.Uyarilar)
            {
                Warnings.Add(u); // SAHA-010: R2 çift sayım önlemleri Uyarı & Denetim sekmesinde
            }

            StatusText = DurumMetni(format, scene, layerCount)
                + (katsayilar is null ? string.Empty : " .DNS ileri dönüşümü etkin: CAD koordinatları Deprem Öncesi kabul edildi, tuval ve hesaplar Deprem Sonrası.");
            var belge = _proje.Yol.Belge;
            if (yalnizDegistiyse && AyniGeometri(geometri.Parseller, belge.DepremOncesiParseller ?? belge.Parseller))
            {
                return;
            }

            // SAHA-015 (architect K1): onaylı kroki parselasyonu sessizce ezilmez. .DNS uygula/kaldır yolunda proje geometrisi CAD'den geri
            // yazılmaz (IleriDonusumUygulaKomutu kroki parsellerini Deprem Öncesi orijinallerinden zaten dönüştürdü); 1. adım katman
            // eşlemesi bilinçli yeniden içe aktarmadır — uyarılır ve denetime ezilen parsel sayısı yazılır.
            var krokiParseli = Hesap?.UygulananKrokiParselSayisi ?? 0;
            if (krokiParseli > 0 && yalnizDegistiyse)
            {
                Warnings.Add("Uygulanmış Yaklaşık Yer Krokisi parselasyonu korundu ve .DNS dönüşümüyle yeniden konumlandı; alanlar dönüşüm ölçeğiyle değişmiş olabilir — krokiyi yeniden yükleyip onaylayın.");
                return;
            }

            if (krokiParseli > 0)
            {
                Warnings.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Uygulanmış Yaklaşık Yer Krokisi parselasyonu ({krokiParseli} parsel) 1. adım katman eşlemesiyle değiştirildi; Kroki satırları eski parsel kodlarına bakıyor — krokiyi yeniden yükleyip onaylayın."));
            }

            LastProjeTask = ProjeyeGonderAsync(new ParselGeometrileriAyarlaKomutu(geometri.Parseller)
            {
                CiftSayimHaricSayisi = geometri.CiftSayimHaricSayisi,
                KrokiParselasyonuEzildi = krokiParseli,
            });
            await LastProjeTask.ConfigureAwait(true);

            // SAHA-014 (reviewer M4): proje komutu beklenirken .DNS değiştiyse tuval yine güncel katsayılarla yenilenir.
            if (nesil == _uygulamaNesli && !Equals(katsayilar, GuncelIleriDonusum))
            {
                (scene, _) = await GuncelSahneAsync(nesil, scene, matched, roles, katsayilar).ConfigureAwait(true);
                if (nesil == _uygulamaNesli)
                {
                    SahneyiYukle(scene, kameraKoru: true);
                }
            }
        }
        else
        {
            StatusText = DurumMetni(format, scene, layerCount);
        }
    }

    /// <summary>SAHA-014: katsayılar değiştikçe (ve nesil güncelken) sahneyi güncel <c>.DNS</c> katsayılarıyla yeniden kurar.</summary>
    private async Task<(CanvasScene Sahne, DonusumKatsayilari? Katsayilar)> GuncelSahneAsync(
        int nesil, CanvasScene scene, CadLayerDocument matched, CadLayerRoleMap roles, DonusumKatsayilari? katsayilar)
    {
        while (nesil == _uygulamaNesli && !Equals(katsayilar, GuncelIleriDonusum))
        {
            katsayilar = GuncelIleriDonusum;
            var k = katsayilar;
            scene = await Task.Run(() => SahneKur(null, matched, roles, k)).ConfigureAwait(true);
        }

        return (scene, katsayilar);
    }

    /// <summary>Tuvale sahneyi ve Parseller tablosuna kapalı poligonları yükler.</summary>
    private void SahneyiYukle(CanvasScene scene, bool kameraKoru)
    {
        Canvas.LoadScene(scene, kameraKoru);
        Parcels.Clear();
        foreach (var p in scene.Polylines.Where(p => p.IsClosed))
        {
            Parcels.Add(new ParcelRow(p.LayerName, TurkceAdlar.Ad(p.Role), p.Label, p.AreaM2));
        }
    }

    /// <summary>SAHA-014: tuval sahnesi — dönüşüm yoksa hazır sahne, varsa eşlenmiş CAD belgesinin ileri dönüşümünden (decimal) kurulur.</summary>
    private static CanvasScene SahneKur(CanvasScene? hazir, CadLayerDocument matched, CadLayerRoleMap roles, DonusumKatsayilari? katsayilar) =>
        katsayilar is null && hazir is not null ? hazir : CanvasScene.From(CadBelgesiDonusumu.Uygula(matched, katsayilar), roles);

    private static bool AyniGeometri(IReadOnlyList<ParselGeometrisi> a, IReadOnlyList<ParselGeometrisi> b) =>
        a.Count == b.Count && a.Zip(b).All(x => x.First.ParselId == x.Second.ParselId && x.First.Rol == x.Second.Rol && x.First.Koseler.SequenceEqual(x.Second.Koseler));

    private string DurumMetni(CadFormat format, CanvasScene scene, int layerCount) => string.Create(CultureInfo.InvariantCulture,
        $"{TurkceAdlar.Ad(format)}: {scene.Polylines.Count} poligon, {layerCount} katman, {Warnings.Count} uyarı")
        + (BilgiNotlari.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $", {BilgiNotlari.Count} bilgi notu.") : ".");

    private static bool NczMi(string uzanti) =>
        string.Equals(uzanti.Trim().TrimStart('.'), "ncz", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// P7.3-C3: başarılı 1. adım içe aktarımından sonra PARSELASYON HARİTASI.NCZ şablonunu günceller: geçerli .NCZ ise baytlar
    /// <see cref="NczSablonuAyarlaKomutu"/> ile saklanır (değişmediyse komut gönderilmez); .DXF/.DWG veya saklanamayan .NCZ'de
    /// eski şablon temizlenir (iskelet moduna dönülür). Yaklaşık Yer Krokisi içe aktarımı bu yoldan geçmez. Uyarı metni döner.
    /// </summary>
    private async Task<string?> NczSablonunuGuncelleAsync(byte[]? ncz, bool sinirAsildi)
    {
        if (_proje is null)
        {
            return null;
        }

        var mevcut = _proje.Yol.Belge.NczSablonu;
        string? not = null;
        if (ncz is not null)
        {
            var sablon = NczSablonu.Olustur(ncz);
            if (sablon.IsSuccess)
            {
                if (sablon.Value.Equals(mevcut))
                {
                    return null;
                }

                var ayarla = ProjeyeGonderAsync(new NczSablonuAyarlaKomutu(ncz));
                LastProjeTask = ayarla;
                if (await ayarla.ConfigureAwait(true))
                {
                    return null;
                }

                // Reviewer H2: yeni şablon yazılamadıysa eski kaynağın şablonu yeni geometriyle eşleşmesin diye temizlenir.
                not = "Kaynak NCZ şablon olarak kaydedilemedi; PARSELASYON HARİTASI.NCZ şablonsuz (iskelet) üretilecek.";
                mevcut = _proje.Yol.Belge.NczSablonu;
            }
        }
        else if (sinirAsildi)
        {
            not = "Kaynak NCZ şablon olarak saklanamadı (boyut sınırı aşıldı); PARSELASYON HARİTASI.NCZ şablonsuz (iskelet) üretilecek.";
        }

        if (mevcut is not null)
        {
            LastProjeTask = ProjeyeGonderAsync(new NczSablonuAyarlaKomutu(null));
            await LastProjeTask.ConfigureAwait(true);
        }

        return not;
    }

    /// <summary>Komutu CommandBus'a gonderir; reddedilirse kullaniciya Turkce mesaj gosterilir. Onay ekrani yenilenir.</summary>
    private async Task<bool> ProjeyeGonderAsync(IProjeKomutu komut)
    {
        var sonuc = await _proje!.Yol.GonderAsync(komut, _proje.KullaniciId).ConfigureAwait(true);
        if (sonuc.IsFailure)
        {
            StatusText = "Projeye yazılamadı: " + sonuc.Error.UserMessage;
        }

        if (OnayCikti is not null)
        {
            await OnayCikti.YenileAsync().ConfigureAwait(true);
        }

        if (Hesap is not null)
        {
            await Hesap.YenileAsync().ConfigureAwait(true);
        }

        return sonuc.IsSuccess;
    }

    private static (CanvasScene Scene, List<AppError> Notlar, CadFormat Format, int LayerCount, CadLayerDocument Matched) Compute(CadLayerDocument document, CadLayerRoleMap roles)
    {
        var notlar = document.Warnings.ToList();
        if (document.Format is CadFormat.Dxf or CadFormat.Dwg)
        {
            // SAHA-010: İmar Adası yalnız İmar Parseli katmanı yoksa R2'ye girer; o zaman etiketlenir (aksi hâlde ada içindeki parsel
            // yazıları "belirsiz etiket" uyarısı üretirdi).
            // Reviewer L7: öncelik ProjeGeometriEslemesi ile aynı ölçüte (kapalı İmar Parseli poligonu var mı) dayanır.
            var parselKatmaniVar = document.Polylines.Any(p => p.IsClosed && roles.Resolve(p.LayerName) == CadLayerRole.ZoningParcel);
            CadLayerRole[] etiketRolleri = parselKatmaniVar
                ? [CadLayerRole.ZoningParcel, CadLayerRole.CadastralParcel]
                : [CadLayerRole.ZoningParcel, CadLayerRole.ZoningBlock, CadLayerRole.CadastralParcel];
            foreach (var role in etiketRolleri)
            {
                var match = CadLabelMatcher.Match(document, roles, [role]);
                document = document with { Polylines = match.Polylines };
                notlar.AddRange(match.Warnings);
            }
        }

        return (CanvasScene.From(document, roles), notlar, document.Format, document.Layers.Count, document);
    }
}
