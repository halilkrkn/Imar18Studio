using System.Globalization;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Dop;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Mevzuat.Tecviz;
using Imar18.Domain.Revizyon;

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
            Donusum = null,
            Kunye = null,
            NczSablonu = null,
            IleriDonusum = null,
            DepremOncesiParseller = null,
            ArsivKadastroNoktalari = [],
        });
    }
}

/// <summary>
/// P7.3-B1 (SAHA-006): yerel Malik Rehberi'ni tümüyle değiştirir (webtapu aktarımı). Kodlar benzersiz (yazım farkından bağımsız)
/// ve TAKBİS malik alanı kuralında (SORU-055: <c>AD SOYAD (BABA ADI)</c> veya eski kod; T.C. Kimlik No yok), adlar boş olmamalıdır. Denetime yalnız sayım yazılır; ad/kimlik yazılmaz (KVKK).
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
            if (!Girdi.MalikEtiketi.GecerliMi(kod))
            {
                return KomutHatasi.Gecersiz(Ad, "owner code must be a name label or code without a national id number (KVKK)");
            }

            if (!kodlar.Add(Girdi.MalikEtiketi.Anahtar(kod)))
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

/// <summary>SAHA-014: <see cref="ParselGeometrileriAyarlaKomutu"/> girdisinin koordinat sistemi.</summary>
public enum ParselKoordinatSistemi
{
    /// <summary>CAD dosyasının kendi koordinatları (Deprem Öncesi). <c>.DNS</c> ileri dönüşümü etkinse komut dönüştürür.</summary>
    KaynakCad,

    /// <summary>Projenin aktif koordinatları. İleri dönüşüm etkinken orijinal türetilemediği için reddedilir.</summary>
    Aktif,
}

/// <summary>
/// Parsel geometrilerinin tamamını değiştirir (CAD içe aktarma veya mühendis düzenlemesi). Kimlikler boş olamaz ve benzersizdir;
/// her halka <see cref="PolygonRing.FromOpen"/> denetiminden geçmelidir (aksi ilgili ERR-TOPO/ERR-GIRD kodu).
/// SAHA-014: <c>.DNS</c> ileri dönüşümü etkinse (<see cref="ProjeBelgesi.IleriDonusum"/>) <see cref="ParselKoordinatSistemi.KaynakCad"/>
/// girdisi Deprem Öncesi sayılır: <see cref="ProjeBelgesi.DepremOncesiParseller"/> girdinin kendisi, <see cref="ProjeBelgesi.Parseller"/>
/// ileri dönüşümü olur (rol değişiminde yeniden eşleme asla dönüşümsüz kalmaz, çift dönüşmez).
/// </summary>
public sealed record ParselGeometrileriAyarlaKomutu(IReadOnlyList<ParselGeometrisi> Parseller) : IProjeKomutu
{
    public string Ad => "ParselGeometrileriAyarla";

    /// <summary>SAHA-014: girdinin koordinat sistemi (varsayılan CAD kaynağı / Deprem Öncesi).</summary>
    public ParselKoordinatSistemi Sistem { get; init; } = ParselKoordinatSistemi.KaynakCad;

    /// <summary>SAHA-010: R2 çift sayımını önlemek için projeye alınmayan imar adası / yinelenen / kabuk poligon sayısı (denetim izi).</summary>
    public int CiftSayimHaricSayisi { get; init; }

    /// <summary>SAHA-015: geometri mühendisin onayladığı Yaklaşık Yer Krokisi parselasyonundan geliyor (denetim izi; yalnız işaret).</summary>
    public bool KrokiParselasyonu { get; init; }

    /// <summary>SAHA-015 (architect K1): bu yeniden eşlemeyle yerine geçilen, oturumda onaylanmış kroki parselasyonunun parsel sayısı (denetim izi).</summary>
    public int KrokiParselasyonuEzildi { get; init; }

    public string? Ayrinti => "parselSayisi=" + KomutHatasi.Sayi(Parseller?.Count ?? 0)
        + (CiftSayimHaricSayisi > 0 ? ";ciftSayimHaric=" + KomutHatasi.Sayi(CiftSayimHaricSayisi) : string.Empty)
        + (KrokiParselasyonu ? ";kaynak=yaklasikYerKrokisi" : string.Empty)
        + (KrokiParselasyonuEzildi > 0 ? ";krokiParselasyonuEzildi=" + KomutHatasi.Sayi(KrokiParselasyonuEzildi) : string.Empty);

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

        if (!Enum.IsDefined(Sistem))
        {
            return KomutHatasi.Gecersiz(Ad, "undefined coordinate system");
        }

        if (mevcut.IleriDonusum is not { } k)
        {
            return Result<ProjeBelgesi>.Success(mevcut with { Parseller = Parseller.ToArray() });
        }

