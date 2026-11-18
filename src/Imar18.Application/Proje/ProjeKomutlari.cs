using System.Globalization;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;

namespace Imar18.Application.Proje;

/// <summary>
/// CommandBus komutu: mevcut belgeden yeni belge üretir veya reddeder. Saf fonksiyondur (G/Ç yapmaz); kalıcılık ve denetim
/// <see cref="ProjeKomutYolu"/>'ndadır. <see cref="Ayrinti"/> denetime yazılır ve kişisel veri içermemelidir.
/// </summary>
public interface IProjeKomutu
{
    /// <summary>Denetimdeki işlem adı.</summary>
    string Ad { get; }

    /// <summary>Denetim ayrıntısı (yalnızca sayım/kod).</summary>
    string? Ayrinti => null;

    Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut);
}

/// <summary>
/// Onay alanını değiştirmeye yetkili komut işareti (P5 reviewer H1). Yalnızca <see cref="Onay.ApprovalService"/>'in özel
/// komutları taşır; başka bir komut <see cref="ProjeBelgesi.Onay"/>'ı değiştirirse CommandBus reddeder ve denetime yazar.
/// </summary>
internal interface IOnayKomutu : IProjeKomutu;

internal static class KomutHatasi
{
    public static Result<ProjeBelgesi> Gecersiz(string komut, string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz);
        return Result<ProjeBelgesi>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage.Replace("{command}", komut, StringComparison.Ordinal).Replace("{reason}", neden, StringComparison.Ordinal),
        });
    }

    public static string Sayi(int n) => n.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Proje adını değiştirir (adı çıktıda görünür; revizyona dahildir).</summary>
public sealed record ProjeAdiDegistirKomutu(string ProjeAdi) : IProjeKomutu
{
    public string Ad => "ProjeAdiDegistir";

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut) =>
        string.IsNullOrWhiteSpace(ProjeAdi) || ProjeAdi.Trim().Length > 200
            ? KomutHatasi.Gecersiz(Ad, "project name must be 1..200 characters")
            : Result<ProjeBelgesi>.Success(mevcut with { ProjeAdi = ProjeAdi.Trim() });
}

/// <summary>
/// P7 (Projeyi Temizle / Sıfırla): CRS, parsel geometrisi, tecviz, DOP, hak eşleştirme ve kimlik eşlemesini boşaltır; proje adı
/// korunur. Onay alanına dokunmaz: içerik revizyonu değiştiği için mevcut onay CommandBus tarafından otomatik düşürülür
/// (<see cref="AuditSonucu.OnayDustu"/>) ve sıfırlama denetime yazılır. Zaten boş projede de başarılıdır (idempotent).
/// </summary>
public sealed record ProjeyiSifirlaKomutu : IProjeKomutu
{
    public string Ad => "ProjeyiSifirla";

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        ArgumentNullException.ThrowIfNull(mevcut);
        return Result<ProjeBelgesi>.Success(mevcut with
        {
            Crs = null,
            Parseller = [],
            Dop = null,
            TecvizParselleri = [],
            DuzenlemeToplamTecvizSiniriM2 = null,
            HakEslestirme = null,
            KimlikCiftleri = [],
            MalikRehberi = Girdi.MalikRehberi.Bos,
        });
    }
}

/// <summary>
/// P7.3-B1 (SAHA-006): yerel Malik Rehberi'ni tümüyle değiştirir (webtapu aktarımı). Kodlar benzersiz ve TAKBİS anonim kod
/// biçiminde (1–32 ASCII harf/rakam/-_.), adlar boş olmamalıdır. Denetime yalnız sayım yazılır; ad/kimlik yazılmaz (KVKK).
/// Rehber RevisionId'ye girmediği için onay düşmez.
/// </summary>
public sealed record MalikRehberiAyarlaKomutu(Girdi.MalikRehberi Rehber) : IProjeKomutu
{
    public string Ad => "MalikRehberiAyarla";

