---
name: implement
description: (Matt Pocock + Superpowers Uyarlaması) Seçilen faz/ticket görevini katı TDD (Red-Green-Refactor), Decimal/Fraction izolasyonu ve NetArchTest kontrolüyle uygular.
---
# Katı TDD ile Görev Uygulama (`implement`)

1. **Adım 1 (Red):** Önce beklenen sonucu (`saha_proje_1..3` altın veri setlerinden veya bağımsız hesaptan) doğrulayan xUnit testini yaz ve `dotnet test` ile başarısız olduğunu gör.
2. **Adım 2 (Green):** Yalnızca testi geçecek en sade, tip güvenli C# 14 / .NET 10 kodunu yaz (`double`/`float` yasak, `NetTopologySuite` sızıntısı yasak).
3. **Adım 3 (Refactor & Arch Check):** Kodu temizle, `dotnet test Imar18Studio.sln` ve `CleanArchitectureRulesTests` testlerinin `%100` yeşil olduğunu doğrula, ardından `docs/STATUS.md` dosyasını güncelle.
