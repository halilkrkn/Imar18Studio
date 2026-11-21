using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Girdi;
using Imar18.Presentation.Avalonia.Gorunum;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>SAHA-019: malik seçeneği (TAKBİS kimliği + baba adısız görünen ad).</summary>
public sealed record MusterekMalikSecenegi(string Id, string Ad)
{
    public override string ToString() => Ad;
}

/// <summary>SAHA-019: hak sahibi canlı özet şeridi satırı: <c>Malik · Kadastro | Net Hak | Dağıtılan | Kalan Fark</c>.</summary>
public sealed record MusterekHakOzeti(string Malik, string Kadastro, decimal NetHakM2, decimal DagitilanM2)
{
    public decimal FarkM2 => NetHakM2 - DagitilanM2;

    public bool Dengeli => FarkM2 == 0m;

    public string Etiket => Malik + " · " + Kadastro;

    public string NetHakMetni => NetHakM2.ToString("0.00", CultureInfo.InvariantCulture);

    public string DagitilanMetni => DagitilanM2.ToString("0.00", CultureInfo.InvariantCulture);

    public string FarkMetni => FarkM2.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
}

/// <summary>SAHA-019: parsel canlı kontrolü (<c>Parsel Alanı == Σ Hisseler</c>).</summary>
public sealed record MusterekParselKontrolu(string Kod, decimal AlanM2, decimal DagitilanM2)
{
    public bool Dengeli => AlanM2 == DagitilanM2;

    public string Metin => string.Create(CultureInfo.InvariantCulture,
        $"{(Dengeli ? "✓" : "✗")} {Kod}: parsel {AlanM2:0.00} m² · Σ hisse {DagitilanM2:0.00} m² · fark {AlanM2 - DagitilanM2:+0.00;-0.00;0.00} m²");
}

/// <summary>
/// SAHA-019: düzenlenebilir hissedar satırı — İmar Parseli, Malik (TAKBİS), Kadastro Kaynağı (malikin hak sahibi olduğu kadastro
/// parselleri; tek seçenek kendiliğinden seçilir) ve Alan (m², Numpad virgülü <see cref="SayiGirdiAyristirici"/>).
/// </summary>
public sealed partial class MusterekSatirVm : ObservableObject
{
    private readonly Func<string, IReadOnlyList<string>> _kaynaklar;

    [ObservableProperty]
    private string? _imarParseli;

    [ObservableProperty]
    private MusterekMalikSecenegi? _malik;

    [ObservableProperty]
    private string? _kadastroKaynagi;

    [ObservableProperty]
    private string _alanMetni = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlanHatali))]
    private string? _hata;

    internal MusterekSatirVm(Func<string, IReadOnlyList<string>> kaynaklar) => _kaynaklar = kaynaklar;

    /// <summary>Seçili malikin kadastro kaynakları.</summary>
    public ObservableCollection<string> KadastroSecenekleri { get; } = [];

    /// <summary>Alan hücresi hatalı mı (kırmızı çerçeve).</summary>
    public bool AlanHatali => Hata is not null;

    /// <summary>Alan geçerliyse değeri.</summary>
    internal decimal? Alan => SayiGirdiAyristirici.TryParseDecimal(AlanMetni, out var a) ? a : null;

    partial void OnMalikChanged(MusterekMalikSecenegi? value)
    {
        KadastroSecenekleri.Clear();
        foreach (var k in value is null ? [] : _kaynaklar(value.Id))
        {
            KadastroSecenekleri.Add(k);
        }

        if (KadastroKaynagi is null || !KadastroSecenekleri.Contains(KadastroKaynagi))
        {
            KadastroKaynagi = KadastroSecenekleri.Count == 1 ? KadastroSecenekleri[0] : null;
        }
    }

    partial void OnAlanMetniChanged(string value) =>
        Hata = value.Trim().Length == 0 || SayiGirdiAyristirici.TryParseDecimal(value, out _, out var h) ? null : "Alan (m²): " + h;
}

public sealed partial class HesapAdimlariViewModel
{
    private KrokiParselasyonOnizleme? _otomatikParselasyon;
    private IReadOnlyList<KrokiHakHedefi> _musterekHedefler = [];
    private bool _musterekYukleniyor;

