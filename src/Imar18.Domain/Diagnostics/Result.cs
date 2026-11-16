namespace Imar18.Domain.Diagnostics;

/// <summary>
/// Başarı değeri veya <see cref="AppError"/> taşıyan sonuç. <c>default(Result&lt;T&gt;)</c> başarısız sayılır
/// ve genel bilinmeyen hata kodunu taşır.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly AppError? _error;
    private readonly bool _isSuccess;

    private Result(T? value, AppError? error, bool isSuccess)
    {
        _value = value;
        _error = error;
        _isSuccess = isSuccess;
    }

    /// <summary>Başarılı mı? (default değer başarısızdır.)</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>Başarısız mı?</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Başarı değeri.</summary>
    /// <exception cref="InvalidOperationException">Sonuç başarısızsa.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Başarısız sonucun değeri yoktur: " + Error.Code);

    /// <summary>Hata kaydı.</summary>
    /// <exception cref="InvalidOperationException">Sonuç başarılıysa.</exception>
    public AppError Error => IsSuccess
        ? throw new InvalidOperationException("Başarılı sonucun hatası yoktur.")
        : _error ?? ErrorCatalog.Get(null);

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(value, null, true);
    }

    public static Result<T> Failure(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error, false);
    }

    /// <summary>Başarılıysa değeri verir.</summary>
    public bool TryGetValue(out T value)
    {
        value = IsSuccess ? _value! : default!;
        return IsSuccess;
    }
}
