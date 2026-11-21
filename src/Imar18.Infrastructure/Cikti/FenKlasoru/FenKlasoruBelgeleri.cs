using System.Globalization;
using System.Security;
using System.Text;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static Imar18.Infrastructure.Cikti.FenKlasoru.FenKlasoruVerisi;

namespace Imar18.Infrastructure.Cikti.FenKlasoru;

/// <summary>Tablo üst grup başlığı hücresi (ör. "KADASTRO" 8 sütun).</summary>
internal readonly record struct GrupBasligi(string Metin, int Sutun);

/// <summary>
/// P7.3-C1 (SAHA-007): Fen Klasörü belgelerinin QuestPDF bileşenleri. Her bileşen <see cref="IDocumentContainer"/>'a kendi
/// sayfalarını ekler; birleşik dosyalar (KOORDİNE-ALAN, TESCİL-1-2-…, RÖLÖVE-1-2-…) alt bileşenlerin ardışık render'ıdır.
/// Düzenler mühendisin örnek paketindeki başlık ve sütunları izler (birebir resmî TKGM şablonu doğrulanmadı — taslak).
/// Filigran yoktur; her sayfanın en altında ince tek satır damga bulunur:
/// <c>PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ · RevisionId &lt;ilk 16 hex&gt;</c>. Sayılar InvariantCulture (<c>.</c> ondalık).
/// </summary>
internal static class FenKlasoruBelgeleri
{
    /// <summary>SAHA-017: resmî TKGM/Netcad estetiği — tüm tablo ve çerçeve çizgileri 0.5 pt.</summary>
    private const float Ince = 0.5f;

    /// <summary>Alt bilgi damgası (RevisionId'nin ilk 16 onaltılık hanesi).</summary>
    public static string AltDamga(CiktiPaketi p) =>
        p.PilotDamgasi + " · RevisionId " + (p.RevizyonId.Length > 16 ? p.RevizyonId[..16] : p.RevizyonId);

    // ------------------------------------------------------------------ ortak sayfa ve tablo