        if (Sistem == ParselKoordinatSistemi.Aktif)
        {
            return KomutHatasi.Gecersiz(Ad, "active-system parcels cannot be set while a forward (.DNS) transformation is applied");
        }

        var orj = Parseller.ToArray();
        var aktif = IleriDonusumHesabi.Donustur(Ad, orj, k);
        return aktif.IsFailure
            ? Result<ProjeBelgesi>.Failure(aktif.Error)
            : Result<ProjeBelgesi>.Success(mevcut with { Parseller = aktif.Value, DepremOncesiParseller = orj });
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

/// <summary>
/// P7.3-C2: ortak noktalı dönüşüm girdisini ayarlar (<c>null</c> temizler). Yapısal doğrulama: tanımlı model, 0 &lt; MK ≤ 10 m,
/// model için en az nokta (Helmert 2, Afin 3), en çok <see cref="MaxNokta"/>, boş olmayan tekil nokta no (≤ 32 karakter).
/// Hesaplanabilirlik (eşdoğrusallık vb.) DonusumServisi ile önizlemede ve çıktıda denetlenir.
/// Denetim kaydına yalnız model ve nokta sayısı yazılır (koordinat yok).
/// </summary>
public sealed record DonusumGirdisiAyarlaKomutu(DonusumGirdisi? Donusum) : IProjeKomutu
{
    public const int MaxNokta = 500;

    public string Ad => "DonusumGirdisiAyarla";

    public string? Ayrinti => Donusum is null
        ? "donusum=yok"
        : "model=" + Donusum.Model + ";nokta=" + KomutHatasi.Sayi(Donusum.OrtakNoktalar?.Count ?? 0);

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        ArgumentNullException.ThrowIfNull(mevcut);
        if (mevcut.IleriDonusum is not null)
        {
            if (Donusum is not null)
            {
                var e = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz);
                return Result<ProjeBelgesi>.Failure(e with
                {
                    TechnicalMessage = e.TechnicalMessage.Replace("{command}", Ad, StringComparison.Ordinal)
                        .Replace("{reason}", "manual common points cannot replace an applied forward (.DNS) transformation", StringComparison.Ordinal),
                    UserMessage = ".DNS dönüşümü uygulanmış; ortak noktaları elle değiştirmek için önce \"Dönüşümü Kaldır\" kullanın.",
                });
            }

            // SAHA-014 "Dönüşümü Kaldır": aktif geometri Deprem Öncesi orijinallere döner; ters kipe sessiz geçiş olmaz.
            return Result<ProjeBelgesi>.Success(mevcut with
            {
                Parseller = mevcut.DepremOncesiParseller ?? mevcut.Parseller,
                DepremOncesiParseller = null,
                IleriDonusum = null,
                Donusum = null,
            });
        }

        if (Donusum is null)
        {
            return Result<ProjeBelgesi>.Success(mevcut with { Donusum = null });
        }

        var normal = Dogrula(Ad, Donusum);
        return normal.IsFailure
            ? Result<ProjeBelgesi>.Failure(normal.Error)
            : Result<ProjeBelgesi>.Success(mevcut with { Donusum = normal.Value });
    }

    /// <summary>Yapısal doğrulama ve kanonik (NoktaNo ordinal) sıralama; <see cref="IleriDonusumUygulaKomutu"/> da kullanır.</summary>
    internal static Result<DonusumGirdisi> Dogrula(string ad, DonusumGirdisi donusum)
    {
        if (!Enum.IsDefined(donusum.Model))
        {
            return Red("undefined transformation model");
        }

        if (donusum.Mk <= 0m || donusum.Mk > DonusumGirdisi.MaxMk)
        {
            return Red("MK must be in (0, 10] m");
        }

        if (donusum.OrtakNoktalar is null || donusum.OrtakNoktalar.Any(n => n is null))
        {
            return Red("common point list is null");
        }

        var enAz = DonusumGirdisi.EnAzNokta(donusum.Model);
        if (donusum.OrtakNoktalar.Count < enAz || donusum.OrtakNoktalar.Count > MaxNokta)
        {
            return Red($"{donusum.Model} needs {KomutHatasi.Sayi(enAz)}..{KomutHatasi.Sayi(MaxNokta)} common points");
        }

        var nolar = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < donusum.OrtakNoktalar.Count; i++)
        {
            var no = donusum.OrtakNoktalar[i].NoktaNo?.Trim() ?? string.Empty;
            if (no.Length is 0 or > 32 || !nolar.Add(no))
            {
                return Red($"empty, too long or duplicate point no at item #{KomutHatasi.Sayi(i + 1)}");
            }
        }

        // Reviewer M5: RevisionId nokta sırasından bağımsız olduğundan saklanan sıra da kanonik (NoktaNo ordinal) olmalı:
        // aynı revizyon -> aynı PDF/ZIP baytı.
        return Result<DonusumGirdisi>.Success(donusum with
        {
            OrtakNoktalar = donusum.OrtakNoktalar.Select(n => n with { NoktaNo = n.NoktaNo.Trim() }).OrderBy(n => n.NoktaNo, StringComparer.Ordinal).ToArray(),
        });

        Result<DonusumGirdisi> Red(string neden) => Result<DonusumGirdisi>.Failure(KomutHatasi.Gecersiz(ad, neden).Error);
    }
}

