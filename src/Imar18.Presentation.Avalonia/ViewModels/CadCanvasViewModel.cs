using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cad;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.Canvas;
using Imar18.Presentation.Avalonia.Gorunum;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>
/// Sag tik menusu ogesi. <paramref name="Bagli"/> false ise islem sonraki fazda baglanacaktir (gerekcesiyle pasif) ve sag
/// paneldeki "Parsel Islemleri" kartinda gosterilmez.
/// </summary>
public sealed record CanvasMenuItem(
    string Id, string Title, string? DisabledReason, IRelayCommand Command, bool Bagli = true, IReadOnlyList<CanvasMenuItem>? AltOgeler = null);

/// <summary>P7.3-A: tuvalden istenen katman → rol ataması (işlemi sihirbaz yapar; tuval yalnız ister).</summary>
public sealed record KatmanRolAtamasi(string KatmanAdi, CadLayerRole Rol);

/// <summary>P7.3-A: "Rol ata" şeridindeki hızlı rol çipi (seçiliyken tuvalde tıklanan katman bu role atanır).</summary>
public sealed partial class RolCipi : ObservableObject
{
    public RolCipi(CadLayerRole rol, IRelayCommand secCommand)
    {
        Rol = rol;
        SecCommand = secCommand;
    }

    public CadLayerRole Rol { get; }

    /// <summary>Türkçe rol adı.</summary>
    public string Ad => TurkceAdlar.Ad(Rol);

    public IRelayCommand SecCommand { get; }

    [ObservableProperty]
    private bool _secili;
}

/// <summary>Canli kesim tutamaci (P4.3). Aci spec §4.2 araligi 75°–105° icinde, konum cephe boyunca 0..1 oranidir.</summary>
public sealed partial class CutHandleViewModel : ObservableObject
{
    /// <summary>En kucuk kesim acisi (spec §4.2).</summary>
    public const double MinAngleDeg = 75d;

    /// <summary>En buyuk kesim acisi (spec §4.2).</summary>
    public const double MaxAngleDeg = 105d;

    private double _angleDeg = 90d;
    private double _positionRatio = 0.5d;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _isAtLimit;

    public double AngleDeg
    {
        get => _angleDeg;
        set
        {
            var v = double.IsFinite(value) ? Math.Clamp(value, MinAngleDeg, MaxAngleDeg) : _angleDeg;
            IsAtLimit = v != value;
            SetProperty(ref _angleDeg, v);
        }
    }

    public double PositionRatio
    {
        get => _positionRatio;
        set
        {
            var v = double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : _positionRatio;
            IsAtLimit = v != value;
            SetProperty(ref _positionRatio, v);
        }
    }

    /// <summary>Tutamaci cephe boyunca (oran) surukler.</summary>
    public void DragBy(double deltaRatio) => PositionRatio = _positionRatio + deltaRatio;

    /// <summary>Tutamaci dondurur (derece).</summary>
    public void RotateBy(double deltaDeg) => AngleDeg = _angleDeg + deltaDeg;

    public void Reset()
    {
        AngleDeg = 90d;
        PositionRatio = 0.5d;
        IsAtLimit = false;
    }
}

/// <summary>
/// SkiaSharp 2B CAD tuvali ViewModel'i (P4.3): kamera, sahne, Kadastro↔İmar kaydiricisi, secim, imlec koordinati,
/// kesim tutamaci ve sag tik menusu. <see cref="RenderVersion"/> her gorunum degisikliginde artar; kontrol yalnizca bu
/// degistiginde yeniden cizer (60 FPS hedefi: sahne yollari onbellekte, cizim matrisle yapilir).
/// </summary>
public sealed partial class CadCanvasViewModel : ObservableObject
{
    private double _swipePercent = 100d;
    private double _viewWidth = 800d;
    private double _viewHeight = 600d;

    [ObservableProperty]
    private CanvasScene _scene = CanvasScene.Empty;

    [ObservableProperty]
    private ScenePolyline? _selected;

    [ObservableProperty]
    private string _cursorText = string.Empty;

    [ObservableProperty]
    private string _contextMenuTitle = "PARSEL İŞLEMLERİ";

    [ObservableProperty]
    private int _renderVersion;

    /// <summary>Son imleç konumundaki OSNAP yakalaması (yoksa null).</summary>
    [ObservableProperty]
    private OsnapSonucu? _yakalama;

