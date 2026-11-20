using Imar18.Application.Cikti;
using Imar18.Application.Onay;

namespace Imar18.Application.Proje;

/// <summary>
/// Sunum katmanına verilen proje çalışma alanı (P5): CommandBus, onay ve çıktı servisleri ile oturumdaki mühendis kimliği.
/// Kompozisyon kökünde (Bootstrapper) kurulur; Presentation Infrastructure'ı bilmez (NetArchTest).
/// </summary>
public sealed record ProjeCalismaAlani(ProjeKomutYolu Yol, ApprovalService Onay, CiktiServisi Cikti, string KullaniciId)
{
    /// <summary>P7.3-B1: webtapu PDF okuyucusu (Infrastructure; yoksa webtapu yükleme pasiftir).</summary>
    public Girdi.ITapuKaydiOkuyucu? TapuOkuyucu { get; init; }

    /// <summary>P7.3-B2: Yaklaşık Yer Krokisi için poligon kesişim alanı hesaplayıcısı (GeometryEngine; yoksa kroki yükleme pasiftir).</summary>
    public Domain.Geometry.IParselKesisimHesaplayici? Kesisim { get; init; }

    /// <summary>P7.3-C2: ortak noktalı dönüşüm servisi (GeometryEngine portu; yoksa 1. adımdaki dönüşüm önizlemesi pasiftir).</summary>
    public KoordinatDonusumu.DonusumServisi? Donusum { get; init; }

    /// <summary>SAHA-014: Netcad <c>.DNS</c> okuyucusu (Infrastructure; yoksa 1. adımdaki "Dönüşüm Dosyası (.DNS) Yükle" pasiftir).</summary>
    public KoordinatDonusumu.IDnsDosyaOkuyucu? DnsOkuyucu { get; init; }

    /// <summary>SAHA-015: kroki blok bölücü (GeometryEngine; yoksa Yaklaşık Yer Krokisi yalnız Kip 1/2 Kroki satırı üretir, parselasyon taslağı yapılmaz).</summary>
    public Domain.Geometry.IKrokiBlokBolucu? KrokiBolucu { get; init; }
}
