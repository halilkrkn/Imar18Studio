using System.Globalization;
using System.IO.Compression;
using System.Text;
using Imar18.Application.Cad;
using Imar18.Application.Cikti;
using Imar18.Domain.Diagnostics;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Imar18.Infrastructure.Cikti.FenKlasoru;

/// <summary>ZIP içindeki tek dosya: ad ve içerik üreticisi.</summary>
internal sealed record FenKlasoruDosyasi(string Ad, Func<byte[]> Uret);

/// <summary>
/// P7.3-C1 (SAHA-007): Fen Klasörü <c>.zip</c> paketi. Mühendisin örnek paketiyle aynı adlı ve aynı tablo düzenli ayrı PDF'ler
/// (sırasıyla): <c>DÖNÜŞÜM PARAMETRELERİ</c>, <c>KOORDİNE ÖZET</c>, <c>ALAN HESABI</c>, <c>ALAN HESABI-1..n</c> (cepheli, sayfa
/// başına), <c>KOORDİNE-ALAN</c> (birleşik), <c>DOP</c>, <c>ÖZET CETVELİ</c>, <c>MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU</c>,
/// <c>TESCİLE ESAS DAĞITIM CETVELİ</c>, <c>TESCİL SAYFASI-1..n</c> + <c>TESCİL-1-2-…</c> (imar adası başına), <c>RÖLÖVE ÖLÇÜ
/// KROKİSİ</c>, <c>RÖLÖVE-1..n</c> + <c>RÖLÖVE-1-2-…</c> (birleşik dosyalar yalnız 2+ imar adasında; tek adada
/// <c>RÖLÖVE-1</c> adı çakışırdı), <c>PARSELASYON HARİTASI</c> ve CKS eşlemesi mümkünse <c>DOP_PILOT.CKS</c> + <c>.sha256</c>.
/// Dosya adları UTF-8 ad bayrağıyla yazılır; giriş zamanları onay zamanıdır (aynı revizyon → aynı bayt). <c>PARSELASYON
/// HARİTASI.NCZ</c> P7.3-C3'te eklenecektir (ZIP açıklaması ve Özet Cetveli notu). Onay/RevisionId/.sha256/audit kapıları
/// <see cref="CiktiServisi"/>'ndedir.
/// </summary>
public sealed class FenKlasoruZipUretici : ICiktiUretici
{
    /// <summary>NCZ bilgi notu (ZIP açıklaması ve Özet Cetveli dipnotu).</summary>
    public const string NczNotu =
        "Not: PARSELASYON HARİTASI.NCZ bu pakette yoktur; Netcad NCZ yazıcısı sonraki sürümde (P7.3-C3) eklenecektir.";

    /// <summary>ZIP içindeki CKS adı: SORU-039 istisnası gereği taslak izi adda (<c>PILOT</c>) ve <c>.sha256</c> yan girişindedir.</summary>
    public const string CksDosyaAdi = "DOP_PILOT.CKS";

    static FenKlasoruZipUretici() => QuestPDF.Settings.License = LicenseType.Community;

    public CiktiTuru Tur => CiktiTuru.FenKlasoruZip;

    public string Uzanti => ".zip";

    public Result<byte[]> Uret(CiktiPaketi paket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paket);
        if (paket.DonusumHatasi is { } dh)
        {
            // Reviewer M1: hesaplanamayan dönüşüm yalnız dönüşüme bağlı Fen Klasörü paketini engeller (Excel/CKS etkilenmez).
            return Result<byte[]>.Failure(dh);
        }