    /// <summary>P7: OSNAP açıkken tıklanarak seçilen (ve panoya kopyalanan) yakalama noktası; tuvalde işaretli kalır.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecilenNoktaMetni))]
    private OsnapSonucu? _secilenNokta;

    /// <summary>OSNAP çubuğunda gösterilen seçili nokta metni (yoksa boş).</summary>
    public string SecilenNoktaMetni => SecilenNokta is { } n ? "📍 " + NoktaMetni(n) : string.Empty;

    [ObservableProperty]
    private bool _osnapUc = true;

    [ObservableProperty]
    private bool _osnapOrta = true;

    [ObservableProperty]
    private bool _osnapDik;

    [ObservableProperty]
    private bool _osnapKesisim;

    [ObservableProperty]
    private bool _izgaraGoster = true;

    [ObservableProperty]
    private bool _etiketGoster = true;

    /// <summary>Açık pafta (kağıt) teması: tuval açık zeminde çizilir.</summary>
    [ObservableProperty]
    private bool _acikPafta;

    /// <summary>HUD: seçili parsel özeti (Türkçe).</summary>
    [ObservableProperty]
    private string _seciliOzet = SecimYok;

    /// <summary>Yakalama toleransı (ekran pikseli).</summary>
    public const double YakalamaToleransiPx = 10d;

    /// <summary>P7.3-A: tuvalden katman seçme toleransı (ekran pikseli; açık çizgiler için).</summary>
    public const double KatmanToleransiPx = 6d;

    private const string SecimYok = "Seçim yok — parsele tıklayın";

    private const string KatmanRoluEtkinDegil = "Katman rolü yalnız 1. adımda (Girdi & Sınır), CAD dosyası açıkken atanır";

    private const string KatmanRoluIsabetYok = "Sağ tıklanan noktada katman yok";

    // ---- P7.3-A: tuvalden katman → rol eşleme ----

    /// <summary>Tuvalden rol atama şu an mümkün mü (1. adım ve yüklü katman; sihirbaz belirler).</summary>
    [ObservableProperty]
    private bool _rolAtamaEtkin;

    /// <summary>Seçili hızlı rol çipi: doluyken sol tık, tıklanan katmanı bu role atar (null: normal seçim).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RolAtamaAktif), nameof(RolAtamaMetni))]
    private CadLayerRole? _rolAtamaModu;

    /// <summary>Listeden (🎯) veya son atamadan işaretlenen katman (imleç başka katmanda değilse vurgulanır).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VurguluKatman))]
    private string? _isaretliKatman;

    /// <summary>Rol atama modunda imlecin üzerinde bulunduğu katman (yalnız modda dolar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VurguluKatman), nameof(RolAtamaMetni))]
    private string? _imlecKatmani;

    private string? _sagTikKatmani;

    /// <summary>Tuvalde kalın/parlak çizilen katman: imleç katmanı, yoksa işaretli katman.</summary>
    public string? VurguluKatman => ImlecKatmani ?? IsaretliKatman;

    /// <summary>HUD: rol atama modu açık mı.</summary>
    public bool RolAtamaAktif => RolAtamaModu is not null;

    /// <summary>HUD metni: aktif rol, imleçteki katman ve mevcut rolü.</summary>
    public string RolAtamaMetni
    {
        get
        {
            if (RolAtamaModu is not { } rol)
            {
                return string.Empty;
            }

            var hedef = ImlecKatmani is { } k
                ? $"Katman: {k} ({TurkceAdlar.Ad(KatmanRolu(k))}) → {TurkceAdlar.Ad(rol)}"
                : "katmana tıklayın";
            return $"🎯 Rol ata: {TurkceAdlar.Ad(rol)} · {hedef} · Esc: çık";
        }
    }

    /// <summary>"Rol ata" şeridindeki hızlı rol çipleri (tüm roller).</summary>
    public ObservableCollection<RolCipi> RolCipleri { get; }

    /// <summary>Tuvalden katman → rol ataması istendi (sihirbaz uygular; mevcut CommandBus zinciri çalışır).</summary>
    public event EventHandler<KatmanRolAtamasi>? KatmanRolAtamaIstendi;

