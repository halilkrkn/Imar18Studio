using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cikti;
using Imar18.Application.KoordinatDonusumu;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.Domain.Revizyon;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>
/// P7.3-C2: 1. adım "Proje Künyesi &amp; Dönüşüm" kartı. Künye (İl, İlçe, Köy/Mahalle, Belediye/İdare, Pafta No(lar),
/// Kadastro ID, Datum/Projeksiyon) ve ortak noktalar (<c>NoktaNo;Y1;X1;Y2;X2</c>, model Helmert2B / Afin2B, MK) CommandBus
/// üzerinden projeye yazılır (RevisionId'ye dahil; değişince onay düşer). webtapu yüklendiyse İl/İlçe/Mahalle boş alanlara
/// otomatik önerilir. Proje noktaları deprem sonrası kabul edilir; deprem öncesi koordinatlar ters dönüşümle hesaplanır.
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
    [NotifyCanExecuteChangedFor(nameof(KunyeyiKaydetCommand), nameof(DonusumuUygulaCommand), nameof(DonusumuOnizleCommand))]
    private bool _isBusy;

    public KunyeDonusumViewModel(ProjeCalismaAlani alan)
    {
        ArgumentNullException.ThrowIfNull(alan);
        _alan = alan;
        Yenile();
    }

    /// <summary>Künye veya dönüşüm projeye yazıldı (onay ekranı yenilenir).</summary>
    public event EventHandler? Uygulandi;

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

        if (!yalnizBos || Il.Length == 0)
        {
            Il = o.Il;
        }

        if (!yalnizBos || Ilce.Length == 0)
        {
            Ilce = o.Ilce;
        }

        if (!yalnizBos || KoyMahalle.Length == 0)
        {
            KoyMahalle = o.Mahalle;
        }

        return true;
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

        if (!decimal.TryParse(MkMetni.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mk) || mk <= 0m || mk > DonusumGirdisi.MaxMk)
        {
            DonusumHatasi = "MK değeri 0'dan büyük, en çok 10 olmalı ve ondalık ayracı '.' olmalı (ör. 0.09).";
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
