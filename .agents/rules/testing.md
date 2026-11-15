---
description: Imar18Studio Katı TDD (Red-Green-Refactor), bağımsız referans doğrulaması ve Pilot test kuralları.
trigger: always_on
---
# Test ve Doğrulama Kuralları (Katı TDD)

- **Önce Test (Red-Green-Refactor):**
  - Her yeni davranış veya hata düzeltmesi için önce beklenen bağımsız referans sonucu belirlenir ve başarısız olan xUnit testi yazılır.
  - Sonucu bilinmeyen bir test, mevcut kodun ürettiği çıktıya uydurulamaz.
  - Test adları Türkçe ve davranışı açıkça anlatacak biçimde (`Modul_Senaryo_BeklenenDavranis()`) yazılır.
- **Zorunlu Senaryo Kapsamı:**
  - Kritik hesaplar (Gauss alanı, tecviz, DOP, mahsup, `Fraction`, `Largest Remainder`) için sınır (boundary), negatif, boş, geçersiz, sıfır, taşma (overflow) ve tekrar edilebilirlik testleri zorunludur.
  - Eşit kalanlı hisse dağıtımında kararlı bağlayıcı sıra (büyük pay → küçük malik/hisse kimliği) test edilmelidir.
  - Geometri ve `.dxf`/`.dwg`/`.ncz` testlerinde alan korunumu, çakışma, boşluk, `self-intersection` ve konkav adada `MultiPolygon` duruşu doğrulanmalıdır.
  - Onay verildikten sonra girdi/geometri değiştiğinde onayın otomatik iptal edildiği (`AC-PILOT-06`) test edilmelidir.
- **Sıfır Test Yasağı ve CI:**
  - Yanlış filtre nedeniyle `0 test çalıştı` (test bulunamaması) durumu kesinlikle başarı sayılmaz; çalıştırılan ve geçen test sayısı raporda açıkça yazılır.
  - Çalıştırılmayan hiçbir kontrol "geçti" diye raporlanamaz.
