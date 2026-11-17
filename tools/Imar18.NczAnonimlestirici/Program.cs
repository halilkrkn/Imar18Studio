using System.Globalization;
using System.Text.Json;
using Imar18.Infrastructure.Cad.Ncz;

// P6.0 (D15) — NCZ anonim koordinat fixture aracı. Yalnızca yerelde, proje sahibi kararıyla çalıştırılır.
// Kullanım:
//   dotnet run --project tools/Imar18.NczAnonimlestirici -- --girdi <kaynak.ncz> --cikti <test-data/synthetic/.../x.ncz>
//       [--ofset-kuzey <m> --ofset-dogu <m>] [--etiket-esleme <esleme.json>] [--katman-esleme <esleme.json>]
//       [--yazilmis-alani-koru] [--uzerine-yaz]
// Ofset verilmezse IMAR18_NCZ_OFSET_KUZEY / IMAR18_NCZ_OFSET_DOGU ortam değişkenlerinden okunur (kabuk geçmişine düşmemesi için
// tercih edilir). Ofset ve eşleme dosyalarını depoya yazmayın (gerçek konuma geri dönüş anahtarıdır). Çıktıya yalnız sayaçlar yazılır.
var secenekler = Ayristir(args);
if (secenekler is null)
{
    Console.Error.WriteLine(
        "Kullanım: --girdi <kaynak.ncz> --cikti <test-data/synthetic/.../x.ncz> [--ofset-kuzey <m> --ofset-dogu <m>] " +
        "[--etiket-esleme <esleme.json>] [--katman-esleme <esleme.json>] [--yazilmis-alani-koru] [--uzerine-yaz] " +
        "(ofset yoksa IMAR18_NCZ_OFSET_KUZEY / IMAR18_NCZ_OFSET_DOGU)");
    return 2;
}

try
{
    var ayarlar = new NczAnonimlestirmeAyarlari(
        Ofset("--ofset-kuzey", "IMAR18_NCZ_OFSET_KUZEY"),
        Ofset("--ofset-dogu", "IMAR18_NCZ_OFSET_DOGU"),
        await EslemeAsync("--etiket-esleme"),
        KatmanEslemesi: await EslemeAsync("--katman-esleme"),
        YazilmisAlaniKoru: secenekler.ContainsKey("--yazilmis-alani-koru"));
    var sonuc = await NczFixtureUretici.UretAsync(secenekler["--girdi"], secenekler["--cikti"], ayarlar, secenekler.ContainsKey("--uzerine-yaz"));
    if (sonuc.IsFailure)
    {
        Console.Error.WriteLine($"{sonuc.Error.Code}: {sonuc.Error.UserMessage}");
        return 1;
    }

    var s = sonuc.Value;
    Console.WriteLine(
        $"Anonim fixture yazıldı: {s.PoligonSayisi} poligon, {s.AtlananKayitSayisi} kayıt atlandı, {s.EslenenEtiketSayisi} etiket eşlendi, " +
        $"{s.YerTutucuEtiketSayisi} yer tutucu, {s.BosaltilanXmlAlanSayisi} XML alanı boşaltıldı, PNG önizleme silindi: {s.PngOnizlemeSilindi}.");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException)
{
    // Ayrıntı (yol, içerik) yazılmaz; yalnızca hata türü.
    Console.Error.WriteLine("Hata: " + ex.GetType().Name + " — " + (ex is ArgumentException ? ex.Message : "girdi okunamadı veya geçersiz."));
    return 1;
}

decimal Ofset(string secenek, string ortam)
{
    var metin = secenekler.TryGetValue(secenek, out var s) ? s : Environment.GetEnvironmentVariable(ortam)
        ?? throw new ArgumentException($"Ofset eksik: {secenek} veya {ortam}.");
    return decimal.Parse(metin, NumberStyles.Float, CultureInfo.InvariantCulture);
}

async Task<IReadOnlyDictionary<string, string>?> EslemeAsync(string secenek) =>
    secenekler.TryGetValue(secenek, out var yol)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(yol)) ?? throw new ArgumentException("Eşleme dosyası boş.")
        : null;

static Dictionary<string, string>? Ayristir(string[] a)
{
    var d = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < a.Length; i++)
    {
        if (a[i] is "--uzerine-yaz" or "--yazilmis-alani-koru")
        {
            d[a[i]] = "1";
        }
        else if (a[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < a.Length)
        {
            d[a[i]] = a[++i];
        }
        else
        {
            return null;
        }
    }

    return new[] { "--girdi", "--cikti" }.All(d.ContainsKey) ? d : null;
}
