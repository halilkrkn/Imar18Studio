using System.Globalization;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Girdi;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.GeometryEngine.Subdivision;
using Imar18.Infrastructure.Cad.Donusum;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// SAHA-015 yerel doğrulama (Category=RealData, CI'da dışlanır): <c>test-data/real/kroki/</c> altındaki gerçek Yaklaşık Yer Krokisi
/// <c>.NCZ</c>'si okunur ve kesin alanlı parselasyona dönüştürülür. Gerçek ad, ada/parsel no veya koordinat koda yazılmaz ve
/// iddia mesajlarına girmez (yalnız sayım/bool/alan farkı doğrulanır). TAKBİS çalışma anında kroki yazılarından türetilir
/// (her yazı bir malik, eşit hisse; baba adı sentetik) — test yalnız geometri/eşleme zincirini doğrular, gerçek hak değildir.
/// </summary>
[Trait("Category", "RealData")]
public sealed class Saha015KrokiGercekVeriTests
{
    private static readonly NczFormatAdapter Adaptor = new();

    private static string? KrokiDosyasi() =>
        Directory.Exists(Path.Combine(GercekVeri.Kok, "kroki"))
            ? Directory.EnumerateFiles(Path.Combine(GercekVeri.Kok, "kroki"), "*.NCZ").FirstOrDefault()
            : null;

    private static CadLayerDocument Oku()
    {
        var dosya = KrokiDosyasi();
        dosya.Should().NotBeNull("yerel test-data/real/kroki/ altında Yaklaşık Yer Krokisi .NCZ bulunmalı");
        using var akis = File.OpenRead(dosya!);
        var sonuc = Adaptor.Read(akis);
        sonuc.IsSuccess.Should().BeTrue();
        return sonuc.Value;
    }

    private static CadLayerRoleMap Roller(CadLayerDocument belge) =>
        belge.Layers.Aggregate(CadLayerRoleMap.Standard, (m, l) => m.WithAssignment(l.Name, CadLayerRoleMap.OtomatikRol(l.Name)));

    private static KrokiParselasyonOnizleme Parselle(CadLayerDocument kroki)
    {
        var parseller = ProjeGeometriEslemesi.Olustur(kroki, Roller(kroki)).Parseller;
        var kadastro = parseller.Where(p => p.Rol == ParselRolu.Kadastro).ToList();
        kadastro.Should().HaveCount(1);
        decimal Alan(ParselGeometrisi p) => GaussAreaCalculator.Compute(PolygonRing.FromOpen(p.Koseler).Value).AreaM2;
        var r1 = Alan(kadastro[0]);
        var r2 = parseller.Where(p => p.Rol == ParselRolu.Imar).Sum(Alan);

        // Çalışma anında sentetik TAKBİS: her kroki yazısı ayrı malik, eşit hisse.
        var yazilar = kroki.Texts.Select(t => t.Text).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var takbis = new KadastroParseli(kadastro[0].ParselId, r1,
            yazilar.Select(y => new KadastroMaliki(MalikEtiketi.Olustur(y, "SENTETİK"), new Fraction(1, yazilar.Count), false)).ToList());
        var dopo = decimal.Round((r1 - r2) / r1, 7, MidpointRounding.AwayFromZero);

        var s = KrokiParselasyonMotoru.Olustur(new KrokiParselasyonGirdisi(kroki, parseller, [takbis], MalikRehberi.Bos, dopo), new KrokiBlokBolucu());
        s.IsSuccess.Should().BeTrue(s.IsFailure ? s.Error.Code : string.Empty);
        return s.Value;
    }

    private static void KesinParselasyonuDogrula(KrokiParselasyonOnizleme o)
    {
        o.Parseller.Count.Should().Be(8);
        o.BolunenBloklar.Count.Should().Be(2);
        o.Parseller.GroupBy(p => p.BlokKimligi).Select(g => g.Count()).Order().Should().Equal(3, 5);
        o.Parseller.Count(p => p.Malikler.Count != 1).Should().Be(0, "her parçada tam bir malik yazısı vardır");
        o.Parseller.SelectMany(p => p.Malikler).Distinct(StringComparer.Ordinal).Count().Should().Be(8, "8 farklı malik 8 parsele düşer");
        o.Parseller.Count(p => Math.Abs(GaussAreaCalculator.Compute(PolygonRing.FromOpen(p.Koseler).Value).AreaM2Exact - p.HedefAlanM2) > 0.01m)
            .Should().Be(0, "her parselin Gauss alanı hedefe ≤ 0.01 m² eşittir");
        o.Parseller.Count(p => !System.Text.RegularExpressions.Regex.IsMatch(p.Kod, "^.+/[1-5]$")).Should().Be(0);
        o.Satirlar.Count.Should().Be(8);
        o.Satirlar.Sum(s => s.AlanM2).Should().Be(o.ToplamAlanM2);
    }

    [RealDataFact]
    public void KrokiGercek_2_TK_OLUSAN_Blogu_6_Acik_Bolme_Cizgisi_Ve_8_Yazi_Okunmali()
    {
        var b = Oku();

        b.Polylines.Count(p => p.IsClosed && CadLayerRoleMap.OtomatikRol(p.LayerName) == CadLayerRole.ZoningParcel).Should().Be(2);
        b.Polylines.Count(p => !p.IsClosed && p.Vertices.Count == 2).Should().Be(6);
        b.Texts.Count.Should().Be(8);
        b.Texts.Count(t => t.Merkez is null || t.Text.Length == 0).Should().Be(0);
        b.SkippedEntityCount.Should().Be(1, "yalnız son tip 14 kaydı atlanır");
    }

    [RealDataFact]
    public void KrokiGercek_8_Kesin_Alanli_Parsel_Uretilmeli()
    {
        KesinParselasyonuDogrula(Parselle(Oku()));
    }

    [RealDataFact]
    public void KrokiGercek_Aktif_DNS_Ile_Donusturulunce_De_8_Kesin_Alanli_Parsel_Uretilmeli()
    {
        var dns = Directory.EnumerateFiles(Path.Combine(GercekVeri.Kok, "donusum"), "*.dns").FirstOrDefault();
        dns.Should().NotBeNull();
        using var akis = File.OpenRead(dns!);
        var k = new NetcadDnsOkuyucu().Oku(akis).Value.Katsayilar;

        var o = Parselle(CadBelgesiDonusumu.Uygula(Oku(), k));

        KesinParselasyonuDogrula(o);
        o.ToplamAlanM2.ToString(CultureInfo.InvariantCulture).Should().NotBeNullOrEmpty();
    }
}
