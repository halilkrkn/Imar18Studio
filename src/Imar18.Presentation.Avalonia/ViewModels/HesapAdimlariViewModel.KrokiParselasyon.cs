using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Girdi;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;
using Imar18.Domain.Revizyon;
using Imar18.Presentation.Avalonia.Canvas;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>
/// SAHA-015 (SORU-044): Yaklaşık Yer Krokisi → kesin alanlı otomatik parselasyon taslağı. Krokide imar bloğunun içinde 2 köşeli
/// bölme çizgisi varsa <see cref="KrokiParselasyonMotoru"/> çalışır; taslak harita tuvalinde kesikli parlak katman olarak çizilir
/// (<see cref="ParselasyonKatmani"/>) ve proje değişmez. <b>✅ Parselasyon Çizimini Onayla ve Uygula</b>
/// <see cref="KrokiParselasyonuUygulaKomutu"/>'nu (önizleme RevisionId'sine bağlı; CommandBus + audit, retler de kaydedilir) gönderir,
/// Kroki satırlarını doldurur ve DOP uygulanmışsa hak eşleştirmesini yazar; tuval uygulanmış (düz çizgili) parselasyona geçer.
/// <b>✕ Taslağı İptal Et</b> yalnız önizlemeyi kaldırır. Proje içeriği önizlemeden sonra değişirse taslak kendiliğinden kalkar
/// (reviewer M3); uygulanmış katman, proje parselleri artık o kodları taşımıyorsa kalkar ve alanları her yenilemede projeden hesaplanır.
/// SAHA-016: önizleme kartı her parselin <b>Müstakil (TAM)</b> / <b>Müşterek (pay/payda + hissedarlar)</b> dağılımını
/// (<see cref="KrokiParselDagilimi"/>), tuval etiketleri <c>TAM</c> / <c>Müşterek</c> durumunu gösterir; onaydan sonra dağılım
/// uygulanan parselasyon için kalır. SAHA-019: <b>✏️ Müşterek Dağıtımı Düzenle</b> paneliyle mühendis dağılımı elle düzenleyip
/// önizlemeye uygulayabilir (<see cref="KrokiDagilimDuzenleyici"/>); onay bu düzenlenmiş dağılımı (Kroki satırları + hak eşleştirmesi)
/// projeye yazar. <b>🔄 Otomatik Dağıtıma Dön</b> otomatik sonuca geri döner.
/// </summary>
public sealed partial class HesapAdimlariViewModel
{
    private KrokiParselasyonOnizleme? _parselasyonOnizleme;
    private RevisionId? _parselasyonRevizyonu;
    private IReadOnlyDictionary<string, (string Malik, string Durum)>? _onayliMalikler;
    private bool _onaylaniyor;

    /// <summary>SAHA-019 (reviewer O3): düzenleme panelinde önizlemeye uygulanmamış değişiklik varken onay reddi.</summary>
    public const string MusterekBekleyenDuzenlemeUyarisi =
        "Müşterek dağılım panelinde önizlemeye uygulanmamış değişiklik var; önce \"✅ Düzenlemeyi Önizlemeye Uygula\" veya \"🔄 Otomatik Dağıtıma Dön\" deyin.";

    /// <summary>SAHA-021: "Başlangıç Ada No" kutusu geçersizken gösterilen hata.</summary>
    public const string KrokiBaslangicAdaNoHatasi = "Başlangıç Ada No 1 ile 9999999 arasında bir tam sayı olmalı (varsayılan 101; ör. 201, 301).";

    /// <summary>
    /// SAHA-021: 4. adım kroki parselasyon kartındaki isteğe bağlı "Başlangıç Ada No" (varsayılan <c>101</c>). Blok etiketi boş veya
    /// <c>100</c>'den küçük sıra numarası olan bloklar bu numaradan başlayarak <c>101/1</c>, <c>102/1</c>… kodlarını alır.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KrokiBaslangicAdaNoHatali))]
    private string _krokiBaslangicAdaNo = KrokiParselasyonGirdisi.VarsayilanBaslangicAdaNo.ToString(CultureInfo.InvariantCulture);

    /// <summary>Kutucuk geçersiz mi (görünüm kırmızı çerçeve).</summary>
    public bool KrokiBaslangicAdaNoHatali => !KrokiBaslangicAdaNoOku(out _);

