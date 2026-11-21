using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cad;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.Presentation.Avalonia.Gorunum;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>SAHA-020: arşiv onaylı kadastro köşesi satırı (<c>Köşe # | Arşiv Nokta No | Arşiv Y | Arşiv X</c>).</summary>
public sealed partial class ArsivNoktaSatirVm : ObservableObject
{
    [ObservableProperty]
    private string _noktaNo = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YHatali))]
    private string _arsivY = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(XHatali))]
    private string _arsivX = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoHatali))]
    private string? _hata;

    internal ArsivNoktaSatirVm(KadastroKosesi kose) => Kose = kose;

    /// <summary>Köşenin projedeki kaynağı (sıra, kadastro parseli, eşleme koordinatı).</summary>
    internal KadastroKosesi Kose { get; }

    public int Sira => Kose.Sira;

    public string KadastroParselId => Kose.KadastroParselId;

    /// <summary>Kısa köşe etiketi (<c>1 · 901/7</c>).</summary>
    public string KoseEtiketi => Sira.ToString(CultureInfo.InvariantCulture) + " · " + KadastroParselId;

    public bool YHatali => !SayiGirdiAyristirici.TryParseDecimal(ArsivY, out _);

    public bool XHatali => !SayiGirdiAyristirici.TryParseDecimal(ArsivX, out _);

    public bool NoHatali => !ArsivNoktalari.NoktaNoGecerli(NoktaNo.Trim());

    private bool _yukleniyor;

    /// <summary>
    /// SAHA-021: mühendis bu satırda projeye henüz uygulanmamış değişiklik yaptı (elle veya toplu yapıştır / <c>.NCN</c>). Satırlar
    /// yeniden kurulurken (CAD yeniden yüklendi) bu değerler varsayılan numara / köşe koordinatıyla ezilmez.
    /// </summary>
    internal bool Duzenlendi { get; set; }

    partial void OnNoktaNoChanged(string value)
    {
        OnPropertyChanged(nameof(NoHatali));
        Duzenlendi |= !_yukleniyor;
    }

    partial void OnArsivYChanged(string value) => Duzenlendi |= !_yukleniyor;

    partial void OnArsivXChanged(string value) => Duzenlendi |= !_yukleniyor;

    internal void Yukle(ArsivNoktaKaydi k, bool duzenleme = false)
    {
        // Reviewer: tam hassasiyet (köşe 6+ ondalık taşıyabilir; yuvarlanırsa "girilmemiş varsayılan" ayrımı bozulmasın).
        Yukle(k.NoktaNo, k.ArsivY.ToString("0.00##########", CultureInfo.InvariantCulture), k.ArsivX.ToString("0.00##########", CultureInfo.InvariantCulture), duzenleme);
    }

    /// <summary>Metin değerlerini olduğu gibi yükler (kaydedilmemiş düzenleme korunurken kullanıcının yazdığı metin değişmez).</summary>
    internal void Yukle(string noktaNo, string arsivY, string arsivX, bool duzenleme)
    {
        _yukleniyor = true;
        try
        {
            NoktaNo = noktaNo;
            ArsivY = arsivY;
            ArsivX = arsivX;
        }
        finally
        {
            _yukleniyor = false;
        }

        Duzenlendi = duzenleme;
    }
}