    /// <summary>"✏️ Müşterek Dağıtımı Düzenle" paneli açık mı.</summary>
    [ObservableProperty]
    private bool _musterekDuzenlemeAcik;

    /// <summary>Düzenleme paneli durum / hata metni.</summary>
    [ObservableProperty]
    private string _musterekDurumu = string.Empty;

    /// <summary>Tüm hak sahipleri ve parseller 0.00 m² farkla kapanıyor mu (Uygula düğmesi).</summary>
    [ObservableProperty]
    private bool _musterekDengeli;

    /// <summary>Üst canlı özet şeridi (hak sahibi başına).</summary>
    public ObservableCollection<MusterekHakOzeti> MusterekHakOzetleri { get; } = [];

    /// <summary>Düzenlenebilir hissedar satırları.</summary>
    public ObservableCollection<MusterekSatirVm> MusterekSatirlari { get; } = [];

    /// <summary>Parsel bazında canlı kontrol.</summary>
    public ObservableCollection<MusterekParselKontrolu> MusterekParselKontrolleri { get; } = [];

    /// <summary>İmar parseli seçenekleri (önizleme kodları: 1/1, 1/2, 2/3…).</summary>
    public ObservableCollection<string> MusterekParselSecenekleri { get; } = [];

    /// <summary>Malik seçenekleri (önizlemenin hak sahipleri).</summary>
    public ObservableCollection<MusterekMalikSecenegi> MusterekMalikSecenekleri { get; } = [];

    /// <summary>Taslağın dağılımı elle düzenlenmiş mi.</summary>
    public bool MusterekElleDuzenlendi => _parselasyonOnizleme?.ElleDuzenlendi == true;

    private bool MusterekDuzenlenebilir() => !IsBusy && _parselasyonOnizleme is not null && _otomatikParselasyon is not null;

    /// <summary>Paneli açar / kapatır (açılışta satırlar güncel önizlemeden yüklenir).</summary>
    [RelayCommand(CanExecute = nameof(MusterekDuzenlenebilir))]
    private void MusterekDuzenle()
    {
        if (MusterekDuzenlemeAcik)
        {
            MusterekDuzenlemeAcik = false;
            return;
        }

        if (_parselasyonOnizleme is { } o)
        {
            MusterekSatirlariniYukle(o);
            MusterekDuzenlemeAcik = true;
            MusterekDurumu = "Satırları düzenleyin; her hak sahibinin ve her parselin farkı 0.00 olunca \"✅ Düzenlemeyi Önizlemeye Uygula\" deyin.";
        }
    }

    [RelayCommand(CanExecute = nameof(MusterekDuzenlenebilir))]
    private void MusterekSatirEkle()
    {
        var s = YeniSatir();
        s.ImarParseli = MusterekParselSecenekleri.FirstOrDefault();
        MusterekSatirlari.Add(s);
        MusterekDenetle();
    }

    [RelayCommand]
    private void MusterekSatirSil(MusterekSatirVm? satir)
    {
        if (!IsBusy && satir is not null && MusterekSatirlari.Remove(satir))
        {
            satir.PropertyChanged -= MusterekSatirDegisti;
            MusterekDenetle();
        }
    }

    /// <summary>Düzenlemeyi taslağa yazar: kart dağılımı, TAM / Müşterek etiketleri ve tuval etiketleri anında yenilenir (proje değişmez).</summary>
    [RelayCommand(CanExecute = nameof(MusterekDuzenlenebilir))]
    private void MusterekDuzenlemeyiUygula()
    {
        if (_otomatikParselasyon is not { } otomatik || IsBusy)
        {
            return;
        }

        var satirlar = MusterekGirdileri(out var hata);
        if (hata is not null)
        {
            MusterekDurumu = "⚠ " + hata;
            return;
        }

        var sonuc = KrokiDagilimDuzenleyici.Uygula(otomatik, _musterekHedefler, satirlar, TakbisOku());
        if (sonuc.IsFailure)
        {
            MusterekDenetle();
            MusterekDurumu = "⚠ Dağılım uygulanamadı: her parselde Σ hisse = parsel alanı ve her hak sahibinde dağıtılan = net hak olmalı (kırmızı satırları düzeltin).";
            return;
        }

        ParselasyonTaslaginiGoster(sonuc.Value, otomatik: false);
        MusterekDurumu = "✅ Düzenlenmiş dağılım önizlemeye uygulandı; projeye yazmak için \"✅ Parselasyon Çizimini Onayla ve Uygula\" deyin.";
        DurumMetni = "Müşterek / müstakil dağılım elle düzenlendi ve önizlemeye uygulandı (proje henüz değişmedi).";
    }

