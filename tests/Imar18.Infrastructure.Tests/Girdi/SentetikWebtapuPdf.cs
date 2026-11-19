using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Imar18.Infrastructure.Tests.Girdi;

/// <summary>Sentetik webtapu satırı (adlar uydurmadır; gerçek kişi/kayıt değildir — AC-PILOT-08).</summary>
internal sealed record SentetikMalik(
    string SistemNo, string KisiNo, string AdSoyad, string BabaSatiri, string? ElBirligi, string Hisse, string M2, string Toplam,
    string[] Edinme, string Terkin = "-");

/// <summary>
/// P7.3-B1 test üreteci: gerçek webtapu "Tapu Kaydı (Aktif Malikler için Detaylı)" PDF'inin koordinat düzenini (A4 yatay,
/// iki sütunlu anahtar–değer başlığı, MÜLKİYET BİLGİLERİ tablosu, sayfa aşan satır, 35° döndürülmüş filigran, sayfa altbilgisi)
/// QuestPDF mutlak konumlarıyla taklit eder. Konumlar QuestPDF'te üstten (pt) verilir.
/// </summary>
internal static class SentetikWebtapuPdf
{
    private const float Genislik = 841.89f;
    private const float Yukseklik = 595.28f;

    static SentetikWebtapuPdf() => QuestPDF.Settings.License = LicenseType.Community;

    public static SentetikMalik[] VarsayilanMalikler() =>
    [
        new("900000101", "800000001", "AYŞE YILMAZ", "ALİ Kızı", "70000001", "1/2", "500.00", "1000.00", ["3402 S.Y.nın", "22/A Md.", "Gereğince", "01-01-2019", "1001"]),
        new("900000102", "800000002", "MEHMET ÇAKIR", "ALİ Oğlu", null, "1/4", "250.00", "1000.00", ["Miras", "02-02-2020", "1002"]),
        new("900000103", "800000003", "ŞÜKRÜ IŞIK", "HASAN Oğlu", "70000001", "1/4", "250.00", "1000.00", ["Satış", "03-03-2021", "1316"]),
    ];

