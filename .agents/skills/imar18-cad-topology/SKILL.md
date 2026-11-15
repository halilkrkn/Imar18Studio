---
name: imar18-cad-topology
description: Çoklu CAD (DXF, DWG ve Netcad NCZ) okuma/yazma, eksen takası (X,Y <-> Y,X), Z=0 izdüşümü, iki döngülü BisectionEngine ve canonical vertex kuantizasyon kurallarını zorlar.
---
# Imar18Studio Çoklu CAD (DXF / DWG / NCZ) ve Topoloji Becerisi (D28)

1. **Çoklu CAD Adaptörü (`ICadFormatAdapter` — FR-F2.9):**
   - Sistem `.dxf` ve `.dwg` dosyalarını `ACadSharp` (`DxfReader`/`DwgReader`), Netcad `.ncz` dosyalarını ise `NczFormatAdapter` (`NativeNczBinaryParser`) üzerinden okur ve yazar.
   - `.ncz` dosyalarında Türkçe karakter kodlaması (`Windows-1254` / `CP857` / `UTF-8`) otomatik algılanır; bozuk veya desteklenmeyen bloklarda uygulama çökmez, `ERR-GIRD-` hatası döner (`AC-CAD-03`).
2. **Format Bazlı Eksen Normalizasyonu ve $Z=0$ İzdüşümü (`AC-CAD-02`):**
   - AutoCAD (`.dxf`, `.dwg`) dosyalarındaki Kartezyen $(X_{	ext{CAD}} = 	ext{Sağa/Doğu},\, Y_{	ext{CAD}} = 	ext{Yukarı/Kuzey})$ koordinatlar ile Netcad (`.ncz`) dosyalarındaki Jeodezik $(Y = 	ext{Sağa/Doğu},\, X = 	ext{Yukarı/Kuzey})$ koordinatlar kapıda normalize edilerek iç modele tek tip $(X_{	ext{jeo}} = 	ext{Kuzey},\, Y_{	ext{jeo}} = 	ext{Doğu})$ olarak aktarılır.
   - NTS Kartezyen geometri motoruna girerken `CoordinateAxisSwapAdapter` ile $x_{	ext{NTS}} = Y_{	ext{jeo}},\, y_{	ext{NTS}} = X_{	ext{jeo}}$ eşlemesi yapılır.
   - Tüm 3B ($Z 
eq 0$) köşe koordinatları işlem öncesinde $Z = 0$ ($XY$ yatay izdüşüm) düzlemine indirgenir.
3. **Yerel Orijinli Gauss Alanı:**
   - Alan hesabından önce poligonun tüm köşeleri ilk köşeye $(X_1, Y_1)$ ötelenir ($X'_i = X_i - X_1,\, Y'_i = Y_i - Y_1$) ve Shoelace formülü uygulanır.
4. **İki Döngülü Bisection ve Canonical Vertex Kuantizasyonu:**
   - Dış döngü cepheye diklik açısını 75°–105° arasında tarar; iç döngü kesim çizgisini ada omurgası boyunca kaydırır.
   - Kesim sonrası köşe koordinatları ızgaraya yuvarlanır (canonical vertex snapping ile komşu parsellerde ortak köşe kimliği korunur).
   - **Kritik Kural:** Kuantizasyon sonrası (1) `NTS.IsValid` topoloji geçerliliği ve (2) `Decimal` Gauss alanı ile `postQuantizationAreaToleranceM2` toleransı yeniden doğrulanır. Konkav adalarda `MultiPolygon` (parçalı kesim) oluşursa veya topoloji bozulursa çözüm geri alınır ve mühendis müdahalesi istenir (`AC-PILOT-01`).
