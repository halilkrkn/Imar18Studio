using Imar18.Application.Cikti;
using Imar18.Infrastructure.Proje;

namespace Imar18.Infrastructure.Cikti;

/// <summary>Çıktı dosyalarını <see cref="AtomikDosyaYazici"/> ile yarım yazmaya karşı korumalı yazar (FR-F6.10).</summary>
public sealed class AtomikCiktiYazici : ICiktiYazici
{
    public Task YazAsync(string yol, ReadOnlyMemory<byte> icerik, CancellationToken cancellationToken = default) =>
        AtomikDosyaYazici.YazAsync(yol, icerik, null, cancellationToken, yedekTut: false); // çıktı klasöründe .bak bırakılmaz (reviewer L1)

    public Task SilAsync(string yol, CancellationToken cancellationToken = default)
    {
        if (File.Exists(yol))
        {
            File.Delete(yol);
        }

        return Task.CompletedTask;
    }
}
