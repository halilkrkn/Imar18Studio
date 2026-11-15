---
name: improve-codebase-architecture
description: (Matt Pocock Uyarlaması) Katman sınırlarını, derin modülleri (GeometryEngine, DopCalculator, CadAdapters) ve test edilebilirliği analiz ederek mimari iyileştirme raporu üretir.
---
# Mimari Derinlik ve Modülerlik İyileştirme (`improve-codebase-architecture`)

1. Sığ ve çok fazla iç detayı dışarı sızdıran sınıfları tespit et; karmaşık geometri (`BisectionEngine`) ve mevzuat (`DopCalculator`) mantığını dar ve net arayüzlere (`Deep Modules`) sahip servisler arkasına topla.
2. `CleanArchitectureRulesTests.cs` içine eklenebilecek yeni mimari koruma kurallarını öner ve doğrula.
