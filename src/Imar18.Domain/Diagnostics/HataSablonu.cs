namespace Imar18.Domain.Diagnostics;

/// <summary>Katalog kaydının teknik mesajındaki {yerTutucu} alanlarını doldurur (kişisel veri yazılmaz — KVKK).</summary>
internal static class HataSablonu
{
    public static AppError Doldur(string code, params (string YerTutucu, string Deger)[] alanlar)
    {
        var error = ErrorCatalog.Get(code);
        var mesaj = error.TechnicalMessage;
        foreach (var (yerTutucu, deger) in alanlar)
        {
            mesaj = mesaj.Replace("{" + yerTutucu + "}", deger, StringComparison.Ordinal);
        }

        return error with { TechnicalMessage = mesaj };
    }

    public static Result<T> Basarisiz<T>(string code, params (string YerTutucu, string Deger)[] alanlar) =>
        Result<T>.Failure(Doldur(code, alanlar));

    public static string M2(decimal deger) => deger.ToString("0.00##########", System.Globalization.CultureInfo.InvariantCulture);
}
