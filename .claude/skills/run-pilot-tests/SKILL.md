---
name: run-pilot-tests
description: Birim, NetArchTest mimari ve Imar18.Pilot.Tests doğrulama senaryolarını çalıştırır; sıfır test çalışmasını hata sayar ve detaylı test raporu üretir.
---
# Pilot ve Mimari Test Koşturma (`/run-pilot-tests`) Becerisi

1. İlgili birim testlerini, `tests/Imar18.Architecture.Tests` ve `tests/Imar18.Pilot.Tests` projelerini `dotnet test -c Release` ile çalıştır.
2. Çıktıdaki `Total tests`, `Passed`, `Failed`, `Skipped` sayılarını ayrıştır.
3. **Kritik Kural:** Çalışan test sayısı `0` ise ("No test matches the given testcase filter") bunu kesinlikle **BAŞARISIZ (FAIL)** olarak raporla.
4. Sonucu aşağıdaki şablonla sun:
   - **Pilot / Faz:**
   - **Çalıştırılan Komutlar:**
   - **Geçen / Başarısız / Atlanan Test Sayısı:**
   - **Mimari Denetim (`NetArchTest`) Durumu:**
   - **Çalıştırılamayan Kontroller ve Nedeni:**
   - **Kritik Bulgular ve Sonraki Adım:**