    /// <summary>Kutucuğu okur; boşsa varsayılan <c>101</c>.</summary>
    internal bool KrokiBaslangicAdaNoOku(out int adaNo)
    {
        var t = (KrokiBaslangicAdaNo ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            adaNo = KrokiParselasyonGirdisi.VarsayilanBaslangicAdaNo;
            return true;
        }

        return int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out adaNo)
            && adaNo is >= 1 and <= KrokiParselasyonGirdisi.EnBuyukBaslangicAdaNo;
    }

    /// <summary>Tuvalde çizilecek parselasyon katmanı (taslak veya uygulanmış; yoksa null). Ana pencere tuvale aktarır.</summary>
    [ObservableProperty]
    private ParselasyonKatmani? _parselasyonKatmani;

    /// <summary>Tuval üstü bilgi şeridi metni (taslak varken dolu).</summary>
    [ObservableProperty]
    private string _krokiParselasyonOzeti = string.Empty;

    /// <summary>SAHA-016: parsel bazında müstakil / müşterek dağılım satırları (taslak veya uygulanmış parselasyon).</summary>
    public ObservableCollection<string> KrokiParselDagilimi { get; } = [];

    /// <summary>Dağılım listesi dolu mu (görünüm).</summary>
    public bool KrokiParselDagilimiVar => KrokiParselDagilimi.Count > 0;

    /// <summary>Onay bekleyen parselasyon taslağı var mı (görünüm: Onayla / İptal düğmeleri).</summary>
    public bool KrokiParselasyonTaslagiVar => _parselasyonOnizleme is not null;

    /// <summary>Eski (Kip 1/2) Kroki satırı önizlemesi var mı (görünüm: "Kroki'ye Aktar" / "Vazgeç").</summary>
    public bool KrokiEslestirmeOnizlemesiVar => _krokiSonucu is not null;

    /// <summary>Güncel taslak (testler ve görünüm).</summary>
    public KrokiParselasyonOnizleme? KrokiParselasyonOnizlemesi => _parselasyonOnizleme;

    /// <summary>Bu oturumda onaylanıp projede hâlâ duran kroki parselasyonunun parsel sayısı (yoksa 0; architect K1 uyarıları için).</summary>
    public int UygulananKrokiParselSayisi => _onayliMalikler?.Count ?? 0;

    private bool ParselasyonOnaylanabilir() => !IsBusy && _parselasyonOnizleme is not null;

    [RelayCommand(CanExecute = nameof(ParselasyonOnaylanabilir))]
    private async Task ParselasyonCiziminiOnaylaAsync()
    {
        if (_parselasyonOnizleme is not { } o || _parselasyonRevizyonu is not { } revizyon || IsBusy)
        {
            return;
        }

        if (MusterekBekleyenDuzenlemeVar())
        {
            DurumMetni = KrokiHatasi = MusterekBekleyenDuzenlemeUyarisi;
            MusterekDurumu = "⚠ " + MusterekBekleyenDuzenlemeUyarisi;
            return;
        }

        var bayat = _alan.Yol.Belge.RevizyonKimligi() != revizyon;
        bool yazildi;
        _onaylaniyor = true;
        try
        {
            // Bayat da olsa komut gönderilir: CommandBus reddeder ve reddi denetime yazar (reviewer M1).
            yazildi = await GonderAsync([new KrokiParselasyonuUygulaKomutu(o, revizyon)], "Parselasyon çizimi projeye yazıldı.").ConfigureAwait(true);
        }
        finally
        {
            _onaylaniyor = false;
        }

        if (!yazildi)
        {
            if (bayat)
            {
                KrokiOnizlemesiniTemizle();
                KrokiOzeti = string.Empty;
                DurumMetni = KrokiHatasi = "Proje önizlemeden sonra değişti; " + KrokiYenidenYukle;
            }
            else
            {
                KrokiHatasi = DurumMetni; // CommandBus reddi (ör. Deprem Sonrası alan sapması); taslak korunur
            }

            return;
        }

        _onayliMalikler = o.Parseller.ToDictionary(p => p.Kod, p => (p.MalikGorunumu(), p.MulkiyetDurumu()), StringComparer.Ordinal);
        var metin = o.KrokiMetni();
        KrokiOnizlemesiniTemizle();
        DagilimiGoster(o.DagilimSatirlari());
        OnayliParselasyonuDenetle(_alan.Yol.Belge);
        KrokiOzeti = "✅ Uygulanan kroki parselasyonu: " + o.Ozet();
        KrokiMetni = metin;
        var durum = string.Create(CultureInfo.InvariantCulture,
            $"✅ Parselasyon çizimi onaylandı ve uygulandı: {o.Parseller.Count} kesin parsel projenin TK_Oluşan parselleri oldu ({o.MustakilSayisi} müstakil (TAM), {o.MusterekSayisi} müşterek (pay/payda)); Kroki satırları dolduruldu.");
        if (Degerlendirme?.Dop is not null)
        {
            await HakUygulaAsync().ConfigureAwait(true);
            durum += " " + DurumMetni;
        }
        else
        {
            durum += " Hak eşleştirmesi için 3. adımda DOP'u uygulayıp \"Hak Eşleştirmeyi Uygula\" deyin.";
        }

        DurumMetni = durum;
    }

    [RelayCommand(CanExecute = nameof(ParselasyonOnaylanabilir))]
    private void ParselasyonTaslaginiIptalEt()
    {
        KrokiOnizlemesiniTemizle();
        KrokiOzeti = string.Empty;
        DurumMetni = "Parselasyon taslağı iptal edildi; proje geometrisi değişmedi.";
    }

    /// <summary>
    /// Taslağı önizleme kartına, uyarılara ve tuval katmanına yükler (proje değişmez). <paramref name="otomatik"/>: motorun yeni ürettiği
    /// taslak (SAHA-019 düzenleme durumu bu taslaktan kurulur); false ise elle düzenlenmiş / otomatiğe dönülen taslak gösterilir.
    /// </summary>
    private void ParselasyonTaslaginiGoster(KrokiParselasyonOnizleme o, bool otomatik = true)
    {
        _parselasyonOnizleme = o;
        if (otomatik)
        {
            MusterekOtomatikAyarla(o);
        }

        KrokiOzeti = "📐 Parselasyon taslağı: " + o.Ozet();
        KrokiUyarilari.Clear();
        foreach (var u in o.Uyarilar)
        {
            KrokiUyarilari.Add(u);
        }

        foreach (var f in o.Farklar.Where(f => f.FarkM2 is { } x && x != 0m))
        {
            KrokiUyarilari.Add(string.Create(CultureInfo.InvariantCulture,
                $"{f.MalikKodu} · {f.KadastroParselId}: parsel {f.KrokiM2:0.00} m² ≠ net hak {f.NetHakM2:0.00} m² (fark {f.FarkM2:+0.00;-0.00;0.00} m²); mühendis kontrol etmeli."));
        }

        KrokiOnizlemesi = o.KrokiMetni();
        DagilimiGoster(o.DagilimSatirlari());
        ParselasyonKatmani = new ParselasyonKatmani(o.Parseller.Select(p => new ParselasyonCizimParseli(p.Kod, p.Koseler, p.Etiket())).ToList(), Taslak: true);
        KrokiParselasyonOzeti = string.Create(CultureInfo.InvariantCulture,
            $"📐 Kroki parselasyon taslağı · {o.Ozet()} — kontrol edip onaylayın");
        KrokiYoklamaDurumunuBildir();
    }

    /// <summary>Taslağı kaldırır; varsa uygulanmış katman geri gelir.</summary>
    private void ParselasyonTaslaginiTemizle()
    {
        if (_parselasyonOnizleme is null && ParselasyonKatmani is not { Taslak: true })
        {
            return;
        }

        _parselasyonOnizleme = null;
        _parselasyonRevizyonu = null;
        MusterekOtomatikAyarla(null);
        KrokiParselasyonOzeti = string.Empty;
        ParselasyonKatmani = null;
        DagilimiGoster([]);
        OnayliParselasyonuDenetle(_alan.Yol.Belge);
    }

    /// <summary>Kart dağılım listesini yeniler.</summary>
    private void DagilimiGoster(IReadOnlyList<string> satirlar)
    {
        KrokiParselDagilimi.Clear();
        foreach (var d in satirlar)
        {
            KrokiParselDagilimi.Add(d);
        }

        OnPropertyChanged(nameof(KrokiParselDagilimiVar));
    }

    /// <summary>
    /// <see cref="YenileAsync"/> kancası: proje içeriği taslaktan sonra değiştiyse (DOP, TAKBİS dışı girdi, geometri, künye…) bayat taslak
    /// kalkar (reviewer M3; onay sürerken değil); ardından uygulanmış katman projeden yenilenir.
    /// </summary>
    private void ParselasyonDurumunuYenile(ProjeBelgesi belge)
    {
        if (!_onaylaniyor && _parselasyonOnizleme is not null && _parselasyonRevizyonu is { } r && belge.RevizyonKimligi() != r)
        {
            KrokiOnizlemesiniTemizle();
            KrokiOzeti = string.Empty;
            KrokiHatasi = "Proje değişti; kroki parselasyon taslağı kaldırıldı. " + KrokiYenidenYukle;
        }

        OnayliParselasyonuDenetle(belge);
    }

    /// <summary>
    /// Uygulanmış parselasyon katmanını güncel proje parsellerinden kurar (geometri ve alan her zaman projeden — reviewer L1); kodlardan
    /// biri artık projede imar parseli değilse katman ve onay kaydı kalkar. Taslak varken dokunmaz.
    /// </summary>
    private void OnayliParselasyonuDenetle(ProjeBelgesi belge)
    {
        if (_onayliMalikler is null || _parselasyonOnizleme is not null)
        {
            return;
        }

        var parseller = _onayliMalikler.Keys
            .Select(k => belge.Parseller.FirstOrDefault(p => p.Rol == ParselRolu.Imar && string.Equals(p.ParselId, k, StringComparison.Ordinal)))
            .ToList();
        if (parseller.Any(p => p is null || PolygonRing.FromOpen(p.Koseler).IsFailure))
        {
            _onayliMalikler = null;
            ParselasyonKatmani = null;
            DagilimiGoster([]);
            OnPropertyChanged(nameof(UygulananKrokiParselSayisi));
            return;
        }

        // Reviewer O5: projede hak eşleştirmesi varsa TAM / Müşterek durumu ve kart dağılımı onay anının kopyasından değil, güncel
        // eşleştirme satırlarından türetilir (sonradan Kroki/geometri değişirse bayat kalmaz); yoksa onay anındaki dağılım kalır.
        var eslestirme = belge.HakEslestirme?.Eslestirme;
        var cizim = new List<ParselasyonCizimParseli>(parseller.Count);
        var dagilim = new List<string>(parseller.Count);
        foreach (var p in parseller)
        {
            var alan = GaussAreaCalculator.Compute(PolygonRing.FromOpen(p!.Koseler).Value).AreaM2;
            var (malik, durum) = _onayliMalikler[p.ParselId];
            if (eslestirme is not null)
            {
                var paydaslar = eslestirme.Where(e => string.Equals(e.ImarParselKodu, p.ParselId, StringComparison.Ordinal))
                    .GroupBy(e => e.MalikId, StringComparer.Ordinal)
                    .Select(g => (Malik: g.Key, Dm2: g.Sum(e => e.AlanM2) * 100m))
                    .ToList();
                durum = paydaslar.Count switch { 0 => string.Empty, 1 => "TAM", _ => "Müşterek" };
                malik = paydaslar.Count == 0 ? malik : MalikEtiketi.Ayristir(paydaslar[0].Malik).AdSoyad
                    + (paydaslar.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $" +{paydaslar.Count - 1}") : string.Empty);
                dagilim.Add(string.Create(CultureInfo.InvariantCulture, $"{p.ParselId} • {alan:0.00} m² • ") + (paydaslar.Count switch
                {
                    0 => "Hak eşleştirmesinde satır yok",
                    1 => "Müstakil (TAM)",
                    _ => string.Create(CultureInfo.InvariantCulture,
                        $"Müşterek ({paydaslar.Count} hissedar: {string.Join(", ", paydaslar.Select(x => string.Create(CultureInfo.InvariantCulture, $"{MalikEtiketi.Ayristir(x.Malik).AdSoyad} {x.Dm2:0}/{alan * 100m:0}")))})"),
                }));
            }

            cizim.Add(new ParselasyonCizimParseli(p.ParselId, p.Koseler, KrokiParseli.Etiket(p.ParselId, malik, alan, durum)));
        }

        ParselasyonKatmani = new ParselasyonKatmani(cizim, Taslak: false);
        if (eslestirme is not null)
        {
            DagilimiGoster(dagilim);
        }

        OnPropertyChanged(nameof(UygulananKrokiParselSayisi));
    }

    /// <summary>Proje sıfırlanınca uygulanmış katman da bırakılır.</summary>
    private void ParselasyonuSifirla()
    {
        _parselasyonOnizleme = null;
        _parselasyonRevizyonu = null;
        _onayliMalikler = null;
        MusterekOtomatikAyarla(null);
        ParselasyonKatmani = null;
        KrokiParselasyonOzeti = string.Empty;
        DagilimiGoster([]);
        OnPropertyChanged(nameof(UygulananKrokiParselSayisi));
    }
}
