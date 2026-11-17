using System.Globalization;
using Imar18.Domain.Diagnostics;

namespace Imar18.Domain.Geometry;

/// <summary>Desteklenen geodetik datumlar (STATUS SORU-028).</summary>
public enum GeodetikDatum
{
    /// <summary>ITRF96 (TUREF), GRS80 elipsoidi, epok 2005.00.</summary>
    Itrf96,

    /// <summary>ED50, International 1924 (Hayford) elipsoidi.</summary>
    Ed50,
}

/// <summary>Elipsoit tanimi (buyuk yari eksen ve ters basiklik; tam ondalik).</summary>
public sealed record ElipsoidTanimi(string Ad, decimal BuyukYariEksenM, decimal TersBasiklik);

/// <summary>
/// Proje bazli CRS/dilim/olcek girdisi (P4.2, D3 — hardcode yok, SORU-009, SORU-028). Muhendis datum, TM 3° dilim orta
/// meridyeni (27–45), epok ve pafta olcegi paydasini girer; bu sinif yalnizca desteklenen tanimlar icinde dogrular.
/// TM 3° parametreleri: olcek faktoru 1, yana oteleme 500 000 m, kuzeye oteleme 0. Datumlar arasi donusum icin sabit
/// parametre tutulmaz (ortak noktali Helmert/Afin raporu kullanilir).
/// </summary>
public sealed class ProjeCrs
{
    /// <summary>ITRF96 icin beklenen referans epok.</summary>
    public const decimal Itrf96ReferansEpok = 2005.00m;

    private static readonly ElipsoidTanimi Grs80 = new("GRS80", 6_378_137m, 298.257222101m);
    private static readonly ElipsoidTanimi Hayford = new("International 1924 (Hayford)", 6_378_388m, 297m);

    private ProjeCrs(GeodetikDatum datum, int ortaMeridyen, decimal? epok, int olcekPaydasi, IReadOnlyList<AppError> uyarilar)
    {
        Datum = datum;
        OrtaMeridyenDerece = ortaMeridyen;
        Epok = epok;
        OlcekPaydasi = olcekPaydasi;
        Uyarilar = uyarilar;
        Elipsoid = datum == GeodetikDatum.Itrf96 ? Grs80 : Hayford;
        CrsId = datum == GeodetikDatum.Itrf96
            ? $"ITRF96/TM3-{ortaMeridyen}@{EpokMetni(epok!.Value)}"
            : $"ED50/TM3-{ortaMeridyen}";
    }

    /// <summary>Kabul edilen epok araligi (yil) — uygulama guvenlik siniri, mevzuat degeri degildir.</summary>
    public const decimal MinEpok = 1900m;

    /// <summary>Kabul edilen epok ust siniri (yil).</summary>
    public const decimal MaxEpok = 2100m;

    /// <summary>TM 3° dilim orta meridyenleri (derece, Dogu).</summary>
    public static IReadOnlyList<int> DesteklenenOrtaMeridyenler { get; } = Array.AsReadOnly(new[] { 27, 30, 33, 36, 39, 42, 45 });

    public GeodetikDatum Datum { get; }

    public int OrtaMeridyenDerece { get; }

    public decimal? Epok { get; }

    public int OlcekPaydasi { get; }

    public ElipsoidTanimi Elipsoid { get; }

    /// <summary>TM 3° olcek faktoru.</summary>
    public decimal OlcekFaktoru => 1m;

    /// <summary>Yana (Dogu) oteleme, metre.</summary>
    public decimal YanaOtelemeM => 500_000m;

    /// <summary>Kanonik CRS kimligi (ornek "ITRF96/TM3-30@2005.00", "ED50/TM3-30").</summary>
    public string CrsId { get; }

    /// <summary>Olusturma uyarilari (ornek ERR-CRS-002 epok uyumsuzlugu).</summary>
    public IReadOnlyList<AppError> Uyarilar { get; }

    /// <returns>
    /// Desteklenmeyen orta meridyen, pozitif olmayan olcek paydasi veya ED50'de epok: ERR-CRS-003; ITRF96'da epok yok: ERR-CRS-001;
    /// ITRF96 epok 2005.00 degil: basari + ERR-CRS-002 uyarisi.
    /// </returns>
    public static Result<ProjeCrs> Olustur(GeodetikDatum datum, int ortaMeridyenDerece, decimal? epok, int olcekPaydasi)
    {
        if (!DesteklenenOrtaMeridyenler.Contains(ortaMeridyenDerece))
        {
            return Gecersiz("central meridian must be one of TM3 zones 27..45");
        }

        if (olcekPaydasi <= 0)
        {
            return Gecersiz("map scale denominator must be positive");
        }

        if (epok is { } e && (e < MinEpok || e > MaxEpok))
        {
            return Gecersiz("epoch must be between 1900 and 2100");
        }

        IReadOnlyList<AppError> uyarilar = [];
        if (datum == GeodetikDatum.Itrf96)
        {
            if (epok is null)
            {
                var eksik = ErrorCatalog.Get(ErrorCodes.CrsEksik);
                return Result<ProjeCrs>.Failure(eksik with
                {
                    TechnicalMessage = eksik.TechnicalMessage.Replace("{projectId}", "project (ITRF96 epoch)", StringComparison.Ordinal),
                });
            }

            if (epok.Value != Itrf96ReferansEpok)
            {
                var u = ErrorCatalog.Get(ErrorCodes.CrsEpokUyumsuz);
                uyarilar = [u with { TechnicalMessage = u.TechnicalMessage
                    .Replace("{sourceEpoch}", EpokMetni(epok.Value), StringComparison.Ordinal)
                    .Replace("{targetEpoch}", "2005.00", StringComparison.Ordinal) }];
            }
        }
        else if (epok is not null)
        {
            return Gecersiz("ED50 does not take an epoch");
        }

        return Result<ProjeCrs>.Success(new ProjeCrs(datum, ortaMeridyenDerece, epok, olcekPaydasi, uyarilar));
    }

    /// <summary>P2 dogrulama sozlesmesine (<see cref="ProjectCoordinateReference"/>) cevirir.</summary>
    public ProjectCoordinateReference ToCoordinateReference() =>
        ProjectCoordinateReference.Create(CrsId, OrtaMeridyenDerece, Epok, OlcekPaydasi).Value;

    /// <summary>Epok metni: en az 2, en fazla 6 ondalik (2005 → "2005.00", 2005.004 → "2005.004").</summary>
    private static string EpokMetni(decimal epok) => epok.ToString("0.00####", CultureInfo.InvariantCulture);

    private static Result<ProjeCrs> Gecersiz(string reason)
    {
        var e = ErrorCatalog.Get(ErrorCodes.CrsGirdisiGecersiz);
        return Result<ProjeCrs>.Failure(e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", reason, StringComparison.Ordinal) });
    }
}

/// <summary>Donusum modeli (P4.2 2B Helmert/Afin raporu).</summary>
public enum DonusumModeli
{
    /// <summary>4 parametreli benzerlik (olcek + donme + 2 oteleme).</summary>
    Helmert2B,

    /// <summary>6 parametreli afin.</summary>
    Afin2B,
}

/// <summary>Kaynak ve hedef sistemde koordinati bilinen ortak nokta.</summary>
public sealed record OrtakNokta(string NoktaId, GeodeticPoint Kaynak, GeodeticPoint Hedef);