    public CadCanvasViewModel()
    {
        const string sonraki = "Sonraki fazda (P5/P6) bağlanacak";
        IRelayCommand Pasif() => new RelayCommand(() => { }, () => false);
        RolCipleri = new(LayerRoleRow.TumRoller.Select(r => new RolCipi(r, new RelayCommand(() => CipSec(r), () => RolAtamaEtkin))));
        ContextMenuItems =
        [
            KatmanRoluMenusu(),
            new CanvasMenuItem("chamfer45", "📐 45° Köşe Pahı Uygula (5×5 m)", sonraki, Pasif(), false),
            new CanvasMenuItem("resolveShared", "🔗 Müşterek Çöz (Md. 17/1-d)", sonraki, Pasif(), false),
            new CanvasMenuItem("lockFrontage", "↔ Cepheyi Sabitle (Alan Kilitli)", sonraki, Pasif(), false),
            new CanvasMenuItem("balanceY1", "⚖️ Tecviz Farkını Y1 Yolunda Dengele", sonraki, Pasif(), false),
            new CanvasMenuItem("fenPreview", "📗 Fen Klasörü (He/M2/Dm2) Önizle", sonraki, Pasif(), false),
            new CanvasMenuItem("cutHandle", "✂️ Canlı Kesim Tutamacını Göster", null, new RelayCommand(ShowCutHandle, () => Selected is not null)),
            new CanvasMenuItem("zoomSelection", "🔍 Seçili Parsele Yakınlaş", null, new RelayCommand(ZoomToSelection, () => Selected is not null)),
            new CanvasMenuItem("clearSelection", "✖ Seçimi Temizle", null, new RelayCommand(ClearSelection, () => Selected is not null)),
            new CanvasMenuItem("fitAll", "🗺️ Tümünü Sığdır", null, new RelayCommand(FitAll)),
        ];
        ParselIslemleriniYenile();
    }

    public CadCamera Camera { get; } = new();

    public CutHandleViewModel CutHandle { get; } = new();

    public ObservableCollection<CanvasMenuItem> ContextMenuItems { get; }

    /// <summary>Sağ panel "Parsel İşlemleri" kartı: yalnızca gerçek komuta bağlı işlemler.</summary>
    public ObservableCollection<CanvasMenuItem> ParselIslemleri { get; } = [];

    /// <summary>Açık OSNAP kipleri.</summary>
    public OsnapKipi AcikKipler =>
        (OsnapUc ? OsnapKipi.Uc : OsnapKipi.Yok) | (OsnapOrta ? OsnapKipi.Orta : OsnapKipi.Yok)
        | (OsnapDik ? OsnapKipi.Dik : OsnapKipi.Yok) | (OsnapKesisim ? OsnapKipi.Kesisim : OsnapKipi.Yok);

    /// <summary>
    /// P5: "Fen Klasörü" bağlam menüsü öğesini gerçek komuta bağlar (onay yoksa komut pasiftir; gerekçe gösterilir).
    /// </summary>
    public void BaglaFenKlasoru(IRelayCommand komut)
    {
        ArgumentNullException.ThrowIfNull(komut);
        var i = ContextMenuItems.ToList().FindIndex(m => m.Id == "fenPreview");
        ContextMenuItems[i] = new CanvasMenuItem("fenPreview", "📦 Fen Klasörü (.zip) Üret", "Güncel revizyon için mühendis onayı gerekli", komut);
        ParselIslemleriniYenile();
    }

    private void ParselIslemleriniYenile()
    {
        ParselIslemleri.Clear();
        foreach (var m in ContextMenuItems.Where(m => m.Bagli && m.AltOgeler is null))
        {
            ParselIslemleri.Add(m);
        }
    }

    // =====================================================================
    // P7.3-A: tuvalden katman → rol eşleme
    // =====================================================================

    /// <summary>Sağ tık menüsü: "Bu katmanın rolü ▸" + her rol için alt öğe (mevcut rol ✓ ile işaretli).</summary>
    private CanvasMenuItem KatmanRoluMenusu()
    {
        var katman = _sagTikKatmani;
        var mevcut = katman is null ? (CadLayerRole?)null : KatmanRolu(katman);
        var alt = LayerRoleRow.TumRoller.Select(r => new CanvasMenuItem(
                "katmanRolu:" + r,
                (mevcut == r ? "✓ " : "   ") + TurkceAdlar.Ad(r),
                null,
                new RelayCommand(() => AtamaIste(katman!, r), () => RolAtamaEtkin && katman is not null)))
            .ToList()
            .AsReadOnly();
        var baslik = katman is null ? "🧩 Bu Katmanın Rolü" : $"🧩 Bu Katmanın Rolü ({katman})";
        var neden = !RolAtamaEtkin ? KatmanRoluEtkinDegil : katman is null ? KatmanRoluIsabetYok : null;
        return new CanvasMenuItem("katmanRolu", baslik, neden, new RelayCommand(() => { }, () => RolAtamaEtkin && katman is not null), true, alt);
    }