/// <summary>
/// SAHA-014: <c>.DNS</c> ile Deprem Öncesi → Deprem Sonrası ileri dönüşüm. Taban her zaman orijinal Deprem Öncesi geometridir
/// (<c>DepremOncesiParseller ?? Parseller</c>): aynı veya farklı <c>.DNS</c> yeniden uygulanınca koordinatlar ikinci kez kaymaz.
/// Aktif <see cref="ProjeBelgesi.Parseller"/> = ileri dönüşüm (6 ondalık); orijinaller saklanır; ortak noktalar/MK
/// <see cref="ProjeBelgesi.Donusum"/>'a, katsayılar birebir <see cref="ProjeBelgesi.IleriDonusum"/>'a yazılır. RevisionId değişir
/// (onay düşer). Parsel yoksa (DNS önce, CAD sonra) yalnız katsayılar kaydedilir; sonraki CAD eşlemesi dönüştürülür.
/// P7.3-C2 elle ters kipi kayıtlıyken (proje noktaları "deprem sonrası" beyan edilmiş) reddedilir — sessiz çift kayma olmaz
/// (reviewer M1); önce "Dönüşümü Kaldır". Denetime yalnız model ve ortak nokta sayısı yazılır (koordinat, katsayı ve dosya adı
/// yazılmaz — KVKK).
/// </summary>
public sealed record IleriDonusumUygulaKomutu(DonusumGirdisi Girdi, DonusumKatsayilari Katsayilar) : IProjeKomutu
{
    public string Ad => "IleriDonusumUygula";

    public string? Ayrinti => Girdi is null
        ? null
        : "model=" + Girdi.Model + ";nokta=" + KomutHatasi.Sayi(Girdi.OrtakNoktalar?.Count ?? 0);

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        ArgumentNullException.ThrowIfNull(mevcut);
        if (Girdi is null)
        {
            return KomutHatasi.Gecersiz(Ad, "transformation input is null");
        }

        if (mevcut.DonusumKipi == DonusumKipi.ElleTers)
        {
            var e = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz);
            return Result<ProjeBelgesi>.Failure(e with
            {
                TechnicalMessage = e.TechnicalMessage.Replace("{command}", Ad, StringComparison.Ordinal)
                    .Replace("{reason}", "manual (inverse) common-point transformation is stored; remove it before applying a forward (.DNS) transformation", StringComparison.Ordinal),
                UserMessage = "Projede elle girilmiş ortak noktalı (ters) dönüşüm kayıtlı: proje noktaları Deprem Sonrası kabul edilmiş. "
                    + ".DNS ileri dönüşümünü uygulamak için önce \"Dönüşümü Kaldır\" kullanın.",
            });
        }

        var k = DonusumKatsayilari.Dogrula(Katsayilar);
        if (k.IsFailure)
        {
            return Result<ProjeBelgesi>.Failure(k.Error);
        }

        var girdi = DonusumGirdisiAyarlaKomutu.Dogrula(Ad, Girdi);
        if (girdi.IsFailure)
        {
            return Result<ProjeBelgesi>.Failure(girdi.Error);
        }

        var taban = (mevcut.IleriDonusum is null ? mevcut.Parseller : mevcut.DepremOncesiParseller ?? mevcut.Parseller).ToArray();
        var aktif = IleriDonusumHesabi.Donustur(Ad, taban, k.Value);
        if (aktif.IsFailure)
        {
            return Result<ProjeBelgesi>.Failure(aktif.Error);
        }

        return Result<ProjeBelgesi>.Success(mevcut with
        {
            Parseller = aktif.Value,
            DepremOncesiParseller = taban,
            IleriDonusum = k.Value,
            Donusum = girdi.Value,
        });
    }
}