    /// <summary>Düzenlemeyi bırakır; <see cref="Imar18.Domain.Mevzuat.Tahsis.MustakilMusterekDagitici"/> otomatik sonucuna döner.</summary>
    [RelayCommand(CanExecute = nameof(MusterekDuzenlenebilir))]
    private void OtomatikDagitimaDon()
    {
        if (_otomatikParselasyon is not { } otomatik)
        {
            return;
        }

        if (!ReferenceEquals(_parselasyonOnizleme, otomatik))
        {
            ParselasyonTaslaginiGoster(otomatik, otomatik: false);
        }

        MusterekSatirlariniYukle(otomatik);
        MusterekDurumu = "🔄 Otomatik dağıtıma dönüldü; elle yapılan değişiklikler iptal edildi.";
        DurumMetni = "Kroki parselasyonu otomatik müstakil / müşterek dağıtıma döndü.";
    }

    /// <summary>Taslak yeni (otomatik) üretildiğinde düzenleme durumu kurulur; panel kapanır.</summary>
    private void MusterekOtomatikAyarla(KrokiParselasyonOnizleme? otomatik)
    {
        _otomatikParselasyon = otomatik;
        _musterekHedefler = otomatik is null ? [] : KrokiDagilimDuzenleyici.Hedefler(otomatik);
        MusterekDuzenlemeAcik = false;
        MusterekDurumu = string.Empty;
        MusterekParselSecenekleri.Clear();
        MusterekMalikSecenekleri.Clear();
        if (otomatik is not null)
        {
            foreach (var p in otomatik.Parseller)
            {
                MusterekParselSecenekleri.Add(p.Kod);
            }

            foreach (var m in _musterekHedefler.Select(h => h.MalikId).Distinct(StringComparer.Ordinal))
            {
                MusterekMalikSecenekleri.Add(new MusterekMalikSecenegi(m, MalikEtiketi.Ayristir(m).AdSoyad + MalikEtiketi.EkiAl(m)));
            }
        }

        MusterekSatirlariniTemizle();
        MusterekHakOzetleri.Clear();
        MusterekParselKontrolleri.Clear();
        MusterekDengeli = false;
        MusterekKomutlariniBildir();
    }

    private void MusterekSatirlariniYukle(KrokiParselasyonOnizleme o)
    {
        _musterekYukleniyor = true;
        try
        {
            MusterekSatirlariniTemizle();
            foreach (var s in o.Satirlar)
            {
                var r = YeniSatir();
                r.ImarParseli = s.ImarParselKodu;
                r.Malik = MusterekMalikSecenekleri.FirstOrDefault(m => m.Id == s.MalikKodu);
                r.KadastroKaynagi = s.KadastroParselKimligi;
                r.AlanMetni = s.AlanM2.ToString("0.00", CultureInfo.InvariantCulture);
                MusterekSatirlari.Add(r);
            }
        }
        finally
        {
            _musterekYukleniyor = false;
        }

        MusterekDenetle();
    }

    private void MusterekSatirlariniTemizle()
    {
        foreach (var s in MusterekSatirlari)
        {
            s.PropertyChanged -= MusterekSatirDegisti;
        }

        MusterekSatirlari.Clear();
    }

    private MusterekSatirVm YeniSatir()
    {
        var s = new MusterekSatirVm(m => _musterekHedefler.Where(h => h.MalikId == m).Select(h => h.KadastroParselId).ToList());
        s.PropertyChanged += MusterekSatirDegisti;
        return s;
    }