    private void KatmanRoluMenusunuYenile()
    {
        var i = ContextMenuItems.ToList().FindIndex(m => m.Id == "katmanRolu");
        ContextMenuItems[i] = KatmanRoluMenusu();
    }

    /// <summary>Katmanın sahnedeki (geçerli eşlemeye göre) rolü.</summary>
    private CadLayerRole KatmanRolu(string katman) =>
        Scene.Polylines.FirstOrDefault(p => string.Equals(p.LayerName, katman, StringComparison.Ordinal))?.Role ?? CadLayerRole.Unassigned;

    private ScenePolyline? KatmanIsabeti(double screenX, double screenY)
    {
        var tol = KatmanToleransiPx / Camera.Scale;
        return double.IsFinite(tol)
            ? Scene.KatmanIsabeti(Camera.ScreenToWorld(screenX, screenY), decimal.Round((decimal)tol, 3, MidpointRounding.AwayFromZero))
            : null;
    }

    private void CipSec(CadLayerRole rol) => RolAtamaModu = RolAtamaModu == rol ? null : rol;

    /// <summary>Rol atama modundan çıkar (Esc, çip tekrar tıklama, adım değişimi).</summary>
    public void RolAtamaModundanCik() => RolAtamaModu = null;

    partial void OnRolAtamaModuChanged(CadLayerRole? value)
    {
        if (value is not null && !RolAtamaEtkin)
        {
            RolAtamaModu = null; // etkin değilken açılamaz
            return;
        }

        foreach (var c in RolCipleri)
        {
            c.Secili = c.Rol == value;
        }

        if (value is null && ImlecKatmani is not null)
        {
            ImlecKatmani = null;
        }

        Invalidate();
    }

    partial void OnRolAtamaEtkinChanged(bool value)
    {
        if (!value)
        {
            RolAtamaModu = null;
        }

        foreach (var c in RolCipleri)
        {
            c.SecCommand.NotifyCanExecuteChanged();
        }

        KatmanRoluMenusunuYenile();
    }

    partial void OnIsaretliKatmanChanged(string? value) => Invalidate();

    partial void OnImlecKatmaniChanged(string? value) => Invalidate();

    private void AtamaIste(string katman, CadLayerRole rol)
    {
        if (!RolAtamaEtkin)
        {
            return;
        }

        // İşaretleme sihirbazdan gelir (RolAta başarılıysa VurguluKatmanAdi → IsaretliKatman); liste dışı katmanda vurgu kopmaz.
        KatmanRolAtamaIstendi?.Invoke(this, new KatmanRolAtamasi(katman, rol));
    }

    /// <summary>
    /// Sol tık (sürüklemesiz): rol atama modunda tıklanan katmanı aktif role atatır (parsel seçmez); aksi halde eski davranış —
    /// OSNAP noktası yakalandıysa seçilip panoya kopyalanır ve parsel seçilir.
    /// </summary>
    public void SolTikla(double screenX, double screenY)
    {
        PointerMoved(screenX, screenY);
        if (RolAtamaModu is { } rol)
        {
            if (KatmanIsabeti(screenX, screenY) is { } isabet)
            {
                AtamaIste(isabet.LayerName, rol);
            }

            return;
        }

        YakalamayiSec();
        SelectAt(screenX, screenY);
    }

    /// <summary>Sağ tık: parsel seçimi (eski davranış) + "Bu katmanın rolü ▸" menüsü tıklanan katmana göre kurulur.</summary>
    public void SagTikla(double screenX, double screenY)
    {
        SelectAt(screenX, screenY);
        _sagTikKatmani = KatmanIsabeti(screenX, screenY)?.LayerName;
        KatmanRoluMenusunuYenile();
    }

    partial void OnOsnapUcChanged(bool value) => OsnapDegisti();

    partial void OnOsnapOrtaChanged(bool value) => OsnapDegisti();

