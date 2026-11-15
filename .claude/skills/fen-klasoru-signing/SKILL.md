---
name: fen-klasoru-signing
description: Fen Klasörü (He/M2/Dm2), FR.348 Kapak, FR.521 Rölöve Krokisi, 4'lü Alan Cetveli, İki Yönlü Dağıtım Cetveli ve RevisionId bağlı mühendis onayı/imza zincirini yönetir.
---
# Fen Klasörü, Resmî Cetveller ve Mühendis Onay Zinciri (`fen-klasoru-signing`)

1. **Zorunlu Cetvel Seti (`D31 Kural 5, 6, 9`):**
   - `FR.348 (EK-2)` Kapak, `Koordine Özet`, `Noktalı Alan`, `Cepheli Alan`, `Dengeleme Alan`, `Mal Sahipleri Araştırma ve Özet Formu` (Kadastro sıralı), `Tescile Esas Dağıtım Cetveli` (İmar sıralı, `dm²` paydalı), `Fen Klasörü` (`He. | M2 | Dm2` 3 sütunlu) ve `FR.521 (EK-17)` Rölöve Ölçü Krokisi.
2. **Onay → İmza → Checksum Zinciri (`AC-PILOT-06`):**
   - Mühendis onayı o anki girdi ve çıktıların `SHA-256 RevisionId` özetine bağlanır. Herhangi bir koordinat, alan veya hisse değiştiğinde önceki onay otomatik olarak geçersizleşir (`Invalidated`).
   - Pilot aşamasında tüm çıktılara `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` filigranı basılır.
