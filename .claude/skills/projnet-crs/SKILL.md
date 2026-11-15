---
name: projnet-crs
description: ProjNet ile ITRF96 (TM 3° / GRS80) ve ED50 (TM 3° / Hayford) dönüşümlerini, Jeodezik (Y_Sağa, X_Yukarı) <-> Kartezyen (X, Y) eksen takasını ve 2D Helmert/Afin raporunu yönetir.
---
# ProjNet CRS ve Jeodezik Eksen Kuralları (`projnet-crs`)

1. **Eksen Sırası Kuralı:**
   - Türkiye haritacılık standardında `Y` Sağa (Doğu / Easting, 6 haneli: `461xxx.xx`), `X` Yukarı (Kuzey / Northing, 7 haneli: `4104xxx.xx`) değeridir.
   - `NetTopologySuite` ve `ProjNet` çağrılarında kapıda `X_Kartezyen = Y_Jeodezik` ve `Y_Kartezyen = X_Jeodezik` dönüşümünü `GeodeticAxisAdapter` üzerinden uygula.
2. **Dönüşüm Parametreleri Raporu (`D31 Kural 9`):**
   - ED50 <-> ITRF96 dönüşümlerinde 2D Helmert / Afin parametrelerini (`A, B, C, D, CX, CY`), ölçek faktörünü ve nokta bazlı `Vy, Vx` uyuşum 잔차 tablosunu üret.
