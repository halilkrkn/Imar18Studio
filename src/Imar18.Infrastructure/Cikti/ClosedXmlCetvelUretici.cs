using System.Globalization;
using ClosedXML.Excel;
using Imar18.Application.Cikti;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Infrastructure.Cikti;

/// <summary>
/// ClosedXML taslak cetvel üreticisi (P5.3, D31 Kural 5/6, spec 25.5–25.6). Tek çalışma kitabında:
/// <list type="number">
/// <item><c>Özet</c> — DOPO / OKO özet formu (R1, D1, R2, D2, R3) canlı formüllerle;</item>
/// <item>4'lü alan cetveli: <c>Koordine Özet</c>, <c>Noktalı Alan</c>, <c>Cepheli Alan</c> (cephe = canlı <c>SQRT</c> formülü),
/// <c>Dengeleme Alan</c> (dengelenmiş = hesap + düzeltme, Tapu/Hesaplanan/Fark/Tecviz kutusu);</item>
/// <item>iki yönlü dağıtım: <c>Mal Sahipleri</c> (kadastro sıralı) ve <c>Tescile Esas Dağıtım</c> (imar sıralı, He | M2 | Dm2,
/// <c>TAM</c> veya dm² paydalı hisse).</item>
/// </list>
/// Toplam ve kontrol hücreleri canlı <c>SUM</c>/<c>ROUND</c> formülleridir (output-contracts skill); her sayfanın 1. satırında
/// ve yazdırma üst/alt bilgisinde <see cref="PilotDamgasi.Metin"/> ile RevisionId bulunur (AC-PILOT-07).
/// </summary>
public sealed class ClosedXmlCetvelUretici : ICiktiUretici
{
    public CiktiTuru Tur => CiktiTuru.Cetveller;

    public string Uzanti => ".xlsx";

