using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;
using Xunit;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// P6.0 yerel doğrulama (Category=RealData, CI'da dışlanır): gerçek <c>.NCZ</c> dosyaları <b>yalnızca bellekte</b>
/// anonimleştirilir; diske hiçbir şey yazılmaz ve hata mesajlarına gerçek değer girmez (yalnız sayı/bool doğrulanır).
/// Ofset test girdisidir (gerçek fixture ofseti değildir).
/// </summary>
[Trait("Category", "RealData")]
public sealed class NczAnonimGercekVeriTests
{
    private static readonly NczAnonimlestirmeAyarlari Ayar = new(-3_000_000.000m, -200_000.000m);
    private static readonly NczFormatAdapter Adaptor = new();

    private static IEnumerable<string> Dosyalar() =>
        Directory.EnumerateFiles(GercekVeri.Kok, "*.NCZ", SearchOption.AllDirectories);

    [RealDataFact]
    public void NczAnonimGercek_Tum_Dosyalarda_Poligon_Alanlari_Birebir_Korunmali_Koseler_Tam_Kaymali()
    {
        foreach (var dosya in Dosyalar())
        {
            var bayt = File.ReadAllBytes(dosya);
            var kaynak = Adaptor.Read(new MemoryStream(bayt, writable: false)).Value;
            var sonuc = NczAnonimlestirici.Anonimlestir(bayt, Ayar);
            sonuc.IsSuccess.Should().BeTrue();
            var cikti = Adaptor.Read(new MemoryStream(sonuc.Value.Icerik, writable: false)).Value;

            cikti.Polylines.Count.Should().Be(kaynak.Polylines.Count);
            var alanFarki = 0;
            var koseFarki = 0;
            for (var i = 0; i < kaynak.Polylines.Count; i++)
            {
                var (k, c) = (kaynak.Polylines[i], cikti.Polylines[i]);
                koseFarki += k.Vertices.Zip(c.Vertices).Count(z => z.Second.X != z.First.X + Ayar.OfsetKuzeyM || z.Second.Y != z.First.Y + Ayar.OfsetDoguM);
                var kr = PolygonRing.FromClosed(k.Vertices);
                var cr = PolygonRing.FromClosed(c.Vertices);
                if (kr.IsSuccess != cr.IsSuccess
                    || (kr.IsSuccess && GaussAreaCalculator.Compute(kr.Value).AreaM2Exact != GaussAreaCalculator.Compute(cr.Value).AreaM2Exact))
                {
                    alanFarki++;
                }
            }

            koseFarki.Should().Be(0);
            alanFarki.Should().Be(0);
        }
    }

    [RealDataFact]
    public void NczAnonimGercek_XML_Degerleri_Etiketler_Ve_PNG_Ciktida_Bulunmamali()
    {
        foreach (var dosya in Dosyalar())
        {
            var bayt = File.ReadAllBytes(dosya);
            var kaynak = Adaptor.Read(new MemoryStream(bayt, writable: false)).Value;
            var sonuc = NczAnonimlestirici.Anonimlestir(bayt, Ayar).Value;
            var cikti = Adaptor.Read(new MemoryStream(sonuc.Icerik, writable: false)).Value;

            // XML alan değerleri (en az 3 karakterli) çıktıda bayt dizisi olarak bulunmamalı.
            var latin = Encoding.Latin1.GetString(bayt);
            var degerler = Regex.Matches(latin, @"<(_[A-Za-z0-9_]+)>([^<]{3,})</\1>").Select(m => m.Groups[2].Value.Trim()).Where(v => v.Length >= 3).Distinct().ToList();
            var sizanDeger = degerler.Count(v => sonuc.Icerik.AsSpan().IndexOf(Encoding.Latin1.GetBytes(v)) >= 0);
            sizanDeger.Should().Be(0);

            sonuc.PngOnizlemeSilindi.Should().BeTrue();
            sonuc.Icerik.AsSpan().IndexOf((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47]).Should().Be(-1);
            sonuc.BosaltilanXmlAlanSayisi.Should().BeGreaterThan(0);

            // Gerçek etiketler (boş olmayan) çıktı etiketleri arasında bulunmamalı.
            var gercekEtiketler = kaynak.Polylines.Select(p => p.Label).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);
            cikti.Polylines.Count(p => gercekEtiketler.Contains(p.Label)).Should().Be(0);

            // Standart dışı katman adları ve izinsiz sınıf adları (serbest metin) çıktıda bulunmamalı (reviewer H2).
            var standartDisi = kaynak.Layers.Select(l => l.Name).Where(n => !NczAnonimlestirici.StandartKatmanAdlari.Contains(n)).ToHashSet(StringComparer.Ordinal);
            cikti.Layers.Count(l => standartDisi.Contains(l.Name)).Should().Be(0);
            cikti.Polylines.Count(p => p.FeatureClass is { Length: > 0 } s && !NczAnonimlestirici.IzinliSiniflar.Contains(s)).Should().Be(0);
            cikti.Polylines.Count(p => p.DeclaredAreaM2 is not null).Should().Be(0);

            // Başlık bölgesi (kullanıcı hesabı adı) taşınmaz: katman tablosu doğrudan başlıktan sonra başlar.
            NczSentetikDosya.BaslikOku(sonuc.Icerik, 3).Should().Be(49);
        }
    }
}
