using System.Buffers.Binary;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Geometry;
using Imar18.Domain.Numerics;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;
using static Imar18.Infrastructure.Tests.Cad.Ncz.NczParselasyonYaziciTests;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// P7.3-C3 yerel gerçek veri round-trip (Category=RealData, CI'da dışlanır): üç projenin gerçek <c>PARSELASYON HARİTASI</c>
/// dosyası <b>yalnızca bellekte</b> şablon olarak kullanılır; dosyadaki imar parselleri hesap motoru çıktısı gibi (Decimal Gauss
/// alanıyla) yeniden yazılır, sonuç <see cref="NczFormatAdapter"/> ile geri okunur. Diske yazılmaz; koda dosya adı, etiket veya
/// koordinat girmez (AC-PILOT-08). Klasör yoksa testler atlanır.
/// </summary>
[Trait("Category", "RealData")]
public sealed class NczParselasyonYaziciGercekVeriTests
{
    private static readonly NczFormatAdapter Adaptor = new();

    public static TheoryData<string> Projeler => new() { "proje-1-", "proje-2-", "proje-3-" };

    private static CadLayerDocument Oku(byte[] d)
    {
        using var s = new MemoryStream(d, writable: false);
        var r = Adaptor.Read(s);
        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        return r.Value;
    }

    /// <summary>İmar parsellerinin katmanı: TK_PARSEL_OLUŞAN kapalı poligon taşıyorsa o, yoksa proje-2 düzenindeki @SM (P4.0 F7).</summary>
    private static string ImarKatmani(CadLayerDocument b) =>
        b.Polylines.Any(p => p.LayerName == NczParselasyonYazici.OlusanKatmani && p.IsClosed) ? NczParselasyonYazici.OlusanKatmani : "@SM";

    private static decimal Gauss(IReadOnlyList<GeodeticPoint> kapali) => GaussAreaCalculator.Compute(PolygonRing.FromClosed(kapali).Value).AreaM2;

    /// <summary>Gerçek dosyadan hesap motoru çıktısı benzeri paket: imar parselleri Decimal Gauss alanıyla, köşeler numaralı.</summary>
    private static (CiktiPaketi Paket, List<CadPolyline> Imar) Paket(byte[] sablon, CadLayerDocument kaynak)
    {
        var imar = kaynak.Polylines.Where(p => p.LayerName == ImarKatmani(kaynak) && p.IsClosed && p.Vertices.Count >= 4).ToList();
        var noktalar = new Dictionary<GeodeticPoint, int>();
        var koordinatlar = new List<KoordinatSatiri>();
        var satirlar = new List<ParselCetvelSatiri>();
        for (var i = 0; i < imar.Count; i++)
        {
            var koseler = imar[i].Vertices.Take(imar[i].Vertices.Count - 1).ToArray();
            var nolar = koseler.Select(k =>
            {
                if (!noktalar.TryGetValue(k, out var no))
                {
                    no = noktalar.Count + 1;
                    noktalar.Add(k, no);
                    koordinatlar.Add(new KoordinatSatiri(no, k.Y, k.X));
                }

                return no;
            }).ToArray();
            var alan = Gauss(imar[i].Vertices);
            var kod = "G" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            satirlar.Add(new ParselCetvelSatiri(kod, "900/" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), ParselRolu.Imar,
                nolar, [], alan, alan, HeM2Dm2.Ayir(alan), koseler));
        }

