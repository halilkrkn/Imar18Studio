using Imar18.Application.Cikti;
using Imar18.Application.Onay;

namespace Imar18.Application.Proje;

/// <summary>
/// Sunum katmanına verilen proje çalışma alanı (P5): CommandBus, onay ve çıktı servisleri ile oturumdaki mühendis kimliği.
/// Kompozisyon kökünde (Bootstrapper) kurulur; Presentation Infrastructure'ı bilmez (NetArchTest).
/// </summary>
public sealed record ProjeCalismaAlani(ProjeKomutYolu Yol, ApprovalService Onay, CiktiServisi Cikti, string KullaniciId);
