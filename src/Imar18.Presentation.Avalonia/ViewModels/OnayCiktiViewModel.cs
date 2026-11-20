using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cikti;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Presentation.Avalonia.Gorunum;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>
/// Sihirbaz 5. adım "Cetvel &amp; Çıktı" (P5.3, spec 11): onay ekranı özeti, onay metni onay kutusu, onay / geri alma ve taslak
/// çıktı komutları. Tüm işlemler Application servisleri (CommandBus, ApprovalService, CiktiServisi) üzerinden yürür; onaysız
/// çıktı isteği arayüzde engellenmez, servis tarafından reddedilip denetime yazılır (spec 11.2 madde 7). Kullanıcıya yalnızca
/// Türkçe <c>UserMessage</c> gösterilir.
/// </summary>
public sealed partial class OnayCiktiViewModel : ObservableObject
{
    private readonly ProjeCalismaAlani _alan;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OnaylaCommand), nameof(GeriAlCommand))]
    [NotifyPropertyChangedFor(nameof(GecerliOnayVar), nameof(RevizyonKisa), nameof(RevizyonTam), nameof(OnayDurumu), nameof(EngelVar))]
    private OnayEkrani? _ekran;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OnaylaCommand))]
    private bool _onayMetniOkundu;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OnaylaCommand), nameof(GeriAlCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _durumMetni = string.Empty;

    /// <summary>
    /// SORU-053: "Doğrulama özeti (.sha256) yan dosyası oluştur" kutucuğu. Varsayılan kapalıdır; kapalıyken diske ve Fen Klasörü
    /// ZIP'ine <c>.sha256</c> yazılmaz, SHA-256 özeti yine durum satırında ve audit kaydında tutulur.
    /// </summary>
    [ObservableProperty]
    private bool _sha256YanDosyasiUret;

    public OnayCiktiViewModel(ProjeCalismaAlani alan)
    {
        ArgumentNullException.ThrowIfNull(alan);
        _alan = alan;
        Yenile();
    }

    /// <summary>Görünüm, istenen çıktı türü için kayıt yolu seçtirir ve <see cref="UretAsync"/>'i çağırır.</summary>
    public event EventHandler<CiktiTuru>? KayitYoluIstendi;

    /// <summary>Onay durumu değiştiğinde (bağlam menüsü ve komut çubuğu yenilemesi için).</summary>
    public event EventHandler? OnayDurumuDegisti;

    public string OnayMetni => ApprovalService.OnayMetni;

    public bool GecerliOnayVar => Ekran?.GecerliOnayVar == true;

    public ObservableCollection<string> Engeller { get; } = [];

    /// <summary>
    /// SAHA-004: topoloji bulgularının Türkçe parsel / alan ayrıntısı (Uyarı &amp; Denetim sekmesi). İmar yolu (Info) ve
    /// milimetrik sınır toleransı (Warning) hesabı bozmayan bilgi/uyarı satırlarıdır; sayaca yalnızca onay engeli olan
    /// (Error/Critical) bulgular girer (<see cref="TopolojiUyariSayisi"/>, P7 uyarı filtresi).
    /// </summary>
    public ObservableCollection<TopolojiNotu> TopolojiNotlari { get; } = [];

    /// <summary>Hesabı/topolojiyi gerçekten etkileyen (onay engeli: Error + Critical) topoloji bulgusu sayısı.</summary>
    public int TopolojiUyariSayisi => TopolojiNotlari.Count(n => n.Seviye >= Imar18.Domain.Diagnostics.ErrorSeverity.Error);

    /// <summary>P7: onay engeli var mı (engel listesi yalnızca 5. adımda gösterilir).</summary>
    public bool EngelVar => Ekran is { Engeller.Count: > 0 };

    /// <summary>P7: RevisionId rozeti (ilk 8 karakter + "…").</summary>
    public string RevizyonKisa => TurkceAdlar.KisaRevizyon(Ekran?.RevizyonId);

    /// <summary>P7: RevisionId'nin tamamı (araç ipucu ve panoya kopyalama).</summary>
    public string RevizyonTam => Ekran?.RevizyonId ?? string.Empty;

    /// <summary>P7: üst çubuktaki onay durumu rozeti.</summary>
    public string OnayDurumu => GecerliOnayVar ? "✅ Onaylı Revizyon" : "⏳ Mühendis Onayı Bekleniyor";

    /// <summary>Görünüm, metni sistem panosuna kopyalar (platform işi görünümdedir).</summary>
    public event EventHandler<string>? PanoyaKopyalaIstendi;

    [RelayCommand]
    private void RevizyonuKopyala()
    {
        if (RevizyonTam.Length > 0)
        {
            PanoyaKopyalaIstendi?.Invoke(this, RevizyonTam);
            DurumMetni = "Revizyon kimliği panoya kopyalandı.";
        }
    }

    /// <summary>Kayıtlı üreticinin uzantısı (dosya seçici süzgeci için).</summary>
    public string? Uzanti(CiktiTuru tur) => _alan.Cikti.Uzanti(tur);

    /// <summary>Kayıt penceresinde önerilen dosya adı (ASCII güvenli; görünen ad Türkçedir).</summary>
    public string OnerilenDosyaAdi(CiktiTuru tur) => (tur switch
    {
        CiktiTuru.Cetveller => "Imar18_Cetveller",
        CiktiTuru.FenKlasoru => "Imar18_Fen_Klasoru",
        CiktiTuru.CksDopOzeti => "Imar18_PILOT_CKS_DOP_Ozeti", // SORU-039: CKS içinde damga yok; taslak niteliği dosya adında
        CiktiTuru.FenKlasoruZip => "Imar18_PILOT_Fen_Klasoru", // P7.3-C1: ZIP içindeki DOP.CKS damgasız olduğundan taslak izi adda
        _ => "Imar18_Cikti",
    }) + (Uzanti(tur) ?? string.Empty);

    /// <summary>Onay ekranını güncel proje durumundan yeniden kurar (senkron; yalnız yapıcı ve testler).</summary>
    public void Yenile() => Uygula(_alan.Onay.OnayEkraniHazirla());

    /// <summary>Onay ekranını arka planda (NTS overlay dahil değerlendirme, 50 ms kuralı — reviewer M2) yeniden kurar.</summary>
    public async Task YenileAsync() => Uygula(await Task.Run(_alan.Onay.OnayEkraniHazirla).ConfigureAwait(true));

    private void Uygula(OnayEkrani ekran)
    {
        var onceki = GecerliOnayVar;
        Ekran = ekran;
        Engeller.Clear();
        foreach (var e in Ekran.Engeller)
        {
            Engeller.Add(e);
        }

        TopolojiNotlari.Clear();
        foreach (var n in Ekran.TopolojiNotlari)
        {
            TopolojiNotlari.Add(n);
        }

        OnPropertyChanged(nameof(TopolojiUyariSayisi));

        if (onceki != GecerliOnayVar)
        {
            OnayDurumuDegisti?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool OnaylanabilirMi() => !IsBusy && OnayMetniOkundu && Ekran is { OnayVerilebilir: true, GecerliOnayVar: false };

    [RelayCommand(CanExecute = nameof(OnaylanabilirMi))]
    private async Task OnaylaAsync()
    {
        IsBusy = true;
        try
        {
            // Onay komutu kilit altında değerlendirme (NTS overlay) yapar: UI thread'inde değil, arka planda (reviewer M2).
            var sonuc = await Task.Run(() => _alan.Onay.OnaylaAsync(_alan.KullaniciId, ApprovalService.OnayMetni)).ConfigureAwait(true);
            DurumMetni = sonuc.IsSuccess ? "Revizyon onaylandı: " + Kisa(sonuc.Value.RevizyonId.Deger) : sonuc.Error.UserMessage;
        }
        finally
        {
            IsBusy = false;
        }

        await YenileAsync().ConfigureAwait(true);
    }

    private bool GeriAlinabilirMi() => !IsBusy && GecerliOnayVar;

    [RelayCommand(CanExecute = nameof(GeriAlinabilirMi))]
    private async Task GeriAlAsync()
    {
        IsBusy = true;
        try
        {
            var sonuc = await Task.Run(() => _alan.Onay.GeriAlAsync(_alan.KullaniciId)).ConfigureAwait(true);
            DurumMetni = sonuc.IsFailure
                ? sonuc.Error.UserMessage
                : sonuc.Value.NihaiCiktiUretilmisti
                    ? "Onay geri alındı. Bu revizyon için çıktı üretilmişti: revizyon gerekli."
                    : "Onay geri alındı.";
            OnayMetniOkundu = false;
        }
        finally
        {
            IsBusy = false;
        }

        await YenileAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CetvelIste() => KayitYoluIstendi?.Invoke(this, CiktiTuru.Cetveller);

    [RelayCommand]
    private void FenKlasoruIste() => KayitYoluIstendi?.Invoke(this, CiktiTuru.FenKlasoru);

    /// <summary>P7.3-C1: Fen Klasörü .zip paketi (ayrı PDF'ler + DOP.CKS).</summary>
    [RelayCommand]
    private void FenKlasoruZipIste() => KayitYoluIstendi?.Invoke(this, CiktiTuru.FenKlasoruZip);

    [RelayCommand]
    private void CksIste() => KayitYoluIstendi?.Invoke(this, CiktiTuru.CksDopOzeti);

    /// <summary>Çıktıyı üretir; başarılıysa true. Sonuç <see cref="DurumMetni"/>'nde gösterilir.</summary>
    public async Task<bool> UretAsync(CiktiTuru tur, string hedefYol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hedefYol);
        IsBusy = true;
        try
        {
            var sonuc = await _alan.Cikti.UretAsync(tur, _alan.KullaniciId, hedefYol, new CiktiSecenekleri(Sha256YanDosyasiUret)).ConfigureAwait(true);
            DurumMetni = sonuc.IsSuccess
                ? $"{TurkceAdlar.Ad(tur)} üretildi · SHA-256 {Kisa(sonuc.Value.Sha256)}{(sonuc.Value.ChecksumDosyasi is null ? string.Empty : " (.sha256 yan dosyası yazıldı)")} · {PilotDamgasi.Metin}"
                : sonuc.Error.UserMessage;
            return sonuc.IsSuccess;
        }
        finally
        {
            IsBusy = false;
            await YenileAsync().ConfigureAwait(true);
        }
    }

    private static string Kisa(string hex) => hex.Length > 12 ? hex[..12] + "…" : hex;
}
