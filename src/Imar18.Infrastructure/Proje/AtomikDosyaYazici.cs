namespace Imar18.Infrastructure.Proje;

/// <summary>
/// Crash-safe atomik dosya yazımı (FR-F6.10, AC-IO-01): içerik aynı klasördeki <c>.tmp</c> dosyasına yazılır ve diske
/// zorlanır (<c>Flush(true)</c>); ardından hedef varsa <see cref="File.Replace(string, string, string?, bool)"/> ile
/// (önceki sürüm <c>.bak</c> olarak korunur), yoksa <see cref="File.Move(string, string)"/> ile yer değiştirilir.
/// Yer değiştirmeden önce kesilen yazım hedef dosyayı bozmaz.
/// </summary>
internal static class AtomikDosyaYazici
{
    public static async Task YazAsync(string hedefYol, ReadOnlyMemory<byte> icerik, Action? yerDegistirmeOncesi, CancellationToken cancellationToken, bool yedekTut = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hedefYol);
        var tam = Path.GetFullPath(hedefYol);
        var klasor = Path.GetDirectoryName(tam) ?? throw new IOException("Hedef klasör çözülemedi.");
        Directory.CreateDirectory(klasor);
        var gecici = tam + ".tmp";

        try
        {
            await using (var akis = new FileStream(gecici, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                await akis.WriteAsync(icerik, cancellationToken).ConfigureAwait(false);
                akis.Flush(flushToDisk: true);
            }

            yerDegistirmeOncesi?.Invoke();

            if (File.Exists(tam))
            {
                File.Replace(gecici, tam, yedekTut ? tam + ".bak" : null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(gecici, tam);
            }
        }
        finally
        {
            if (File.Exists(gecici))
            {
                File.Delete(gecici);
            }
        }
    }
}