    public string? Ayrinti => Rehber is null
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"malik={Rehber.Malikler?.Count ?? 0} parsel={Rehber.Parseller?.Count ?? 0}");

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        ArgumentNullException.ThrowIfNull(mevcut);
        if (Rehber?.Malikler is null || Rehber.Parseller is null || Rehber.Malikler.Any(m => m is null) || Rehber.Parseller.Any(p => p is null))
        {
            return KomutHatasi.Gecersiz(Ad, "owner directory lists must not be null");
        }

        var kodlar = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Rehber.Malikler)
        {
            var kod = m.MalikKodu?.Trim() ?? string.Empty;
            if (kod.Length is 0 or > 32 || !kod.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                return KomutHatasi.Gecersiz(Ad, "owner code must be an anonymous code (ASCII letters/digits/-/_/.)");
            }

            if (!kodlar.Add(kod))
            {
                return KomutHatasi.Gecersiz(Ad, "duplicate owner code");
            }

            if (string.IsNullOrWhiteSpace(m.AdSoyad))
            {
                return KomutHatasi.Gecersiz(Ad, "owner name must not be empty");
            }
        }

        if (Rehber.Parseller.Any(p => string.IsNullOrWhiteSpace(p.KadastroParselId))
            || Rehber.Parseller.GroupBy(p => p.KadastroParselId.Trim(), StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            return KomutHatasi.Gecersiz(Ad, "cadastral parcel ids must be non-empty and unique");
        }

        return Result<ProjeBelgesi>.Success(mevcut with { MalikRehberi = Rehber });
    }
}

/// <summary>Proje CRS/dilim/epok/ölçek girdisini ayarlar (D3; doğrulama <see cref="ProjeCrs.Olustur"/>'dadır).</summary>
public sealed record ProjeCrsAyarlaKomutu(ProjeCrs Crs) : IProjeKomutu
{
    public string Ad => "ProjeCrsAyarla";

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut) =>
        Crs is null ? KomutHatasi.Gecersiz(Ad, "CRS is null") : Result<ProjeBelgesi>.Success(mevcut with { Crs = Crs });
}

/// <summary>
/// Parsel geometrilerinin tamamını değiştirir (CAD içe aktarma veya mühendis düzenlemesi). Kimlikler boş olamaz ve benzersizdir;
/// her halka <see cref="PolygonRing.FromOpen"/> denetiminden geçmelidir (aksi ilgili ERR-TOPO/ERR-GIRD kodu).
/// </summary>
public sealed record ParselGeometrileriAyarlaKomutu(IReadOnlyList<ParselGeometrisi> Parseller) : IProjeKomutu
{
    public string Ad => "ParselGeometrileriAyarla";

    public string? Ayrinti => "parselSayisi=" + KomutHatasi.Sayi(Parseller?.Count ?? 0);

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        if (Parseller is null || Parseller.Any(p => p?.Koseler is null))
        {
            return KomutHatasi.Gecersiz(Ad, "parcel list or vertex list is null");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Parseller.Count; i++)
        {
            var p = Parseller[i];
            if (string.IsNullOrWhiteSpace(p.ParselId) || p.ParselId.Length > 64)
            {
                return KomutHatasi.Gecersiz(Ad, $"empty or too long parcel id at item #{i + 1}");
            }

            if (!ids.Add(p.ParselId))
            {
                return KomutHatasi.Gecersiz(Ad, $"duplicate parcel id at item #{i + 1}");
            }

            if (!Enum.IsDefined(p.Rol))
            {
                return KomutHatasi.Gecersiz(Ad, $"undefined parcel role at item #{i + 1}");
            }

            var halka = PolygonRing.FromOpen(p.Koseler);
            if (halka.IsFailure)
            {
                return Result<ProjeBelgesi>.Failure(halka.Error with { TechnicalMessage = halka.Error.TechnicalMessage + $" (item #{i + 1})" });
            }
        }

        return Result<ProjeBelgesi>.Success(mevcut with { Parseller = Parseller.ToArray() });
    }
}