        var paket = new CiktiPaketi("Yerel Gerçek Veri Round-Trip", new string('a', 64), "muhendis-01", new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero),
            PilotDamgasi.Metin, PilotDamgasi.FenKlasoruSablonSurumu, "ITRF96 TM3", 1000, koordinatlar, satirlar, null,
            new DopOzeti(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m), [], [], [])
        {
            NczSablonu = NczSablonu.Olustur(sablon).Value,
        };
        return (paket, imar);
    }

    [RealDataTheory]
    [MemberData(nameof(Projeler))]
    public void NczYaziciGercek_Sablonlu_Cikti_Hatasiz_Okunmali_Imar_Koseleri_Ve_Alanlari_Birebir_Tutmali(string onek)
    {
        var sablon = File.ReadAllBytes(GercekVeri.ParselasyonDosyasi(onek));
        var kaynak = Oku(sablon);
        var (paket, imar) = Paket(sablon, kaynak);
        imar.Should().NotBeEmpty();

        var r = NczParselasyonYazici.Yaz(paket);
        r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.TechnicalMessage : string.Empty);
        var cikti = Oku(r.Value);

        var yeni = cikti.Polylines.Where(p => p.LayerName == NczParselasyonYazici.OlusanKatmani).ToList();
        yeni.Should().HaveCount(imar.Count, "imar parsel sayısı birebir");
        for (var i = 0; i < imar.Count; i++)
        {
            yeni[i].Label.Should().Be(paket.Parseller[i].TescilNo);
            yeni[i].Vertices.Should().Equal(imar[i].Vertices.Select(v => v with { }), "köşeler 6 ondalıkta birebir (≤ 1 mm)");
            Gauss(yeni[i].Vertices).Should().Be(paket.Parseller[i].HesapAlaniM2);
            yeni[i].DeclaredAreaM2.Should().Be(paket.Parseller[i].DengelenmisAlanM2);
        }

        var imarKatmani = ImarKatmani(kaynak);
        cikti.Polylines.Where(p => p.LayerName != NczParselasyonYazici.OlusanKatmani && p.LayerName != imarKatmani).Should()
            .HaveCount(kaynak.Polylines.Count(p => p.LayerName != NczParselasyonYazici.OlusanKatmani && p.LayerName != imarKatmani),
                "kadastro ve diğer şablon poligonları aynen kopyalanır");
        cikti.Polylines.Where(p => p.LayerName == imarKatmani && imarKatmani != NczParselasyonYazici.OlusanKatmani && p.IsClosed && p.Vertices.Count >= 4)
            .Should().BeEmpty("şablondaki eski imar poligonları yeniden yazıldığı için düşer");
    }

    [RealDataTheory]
    [MemberData(nameof(Projeler))]
    public void NczYaziciGercek_Sablon_Baslik_Bolgesi_XML_Ve_Ofsetler_Tutarli_Olmali(string onek)
    {
        var sablon = File.ReadAllBytes(GercekVeri.ParselasyonDosyasi(onek));
        var (paket, _) = Paket(sablon, Oku(sablon));

        var d = NczParselasyonYazici.Yaz(paket).Value;

        H(d, 1).Should().Be(d.Length);
        H(d, 3).Should().Be(H(sablon, 3));
        H(d, 4).Should().Be(H(sablon, 4), "PNG sonu katman tablosundan öncedir, kaymaz");
        H(d, 9).Should().Be(H(sablon, 9));
        var eklenen = (Katmanlar(d).Count - Katmanlar(sablon).Count) * 29;
        Katmanlar(d).Take(Katmanlar(sablon).Count).Should().Equal(Katmanlar(sablon));
        H(d, 7).Should().Be(H(sablon, 7) + eklenen);
        H(d, 8).Should().Be(H(sablon, 8) + eklenen);
        H(d, 10).Should().Be(H(sablon, 10) + eklenen);
        H(d, 11).Should().Be(H(sablon, 11) + eklenen);
        (H(d, 8) - H(d, 7)).Should().Be(89, "spike: h[8] = h[7] + 89");
        d.AsSpan(85, H(sablon, 3) - 85).SequenceEqual(sablon.AsSpan(85, H(sablon, 3) - 85)).Should().BeTrue("başlık bölgesi (PNG, çizgi tipleri) aynen korunur");
        var ara = H(sablon, 7) + 4;
        d.AsSpan(H(d, 7) + 4, H(sablon, 11) - ara).SequenceEqual(sablon.AsSpan(ara, H(sablon, 11) - ara)).Should().BeTrue("font/sembol/XML bölgesi aynen korunur");
        var varliklar = Kayitlar(d).Where(Varlik).ToList();
        BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(81)).Should().Be(varliklar.Count - 1);
        Kayitlar(d)[^1].Yuk[0].Should().Be(14);
        Oku(d).UnrecognizedRecordCount.Should().Be(Oku(sablon).UnrecognizedRecordCount);
    }
}

/// <summary>Yerel <c>test-data/real/</c> klasörü yoksa testi atlayan <see cref="TheoryAttribute"/>.</summary>
public sealed class RealDataTheoryAttribute : TheoryAttribute
{
    public RealDataTheoryAttribute()
    {
        if (!GercekVeri.VarMi)
        {
            Skip = "test-data/real/ yerel gercek veri klasoru yok (CI veya baska makine).";
        }
    }
}