/// <summary>SAHA-014: parsel listesinin ileri dönüşümü ve güvenlik denetimleri (architect Y1).</summary>
internal static class IleriDonusumHesabi
{
    /// <summary>
    /// Her köşeyi <see cref="DonusumKatsayilari.Uygula"/> ile dönüştürür; sonuç halkası <see cref="PolygonRing.FromOpen"/>'dan geçmeli
    /// ve iki farklı Deprem Öncesi köşe aynı Deprem Sonrası noktaya düşmemelidir (aksi <c>ERR-CRS-004</c>; nokta no eşlemesi bozulurdu).
    /// </summary>
    public static Result<IReadOnlyList<ParselGeometrisi>> Donustur(string ad, IReadOnlyList<ParselGeometrisi> orj, DonusumKatsayilari k)
    {
        var eslem = new Dictionary<GeodeticPoint, GeodeticPoint>();
        var sonuc = new ParselGeometrisi[orj.Count];
        for (var i = 0; i < orj.Count; i++)
        {
            var koseler = new GeodeticPoint[orj[i].Koseler.Count];
            for (var j = 0; j < koseler.Length; j++)
            {
                var o = orj[i].Koseler[j];
                var a = k.Uygula(o);
                if (eslem.TryGetValue(a, out var onceki) && onceki != o)
                {
                    var e = ErrorCatalog.Get(ErrorCodes.CrsDonusumHesaplanamadi);
                    return Result<IReadOnlyList<ParselGeometrisi>>.Failure(e with
                    {
                        TechnicalMessage = e.TechnicalMessage.Replace("{reason}", ad + ": two distinct original vertices map to the same transformed point (item #" + KomutHatasi.Sayi(i + 1) + ")", StringComparison.Ordinal),
                    });
                }

                eslem[a] = o;
                koseler[j] = a;
            }

            var halka = PolygonRing.FromOpen(koseler);
            if (halka.IsFailure)
            {
                return Result<IReadOnlyList<ParselGeometrisi>>.Failure(halka.Error with { TechnicalMessage = halka.Error.TechnicalMessage + " (" + ad + ", item #" + KomutHatasi.Sayi(i + 1) + ")" });
            }

            sonuc[i] = orj[i] with { Koseler = koseler };
        }

        return Result<IReadOnlyList<ParselGeometrisi>>.Success(sonuc);
    }
}

/// <summary>P7.3-C2: proje künyesini ayarlar (<c>null</c> temizler); her alan kırpılır ve en çok 120 karakterdir.</summary>
public sealed record ProjeKunyesiAyarlaKomutu(ProjeKunyesi? Kunye) : IProjeKomutu
{
    public string Ad => "ProjeKunyesiAyarla";

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        ArgumentNullException.ThrowIfNull(mevcut);
        if (Kunye is null)
        {
            return Result<ProjeBelgesi>.Success(mevcut with { Kunye = null });
        }

        if (Kunye.Alanlar().Any(a => a.Deger is null || a.Deger.Trim().Length > ProjeKunyesi.MaxUzunluk))
        {
            return KomutHatasi.Gecersiz(Ad, "identity fields must be non-null and at most 120 characters");
        }

        var k = new ProjeKunyesi(Kunye.Il.Trim(), Kunye.Ilce.Trim(), Kunye.KoyMahalle.Trim(), Kunye.Belediye.Trim(),
            Kunye.PaftaNolari.Trim(), Kunye.KadastroId.Trim(), Kunye.DatumProjeksiyon.Trim());
        return Result<ProjeBelgesi>.Success(mevcut with { Kunye = k });
    }
}

/// <summary>
/// P7.3-C3 (SAHA-007): 1. adımda yüklenen kaynak <c>.NCZ</c>'nin şablon baytlarını saklar (<c>null</c> = temizle; <c>.DXF</c>/<c>.DWG</c>
/// içe aktarımında şablonsuz iskelet moduna dönülür). Şablon <c>PARSELASYON HARİTASI.NCZ</c> içeriğini belirlediği için
/// RevisionId'ye girer: değişirse onay düşer (AC-PILOT-06). Denetime yalnız bayt sayısı ve SHA-256'nın ilk 12 hanesi yazılır;
/// dosyadaki kullanıcı adı/XML değerleri yazılmaz (KVKK).
/// </summary>
public sealed record NczSablonuAyarlaKomutu(byte[]? Icerik) : IProjeKomutu
{
    public string Ad => "NczSablonuAyarla";

    public string? Ayrinti => Icerik is null
        ? "temizlendi"
        : Cad.NczSablonu.Olustur(Icerik) is { IsSuccess: true } s
            ? "bayt=" + KomutHatasi.Sayi(s.Value.Uzunluk) + " sha256=" + s.Value.Sha256[..12]
            : "bayt=" + KomutHatasi.Sayi(Icerik.Length);

    public Result<ProjeBelgesi> Uygula(ProjeBelgesi mevcut)
    {
        ArgumentNullException.ThrowIfNull(mevcut);
        if (Icerik is null)
        {
            return Result<ProjeBelgesi>.Success(mevcut with { NczSablonu = null });
        }

        var sablon = Cad.NczSablonu.Olustur(Icerik);
        return sablon.IsFailure
            ? Result<ProjeBelgesi>.Failure(sablon.Error)
            : Result<ProjeBelgesi>.Success(mevcut with { NczSablonu = sablon.Value });
    }
}
