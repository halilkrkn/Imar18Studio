using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Cad;
using Imar18.Application.Girdi;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>
/// P7.3-B1: tek webtapu belgesinin önizleme kartı (Ada/Parsel, AT m², Cilt/Sayfa, aktif malikler ve kadastro parseli eşlemesi).
/// Ad/baba adı yalnız ekranda ve yerel Malik Rehberi'nde görünür; denetim kaydına ve durum metnine yazılmaz (KVKK).
/// </summary>
public sealed partial class WebtapuOnizlemesi : ObservableObject
{
    private readonly IReadOnlyDictionary<string, decimal> _tapuAlanlari;

    internal WebtapuOnizlemesi(TapuKaydi kayit, IReadOnlyList<string> kadastroSecenekleri, IReadOnlyDictionary<string, decimal> tapuAlanlari)
    {
        Kayit = kayit;
        _tapuAlanlari = tapuAlanlari;
        KadastroSecenekleri = kadastroSecenekleri;
        var aktif = kayit.AktifMalikler;
        Baslik = string.Create(CultureInfo.InvariantCulture,
            $"{kayit.Ada}/{kayit.Parsel} · AT {kayit.AtYuzolcumM2:0.00} m² · Cilt {kayit.Cilt ?? "—"} / Sayfa {kayit.Sayfa ?? "—"} · {aktif.Count} aktif malik");
        Konum = string.Join(" / ", new[] { kayit.Il, kayit.Ilce, kayit.Mahalle }.Where(s => !string.IsNullOrWhiteSpace(s)));
        MalikSatirlari = aktif.Select(m => string.Create(CultureInfo.InvariantCulture,
            $"{m.AdSoyad}{(string.IsNullOrWhiteSpace(m.BabaAdi) ? string.Empty : " (" + m.BabaAdi + ")")} · {m.Pay}/{m.Payda} · {m.MetrekareM2:0.00} m²")).ToList();
        _seciliKadastroParsel = WebtapuAktarici.KadastroParseliOner(kayit, kadastroSecenekleri);
        UyariGuncelle();
    }

    internal TapuKaydi Kayit { get; }

    /// <summary>Ada/Parsel · AT m² · Cilt/Sayfa · aktif malik sayısı.</summary>
    public string Baslik { get; }

    /// <summary>İl / İlçe / Mahalle (varsa).</summary>
    public string Konum { get; }

    /// <summary>Aktif malikler: ad (baba adı) · hisse · m².</summary>
    public IReadOnlyList<string> MalikSatirlari { get; }

    /// <summary>Projedeki kadastro parsel kimlikleri (ComboBox).</summary>
    public IReadOnlyList<string> KadastroSecenekleri { get; }

    /// <summary>Hisselerin yazılacağı kadastro parseli (önerilen; mühendis değiştirebilir; null: seçilmedi).</summary>
    [ObservableProperty]
    private string? _seciliKadastroParsel;

    /// <summary>2. adım tapu alanı ile webtapu AT yüzölçümü uyuşmazlığı (yalnız bilgi; otomatik düzeltilmez).</summary>
    [ObservableProperty]
    private string? _uyari;

    partial void OnSeciliKadastroParselChanged(string? value) => UyariGuncelle();

    private void UyariGuncelle()
    {
        if (SeciliKadastroParsel is null)
        {
            Uyari = "Hisselerin yazılacağı kadastro parselini seçin (webtapu parsel no projede bulunamadı).";
        }
        else if (_tapuAlanlari.TryGetValue(SeciliKadastroParsel, out var tapu) && tapu != Kayit.AtYuzolcumM2)
        {
            Uyari = string.Create(CultureInfo.InvariantCulture,
                $"2. adım tapu alanı {tapu:0.00} m² ≠ webtapu AT {Kayit.AtYuzolcumM2:0.00} m²; tapu alanını kontrol edin.");
        }
        else
        {
            Uyari = null;
        }
    }
}

