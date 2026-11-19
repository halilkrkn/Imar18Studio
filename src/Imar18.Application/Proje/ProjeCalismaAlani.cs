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
}
