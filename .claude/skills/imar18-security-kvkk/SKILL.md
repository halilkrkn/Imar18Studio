---
name: imar18-security-kvkk
description: İki evreli güvenlik ve KVKK kurallarını yönetir; Pilot'ta sentetik veri ve atomik audit/checksum, Faz 11-12'de SQLCipher, Argon2id DEK/KEK, DPAPI ve Ed25519 imza zincirini zorlar.
---
# Imar18Studio Güvenlik, Kriptografi ve KVKK Becerisi

Bu beceri `docs/STATUS.md` içindeki aktif evreye göre iki kademeli çalışır:

## Kademe 1 — Evre 1: PİLOT (`Faz P0–P7`) Zorunlu Kuralları
1. **Sentetik ve Anonim Veri Sınırı (`AC-PILOT-08`):**
   - Public GitHub deposuna, test fixture'larına (`test-data/synthetic/`), CI loglarına veya hata raporlarına gerçek malik adı, TC Kimlik No, baba adı, adres, gerçek tapu kütük bilgisi veya gizli koordinat kesinlikle yazılamaz.
2. **Atomik Kayıt ve Denetim İzi (`AuditLog`):**
   - Proje kaydı geçici dosya + atomik yer değiştirme (`AtomicProjectWriter` / `File.Replace`) ile yarım yazmaya karşı korunur (`AC-IO-01`).
   - Veri mutasyonu ile başarılı işlem `AuditLog` kaydı aynı transaction içinde commit edilir.
   - **Reddedilen İşlem Kaydı:** Kritik hata nedeniyle engellenen veya onaysız denenen çıktı girişimleri de `AuditLog` tablosuna kaydedilir.
3. **Pilot Kapsam Sınırı:**
   - Pilot aşamasında SQLCipher, Argon2id, DPAPI, Ed25519 üretim imzası ve TPM 2.0 kodlanmaz; Pilot checksum'u dijital imza olarak sunulmaz.

## Kademe 2 — Evre 2: PİLOT SONRASI (`Faz 11–12`) Zorunlu Kuralları
*(Yalnızca `docs/STATUS.md` içinde Faz 11–12 açıldığında devreye girer)*
1. **Faz 11 (`AC-SEC-01..08`):** `.imr18` konteyneri SQLCipher (AES-256) ile şifrelenir; rastgele üretilen DEK, kullanıcı parolasından Argon2id ile türetilen KEK ile sarılır.
2. **Faz 12 (`AC-AUDIT-01..10`):** Her `AuditLog` kaydı SHA-256 hash zinciriyle bağlanır ve DPAPI `CurrentUser` korumalı Ed25519 özel anahtarıyla imzalanır. Zincir bozulursa uygulama otomatik olarak `ReadOnly` (salt-okunur) moda geçer.