    public static byte[] Olustur(
        SentetikMalik[]? malikler = null, string adaParsel = "901/7", string atYuzolcum = "1000.00", bool filigran = true,
        SentetikMalik? terkinli = null)
    {
        var liste = (malikler ?? VarsayilanMalikler()).ToList();
        if (terkinli is not null)
        {
            liste.Insert(1, terkinli);
        }

        // Sayfa 1: başlık; Sayfa 2: ilk iki satır + 3. satırın başı; Sayfa 3: 3. satırın son yevmiyesi + doğrulama notu.
        var sayfalar = new List<List<(float X, float Y, string Metin, float Boyut)>> { new(), new(), new() };
        var s1 = sayfalar[0];
        s1.Add((40, 27, "BU BELGE TOPLAM 3 SAYFADAN OLUŞMAKTADIR BİLGİ AMAÇLIDIR.", 12));
        s1.Add((689, 27, "Tarih: 1-1-2026-10:00", 12));
        s1.Add((40, 100, "Kaydı Oluşturan: SENTETİK KULLANICI", 15));
        s1.Add((40, 134, "Tapu Kaydı (Aktif Malikler için Detaylı - ŞBİ var)", 15));
        s1.Add((40, 158, "TAPU KAYIT BİLGİSİ", 12));
        void Anahtar(float y, string sol, string? solDeger, string? sag, string? sagDeger)
        {
            s1.Add((50, y, sol, 12));
            if (solDeger is not null)
            {
                s1.Add((189, y, solDeger, 12));
            }

            if (sag is not null)
            {
                s1.Add((429, y, sag, 12));
                if (sagDeger is not null)
                {
                    s1.Add((588, y, sagDeger, 12));
                }
            }
        }

        Anahtar(178, "Zemin Tipi:", "AnaTasinmaz", "Ada/Parsel:", adaParsel);
        Anahtar(197, "Taşınmaz Kimlik No:", "100000001", "AT Yüzölçüm(m2):", atYuzolcum);
        Anahtar(216, "İl/İlçe:", "ÖRNEKİL/ÖRNEKİLÇE", "Bağımsız Bölüm Nitelik:", null);
        Anahtar(309, "Kurum Adı:", "Örnek", "Bağımsız Bölüm Brüt", null);
        Anahtar(328, "Mahalle/Köy Adı:", "ÇAMLIK Mah.", "Bağımsız Bölüm Net", null);
        Anahtar(347, "Mevkii:", "Sırtlar", null, null);
        Anahtar(366, "Cilt/Sayfa No:", "12/345", "Blok/Kat/Giriş/BBNo:", null);
        Anahtar(386, "Kayıt Durum:", "Aktif", "Arsa Pay/Payda:", null);
        Anahtar(414, "Ana Taşınmaz Nitelik:", null, null, null);
        s1.Add((588, 414, "Tarla", 12));
        s1.Add((40, 466, "MÜLKİYET BİLGİLERİ", 15));
        foreach (var (x, m) in new[] { (45.7f, "(Hisse) Sistem"), (224.5f, "Malik"), (361.7f, "El Birliği"), (423.1f, "Hisse Pay/"), (494f, "Metrekare"), (569.9f, "Toplam"), (649f, "Edinme"), (720.4f, "Terkin Sebebi-") })
        {
            s1.Add((x, 499, m, 12));
        }

        foreach (var (x, m) in new[] { (77.3f, "No"), (375.3f, "No"), (435.6f, "Payda"), (563f, "Metrekare"), (634.3f, "Sebebi-Tarih-"), (720.9f, "Tarih-Yevmiye") })
        {
            s1.Add((x, 513, m, 12));
        }

        s1.Add((646.8f, 527, "Yevmiye", 12));

        // Satırlar: 104 pt aralıkla; satır içi 14 pt. Sayfa 2'de en çok 3 satır başlar, taşan kısım sayfa 3'e geçer.
        var sayfa = 1;
        var y = 380f; // sayfa 2'nin alt yarısından başlar: üçüncü satır sayfa sonunu aşar
        foreach (var m in liste)
        {
            var hedef = sayfalar[sayfa];
            hedef.Add((54.7f, y, m.SistemNo, 12));
            hedef.Add((150, y, $"(SN:{m.KisiNo}) {m.AdSoyad} :", 12));
            if (m.ElBirligi is not null)
            {
                hedef.Add((356, y, m.ElBirligi, 12));
            }

            hedef.Add((442.8f, y, m.Hisse, 12));
            hedef.Add((502.6f, y, m.M2, 12));
            hedef.Add((568.2f, y, m.Toplam, 12));
            hedef.Add((756.3f, y, m.Terkin, 12));
            hedef.Add((200, y + 14, m.BabaSatiri, 12));
            var ey = y;
            foreach (var e in m.Edinme)
            {
                if (ey > 540)
                {
                    // Sayfa sonu: satırın kalan edinme metni sonraki sayfanın başına taşar (gerçek belgedeki "1316").
                    sayfa++;
                    hedef = sayfalar[sayfa];
                    ey = 42;
                }

                hedef.Add((640, ey, e, 12));
                ey += 14;
            }

            y = ey + 20;
            if (y > 560)
            {
                sayfa++;
                y = 42;
            }
        }

        var son = sayfalar[^1];
        son.Add((40, 300, "Bu belgeyi akıllı telefonunuzdan karekod tarama programları ile aşağıdaki barkodu taratarak;", 12));
        son.Add((40, 320, "veya Web Tapu anasayfasından doğrulayabilirsiniz. BİLGİ AMAÇLIDIR", 12));

        return Document.Create(c =>
        {
            for (var i = 0; i < sayfalar.Count; i++)
            {
                var icerik = sayfalar[i];
                var no = i + 1;
                c.Page(p =>
                {
                    p.Size(new PageSize(Genislik, Yukseklik));
                    p.Margin(0);
                    // Gerçek webtapu'da ligatür yoktur; QuestPDF "tı" → tek glif üretip metni bozmasın.
                    p.DefaultTextStyle(s => s.DisableFontFeature(FontFeatures.StandardLigatures));
                    p.Content().Layers(l =>
                    {
                        l.PrimaryLayer().Height(Yukseklik - 1);
                        foreach (var (x, yy, metin, boyut) in icerik)
                        {
                            l.Layer().PaddingLeft(x).PaddingTop(yy).Text(metin).FontSize(boyut);
                        }

                        l.Layer().PaddingLeft(779).PaddingTop(560).Text($"{no} / {sayfalar.Count}").FontSize(12);
                        if (filigran)
                        {
                            l.Layer().PaddingLeft(250).PaddingTop(380).Rotate(-35).Text("BİLGİ AMAÇLIDIR").FontSize(100).FontColor(Colors.Grey.Lighten2);
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    /// <summary>Metin katmanı olmayan (taranmış gibi) tek sayfalık PDF.</summary>
    public static byte[] Metinsiz() => Document.Create(c => c.Page(p =>
    {
        p.Size(PageSizes.A4.Landscape());
        p.Content().Background(Colors.Grey.Lighten3).Height(200);
    })).GeneratePdf();

    /// <summary>webtapu olmayan, düz metinli PDF.</summary>
    public static byte[] BaskaBelge() => Document.Create(c => c.Page(p =>
    {
        p.Size(PageSizes.A4);
        p.Content().Text("Bu bir toplantı tutanağıdır. Ada parsel bilgisi içermez.");
    })).GeneratePdf();
}
