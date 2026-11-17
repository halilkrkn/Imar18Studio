namespace Imar18.Domain.Diagnostics;

/// <summary>Katalogdaki hata kodu sabitleri (spec 5.2 önekleri). Mevzuat değeri içermez.</summary>
public static class ErrorCodes
{
    public const string GenelBeklenmeyen = "ERR-GENEL-001";

    public const string GirdCadOkunamadi = "ERR-GIRD-001";
    public const string GirdCadSurumUyumsuz = "ERR-GIRD-002";
    public const string GirdPdfBozuk = "ERR-GIRD-003";
    public const string GirdNczOkunamadi = "ERR-GIRD-NCZ-001";
    public const string GirdZKoduIndirgendi = "ERR-GIRD-004";
    public const string GirdKoordinatKesinlikZarfiDisinda = "ERR-GIRD-005";
    public const string GirdIzgaraDisiKose = "ERR-GIRD-006";
    public const string GirdCksOkunamadi = "ERR-GIRD-007";
    public const string GirdDesteklenmeyenGeometriAtlandi = "ERR-GIRD-008";
    public const string GirdToleranslaKapatildi = "ERR-GIRD-009";
    public const string GirdEtiketPoligonEslesmedi = "ERR-GIRD-010";
    public const string GirdProjeGirdisiGecersiz = "ERR-GIRD-011";

    public const string CrsEksik = "ERR-CRS-001";
    public const string CrsEpokUyumsuz = "ERR-CRS-002";
    public const string CrsGirdisiGecersiz = "ERR-CRS-003";
    public const string CrsDonusumHesaplanamadi = "ERR-CRS-004";
    public const string CrsDonusumToleransAsimi = "ERR-CRS-005";

    public const string TopoKendiUzerindeKesisim = "ERR-TOPO-001";
    public const string TopoAcikPoligon = "ERR-TOPO-002";
    public const string TopoDejenerePoligon = "ERR-TOPO-003";
    public const string TopoKonkavKesimCokParcali = "ERR-TOPO-004";

    public const string TapuMalikEslesmedi = "ERR-TAPU-001";
    public const string TapuHisseEksik = "ERR-TAPU-002";
    public const string TapuHisseToplamiBirDegil = "ERR-TAPU-003";
    public const string TapuDagitimGirdisiGecersiz = "ERR-TAPU-004";
    public const string TapuImarParselDagitimToplamiUyumsuz = "ERR-TAPU-005";
    public const string TapuMalikTahsisiNetHaktanFarkli = "ERR-TAPU-006";
    public const string TapuMuvafakatsizMustakilTahsis = "ERR-TAPU-007";
    public const string TapuParselKimlikEslemesiGecersiz = "ERR-TAPU-008";

    public const string TecvizSiniriAsildi = "ERR-TECVIZ-001";
    public const string TecvizParselToplamSonucuFarkli = "ERR-TECVIZ-002";
    public const string TecvizGirdisiEksik = "ERR-TECVIZ-003";
    public const string TecvizGirdisiGecersiz = "ERR-TECVIZ-004";

    public const string DopYuzde45Asildi = "ERR-DOP-001";
    public const string DopMahsupEksik = "ERR-DOP-002";
    public const string DopGirdisiGecersiz = "ERR-DOP-003";
    public const string DopR2DengelemeFarki = "ERR-DOP-004";
    public const string DopAsimKarsilanamadi = "ERR-DOP-005";

    public const string PlanCepheDerinlik = "ERR-PLAN-001";
    public const string PlanAsgariAlan = "ERR-PLAN-002";
    public const string PlanBolmeGirdisiGecersiz = "ERR-PLAN-003";
    public const string PlanHedefAlanaYakinsanamadi = "ERR-PLAN-004";
    public const string PlanHedefAciAraligindaErisilemez = "ERR-PLAN-005";

    public const string KuantAlanSapmasi = "ERR-KUANT-001";
    public const string KuantKoseCakismasi = "ERR-KUANT-002";

