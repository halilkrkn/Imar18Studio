using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;

namespace Imar18.Domain.Revizyon;

/// <summary>
/// P3 hesap revizyonu (D16, AC-PILOT-06): tecviz girdileri (sınır, ölçek, yöntem, kalite, kaynak notu, toplam sınır),
/// DOP girdisi (R1, D1, R2, D2, R3, ortak katılım) ve hak eşleştirme girdisinin tek SHA-256 kimliği.
/// Bunlardan herhangi biri değişirse kimlik değişir ve <see cref="MuhendisOnayi"/> geçersizleşir.
/// Geometri (köşe koordinatları) ve CRS P4/P5'te aynı oluşturucuya eklenecektir.
/// </summary>
public static class HesapRevizyonu
{
    public static RevisionId Hesapla(
        HakEslestirmeGirdisi hakEslestirme,
        DopGirdisi dop,
        IReadOnlyCollection<TecvizParselGirdisi> tecvizParselleri,
        decimal? duzenlemeToplamTecvizSiniriM2)
    {
        ArgumentNullException.ThrowIfNull(hakEslestirme);
        ArgumentNullException.ThrowIfNull(dop);
        ArgumentNullException.ThrowIfNull(tecvizParselleri);
        if (tecvizParselleri.Any(t => t is null))
        {
            throw new ArgumentException("Tecviz parsel girdisi null olamaz.", nameof(tecvizParselleri));
        }

        var o = new RevizyonOzetiOlusturucu().Ekle("tur", "p3-hesap");

        o.Ekle("dop.r1", dop.R1).Ekle("dop.d1", dop.D1).Ekle("dop.r2", dop.R2).Ekle("dop.d2", dop.D2)
            .Ekle("dop.r3", dop.R3).Ekle("dop.ortak", dop.OrtakKatilimM2);

        var tecviz = tecvizParselleri
            .Select(t => (Parsel: t, Anahtar: t.Tecviz is null
                ? "-"
                : string.Join(
                    '\u001F',
                    t.Tecviz.OlcekPaydaM.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    t.Tecviz.UretimYontemi,
                    t.Tecviz.KoordinatKalite.ToString(),
                    Numerics.Fraction.FromDecimal(t.Tecviz.TecvizSiniriM2).ToString(),
                    t.Tecviz.KaynakNotu)))
            .OrderBy(x => x.Parsel.ParselId, StringComparer.Ordinal)
            .ThenBy(x => x.Parsel.TapuAlaniM2)
            .ThenBy(x => x.Parsel.HesapAlaniM2)
            .ThenBy(x => x.Anahtar, StringComparer.Ordinal)
            .Select(x => x.Parsel)
            .ToArray();
        o.Ekle("tecvizSayisi", tecviz.Length);
        foreach (var t in tecviz)
        {
            o.Ekle("t.id", t.ParselId).Ekle("t.tapu", t.TapuAlaniM2).Ekle("t.hesap", t.HesapAlaniM2).Ekle("t.girdiVar", t.Tecviz is not null);
            if (t.Tecviz is { } g)
            {
                o.Ekle("t.olcek", g.OlcekPaydaM).Ekle("t.yontem", g.UretimYontemi).Ekle("t.kalite", g.KoordinatKalite.ToString())
                    .Ekle("t.sinir", g.TecvizSiniriM2).Ekle("t.kaynak", g.KaynakNotu);
            }
        }

        o.Ekle("t.toplamVar", duzenlemeToplamTecvizSiniriM2.HasValue);
        if (duzenlemeToplamTecvizSiniriM2 is { } toplam)
        {
            o.Ekle("t.toplam", toplam);
        }

        hakEslestirme.Yaz(o);
        return o.Olustur();
    }
}
