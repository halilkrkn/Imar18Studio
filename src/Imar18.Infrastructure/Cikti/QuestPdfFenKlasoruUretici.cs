using System.Globalization;
using System.Text;
using Imar18.Application.Cikti;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Numerics;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Imar18.Infrastructure.Cikti;

/// <summary>
/// QuestPDF taslak Fen Klasörü üreticisi (P5.3, spec 25.6/25.9, D31 Kural 5/6/9). Bölümler:
/// <list type="number">
/// <item>FR.348 (EK-2) değişiklik tasarımı kapak sayfası ve içindekiler (taslak);</item>
/// <item>Fen Klasörü alan tablosu — <c>He. | M2 | Dm2</c> üç sütunlu yüzölçümü;</item>
/// <item>Tescil sayfası — imar parseline göre dağıtım, <c>TAM</c> veya dm² paydalı hisse;</item>
/// <item>DOPO / OKO özeti ve dengeleme kutusu (Tapu / Hesaplanan / Fark / Tecviz);</item>
/// <item>FR.521 (EK-17) rölöve ölçü krokisi (SVG; poligonlar, tescil no ve cephe uzunlukları) ve koordine özet.</item>
/// </list>
/// Her sayfada üst bilgi, ön plan filigranı ve alt bilgide <see cref="PilotDamgasi.Metin"/> ile RevisionId bulunur
/// (AC-PILOT-07). Belge meta verisindeki tarih onay zamanıdır. Şablonlar resmî TKGM örnekleriyle birebir doğrulanmadı
/// (taslak, STATUS SORU-034). Koordinat/ölçek hesapları decimal'dir; <c>float</c> yalnızca QuestPDF yerleşim API'sidir.
/// QuestPDF Community lisansı kullanılır (proje sahibi teyidi SORU-034).
/// </summary>
public sealed class QuestPdfFenKlasoruUretici : ICiktiUretici
{
    static QuestPdfFenKlasoruUretici() => QuestPDF.Settings.License = LicenseType.Community;

    public CiktiTuru Tur => CiktiTuru.FenKlasoru;

    public string Uzanti => ".pdf";

