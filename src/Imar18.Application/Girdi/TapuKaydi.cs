using System.Numerics;
using Imar18.Domain.Diagnostics;

namespace Imar18.Application.Girdi;

/// <summary>
/// P7.3-B1 (SAHA-006): webtapu "Tapu Kaydı" belgesinden okunan mülkiyet satırı. <see cref="AdSoyad"/>, <see cref="BabaAdi"/> ve
/// <see cref="KisiSistemNo"/> kişisel veridir: yalnız yerel Malik Rehberi'ne yazılır; hesap motoruna, denetim kaydına ve hata
/// mesajlarına girmez (KVKK, AC-PILOT-08).
/// </summary>
public sealed record TapuMalikSatiri(
    string HisseSistemNo,
    string? KisiSistemNo,
    string AdSoyad,
    string? BabaAdi,
    string? ElBirligiNo,
    BigInteger Pay,
    BigInteger Payda,
    decimal MetrekareM2,
    decimal ToplamM2,
    string EdinmeSebebi,
    bool Terkinli);

/// <summary>webtapu tapu kaydı: taşınmaz bilgileri ve mülkiyet satırları (belge sırasıyla).</summary>
public sealed record TapuKaydi(
    string? TasinmazNo,
    string? Il,
    string? Ilce,
    string? Mahalle,
    string? Mevkii,
    string? Cilt,
    string? Sayfa,
    string Ada,
    string Parsel,
    decimal AtYuzolcumM2,
    string? Nitelik,
    IReadOnlyList<TapuMalikSatiri> Malikler)
{
    /// <summary>Terkin edilmemiş (aktif) malikler.</summary>
    public IReadOnlyList<TapuMalikSatiri> AktifMalikler => Malikler.Where(m => !m.Terkinli).ToList();
}

/// <summary>
/// webtapu PDF okuyucusu sözleşmesi (uygulaması Infrastructure'da; PdfPig Application'a sızmaz). Okunamayan/tanınmayan belge
/// <c>ERR-GIRD-003</c>; aktif hisse toplamı 1 değilse <c>ERR-TAPU-003</c>; hisse metrekaresi AT yüzölçüm × hisse ile ±0.01 m²
/// uyuşmazsa <c>ERR-TAPU-004</c>.
/// </summary>
public interface ITapuKaydiOkuyucu
{
    Result<TapuKaydi> Oku(Stream kaynak, CancellationToken cancellationToken = default);
}
