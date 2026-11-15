---
name: ncz-netcad
description: Netcad 8.5 (.ncz) ikili (binary) ve XML (<NetmapManager>) katman yapısını, Windows-1254 (Turkish) karakter kodlamasını ve Netcad .CKS (DOPO/OKO) dosya formatını işler.
---
# Netcad .NCZ ve .CKS Adaptör Kuralları (`ncz-netcad`)

1. **Netcad `.NCZ` Yapısı (`D31 Kural 7`):**
   - `01 78 00 00 00` başlığını, gömülü PNG önizlemesini (`IHDR..IEND`), `<NetmapManager>` XML meta verisini ve nokta/obje/katman bloklarını `Windows-1254` (`Encoding.GetEncoding(1254)`) Türkçe karakter desteğiyle oku.
   - Okuma hatası veya desteklenmeyen özel makro durumunda kullanıcıya `.dxf` / `.dwg` yedek aktarım (`Fallback`) yolunu açık hata koduyla (`IMR-E-2009`) sun.
2. **Netcad `.CKS` Hesap Dosyası (`D31 Kural 1`):**
   - `R1, D1, R2, D2, R3, DOPO, OKO` satırlarını içeren `.CKS` özet dosyasını hem içe aktar (`CksFileReader`) hem de dışa aktar (`CksFileWriter`).
