---
name: code-review
description: (Matt Pocock Uyarlaması) Yazılan kodu hem (1) Clean Architecture ve C# 14 standartları hem de (2) Imar18Studio v2.19 spesifikasyonu ve D31 saha kuralları açısından iki eksende denetler.
---
# İki Eksenli Bağımsız Kod ve Spesifikasyon İncelemesi (`code-review`)

1. **Eksen 1 — Mimari ve Kod Kalitesi:**
   - `Domain` ve `Application` katmanlarında `double`/`float` veya `NetTopologySuite` sızıntısı var mı?
   - `0 test çalıştı` gibi sahte yeşil durumlar var mı?
2. **Eksen 2 — Mevzuat ve Saha Kuralları (`D28 & D31`):**
   - `DOPO = (R1 - D1 - R2 + D2 + R3) / (R1 - D1)` formülü ve `dm²` tescil paydası (`Alan * 100`) birebir uygulanmış mı?
   - Tecviz içi alan farkı yeni imar parselleri yerine `Y1` yol parselinde dengelenmiş mi?
   - Pilot aşamasında Faz 11–20 özellikleri yanlışlıkla kodlanmış mı?