        try
        {
            var dosyalar = Dosyalar(paket);
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8))
            {
                zip.Comment = PilotDamgasi.Metin + " · RevisionId " + paket.RevizyonId + " · Şablon " + paket.SablonSurumu + " · " + NczNotu;
                var zaman = ZipZamani(paket.OnayZamani);
                foreach (var d in dosyalar)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var giris = zip.CreateEntry(d.Ad, CompressionLevel.Optimal);
                    giris.LastWriteTime = zaman;
                    using var akis = giris.Open();
                    akis.Write(d.Uret());
                }
            }

            return Result<byte[]>.Success(ms.ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiFenKlasoruSablon);
            return Result<byte[]>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{templateId}", paket.SablonSurumu + " zip (" + ex.GetType().Name + ")", StringComparison.Ordinal) });
        }
    }

    /// <summary>ZIP girişleri (sıra ve adlar sözleşmedir; içerik tembel üretilir).</summary>
    internal static IReadOnlyList<FenKlasoruDosyasi> Dosyalar(CiktiPaketi p)
    {
        var liste = new List<FenKlasoruDosyasi>();
        void Ekle(string ad, Action<IDocumentContainer> bilesen) => liste.Add(new FenKlasoruDosyasi(ad + ".pdf", () => Pdf(p, ad, bilesen)));

        var cepheli = FenKlasoruVerisi.CepheliSayfalari(p);
        Ekle("DÖNÜŞÜM PARAMETRELERİ", c => FenKlasoruBelgeleri.DonusumParametreleri(c, p));
        Ekle("KOORDİNE ÖZET", c => FenKlasoruBelgeleri.KoordineOzet(c, p));
        Ekle("ALAN HESABI", c => FenKlasoruBelgeleri.AlanHesabi(c, p));
        for (var i = 0; i < cepheli.Count; i++)
        {
            var n = i + 1;
            var serit = cepheli[i];
            Ekle("ALAN HESABI-" + n.ToString(CultureInfo.InvariantCulture), c => FenKlasoruBelgeleri.CepheliAlanHesabi(c, p, n, serit));
        }

        Ekle("KOORDİNE-ALAN", c =>
        {
            FenKlasoruBelgeleri.KoordineOzet(c, p);
            FenKlasoruBelgeleri.AlanHesabi(c, p);
            for (var i = 0; i < cepheli.Count; i++)
            {
                FenKlasoruBelgeleri.CepheliAlanHesabi(c, p, i + 1, cepheli[i]);
            }
        });
        Ekle("DOP", c => FenKlasoruBelgeleri.Dop(c, p));
        Ekle("ÖZET CETVELİ", c => FenKlasoruBelgeleri.OzetCetveli(c, p));
        Ekle("MAL SAHİPLERİ ARAŞTIRMA VE ÖZET FORMU", c => FenKlasoruBelgeleri.MalSahipleri(c, p));
        Ekle("TESCİLE ESAS DAĞITIM CETVELİ", c => FenKlasoruBelgeleri.TesciliEsasDagitim(c, p));

        var adalar = FenKlasoruVerisi.ImarAdalari(p);
        for (var i = 0; i < adalar.Count; i++)
        {
            var n = i + 1;
            var ada = adalar[i];
            Ekle("TESCİL SAYFASI-" + n.ToString(CultureInfo.InvariantCulture), c => FenKlasoruBelgeleri.TescilSayfasi(c, p, ada, n));
        }

        if (adalar.Count > 1)
        {
            Ekle("TESCİL-" + Sira(adalar.Count), c =>
            {
                for (var i = 0; i < adalar.Count; i++)
                {
                    FenKlasoruBelgeleri.TescilSayfasi(c, p, adalar[i], i + 1);
                }
            });
        }

        Ekle("RÖLÖVE ÖLÇÜ KROKİSİ", c => FenKlasoruBelgeleri.Roleve(c, p, null));
        for (var i = 0; i < adalar.Count; i++)
        {
            var ada = adalar[i];
            Ekle("RÖLÖVE-" + (i + 1).ToString(CultureInfo.InvariantCulture), c => FenKlasoruBelgeleri.Roleve(c, p, ada));
        }

        if (adalar.Count > 1)
        {
            Ekle("RÖLÖVE-" + Sira(adalar.Count), c =>
            {
                foreach (var ada in adalar)
                {
                    FenKlasoruBelgeleri.Roleve(c, p, ada);
                }
            });
        }

        Ekle("PARSELASYON HARİTASI", c => FenKlasoruBelgeleri.ParselasyonHaritasi(c, p));

        // CKS yalnız Netcad alanlarıyla eşlenebiliyorsa (D1 = D2 = R3 = 0; SORU-033/034). İçeride damga yok (SORU-039);
        // istisna koşulu gereği taslak izi dosya adındaki PILOT ibaresi ve yanındaki .sha256 girişiyle taşınır (reviewer H1).
        if (CksOzetiEslemesi.Olustur(p.Dop) is { IsSuccess: true } cks && CksDosyaUretici.Yaz(cks.Value) is { IsSuccess: true } cksBayt)
        {
            var bayt = cksBayt.Value;
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bayt));
            liste.Add(new FenKlasoruDosyasi(CksDosyaAdi, () => bayt));
            liste.Add(new FenKlasoruDosyasi(CksDosyaAdi + ".sha256", () => Encoding.UTF8.GetBytes(sha + "  " + CksDosyaAdi + "\n")));
        }

        return liste;
    }

    /// <summary>"1-2-3" (birleşik dosya adı eki).</summary>
    internal static string Sira(int adet) => string.Join('-', Enumerable.Range(1, adet).Select(i => i.ToString(CultureInfo.InvariantCulture)));

    private static byte[] Pdf(CiktiPaketi p, string ad, Action<IDocumentContainer> bilesen)
    {
        var belge = Document.Create(bilesen);
        belge.WithMetadata(new DocumentMetadata
        {
            Title = ad + " (taslak) — " + p.ProjeAdi,
            Subject = p.PilotDamgasi + " · RevisionId " + p.RevizyonId,
            Author = "Imar18Studio Pilot",
            Creator = "Imar18Studio Pilot",
            Producer = "Imar18Studio Pilot",
            Keywords = p.SablonSurumu,
            CreationDate = p.OnayZamani,
            ModifiedDate = p.OnayZamani,
        });
        return belge.GeneratePdf();
    }

    /// <summary>ZIP (DOS) zaman aralığına (1980–2107) sıkıştırılmış onay zamanı.</summary>
    private static DateTimeOffset ZipZamani(DateTimeOffset onay)
    {
        var min = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var max = new DateTimeOffset(2107, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var z = onay < min ? min : onay > max ? max : onay;
        return new DateTimeOffset(z.UtcDateTime.Year, z.UtcDateTime.Month, z.UtcDateTime.Day, z.UtcDateTime.Hour, z.UtcDateTime.Minute, z.UtcDateTime.Second, TimeSpan.Zero);
    }
}