    partial void OnOsnapDikChanged(bool value) => OsnapDegisti();

    partial void OnOsnapKesisimChanged(bool value) => OsnapDegisti();

    partial void OnIzgaraGosterChanged(bool value) => Invalidate();

    partial void OnEtiketGosterChanged(bool value) => Invalidate();

    partial void OnAcikPaftaChanged(bool value) => Invalidate();

    private void OsnapDegisti()
    {
        OnPropertyChanged(nameof(AcikKipler));
        if (AcikKipler == OsnapKipi.Yok && Yakalama is not null)
        {
            Yakalama = null;
            Invalidate();
        }
    }

    /// <summary>0 = yalnizca eski kadastro, 100 = tam yeni imar (referans tasarim "swipe" kaydiricisi).</summary>
    public double SwipePercent
    {
        get => _swipePercent;
        set
        {
            var v = double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : _swipePercent;
            if (SetProperty(ref _swipePercent, v))
            {
                Invalidate();
            }
        }
    }

    /// <summary>Gorunum yuzdesi (100 = sigdirilmis olcek degil, mutlak 1 px/m'ye gore; Turkce yazim: %100).</summary>
    public string ZoomText => "%" + (Camera.Scale * 100d).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Gorunum merkezinde yakinlastirir.</summary>
    [RelayCommand]
    private void Yakinlas() => ZoomAt(_viewWidth / 2d, _viewHeight / 2d, 1.25);

    /// <summary>Gorunum merkezinde uzaklastirir.</summary>
    [RelayCommand]
    private void Uzaklas() => ZoomAt(_viewWidth / 2d, _viewHeight / 2d, 1 / 1.25);

