---
name: imar18-output-contracts
description: Taslak ve nihai çıktılarda (QuestPDF Fen Klasörü, ClosedXML Excel, DXF/DWG/NCZ ve PARSİD) şablon/şema sürümü, RevisionId onay bağı ve Pilot filigran kurallarını zorlar.
---
# Imar18Studio Çıktı Sözleşmeleri (Fen Klasörü, Excel, CAD, PARSİD) Becerisi

1. **Onay ve Revizyon Bağı (`AC-PILOT-05..06`):**
   - Kritik hata (`ErrorSeverity.Critical`) varken mühendis onayı verilemez ve çıktı üretilemez.
   - Mühendis onayı hesaplanan `RevisionId` / `StateHash` değerine bağlanır. Onay verildikten sonra herhangi bir parsel, hisse, geometri veya parametre değişirse onay otomatik olarak düşer.
2. **Pilot Damgası ve Deneysel Şema Kuralı (`AC-PILOT-04`, `AC-PILOT-09`):**
   - Evre 1 (Pilot) aşamasında üretilen tüm PDF ve Excel dosyalarına açıkça `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` ibaresi/filigranı basılır.
   - Doğrulanmış resmi PARSİD şema sürümü tanımlı değilse resmi ihracat engellenir (`AC-PARSID-03`) veya yalnızca açıkça `"Deneysel İç Format"` uyarısıyla taslak üretilir; sahte PARSİD uyumluluğu iddia edilmez.
3. **Canlı Formüllü Excel (`ClosedXML`) ve Fen Klasörü (`QuestPDF`):**
   - Dağıtım cetvellerinde toplam ve kontrol hücreleri sabit metin değil, canlı Excel formülleri (`SUM`, `ROUND`) içerir ve `Decimal` toplamlarıyla kuruş/santimetrekare bazında birebir örtüşür.
   - Fen Klasörü PDF üretiminde şablon sürümü (`TKGM_2023_4_v1`), PDF SHA-256 checksum ve denetim izi (`AuditLog`) sırası korunur.
