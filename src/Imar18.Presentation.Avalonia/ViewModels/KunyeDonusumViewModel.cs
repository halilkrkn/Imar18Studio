using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cikti;
using Imar18.Application.Girdi;
using Imar18.Application.KoordinatDonusumu;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Revizyon;
using Imar18.Presentation.Avalonia.Gorunum;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>
/// P7.3-C2: 1. adım "Proje Künyesi &amp; Dönüşüm" kartı. Künye (İl, İlçe, Köy/Mahalle, Belediye/İdare, Pafta No(lar),
/// Kadastro ID, Datum/Projeksiyon) ve ortak noktalar (<c>NoktaNo;Y1;X1;Y2;X2</c>, model Helmert2B / Afin2B, MK) CommandBus
/// üzerinden projeye yazılır (RevisionId'ye dahil; değişince onay düşer). webtapu yüklendiyse İl/İlçe/Mahalle boş alanlara
/// otomatik önerilir. Proje noktaları deprem sonrası kabul edilir; deprem öncesi koordinatlar ters dönüşümle hesaplanır.
/// P7 UX: ortak noktalar varsayılan olarak kutucuklu tabloda (<see cref="OrtakNoktaSatirlari"/>) düzenlenir; tablo
/// <see cref="OrtakNoktaMetni"/> ile iki yönlü senkrondur (metin "Metin / Toplu Yapıştır" alanındadır) ve hücre düzenlendikçe
/// parametre önizlemesi canlı güncellenir. "📄 WebTapu PDF'ten Künye Oku" seçilen PDF'ten yalnız İl / İlçe / Mahalle'yi
/// künye kutularına yazar (kişi adları künyeye yazılmaz — KVKK).
/// </summary>
public sealed partial class KunyeDonusumViewModel : ObservableObject
{
    private readonly ProjeCalismaAlani _alan;

    [ObservableProperty]
    private string _il = string.Empty;

    [ObservableProperty]
    private string _ilce = string.Empty;

    [ObservableProperty]
    private string _koyMahalle = string.Empty;

    [ObservableProperty]
    private string _belediye = string.Empty;

    [ObservableProperty]
    private string _paftaNolari = string.Empty;

    [ObservableProperty]
    private string _kadastroId = string.Empty;

    [ObservableProperty]
    private string _datumProjeksiyon = string.Empty;

    [ObservableProperty]
    private string _ortakNoktaMetni = string.Empty;

    [ObservableProperty]
    private DonusumModeli _model = DonusumModeli.Helmert2B;