    public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paket);
        try
        {
            var belge = Document.Create(c =>
            {
                Sayfa(c, paket, "FR.348 (EK-2) — DEĞİŞİKLİK TASARIMI KAPAK SAYFASI (TASLAK)", x => Kapak(x, paket));
                Sayfa(c, paket, "FEN KLASÖRÜ — PARSEL ALAN TABLOSU (He. | M2 | Dm2)", x => AlanTablosu(x, paket));
                Sayfa(c, paket, "TESCİL SAYFASI — TESCİLE ESAS DAĞITIM (İmar parseline göre)", x => TescilSayfasi(x, paket));
                Sayfa(c, paket, "DOPO / OKO ÖZET FORMU VE DENGELEME", x => DopSayfasi(x, paket));
                Sayfa(c, paket, "FR.521 (EK-17) — RÖLÖVE ÖLÇÜ KROKİSİ (TASLAK)", x => Kroki(x, paket));
            });
            belge.WithMetadata(new DocumentMetadata
            {
                Title = "Fen Klasörü (taslak) — " + paket.ProjeAdi,
                Subject = paket.PilotDamgasi + " · RevisionId " + paket.RevizyonId,
                Author = "Imar18Studio Pilot",
                Creator = "Imar18Studio Pilot",
                Producer = "Imar18Studio Pilot",
                Keywords = paket.SablonSurumu,
                CreationDate = paket.OnayZamani,
                ModifiedDate = paket.OnayZamani,
            });
            cancellationToken.ThrowIfCancellationRequested();
            return Result<byte[]>.Success(belge.GeneratePdf());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiFenKlasoruSablon);
            return Result<byte[]>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{templateId}", paket.SablonSurumu + " (" + ex.GetType().Name + ")", StringComparison.Ordinal) });
        }
    }

    private static string M2(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("tr-TR"));

    private static string Oran(decimal v) => v.ToString("0.0000000", CultureInfo.InvariantCulture);

    private static void Sayfa(IDocumentContainer c, CiktiPaketi p, string baslik, Action<IContainer> icerik) => c.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(1.5f, Unit.Centimetre);
        // Ligatür kapalı: RevisionId/hash ve kodlar PDF metninden birebir kopyalanabilmeli (P6 regresyonu: "ff", "fb", "ti").
        page.DefaultTextStyle(t => t.FontSize(9).DisableFontFeature(FontFeatures.StandardLigatures));
        page.Header().Column(col =>
        {
            col.Item().Text(p.PilotDamgasi).Bold().FontSize(11).FontColor(Colors.Red.Darken2);
            col.Item().Text(baslik).Bold().FontSize(12);
            col.Item().Text(p.ProjeAdi + " · Şablon " + p.SablonSurumu).FontSize(8);
            col.Item().PaddingBottom(6).LineHorizontal(0.5f);
        });
        page.Foreground().AlignCenter().AlignMiddle().Rotate(-35).Text(p.PilotDamgasi).FontSize(34).Bold().FontColor(Colors.Red.Lighten3);
        page.Content().Element(icerik);
        page.Footer().Column(col =>
        {
            col.Item().LineHorizontal(0.5f);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("RevisionId " + p.RevizyonId).FontSize(7);
                row.ConstantItem(60).AlignRight().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(7);
                    t.Span(" / ").FontSize(7);
                    t.TotalPages().FontSize(7);
                });
            });
            col.Item().Text(p.PilotDamgasi + " · Pilot checksum (SHA-256) çıktıyla birlikte .sha256 dosyasındadır; dijital imza değildir.").FontSize(7);
        });
    });

    private static void Tablo(IContainer x, string[] basliklar, IEnumerable<string[]> satirlar, float[]? genislik = null) => x.Table(t =>
    {
        t.ColumnsDefinition(cd =>
        {
            for (var i = 0; i < basliklar.Length; i++)
            {
                cd.RelativeColumn(genislik?[i] ?? 1f);
            }
        });
        t.Header(h =>
        {
            foreach (var b in basliklar)
            {
                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(2).Text(b).Bold();
            }
        });
        foreach (var s in satirlar)
        {
            foreach (var hucre in s)
            {
                t.Cell().Border(0.5f).Padding(2).Text(hucre);
            }
        }
    });

    private static void Kapak(IContainer x, CiktiPaketi p) => x.Column(col =>
    {
        col.Spacing(6);
        col.Item().Text("DEĞİŞİKLİK HARİTALARI YAPIM VE KONTROL BİLGİLERİ").Bold().FontSize(14);
        Tablo(col.Item(), ["Alan", "Değer"],
        [
            ["Proje", p.ProjeAdi],
            ["Koordinat sistemi", p.CrsAciklamasi],
            ["Pafta ölçeği", "1/" + p.OlcekPaydasi.ToString(CultureInfo.InvariantCulture)],
            ["RevisionId", p.RevizyonId],
            ["Mühendis onayı", p.OnaylayanId + " · " + p.OnayZamani.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)],
            ["Fen Klasörü şablonu", p.SablonSurumu],
            ["PARSİD", PilotDamgasi.ParsidSemaDurumu],
            ["Parsel sayısı", p.Parseller.Count.ToString(CultureInfo.InvariantCulture)],
        ], [1f, 3f]);
        col.Item().PaddingTop(8).Text("İÇİNDEKİLER").Bold();
        col.Item().Text("1. FR.348 (EK-2) Kapak ve İçindekiler");
        col.Item().Text("2. Fen Klasörü Parsel Alan Tablosu (He. | M2 | Dm2)");
        col.Item().Text("3. Tescil Sayfası — Tescile Esas Dağıtım");
        col.Item().Text("4. DOPO / OKO Özet Formu ve Dengeleme");
        col.Item().Text("5. FR.521 (EK-17) Rölöve Ölçü Krokisi ve Koordine Özet");
        if (p.Uyarilar.Count > 0)
        {
            col.Item().PaddingTop(8).Text("UYARILAR").Bold();
            foreach (var u in p.Uyarilar)
            {
                col.Item().Text("• " + u);
            }
        }
    });

    private static void AlanTablosu(IContainer x, CiktiPaketi p)
    {
        var parseller = p.Parseller.Where(s => s.Rol != ParselRolu.Kadastro).ToArray();
        var satirlar = parseller.Select(s => new[]
        {
            s.TescilNo, s.GeciciKod, ClosedXmlCetvelUretici.RolAdi(s.Rol),
            s.Alan.He.ToString(CultureInfo.InvariantCulture), s.Alan.M2.ToString(CultureInfo.InvariantCulture), s.Alan.Dm2.ToString("00", CultureInfo.InvariantCulture),
        }).ToList();
        var toplam = HeM2Dm2.Ayir(parseller.Sum(s => s.HesapAlaniM2));
        satirlar.Add(["TOPLAM", string.Empty, string.Empty, toplam.He.ToString(CultureInfo.InvariantCulture), toplam.M2.ToString(CultureInfo.InvariantCulture), toplam.Dm2.ToString("00", CultureInfo.InvariantCulture)]);
        Tablo(x, ["Ada/Parsel", "Geçici Kod", "Cinsi", "He.", "M2", "Dm2"], satirlar, [1.2f, 1.2f, 1.4f, 0.6f, 0.8f, 0.6f]);
    }

    private static void TescilSayfasi(IContainer x, CiktiPaketi p) =>
        Tablo(x, ["Ada/Parsel", "He.", "M2", "Dm2", "Malik Kodu", "Kadastro", "Alan (m²)", "Hisse"],
            p.ImarSirali.Select(s => new[]
            {
                s.TescilNo, s.ParselAlani.He.ToString(CultureInfo.InvariantCulture), s.ParselAlani.M2.ToString(CultureInfo.InvariantCulture),
                s.ParselAlani.Dm2.ToString("00", CultureInfo.InvariantCulture), s.MalikKodu, s.KadastroParselleri, M2(s.AlanM2), s.Hisse,
            }),
            [1f, 0.5f, 0.7f, 0.5f, 1.2f, 1f, 1f, 1.4f]);

    private static void DopSayfasi(IContainer x, CiktiPaketi p) => x.Column(col =>
    {
        col.Spacing(8);
        var d = p.Dop;
        Tablo(col.Item(), ["Kalem", "Değer"],
        [
            ["R1 Düzenlemeye Giren Alanlar (m²)", M2(d.R1)],
            ["D1 Eski DOPO > Proje DOPO Alanlar (m²)", M2(d.D1)],
            ["R2 İmar Alanları (m²)", M2(d.R2)],
            ["D2 Eski DOPO > Proje DOPO Tahsisler (m²)", M2(d.D2)],
            ["R3 DOP Alınmayan Alanlar (m²)", M2(d.R3)],
            ["Kamuya Ayrılan Alan R1 − R2 (m²)", M2(d.KamuyaAyrilanM2)],
            ["DOPO = (R1 − D1 − R2 + D2 + R3) / (R1 − D1)", Oran(d.Dopo)],
            ["OKO", Oran(d.Oko)],
            ["DOPO (OKO ile birlikte)", Oran(d.DopoOkoIleBirlikte)],
        ], [3f, 1f]);
        if (p.Dengeleme is { } g)
        {
            col.Item().Text("DENGELEME ALAN HESABI").Bold();
            Tablo(col.Item(), ["Ada/Parsel", "Geçici Kod", "Hesaplanan", "Düzeltme", "Dengelenmiş"],
                g.Satirlar.Select(s => new[] { s.TescilNo, s.GeciciKod, M2(s.HesapAlaniM2), M2(s.DuzeltmeM2), M2(s.DengelenmisAlanM2) }));
            Tablo(col.Item(), ["Tapu Alanı", "Hesaplanan", "Fark", "Tecviz"], [[M2(g.TapuAlaniM2), M2(g.HesaplananM2), M2(g.FarkM2), M2(g.TecvizSiniriM2)]]);
        }
    });

    private static void Kroki(IContainer x, CiktiPaketi p) => x.Column(col =>
    {
        col.Spacing(6);
        col.Item().Height(430).Svg(KrokiSvg(p)).FitArea();
        col.Item().Text("Kenar uzunlukları (cephe) metre, 2 ondalık. Kroki ölçeksizdir; koordinatlar aşağıdaki koordine özettedir.").FontSize(7);
        Tablo(col.Item(), ["Nokta No", "Y (Sağa)", "X (Yukarı)"],
            p.Koordinatlar.Select(k => new[]
            {
                k.NoktaNo.ToString(CultureInfo.InvariantCulture), k.Y.ToString("0.000", CultureInfo.InvariantCulture), k.X.ToString("0.000", CultureInfo.InvariantCulture),
            }));
    });

    /// <summary>
    /// Krokinin SVG metni (decimal hesap): ekran x = Y (Doğu), ekran y = −X (Kuzey yukarı); en uzun kenar 1000 birime ölçeklenir.
    /// Kadastro sınırları kesikli, imar/yol/kamu parselleri dolgulu çizilir; etiket ağırlık merkezine (köşe ortalaması), cephe
    /// uzunluğu kenar ortasına yazılır.
    /// </summary>
    internal static string KrokiSvg(CiktiPaketi p)
    {
        var tum = p.Parseller.SelectMany(s => s.Koseler).ToArray();
        if (tum.Length == 0)
        {
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"></svg>";
        }

        var minY = tum.Min(k => k.Y);
        var maxY = tum.Max(k => k.Y);
        var minX = tum.Min(k => k.X);
        var maxX = tum.Max(k => k.X);
        var acikl = Math.Max(Math.Max(maxY - minY, maxX - minX), 0.001m);
        const decimal Boyut = 1000m;
        const decimal Kenar = 40m;
        var olcek = Boyut / acikl;
        string S(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
        decimal Ex(GeodeticPoint k) => Kenar + ((k.Y - minY) * olcek);
        decimal Ey(GeodeticPoint k) => Kenar + ((maxX - k.X) * olcek);

        var w = Kenar + ((maxY - minY) * olcek) + Kenar;
        var h = Kenar + ((maxX - minX) * olcek) + Kenar;
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ").Append(S(w)).Append(' ').Append(S(h)).Append("\">");
        foreach (var s in p.Parseller.OrderBy(s => s.Rol == ParselRolu.Kadastro ? 1 : 0))
        {
            var (dolgu, cizgi, kesik) = s.Rol switch
            {
                ParselRolu.Kadastro => ("none", "#555555", " stroke-dasharray=\"8 6\""),
                ParselRolu.Imar => ("#dbeafe", "#1d4ed8", string.Empty),
                ParselRolu.YolTerk => ("#e5e7eb", "#6b7280", string.Empty),
                _ => ("#dcfce7", "#15803d", string.Empty),
            };
            sb.Append("<polygon points=\"");
            sb.AppendJoin(' ', s.Koseler.Select(k => S(Ex(k)) + "," + S(Ey(k))));
            sb.Append("\" fill=\"").Append(dolgu).Append("\" stroke=\"").Append(cizgi).Append("\" stroke-width=\"2\"").Append(kesik).Append("/>");

            if (s.Rol != ParselRolu.Kadastro)
            {
                var n = s.Koseler.Count;
                for (var i = 0; i < n; i++)
                {
                    var a = s.Koseler[i];
                    var b = s.Koseler[(i + 1) % n];
                    sb.Append("<text x=\"").Append(S((Ex(a) + Ex(b)) / 2m)).Append("\" y=\"").Append(S((Ey(a) + Ey(b)) / 2m))
                        .Append("\" font-size=\"14\" fill=\"#374151\" text-anchor=\"middle\">")
                        .Append(s.Kenarlar[i].CepheM.ToString("0.00", CultureInfo.InvariantCulture)).Append("</text>");
                }

                var cx = s.Koseler.Average(k => Ex(k));
                var cy = s.Koseler.Average(k => Ey(k));
                sb.Append("<text x=\"").Append(S(cx)).Append("\" y=\"").Append(S(cy))
                    .Append("\" font-size=\"20\" font-weight=\"bold\" fill=\"#111827\" text-anchor=\"middle\">")
                    .Append(System.Security.SecurityElement.Escape(s.TescilNo)).Append("</text>");
            }
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