    private void MusterekSatirDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (!_musterekYukleniyor && e.PropertyName != nameof(MusterekSatirVm.Hata))
        {
            MusterekDenetle();
        }
    }

    /// <summary>Satırları okur; eksik / geçersiz hücrede ilk hatayı döndürür (satır sıra numarasıyla).</summary>
    private List<KrokiEslesmeGirdisi> MusterekGirdileri(out string? hata)
    {
        hata = null;
        var liste = new List<KrokiEslesmeGirdisi>();
        for (var i = 0; i < MusterekSatirlari.Count; i++)
        {
            var s = MusterekSatirlari[i];
            var no = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (s.ImarParseli is null || s.Malik is null || s.KadastroKaynagi is null)
            {
                hata ??= $"{no}. satırda imar parseli, malik ve kadastro kaynağını seçin.";
                continue;
            }

            if (s.Alan is not { } a || a <= 0m || decimal.Round(a, 2) != a)
            {
                hata ??= $"{no}. satırda alan pozitif ve en fazla 2 ondalıklı olmalı (m²).";
                continue;
            }

            liste.Add(new KrokiEslesmeGirdisi(s.Malik.Id, s.KadastroKaynagi, s.ImarParseli, a));
        }

        if (liste.Count == 0)
        {
            hata ??= "En az bir hissedar satırı girin.";
        }

        return liste;
    }

    /// <summary>
    /// Reviewer O3: panel açıkken satırlar gösterilen taslağın dağılımından farklıysa (önizlemeye uygulanmamış düzenleme) true — onay bu
    /// durumda reddedilir; mühendis önce "✅ Düzenlemeyi Önizlemeye Uygula" veya "🔄 Otomatik Dağıtıma Dön" demelidir.
    /// </summary>
    internal bool MusterekBekleyenDuzenlemeVar()
    {
        if (!MusterekDuzenlemeAcik || _parselasyonOnizleme is not { } o)
        {
            return false;
        }

        var satirlar = MusterekGirdileri(out var hata);
        if (hata is not null)
        {
            return true;
        }

        static IEnumerable<(string, string, string, decimal)> Kanonik(IEnumerable<KrokiEslesmeGirdisi> x) => x
            .GroupBy(s => (s.ImarParselKodu, s.MalikKodu, s.KadastroParselKimligi))
            .Select(g => (g.Key.ImarParselKodu, g.Key.MalikKodu, g.Key.KadastroParselKimligi, g.Sum(s => s.AlanM2)))
            .OrderBy(t => t.ImarParselKodu, StringComparer.Ordinal).ThenBy(t => t.MalikKodu, StringComparer.Ordinal).ThenBy(t => t.KadastroParselKimligi, StringComparer.Ordinal);
        return !Kanonik(satirlar).SequenceEqual(Kanonik(o.Satirlar));
    }

    /// <summary>Canlı özet şeridi ve parsel kontrolünü yeniler.</summary>
    private void MusterekDenetle()
    {
        if (_otomatikParselasyon is not { } otomatik)
        {
            return;
        }

        var satirlar = MusterekGirdileri(out var hata);
        var d = KrokiDagilimDuzenleyici.Denetle(otomatik, _musterekHedefler, satirlar);
        MusterekHakOzetleri.Clear();
        foreach (var (h, dagitilan) in d.Haklar)
        {
            MusterekHakOzetleri.Add(new MusterekHakOzeti(MalikEtiketi.Ayristir(h.MalikId).AdSoyad + MalikEtiketi.EkiAl(h.MalikId), h.KadastroParselId, h.NetHakM2, dagitilan));
        }

        MusterekParselKontrolleri.Clear();
        foreach (var (kod, alan, dagitilan) in d.Parseller)
        {
            MusterekParselKontrolleri.Add(new MusterekParselKontrolu(kod, alan, dagitilan));
        }

        MusterekDengeli = hata is null && d.Dengeli;
        if (MusterekDuzenlemeAcik)
        {
            MusterekDurumu = hata is not null ? "⚠ " + hata
                : d.Dengeli ? "✓ Dağılım dengeli (tüm farklar 0.00); \"✅ Düzenlemeyi Önizlemeye Uygula\" diyebilirsiniz."
                : "Kırmızı satırlarda fark var; Σ hisse = parsel alanı ve dağıtılan = net hak olmalı.";
        }
    }

    private void MusterekKomutlariniBildir()
    {
        MusterekDuzenleCommand.NotifyCanExecuteChanged();
        MusterekSatirEkleCommand.NotifyCanExecuteChanged();
        MusterekDuzenlemeyiUygulaCommand.NotifyCanExecuteChanged();
        OtomatikDagitimaDonCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(MusterekElleDuzenlendi));
    }
}
