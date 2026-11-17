using Imar18.Domain.Diagnostics;

namespace Imar18.Application.Cad;

/// <summary>
/// Netcad <c>.CKS</c> DOP ozet dosyasinin degerleri (spec §25.1). Alanlar Netcad'in kendi satir adlarini birebir
/// tasir; $R_1, D_1, R_2, D_2, R_3$ girdilerine nasil eslenecegi muhendis kararidir (STATUS SORU-033) ve bu
/// sozlesmede yapilmaz. Tum degerler dosyadaki ondalik yaziyla kayipsiz <c>decimal</c>'dir.
/// </summary>
public sealed record CksDopOzeti(
    decimal ToplamTahsisM2,
    decimal ToplamImarAdaAlaniM2,
    decimal ToplamOrtakKatilimAlaniM2,
    decimal ToplamDuzenlemeyeGirenDopAlinanM2,
    decimal ToplamDuzenlemeyeGirenDopAlinmayanM2,
    decimal ToplamKamulastirilanAlanM2,
    decimal ToplamBagislananAlanM2,
    decimal ToplamKadastroParselAlaniM2,
    decimal ToplamPayM2,
    decimal OrtakKatilimOrani,
    decimal Dopo,
    decimal DopoOkoIleBirlikte,
    decimal MaksimumDopOrani,
    decimal KamuyaAyrilanAlanM2);

/// <summary>
/// <c>.CKS</c> okuma sozlesmesi. Bozuk, eksik veya ic aritmetigi tutarsiz dosyada istisna atmaz,
/// <c>ERR-GIRD-007</c> dondurur; hata mesajina dosya degeri yazmaz. Yazma (disa aktarim) P5 cikti sozlesmesine
/// birakilmistir (pilot damga kurali — STATUS SORU-033).
/// </summary>
public interface ICksFileReader
{
    Result<CksDopOzeti> Read(Stream source, CancellationToken cancellationToken = default);
}
