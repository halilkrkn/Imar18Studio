---
name: systematic-debugging
description: (obra/Superpowers Uyarlaması) DOP/Hisse kuruş/dm² yuvarlama farklarını, NetTopologySuite topoloji/kuantizasyon hatalarını ve DXF/DWG/NCZ eksen kaymalarını kök neden analiziyle ayıklar.
---
# Sistematik Hata Ayıklama (`systematic-debugging`)

1. **Tahminle Kod Değiştirme Yasağı:** Bir test veya saha projesi (`Proje 1..3`) beklenen değerden `0.01 m²` veya `1 dm²` bile saptığında rastgele yuvarlama değiştirme.
2. **4 Adımlı Kök Neden Protokolü:**
   - **1. İzole Tekrar Üretim:** Hatayı en küçük birim testte (`Minimal Reproducible Test`) sabitle.
   - **2. Ara Değer İzleme:** `Gauss` alanı -> `Tecviz/Y1` dengelemesi -> `DOPO` -> `Largest Remainder` zincirindeki her ara `Decimal` / `Fraction` değerini altın veri setiyle karşılaştır.
   - **3. Hipotez ve Kanıt:** Eksen sırası (`Y/X`), `MidpointRounding.AwayFromZero` basamağı veya eşit kalan (`tie-break`) sıralamasından hangisinin sapmaya yol açtığını kanıtla.
   - **4. Kalıcı Regresyon Testi:** Düzeltmeyi uygula ve bu hatanın tekrarlanmasını önleyen regresyon testini ekle.