/// <summary>Tecviz parsel girdilerini (mühendis girdisi, D1) ayarlar; doğrulama <see cref="TecvizChecker"/> ile yapılır.</summary>
public sealed record TecvizGirdileriAyarlaKomutu(IReadOnlyList<TecvizParselGirdisi> Parseller, decimal? DuzenlemeToplamTecvizSiniriM2) : IProjeKomutu
{
    public string Ad => "TecvizGirdileriAyarla";

    public string? Ayrinti => "parselSayisi=" + KomutHatasi.Sayi(Parseller?.Count ?? 0);

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        if (Parseller is null)
        {
            return KomutHatasi.Gecersiz(Ad, "parcel list is null");
        }

        var kontrol = TecvizChecker.Degerlendir(Parseller, DuzenlemeToplamTecvizSiniriM2);
        return kontrol.IsFailure
            ? Result<ProjeBelgesi>.Failure(kontrol.Error)
            : Result<ProjeBelgesi>.Success(mevcut with { TecvizParselleri = Parseller.ToArray(), DuzenlemeToplamTecvizSiniriM2 = DuzenlemeToplamTecvizSiniriM2 });
    }
}

/// <summary>Resmî DOP sayfası girdisini ayarlar (doğrulama <see cref="DopGirdisi.Olustur"/>'dadır).</summary>
public sealed record DopGirdisiAyarlaKomutu(DopGirdisi Dop) : IProjeKomutu
{
    public string Ad => "DopGirdisiAyarla";

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut) =>
        Dop is null ? KomutHatasi.Gecersiz(Ad, "DOP input is null") : Result<ProjeBelgesi>.Success(mevcut with { Dop = Dop });
}

/// <summary>Hak eşleştirme girdisini (FR-F1.12, mühendis girdisi) ayarlar; iş kuralları onay değerlendirmesinde çalışır.</summary>
public sealed record HakEslestirmeAyarlaKomutu(HakEslestirmeGirdisi HakEslestirme) : IProjeKomutu
{
    public string Ad => "HakEslestirmeAyarla";

    public string? Ayrinti => HakEslestirme is null
        ? null
        : "kadastro=" + KomutHatasi.Sayi(HakEslestirme.KadastroParselleri?.Count ?? 0) + ";imar=" + KomutHatasi.Sayi(HakEslestirme.ImarParselleri?.Count ?? 0)
            + ";satir=" + KomutHatasi.Sayi(HakEslestirme.Eslestirme?.Count ?? 0);

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        if (HakEslestirme?.KadastroParselleri is null || HakEslestirme.ImarParselleri is null || HakEslestirme.Eslestirme is null
            || HakEslestirme.KadastroParselleri.Any(k => k?.Malikler is null || k.Malikler.Any(m => m is null))
            || HakEslestirme.ImarParselleri.Any(p => p is null) || HakEslestirme.Eslestirme.Any(s => s is null))
        {
            return KomutHatasi.Gecersiz(Ad, "allocation input or one of its lists is null");
        }

        return Result<ProjeBelgesi>.Success(mevcut with { HakEslestirme = HakEslestirme });
    }
}

/// <summary>Geçici imar kodu ↔ kalıcı tescil no eşlemesini ayarlar (spec 25.3; doğrulama <see cref="ParselKimlikEslemesi"/>).</summary>
public sealed record ParselKimlikEslemesiAyarlaKomutu(IReadOnlyList<ParselKimlikCifti> Ciftler) : IProjeKomutu
{
    public string Ad => "ParselKimlikEslemesiAyarla";

    public string? Ayrinti => "ciftSayisi=" + KomutHatasi.Sayi(Ciftler?.Count ?? 0);

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        if (Ciftler is null)
        {
            return KomutHatasi.Gecersiz(Ad, "pair list is null");
        }

        var eslesme = ParselKimlikEslemesi.Olustur(Ciftler);
        return eslesme.IsFailure
            ? Result<ProjeBelgesi>.Failure(eslesme.Error)
            : Result<ProjeBelgesi>.Success(mevcut with { KimlikCiftleri = eslesme.Value.Ciftler });
    }
}