    public void SetViewport(double width, double height)
    {
        if (width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height))
        {
            _viewWidth = width;
            _viewHeight = height;
        }
    }

    /// <summary>
    /// Sahneyi yükler. <paramref name="kameraKoru"/> true ise (aynı belgenin rol eşlemesi yeniden uygulandı, P7.3-A) görünüm
    /// ve işaretli katman korunur — art arda tuvalden rol atarken "Tümünü Sığdır" ile sıfırlanmaz.
    /// </summary>
    public void LoadScene(CanvasScene scene, bool kameraKoru = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Scene = scene;
        Selected = null;
        Yakalama = null;
        SecilenNokta = null;
        ImlecKatmani = null;
        CutHandle.IsVisible = false;
        if (kameraKoru && scene.Bounds is not null)
        {
            Invalidate();
            return;
        }

        IsaretliKatman = null;
        FitAll();
    }

    public void FitAll()
    {
        if (Scene.Bounds is { } b)
        {
            Camera.FitTo(b.Min, b.Max, _viewWidth, _viewHeight);
        }

        Invalidate();
    }

    public void Pan(double dx, double dy)
    {
        Camera.Pan(dx, dy);
        Invalidate();
    }

    public void ZoomAt(double screenX, double screenY, double factor)
    {
        Camera.ZoomAt(screenX, screenY, factor);
        Invalidate();
    }

    /// <summary>
    /// Imlec koordinatini (OSNAP acikken yakalanan noktayi) Jeodezik Y (Dogu) / X (Kuzey) olarak yazar. Yakalama yalniz
    /// gosterimdir; geometriye yazmaz.
    /// </summary>
    public void PointerMoved(double screenX, double screenY)
    {
        var w = Camera.ScreenToWorld(screenX, screenY);
        var onceki = Yakalama;
        var tol = YakalamaToleransiPx / Camera.Scale;
        Yakalama = AcikKipler == OsnapKipi.Yok || !double.IsFinite(tol)
            ? null
            : OsnapCozucu.Coz(Scene, w, decimal.Round((decimal)tol, 3, MidpointRounding.AwayFromZero), AcikKipler);
        // P7.3-A: rol atama modunda imlecin altındaki katman vurgulanır (açık çizgiler dahil).
        ImlecKatmani = RolAtamaModu is null ? null : KatmanIsabeti(screenX, screenY)?.LayerName;
        var p = Yakalama?.Nokta ?? w;
        var ek = Yakalama is { } y ? "   · OSNAP: " + KipAdi(y.Kip) : string.Empty;
        CursorText = string.Create(CultureInfo.InvariantCulture, $"Doğu Y: {p.Y:0.000}   Kuzey X: {p.X:0.000}{ek}");
        if (onceki != Yakalama)
        {
            Invalidate();
        }
    }

    /// <summary>Imlec tuvalden cikti: yakalama isareti silinir.</summary>
    public void PointerLeft()
    {
        ImlecKatmani = null;
        if (Yakalama is not null)
        {
            Yakalama = null;
            Invalidate();
        }
    }

    /// <summary>
    /// P7: OSNAP noktası seçildiğinde (tıklama) — metin panoya kopyalanmak üzere görünüme iletilir (platform işi görünümdedir).
    /// </summary>
    public event EventHandler<string>? NoktaSecildi;

    /// <summary>
    /// İmleç bir OSNAP noktasını yakalamışsa onu seçer, tuvalde işaretler ve koordinat metnini <see cref="NoktaSecildi"/> ile
    /// yayınlar (panoya kopyalama). Yakalama yoksa false (tıklama parsel seçimine kalır). Geometriye/projeye yazmaz.
    /// </summary>
    public bool YakalamayiSec()
    {
        if (Yakalama is not { } y)
        {
            return false;
        }

        SecilenNokta = y;
        Invalidate();
        NoktaSecildi?.Invoke(this, NoktaMetni(y));
        return true;
    }

    /// <summary>Panoya kopyalanan nokta metni: "Y=500010.000 X=4000010.000 (Uç)" (kültürden bağımsız, 1 mm).</summary>
    public static string NoktaMetni(OsnapSonucu nokta)
    {
        ArgumentNullException.ThrowIfNull(nokta);
        return string.Create(CultureInfo.InvariantCulture, $"Y={nokta.Nokta.Y:0.000} X={nokta.Nokta.X:0.000} ({KipAdi(nokta.Kip)})");
    }

    /// <summary>Tuvalde yakalama sembolünün yanındaki etiket: "Uç · Y 500010.000 · X 4000010.000".</summary>
    public static string OsnapEtiketi(OsnapSonucu nokta)
    {
        ArgumentNullException.ThrowIfNull(nokta);
        return string.Create(CultureInfo.InvariantCulture, $"{KipAdi(nokta.Kip)} · Y {nokta.Nokta.Y:0.000} · X {nokta.Nokta.X:0.000}");
    }

    internal static string KipAdi(OsnapKipi kip) => kip switch
    {
        OsnapKipi.Uc => "Uç",
        OsnapKipi.Orta => "Orta",
        OsnapKipi.Dik => "Dik 90°",
        OsnapKipi.Kesisim => "Kesişim",
        _ => "—",
    };

    public void SelectAt(double screenX, double screenY)
    {
        Selected = Scene.HitTest(Camera.ScreenToWorld(screenX, screenY));
        Invalidate();
    }

    partial void OnSelectedChanged(ScenePolyline? value)
    {
        ContextMenuTitle = value is null
            ? "PARSEL İŞLEMLERİ"
            : $"PARSEL İŞLEMLERİ ({(value.Label.Length > 0 ? value.Label : value.LayerName)})";
        SeciliOzet = value is null
            ? SecimYok
            : $"🎯 {(value.Label.Length > 0 ? value.Label : value.LayerName)} · {TurkceAdlar.Ad(value.Role)}"
              + (value.AreaM2 is { } a ? " · " + a.ToString("0.00", CultureInfo.InvariantCulture) + " m²" : string.Empty);
        foreach (var item in ContextMenuItems)
        {
            item.Command.NotifyCanExecuteChanged();
        }
    }

    private void ShowCutHandle()
    {
        CutHandle.Reset();
        CutHandle.IsVisible = true;
        Invalidate();
    }

    private void ClearSelection()
    {
        Selected = null;
        CutHandle.IsVisible = false;
        Invalidate();
    }

    private void ZoomToSelection()
    {
        if (Selected is null || Selected.Vertices.Count == 0)
        {
            return;
        }

        var min = new GeodeticPoint(X: Selected.Vertices.Min(v => v.X), Y: Selected.Vertices.Min(v => v.Y));
        var max = new GeodeticPoint(X: Selected.Vertices.Max(v => v.X), Y: Selected.Vertices.Max(v => v.Y));
        Camera.FitTo(min, max, _viewWidth, _viewHeight, 48);
        Invalidate();
    }

    private void Invalidate()
    {
        RenderVersion++;
        OnPropertyChanged(nameof(ZoomText));
    }
}
