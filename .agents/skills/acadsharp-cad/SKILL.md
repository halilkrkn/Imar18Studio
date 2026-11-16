---
name: acadsharp-cad
description: ACadSharp ve netDxf ile AutoCAD .dwg (R14-2024) ve .dxf dosyalarını okuma/yazma, Z=0 izdüşümü, kapalı LWPOLYLINE doğrulaması ve katman eşleme kurallarını uygular.
---
# AutoCAD DXF / DWG Adaptör Kuralları (`acadsharp-cad`)

1. **Ortak Arayüz (`D28`):** `ICadFormatAdapter` altında `.dxf` (`netDxf` / `ACadSharp`) ve `.dwg` (`ACadSharp` `DwgReader`/`DwgWriter`) formatlarını destekle.
2. **Z=0 Yatay İzdüşüm ve Kapalı Poligon:**
   - 3B (`Z != 0`) poligon ve noktaları kapıda `Z = 0` yatay düzlemine indirge ve `ERR-GIRD-004` (Warning) uyarısı üret.
   - `LWPOLYLINE` ve `POLYLINE` varlıklarının kapanma toleransını (`<= 0.001 m`) kontrol et.
3. **Standart Katman Eşlemesi:** `TK_PARSEL_GİREN`, `TK_PARSEL_OLUŞAN`, `TK_PARSEL_YOLATERK`, `DÜZENLEME_SINIRI`, `ADA_KENARLT`, `PLAN_ADAKENARI`, `PL_PARK`, `NOKTA_PARSEL_YENI`, `TESCİLLİ_NOKTA`.