    private static void Sayfa(IDocumentContainer c, CiktiPaketi p, bool yatay, Action<IContainer> icerik) => c.Page(page =>
    {
        page.Size(yatay ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.MarginHorizontal(1.2f, Unit.Centimetre);
        page.MarginTop(1f, Unit.Centimetre);
        page.MarginBottom(0.6f, Unit.Centimetre);
        // Ligatür kapalı: RevisionId ve kodlar PDF metninden birebir kopyalanabilmeli (P6 regresyonu).
        page.DefaultTextStyle(t => t.FontSize(7).DisableFontFeature(FontFeatures.StandardLigatures));
        page.Content().Element(icerik);
        page.Footer().PaddingTop(3).Row(r =>
        {
            r.RelativeItem().Text(AltDamga(p)).FontSize(6).FontColor(Colors.Grey.Darken2);
            r.ConstantItem(50).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(6).FontColor(Colors.Grey.Darken2));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    });

    private static bool Sayisal(string s) =>
        s.Length > 0 && decimal.TryParse(s.Replace("\n", string.Empty, StringComparison.Ordinal), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Inv, out _);

    private static IContainer Hucre(IContainer c) => c.Border(Ince).PaddingHorizontal(2).PaddingVertical(1);

    private static void Tablo(
        IContainer x,
        string[] basliklar,
        IEnumerable<string[]> satirlar,
        float[]? genislik = null,
        GrupBasligi[]? grup = null,
        float font = 7f) => x.Table(t =>
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
            if (grup is not null)
            {
                foreach (var g in grup)
                {
                    Hucre(h.Cell().ColumnSpan((uint)g.Sutun)).Background(Colors.Grey.Lighten4).AlignCenter().Text(g.Metin).Bold().FontSize(font);
                }
            }

            foreach (var b in basliklar)
            {
                Hucre(h.Cell()).Background(Colors.Grey.Lighten4).AlignCenter().Text(b).Bold().FontSize(font);
            }
        });
        foreach (var s in satirlar)
        {
            foreach (var hucre in s)
            {
                var c = Hucre(t.Cell());
                (Sayisal(hucre) ? c.AlignRight() : c).Text(hucre).FontSize(font);
            }
        }
    });

    private static void Baslik(ColumnDescriptor col, string metin, float boyut = 12f) =>
        col.Item().PaddingBottom(4).AlignCenter().Text(metin).Bold().FontSize(boyut);

    private static void Not(ColumnDescriptor col, string metin) =>
        col.Item().PaddingVertical(4).Border(Ince).Background(Colors.Grey.Lighten5).Padding(6).Text(metin).FontSize(8);

    /// <summary>Satırları iki yan yana bloğa böler (her sayfa parçasında sol R + sağ R satır).</summary>
    private static void IkiBloklu(ColumnDescriptor col, string[] basliklar, IReadOnlyList<string[]> satirlar, int blokSatiri, float[]? genislik = null)
    {
        if (satirlar.Count == 0)
        {
            col.Item().Row(r =>
            {
                Tablo(r.RelativeItem(), basliklar, [], genislik);
                r.ConstantItem(8);
                Tablo(r.RelativeItem(), basliklar, [], genislik);
            });
            return;
        }

        foreach (var parca in satirlar.Chunk(blokSatiri * 2))
        {
            var sol = parca.Take((parca.Length + 1) / 2).ToArray();
            var sag = parca.Skip(sol.Length).ToArray();
            col.Item().PaddingBottom(4).Row(r =>
            {
                Tablo(r.RelativeItem(), basliklar, sol, genislik);
                r.ConstantItem(8);
                Tablo(r.RelativeItem(), basliklar, sag, genislik);
            });
        }
    }

    private static string Il(CiktiPaketi p) => p.Kunye?.Il ?? string.Empty;

    private static string Ilce(CiktiPaketi p) => p.Kunye?.Ilce ?? string.Empty;

    private static string Mahalle(CiktiPaketi p) => p.Kunye?.KoyMahalle ?? string.Empty;

    private static string Belediye(CiktiPaketi p) => p.Kunye?.Belediye ?? string.Empty;

    private static string Pafta(CiktiPaketi p) => p.Kunye?.PaftaNolari ?? string.Empty;

    private static void KunyeSatirlari(ColumnDescriptor col, CiktiPaketi p, bool paftaDahil)
    {
        col.Item().Text("İLİ             : " + Il(p));
        col.Item().Text("İLÇESİ          : " + Ilce(p));
        col.Item().Text("KÖY/MAHALLESİ   : " + Mahalle(p));
        col.Item().Text("BELEDİYESİ      : " + Belediye(p));
        if (paftaDahil)
        {
            col.Item().Text("PAFTANO         : " + Pafta(p));
        }
    }

    // ------------------------------------------------------------------ 1. DÖNÜŞÜM PARAMETRELERİ

    public static void DonusumParametreleri(IDocumentContainer c, CiktiPaketi p) => Sayfa(c, p, yatay: true, x => x.Column(col =>
    {
        Baslik(col, "DÖNÜŞÜM PARAMETRELERİ");
        if (p.Donusum is not { } d)
        {
            // SAHA-014: dönüşüm isteğe bağlıdır (deprem bölgesi dışı / dönüşümsüz projeler); kullanıcı zorlanmaz.
            Not(col, "Bu projede koordinat dönüşümü uygulanmadı (isteğe bağlı); proje koordinatları CAD dosyasındaki hâliyle "
                + "kullanıldı. Deprem bölgesindeki projelerde 1. adımda Netcad dönüşüm dosyası (.DNS) yüklenip uygulandığında "
                + "dönüşüm parametreleri, ortak nokta hataları ve Deprem Öncesi / Deprem Sonrası koordinatlar bu belgede listelenir.");
            return;
        }

        var par = d.Parametreler;

        // SAHA-014: .DNS katsayıları dosyadaki tam ondalık değerleriyle (birebir) basılır; elle ortak nokta (P7.3-C2) kipi 8 ondalık.
        string P8(decimal v) => d.IleriDonusum ? v.ToString(Inv) : v.ToString("0.00000000", Inv);
        string P3(decimal v) => d.IleriDonusum ? v.ToString(Inv) : v.ToString("0.000", Inv);
        col.Item().Row(r =>
        {
            r.RelativeItem(2).Column(sol =>
            {
                sol.Item().AlignCenter().Text("DÖNÜŞÜM ORTAK NOKTALARI").Bold();
                Tablo(sol.Item(), ["NOKTANO", "Y", "X", "Y", "X"],
                    d.Girdi.OrtakNoktalar.Select(n => new[] { n.NoktaNo, F2(n.Y1), F2(n.X1), F2(n.Y2), F2(n.X2) }),
                    [1.3f, 1.4f, 1.4f, 1.4f, 1.4f],
                    [new GrupBasligi(string.Empty, 1), new GrupBasligi("I.SİSTEM <KOORDİNATLAR>", 2), new GrupBasligi("II.SİSTEM <KOORDİNATLAR>", 2)], 6.5f);
                sol.Item().PaddingTop(4).Text("ÖLÇEK KATSAYISI = " + (par.OlcekKatsayisi is { } o ? o.ToString("0.000000000", Inv) : "— (Afin)"));
                sol.Item().Text("DÖNÜKLÜK        = " + (par.DonuklukGrad is { } dn ? dn.ToString("0.000000000", Inv) + " grad" : "— (Afin)"));
                sol.Item().Text("ORTALAMA HATA   = " + (par.OrtalamaHataM is { } m0 ? m0.ToString("0.000000000", Inv) + " m" : "— (serbestlik derecesi 0)"));
                sol.Item().Text("MODEL           = " + (par.Model == DonusumModeli.Helmert2B ? "Helmert (2B benzerlik)" : "Afin (2B)"));
                sol.Item().PaddingTop(6).AlignCenter().Text("ORTAK NOKTALARIN DÖNÜŞÜM SONRASI KOORDİNATLARI").Bold();
                Tablo(sol.Item(), ["NOKTANO", "Y", "X", "Vy", "Vx"],
                    par.Artiklar.Select(a => new[] { a.NoktaNo, F2(a.Y), F2(a.X), F2(a.Vy), F2(a.Vx) }),
                    [1.3f, 1.4f, 1.4f, 1f, 1f],
                    [new GrupBasligi(string.Empty, 1), new GrupBasligi("II.SİSTEM <KOORDİNATLAR>", 2), new GrupBasligi("HATALAR", 2)], 6.5f);
                sol.Item().PaddingTop(4).Text("Y2 = A·Y1 + C·X1 + CY ;  X2 = B·Y1 + D·X1 + CX  (V = hesaplanan − II. sistem). "
                    + (d.IleriDonusum
                        ? "Proje noktaları I. sistemde (Deprem Öncesi) yüklendi ve .DNS katsayılarıyla II. sisteme (Deprem Sonrası) ileri dönüştürüldü; hesaplar Deprem Sonrası koordinatlarla yapıldı."
                        : "Proje noktaları II. sistem (deprem sonrası) kabul edilir; I. sistem (deprem öncesi) ters dönüşümle hesaplanır.")).FontSize(6);
            });
            r.ConstantItem(10);
            r.RelativeItem(5).Column(sag =>
            {
                sag.Item().Text(string.Create(Inv, $"A= {P8(par.A)}   B= {P8(par.B)}   C= {P8(par.C)}   D= {P8(par.D)}   CY= {P3(par.Cy)}   CX= {P3(par.Cx)}")).Bold();
                var oncesi = d.DepremOncesi.ToDictionary(k => k.NoktaNo);
                var satirlar = p.Koordinatlar.Select(k => oncesi.TryGetValue(k.NoktaNo, out var o)
                    ? new[] { p.NoktaAdi(k.NoktaNo), F2(o.Y), F2(o.X), F2(k.Y), F2(k.X) }
                    : [p.NoktaAdi(k.NoktaNo), "-", "-", F2(k.Y), F2(k.X)]).ToArray();
                IkiBloklu(sag, ["Nokta no", "Dönüşecek Y", "Dönüşecek X", "Dönüşmüş Y", "Dönüşmüş X"], satirlar, 45, [0.9f, 1.3f, 1.4f, 1.3f, 1.4f]);
            });
        });
    }));

    // ------------------------------------------------------------------ 2. KOORDİNE ÖZET

    /// <summary>
    /// SAHA-017 / SAHA-021 (mühendis teyidi, gerçek KOORDİNE ÖZET / RÖLÖVE-1 ile aynı): dönüşüm veya arşiv onaylı nokta varsa üst tablo
    /// "DEPREM ÖNCESİ / ARŞİV ONAYLI" = yalnız kadastro köşeleri (arşiv no + arşiv Y, X), MK sütunu yok; alt tablo "DEPREM SONRASI"
    /// (ya da ikisi de yoksa tek tablo) = önce yalnız yeni TK_Oluşan köşeleri (MK = proje MK'si / 0.09), altına kadastro köşeleri
    /// (arşiv no, dönüşmüş koordinat, MK boş). Kadastro köşesiyle çakışan köşe iki kez yazılmaz.
    /// </summary>
    public static void KoordineOzet(IDocumentContainer c, CiktiPaketi p) => Sayfa(c, p, yatay: false, x => x.Column(col =>
    {
        var (olusan, kadastro) = NoktaGruplari(p, HesapParselleri(p), adaKapsami: false);
        var sonrasi = SonrasiSatirlari(p, olusan, kadastro);
        if (!IkiTabloMu(p))
        {
            // SAHA-014: dönüşüm ve arşiv noktası yoksa Deprem Öncesi/Sonrası başlıkları basılmaz (kullanıcı zorlanmaz).
            Baslik(col, SonrasiBasligi(p), 11);
            IkiBloklu(col, ["Nokta No", "Y", "X", "MK"], sonrasi, 30, [1f, 1.5f, 1.6f, 0.6f]);
            return;
        }

        Baslik(col, OncesiBasligi(p), 11);
        IkiBloklu(col, ["Nokta No", "Y", "X"],
            OncesiNoktalar(p, kadastro).Select(k => new[] { p.NoktaAdi(k.NoktaNo), F2(k.Y), F2(k.X) }).ToArray(), 30);

        col.Item().PaddingTop(8);
        Baslik(col, SonrasiBasligi(p), 11);
        IkiBloklu(col, ["Nokta No", "Y", "X", "MK"], sonrasi, 30, [1f, 1.5f, 1.6f, 0.6f]);
    }));

    // ------------------------------------------------------------------ 3. ALAN HESABI

    public static void AlanHesabi(IDocumentContainer c, CiktiPaketi p) => Sayfa(c, p, yatay: false, x => x.Column(col =>
    {
        Baslik(col, "DENGELEME ALAN HESABI", 11);
        var pafta = Pafta(p);
        IReadOnlyList<(string Kimlik, decimal Hesap, decimal Duzeltme, decimal Deng)> satirlar = p.Dengeleme is { } g
            ? g.Satirlar.Select(s => (s.TescilNo is { Length: > 0 } t && t != "-" ? t : s.GeciciKod, s.HesapAlaniM2, s.DuzeltmeM2, s.DengelenmisAlanM2))
                .OrderBy(s => s.Item1, ParselNoKarsilastirici.Instance).ToArray()
            : HesapParselleri(p).Select(s => (Kimlik(s), s.HesapAlaniM2, 0m, s.DengelenmisAlanM2)).ToArray();
        var tablo = satirlar.Select(s => new[]
        {
            s.Kimlik.Contains('/', StringComparison.Ordinal) ? AdaNo(s.Kimlik) : string.Empty, ParselNo(s.Kimlik),
            F2(s.Hesap), (s.Duzeltme >= 0 ? "+" : string.Empty) + F2(s.Duzeltme), F2(s.Deng), pafta,
        }).ToList();
        var dz = satirlar.Sum(s => s.Duzeltme);
        tablo.Add(["TOPLAM", string.Empty, F2(satirlar.Sum(s => s.Hesap)), (dz >= 0 ? "+" : string.Empty) + F2(dz), F2(satirlar.Sum(s => s.Deng)), string.Empty]);
        Tablo(col.Item(), ["Ada", "Parsel", "Alan", "Düzeltme", "Deng.Alan", "Pafta"], tablo, [1f, 1f, 1.2f, 1f, 1.2f, 1.2f]);
        if (p.Dengeleme is { } k)
        {
            col.Item().PaddingTop(4).Width(260).Element(e => Tablo(e, ["", "Alan (m²)"],
            [
                ["Tapu Alanı", F2(k.TapuAlaniM2)], ["HESAPLANAN", F2(k.HesaplananM2)], ["FARK", F2(k.FarkM2)], ["TECVİZ", F2(k.TecvizSiniriM2)],
            ]));
        }
        else
        {
            Not(col, "Dengeleme cetveli üretilmedi (yol terk parseli yok); alanlar düzeltmesiz hesap alanıdır.");
        }

        col.Item().PaddingTop(10);
        Baslik(col, "NOKTALI ALAN HESABI", 11);
        Tablo(col.Item(), ["Ada", "Parsel", "Noktalar", "HesapAlan"],
            HesapParselleri(p).Select(s => new[]
            {
                Kimlik(s).Contains('/', StringComparison.Ordinal) ? AdaNo(Kimlik(s)) : string.Empty, ParselNo(Kimlik(s)),
                string.Join(",", s.NoktaNolari.Select(p.NoktaAdi)), F2(s.DengelenmisAlanM2),
            }),
            [0.7f, 0.7f, 5f, 1.1f]);
    }));

    // ------------------------------------------------------------------ 4. CEPHELİ ALAN HESABI-n

    public static void CepheliAlanHesabi(IDocumentContainer c, CiktiPaketi p, int sayfaNo, IReadOnlyList<IReadOnlyList<CepheliSatir>> seritler) =>
        Sayfa(c, p, yatay: true, x => x.Column(col =>
        {
            Baslik(col, "CEPHELİ ALAN HESABI-" + sayfaNo.ToString(Inv));
            col.Item().Row(r =>
            {
                for (var i = 0; i < CepheliSayfaSeridi; i++)
                {
                    if (i > 0)
                    {
                        r.ConstantItem(6);
                    }

                    var serit = i < seritler.Count ? seritler[i] : [];
                    Tablo(r.RelativeItem(), ["Ada Parsel", "Hesap Alan", "Nokta No", "Y", "X", "Cephe"],
                        serit.Select(s => new[] { s.AdaParsel, s.HesapAlan, p.NoktaAdi(s.NoktaNo), F2(s.Y), F2(s.X), F2(s.CepheM) }),
                        [1f, 0.9f, 0.7f, 1.2f, 1.3f, 0.7f], font: 6f);
                }
            });
        }));

    // ------------------------------------------------------------------ 6. DOP

    /// <summary>
    /// SAHA-017: resmî Netcad DOP formatı — tek dış çerçeve, etiket | <c>=</c> | değer sütunları (<c>=</c> işaretleri alt alta
    /// hizalı), alan kalemleri ile oranlar ve "Düzenleme Ortaklık Payı Hesabı" yatay ayırıcılarla bölünür.
    /// </summary>
    public static void Dop(IDocumentContainer c, CiktiPaketi p) => Sayfa(c, p, yatay: false, x => x.Column(col =>
    {
        var d = p.Dop;
        var cks = CksOzetiEslemesi.Olustur(d);
        Baslik(col, "DOP");
        // Eşleme SORU-033 a / SORU-034 ile aynıdır. CKS eşlenemiyorsa (D1, D2 veya R3 ≠ 0) modelde karşılığı olmayan
        // kamulaştırılan/bağışlanan alanlar "—" basılır, D1 ve D2 kendi etiketleriyle ayrı satırdadır (reviewer M2).
        var alanlar = new List<(string, string)>
        {
            ("Toplam Tahsis", F2(d.R2)),
            ("Toplam İmar Ada Alanı", F2(d.R2)),
            ("Toplam Ortak Katılım Alanı", F2(d.OrtakKatilimM2)),
            ("Toplam Düzenlemeye Giren (Dopal)", F2(d.R1)),
            ("Toplam Düzenlemeye Giren (Dopalma)", F2(d.R3)),
            ("Toplam Kamulaştırılan Alan", cks.IsSuccess ? F2(cks.Value.ToplamKamulastirilanAlanM2) : "—"),
            ("Toplam Bağışlanan Alan", cks.IsSuccess ? F2(cks.Value.ToplamBagislananAlanM2) : "—"),
            ("Toplam Kadastro Parsel Alanı", F2(d.R1)),
            ("Toplam Pay", F2(d.R1)),
        };
        if (cks.IsFailure)
        {
            alanlar.Add(("D1 — Eski DOPO > Proje DOPO Olan Alanlar", F2(d.D1)));
            alanlar.Add(("D2 — Eski DOPO > Proje DOPO Olan Tahsisler", F2(d.D2)));
        }

        (string, string)[] oranlar =
        [
            ("Ortak Katılım Oranı", Oran(d.Oko)), ("DOPO", Oran(d.Dopo)), ("DOPO(OKO ile birlikte)", Oran(d.DopoOkoIleBirlikte)),
            ("Maksimum Dop Oranı", Oran(DopCalculator.YasalUstSinir)),
        ];

        // D1 = D2 = R3 = 0 iken DOPO = Kamuya Ayrılan / R1 (gerçek DOP.pdf düzeni); aksi hâlde tam formül sayılarla.
        var dopIfadesi = d.D1 == 0m && d.D2 == 0m && d.R3 == 0m
            ? $"{F2(d.KamuyaAyrilanM2)} / {F2(d.R1)} = {Oran(d.Dopo)}"
            : $"({F2(d.R1)} - {F2(d.D1)} - {F2(d.R2)} + {F2(d.D2)} + {F2(d.R3)}) / ({F2(d.R1)} - {F2(d.D1)}) = {Oran(d.Dopo)}";

        col.Item().PaddingTop(6).AlignCenter().Width(430).Border(Ince).PaddingVertical(8).PaddingHorizontal(14).Column(k =>
        {
            k.Spacing(2);
            void Satir(string etiket, string deger, bool ifade = false) => k.Item().Row(r =>
            {
                r.ConstantItem(ifade ? 150 : 230).Text(etiket).FontSize(9);
                r.ConstantItem(14).AlignCenter().Text("=").FontSize(9);
                (ifade ? r.RelativeItem() : r.RelativeItem().AlignRight()).Text(deger).FontSize(9);
            });

            foreach (var (e, v) in alanlar)
            {
                Satir(e, v);
            }

            k.Item().PaddingVertical(3).LineHorizontal(Ince);
            foreach (var (e, v) in oranlar)
            {
                Satir(e, v);
            }

            k.Item().PaddingVertical(3).LineHorizontal(Ince);
            k.Item().PaddingBottom(2).Text("Düzenleme Ortaklık Payı Hesabı").Bold().FontSize(9);
            Satir("Kamuya Ayrılan Alan", $"{F2(d.R1)} - {F2(d.R2)} = {F2(d.KamuyaAyrilanM2)}", ifade: true);
            Satir("Düzenleme Ortaklık Payı", dopIfadesi, ifade: true);
        });
        if (cks.IsFailure)
        {
            Not(col, "D1, D2 veya R3 sıfırdan farklı: Netcad DOP çerçevesinde D1/D2 alanı yoktur; DOP.CKS üretilmedi, değerler yukarıdaki formülle gösterildi.");
        }
    }));

    // ------------------------------------------------------------------ 7. ÖZET CETVELİ

    public static void OzetCetveli(IDocumentContainer c, CiktiPaketi p) => Sayfa(c, p, yatay: true, x => x.Column(col =>
    {
        var d = p.Dop;
        col.Item().Row(r =>
        {
            r.RelativeItem().Column(k => KunyeSatirlari(k, p, paftaDahil: false));
            r.RelativeItem().AlignCenter().Column(k =>
            {
                k.Item().AlignCenter().Text(Belediye(p)).FontSize(8);
                k.Item().AlignCenter().Text("ÖZET CETVELİ").Bold().FontSize(12);
            });
            r.RelativeItem().AlignRight().Text(t =>
            {
                t.Span("SAYFA NO:");
                t.CurrentPageNumber();
            });
        });

        var malik = p.KadastroParselleri;
        var kadastro = malik.Select(k => new[]
        {
            k.ParselId, F2(k.TapuAlaniM2), "-", "-", "-", F2(k.Malikler.Sum(m => m.GirenM2)), "-", F2(k.Malikler.Sum(m => m.DopKesintisiM2)), F2(k.Malikler.Sum(m => m.NetHakM2)),
        }).ToList();
        var adalar = p.Parseller.Where(s => s.Rol == ParselRolu.Imar)
            .GroupBy(s => AdaParselli(Kimlik(s)) ? AdaNo(Kimlik(s)) : Kimlik(s), StringComparer.Ordinal)
            .OrderBy(g => g.Key, ParselNoKarsilastirici.Instance)
            .Select(g => new[] { g.Key, F2(g.Sum(s => s.DengelenmisAlanM2)) }).ToList();
        var n = Math.Max(kadastro.Count, adalar.Count);
        var satirlar = Enumerable.Range(0, n).Select(i =>
            (i < kadastro.Count ? kadastro[i] : Enumerable.Repeat(string.Empty, 9).ToArray())
            .Concat(i < adalar.Count ? adalar[i] : [string.Empty, string.Empty]).ToArray());
        Tablo(col.Item().PaddingTop(4), ["Ada/Par", "Alan", "Kamu.", "Y.Terk", "D.Girmeyen", "Dopal", "Dopalma", "Dop", "İ.Tahsis", "AdaNo", "Alan"],
            satirlar, [1f, 1f, 0.7f, 0.7f, 0.9f, 1f, 0.8f, 1f, 1f, 0.8f, 1f], [new GrupBasligi("K A D A S T R O", 9), new GrupBasligi("İ M A R", 2)]);

        col.Item().PaddingTop(8);
        Tablo(col.Item(), ["Toplam Pay", "Toplam Alan", "Top Kamu", "Toplam Y.Terk", "Toplam D.Girmeyen", "Toplam Dopal", "Top. Dopalma", "Düz. Giren Dop", "Toplam Tahsis", "Toplam Alan"],
        [
            [F2(malik.Sum(k => k.TapuAlaniM2)), F2(malik.Sum(k => k.TapuAlaniM2)), "-", "-", "-", F2(d.R1), d.R3 == 0m ? "-" : F2(d.R3), F2(d.DopMiktariM2),
                F2(malik.Sum(k => k.Malikler.Sum(m => m.NetHakM2))), F2(d.R2)],
        ], [1f, 1f, 0.8f, 0.8f, 1f, 1f, 0.9f, 1f, 1f, 1f], [new GrupBasligi("K A D A S T R O   T O P L A M L A R I", 9), new GrupBasligi("İ M A R", 1)]);

        col.Item().PaddingTop(8).Column(h =>
        {
            h.Item().Text("Düzenleme Ortaklık Payı Hesabı").Bold();
            h.Item().Text($"Kamuya Ayrılan Alan = {F2(d.R1)} - {F2(d.R2)} = {F2(d.KamuyaAyrilanM2)}");
            h.Item().Text($"Düzenleme Ortaklık Payı = ({F2(d.R1)} - {F2(d.D1)} - {F2(d.R2)} + {F2(d.D2)} + {F2(d.R3)}) / ({F2(d.R1)} - {F2(d.D1)}) = {Oran(d.Dopo)}");
        });

        // SAHA-017: resmî cetvele geliştirici notu basılmaz (NCZ notu yalnız ZIP açıklamasındadır).
    }));

    // ------------------------------------------------------------------ 8–9. MAL SAHİPLERİ / TESCİLE ESAS DAĞITIM

    private static readonly string[] DagitimBasliklari =
        ["SNo", "Pafta", "Ada/Par", "Alan/Pay", "Hisse", "Malik/Baba Adı", "Kamu", "Dop/İTh", "Ada/Par", "Alan", "Hisse", "Pay"];

    private static readonly float[] DagitimGenislik = [0.5f, 0.8f, 0.8f, 1f, 0.8f, 2.2f, 0.5f, 1f, 0.8f, 0.9f, 1.4f, 0.9f];

    private static readonly GrupBasligi[] DagitimGrup = [new("K A D A S T R O", 8), new("İ M A R", 4)];

    private static void DagitimUstBilgi(ColumnDescriptor col, CiktiPaketi p, string baslik, string altBaslik)
    {
        var d = p.Dop;
        var malik = p.KadastroParselleri;
        col.Item().Row(r =>
        {
            r.RelativeItem().Border(Ince).Padding(3).Column(k =>
            {
                k.Item().Text("TOPLAMALAN              : " + F2(malik.Sum(m => m.TapuAlaniM2)));
                k.Item().Text("TOPLAMPAY               : " + F2(d.R1));
                k.Item().Text("TOPLAMKAMULAŞTIRMA      : -");
                k.Item().Text("TOP. DÜZENLE. GİRMEYEN  : -");
                k.Item().Text("TOP. DÜZENLEMEYE GİREN  : " + F2(d.R1));
            });
            r.RelativeItem(2).AlignCenter().Column(k =>
            {
                k.Item().AlignCenter().Text(baslik).Bold().FontSize(11);
                k.Item().AlignCenter().Text(altBaslik).FontSize(8);
            });
            r.RelativeItem().Border(Ince).Padding(3).Column(k =>
            {
                k.Item().AlignRight().Text(t =>
                {
                    t.Span("SAYFA NO:");
                    t.CurrentPageNumber();
                });
                k.Item().Text("TOPLAMADA ALANI   : " + F2(d.R2));
                k.Item().Text("O.KATILIM ORANI   : " + Oran(d.Oko));
                k.Item().Text("D.O.PAYI ORANI    : " + Oran(d.Dopo));
            });
        });
    }

    private static string ImarHissesi(CiktiPaketi p, string tescilNo, string malikKodu) =>
        p.ImarSirali.FirstOrDefault(s => s.TescilNo == tescilNo && s.MalikKodu == malikKodu)?.Hisse ?? string.Empty;

    private static decimal ImarAlani(CiktiPaketi p, string tescilNo) =>
        p.ImarSirali.FirstOrDefault(s => s.TescilNo == tescilNo)?.ParselAlaniM2
        ?? p.Parseller.FirstOrDefault(s => s.TescilNo == tescilNo)?.DengelenmisAlanM2 ?? 0m;

    public static void MalSahipleri(IDocumentContainer c, CiktiPaketi p) => Sayfa(c, p, yatay: true, x => x.Column(col =>
    {
        DagitimUstBilgi(col, p, "MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU", "( KADASTRO PARSELİNE SIRALI )");
        var sno = 0;
        var satirlar = new List<string[]>();
        foreach (var k in p.KadastroParselleri)
        {
            foreach (var m in k.Malikler)
            {
                var (ad, baba) = Malik(p, m.MalikKodu);
                var imar = p.MalikSirali.Where(s => s.KadastroParselId == k.ParselId && s.MalikKodu == m.MalikKodu).ToArray();
                string[] kadastro = [(++sno).ToString(Inv), Pafta(p), k.ParselId, F2(k.TapuAlaniM2) + "\n" + F2(m.GirenM2), m.Hisse, ad + "\n" + baba, "-",
                    F2(m.DopKesintisiM2) + "\n" + F2(m.NetHakM2)];
                if (imar.Length == 0)
                {
                    satirlar.Add([.. kadastro, string.Empty, string.Empty, string.Empty, string.Empty]);
                }

                for (var i = 0; i < imar.Length; i++)
                {
                    var s = imar[i];
                    satirlar.Add([.. i == 0 ? kadastro : Enumerable.Repeat(string.Empty, kadastro.Length),
                        s.ImarParseli, F2(ImarAlani(p, s.ImarParseli)), ImarHissesi(p, s.ImarParseli, m.MalikKodu), F2(s.TahsisM2)]);
                }
            }
        }

        Tablo(col.Item().PaddingTop(4), DagitimBasliklari, satirlar, DagitimGenislik, DagitimGrup);
    }));

    public static void TesciliEsasDagitim(IDocumentContainer c, CiktiPaketi p) => Sayfa(c, p, yatay: true, x => x.Column(col =>
    {
        // SAHA-017: başlık bu belgenin kendi adıdır (önceden yanlışlıkla Mal Sahipleri formu başlığı basılıyordu).
        DagitimUstBilgi(col, p, "TESCİLE ESAS DAĞITIM CETVELİ", "( İMAR PARSELİNE SIRALI )");
        var sno = 0;
        var satirlar = new List<string[]>();
        foreach (var g in p.ImarSirali.GroupBy(s => ImarKodu(s.TescilNo, s.GeciciKod), StringComparer.Ordinal))
        {
            var ilk = true;
            foreach (var s in g)
            {
                var (ad, baba) = Malik(p, s.MalikKodu);
                var kadastrolar = s.KadastroParselleri.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var km = kadastrolar.Select(id => (Id: id, Parsel: p.KadastroParselleri.FirstOrDefault(k => k.ParselId == id)))
                    .Select(x => (x.Id, x.Parsel, Malik: x.Parsel?.Malikler.FirstOrDefault(m => m.MalikKodu == s.MalikKodu))).ToArray();
                satirlar.Add(
                [
                    (++sno).ToString(Inv), Pafta(p), string.Join("\n", km.Select(k => k.Id)),
                    string.Join("\n", km.Select(k => F2(k.Parsel?.TapuAlaniM2 ?? 0m) + " / " + F2(k.Malik?.GirenM2 ?? 0m))),
                    string.Join("\n", km.Select(k => k.Malik?.Hisse ?? "-")), ad + "\n" + baba, "-",
                    string.Join("\n", km.Select(k => F2(k.Malik?.DopKesintisiM2 ?? 0m) + " / " + F2(k.Malik?.NetHakM2 ?? 0m))),
                    ilk ? g.Key : string.Empty, ilk ? F2(s.ParselAlaniM2) : string.Empty, s.Hisse, F2(s.AlanM2),
                ]);
                ilk = false;
            }
        }

        Tablo(col.Item().PaddingTop(4), DagitimBasliklari, satirlar, DagitimGenislik, DagitimGrup);
    }));

    // ------------------------------------------------------------------ 10. TESCİL SAYFASI-n

    /// <summary>Tescil sayfası alan gösterimi: He., M2 (4 haneli sıfır dolgulu), Dm2 (2 haneli sıfır dolgulu).</summary>
    public static (string He, string M2, string Dm2) TescilAlani(Imar18.Domain.Numerics.HeM2Dm2 a) =>
        (a.He.ToString(Inv), a.M2.ToString("0000", Inv), a.Dm2.ToString("00", Inv));

    public static void TescilSayfasi(IDocumentContainer c, CiktiPaketi p, string ada, int sayfaNo) => Sayfa(c, p, yatay: true, x => x.Column(col =>
    {
        col.Item().Row(r =>
        {
            r.RelativeItem().Column(k => KunyeSatirlari(k, p, paftaDahil: true));
            r.RelativeItem().AlignCenter().AlignMiddle().Text("TESCİL SAYFASI").Bold().FontSize(13);
            r.RelativeItem().AlignRight().Column(k =>
            {
                k.Item().AlignRight().Text("SAYFA NO:" + sayfaNo.ToString(Inv));
                k.Item().AlignRight().Text("ADANO: " + ada).Bold();
            });
        });

        var satirlar = new List<string[]>();
        foreach (var g in p.ImarSirali.Where(s => AdaParselli(ImarKodu(s.TescilNo, s.GeciciKod)) && AdaNo(ImarKodu(s.TescilNo, s.GeciciKod)) == ada)
                     .GroupBy(s => ImarKodu(s.TescilNo, s.GeciciKod), StringComparer.Ordinal).OrderBy(g => g.Key, ParselNoKarsilastirici.Instance))
        {
            var ilk = true;
            foreach (var s in g)
            {
                var (he, m2, dm2) = TescilAlani(s.ParselAlani);
                var (ad, baba) = Malik(p, s.MalikKodu);
                var kad = s.KadastroParselleri.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(id => Kadastro(p, id)).ToArray();
                satirlar.Add(
                [
                    string.Empty, string.Empty, ilk ? ParselNo(g.Key) : string.Empty, ilk ? he : string.Empty, ilk ? m2 : string.Empty, ilk ? dm2 : string.Empty,
                    ilk ? "ARSA" : string.Empty, s.Hisse,
                    string.Join("\n", kad.Select(k => k.Ada)), string.Join("\n", kad.Select(k => k.Parsel)),
                    string.Join("\n", kad.Select(k => k.Cilt)), string.Join("\n", kad.Select(k => k.Sayfa)), ad, baba,
                ]);
                ilk = false;
            }
        }

        // Sıfır dolgulu M2/Dm2 metin olarak (sağa yaslı sayı biçimi baştaki sıfırları korur).
        Tablo(col.Item().PaddingTop(6), ["Clt", "Syf", "ParNo", "He.", "M2", "Dm2", "Cinsi", "Hisse", "KAda", "KPar", "Cilt", "Syf.", "Maliki", "Babası"],
            satirlar, [0.4f, 0.4f, 0.5f, 0.4f, 0.6f, 0.4f, 0.7f, 1.3f, 0.6f, 0.6f, 0.5f, 0.5f, 2.2f, 1.3f],
            [new GrupBasligi(string.Empty, 3), new GrupBasligi("Alan", 3), new GrupBasligi(string.Empty, 2), new GrupBasligi("K A D A S T R O", 6)]);
    }));

    // ------------------------------------------------------------------ 11. RÖLÖVE ÖLÇÜ KROKİSİ

    public static void Roleve(IDocumentContainer c, CiktiPaketi p, string? ada) => Sayfa(c, p, yatay: true, x => x.Column(col =>
    {
        var secili = ada is null ? HesapParselleri(p) : AdaParselleri(p, ada);
        col.Item().Row(r =>
        {
            r.RelativeItem().Text("RÖLÖVE ÖLÇÜ KROKİSİ" + (ada is null ? string.Empty : " — ADA " + ada)).Bold().FontSize(12);
            r.ConstantItem(140).AlignRight().Text("EK-17 / FR.521").Bold();
        });
        col.Item().PaddingTop(4).Row(r =>
        {
            r.RelativeItem(3).Border(Ince).Height(300).Padding(4).Svg(KrokiSvg(p, secili)).FitArea();
            r.ConstantItem(8);
            r.RelativeItem(2).Column(sag =>
            {
                sag.Item().Border(Ince).Padding(3).Column(k =>
                {
                    k.Item().Text("ID: " + (p.Kunye?.KadastroId ?? string.Empty));
                    k.Item().Text("Datum: " + DatumBasligi(p));
                    k.Item().Text("Projeksiyon: " + (p.Kunye?.DatumProjeksiyon is { Length: > 0 } dp ? dp : p.CrsAciklamasi));
                });
                // SAHA-017: Koordine Özet ile aynı ayrım — Öncesi yalnız kadastro (MK yok); Sonrası TK_Oluşan (MK) + kadastro (MK boş).
                var (olusan, kadastro) = NoktaGruplari(p, secili, adaKapsami: ada is not null);

                // SAHA-014 / SAHA-021: dönüşüm ve arşiv noktası yoksa yalnız tek "KOORDİNE ÖZET" basılır.
                if (IkiTabloMu(p))
                {
                    sag.Item().PaddingTop(4).Text(OncesiBasligi(p)).Bold().FontSize(6.5f);
                    Tablo(sag.Item(), ["Nokta No", "Y", "X"], OncesiNoktalar(p, kadastro).Select(k => new[] { p.NoktaAdi(k.NoktaNo), F2(k.Y), F2(k.X) }), font: 6f);
                }

                sag.Item().PaddingTop(4).Text(SonrasiBasligi(p)).Bold().FontSize(6.5f);
                Tablo(sag.Item(), ["Nokta No", "Y", "X", "MK"], SonrasiSatirlari(p, olusan, kadastro), [1f, 1.4f, 1.5f, 0.6f], font: 6f);
            });
        });

        var adaParsel = ada is null
            ? string.Join(", ", p.KadastroParselleri.Select(k => k.ParselId))
            : ada + "/" + string.Join("-", secili.Select(s => ParselNo(Kimlik(s))));
        col.Item().PaddingTop(6).Row(r =>
        {
            r.RelativeItem(2).Element(e => Tablo(e, ["", ""],
            [
                ["İLİ", Il(p)], ["İLÇESİ", Ilce(p)], ["MAH./KÖY", Mahalle(p)], ["Pafta No", Pafta(p)], ["ADA/PARSEL NO", adaParsel],
            ], [1f, 3f]));
            r.ConstantItem(6);
            r.RelativeItem(4).Element(e => Tablo(e, ["", "DÜZENLEYEN", "UYGUNDUR\nBELEDİYE/ÖZEL İDARE\nKONTROL EDEN", "ONAYLAYAN", "KADASTRO MÜDÜRLÜĞÜ\nKONTROL EDEN"],
            [
                ["ADI-SOYADI", "", "", "", ""], ["TARİH", "", "", "", ""], ["İMZA", "\n\n", "", "", ""],
            ], [0.8f, 1f, 1.2f, 1f, 1.2f]));
        });
        col.Item().PaddingTop(2).Text("FR.521 taslak · * Yeni üretilen parsel köşe ve detay noktalarının").FontSize(6);
    }));

    /// <summary>
    /// Kroki SVG'si (decimal): ekran x = Y (Doğu), ekran y = −X (Kuzey yukarı); seçili parsellerin kutusu (+%12 pay) 1000
    /// birime ölçeklenir, diğer parseller soluk çizilir (kutu dışı kırpılır). Kadastro kesikli. SAHA-021 (gerçek RÖLÖVE-1 düzeni):
    /// önce tüm çizgiler, sonra yazılar — her kenarın ortasına kenara paralel, içe ofsetli cephe uzunluğu (komşu parsellerin ortak kenarı
    /// bir kez), her köşede beyaz içli ince siyah halka (○) ve nokta no (kadastro köşesinde arşiv no), her parselin içinde yalnız parsel no
    /// (<c>1</c>, <c>2</c>…; yola terk <c>Y1</c>, <c>Y2</c>…) ve her imar adasının numarası (<c>101</c>) adanın ağırlık merkezinde büyük
    /// ve kalın, tek kez.
    /// </summary>
    internal static string KrokiSvg(CiktiPaketi p, IReadOnlyList<ParselCetvelSatiri> secili)
    {
        var kutuKoseleri = (secili.Count > 0 ? secili : p.Parseller).SelectMany(s => s.Koseler).ToArray();
        if (kutuKoseleri.Length == 0)
        {
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"></svg>";
        }

        var minY = kutuKoseleri.Min(k => k.Y);
        var maxY = kutuKoseleri.Max(k => k.Y);
        var minX = kutuKoseleri.Min(k => k.X);
        var maxX = kutuKoseleri.Max(k => k.X);
        var acik = Math.Max(Math.Max(maxY - minY, maxX - minX), 0.001m);
        var pay = acik * 0.12m;
        minY -= pay;
        maxY += pay;
        minX -= pay;
        maxX += pay;
        acik += 2 * pay;
        var olcek = 1000m / acik;
        string S(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.##", Inv);
        decimal Ex(GeodeticPoint k) => (k.Y - minY) * olcek;
        decimal Ey(GeodeticPoint k) => (maxX - k.X) * olcek;

        // SAHA-017: okunaklı yazı + beyaz arka plan (halo); çizgiler yazının altında kalır. Halo yazı kopyası değil dikdörtgendir:
        // PDF metni tek kez seçilir/kopyalanır.
        void Yazi(StringBuilder b, decimal x, decimal y, string metin, int boyut, string renk, bool kalin = false) =>
            HaloluYazi(b, S, x, y, metin, boyut, renk, kalin);

        var sb = new StringBuilder();
        var (vbG, vbY) = (S((maxY - minY) * olcek), S((maxX - minX) * olcek));
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ").Append(vbG).Append(' ').Append(vbY).Append("\">");

        // SAHA-017: seçili kutu dışındaki parseller kırpılır (çizgiler çerçeveden taşıp yan tabloların üstüne çizilmez).
        sb.Append("<defs><clipPath id=\"kroki\"><rect x=\"0\" y=\"0\" width=\"").Append(vbG).Append("\" height=\"").Append(vbY)
            .Append("\"/></clipPath></defs><g clip-path=\"url(#kroki)\">");
        var seciliKodlar = secili.Select(s => s.GeciciKod).ToHashSet(StringComparer.Ordinal);
        foreach (var s in p.Parseller.OrderBy(s => s.Rol == ParselRolu.Kadastro ? 0 : seciliKodlar.Contains(s.GeciciKod) ? 2 : 1))
        {
            var (cizgi, kalinlik, kesik) = s.Rol == ParselRolu.Kadastro
                ? ("#555555", "1.5", " stroke-dasharray=\"8 6\"")
                : seciliKodlar.Contains(s.GeciciKod) ? ("#111111", "3", string.Empty) : ("#9ca3af", "1.5", string.Empty);
            sb.Append("<polygon points=\"").AppendJoin(' ', s.Koseler.Select(k => S(Ex(k)) + "," + S(Ey(k))))
                .Append("\" fill=\"none\" stroke=\"").Append(cizgi).Append("\" stroke-width=\"").Append(kalinlik).Append('"').Append(kesik).Append("/>");
        }

        var seciliParseller = p.Parseller.Where(s => seciliKodlar.Contains(s.GeciciKod)).ToList();

        // 1) Cephe uzunlukları: kenar ortasında, kenara paralel (yazı ters dönmez), kenar normali yönünde parselin İÇİNE kaydırılmış;
        // komşu iki parselin ortak kenarı (aynı iki nokta no) tek kez basılır. Köşe, kenar ve nokta no dizileri aynı uzunlukta
        // üretilir; yine de en kısasına göre gezilir (reviewer M6).
        var basilanKenarlar = new HashSet<(int, int)>();
        foreach (var s in seciliParseller)
        {
            var ic = IcNokta(s.Koseler);
            var (icX, icY) = (Ex(ic), Ey(ic));
            var n = Math.Min(s.Koseler.Count, Math.Min(s.Kenarlar.Count, s.NoktaNolari.Count));
            for (var i = 0; i < n; i++)
            {
                var (no1, no2) = (s.NoktaNolari[i], s.NoktaNolari[(i + 1) % n]);
                if (no1 == no2 || !basilanKenarlar.Add((Math.Min(no1, no2), Math.Max(no1, no2))))
                {
                    continue;
                }

                var a = s.Koseler[i];
                var b = s.Koseler[(i + 1) % s.Koseler.Count];
                var (mx, my) = ((Ex(a) + Ex(b)) / 2m, (Ey(a) + Ey(b)) / 2m);
                var (dx, dy) = (Ex(b) - Ex(a), Ey(b) - Ey(a));
                var uz = Imar18.Domain.Numerics.DecimalMath.Sqrt((dx * dx) + (dy * dy));
                var (nx, ny) = uz == 0m ? (0m, 0m) : (-dy / uz, dx / uz);
                if ((nx * (icX - mx)) + (ny * (icY - my)) < 0m)
                {
                    (nx, ny) = (-nx, -ny); // normal parselin içini göstersin
                }

                DonukYazi(sb, S, mx + (nx * 14m), my + (ny * 14m), KenarAcisiDerece(dx, dy), F2(s.Kenarlar[i].CepheM), 15, "#374151");
            }
        }

        // 2) Köşe nokta işaretleri (○) ve nokta no: köşeden parselin iç noktasına zıt yönde DIŞARI; ortak köşe bir kez.
        var cizilenNoktalar = new HashSet<int>();
        foreach (var s in seciliParseller)
        {
            var ic = IcNokta(s.Koseler);
            var (icX, icY) = (Ex(ic), Ey(ic));
            for (var i = 0; i < Math.Min(s.Koseler.Count, s.NoktaNolari.Count); i++)
            {
                if (cizilenNoktalar.Add(s.NoktaNolari[i]))
                {
                    KoseIsareti(sb, S, Ex(s.Koseler[i]), Ey(s.Koseler[i]), icX, icY, p.NoktaAdi(s.NoktaNolari[i]), "#111111");
                }
            }
        }

        // SAHA-020: arşiv noktası verilen kadastro köşeleri (seçili parsellerle ortak olmasa da) arşiv nokta no'suyla basılır; kutu
        // dışındakiler kırpılır. Yazı kadastro parselinin iç noktasından dışa kaydırılır.
        foreach (var s in p.Parseller.Where(s => s.Rol == ParselRolu.Kadastro && p.ArsivKoordinatlari.Count > 0))
        {
            var ic = IcNokta(s.Koseler);
            var (icX, icY) = (Ex(ic), Ey(ic));
            for (var i = 0; i < Math.Min(s.Koseler.Count, s.NoktaNolari.Count); i++)
            {
                var no = s.NoktaNolari[i];
                var a = s.Koseler[i];
                if (!p.ArsivKoordinatlari.ContainsKey(no) || a.X < minX || a.X > maxX || a.Y < minY || a.Y > maxY || !cizilenNoktalar.Add(no))
                {
                    continue;
                }

                KoseIsareti(sb, S, Ex(a), Ey(a), icX, icY, p.NoktaAdi(no), "#555555");
            }
        }

        // 3) Parsel etiketleri (yalnız parsel no / Y1) ve ada numaraları (adada bir kez, büyük ve kalın). Ada yazısıyla çakışacak
        // parsel etiketi ada yazısının altına kaydırılır.
        const int adaBoyu = 44;
        var etiketler = CizimEtiketleri(p);
        var adalar = AdaEtiketNoktalari(seciliParseller).Select(a => (a.Ada, X: Ex(a.Nokta), Y: Ey(a.Nokta))).ToList();
        foreach (var s in seciliParseller)
        {
            var ic = IcNokta(s.Koseler);
            var (x, y) = (Ex(ic), Ey(ic));
            if (adalar.Any(a => Math.Abs(a.X - x) < adaBoyu * 1.5m && Math.Abs(a.Y - y) < adaBoyu))
            {
                y += adaBoyu;
            }

            Yazi(sb, x, y, etiketler.TryGetValue(s.GeciciKod, out var e) ? e : Kimlik(s), 24, "#111827", kalin: s.Rol == ParselRolu.YolTerk);
        }

        foreach (var (ada, x, y) in adalar)
        {
            Yazi(sb, x, y, ada, adaBoyu, "#111827", kalin: true);
        }

        sb.Append("</g></svg>");
        return sb.ToString();
    }

    /// <summary>SAHA-021: köşe nokta işareti — beyaz içli ince halka (○) ve köşeden iç noktaya zıt yönde dışa kaydırılmış nokta no.</summary>
    private static void KoseIsareti(StringBuilder sb, Func<decimal, string> s, decimal x, decimal y, decimal icX, decimal icY, string ad, string halka)
    {
        var (vx, vy) = (x - icX, y - icY);
        var uz = Imar18.Domain.Numerics.DecimalMath.Sqrt((vx * vx) + (vy * vy));
        var (ux, uy) = uz == 0m ? (0m, -1m) : (vx / uz, vy / uz);
        sb.Append("<circle cx=\"").Append(s(x)).Append("\" cy=\"").Append(s(y)).Append("\" r=\"5\" fill=\"#ffffff\" stroke=\"").Append(halka)
            .Append("\" stroke-width=\"1.2\"/>");
        HaloluYazi(sb, s, x + (ux * 20m), y + (uy * 20m), ad, 16, "#1d4ed8");
    }

    /// <summary>
    /// SAHA-021: ekran kenar doğrultusunun açısı (derece, saat yönü pozitif — SVG y aşağı); yazı ters okunmasın diye (−90°, 90°]
    /// aralığına indirgenir. Decimal (<see cref="Imar18.Domain.Numerics.DecimalMath.Atan2"/>), 2 ondalık.
    /// </summary>
    internal static decimal KenarAcisiDerece(decimal dx, decimal dy)
    {
        if (dx == 0m && dy == 0m)
        {
            return 0m;
        }

        var aci = Imar18.Domain.Numerics.DecimalMath.Atan2(dy, dx) * 180m / Imar18.Domain.Numerics.DecimalMath.Pi;
        if (aci > 90m)
        {
            aci -= 180m;
        }
        else if (aci <= -90m)
        {
            aci += 180m;
        }

        return decimal.Round(aci, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>SAHA-021: (x, y) merkezli, <paramref name="aci"/> derece döndürülmüş halolu yazı (<c>translate</c> + <c>rotate</c>).</summary>
    internal static void DonukYazi(StringBuilder b, Func<decimal, string> s, decimal x, decimal y, decimal aci, string metin, decimal boyut, string renk)
    {
        b.Append("<g transform=\"translate(").Append(s(x)).Append(' ').Append(s(y)).Append(") rotate(").Append(aci.ToString("0.##", Inv)).Append(")\">");
        HaloluYazi(b, s, 0m, 0m, metin, boyut, renk);
        b.Append("</g>");
    }

    /// <summary>
    /// SAHA-017: ortalanmış yazı ve arkasında beyaz (yarı saydam olmayan) arka plan dikdörtgeni. Genişlik Arial için
    /// ≈ 0.6 × boyut × karakter; dikey merkez <paramref name="y"/>. Yazı tek kez basılır (PDF metni temiz kopyalanır).
    /// </summary>
    internal static void HaloluYazi(StringBuilder b, Func<decimal, string> s, decimal x, decimal y, string metin, decimal boyut, string renk, bool kalin = false)
    {
        var gen = (0.6m * boyut * metin.Length) + (0.4m * boyut);
        var yuk = 1.15m * boyut;
        b.Append("<rect x=\"").Append(s(x - (gen / 2m))).Append("\" y=\"").Append(s(y - (yuk / 2m))).Append("\" width=\"").Append(s(gen))
            .Append("\" height=\"").Append(s(yuk)).Append("\" fill=\"#ffffff\"/>");
        b.Append("<text x=\"").Append(s(x)).Append("\" y=\"").Append(s(y + (boyut * 0.35m)))
            .Append("\" font-family=\"Arial, Helvetica, sans-serif\" font-size=\"").Append(s(boyut)).Append("\" text-anchor=\"middle\"")
            .Append(kalin ? " font-weight=\"bold\"" : string.Empty).Append(" fill=\"").Append(renk).Append("\">")
            .Append(SecurityElement.Escape(metin)).Append("</text>");
    }

    // ------------------------------------------------------------------ 12. PARSELASYON HARİTASI

    /// <summary>Haritanın kâğıttaki alanı (mm): yatay A4 içerik genişliği ve imza/kimlik tablolarından kalan yükseklik.</summary>
    public const decimal HaritaGenislikMm = 250m;

    public const decimal HaritaYukseklikMm = 120m;

    public static void ParselasyonHaritasi(IDocumentContainer c, CiktiPaketi p) => Sayfa(c, p, yatay: true, x => x.Column(col =>
    {
        var (svg, olcek) = HaritaSvg(p);
        col.Item().Row(r =>
        {
            r.ConstantItem(220).Element(e => Tablo(e, ["", ""],
            [
                ["İli", Il(p)], ["İlçesi", Ilce(p)], ["Köy-Mah", Mahalle(p)], ["Pafta No", Pafta(p)],
                ["Ada/Parsel No", KadastroAdaParselleri(p)], // SAHA-017: projedeki kadastro parselleri
            ], [1f, 2.2f]));
            r.RelativeItem().AlignCenter().AlignMiddle().Column(k =>
            {
                k.Item().AlignCenter().Text("PARSELASYON HARİTASI").Bold().FontSize(14);
                k.Item().AlignCenter().Text("Ölçek 1/" + olcek.ToString(Inv)).FontSize(9);
            });
        });
        col.Item().PaddingTop(4).AlignCenter()
            .Width((float)HaritaGenislikMm, Unit.Millimetre).Height((float)HaritaYukseklikMm, Unit.Millimetre)
            .Svg(svg).FitArea();
        col.Item().PaddingTop(4).Element(e => Tablo(e, ["", "DÜZENLEYEN", "UYGUNDUR", "KONTROL EDİLMİŞTİR"],
        [
            ["ADI-SOYADI", "", "", ""], ["TARİH", "", "", ""], ["İMZA", "\n", "", ""],
        ], [0.8f, 1.5f, 1.5f, 1.5f]));
    }));

    /// <summary>
    /// Ölçekli parselasyon SVG'si: viewBox birimi metre, boyutu harita alanının (mm) ölçekle çarpımı — kâğıtta 1 mm =
    /// ölçek/1000 m. Kadastro kesikli ince, imar kalın düz; kuzey oku ve koordinat ızgarası artıları (ızgara değerleri kenarda).
    /// </summary>
    internal static (string Svg, int Olcek) HaritaSvg(CiktiPaketi p)
    {
        var tum = p.Parseller.SelectMany(s => s.Koseler).ToArray();
        if (tum.Length == 0)
        {
            return ("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"></svg>", StandartOlcekler[0]);
        }

        var minY = tum.Min(k => k.Y);
        var maxY = tum.Max(k => k.Y);
        var minX = tum.Min(k => k.X);
        var maxX = tum.Max(k => k.X);
        // %10 kenar payıyla sığan en küçük standart ölçek.
        var olcek = OlcekSec((maxY - minY) * 1.1m, (maxX - minX) * 1.1m, HaritaGenislikMm, HaritaYukseklikMm);
        var mmM = olcek / 1000m;
        var gen = HaritaGenislikMm * mmM;
        var yuk = HaritaYukseklikMm * mmM;
        var y0 = ((minY + maxY) / 2m) - (gen / 2m);
        var x1 = ((minX + maxX) / 2m) + (yuk / 2m);
        string S(decimal v) => decimal.Round(v, 3, MidpointRounding.AwayFromZero).ToString("0.###", Inv);
        decimal Ex(decimal y) => y - y0;
        decimal Ey(decimal x) => x1 - x;

        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ").Append(S(gen)).Append(' ').Append(S(yuk)).Append("\">");
        sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(S(gen)).Append("\" height=\"").Append(S(yuk))
            .Append("\" fill=\"none\" stroke=\"#000000\" stroke-width=\"").Append(S(0.3m * mmM)).Append("\"/>");

        // Koordinat ızgarası artıları.
        var izgara = IzgaraAraligiM(olcek);
        var arti = 2m * mmM;
        var yazi = 2m * mmM;
        for (var gy = Math.Ceiling(y0 / izgara) * izgara; gy <= y0 + gen; gy += izgara)
        {
            for (var gx = Math.Ceiling((x1 - yuk) / izgara) * izgara; gx <= x1; gx += izgara)
            {
                sb.Append("<path d=\"M").Append(S(Ex(gy) - arti)).Append(' ').Append(S(Ey(gx))).Append(" H").Append(S(Ex(gy) + arti))
                    .Append(" M").Append(S(Ex(gy))).Append(' ').Append(S(Ey(gx) - arti)).Append(" V").Append(S(Ey(gx) + arti))
                    .Append("\" stroke=\"#6b7280\" stroke-width=\"").Append(S(0.15m * mmM)).Append("\" fill=\"none\"/>");
            }

            sb.Append("<text x=\"").Append(S(Ex(gy) + (0.5m * mmM))).Append("\" y=\"").Append(S(yuk - (1m * mmM)))
                .Append("\" font-size=\"").Append(S(yazi)).Append("\" fill=\"#6b7280\">Y=").Append(gy.ToString("0", Inv)).Append("</text>");
        }

        for (var gx = Math.Ceiling((x1 - yuk) / izgara) * izgara; gx <= x1; gx += izgara)
        {
            sb.Append("<text x=\"").Append(S(1m * mmM)).Append("\" y=\"").Append(S(Ey(gx) - (0.5m * mmM)))
                .Append("\" font-size=\"").Append(S(yazi)).Append("\" fill=\"#6b7280\">X=").Append(gx.ToString("0", Inv)).Append("</text>");
        }

        // SAHA-017: kadastro ince (0.18 mm) kesikli gri, oluşan parseller kalın (0.5 mm) net siyah; etiketler poligonun kendi iç
        // noktasında (yol terki U/L biçimliyse bile kendi alanında, komşu imar etiketinin üstüne binmez). Kadastro etiketi kendi
        // iç noktasının bir satır altına kaydırılır (içindeki imar etiketiyle çakışmasın).
        foreach (var s in p.Parseller.OrderBy(s => s.Rol == ParselRolu.Kadastro ? 0 : 1))
        {
            var kad = s.Rol == ParselRolu.Kadastro;
            sb.Append("<polygon points=\"").AppendJoin(' ', s.Koseler.Select(k => S(Ex(k.Y)) + "," + S(Ey(k.X))))
                .Append("\" fill=\"none\" stroke=\"").Append(kad ? "#6b6b6b" : "#000000").Append("\" stroke-width=\"").Append(S((kad ? 0.18m : 0.5m) * mmM)).Append('"')
                .Append(kad ? " stroke-dasharray=\"" + S(1.5m * mmM) + " " + S(1m * mmM) + "\"" : " stroke-linejoin=\"round\"").Append("/>");
        }

        // SAHA-021: imar parselinde yalnız parsel no (1, 2…), yola terkte Y1, Y2…; ada no adanın ağırlık merkezinde büyük ve kalın,
        // tek kez (ada yazısıyla çakışan parsel etiketi altına kaydırılır).
        var etiketler = CizimEtiketleri(p);
        var adaBoyu = 5m * mmM;
        var adalar = AdaEtiketNoktalari(p.Parseller).Select(a => (a.Ada, X: Ex(a.Nokta.Y), Y: Ey(a.Nokta.X))).ToList();
        foreach (var s in p.Parseller.OrderBy(s => s.Rol == ParselRolu.Kadastro ? 0 : 1))
        {
            var kad = s.Rol == ParselRolu.Kadastro;
            var ic = IcNokta(s.Koseler);
            var boy = (kad ? 2m : 3m) * mmM;
            var (x, y) = (Ex(ic.Y), Ey(ic.X) + (kad ? boy * 2m : 0m));
            if (!kad && adalar.Any(a => Math.Abs(a.X - x) < adaBoyu * 1.5m && Math.Abs(a.Y - y) < adaBoyu))
            {
                y += adaBoyu;
            }

            var metin = kad ? s.GeciciKod : etiketler.TryGetValue(s.GeciciKod, out var e) ? e : Kimlik(s);
            HaloluYazi(sb, S, x, y, metin, boy, kad ? "#555555" : "#000000", kalin: s.Rol == ParselRolu.YolTerk);
        }

        foreach (var (ada, x, y) in adalar)
        {
            HaloluYazi(sb, S, x, y, ada, adaBoyu, "#000000", kalin: true);
        }

        // Kuzey oku (sağ üst).
        var ox = gen - (12m * mmM);
        var oy = 6m * mmM;
        var ob = 8m * mmM;
        sb.Append("<path d=\"M").Append(S(ox)).Append(' ').Append(S(oy)).Append(" L").Append(S(ox - (2m * mmM))).Append(' ').Append(S(oy + ob))
            .Append(" L").Append(S(ox)).Append(' ').Append(S(oy + (ob * 0.7m))).Append(" L").Append(S(ox + (2m * mmM))).Append(' ').Append(S(oy + ob))
            .Append(" Z\" fill=\"#000000\"/>");
        sb.Append("<text x=\"").Append(S(ox)).Append("\" y=\"").Append(S(oy - (1m * mmM))).Append("\" font-size=\"").Append(S(3m * mmM))
            .Append("\" text-anchor=\"middle\" font-weight=\"bold\">K</text>");
        sb.Append("</svg>");
        return (sb.ToString(), olcek);
    }
}
