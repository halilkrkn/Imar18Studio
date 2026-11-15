---
name: imar18-tdd-dotnet
description: Imar18Studio C# 14 / .NET 10 geliştirme sürecinde katı Red-Green-Refactor TDD döngüsünü, Türkçe test isimlendirmesini ve bağımsız referans doğrulamasını uygular.
---
# Imar18Studio Katı TDD (.NET 10 / xUnit) Becerisi

Her kod değişikliğinde aşağıdaki döngüyü sırasıyla uygula:

1. **Bağımsız Referans ve Test Tasarımı (Red):**
   - İlgili `FR-*`, `AC-*` ve `TC-*` kodunu belirle.
   - Üretim kodunu yazmadan önce `tests/` altındaki ilgili projede Türkçe isimli (`Modul_Senaryo_BeklenenDavranis()`) xUnit testlerini yaz.
   - Pozitif senaryonun yanında boş, sıfır, negatif, sınır (boundary), taşma ve geçersiz girdi testlerini ekle.
   - `dotnet test --filter "..."` çalıştırarak testin gerçekten derlendiğini ve beklenen nedenle başarısız olduğunu doğrula.
2. **Minimum Üretim Kodu (Green):**
   - Testleri geçecek en yalın, yan etkisiz ve tip güvenli C# 14 kodunu yaz.
   - Sonucu bilinmeyen bir testi asla kodun mevcut çıktısına uydurma.
3. **Mimari ve Kalite İyileştirme (Refactor):**
   - Kod tekrarlarını temizle, `Decimal`/`Fraction` izolasyonunu kontrol et.
   - `dotnet build -c Release -warnaserror`, ilgili birim testleri ve `dotnet test tests/Imar18.Architecture.Tests -c Release` komutlarını çalıştır; çalışan test sayısının `> 0` olduğunu doğrula.
