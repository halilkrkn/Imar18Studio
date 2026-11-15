---
name: imar18-architecture
description: Clean Architecture katman sınırlarını, NetTopologySuite DTO sızıntı yasağını, CommandBus mutasyon kuralını ve NetArchTest denetimlerini korur.
---
# Imar18Studio Mimari Koruma Becerisi

1. **Bağımlılık Yönü:**
   - `Imar18.Domain` hiçbir iç veya dış UI/I/O/geometri projesine referans veremez.
   - `Imar18.Application` yalnızca `Imar18.Domain` (ve soyut arayüzler üzerinden `GeometryEngine` sözleşmeleri) ile konuşur; `NetTopologySuite.Geometries.*` tipleri `Imar18.Application` public imzalarında, komutlarında veya DTO'larında yer alamaz.
   - `Imar18.Presentation.Avalonia` doğrudan `Imar18.Infrastructure` sınıflarını çağıramaz.
2. **CQRS ve CommandBus:**
   - Durum değiştiren her işlem `ICommand` / `CommandBus` üzerinden yürütülür ve aynı veritabanı transaction'ı içinde `AuditLog` kaydı üretir.
3. **Zorunlu Mimari Test (`Imar18.Architecture.Tests`):**
   - Her PR ve görev sonunda `dotnet test tests/Imar18.Architecture.Tests -c Release` komutu çalıştırılır ve tüm `NetArchTest.Rules` denetimlerinin yeşil olduğu kanıtlanır.