    public const string CiktiParsidZorunluAlanEksik = "ERR-CIKTI-001";
    public const string CiktiFenKlasoruSablon = "ERR-CIKTI-002";
    public const string CiktiCadDisaAktarim = "ERR-CIKTI-003";
    public const string CiktiOnayKritikHataEngeli = "ERR-CIKTI-004";
    public const string CiktiOnayMetniKabulEdilmedi = "ERR-CIKTI-005";
    public const string CiktiOnayIcinHesapEksik = "ERR-CIKTI-006";
    public const string CiktiGecerliOnayYok = "ERR-CIKTI-007";
    public const string CiktiOnayGeriAlinamaz = "ERR-CIKTI-008";
    public const string CiktiProjeKaydiHatasi = "ERR-CIKTI-009";

    public const string GuvenAuditZinciriBozuk = "ERR-GUVEN-001";
    public const string GuvenYetkisizErisim = "ERR-GUVEN-002";
}

/// <summary>
/// Merkezi, sürümlü hata kataloğu (spec 5, AC-ERR-01..03). Hata mesajları UI'de string olarak tutulmaz.
/// Bu sürüm Pilot çekirdek kodlarını içerir; kodlar eklendikçe <see cref="Version"/> artırılır.
/// Hiçbir kayıt mevzuat sayısal değeri uydurmaz (yalnızca spec'te geçen %45 DOPO üst sınırı anılır).
/// </summary>
public static class ErrorCatalog
{
    /// <summary>Katalog sürümü (AC-ERR-01).</summary>
    public const string Version = "1.6.0";

