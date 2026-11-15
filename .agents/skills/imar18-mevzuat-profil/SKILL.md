---
name: imar18-mevzuat-profil
description: TPYS_2022 uzman onaylı mevzuat profilini, sürüm/checksum doğrulamasını ve test-data/synthetic altın veri seti kurallarını yönetir.
---
# Imar18Studio Mevzuat Profili ve Altın Veri Seti Becerisi

1. **Sürümlü Mevzuat Profili (`MevzuatProfili`):**
   - Tecviz katsayıları, DOP üst sınırı (`0.45m`), hata çemberi örnekleme parametreleri ($400n$) ve asgari parsel ölçüleri kod içine sabit sayılar (magic numbers) olarak gömülmez; `TPYS_2022` kimlikli ve SHA-256 checksum'lı `MevzuatProfili` üzerinden okunur.
   - Onaysız veya checksum'ı bozuk mevzuat profiliyle resmi hesap/çıktı üretilemez (`AC-VER-03..04`).
2. **Altın Veri Seti (`test-data/synthetic/pilot-1..4/`):**
   - Gerçek harita mühendisliği projelerinden türetilen test verilerinde tüm kişisel veriler (Malik Adı, TC Kimlik No, Cilt/Sayfa) anonimleştirilir ve koordinatlar yerel ofsetle kaydırılır (`AC-PILOT-08`).
   - Her altın veri seti klasöründe girdi dosyaları (`.dxf`, `.dwg`, `.ncz`, sentetik tapu JSON/PDF) ve uzman tarafından doğrulanmış beklenen çıktı değerleri (`expected-results.json`) birlikte tutulur.

- **Tecviz Kuralı (D1):** Tecviz sınırı mühendis girdisidir (`TecvizGirdisi.TecvizSiniriM2`); sabit kodlanmaz; `TecvizChecker` yalnızca girilen sınırı (`|GaussAlanı - TapuAlanı| <= TecvizSınırıM2`) denetler.
