---
description: Imar18Studio Pilot/Pilot Sonrası kapsam kilidi, KVKK/Public Repo sentetik veri sınırı ve LLM Guardrail kuralları.
trigger: always_on
---
# Güvenlik, Veri Koruma ve Pilot Kapsam Sınırları

- **Aktif Faz Kilidi (`docs/STATUS.md`):**
  - `docs/STATUS.md` içinde aktif evre `EVRE 1: PİLOT (FAZ P0–P7)` olduğu sürece Faz 11–20 özellikleri (SQLCipher/Argon2id/DPAPI, Ed25519 üretim imzası, `.imr18.lock`, `Tesseract` OCR, tam WCAG sertifikasyonu, ürün içi LLM, telemetri, üretim kod imzalama) kodlanmaz.
- **Public Depo ve Sentetik Veri Kuralı (`AC-PILOT-08`):**
  - Gerçek tapu kaydı, malik adı, TC Kimlik No, baba adı, adres, ham müşteri `.dxf`/`.dwg`/`.ncz` dosyası, gizli koordinat, sertifika veya API anahtarı public depoya, CI loglarına veya artifact'lara kesinlikle konulmaz.
  - Harita mühendisinden alınan gerçek saha projeleri yalnızca kişisel verileri maskelenip koordinatları yerel ofsetle kaydırıldıktan sonra `test-data/synthetic/pilot-1..4/` altına eklenir.
- **Deterministik Motor ve LLM Sınırları:**
  - Ürün içi LLM (Faz 18'de eklendiğinde dahi) hiçbir koşulda alan, DOP, tecviz, parselasyon veya hisse hesabı yapamaz, mevzuat kararı veremez ve proje dosyasına doğrudan yazamaz.
- **Taslak Çıktı ve Şema Sözleşmesi:**
  - Pilot aşamasında üretilen tüm PDF ve Excel çıktıları `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` ibaresiyle damgalanır.
  - **`.CKS` istisnası (`SORU-039`, proje sahibi onayı 19 Kasım 2026):** `.CKS` Netcad ile bayt bayt uyumlu bir takas dosyası olduğundan içine Pilot damgası yazılmaz (Netcad 1. satırdaki metnin üstüne çerçeve çizer). Taslak niteliği dosya adındaki `PILOT` ibaresi (ör. `Imar18_PILOT_CKS_DOP_Ozeti.CKS`), `.sha256` yan dosyası, UI damgası ve RevisionId'li audit kaydıyla izlenir.
  - Doğrulanmış resmi PARSİD şeması yoksa çıktı yalnızca "deneysel iç format" olarak adlandırılır; sahte PARSİD uyumluluğu iddia edilmez.

- **Git ve Platform Bağlayıcılığı (D9):** `.claude/settings.json` yalnızca Claude Code'u bağlar; Antigravity için kural `AGENTS.md`'dedir: `git push` ve `main`'e merge kesinlikle yasaktır.