/// <summary>
/// P7.3-B (SAHA-006) 4. adım içe aktarımları. <b>B1</b>: webtapu PDF(ler) → önizleme → <see cref="TakbiseAktarCommand"/> (yalnız
/// TAKBİS metni + <see cref="MalikRehberiAyarlaKomutu"/>; Kroki'ye dokunmaz). <b>B2</b>: Yaklaşık Yer Krokisi (.NCZ/.DXF/.DWG) →
/// <see cref="YaklasikYerKrokisiEslestirici"/> (Kip 1 kullanım poligonu + Kip 2 hisse oranı) → önizleme → <see cref="KrokiyeAktarCommand"/>
/// (Kroki metni). Hiçbiri hak eşleştirmesini kendisi yazmaz; mühendis "Hak Eşleştirmeyi Uygula" ile projeye yazar. Okuma ve
/// eşleştirme arka planda yapılır (50 ms kuralı); beklenmeyen istisnalar katalog hatasına çevrilir (async void olay
/// işleyicisi uygulamayı çökertmez). Bayat önizleme aktarılmaz: TAKBİS sürümü ve proje belgesi önizleme anındakiyle aynı olmalı.
/// </summary>
public sealed partial class HesapAdimlariViewModel
{
    private const string KrokiYenidenYukle = "Yaklaşık Yer Krokisi'ni yeniden yükleyin.";

    private readonly CadImportService? _krokiIthal;
    private KrokiEslestirmeSonucu? _krokiSonucu;
    private ProjeBelgesi? _krokiBelgesi;
    private int _takbisSurumu;

    /// <summary>Kroki eşleştirme önizlemesi (aktarılacak Kroki metni; boş: önizleme yok).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KrokiyeAktarCommand), nameof(KrokiOnizlemesiniKaldirCommand))]
    private string _krokiOnizlemesi = string.Empty;

    /// <summary>Kroki özeti: kullanım poligonu sayısı ve toplam kontrolü (aktarımdan sonra da görünür).</summary>
    [ObservableProperty]
    private string _krokiOzeti = string.Empty;

    /// <summary>P7.3-B1: webtapu önizleme kartları (aktarım onaylanana kadar TAKBİS değişmez).</summary>
    public ObservableCollection<WebtapuOnizlemesi> WebtapuOnizlemeleri { get; } = [];

    /// <summary>P7.3-B2: eşleştirme uyarıları ve malik bazında kroki ↔ net hak farkları.</summary>
    public ObservableCollection<string> KrokiUyarilari { get; } = [];

    /// <summary>webtapu okuyucusu bağlı mı (değilse düğme pasif).</summary>
    public bool WebtapuEtkin => _alan.TapuOkuyucu is not null;

    /// <summary>Kroki içe aktarımı ve kesişim hesaplayıcısı bağlı mı (değilse düğme pasif).</summary>
    public bool KrokiNczEtkin => _krokiIthal is not null && _alan.Kesisim is not null;

    /// <summary>webtapu önizlemesi var mı (görünüm).</summary>
    public bool WebtapuOnizlemesiVar => WebtapuOnizlemeleri.Count > 0;

    /// <summary>Kroki önizlemesi var mı (görünüm).</summary>
    public bool KrokiOnizlemesiVar => KrokiOnizlemesi.Length > 0;

    /// <summary>Görünüm webtapu PDF dosya seçicisini açar (çoklu seçim) ve <see cref="WebtapuYukleAsync"/> çağırır.</summary>
    public event EventHandler? WebtapuDosyasiIstendi;

    /// <summary>Görünüm Yaklaşık Yer Krokisi dosya seçicisini açar ve <see cref="YaklasikYerKrokisiYukleAsync"/> çağırır.</summary>
    public event EventHandler? KrokiDosyasiIstendi;

    partial void OnKrokiOnizlemesiChanged(string value) => OnPropertyChanged(nameof(KrokiOnizlemesiVar));

    private bool WebtapuIstenebilir() => WebtapuEtkin && !IsBusy;