    private static readonly AppError[] Entries =
    [
        // ---- Genel (spec 5.2'de önek yok; bilinmeyen hata için varsayım — STATUS SORU-018) ----
        New(ErrorCodes.GenelBeklenmeyen, ErrorSeverity.Error, ErrorCategory.General,
            "Beklenmeyen bir hata oluştu.",
            "Unmapped or unknown error code: {code}.",
            "İşlemi yeniden deneyin; sorun sürerse kaydı destek için saklayın.",
            retryable: true, review: false),

        // ---- ERR-GIRD: Girdi / dosya ----
        New(ErrorCodes.GirdCadOkunamadi, ErrorSeverity.Error, ErrorCategory.Input,
            "CAD dosyası okunamadı.",
            "CAD file {path} could not be parsed by the adapter.",
            "Dosyanın bozuk olmadığını kontrol edin ya da DXF/DWG/NCZ biçiminde yeniden dışa aktarın.",
            retryable: true, review: false),
        New(ErrorCodes.GirdCadSurumUyumsuz, ErrorSeverity.Error, ErrorCategory.Input,
            "CAD dosyasının sürümü desteklenmiyor.",
            "Unsupported CAD file version {version} for file {path}.",
            "Dosyayı desteklenen bir sürüme veya DXF biçimine dönüştürüp yeniden yükleyin.",
            retryable: false, review: false),
        New(ErrorCodes.GirdPdfBozuk, ErrorSeverity.Error, ErrorCategory.Input,
            "PDF belgesi okunamadı.",
            "PDF document {path} is corrupt or unreadable.",
            "PDF dosyasını yeniden alın veya verileri elle girin.",
            retryable: true, review: false),
        New(ErrorCodes.GirdNczOkunamadi, ErrorSeverity.Error, ErrorCategory.Input,
            "Netcad NCZ dosyası okunamadı veya sürümü uyumsuz.",
            "NCZ block parse failure or unsupported Netcad version in {path}.",
            "Dosyayı Netcad'den DXF/DWG olarak dışa aktarıp yedek girdi olarak yükleyin.",
            retryable: false, review: false),

        New(ErrorCodes.GirdZKoduIndirgendi, ErrorSeverity.Warning, ErrorCategory.Input,
            "Üç boyutlu (Z ≠ 0) köşeler yatay düzleme indirgendi.",
            "Non-zero Z values in {path} were projected to the Z=0 horizontal plane.",
            "Alan hesabının yatay izdüşüm üzerinden yapıldığını kontrol edin.",
            retryable: false, review: false),

        New(ErrorCodes.GirdKoordinatKesinlikZarfiDisinda, ErrorSeverity.Error, ErrorCategory.Input,
            "Koordinatlar desteklenen hassasiyet veya boyut sınırlarının dışında.",
            "Coordinate precision envelope violated: {reason}.",
            "Koordinatları en fazla 6 ondalık basamakla ve aynı proje bölgesinde girin.",
            retryable: false, review: false),

        // P4.1 (katalog 1.4.0): SORU-025 ızgara dışı köşe uyarısı, CKS okuma ve desteklenmeyen CAD geometrisi.
        New(ErrorCodes.GirdIzgaraDisiKose, ErrorSeverity.Warning, ErrorCategory.Input,
            "Bazı köşeler 1 mm koordinat ızgarasında değil.",
            "{count} vertices are off the 1 mm grid in {path}; coordinates were kept unquantized.",
            "Köşeler kuantize edilmeden korundu; ızgaraya çekmek için mühendis onayı gerekir (resmî alan değişebilir).",
            retryable: false, review: true),
        New(ErrorCodes.GirdCksOkunamadi, ErrorSeverity.Error, ErrorCategory.Input,
            "Netcad CKS DOP özet dosyası okunamadı veya tutarsız.",
            "CKS DOP summary {path} could not be parsed: {reason}.",
            "Dosyayı Netcad'den yeniden dışa aktarın veya DOP değerlerini elle girin.",
            retryable: true, review: false),
        New(ErrorCodes.GirdDesteklenmeyenGeometriAtlandi, ErrorSeverity.Warning, ErrorCategory.Input,
            "Desteklenmeyen geometrili çizgiler (yay segmenti veya eğik düzlem) atlandı.",
            "{count} polylines with unsupported geometry ({reason}) were skipped in {path}.",
            "Yayları köşe noktalarına dönüştürüp (explode/segment) dosyayı yeniden dışa aktarın; atlanan alanları kontrol edin.",
            retryable: false, review: true),
        New(ErrorCodes.GirdToleranslaKapatildi, ErrorSeverity.Warning, ErrorCategory.Input,
            "Bazı çizgiler 1 mm kapanma toleransıyla kapatıldı (son köşe ilk köşeye eşitlendi).",
            "{count} polylines were closed within the {tolerance} m closure tolerance in {path}.",
            "Kapatılan çizgilerin ilk/son köşesini kontrol edin; gerekirse CAD'de kapatıp yeniden dışa aktarın.",
            retryable: false, review: true),

        // P4.2 (katalog 1.5.0): etiket–poligon eşleştirmesi.
        New(ErrorCodes.GirdEtiketPoligonEslesmedi, ErrorSeverity.Warning, ErrorCategory.Input,
            "Bazı parsel poligonlarına etiket eşleştirilemedi.",
            "{unlabeled} unlabeled and {ambiguous} ambiguous polygons after label matching in {path}.",
            "Etiketsiz veya birden çok yazı içeren poligonların parsel kodunu elle atayın.",
            retryable: false, review: true),

        // P5.1 (katalog 1.6.0): CommandBus komut girdisi.
        New(ErrorCodes.GirdProjeGirdisiGecersiz, ErrorSeverity.Error, ErrorCategory.Input,
            "Proje girdisi geçersiz; değişiklik uygulanmadı.",
            "Project command {command} rejected: {reason}.",
            "Girdiyi düzeltip işlemi yeniden deneyin.",
            retryable: true, review: false),

        // ---- ERR-CRS: Koordinat sistemi ----
        New(ErrorCodes.CrsEksik, ErrorSeverity.Critical, ErrorCategory.Crs,
            "Koordinat sistemi (CRS/dilim) tanımlı değil.",
            "Project CRS is missing for {projectId}.",
            "Proje ayarlarından CRS ve dilimi girin.",
            retryable: false, review: true),
        New(ErrorCodes.CrsEpokUyumsuz, ErrorSeverity.Warning, ErrorCategory.Crs,
            "Koordinat epoku uyumsuz görünüyor.",
            "Epoch mismatch between {sourceEpoch} and {targetEpoch}.",
            "Kaynak ve hedef epokları kontrol edin.",
            retryable: false, review: true),

        New(ErrorCodes.CrsGirdisiGecersiz, ErrorSeverity.Error, ErrorCategory.Crs,
            "Koordinat sistemi veya pafta ölçeği girdisi geçersiz.",
            "Invalid CRS/scale input: {reason}.",
            "Proje ayarlarından CRS, dilim ve pafta ölçeğini kontrol edin.",
            retryable: false, review: false),

        // P4.2 (katalog 1.5.0): 2B Helmert/Afin dönüşüm raporu.
        New(ErrorCodes.CrsDonusumHesaplanamadi, ErrorSeverity.Error, ErrorCategory.Crs,
            "Koordinat dönüşümü hesaplanamadı.",
            "Transformation could not be estimated: {reason}.",
            "Yeterli sayıda, iyi dağılmış ve benzersiz kimlikli ortak nokta girin.",
            retryable: false, review: false),
        New(ErrorCodes.CrsDonusumToleransAsimi, ErrorSeverity.Warning, ErrorCategory.Crs,
            "Bazı ortak noktaların dönüşüm artığı toleransı aşıyor.",
            "{count} common points exceed the {tolerance} m residual tolerance.",
            "Toleransı aşan ortak noktaları kontrol edin; hatalı noktayı çıkarıp dönüşümü yeniden hesaplayın.",
            retryable: false, review: true),

        // ---- ERR-TOPO: Topoloji (spec 5.3 örneği birebir) ----
        New(ErrorCodes.TopoKendiUzerindeKesisim, ErrorSeverity.Critical, ErrorCategory.Topology,
            "Parsel sınırı kendi üzerinde kesişiyor.",
            "Self-intersection detected in polygon {parcelId}.",
            "Topoloji Onar ekranını kullanın veya CAD (DXF/DWG/NCZ) köşe sırasını kontrol edin.",
            retryable: false, review: true),
        New(ErrorCodes.TopoAcikPoligon, ErrorSeverity.Critical, ErrorCategory.Topology,
            "Parsel sınırı kapalı değil.",
            "Polygon {parcelId} ring is not closed.",
            "Parsel sınırının ilk ve son köşesini birleştirin.",
            retryable: false, review: true),

        New(ErrorCodes.TopoDejenerePoligon, ErrorSeverity.Critical, ErrorCategory.Topology,
            "Parsel sınırı geçerli bir alan oluşturmuyor.",
            "Degenerate polygon: {reason}.",
            "Köşe noktalarını kontrol edin; tekrarlanan veya aynı doğru üzerindeki köşeleri düzeltin.",
            retryable: false, review: true),
        New(ErrorCodes.TopoKonkavKesimCokParcali, ErrorSeverity.Critical, ErrorCategory.Topology,
            "Kesim, adayı birden fazla parçaya ayırıyor; otomatik bölme durduruldu.",
            "Cut produced a multi-part piece at angle {angle}; subdivision stopped for engineer review.",
            "Kesim yönünü veya cephe kenarını değiştirin ya da bölmeyi mühendis olarak elle yapın.",
            retryable: false, review: true),

        // ---- ERR-TAPU: Tapu / kroki / hisse ----
        New(ErrorCodes.TapuMalikEslesmedi, ErrorSeverity.Critical, ErrorCategory.Tapu,
            "Malik eşleşmesi yapılamadı.",
            "Owner {ownerId} could not be matched to a cadastral record.",
            "Tapu verisini ve malik eşleştirmesini kontrol edin.",
            retryable: false, review: true),
        New(ErrorCodes.TapuHisseEksik, ErrorSeverity.Critical, ErrorCategory.Tapu,
            "Hisse bilgisi eksik.",
            "Share information is missing for {ownerId}.",
            "Tapu verisini kontrol edin.",
            retryable: false, review: true),
        New(ErrorCodes.TapuHisseToplamiBirDegil, ErrorSeverity.Critical, ErrorCategory.Tapu,
            "Hisselerin toplamı bütüne (1) eşit değil.",
            "Allocation weights sum to {sum}, expected exactly 1.",
            "Hisse paylarını kontrol edip toplamın tam olarak bire eşit olmasını sağlayın.",
            retryable: false, review: true),
        New(ErrorCodes.TapuDagitimGirdisiGecersiz, ErrorSeverity.Error, ErrorCategory.Tapu,
            "Hisse dağıtım girdisi geçersiz.",
            "Invalid allocation input: {reason}.",
            "Malik kimliklerinin benzersiz ve boş olmadığını, hisselerin negatif olmadığını kontrol edin.",
            retryable: false, review: false),
        New(ErrorCodes.TapuImarParselDagitimToplamiUyumsuz, ErrorSeverity.Critical, ErrorCategory.Tapu,
            "İmar parseline eşleştirilen malik alanlarının toplamı parsel alanına eşit değil.",
            "Assigned areas for parcel {parcelId} differ from parcel area by {deltaDm2} dm2 (allowed {allowedDm2}).",
            "Malik -> imar parseli eşleştirme tablosundaki alanları kontrol edin.",
            retryable: false, review: true),
        New(ErrorCodes.TapuMalikTahsisiNetHaktanFarkli, ErrorSeverity.Warning, ErrorCategory.Tapu,
            "Bir malikin toplam tahsisi, DOP sonrası net hakkından farklı.",
            "Owner #{ownerIndex} allocation differs from net entitlement by {deltaDm2} dm2.",
            "Fazla/eksik tahsisi mühendis olarak inceleyin ve eşleştirmeyi gözden geçirin.",
            retryable: false, review: true),
        New(ErrorCodes.TapuMuvafakatsizMustakilTahsis, ErrorSeverity.Critical, ErrorCategory.Tapu,
            "Muvafakati olmayan müşterek paydaş müstakil imar parseline tahsis edilmiş.",
            "Owner #{ownerIndex} from a shared cadastral parcel was assigned alone to parcel {parcelId} without consent.",
            "Paydaşın muvafakatini kaydedin veya paydaşı hisseli bir imar parseline tahsis edin.",
            retryable: false, review: true),
        New(ErrorCodes.TapuParselKimlikEslemesiGecersiz, ErrorSeverity.Error, ErrorCategory.Tapu,
            "Geçici ve kalıcı parsel kimlik eşlemesi geçersiz.",
            "Invalid temporary/registry parcel id mapping: {reason}.",
            "Her geçici kodun tek bir tescil numarasına ve her tescil numarasının tek bir geçici koda eşlendiğini kontrol edin.",
            retryable: false, review: false),

        // ---- ERR-TECVIZ: Tecviz (seviyeler için STATUS SORU-019) ----
        New(ErrorCodes.TecvizSiniriAsildi, ErrorSeverity.Critical, ErrorCategory.Tecviz,
            "Tapu alanı ile hesaplanan alan farkı yanılma sınırını aşıyor.",
            "|Gauss - Tapu| = {delta} exceeds engineer-entered limit {limit} for {parcelId}.",
            "Koordinatları ve tapu alanını kontrol edin; tecviz girdisini mühendis olarak doğrulayın.",
            retryable: false, review: true),
        New(ErrorCodes.TecvizParselToplamSonucuFarkli, ErrorSeverity.Warning, ErrorCategory.Tecviz,
            "Parsel bazlı ve toplam bazlı tecviz kontrolleri farklı sonuç veriyor.",
            "Per-parcel and total tecviz checks disagree for regulation {regulationId}.",
            "Hangi kontrolün bağlayıcı olduğunu mühendis olarak teyit edin.",
            retryable: false, review: true),
        New(ErrorCodes.TecvizGirdisiEksik, ErrorSeverity.Error, ErrorCategory.Tecviz,
            "Tecviz sınırı girilmemiş.",
            "TecvizGirdisi missing for parcel {parcelId}.",
            "Tecviz sınırını mühendis girdisi olarak giriniz.",
            retryable: false, review: true),
        New(ErrorCodes.TecvizGirdisiGecersiz, ErrorSeverity.Error, ErrorCategory.Tecviz,
            "Tecviz girdisi geçersiz.",
            "Invalid tecviz input: {reason}.",
            "Tecviz sınırının, ölçek paydasının ve alanların pozitif, kaynak notunun dolu olduğunu kontrol edin.",
            retryable: false, review: false),

        // ---- ERR-DOP: DOP / mahsup ----
        New(ErrorCodes.DopYuzde45Asildi, ErrorSeverity.Critical, ErrorCategory.Dop,
            "Düzenleme Ortaklık Payı oranı %45 yasal üst sınırını aşıyor.",
            "DOPO {dopo} exceeds 0.45 upper bound.",
            "Aşan kısım için mevzuattaki karşılama sırasını uygulayın ve mühendis incelemesi isteyin.",
            retryable: false, review: true),
        New(ErrorCodes.DopMahsupEksik, ErrorSeverity.Error, ErrorCategory.Dop,
            "DOP mahsup bilgisi eksik.",
            "Offset (mahsup) data missing for parcel {parcelId}.",
            "Mahsup girdilerini tamamlayın.",
            retryable: false, review: true),
        New(ErrorCodes.DopGirdisiGecersiz, ErrorSeverity.Error, ErrorCategory.Dop,
            "DOP hesap girdisi geçersiz.",
            "Invalid DOP input: {reason}.",
            "R1, D1, R2, D2, R3 alanlarının negatif olmadığını ve R1 − D1 değerinin pozitif olduğunu kontrol edin.",
            retryable: false, review: false),
        New(ErrorCodes.DopR2DengelemeFarki, ErrorSeverity.Warning, ErrorCategory.Dop,
            "DOP sayfasındaki imar alanı ile dengeleme cetveli parsel toplamı farklı.",
            "R2 (DOP sheet) differs from balancing sheet parcel total by {delta} m2; difference is not distributed.",
            "Farkı mühendis olarak inceleyin; DOP sayfası referans alınmıştır.",
            retryable: false, review: true),
        New(ErrorCodes.DopAsimKarsilanamadi, ErrorSeverity.Critical, ErrorCategory.Dop,
            "%45 sınırını aşan DOP miktarı tanımlı kaynaklarla karşılanamıyor.",
            "DOP excess {excess} m2 exceeds available sources by {shortfall} m2.",
            "Karşılama kaynaklarını (tescil harici, belediye, muvafakatli kamu, kamulaştırma) gözden geçirin.",
            retryable: false, review: true),

        // ---- ERR-PLAN: Plan kuralı ----
        New(ErrorCodes.PlanCepheDerinlik, ErrorSeverity.Error, ErrorCategory.Plan,
            "Parsel cephe veya derinlik koşulunu sağlamıyor.",
            "Frontage/depth rule violated for parcel {parcelId}.",
            "Parsel kesim çizgisini veya plan koşullarını gözden geçirin.",
            retryable: false, review: true),
        New(ErrorCodes.PlanAsgariAlan, ErrorSeverity.Error, ErrorCategory.Plan,
            "Parsel asgari alan koşulunu sağlamıyor.",
            "Minimum area rule violated for parcel {parcelId}.",
            "Parsel alanını veya plan koşullarını gözden geçirin.",
            retryable: false, review: true),

        New(ErrorCodes.PlanBolmeGirdisiGecersiz, ErrorSeverity.Error, ErrorCategory.Plan,
            "Parsel bölme girdisi geçersiz.",
            "Invalid subdivision input: {reason}.",
            "Hedef alanı, cephe kenarını, açı aralığını ve toleransları kontrol edin.",
            retryable: false, review: false),
        New(ErrorCodes.PlanHedefAlanaYakinsanamadi, ErrorSeverity.Error, ErrorCategory.Plan,
            "Hedef parsel alanına izin verilen deneme sayısı içinde ulaşılamadı.",
            "Bisection did not converge within {iterations} iterations.",
            "Yakınsama toleransını veya deneme sınırını gözden geçirin ya da mühendis müdahalesi isteyin.",
            retryable: true, review: true),
        New(ErrorCodes.PlanHedefAciAraligindaErisilemez, ErrorSeverity.Error, ErrorCategory.Plan,
            "Hedef parsel alanı, izin verilen açı aralığında bu cepheden oluşturulamıyor.",
            "Target area unreachable from frontage edge within angle range.",
            "Hedef alanı, cephe kenarını veya açı aralığını değiştirin.",
            retryable: false, review: true),

        // ---- ERR-KUANT: Kuantizasyon ----
        New(ErrorCodes.KuantAlanSapmasi, ErrorSeverity.Critical, ErrorCategory.Quantization,
            "Koordinat yuvarlama sonrası alan sapması izin verilen sınırı aşıyor; çözüm geri alındı.",
            "Post-quantization area deviation {delta} exceeds tolerance {tolerance}; solution rolled back.",
            "Parsel kesimini yeniden çalıştırın veya mühendis müdahalesi isteyin.",
            retryable: true, review: true),
        New(ErrorCodes.KuantKoseCakismasi, ErrorSeverity.Critical, ErrorCategory.Quantization,
            "Koordinat yuvarlama sonrası köşeler çakıştı veya topoloji bozuldu; çözüm geri alındı.",
            "Vertex collision or invalid topology after quantization in {parcelId}; solution rolled back.",
            "Parsel kesimini yeniden çalıştırın veya mühendis müdahalesi isteyin.",
            retryable: true, review: true),

        // ---- ERR-CIKTI: Çıktı ----
        New(ErrorCodes.CiktiParsidZorunluAlanEksik, ErrorSeverity.Critical, ErrorCategory.Output,
            "Zorunlu alan eksik.",
            "Required export field {field} is missing; export blocked.",
            "Eksik alanı kontrol edip tamamlayın.",
            retryable: false, review: true),
        New(ErrorCodes.CiktiFenKlasoruSablon, ErrorSeverity.Error, ErrorCategory.Output,
            "Fen Klasörü şablonu oluşturulamadı.",
            "Fen Klasörü template {templateId} failed to render.",
            "Şablon sürümünü ve girdi verisini kontrol edin.",
            retryable: true, review: false),
        New(ErrorCodes.CiktiCadDisaAktarim, ErrorSeverity.Error, ErrorCategory.Output,
            "CAD dosyası dışa aktarılamadı.",
            "CAD export to {format} failed.",
            "Hedef biçimi ve çıktı yolunu kontrol edip yeniden deneyin.",
            retryable: true, review: false),

        // P5 (katalog 1.6.0, STATUS SORU-034): mühendis onayı (spec 11, AC-PILOT-05/06) ve atomik proje kaydı (FR-F6.10).
        New(ErrorCodes.CiktiOnayKritikHataEngeli, ErrorSeverity.Error, ErrorCategory.Output,
            "Kritik hata bulunduğu için onay verilemez.",
            "Approval blocked: {count} critical finding(s) for revision {revisionId}.",
            "Kritik hataları giderip hesabı yeniden çalıştırın; ardından onayı tekrar deneyin.",
            retryable: false, review: true),
        New(ErrorCodes.CiktiOnayMetniKabulEdilmedi, ErrorSeverity.Error, ErrorCategory.Output,
            "Onay metni okunup kabul edilmeden onay verilemez.",
            "Approval statement was not accepted for revision {revisionId}.",
            "Onay metnini okuyup kabul ettiğinizi işaretleyin.",
            retryable: true, review: false),
        New(ErrorCodes.CiktiOnayIcinHesapEksik, ErrorSeverity.Error, ErrorCategory.Output,
            "Hesap tamamlanmadığı için onay veya çıktı üretilemez.",
            "Calculation incomplete for revision {revisionId}: {missing}.",
            "Eksik girdileri (CRS, geometri, tecviz, DOP, hak eşleştirme) tamamlayıp hesabı yeniden çalıştırın.",
            retryable: true, review: true),
        New(ErrorCodes.CiktiGecerliOnayYok, ErrorSeverity.Error, ErrorCategory.Output,
            "Güncel revizyon için geçerli mühendis onayı yok; çıktı üretilemez.",
            "Output {output} blocked: no valid approval for revision {revisionId}.",
            "Onay ekranından güncel revizyonu onaylayın.",
            retryable: true, review: false),
        New(ErrorCodes.CiktiOnayGeriAlinamaz, ErrorSeverity.Error, ErrorCategory.Output,
            "Onay geri alınamaz.",
            "Approval revocation rejected: {reason}.",
            "Onay 24 saat içinde ve yalnızca mevcutken geri alınabilir; gerekirse yeni revizyon oluşturun.",
            retryable: false, review: false),
        New(ErrorCodes.CiktiProjeKaydiHatasi, ErrorSeverity.Error, ErrorCategory.Output,
            "Proje kaydı veya çıktı dosyası okunamadı ya da yazılamadı; işlem uygulanmadı.",
            "Project store or output file failure: {reason}.",
            "Disk alanını ve dosya erişimini kontrol edin; sorun sürerse yedek kaydı kullanın.",
            retryable: true, review: false),

        // ---- ERR-GUVEN: Güvenlik ----
        New(ErrorCodes.GuvenAuditZinciriBozuk, ErrorSeverity.Critical, ErrorCategory.Security,
            "Denetim kaydı bütünlüğü doğrulanamadı.",
            "Audit chain verification failed at record {recordId}.",
            "Projeyi salt okunur açıp yöneticiye başvurun.",
            retryable: false, review: true),
        New(ErrorCodes.GuvenYetkisizErisim, ErrorSeverity.Critical, ErrorCategory.Security,
            "Bu işlem için yetkiniz yok.",
            "Unauthorized access attempt to {resource}.",
            "Yetkili bir kullanıcıyla yeniden deneyin.",
            retryable: false, review: true),
    ];

