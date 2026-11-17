using Imar18.Domain.Diagnostics;

namespace Imar18.Application.Cad;

/// <summary>CAD ice aktarma asamasi.</summary>
public enum CadImportStage
{
    Reading,
    Completed,
    Failed,
}

/// <summary>Ice aktarma ilerleme bildirimi (yuzde 0..100).</summary>
public sealed record CadImportProgress(CadImportStage Stage, int Percent);

/// <summary>
/// CAD ice aktarma sarmalayicisi (P4.3, P2 reviewer L6, P4.1 reviewer L11). architecture.md geregi 50 ms ustu islem
/// arka planda calisir ve <see cref="IProgress{T}"/> / <see cref="CancellationToken"/> destekler.
/// <list type="bullet">
/// <item>Uzantiya gore adaptor secilir; desteklenmeyen uzanti veya sinir ustu akis okunmadan reddedilir (ERR-GIRD-001).</item>
/// <item>Okuma <see cref="Task.Run(Func{TResult})"/> ile arka planda yapilir; iptalde sonuc beklenmez
/// (<see cref="Task.WaitAsync(CancellationToken)"/>). ACadSharp okumasi iptal edilemedigi icin arka plan gorevi
/// tamamlanana kadar bellek tutabilir; sonucu atilir.</item>
/// <item>Adaptor istisnasi Result'a cevrilir; ic mesaj (deger icerebilir) yazilmaz.</item>
/// </list>
/// </summary>
public sealed class CadImportService
{
    /// <summary>Varsayilan ust sinir (DXF/DWG adaptor siniriyla ayni).</summary>
    public const long DefaultMaxFileSizeBytes = 256L * 1024 * 1024;

    private readonly ICadFormatAdapter[] _adapters;
    private readonly long _maxFileSizeBytes;
    private Task? _pendingRead;

    public CadImportService(IEnumerable<ICadFormatAdapter> adapters, long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = [.. adapters];
        if (_adapters.Length == 0)
        {
            throw new ArgumentException("At least one CAD adapter is required.", nameof(adapters));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    /// <summary>Kayitli adaptorlerin bicimleri (kayit sirasiyla).</summary>
    public IReadOnlyList<CadFormat> SupportedFormats => _adapters.Select(a => a.Format).ToList().AsReadOnly();

    public async Task<Result<CadLayerDocument>> ImportAsync(
        Stream source,
        string extension,
        IProgress<CadImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(extension);
        cancellationToken.ThrowIfCancellationRequested();

        if (_pendingRead is { IsCompleted: false })
        {
            // Iptal edilen onceki okuma (iptal edilemeyen ACadSharp) bitmeden yeni okuma baslatilmaz: bellek birikmez.
            return Fail("previous import is still finishing in the background", progress);
        }

        var adapter = _adapters.FirstOrDefault(a => a.SupportsExtension(extension.Trim()));
        if (adapter is null)
        {
            return Fail("unsupported extension", progress);
        }

        if (source.CanSeek && source.Length - source.Position > _maxFileSizeBytes)
        {
            return Fail("file too large for import", progress);
        }

        progress?.Report(new CadImportProgress(CadImportStage.Reading, 0));
        var reading = Task.Run(
            () =>
            {
                try
                {
                    return adapter.Read(source, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Adaptor istisnasi AC-CAD-03 geregi Result'a cevrilir.
                catch (Exception)
#pragma warning restore CA1031
                {
                    return FailResult("adapter failed unexpectedly");
                }
            },
            CancellationToken.None);
        _pendingRead = reading;

        var result = await reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new CadImportProgress(result.IsSuccess ? CadImportStage.Completed : CadImportStage.Failed, 100));
        return result;
    }

    /// <summary>Iptal edilmis bir onceki okumanin arka planda bitmesini bekler (yoksa hemen doner).</summary>
    public async Task WaitForBackgroundReadAsync()
    {
        if (_pendingRead is { } pending)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Terk edilmis okumanin sonucu/istisnasi onemsizdir.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    private static Result<CadLayerDocument> Fail(string reason, IProgress<CadImportProgress>? progress)
    {
        progress?.Report(new CadImportProgress(CadImportStage.Failed, 100));
        return FailResult(reason);
    }

    private static Result<CadLayerDocument> FailResult(string reason)
    {
        var e = ErrorCatalog.Get(ErrorCodes.GirdCadOkunamadi);
        return Result<CadLayerDocument>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage.Replace("{path}", $"input ({reason})", StringComparison.Ordinal),
        });
    }
}