/// <summary>
/// SAHA-020: 1. adım "📌 Arşiv Onaylı Kadastro Noktaları (TK_PARSEL)" kartı. Projenin kadastro köşelerini (<c>.DNS</c> ileri
/// dönüşümünde Deprem Öncesi) <see cref="ArsivNoktalari.Koseler"/> sırasıyla listeler; varsayılan nokta no projede kayıtlıysa o, yoksa
/// yüklenen <c>.NCZ</c>'nin tip 1 noktasından (≤ 0.02 m), o da yoksa köşe sırasıdır. Mühendis nokta no ve arşiv Y, X'i kutucuklarda
/// düzenler veya "📋 Toplu Yapıştır / .NCN Yükle" alanından (<c>NoktaNo Y X</c>, <c>NoktaNo;Y;X</c>, Netcad <c>.ncn</c>) aktarır;
/// "Arşiv Noktalarını Uygula" <see cref="ArsivNoktalariAyarlaKomutu"/>'nu CommandBus'a gönderir (RevisionId'ye girer; onay düşer).
/// </summary>
public sealed partial class ArsivNoktalariViewModel : ObservableObject
{
    private readonly ProjeCalismaAlani _alan;
    private IReadOnlyList<CadPoint> _cadNoktalari = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UygulaCommand), nameof(KaldirCommand), nameof(TopluAktarCommand), nameof(NcnIsteCommand))]
    private bool _isBusy;

    /// <summary>Toplu yapıştırma metni.</summary>
    [ObservableProperty]
    private string _topluMetin = string.Empty;

    [ObservableProperty]
    private string _durum = string.Empty;

    [ObservableProperty]
    private string _hata = string.Empty;

    public ArsivNoktalariViewModel(ProjeCalismaAlani alan)
    {
        ArgumentNullException.ThrowIfNull(alan);
        _alan = alan;
        Yenile();
    }

    /// <summary>Arşiv noktaları projeye yazıldı veya kaldırıldı (onay ekranı yenilenir).</summary>
    public event EventHandler? Uygulandi;

    /// <summary>Görünüm <c>.ncn</c> dosya seçicisini açar ve <see cref="NcnYukleAsync"/> çağırır.</summary>
    public event EventHandler? NcnDosyasiIstendi;

    /// <summary>Kadastro köşe satırları.</summary>
    public ObservableCollection<ArsivNoktaSatirVm> Satirlar { get; } = [];

    /// <summary>Kadastro köşesi var mı (tablo görünür).</summary>
    public bool SatirVar => Satirlar.Count > 0;

    /// <summary>Projede arşiv noktası kayıtlı mı.</summary>
    public bool KayitliVar => _alan.Yol.Belge.ArsivKadastroNoktalari.Count > 0;

    /// <summary>
    /// Yüklenen CAD'in tip 1 noktaları (yalnız <c>.NCZ</c>); satırlar yeniden kurulur (kayıtlı olmayan köşeler için varsayılan no).
    /// </summary>
    public void CadNoktalariniAyarla(IReadOnlyList<CadPoint> noktalar)
    {
        _cadNoktalari = noktalar ?? [];
        Yenile(zorla: true);
    }

    /// <summary>
    /// Satırları projeden kurar. <paramref name="zorla"/> false ise köşe listesi değişmediyse satırlara dokunulmaz (her proje
    /// komutundan sonra çağrılır). SAHA-021: satırlar yeniden kurulsa da (<paramref name="zorla"/> veya köşe listesi değişti) mühendisin
    /// kaydedilmemiş düzenlemeleri aynı (≤ 0.02 m) köşede korunur; yalnız <paramref name="duzenlemeleriKoru"/> false (Kaldır / Sıfırla)
    /// iken bırakılır. Kayıtlı arşiv noktaları <see cref="ArsivNoktalari.Varsayilan"/> ile her zaman korunur.
    /// </summary>
    public void Yenile(bool zorla = false, bool duzenlemeleriKoru = true)
    {
        var b = _alan.Yol.Belge;
        var koseler = ArsivNoktalari.Koseler(b);
        var ayni = koseler.Count == Satirlar.Count && koseler.Select((k, i) => k == Satirlar[i].Kose).All(x => x);
        if (!zorla && ayni)
        {
            DurumuYaz();
            return;
        }

        var duzenlenen = duzenlemeleriKoru ? Satirlar.Where(s => s.Duzenlendi).ToList() : [];
        foreach (var s in Satirlar)
        {
            s.PropertyChanged -= SatirDegisti;
        }

        Satirlar.Clear();
        var varsayilan = ArsivNoktalari.Varsayilan(b, _cadNoktalari).ToDictionary(k => k.Sira);
        foreach (var k in koseler)
        {
            var s = new ArsivNoktaSatirVm(k);
            var onceki = duzenlenen.Where(d => ArsivNoktalari.Uzaklik(d.Kose.Kose, k.Kose) <= ArsivNoktalari.CakismaToleransiM)
                .OrderBy(d => ArsivNoktalari.Uzaklik(d.Kose.Kose, k.Kose)).FirstOrDefault();
            if (onceki is not null)
            {
                duzenlenen.Remove(onceki);

                // Reviewer O2: köşe kaydıysa ve arşiv Y, X eski köşenin kendisiyse (≤ 1 mm; mühendis girmedi) yeni köşe koordinatı alınır.
                var girilmemis = SayiGirdiAyristirici.TryParseDecimal(onceki.ArsivY, out var y) && SayiGirdiAyristirici.TryParseDecimal(onceki.ArsivX, out var x)
                    && ArsivNoktalari.Uzaklik(new GeodeticPoint(x, y), onceki.Kose.Kose) <= ArsivNoktalari.AyniKoordinatToleransiM;
                var (ay, ax) = girilmemis && onceki.Kose.Kose != k.Kose
                    ? (k.Kose.Y.ToString("0.00##########", CultureInfo.InvariantCulture), k.Kose.X.ToString("0.00##########", CultureInfo.InvariantCulture))
                    : (onceki.ArsivY, onceki.ArsivX);
                s.Yukle(onceki.NoktaNo, ay, ax, duzenleme: true);
            }
            else
            {
                s.Yukle(varsayilan[k.Sira]);
            }

            s.PropertyChanged += SatirDegisti;
            Satirlar.Add(s);
        }

        Hata = string.Empty;
        DurumuYaz();
        OnPropertyChanged(nameof(SatirVar));
        KomutlariBildir();
    }

    /// <summary>Proje sıfırlandı: CAD noktaları ve satırlar boşalır.</summary>
    public void Sifirla()
    {
        _cadNoktalari = [];
        TopluMetin = string.Empty;
        Yenile(zorla: true, duzenlemeleriKoru: false);
    }

    private bool Bos() => !IsBusy && Satirlar.Count > 0;

    private bool Kaldirilabilir() => !IsBusy && KayitliVar;

    [RelayCommand(CanExecute = nameof(Bos))]
    private void NcnIste() => NcnDosyasiIstendi?.Invoke(this, EventArgs.Empty);

    /// <summary>Toplu alana okunacak en fazla karakter (uygulama güvenlik sınırı).</summary>
    public const int EnFazlaNcnKarakteri = 1_000_000;

    /// <summary>Netcad <c>.ncn</c> (veya düz metin) dosyasını toplu alana okur ve aktarır.</summary>
    public async Task NcnYukleAsync(Stream akis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(akis);
        // Reviewer D2: okuma en fazla 1 M karakterle sınırlıdır (dosyanın tamamı belleğe alınmaz).
        using var okuyucu = new StreamReader(akis, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        var tampon = new char[EnFazlaNcnKarakteri];
        var okunan = 0;
        int n;
        while (okunan < tampon.Length && (n = await okuyucu.ReadAsync(tampon.AsMemory(okunan), cancellationToken).ConfigureAwait(true)) > 0)
        {
            okunan += n;
        }

        TopluMetin = new string(tampon, 0, okunan);
        TopluAktar();
    }

    /// <summary>
    /// Toplu metni satırlara aktarır: her satır koordinatıyla (≤ 0.02 m) en yakın köşeye, bulunamazsa aynı nokta no'lu köşeye eşlenir
    /// (<see cref="ArsivNoktalari.TopluAktar"/>). Proje değişmez; "Arşiv Noktalarını Uygula" ile yazılır.
    /// </summary>
    [RelayCommand(CanExecute = nameof(Bos))]
    private void TopluAktar()
    {
        var girdiler = TopluAyristir(TopluMetin, out var hatalar);
        if (girdiler.Count == 0)
        {
            Hata = hatalar.Count > 0 ? string.Join(Environment.NewLine, hatalar.Take(5)) : "Aktarılacak satır yok (biçim: NoktaNo Y X veya NoktaNo;Y;X).";
            return;
        }

        if (!Kayitlar(out var mevcut, out var hata))
        {
            Hata = hata!;
            return;
        }

        var sonuc = ArsivNoktalari.TopluAktar(mevcut, girdiler);
        for (var i = 0; i < Satirlar.Count; i++)
        {
            // SAHA-021: aktarılan satır kaydedilmemiş düzenlemedir (CAD yeniden yüklense de korunur).
            Satirlar[i].Yukle(sonuc.Kayitlar[i], duzenleme: Satirlar[i].Duzenlendi || sonuc.Kayitlar[i] != mevcut[i]);
        }

        var notlar = new List<string>(hatalar.Take(5));
        if (sonuc.Eslenemeyen.Count > 0)
        {
            notlar.Add(string.Create(CultureInfo.InvariantCulture,
                $"{sonuc.Eslenemeyen.Count} satır hiçbir kadastro köşesiyle (≤ {ArsivNoktalari.CakismaToleransiM:0.00} m veya aynı nokta no) eşleşmedi: {string.Join(", ", sonuc.Eslenemeyen.Take(10))}"));
        }

        Hata = string.Join(Environment.NewLine, notlar);
        Durum = string.Create(CultureInfo.InvariantCulture,
            $"📋 {sonuc.Eslenen} arşiv noktası aktarıldı; kontrol edip \"Arşiv Noktalarını Uygula\" deyin.");
    }

    [RelayCommand(CanExecute = nameof(Bos))]
    private async Task UygulaAsync()
    {
        if (!Kayitlar(out var kayitlar, out var hata))
        {
            Hata = hata!;
            return;
        }

        IsBusy = true;
        try
        {
            var r = await _alan.Yol.GonderAsync(new ArsivNoktalariAyarlaKomutu(kayitlar), _alan.KullaniciId).ConfigureAwait(true);
            if (r.IsFailure)
            {
                Hata = "Arşiv noktaları projeye yazılamadı: " + r.Error.UserMessage
                    + (r.Error.TechnicalMessage.Contains("duplicated", StringComparison.Ordinal) ? " (aynı nokta no iki köşede kullanılamaz)" : string.Empty);
                return;
            }

            Hata = string.Empty;
            foreach (var s in Satirlar)
            {
                s.Duzenlendi = false; // projeye yazıldı; artık Varsayilan kayıtlı değeri korur
            }

            DurumuYaz();
            Durum = string.Create(CultureInfo.InvariantCulture,
                $"✅ {kayitlar.Count} arşiv onaylı kadastro noktası projeye kaydedildi; KOORDİNE ÖZET, RÖLÖVE ve ALAN HESABI bu numaralarla basılır.");
        }
        finally
        {
            IsBusy = false;
        }

        Uygulandi?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(Kaldirilabilir))]
    private async Task KaldirAsync()
    {
        IsBusy = true;
        try
        {
            var r = await _alan.Yol.GonderAsync(new ArsivNoktalariAyarlaKomutu([]), _alan.KullaniciId).ConfigureAwait(true);
            if (r.IsFailure)
            {
                Hata = "Arşiv noktaları kaldırılamadı: " + r.Error.UserMessage;
                return;
            }
        }
        finally
        {
            IsBusy = false;
        }

        Yenile(zorla: true, duzenlemeleriKoru: false);
        Durum = "Arşiv noktaları kaldırıldı; çıktılarda otomatik nokta numaraları basılır.";
        Uygulandi?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Satırları kayıtlara çevirir; ilk hatalı satırı (köşe sırasıyla) bildirir.</summary>
    private bool Kayitlar(out IReadOnlyList<ArsivNoktaKaydi> kayitlar, out string? hata)
    {
        var liste = new List<ArsivNoktaKaydi>(Satirlar.Count);
        hata = null;
        foreach (var s in Satirlar)
        {
            var no = s.NoktaNo.Trim();
            if (!ArsivNoktalari.NoktaNoGecerli(no))
            {
                hata = $"Köşe {s.Sira}: arşiv nokta no 1–14 karakter (harf, rakam, - . _ /) olmalı.";
                break;
            }

            if (!SayiGirdiAyristirici.TryParseDecimal(s.ArsivY, out var y, out var hy))
            {
                hata = $"Köşe {s.Sira}: arşiv Y geçersiz — " + hy;
                break;
            }

            if (!SayiGirdiAyristirici.TryParseDecimal(s.ArsivX, out var x, out var hx))
            {
                hata = $"Köşe {s.Sira}: arşiv X geçersiz — " + hx;
                break;
            }

            if (ArsivNoktalari.Uzaklik(new GeodeticPoint(x, y), s.Kose.Kose) > ArsivNoktalari.EnFazlaArsivSapmasiM)
            {
                hata = $"Köşe {s.Sira}: arşiv Y, X köşeden 100 m'den uzak — Y ile X yer değiştirmiş olabilir.";
                break;
            }

            liste.Add(new ArsivNoktaKaydi(s.Sira, no, y, x, s.KadastroParselId, s.Kose.Kose.Y, s.Kose.Kose.X));
        }

        var yinelenen = liste.GroupBy(k => k.NoktaNo, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (hata is null && yinelenen is not null)
        {
            hata = "Aynı arşiv nokta no birden çok köşede: " + yinelenen.Key;
        }

        kayitlar = liste.AsReadOnly();
        return hata is null;
    }

    /// <summary>
    /// Toplu metin ayrıştırıcısı: boş ve <c>#</c> / <c>;</c> ile başlayan satırlar atlanır; ayırıcı <c>;</c>, sekme veya boşluk (yoksa
    /// <c>,</c>); ilk alan nokta no, sonraki iki alan Y, X (<see cref="SayiGirdiAyristirici"/>: <c>,</c> / <c>.</c> ondalık); fazla alanlar
    /// (Netcad <c>.ncn</c> kot / kod) yok sayılır. Sayısal olmayan başlık satırları hata olarak raporlanır.
    /// </summary>
    internal static List<ArsivNoktaGirdisi> TopluAyristir(string metin, out List<string> hatalar)
    {
        hatalar = [];
        var sonuc = new List<ArsivNoktaGirdisi>();
        var satirlar = (metin ?? string.Empty).Split('\n');
        for (var i = 0; i < satirlar.Length; i++)
        {
            var t = satirlar[i].Trim().TrimEnd('\r');
            if (t.Length == 0 || t[0] == '#')
            {
                continue;
            }

            string[] alan;
            if (t.Contains(';', StringComparison.Ordinal))
            {
                alan = t.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                alan = t.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (alan.Length < 3 && t.Contains(',', StringComparison.Ordinal))
                {
                    alan = t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                }
            }

            var satirNo = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (alan.Length < 3)
            {
                hatalar.Add($"Satır {satirNo}: en az 3 alan gerekli (NoktaNo Y X).");
                continue;
            }

            if (!SayiGirdiAyristirici.TryParseDecimal(alan[1], out var y) || !SayiGirdiAyristirici.TryParseDecimal(alan[2], out var x))
            {
                hatalar.Add($"Satır {satirNo}: Y / X sayı değil.");
                continue;
            }

            if (!ArsivNoktalari.NoktaNoGecerli(alan[0]))
            {
                hatalar.Add($"Satır {satirNo}: nokta no 1–14 karakter (harf, rakam, - . _ /) olmalı.");
                continue;
            }

            sonuc.Add(new ArsivNoktaGirdisi(alan[0], y, x));
        }

        return sonuc;
    }

    private void SatirDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArsivNoktaSatirVm.NoktaNo) or nameof(ArsivNoktaSatirVm.ArsivY) or nameof(ArsivNoktaSatirVm.ArsivX))
        {
            Durum = KayitliVar ? "Değişiklikler kaydedilmedi; \"Arşiv Noktalarını Uygula\" deyin." : Durum;
        }
    }

    private void DurumuYaz()
    {
        var b = _alan.Yol.Belge;
        Durum = Satirlar.Count == 0
            ? "Kadastro (TK_PARSEL) parseli yok; önce CAD dosyasını yükleyip katman rollerini eşleyin."
            : b.ArsivKadastroNoktalari.Count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Kayıtlı: {b.ArsivKadastroNoktalari.Count} arşiv noktası · {Satirlar.Count} kadastro köşesi")
                : string.Create(CultureInfo.InvariantCulture, $"{Satirlar.Count} kadastro köşesi listelendi (kaydedilmedi; varsayılan numaralar)");
        OnPropertyChanged(nameof(KayitliVar));
        KomutlariBildir();
    }

    private void KomutlariBildir()
    {
        UygulaCommand.NotifyCanExecuteChanged();
        KaldirCommand.NotifyCanExecuteChanged();
        TopluAktarCommand.NotifyCanExecuteChanged();
        NcnIsteCommand.NotifyCanExecuteChanged();
    }
}