    [ObservableProperty]
    private string _mkMetni = DonusumGirdisi.VarsayilanMk.ToString("0.00", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _kunyeDurumu = string.Empty;

    [ObservableProperty]
    private string _donusumDurumu = string.Empty;

    [ObservableProperty]
    private string _donusumHatasi = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KunyeyiKaydetCommand), nameof(DonusumuUygulaCommand), nameof(DonusumuOnizleCommand), nameof(WebtapuKunyeIsteCommand))]
    private bool _isBusy;

    private readonly TabloMetinSenkronu<OrtakNoktaSatirVm> _ortakSenkron;
    private bool _yenileniyor;

    public KunyeDonusumViewModel(ProjeCalismaAlani alan)
    {
        ArgumentNullException.ThrowIfNull(alan);
        _alan = alan;
        _ortakSenkron = new TabloMetinSenkronu<OrtakNoktaSatirVm>(OrtakNoktaSatirlari, OrtakNoktaSatirVm.Metinden, m => OrtakNoktaMetni = m, CanliOnizle);
        Yenile();
    }

    /// <summary>Künye veya dönüşüm projeye yazıldı (onay ekranı yenilenir).</summary>
    public event EventHandler? Uygulandi;

    /// <summary>Görünüm tek webtapu PDF'i için dosya seçicisini açar ve <see cref="WebtapudanKunyeOkuAsync"/> çağırır.</summary>
    public event EventHandler? WebtapuKunyeDosyasiIstendi;

    /// <summary>P7 UX: ortak nokta kutucuklu tablosu (<see cref="OrtakNoktaMetni"/> ile iki yönlü senkron).</summary>
    public ObservableCollection<OrtakNoktaSatirVm> OrtakNoktaSatirlari { get; } = [];

    /// <summary>webtapu PDF okuyucusu bağlı mı (değilse "WebTapu PDF'ten Künye Oku" pasif).</summary>
    public bool WebtapuOkuyucuVar => _alan.TapuOkuyucu is not null;

    partial void OnOrtakNoktaMetniChanged(string value) => _ortakSenkron?.MetindenYukle(value);

    public IReadOnlyList<DonusumModeli> Modeller { get; } = Enum.GetValues<DonusumModeli>();

    /// <summary>Önizleme: parametre özeti satırları (A, B, C, D, CY, CX, ölçek, dönüklük, ortalama hata).</summary>
    public ObservableCollection<string> ParametreSatirlari { get; } = [];

    /// <summary>Dönüşüm servisi bağlı mı (yoksa önizleme pasif; kayıt yine yapılabilir).</summary>
    public bool OnizlemeVar => _alan.Donusum is not null;

    /// <summary>webtapu'dan gelen İl/İlçe/Mahalle önerisi varsa true.</summary>
    public bool WebtapuOnerisiVar => WebtapuOnerisi() is not null;

    public static string ModelAdi(DonusumModeli m) => m == DonusumModeli.Helmert2B ? "Helmert (2B, 4 parametre)" : "Afin (2B, 6 parametre)";

    /// <summary>Projedeki künye ve dönüşüm girdisini alanlara yükler; boş İl/İlçe/Mahalle'ye webtapu önerisini koyar.</summary>
    public void Yenile()
    {
        var b = _alan.Yol.Belge;
        var k = b.Kunye ?? ProjeKunyesi.Bos;
        Il = k.Il;
        Ilce = k.Ilce;
        KoyMahalle = k.KoyMahalle;
        Belediye = k.Belediye;
        PaftaNolari = k.PaftaNolari;
        KadastroId = k.KadastroId;
        DatumProjeksiyon = k.DatumProjeksiyon.Length > 0 ? k.DatumProjeksiyon : CrsOnerisi(b.Crs);
        if (b.Kunye is null)
        {
            WebtapuOnerisiniUygula(yalnizBos: true);
        }

        _yenileniyor = true;
        try
        {
            if (b.Donusum is { } d)
            {
                OrtakNoktaMetni = DonusumMetni.Yaz(d.OrtakNoktalar);
                Model = d.Model;
                MkMetni = d.Mk.ToString("0.00###", CultureInfo.InvariantCulture);
            }
            else
            {
                OrtakNoktaMetni = string.Empty;
                Model = DonusumModeli.Helmert2B;
                MkMetni = DonusumGirdisi.VarsayilanMk.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _yenileniyor = false;
        }

        ParametreSatirlari.Clear();
        DonusumHatasi = string.Empty;
        DonusumDurumu = b.Donusum is null ? "Dönüşüm girdisi yok (isteğe bağlı)." : $"Kayıtlı: {b.Donusum.OrtakNoktalar.Count} ortak nokta · {ModelAdi(b.Donusum.Model)}";
        OnPropertyChanged(nameof(WebtapuOnerisiVar));
    }

    /// <summary>
    /// Yalnız webtapu önerisini boş İl/İlçe/Mahalle alanlarına uygular (diğer düzenlemeleri ezmez); künye kaydedilmişse dokunmaz.
    /// </summary>
    public void OneriyiYenile()
    {
        if (_alan.Yol.Belge.Kunye is null)
        {
            WebtapuOnerisiniUygula(yalnizBos: true);
        }

        OnPropertyChanged(nameof(WebtapuOnerisiVar));
    }

    /// <summary>Proje temizlendiğinde alanları sıfırlar.</summary>
    public void Sifirla()
    {
        KunyeDurumu = string.Empty;
        Yenile();
    }

    private static string CrsOnerisi(ProjeCrs? crs) => crs is null
        ? string.Empty
        : (crs.Datum == GeodetikDatum.Itrf96 ? "ITRF-96" : "ED-50") + " / TM3 " + crs.OrtaMeridyenDerece.ToString(CultureInfo.InvariantCulture) + "°";

    private (string Il, string Ilce, string Mahalle)? WebtapuOnerisi()
    {
        var p = _alan.Yol.Belge.MalikRehberi.Parseller.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Il) || !string.IsNullOrWhiteSpace(x.Ilce) || !string.IsNullOrWhiteSpace(x.Mahalle));
        return p is null ? null : (p.Il?.Trim() ?? string.Empty, p.Ilce?.Trim() ?? string.Empty, p.Mahalle?.Trim() ?? string.Empty);
    }

    private bool WebtapuOnerisiniUygula(bool yalnizBos)
    {
        if (WebtapuOnerisi() is not { } o)
        {
            return false;
        }

        KonumuYaz(o.Il, o.Ilce, o.Mahalle, yalnizBos);
        return true;
    }

    /// <summary>
    /// İl / İlçe / Köy-Mahalle kutularına yazar (boş kaynak değer yazılmaz); <paramref name="yalnizBos"/> ise yalnız boş kutular
    /// doldurulur. Değişen kutu sayısını döner.
    /// </summary>
    private int KonumuYaz(string? il, string? ilce, string? mahalle, bool yalnizBos)
    {
        var n = 0;
        if (Yeni(Il, il) is { } a)
        {
            Il = a;
            n++;
        }

        if (Yeni(Ilce, ilce) is { } b)
        {
            Ilce = b;
            n++;
        }

        if (Yeni(KoyMahalle, mahalle) is { } c)
        {
            KoyMahalle = c;
            n++;
        }

        return n;

        string? Yeni(string mevcut, string? kaynak)
        {
            var y = kaynak?.Trim() ?? string.Empty;
            return y.Length == 0 || (yalnizBos && mevcut.Trim().Length > 0) || string.Equals(mevcut, y, StringComparison.Ordinal) ? null : y;
        }
    }

    /// <summary>
    /// P7 UX: 4. adımda webtapu TAKBİS'e aktarıldığında 1. adımdaki boş İl / İlçe / Köy-Mahalle kutuları webtapu kaydıyla
    /// doldurulur (dolu kutulara dokunulmaz; kaydetmek için "Künyeyi Kaydet").
    /// </summary>
    public void WebtapuAktarildi()
    {
        if (WebtapuOnerisi() is { } o && KonumuYaz(o.Il, o.Ilce, o.Mahalle, yalnizBos: true) > 0)
        {
            KunyeDurumu = "Boş İl / İlçe / Mahalle webtapu kaydından dolduruldu; kaydetmek için \"Künyeyi Kaydet\".";
        }

        OnPropertyChanged(nameof(WebtapuOnerisiVar));
    }

    private bool WebtapuKunyeOkunabilir() => WebtapuOkuyucuVar && !IsBusy;

    [RelayCommand(CanExecute = nameof(WebtapuKunyeOkunabilir))]
    private void WebtapuKunyeIste() => WebtapuKunyeDosyasiIstendi?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// P7 UX: webtapu PDF'ini arka planda okur ve yalnız İl / İlçe / Mahalle'yi künye kutularına yazar (kişi adları, hisseler ve
    /// Malik Rehberi'ne dokunulmaz — KVKK). Okunamayan belgenin Türkçe mesajı <see cref="KunyeDurumu"/>'na yazılır; okuyucunun
    /// beklenmeyen istisnası ERR-GIRD-003 sayılır. Projeye yazmak için "Künyeyi Kaydet" gerekir.
    /// </summary>
    public async Task WebtapudanKunyeOkuAsync(Stream akis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(akis);
        if (_alan.TapuOkuyucu is not { } okuyucu || IsBusy)
        {
            return;
        }

        IsBusy = true;
        Result<TapuKaydi> sonuc;
        try
        {
            sonuc = await Task.Run(() => okuyucu.Oku(akis, cancellationToken), cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // async void dosya seçici işleyicisine istisna sızarsa uygulama çöker; katalog hatasına çevrilir.
        catch (Exception)
#pragma warning restore CA1031
        {
            sonuc = Result<TapuKaydi>.Failure(ErrorCatalog.Get(ErrorCodes.GirdPdfBozuk));
        }
        finally
        {
            IsBusy = false;
        }

        if (sonuc.IsFailure)
        {
            KunyeDurumu = sonuc.Error.UserMessage;
            return;
        }

        var k = sonuc.Value;
        if (string.IsNullOrWhiteSpace(k.Il) && string.IsNullOrWhiteSpace(k.Ilce) && string.IsNullOrWhiteSpace(k.Mahalle))
        {
            KunyeDurumu = "webtapu PDF'inde İl / İlçe / Mahalle bulunamadı.";
            return;
        }

        KonumuYaz(k.Il, k.Ilce, k.Mahalle, yalnizBos: false);
        KunyeDurumu = "İl / İlçe / Mahalle webtapu PDF'inden okundu (kişi adları künyeye yazılmaz); kaydetmek için \"Künyeyi Kaydet\".";
    }

    [RelayCommand]
    private void WebtapudanOner() =>
        KunyeDurumu = WebtapuOnerisiniUygula(yalnizBos: false)
            ? "İl / İlçe / Mahalle webtapu kaydından önerildi; kaydetmek için \"Künyeyi Kaydet\"."
            : "Önerilecek webtapu kaydı yok (4. adımda webtapu PDF yükleyin).";

    private bool Bos() => !IsBusy;

    [RelayCommand(CanExecute = nameof(Bos))]
    private async Task KunyeyiKaydetAsync()
    {
        var k = new ProjeKunyesi(Il, Ilce, KoyMahalle, Belediye, PaftaNolari, KadastroId, DatumProjeksiyon);
        var tumuBos = k.Alanlar().All(a => string.IsNullOrWhiteSpace(a.Deger));
        IsBusy = true;
        bool basarili;
        try
        {
            var r = await _alan.Yol.GonderAsync(new ProjeKunyesiAyarlaKomutu(tumuBos ? null : k), _alan.KullaniciId).ConfigureAwait(true);
            basarili = r.IsSuccess;
            KunyeDurumu = r.IsSuccess ? (tumuBos ? "Künye temizlendi." : "Künye kaydedildi (revizyona dahil).") : r.Error.UserMessage;
        }
        finally
        {
            IsBusy = false;
        }

        if (basarili)
        {
            Uygulandi?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Metni ve MK'yı doğrular; boş metin "dönüşüm yok" demektir (null).</summary>
    private (bool Gecerli, DonusumGirdisi? Girdi) GirdiOku()
    {
        DonusumHatasi = string.Empty;
        var noktalar = DonusumMetni.Ayristir(OrtakNoktaMetni);
        if (noktalar.IsFailure)
        {
            DonusumHatasi = noktalar.Error.UserMessage;
            return (false, null);
        }

        if (noktalar.Value.Count == 0)
        {
            return (true, null);
        }

        if (!SayiGirdiAyristirici.TryParseDecimal(MkMetni, out var mk, out var mkHatasi) || mk <= 0m || mk > DonusumGirdisi.MaxMk)
        {
            DonusumHatasi = mkHatasi == SayiGirdiAyristirici.BinlikAyiriciUyarisi
                ? "MK: " + mkHatasi
                : "MK değeri 0'dan büyük ve en çok 10 olmalı; ondalık ayırıcı '.' veya ',' olabilir (ör. 0.09).";
            return (false, null);
        }

        var enAz = DonusumGirdisi.EnAzNokta(Model);
        if (noktalar.Value.Count < enAz)
        {
            DonusumHatasi = $"{ModelAdi(Model)} için en az {enAz} ortak nokta gerekir.";
            return (false, null);
        }

        return (true, new DonusumGirdisi(Model, mk, noktalar.Value));
    }

    /// <summary>Parametre önizlemesi; hesaplanamıyorsa Türkçe hata.</summary>
    private bool Onizle(DonusumGirdisi girdi)
    {
        ParametreSatirlari.Clear();
        if (_alan.Donusum is not { } servis)
        {
            return true;
        }

        var r = servis.ParametreleriHesapla(girdi);
        if (r.IsFailure)
        {
            DonusumHatasi = "Dönüşüm hesaplanamadı: noktalar çakışık/eşdoğrusal olabilir veya sayı yetersiz (" + r.Error.Code + ").";
            return false;
        }

        var p = r.Value;
        var inv = CultureInfo.InvariantCulture;
        ParametreSatirlari.Add(string.Create(inv, $"A= {p.A:0.00000000}   B= {p.B:0.00000000}"));
        ParametreSatirlari.Add(string.Create(inv, $"C= {p.C:0.00000000}   D= {p.D:0.00000000}"));
        ParametreSatirlari.Add(string.Create(inv, $"CY= {p.Cy:0.000}   CX= {p.Cx:0.000}"));
        ParametreSatirlari.Add("Ölçek katsayısı = " + (p.OlcekKatsayisi is { } o ? o.ToString("0.000000000", inv) : "— (Afin)"));
        ParametreSatirlari.Add("Dönüklük = " + (p.DonuklukGrad is { } d ? d.ToString("0.000000000", inv) + " grad" : "— (Afin)"));
        ParametreSatirlari.Add("Ortalama hata = " + (p.OrtalamaHataM is { } m ? m.ToString("0.000", inv) + " m" : "— (serbestlik 0)"));
        var enBuyuk = p.Artiklar.Select(a => Math.Max(Math.Abs(a.Vy), Math.Abs(a.Vx))).DefaultIfEmpty(0m).Max();
        ParametreSatirlari.Add(string.Create(inv, $"{p.Artiklar.Count} ortak nokta · en büyük |V| = {enBuyuk:0.000} m"));
        return true;
    }

    [RelayCommand(CanExecute = nameof(Bos))]
    private void DonusumuOnizle()
    {
        var (gecerli, girdi) = GirdiOku();
        if (!gecerli)
        {
            return;
        }

        if (girdi is null)
        {
            ParametreSatirlari.Clear();
            DonusumDurumu = "Ortak nokta girilmedi.";
            return;
        }

        if (Onizle(girdi))
        {
            DonusumDurumu = OnizlemeVar ? "Önizleme hazır (henüz kaydedilmedi)." : "Dönüşüm servisi bağlı değil; önizleme yapılamadı.";
        }
    }

    /// <summary>
    /// P7 UX: tabloda hücre düzenlenince / satır eklenip silinince parametre önizlemesi canlı güncellenir (projeye yazılmaz).
    /// Eksik satır veya model için yetersiz nokta hata sayılmaz, yalnız durum metninde belirtilir.
    /// </summary>
    private void CanliOnizle()
    {
        if (IsBusy)
        {
            return;
        }

        ParametreSatirlari.Clear();
        DonusumHatasi = string.Empty;
        var dolu = OrtakNoktaSatirlari.Where(s => !s.Bos).ToList();
        var enAz = DonusumGirdisi.EnAzNokta(Model);
        if (dolu.Count == 0)
        {
            DonusumDurumu = "Ortak nokta girilmedi.";
        }
        else if (dolu.FirstOrDefault(s => s.Hata is not null) is { } hatali)
        {
            DonusumHatasi = "Ortak nokta " + (hatali.NoktaNo.Trim().Length > 0 ? hatali.NoktaNo.Trim() : "(numarasız)") + " — " + hatali.Hata;
            DonusumDurumu = "Canlı önizleme: hatalı hücreyi düzeltin.";
        }
        else if (dolu.Any(s => !s.Tamam))
        {
            DonusumDurumu = "Canlı önizleme: satırları tamamlayın (Nokta No, Y1, X1, Y2, X2).";
        }
        else if (dolu.Count < enAz)
        {
            DonusumDurumu = $"Canlı önizleme: {ModelAdi(Model)} için en az {enAz} ortak nokta gerekir.";
        }
        else if (GirdiOku() is (true, { } girdi) && Onizle(girdi))
        {
            DonusumDurumu = OnizlemeVar ? "Canlı önizleme (henüz kaydedilmedi)." : "Dönüşüm servisi bağlı değil; önizleme yapılamadı.";
        }
    }

    /// <summary>Model değişince (Helmert ↔ Afin) tablodaki noktalarla canlı önizleme yenilenir (Yenile sırasında değil).</summary>
    partial void OnModelChanged(DonusumModeli value)
    {
        if (!_yenileniyor && OrtakNoktaSatirlari.Any(s => !s.Bos))
        {
            CanliOnizle();
        }
    }

    /// <summary>
    /// "Metin / Toplu Yapıştır" alanından çıkınca: virgüllü ondalık içeren metin tablodan <c>.</c> biçiminde yeniden yazılır
    /// (metin ayrıştırıcısı yalnız '.' kabul eder) ve canlı önizleme yapılır. Virgül yoksa metne dokunulmaz.
    /// </summary>
    [RelayCommand]
    private void OrtakNoktaMetniniNormalizeEt()
    {
        if (OrtakNoktaMetni.Contains(',', StringComparison.Ordinal))
        {
            _ortakSenkron.NormalizeEt(); // TablodanYaz → CanliOnizle
        }
        else if (OrtakNoktaSatirlari.Any(s => !s.Bos))
        {
            CanliOnizle();
        }
    }

    [RelayCommand]
    private void OrtakNoktaEkle() => OrtakNoktaSatirlari.Add(new OrtakNoktaSatirVm());

    [RelayCommand]
    private void OrtakNoktaSil(OrtakNoktaSatirVm? satir)
    {
        if (satir is not null)
        {
            OrtakNoktaSatirlari.Remove(satir);
        }
    }

    [RelayCommand(CanExecute = nameof(Bos))]
    private async Task DonusumuUygulaAsync()
    {
        var (gecerli, girdi) = GirdiOku();
        if (!gecerli || (girdi is not null && !Onizle(girdi)))
        {
            DonusumDurumu = "Kaydedilmedi.";
            return;
        }

        IsBusy = true;
        try
        {
            var r = await _alan.Yol.GonderAsync(new DonusumGirdisiAyarlaKomutu(girdi), _alan.KullaniciId).ConfigureAwait(true);
            if (r.IsFailure)
            {
                DonusumHatasi = r.Error.UserMessage;
                DonusumDurumu = "Kaydedilmedi.";
                return;
            }
            else
            {
                DonusumDurumu = girdi is null
                    ? "Dönüşüm girdisi temizlendi."
                    : $"Kaydedildi: {girdi.OrtakNoktalar.Count} ortak nokta · {ModelAdi(girdi.Model)} · MK {girdi.Mk.ToString("0.00###", CultureInfo.InvariantCulture)} (revizyona dahil; deprem öncesi koordinatlar ters dönüşümle hesaplanır).";
            }
        }
        finally
        {
            IsBusy = false;
        }

        Uygulandi?.Invoke(this, EventArgs.Empty);
    }
}