    public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paket);
        try
        {
            using var wb = new XLWorkbook();
            wb.Properties.Title = "Imar18Studio Pilot Cetvelleri — " + paket.PilotDamgasi;
            wb.Properties.Comments = "RevisionId " + paket.RevizyonId;
            wb.Properties.Created = paket.OnayZamani.UtcDateTime;
            wb.Properties.Modified = paket.OnayZamani.UtcDateTime;

            Ozet(Sayfa(wb, "Özet", paket, "DOPO / OKO ÖZET FORMU"), paket);
            cancellationToken.ThrowIfCancellationRequested();
            KoordineOzet(Sayfa(wb, "Koordine Özet", paket, "KOORDİNE ÖZET"), paket);
            NoktaliAlan(Sayfa(wb, "Noktalı Alan", paket, "NOKTALI ALAN HESABI"), paket);
            CepheliAlan(Sayfa(wb, "Cepheli Alan", paket, "CEPHELİ ALAN HESABI"), paket);
            Dengeleme(Sayfa(wb, "Dengeleme Alan", paket, "DENGELEME ALAN HESABI"), paket);
            cancellationToken.ThrowIfCancellationRequested();
            MalSahipleri(Sayfa(wb, "Mal Sahipleri", paket, "MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU (Kadastro parseline göre)"), paket);
            TescileEsas(Sayfa(wb, "Tescile Esas Dağıtım", paket, "TESCİLE ESAS DAĞITIM CETVELİ (İmar parseline göre)"), paket);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return Result<byte[]>.Success(ms.ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiFenKlasoruSablon);
            return Result<byte[]>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{templateId}", "xlsx cetvel (" + ex.GetType().Name + ")", StringComparison.Ordinal) });
        }
    }

    /// <summary>Veri tablosu başlık satırı (1: damga, 2: proje/revizyon, 3: başlık, 5: sütun başlıkları).</summary>
    internal const int BaslikSatiri = 5;

    private static IXLWorksheet Sayfa(XLWorkbook wb, string ad, CiktiPaketi p, string baslik)
    {
        var ws = wb.Worksheets.Add(ad);
        ws.Cell(1, 1).Value = p.PilotDamgasi;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.Red;
        ws.Cell(2, 1).Value = p.ProjeAdi + " · RevisionId " + p.RevizyonId + " · Onay: " + p.OnaylayanId + " "
            + p.OnayZamani.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        ws.Cell(3, 1).Value = baslik;
        ws.Cell(3, 1).Style.Font.Bold = true;
        ws.PageSetup.Header.Center.AddText(p.PilotDamgasi);
        ws.PageSetup.Footer.Center.AddText("RevisionId " + p.RevizyonId);
        return ws;
    }

    private static void Basliklar(IXLWorksheet ws, params string[] basliklar)
    {
        for (var i = 0; i < basliklar.Length; i++)
        {
            var c = ws.Cell(BaslikSatiri, i + 1);
            c.Value = basliklar[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.LightGray;
        }
    }

    private static void Sayi(IXLCell c, decimal v, string bicim = "0.00")
    {
        c.Value = v;
        c.Style.NumberFormat.Format = bicim;
    }

    private static void Formul(IXLCell c, string formul, string bicim = "0.00")
    {
        c.FormulaA1 = formul;
        c.Style.NumberFormat.Format = bicim;
    }

    private static void Ozet(IXLWorksheet ws, CiktiPaketi p)
    {
        Basliklar(ws, "Kalem", "Değer", "Motor (decimal) kontrol");
        var d = p.Dop;
        (string Ad, decimal Deger)[] girdiler =
        [
            ("R1 Düzenlemeye Giren Alanlar (m²)", d.R1),
            ("D1 Eski DOPO > Proje DOPO Alanlar (m²)", d.D1),
            ("R2 İmar Alanları (m²)", d.R2),
            ("D2 Eski DOPO > Proje DOPO Tahsisler (m²)", d.D2),
            ("R3 DOP Alınmayan Alanlar (m²)", d.R3),
            ("Ortak Katılım Alanı (m²)", d.OrtakKatilimM2),
        ];
        var r = BaslikSatiri + 1; // R1 = B6 ... Ortak = B11
        foreach (var (ad, deger) in girdiler)
        {
            ws.Cell(r, 1).Value = ad;
            Sayi(ws.Cell(r, 2), deger);
            Sayi(ws.Cell(r, 3), deger);
            r++;
        }

        (string Ad, string Formul, decimal Motor, string Bicim)[] hesaplar =
        [
            ("DOP Miktarı R1 − D1 − R2 + D2 + R3 (m²)", "B6-B7-B8+B9+B10", d.DopMiktariM2, "0.00"),
            ("Kamuya Ayrılan Alan R1 − R2 (m²)", "B6-B8", d.KamuyaAyrilanM2, "0.00"),
            ("DOPO = (R1 − D1 − R2 + D2 + R3) / (R1 − D1)", "ROUND((B6-B7-B8+B9+B10)/(B6-B7),7)", d.Dopo, "0.0000000"),
            ("OKO = Ortak Katılım / (R1 − D1)", "ROUND(B11/(B6-B7),7)", d.Oko, "0.0000000"),
            ("DOPO (OKO ile birlikte)", "ROUND((B6-B7-B8+B9+B10+B11)/(B6-B7),7)", d.DopoOkoIleBirlikte, "0.0000000"),
            ("Yasal üst sınır (Md. 18/3)", "0.45", 0.45m, "0.00"),
        ];
        foreach (var (ad, formul, motor, bicim) in hesaplar)
        {
            ws.Cell(r, 1).Value = ad;
            Formul(ws.Cell(r, 2), formul, bicim);
            Sayi(ws.Cell(r, 3), motor, bicim);
            r++;
        }

        ws.Cell(r, 1).Value = "Kontrol (DOPO ≤ %45)";
        ws.Cell(r, 2).FormulaA1 = "IF(B14<=B17,\"UYGUN\",\"AŞIM\")";
        ws.Columns().AdjustToContents();
    }

    private static void KoordineOzet(IXLWorksheet ws, CiktiPaketi p)
    {
        Basliklar(ws, "Nokta No", "Y (Sağa)", "X (Yukarı)");
        var r = BaslikSatiri + 1;
        foreach (var k in p.Koordinatlar)
        {
            ws.Cell(r, 1).Value = k.NoktaNo;
            Sayi(ws.Cell(r, 2), k.Y, "0.000");
            Sayi(ws.Cell(r, 3), k.X, "0.000");
            r++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void NoktaliAlan(IXLWorksheet ws, CiktiPaketi p)
    {
        Basliklar(ws, "Ada/Parsel", "Geçici Kod", "Rol", "Hesap Alanı (m²)", "Nokta Dizisi");
        var r = BaslikSatiri + 1;
        foreach (var s in p.Parseller)
        {
            ws.Cell(r, 1).Value = s.TescilNo;
            ws.Cell(r, 2).Value = s.GeciciKod;
            ws.Cell(r, 3).Value = RolAdi(s.Rol);
            Sayi(ws.Cell(r, 4), s.HesapAlaniM2);
            ws.Cell(r, 5).Value = string.Join(", ", s.NoktaNolari);
            r++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void CepheliAlan(IXLWorksheet ws, CiktiPaketi p)
    {
        Basliklar(ws, "Ada/Parsel", "Nokta No", "Y (Sağa)", "X (Yukarı)", "Cephe (m)");
        var r = BaslikSatiri + 1;
        foreach (var s in p.Parseller)
        {
            var ilk = r;
            var son = r + s.Kenarlar.Count - 1;
            foreach (var k in s.Kenarlar)
            {
                ws.Cell(r, 1).Value = s.TescilNo;
                ws.Cell(r, 2).Value = k.NoktaNo;
                Sayi(ws.Cell(r, 3), k.Y, "0.000");
                Sayi(ws.Cell(r, 4), k.X, "0.000");
                var sonraki = r == son ? ilk : r + 1;
                Formul(ws.Cell(r, 5), $"ROUND(SQRT((C{sonraki}-C{r})^2+(D{sonraki}-D{r})^2),2)");
                r++;
            }

            ws.Cell(r, 1).Value = s.TescilNo;
            ws.Cell(r, 2).Value = "Alan (m²)";
            Sayi(ws.Cell(r, 5), s.HesapAlaniM2);
            ws.Row(r).Style.Font.Bold = true;
            r += 2;
        }

        ws.Columns().AdjustToContents();
    }

    private static void Dengeleme(IXLWorksheet ws, CiktiPaketi p)
    {
        Basliklar(ws, "Ada/Parsel", "Geçici Kod", "Hesaplanan Alan (m²)", "Düzeltme (m²)", "Dengelenmiş Alan (m²)");
        if (p.Dengeleme is not { } d)
        {
            ws.Cell(BaslikSatiri + 1, 1).Value = "Dengeleme cetveli üretilmedi (yol terk parseli yok).";
            return;
        }

        var r = BaslikSatiri + 1;
        var ilk = r;
        foreach (var s in d.Satirlar)
        {
            ws.Cell(r, 1).Value = s.TescilNo;
            ws.Cell(r, 2).Value = s.GeciciKod;
            Sayi(ws.Cell(r, 3), s.HesapAlaniM2);
            Sayi(ws.Cell(r, 4), s.DuzeltmeM2);
            Formul(ws.Cell(r, 5), $"ROUND(C{r}+D{r},2)");
            r++;
        }

        var son = r - 1;
        ws.Cell(r, 1).Value = "TOPLAM";
        Formul(ws.Cell(r, 3), $"SUM(C{ilk}:C{son})");
        Formul(ws.Cell(r, 4), $"SUM(D{ilk}:D{son})");
        Formul(ws.Cell(r, 5), $"SUM(E{ilk}:E{son})");
        ws.Row(r).Style.Font.Bold = true;
        var toplam = r;

        r += 2;
        ws.Cell(r, 1).Value = "Tapu Alanı (m²)";
        Sayi(ws.Cell(r, 3), d.TapuAlaniM2);
        ws.Cell(r + 1, 1).Value = "Hesaplanan (m²)";
        Formul(ws.Cell(r + 1, 3), $"C{toplam}");
        ws.Cell(r + 2, 1).Value = "Fark (m²)";
        Formul(ws.Cell(r + 2, 3), $"ROUND(C{r}-C{r + 1},2)");
        ws.Cell(r + 3, 1).Value = "Tecviz (m²)";
        Sayi(ws.Cell(r + 3, 3), d.TecvizSiniriM2);
        ws.Cell(r + 4, 1).Value = "Durum";
        ws.Cell(r + 4, 3).FormulaA1 = $"IF(ABS(C{r + 2})<=C{r + 3},\"UYGUN\",\"AŞIM\")";
        ws.Columns().AdjustToContents();
    }

    private static void MalSahipleri(IXLWorksheet ws, CiktiPaketi p)
    {
        Basliklar(ws, "Kadastro Parseli", "Malik Kodu", "Giren (m²)", "DOP Kesintisi (m²)", "Net Hak (m²)", "İmar Parseli", "Tahsis (m²)");
        var r = BaslikSatiri + 1;
        var ilk = r;
        foreach (var s in p.MalikSirali)
        {
            ws.Cell(r, 1).Value = s.KadastroParselId;
            ws.Cell(r, 2).Value = s.MalikKodu;
            Sayi(ws.Cell(r, 3), s.GirenM2);
            Sayi(ws.Cell(r, 4), s.DopKesintisiM2);
            Formul(ws.Cell(r, 5), $"ROUND(C{r}-D{r},2)");
            ws.Cell(r, 6).Value = s.ImarParseli;
            Sayi(ws.Cell(r, 7), s.TahsisM2);
            r++;
        }

        ws.Cell(r, 1).Value = "TOPLAM TAHSİS";
        if (r > ilk)
        {
            Formul(ws.Cell(r, 7), $"SUM(G{ilk}:G{r - 1})");
        }
        else
        {
            Sayi(ws.Cell(r, 7), 0m); // boş listede döngüsel formül üretilmez (reviewer L3)
        }
        ws.Row(r).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }

    private static void TescileEsas(IXLWorksheet ws, CiktiPaketi p)
    {
        Basliklar(ws, "Ada/Parsel", "Geçici Kod", "He.", "M2", "Dm2", "Malik Kodu", "Kadastro Parseli", "Alan (m²)", "Hisse");
        var r = BaslikSatiri + 1;
        var ilk = r;
        foreach (var s in p.ImarSirali)
        {
            ws.Cell(r, 1).Value = s.TescilNo;
            ws.Cell(r, 2).Value = s.GeciciKod;
            ws.Cell(r, 3).Value = s.ParselAlani.He;
            ws.Cell(r, 4).Value = s.ParselAlani.M2;
            ws.Cell(r, 5).Value = s.ParselAlani.Dm2;
            ws.Cell(r, 5).Style.NumberFormat.Format = "00";
            ws.Cell(r, 6).Value = s.MalikKodu;
            ws.Cell(r, 7).Value = s.KadastroParselleri;
            Sayi(ws.Cell(r, 8), s.AlanM2);
            ws.Cell(r, 9).Value = s.Hisse;
            r++;
        }

        ws.Cell(r, 1).Value = "TOPLAM";
        if (r > ilk)
        {
            Formul(ws.Cell(r, 8), $"SUM(H{ilk}:H{r - 1})");
        }
        else
        {
            Sayi(ws.Cell(r, 8), 0m);
        }
        ws.Row(r).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }

    internal static string RolAdi(ParselRolu rol) => rol switch
    {
        ParselRolu.Kadastro => "Kadastro (giren)",
        ParselRolu.Imar => "İmar (oluşan)",
        ParselRolu.YolTerk => "Yol terk",
        _ => "Kamu alanı",
    };
}
