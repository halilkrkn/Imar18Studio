using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Geometry;

namespace Imar18.Presentation.Avalonia.ViewModels;

/// <summary>
/// P7 UX (kutucuklu tablolar): 4. adım TAKBİS hisseleri (<see cref="TakbisSatirlari"/>) ve Kroki/tahsis (<see cref="KrokiSatirlari"/>)
/// varsayılan olarak etkileşimli tablolarda düzenlenir. Tablolar <see cref="HesapAdimlariViewModel.TakbisMetni"/> ve
/// <see cref="HesapAdimlariViewModel.KrokiMetni"/> ile iki yönlü senkrondur: webtapu "TAKBİS'e Aktar" ve Yaklaşık Yer Krokisi
/// "Kroki'ye Aktar" metni yazdığında tablo anında dolar; hücre düzenlenince metin yeniden yazılır. Hak eşleştirme komutu metni
/// okumaya devam eder (davranış ve mevcut testler değişmez). Kadastro / imar parseli hücreleri projedeki kimlikleri önerir.
/// 2. adımda projede Kadastro Parseli rolünde poligon yoksa <see cref="KadastroParseliYok"/> yönlendirmesi gösterilir.
/// </summary>
public sealed partial class HesapAdimlariViewModel
{
    /// <summary>2. adım: projede kadastro parseli yokken gösterilen yönlendirme.</summary>
    public const string KadastroYonlendirmesi = "1. adımda en az bir katmana 'Kadastro Parseli' rolü atayıp 'Girdileri Doğrula' düğmesine basın.";

    private TabloMetinSenkronu<TakbisSatirVm>? _takbisSenkron;
    private TabloMetinSenkronu<KrokiSatirVm>? _krokiSenkron;

    /// <summary>Son değerlendirmede projede Kadastro Parseli rolünde poligon yoksa true (2. adım yönlendirmesi).</summary>
    [ObservableProperty]
    private bool _kadastroParseliYok;

    /// <summary>TAKBİS hisseleri tablosu (<see cref="TakbisMetni"/> ile iki yönlü senkron).</summary>
    public ObservableCollection<TakbisSatirVm> TakbisSatirlari { get; } = [];

    /// <summary>Kroki (tahsis) tablosu (<see cref="KrokiMetni"/> ile iki yönlü senkron).</summary>
    public ObservableCollection<KrokiSatirVm> KrokiSatirlari { get; } = [];

    /// <summary>Projedeki kadastro parseli kimlikleri (tablo hücresi önerileri).</summary>
    public ObservableCollection<string> KadastroParselOnerileri { get; } = [];

    /// <summary>Projedeki imar parseli kodları (Kroki tablosu önerileri).</summary>
    public ObservableCollection<string> ImarParselOnerileri { get; } = [];

    /// <summary>webtapu hisseleri TAKBİS'e aktarıldı (1. adım boş İl / İlçe / Mahalle doldurulur).</summary>
    public event EventHandler? TakbiseAktarildi;

    private void TablolariBagla()
    {
        _takbisSenkron = new TabloMetinSenkronu<TakbisSatirVm>(TakbisSatirlari, TakbisSatirVm.Metinden, m => TakbisMetni = m);
        _krokiSenkron = new TabloMetinSenkronu<KrokiSatirVm>(KrokiSatirlari, KrokiSatirVm.Metinden, m => KrokiMetni = m);
    }

    [RelayCommand]
    private void TakbisSatiriEkle() => TakbisSatirlari.Add(new TakbisSatirVm());

    [RelayCommand]
    private void TakbisSatiriSil(TakbisSatirVm? satir)
    {
        if (satir is not null)
        {
            TakbisSatirlari.Remove(satir);
        }
    }

    [RelayCommand]
    private void KrokiSatiriEkle() => KrokiSatirlari.Add(new KrokiSatirVm());

    [RelayCommand]
    private void KrokiSatiriSil(KrokiSatirVm? satir)
    {
        if (satir is not null)
        {
            KrokiSatirlari.Remove(satir);
        }
    }

    /// <summary>Kadastro / imar parseli önerilerini ve 2. adım yönlendirmesini güncel değerlendirmeyle yeniler.</summary>
    private void TabloOnerileriniYenile(ProjeBelgesi belge, ProjeDegerlendirmesi d)
    {
        var kadastro = d.Parseller.Where(p => p.Geometri.Rol == ParselRolu.Kadastro).Select(p => p.Geometri.ParselId)
            .Concat(belge.Parseller.Where(p => p.Rol == ParselRolu.Kadastro).Select(p => p.ParselId))
            .Distinct(StringComparer.Ordinal).ToList();
        KadastroParseliYok = kadastro.Count == 0;
        Esitle(KadastroParselOnerileri, kadastro);
        Esitle(ImarParselOnerileri, d.HakIcinImarParselleri.Select(p => p.GeciciKod).Distinct(StringComparer.Ordinal).ToList());
    }

    private static void Esitle(ObservableCollection<string> hedef, IReadOnlyList<string> kaynak)
    {
        if (hedef.SequenceEqual(kaynak, StringComparer.Ordinal))
        {
            return;
        }

        hedef.Clear();
        foreach (var k in kaynak)
        {
            hedef.Add(k);
        }
    }
}
