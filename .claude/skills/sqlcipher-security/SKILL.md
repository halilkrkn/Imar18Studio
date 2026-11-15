---
name: sqlcipher-security
description: SQLite + SQLCipher (.imr18), Argon2id, Windows DPAPI, Ed25519 AuditLog zinciri ve KVKK veri maskeleme kurallarını yönetir (Faz 11-13 ve AC-PILOT-08).
---
# SQLCipher, Kriptografi ve KVKK Güvenlik Kuralları (`sqlcipher-security`)

1. **Pilot Kapsam Kilidi (`D27`):**
   - `docs/STATUS.md` içinde aktif evre `EVRE 1: PİLOT` iken tam `SQLCipher / Argon2id / DPAPI / Ed25519` üretim şifrelemesi kodlanmaz; yalnızca düz yerel kayıt ve `SHA-256 RevisionId` kullanılır. `Faz 11–13` açıldığında devreye girer.
2. **Public Depo ve KVKK Kuralı (`AC-PILOT-08`):**
   - Gerçek malik adları, TC Kimlik numaraları ve ham müşteri dosyaları asla depoya veya loglara yazılmaz; yalnızca anonimleştirilmiş altın veri setleri (`test-data/synthetic/`) kullanılır.
3. **Faz 11–13 Kurumsal Güvenlik:**
   - `.imr18` konteyneri `AES-256 SQLCipher`, `Argon2id` (64 MB, 3 iterasyon), `DPAPI CurrentUser` ve `NSec Ed25519` imzalı `AuditLog` zinciriyle korunur.
