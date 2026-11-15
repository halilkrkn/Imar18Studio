---
description: Imar18Studio Clean Architecture, DDD, CQRS, Decimal/Fraction tip izolasyonu ve Çoklu CAD (DXF/DWG/NCZ) mimari kuralları.
trigger: always_on
---
# Mimari Kurallar (Clean Architecture + DDD + CQRS)

- **Katman Sınırları:**
  - `Imar18.Domain`: Saf C# 14 ve .NET 10 BCL kullanır; UI, veritabanı, dosya sistemi veya 3. parti geometri kütüphanelerine bağımlı olamaz.
  - `Imar18.GeometryEngine`: Yalnızca geometrik/mekânsal hesap, eksen dönüşümü (`CoordinateAxisSwapAdapter`), iki döngülü `BisectionEngine` ve kuantizasyon sorumluluğu taşır.
  - `Imar18.Application`: CQRS / `CommandBus` orkestrasyonunu yürütür; `NetTopologySuite.Geometries` tipleri Application sözleşmelerine veya dış DTO'lara kesinlikle sızamaz.
  - `Imar18.Infrastructure`: `ICadFormatAdapter` (`.dxf`, `.dwg`, `.ncz`), `PdfPig`, `ClosedXML`, `QuestPDF` ve yerel veri saklama arayüzlerini uygular.
  - `Imar18.Presentation.Avalonia`: Yalnızca MVVM (`CommunityToolkit.Mvvm`) ViewModel'leri ve `CommandBus` ile çalışır; `Infrastructure` katmanına doğrudan erişemez.
- **Tip İzolasyonu (`Decimal` ve `Fraction`):**
  - Tapu alanı, tecviz, DOP, mahsup, tahsis ve hisse hesapları yalnızca `System.Decimal` veya `Fraction(BigInteger Pay, BigInteger Payda)` ile yapılır.
  - `double` yalnızca `Imar18.GeometryEngine` ve `SkiaSharp` ekran dönüşüm matrislerinde kullanılabilir; geometri motorundan çıkan alanlar kapıda 2 ondalık basamağa (`MidpointRounding.AwayFromZero`) yuvarlanarak `Decimal` tipine dönüştürülür.
- **Eksen ve Kuantizasyon Doğrulaması:**
  - `.dxf`/`.dwg` Kartezyen $(X, Y)$ ve Netcad `.ncz` Jeodezik $(Y, X)$ koordinatları kapıda normalize edilir ve $Z=0$ yatay izdüşüm düzlemine indirgenir.
  - Kuantizasyon sonrası `NTS.IsValid`, `Decimal` Gauss alanı ve `postQuantizationAreaToleranceM2` doğrulanır; topoloji bozulursa çözüm geri alınır.
- **Mutasyon, Onay ve UI Duyarlılığı:**
  - Tüm veri mutasyonları `CommandBus` üzerinden geçer ve aynı transaction içinde `AuditLog` kaydı üretir (reddedilen girişimler de kaydedilir).
  - Mühendis onayı hesaplanan `RevisionId` değerine bağlanır; veri değişirse onay otomatik olarak geçersizleşir.
  - 50 ms üzeri tüm işlemler `IProgress<T>` ve `CancellationToken` ile arka planda çalıştırılır.
  - `NetArchTest` kuralları ihlal edilirse görev tamamlanmış sayılmaz.
- **Paket Sürümü Kilidi (`NU1101`/`NU1102`/`NU1103`):**
  - Paket sürümü hatası (`NU1101`/`NU1102`/`NU1103` vb.) olursa sürümü KENDİN DEĞİŞTİRME. Önce hatanın tam metnini, paket adını ve istenen sürümü raporla; NuGet'te mevcut sürümleri listele (`dotnet package search <paket> --exact-match` veya `nuget.org`) ve önerini tek satırda yaz. Proje sahibinin onayı olmadan `Directory.Build.props` ve `.csproj`'daki `PackageReference` sürümlerini değiştirme.
- **Faz / Görev Sonu Zorunlu Oturum Uyarısı:**
  - KURAL: Herhangi bir geliştirme fazı veya görevi tamamlandığında, gereksiz bağlam birikimini ve token tüketimini önlemek için yanıtının en sonuna mutlaka şu uyarıyı ekle:
    `🛑 FAZ TAMAMLANDI: Token tasarrufu ve temiz bağlam için şimdi mevcut sohbeti kapatıp yeni bir sohbet oturumu açın.`