    private static readonly Dictionary<string, AppError> ByCode =
        Entries.ToDictionary(e => e.Code, StringComparer.Ordinal);

    /// <summary>Tüm katalog kayıtları.</summary>
    public static IReadOnlyList<AppError> All { get; } = Array.AsReadOnly(Entries);

    /// <summary>Koda göre kaydı getirir; bilinmeyen veya boş kod için genel hata (<see cref="ErrorCodes.GenelBeklenmeyen"/>) döner.</summary>
    public static AppError Get(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var error) ? error : ByCode[ErrorCodes.GenelBeklenmeyen];

    /// <summary>Kod katalogda tanımlıysa true.</summary>
    public static bool TryGet(string? code, out AppError error)
    {
        if (code is not null && ByCode.TryGetValue(code, out var bulunan))
        {
            error = bulunan;
            return true;
        }

        error = ByCode[ErrorCodes.GenelBeklenmeyen];
        return false;
    }

    private static AppError New(
        string code,
        ErrorSeverity severity,
        ErrorCategory category,
        string userMessage,
        string technicalMessage,
        string recommendedAction,
        bool retryable,
        bool review) =>
        new(code, severity, category, userMessage, technicalMessage, recommendedAction, retryable, review,
            AuditLogRequired: severity == ErrorSeverity.Critical); // varsayim: STATUS SORU-021
}
