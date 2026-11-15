---
name: nettopologysuite
description: NetTopologySuite (NTS) poligon topolojisi, 1 mm (0.001 m) kuantizasyon, Gauss (Shoelace) çift alan kontrolü ve 6 gerçek imar adası tipinde BisectionEngine kurallarını zorlar.
---
# NetTopologySuite ve Parselasyon Geometri Kuralları (`nettopologysuite`)

1. **Katman İzolasyonu (`AC-PILOT-02`):** `NetTopologySuite.Geometries.*` tipleri yalnızca `Imar18.GeometryEngine` içinde kalır; `Domain` veya `Application` katmanlarına asla sızmaz.
2. **1 mm Kuantizasyon ve Topoloji:**
   - Tüm köşe koordinatlarını `PrecisionModel(1000.0)` (`0.001 m`) ızgarasına oturt (`Canonical Vertex Snapping`).
   - Her kesim ve kuantizasyon sonrasında `IsValid`, `IsSimple` ve `Decimal` Gauss alanı kontrolünü çalıştır; poligon `MultiPolygon`'a bölünürse veya topoloji bozulursa işlemi geri al (`Rollback`).
3. **6 Gerçek İmar Adası Geometrisi (`BisectionEngine`):**
   - Düz dikdörtgen, sırt sırta çift cepheli omurgalı (`103/104 Ada`), 45° köşe pahlı (`C1, C2, C6, C7`), yamuk/trapez, üçgen/kama ve konkav L-ada tiplerinde cepheye `75°–105°` diklik aralığını ve minimum cephe/derinlik şartlarını doğrula.