    private bool KrokiIstenebilir() => KrokiNczEtkin && !IsBusy;

    private bool WebtapuAktarilabilir() => !IsBusy && WebtapuOnizlemeleri.Count > 0;

    private bool KrokiAktarilabilir() => !IsBusy && _krokiSonucu is not null;

    [RelayCommand(CanExecute = nameof(WebtapuIstenebilir))]
    private void WebtapuIste() => WebtapuDosyasiIstendi?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(KrokiIstenebilir))]
    private void KrokiNczIste() => KrokiDosyasiIstendi?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// webtapu PDF'lerini arka planda okur ve her biri için önizleme kartı ekler; okunamayan belgenin Türkçe mesajı
    /// <see cref="HesapAdimlariViewModel.TakbisHatasi"/>'na yazılır (ERR-GIRD-003 / ERR-TAPU-003 / ERR-TAPU-004). Okuyucunun
    /// beklenmeyen istisnası ERR-GIRD-003 sayılır.
    /// </summary>
    public async Task WebtapuYukleAsync(IReadOnlyList<Stream> akislar, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(akislar);
        if (_alan.TapuOkuyucu is not { } okuyucu || akislar.Count == 0 || IsBusy)
        {
            return;
        }

        var belge = _alan.Yol.Belge;
        var tapu = belge.TecvizParselleri.GroupBy(t => t.ParselId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().TapuAlaniM2, StringComparer.Ordinal);
        var hatalar = new List<string>();
        var eklenen = 0;
        IsBusy = true;
        try
        {
            for (var i = 0; i < akislar.Count; i++)
            {
                var akis = akislar[i];
                var sonuc = await GuvenliAsync(() => okuyucu.Oku(akis, cancellationToken), ErrorCodes.GirdPdfBozuk, cancellationToken).ConfigureAwait(true);
                if (sonuc.IsFailure)
                {
                    hatalar.Add(akislar.Count > 1 ? $"{i + 1}. dosya: {sonuc.Error.UserMessage}" : sonuc.Error.UserMessage);
                    continue;
                }

                WebtapuOnizlemeleri.Add(new WebtapuOnizlemesi(sonuc.Value, KadastroKimlikleri(belge), tapu));
                eklenen++;
            }
        }
        finally
        {
            IsBusy = false;
            WebtapuDurumunuBildir();
        }

        TakbisHatasi = hatalar.Count > 0 ? string.Join(Environment.NewLine, hatalar) : null;
        DurumMetni = eklenen > 0
            ? $"{eklenen} webtapu belgesi okundu; önizlemeyi kontrol edip \"TAKBİS'e Aktar\" deyin."
            : "webtapu belgesi okunamadı.";
    }

    [RelayCommand(CanExecute = nameof(WebtapuAktarilabilir))]
    private async Task TakbiseAktarAsync()
    {
        if (WebtapuOnizlemeleri.Count == 0 || IsBusy)
        {
            return;
        }

        if (WebtapuOnizlemeleri.Any(o => o.SeciliKadastroParsel is null))
        {
            DurumMetni = TakbisHatasi = "Her webtapu belgesi için hisselerin yazılacağı kadastro parseli seçin.";
            return;
        }

        if (WebtapuOnizlemeleri.GroupBy(o => o.SeciliKadastroParsel, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            DurumMetni = TakbisHatasi = "Aynı kadastro parseline birden çok webtapu belgesi eşlendi; fazlasını kaldırın.";
            return;
        }

        // Reviewer M2: önizlemeden sonra CAD yeniden içe aktarıldıysa / proje sıfırlandıysa seçilen parsel artık olmayabilir.
        var belge = _alan.Yol.Belge;
        var guncel = KadastroKimlikleri(belge);
        if (WebtapuOnizlemeleri.FirstOrDefault(o => !guncel.Contains(o.SeciliKadastroParsel!, StringComparer.Ordinal)) is { } eski)
        {
            DurumMetni = TakbisHatasi = $"{eski.SeciliKadastroParsel} kadastro parseli projede yok; parsel seçimini düzeltin veya webtapu belgesini yeniden yükleyin.";
            return;
        }

        // Reviewer H1: TAKBİS'te elle girilmiş ve projedeki hak eşleştirmesinde kullanılan kodlar yeni kodlara verilmez.
        var ayrilmis = Satirlar(TakbisMetni).Where(s => s.Length >= 2).Select(s => s[1])
            .Concat(belge.HakEslestirme?.KadastroParselleri.SelectMany(k => k.Malikler.Select(m => m.MalikId)) ?? [])
            .Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var rehber = belge.MalikRehberi;
        var aktarimlar = new List<(string Parsel, IReadOnlyList<TakbisHisseGirdisi> Hisseler)>();
        foreach (var o in WebtapuOnizlemeleri)
        {
            var aktarim = WebtapuAktarici.Aktar(o.Kayit, o.SeciliKadastroParsel!, rehber, ayrilmis);
            rehber = aktarim.Rehber;
            aktarimlar.Add((o.SeciliKadastroParsel!, aktarim.Hisseler));
            ayrilmis.AddRange(aktarim.Hisseler.Select(h => h.MalikKodu));
        }

        if (!await GonderAsync([new MalikRehberiAyarlaKomutu(rehber)], "Malik Rehberi güncellendi.").ConfigureAwait(true))
        {
            return; // durum metni CommandBus retini gösterir; TAKBİS ve önizleme korunur
        }

        // Reviewer M3: metin gönderimden SONRA güncel TAKBİS kutusu üzerinden kurulur (bekleme sırasındaki düzenleme ezilmez).
        var metin = TakbisMetni ?? string.Empty;
        foreach (var (parsel, hisseler) in aktarimlar)
        {
            metin = WebtapuAktarici.TakbisMetniniGuncelle(metin, parsel, hisseler);
        }

        TakbisMetni = metin;
        WebtapuOnizlemeleri.Clear();
        WebtapuDurumunuBildir();
        DurumMetni = string.Create(CultureInfo.InvariantCulture,
            $"TAKBİS'e aktarıldı: {aktarimlar.Count} parsel, {aktarimlar.Sum(a => a.Hisseler.Count)} hisse (muvafakat varsayılan H). Kroki satırlarını yeni malik kodlarıyla güncelleyip \"Hak Eşleştirmeyi Uygula\" deyin.");
    }

    [RelayCommand(CanExecute = nameof(HazirMi))]
    private void WebtapuOnizlemesiniKaldir(WebtapuOnizlemesi? onizleme)
    {
        if (onizleme is not null && WebtapuOnizlemeleri.Remove(onizleme))
        {
            WebtapuDurumunuBildir();
        }
    }

    /// <summary>
    /// Yaklaşık Yer Krokisi'ni okur ve güncel TAKBİS metni + proje geometrisiyle eşleştirip önizleme üretir. TAKBİS geçersizse
    /// eşleştirme yapılmaz (<see cref="HesapAdimlariViewModel.TakbisHatasi"/>); okuma/eşleştirme hatası
    /// <see cref="HesapAdimlariViewModel.KrokiHatasi"/>'na (kesişim istisnası ERR-TOPO-008). Yükleme sırasında TAKBİS
    /// değişirse sonuç atılır.
    /// </summary>
    public async Task YaklasikYerKrokisiYukleAsync(Stream akis, string uzanti, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(akis);
        ArgumentNullException.ThrowIfNull(uzanti);
        if (_krokiIthal is not { } ithal || _alan.Kesisim is not { } kesisim || IsBusy)
        {
            return;
        }

        KrokiOnizlemesiniTemizle();
        KrokiOzeti = string.Empty;
        KrokiHatasi = null;
        var surum = _takbisSurumu;
        if (TakbisOku() is not { } takbis)
        {
            return;
        }

        if (takbis.Count == 0)
        {
            DurumMetni = TakbisHatasi = "Önce TAKBİS hisselerini girin (webtapu PDF yükleyebilirsiniz); kroki hisse oranlarıyla eşleştirilir.";
            return;
        }

        IsBusy = true;
        try
        {
            var okunan = await ithal.ImportAsync(akis, uzanti, null, cancellationToken).ConfigureAwait(true);
            if (okunan.IsFailure)
            {
                DurumMetni = KrokiHatasi = okunan.Error.UserMessage;
                return;
            }

            var belge = _alan.Yol.Belge;
            var sonuc = await GuvenliAsync(
                () =>
                {
                    var d = _alan.Onay.Degerlendir();
                    var girdi = new KrokiEslestirmeGirdisi(okunan.Value, belge.Parseller, d.HakIcinImarParselleri, takbis, belge.MalikRehberi, d.Dop?.Dopo);
                    var e = YaklasikYerKrokisiEslestirici.Eslestir(girdi, kesisim, cancellationToken);
                    return e.IsFailure
                        ? Result<(KrokiEslestirmeSonucu Sonuc, decimal ImarToplami)>.Failure(e.Error)
                        : Result<(KrokiEslestirmeSonucu Sonuc, decimal ImarToplami)>.Success((e.Value, d.HakIcinImarParselleri.Sum(p => p.AlanM2)));
                },
                ErrorCodes.TopoAnalizYapilamadi,
                cancellationToken).ConfigureAwait(true);
            if (sonuc.IsFailure)
            {
                DurumMetni = KrokiHatasi = KrokiHataMetni(sonuc.Error);
                return;
            }

            if (surum != _takbisSurumu)
            {
                DurumMetni = KrokiHatasi = "TAKBİS hisseleri yükleme sırasında değişti; " + KrokiYenidenYukle;
                return;
            }

            _krokiBelgesi = belge;
            KrokiOnizlemesiniGoster(sonuc.Value.Sonuc, sonuc.Value.ImarToplami);
            DurumMetni = "Yaklaşık Yer Krokisi eşleştirildi; önizlemeyi kontrol edip \"Kroki'ye Aktar\" deyin.";
        }
        finally
        {
            IsBusy = false;
            KrokiYoklamaDurumunuBildir();
        }
    }

    [RelayCommand(CanExecute = nameof(KrokiAktarilabilir))]
    private void KrokiyeAktar()
    {
        if (_krokiSonucu is null || IsBusy)
        {
            return;
        }

        // Reviewer M2: önizleme anındaki proje (geometri, DOPO, rehber) değiştiyse satırlar bayattır.
        if (!ReferenceEquals(_krokiBelgesi, _alan.Yol.Belge))
        {
            KrokiOnizlemesiniTemizle();
            KrokiOzeti = string.Empty;
            DurumMetni = KrokiHatasi = "Proje önizlemeden sonra değişti; " + KrokiYenidenYukle;
            return;
        }

        KrokiMetni = KrokiOnizlemesi;
        KrokiOnizlemesiniTemizle();
        DurumMetni = "Kroki satırları aktarıldı; \"Hak Eşleştirmeyi Uygula\" ile projeye yazın.";
    }

    [RelayCommand(CanExecute = nameof(KrokiAktarilabilir))]
    private void KrokiOnizlemesiniKaldir()
    {
        KrokiOnizlemesiniTemizle();
        KrokiOzeti = string.Empty;
    }

    /// <summary>TAKBİS metni değişti (sürüm artar; varsa eski hisselerle üretilmiş kroki önizlemesi ve özeti atılır).</summary>
    private void TakbisDegisti()
    {
        Interlocked.Increment(ref _takbisSurumu);
        if (_krokiSonucu is not null)
        {
            KrokiOnizlemesiniTemizle();
            KrokiOzeti = string.Empty;
        }
    }

    /// <summary>Reviewer M4: eşleştiricinin teknik nedenine Türkçe ipucu eklenir (katalog kodu ve mesajı korunur).</summary>
    private static string KrokiHataMetni(AppError hata)
    {
        var teknik = hata.TechnicalMessage ?? string.Empty;
        if (teknik.Contains(YaklasikYerKrokisiEslestirici.OrtusmeYokNedeni, StringComparison.Ordinal))
        {
            return hata.UserMessage + " Kroki proje kadastro alanıyla örtüşmüyor (CRS / koordinat ofsetini kontrol edin).";
        }

        if (teknik.Contains(YaklasikYerKrokisiEslestirici.ImarParseliYokNedeni, StringComparison.Ordinal))
        {
            return hata.UserMessage + " Projede imar parseli yok; önce Katman → Rol eşlemesiyle imar parsellerini tanımlayın.";
        }

        return hata.UserMessage;
    }

    /// <summary>İşi arka planda çalıştırır; iptal dışındaki beklenmeyen istisnayı <paramref name="yedekKod"/> katalog hatasına çevirir.</summary>
    private static async Task<Result<T>> GuvenliAsync<T>(Func<Result<T>> is_, string yedekKod, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(is_, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Reviewer H2: async void olay işleyicisine istisna sızarsa uygulama çöker; Result'a çevrilir.
        catch (Exception)
#pragma warning restore CA1031
        {
            return Result<T>.Failure(ErrorCatalog.Get(yedekKod));
        }
    }

    private static List<string> KadastroKimlikleri(ProjeBelgesi belge) =>
        belge.Parseller.Where(p => p.Rol == ParselRolu.Kadastro).Select(p => p.ParselId).Distinct(StringComparer.Ordinal).ToList();

    private void KrokiOnizlemesiniGoster(KrokiEslestirmeSonucu s, decimal imarToplami)
    {
        _krokiSonucu = s;
        var toplam = s.Satirlar.Sum(r => r.AlanM2);
        KrokiOzeti = string.Create(CultureInfo.InvariantCulture,
            $"{s.KullanimPoligonu} kullanım poligonu ({s.KullanimPoligonu - s.CozulemeyenPoligon} malike eşlendi) · {s.Satirlar.Count} satır · Σ kroki {toplam:0.00} m² {(toplam == imarToplami ? "=" : "≠")} Σ imar {imarToplami:0.00} m²");
        KrokiUyarilari.Clear();
        foreach (var u in s.Uyarilar)
        {
            KrokiUyarilari.Add(u);
        }

        foreach (var f in s.Farklar.Where(f => f.FarkM2 is { } x && x != 0m))
        {
            KrokiUyarilari.Add(string.Create(CultureInfo.InvariantCulture,
                $"{f.MalikKodu} · {f.KadastroParselId}: kroki {f.KrokiM2:0.00} m² ≠ net hak {f.NetHakM2:0.00} m² (fark {f.FarkM2:+0.00;-0.00;0.00} m²); mühendis kontrol etmeli."));
        }

        KrokiOnizlemesi = s.KrokiMetni();
        KrokiYoklamaDurumunuBildir();
    }

    private void KrokiOnizlemesiniTemizle()
    {
        _krokiSonucu = null;
        _krokiBelgesi = null;
        KrokiOnizlemesi = string.Empty;
        KrokiUyarilari.Clear();
        KrokiYoklamaDurumunuBildir();
    }

    /// <summary>Sıfırlamada içe aktarım önizlemeleri ve özetleri boşaltılır.</summary>
    private void IceAktarimiSifirla()
    {
        WebtapuOnizlemeleri.Clear();
        WebtapuDurumunuBildir();
        KrokiOnizlemesiniTemizle();
        KrokiOzeti = string.Empty;
    }

    private void WebtapuDurumunuBildir()
    {
        OnPropertyChanged(nameof(WebtapuOnizlemesiVar));
        TakbiseAktarCommand.NotifyCanExecuteChanged();
    }

    private void KrokiYoklamaDurumunuBildir()
    {
        KrokiyeAktarCommand.NotifyCanExecuteChanged();
        KrokiOnizlemesiniKaldirCommand.NotifyCanExecuteChanged();
    }
}
