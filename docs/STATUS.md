# STATUS.md — Imar18Studio Proje Durumu, Faz Kilidi ve İki Evreli (P0–P7 & Faz 11–20) Yol Haritası Takibi

> **Hedef Depo Konumu:** `docs/STATUS.md`
>
> **Referans Spesifikasyon:** `Imar18Studio_Spesifikasyon_v2_19.md` (Birleştirilmiş Nihai Sürüm: D17–D31 ve P0 İnceleme Düzeltmeleri D1–D16)
>
> **Son Güncelleme:** 17 Kasım 2026

## 1. Aktif Faz Kilidi (Proje Sahibi Onaylı)

| Parametre | Değer | Açıklama |
| ----- | ----- | ----- |
| **AKTİF EVRE** | **`EVRE 1: PİLOT (FAZ P0 – FAZ P7)`** | Yalnızca Faz P0–P7 kapsamındaki çekirdek modüller (Sayısal Çekirdek, Geometri, DOP/TecvizGirdisi, **Birincil `.NCZ` + Yedek `.DXF/.DWG` Girişi**, Temel UI, Revizyona Bağlı Onay, Temel Audit ve `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgalı Taslak Çıktı) geliştirilir. |
| **EVRE 2 DURUMU** | **`KİLİTLİ (FAZ 11 – FAZ 20: PİLOT SONRASI)`** | Faz 11–20 özellikleri Pilot kapanışına kadar **kodlanmaz**. |
| **Çift Platform Uyumu** | **Claude Code (`.claude/`) + Antigravity (`.agents/`)** | Tüm `SKILL.md` (**30 adet: 11 çekirdek Imar18 + 19 yardımcı**), 5 alt ajan ve `rules/*.md` (4 adet) dosyaları her iki araçta da ortak çalışır (`D30`, `D11`). |
| **Veri Güvenliği Sınırı** | **Sentetik / Anonim Veri (`AC-PILOT-08`)** | Public depoda yalnızca sentetik/anonimleştirilmiş `Proje-A`, `Proje-B`, `Proje-C` (`901..903`) kodları bulunur (`D4`, `D12`). |
| **Yerel Gerçek Veri (`test-data/real/`)** | **Gitignore'lu, yalnızca yerel (Proje Sahibi Kararı)** | Gerçek cetvel/NCZ dosyaları kodlama ajanının yerel doğrulaması için bilgisayarda tutulur; GitHub'a gitmez. Ajan bu verileri public dosyalara kopyalamaz; gerçek veriye bağlı testler `Category=RealData` taşır ve CI'da dışlanır. |
| **Riskler (`D2`)** | **`D1/D2/R3` Sıfırdan Farklı Altın Veri Eksik** | (17 Kasım 2026: `SORU-008` kapandı — sıfırdan farklı $D_1, D_2, R_3$ sentetik testle doğrulanır.) Mevcut 3 altın veri setinde $D_1 = D_2 = R_3 = 0$ durumundadır; sıfırdan farklı $D_1, D_2, R_3$ içeren altın veri henüz mevcut değildir (`SORU-008`). |

## 2. Aktif Çalışma ve Dosya Kilit Tablosu (D27 Eşzamanlılık Kontrolü)

| Ajan | Branch (Dal) | Kilitlenen Faz / Katman / Dosyalar | Durum | Başlangıç / Bitiş Zamanı | Not |
| ----- | ----- | ----- | ----- | ----- | ----- |
| Dokümantasyon & CI Ajanı | `fix/p0-rapor-duzeltmeleri` | `docs/*`, `.claude/*`, `.agents/*`, `.github/workflows/ci.yml`, `tests/Imar18.Architecture.Tests/*`, `test-data/synthetic/*` | **Kilit Açık (Tamamlandı)** | 2026-11-14 | P0 inceleme raporu ve harita mühendisi kararları (`D1–D16`) dosyalara işlendi; 15 Kasım 2026'da yeniden denetlenip F1–F8 düzeltildi. Git commit ve `dotnet build/test` doğrulaması proje sahibi tarafından yapılacaktır (sohbet ortamında git/dotnet yoktur; commit sayısı doğrulanmamıştır). |
| Claude Code (Üretici) | `feat/p1-sayisal-cekirdek` | `src/Imar18.Domain/Numerics/*`, `src/Imar18.Domain/Diagnostics/*`, `tests/Imar18.Domain.Tests/*`, `docs/STATUS.md`, `docs/PILOT_ROADMAP_v2_19.md` (yalnızca `IMR-*` → `ERR-*` ifadeleri) | **Kilit Açık (Üretim Tamamlandı)** | 2026-11-16 | P1.1–P1.3 Claude Code üretimi tamamlandı; **Antigravity bağımsız test ve denetimi bekleniyor.** `main`'e merge/`git push` yapılmadı. |
| Claude Code (Üretici) & Antigravity (Denetçi) | `feat/p2-geometri-motoru` | `src/Imar18.GeometryEngine/*`, `src/Imar18.Domain/Geometry/*`, `src/Imar18.Domain/Diagnostics/ErrorCatalog.cs` (yalnızca yeni P2 kodları), `tests/Imar18.GeometryEngine.Tests/*`, `tests/Imar18.Domain.Tests/Geometry/*`, `tests/Imar18.Domain.Tests/Diagnostics/*`, `docs/STATUS.md` | **Kilit Açık (Onaylandı ve dev'e Birleştirildi)** | 2026-11-17 | P2.1–P2.3 Claude Code üretimi ve Antigravity bağımsız denetimi tamamlandı (301 test %100 yeşil). `dev` dalına `--no-ff` ile birleştirildi; `main`'e merge/`git push` yapılmadı. |
| Claude Code (Üretici) & Antigravity (Denetçi) | `feat/p3-mevzuat-kurallari` | `src/Imar18.Domain/Mevzuat/*`, `src/Imar18.Domain/Revizyon/*`, `src/Imar18.Domain/Diagnostics/ErrorCatalog.cs` (yalnızca yeni P3 kodları), `src/Imar18.GeometryEngine/Tecviz/*`, `tests/Imar18.Domain.Tests/Mevzuat/*`, `tests/Imar18.Domain.Tests/Revizyon/*`, `tests/Imar18.Domain.Tests/Diagnostics/*`, `tests/Imar18.GeometryEngine.Tests/Tecviz/*`, `docs/STATUS.md` | **Kilit Açık (Onaylandı ve dev'e Birleştirildi)** | 2026-11-17 | P3.1–P3.3 Claude Code üretimi ve Antigravity bağımsız denetimi tamamlandı (483 test %100 yeşil). `dev` dalına `--no-ff` ile birleştirildi; `main`'e merge/`git push` yapılmadı. |

## 3. Evre 1: PİLOT İlerleme Takibi (Faz P0 – Faz P7) ve Bağlayıcı Önkoşullar (`D13`)

> **Faz Önkoşul ve Notları (`D8`, `D13`):**
> - **Smoke Test Notu (`D8`):** Faz P0'da oluşturulan `[Trait("Category", "Smoke")]` işaretli başlangıç testleri (`true.Should().BeTrue()`), **Faz P1'den itibaren gerçek birim ve entegrasyon testleriyle değiştirilecektir**.
> - **P3.1 Önkoşulu:** ~~`SORU-013` ve `SORU-016` mühendis teyidi~~ → **karşılandı (17 Kasım 2026, proje sahibi kararı)**.
> - **P3.2 Önkoşulu:** ~~`SORU-008` ve `SORU-014`~~ → **karşılandı** (`SORU-014` kapalı; `SORU-008` sentetik fixture varsayımıyla kısmen kapalı).
> - **P3.3 Önkoşulu:** ~~`SORU-015`~~ → **karşılandı** (`proje_b_dagitim_eslesmesi.json` tamamlandı, 17 Kasım 2026).
> - **P4.0 Önkoşulu:** `P4` başında önce **`P4.0: NCZ fizibilite spike (D14)`** koşturulur.
> - **P6.0 Önkoşulu:** `P6` başında **`P6.0: NCZ'den anonim koordinat fixture üretim aracı (D15 — yeni iş, henüz yazılmadı)`** geliştirilir.

### Faz P0 — Spesifikasyon, Çift Uyumlu Skill/Klasör İskeleti, Sentetik Veri ve Erken CI
* [x] **P0.1:** `Imar18Studio_Spesifikasyon_v2_19.md` birleştirilmiş nihai spesifikasyonu tamamlandı.
* [x] **P0.2:** `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `docs/STATUS.md` oluşturuldu.
* [x] **P0.3:** 3 saha projesi (`Proje-A`, `Proje-B`, `Proje-C`) analiz edilerek `test-data/synthetic/` altına anonimleştirildi (`AC-PILOT-08`).
* [x] **P0.4:** `.claude/` ve `.agents/` (30 `SKILL.md`: 11 çekirdek + 19 yardımcı, 5 subagent, 4 kural, `settings.json`), `Imar18Studio.sln`, `Imar18.Architecture.Tests` ve `.github/workflows/ci.yml` üretildi; `D1–D16` inceleme düzeltmeleri dosyalara işlendi (commit/test doğrulaması bekliyor).

### Faz P1 — Sayısal Çekirdek (`Imar18.Domain`)
* [x] **P1.1:** `Decimal` yuvarlama sözleşmesi ve `Fraction(BigInteger Pay, BigInteger Payda)` rasyonel kesir motoru (`imar18-domain-rules`). Antigravity bağımsız test ve denetim onayı verildi (16 Kasım 2026). (`Fraction.cs`, `TescilHissesi.cs`; `FromDecimal` kayıpsız.)
* [x] **P1.2:** Deterministik `Largest Remainder` yuvarlama farkı dağıtımı (`Proje-B` referansı `SORU-011`'e bağlıdır). Antigravity bağımsız test ve denetim onayı verildi (16 Kasım 2026). (`LargestRemainderAllocator.cs`; LR çıktısı Σ = payda bağımsız testlerle doğrulandı.)
* [x] **P1.3:** Çekirdek `AppError` ve `ErrorCatalog` (`AC-ERR-01..03`). Antigravity bağımsız test ve denetim onayı verildi (16 Kasım 2026). (`AppError.cs`, `ErrorCatalog.cs`, `Result.cs`; katalog sürümü 1.1.0, `AuditLogRequired` eşitlendi.)

> **P1 Notları:**
> - **Roadmap düzeltmesi (16 Kasım 2026):** `docs/PILOT_ROADMAP_v2_19.md` P1.3 başlığındaki `IMR-E/W/I-xxxx` ve P3.2'deki `IMR-E-3001` ifadeleri spec §5 (D17) ile çeliştiği için `ERR-<ÖNEK>-NNN` / `ERR-DOP-001` ile değiştirildi; başka roadmap içeriğine dokunulmadı. `.claude/skills` ve `.agents/skills` altındaki `acadsharp-cad`, `ncz-netcad`, `turkish-legal-domain` `SKILL.md` dosyalarında hâlâ `IMR-W-2004`, `IMR-E-2009`, `IMR-E-3001` ifadeleri vardır; **düzeltildi (16 Kasım 2026, ayrı commit):** `IMR-E-3001` → `ERR-DOP-001`, `IMR-E-2009` → `ERR-GIRD-NCZ-001`, `IMR-W-2004` → yeni `ERR-GIRD-004` (Warning, Z≠0 yatay izdüşüm uyarısı; katalog 1.1.0); `.claude/skills` ve `.agents/skills` birebir aynıdır.
> - **Smoke testleri (`D8`):** `[Trait("Category","Smoke")]` testleri silinmedi. Domain için gerçek testler (`FractionTests`, `TescilHissesiTests`, `LargestRemainderAllocatorTests`, `ErrorCatalogTests`, `ResultTests`) eklendi; `Imar18.Domain.Tests` smoke testi artık gerçek testlerle birlikte durur ve P1 sonunda **gerçek testlerle değiştirilmiş** sayılır. Application/GeometryEngine/Infrastructure/Pilot smoke testleri ilgili fazlarda değiştirilecektir.
> - **Bağımsız inceleme (`reviewer`, 16 Kasım 2026):** Kritik bulgu yok. Düzeltilenler: malik kimliğinin hata `TechnicalMessage`'ına sızması (KVKK), `weights` koleksiyonunun tek anlık kopyası, `ErrorCatalog.All` salt okunur, `AllocateAlanM2` null kontrol sırası, tek paydaşta alan doğrulaması. Karar gerektirenler `SORU-021`'de; `TescilHissesi` public yapıcısı (positional `record struct`) ile geçersiz örnek kurulabilmesi ve `Fraction.ToDecimal` varsayılan `scale=6` (alan için `ToAlanM2`/scale 2 kullanılmalı) Antigravity denetimine bırakıldı.
> - **[ANTIGRAVITY TEST BULGUSU — P1 Sayısal Çekirdek Bağımsız Denetimi (16 Kasım 2026)]:**
>   - **Bulgu 1 (Orta — Spec Tutarsızlığı):** `AppError` modelinde `AuditLogRequired` alanı eksik. Spec §5.3 JSON örneğinde `"auditLogRequired": true` varken §5.4 C# modelinde tanımlanmamıştır. Kod §5.4'ü esas almıştır.
>   - **Bulgu 2 (Düşük/Orta — Tip Güvenliği):** `TescilHissesi` positional `record struct` olduğundan derleyicinin ürettiği public birincil yapıcıyla `Olustur(...)` kontrolleri bypass edilip sıfır payda veya negatif pay ile geçersiz örnek kurulabilmektedir (Kanıt: `AntigravityIndependentAuditTests.TescilHissesi_Public_Yapici_Gecersiz_Durumlari_Engelleyemez_Guvenlik_Kaniti`).
>   - **Bulgu 3 (Düşük — CI Ortamı):** `LargestRemainderAllocatorTests` altın JSON dosyasını `AppContext.BaseDirectory`'den `Imar18Studio.sln` arayarak bulur. Tam checkout yapılan GitHub CI'da geçerlidir ancak `.sln` kopyalanmayan izole ortamlarda kırılma riski taşır.
>   - **Eklenen Bağımsız Testler:** `AntigravityIndependentAuditTests.cs` (7 yeni test: Fraction default struct, TescilHissesi güvenlik kanıtı ve 5 bağımsız sınır testi; toplam test sayısı 162'ye ulaştı, %100 yeşil).
>   - **Kapanış ve Onay (16 Kasım 2026):** **BULGU-1 kapandı** — `AppError`'a `AuditLogRequired` eklendi, spec §5.4 yalnızca bu satırla §5.3'e eşitlendi; katalog varsayılanı (varsayım) `Severity == Critical => true`, diğerleri `false`. **BULGU-2 kapandı** — `TescilHissesi` `sealed record` (sınıf), yapıcı private, özellikler salt-okunur, tek giriş `Olustur(...)`; zafiyet kanıt testi silinip yerine ret/null-default testleri yazıldı. **BULGU-3 açık (teknik borç):** P6'da ortak `TestDataLocator` ile çözülecek. `Fraction.ToDecimal` negatif yarım değer (AwayFromZero) testleri genişletildi, kod değişmedi. **Antigravity bağımsız denetimi (176 test %100 yeşil, 0 bulgu) sonucunda P1.1–P1.3 [x] olarak onaylandı.**
> - **Kod/Test eşlemesi:** `ErrorCatalog` yalnızca çekirdek hataları içerir (Pilot sonrası kodlar Faz 11–20'de eklenir); kodlarda mevzuat sayısal değeri uydurulmadı (yalnızca spec'te geçen `%45` DOPO üst sınırı `ERR-DOP-001`'de anılır).

### Faz P2 — Geometri Motoru (`Imar18.GeometryEngine`)
* [x] **P2.1:** `CoordinateAxisSwapAdapter` ($x_{\text{NTS}} = Y_{\text{jeo}},\, y_{\text{NTS}} = X_{\text{jeo}}$) ve $Z=0$ yatay izdüşüm indirgeme kapısı. Antigravity bağımsız test ve denetim onayı verildi (17 Kasım 2026).
* [x] **P2.2:** İlk köşeye $(X_1, Y_1)$ ötelemeli yerel orijin Gauss (Shoelace) alan hesabı. Antigravity bağımsız test ve denetim onayı verildi (17 Kasım 2026).
* [x] **P2.3:** Proje CRS/dilim ve ölçek girdisi (`D3` — hardcode yok), iki döngülü `BisectionEngine`, canonical vertex kuantizasyonu ve konkav ada `MultiPolygon` güvenli duruşu (`TC-GEOM-001`). Antigravity bağımsız test ve denetim onayı verildi (17 Kasım 2026).

> **P2 Notları (17 Kasım 2026):**
> - **Kod/Test eşlemesi:**
>   - P2.1 → `Imar18.Domain.Geometry.GeodeticPoint/GeodeticPoint3D`, `HorizontalProjection` (Z=0, tek `ERR-GIRD-004`), `CoordinateQuantizer` (1 mm, mutlak koordinatta AwayFromZero) ve `Imar18.GeometryEngine.CoordinateAxisSwapAdapter` (internal; `x_NTS = Y_jeo`, `y_NTS = X_jeo`, yerel orijinli).
>   - P2.2 → `PolygonRing` (private yapıcı; ≥3 köşe, ardışık tekrar yok, sıfırdan farklı alan; kesinlik zarfı: yerel < 10⁶ m, ≤ 6 ondalık, ≤ 5 000 köşe → `ERR-GIRD-005`) ve `GaussAreaCalculator` (ilk köşeye ötelemeli, S1 = S2 çift kontrol, `decimal` tam kesin, 2 ondalık AwayFromZero).
>   - P2.3 → `ProjectCoordinateReference` (yalnız doğrulama, `ERR-CRS-001/003`) ve `Imar18.GeometryEngine.Subdivision.BisectionEngine` (tek kesim).
>     - Açı taraması 75–105°; iç döngü cephe boyunca bisection.
>     - Kesim köşeleri 1 mm'ye kuantize edilir; parça ve kalan aynı köşeyi paylaşır.
>     - Kuantizasyon sonrası NTS `IsValid` ve `Decimal` Gauss alanı (parça, kalan ve parça+kalan−ada) toleransla yeniden doğrulanır.
>     - Konkav adada `MultiPolygon` oluşursa `ERR-TOPO-004` ile güvenli duruş yapılır.
> - **Yeni hata kodları (katalog 1.2.0):** `ERR-TOPO-003/004`, `ERR-PLAN-003/004/005`, `ERR-GIRD-005`, `ERR-CRS-003` (`SORU-029`).
> - **Mevzuat değeri uydurulmadı:**
>   - 75–105° aralığı spec §4.2'den alındı.
>   - Tolerans, açı adımı ve iterasyon sayısı girdidir.
>   - En küçük adım (0.001°) ve en fazla 200 iterasyon yalnızca uygulama güvenlik sınırıdır (sonsuz döngü/DoS koruması).
> - **Mimari:** `architect` alt ajanı tasarım ve risk analizini yaptı. NTS tipleri yalnızca `internal` sınıflarda kalır (`InternalsVisibleTo Imar18.GeometryEngine.Tests`). Domain'de `double` yoktur.
> - **Bağımsız inceleme (`reviewer`, 17 Kasım 2026):** Sonuç "Request changes". Kritik bulgu yok, bulgular TDD ile düzeltildi:
>   - **H1:** Parça+kalan−ada sapması da toleransla denetleniyor. Yeni köşenin eklendiği ada kenarı/köşesi `CutStartEdgeIndex`/`CutEndEdgeIndex`/`CutEndOnExistingVertex` ile raporlanıyor.
>   - **H2:** Kesim doğrusu ada kenarıyla çakışınca oluşan geri dönen çıkıntı temizlendi; L adada tam dik kesim artık kabul ediliyor.
>   - **M1:** Teğet köşe artık MultiPolygon sayılmıyor.
>   - **M2:** Açı adımı ve iterasyon için üst sınır eklendi; double aralığı tükenince döngüden çıkılıyor.
>   - **M3:** Dejenere parça artık `ERR-TOPO-003` ile raporlanıyor (`DegeneratePiece`).
>   - **L1:** Cephe kenarında kesin kesişim zorunlu.
>   - **L2:** Yerel çerçeve dışı NTS koordinatı reddediliyor.
>   - **L5:** Kalan ve ada sapması ayrı alanlarda raporlanıyor.
>   - **L7:** Hata mesajlarında koordinat sızıntısı testleri eklendi.
>   - Eksik ada tipleri için testler eklendi: üçgen/kama, 45° pahlı köşe, saat yönündeki halkada eğik açı.
> - **[ANTIGRAVITY TEST VE DENETİM ONAYI — P2 Geometri Motoru Bağımsız Denetimi (17 Kasım 2026)]:**
>   - **Denetim Kapsamı:** `code-review`, `imar18-tdd-dotnet`, `imar18-cad-topology` ve `nettopologysuite` kuralları doğrultusunda `src/` koduna kesinlikle dokunulmadan `tests/Imar18.GeometryEngine.Tests/AntigravityIndependentGeometryAuditTests.cs` (21 yeni bağımsız xUnit testi) yazıldı ve koşturuldu.
>   - **6 Gerçek İmar Adası Geometrisi:** Düz dikdörtgen (1800 m²), sırt sırta çift cepheli omurgalı 103/104 ada (3600 m²), 45° köşe pahlı ada (1450 m², C1/C2 köşe pahını kesen 1312 m²'lik kesim), yamuk/trapez ada (800 m²), dik üçgen/kama ada (900 m², hipotenüsü kesen 500 m²'lik kesim) ve konkav L-ada (1150 m², hem geniş kolda hem iç köşeyi geçen dar kolda tek parçalı kesimler) bağımsız matematiksel referanslarla doğrulandı.
>   - **Alan Korunumu ve Kuantizasyon:** Tüm kesimlerde parça ve kalan alanları toplamının ada alanına tam eşitliği (sıfır sapma), tüm kesim köşelerinin mutlak ITRF96 koordinatlarında 1 mm (0.001 m) ızgarasında (`CoordinateQuantizer.IsOnGrid`) olduğu ve `NtsValidity.IsValid` topolojik geçerliliği teyit edildi.
>   - **75°–105° Diklik Aralığı:** Min sınır 75.000°, max sınır 105.000°, tercih edilen 90.000° diklik ve aralık dışı (74.999°, 105.001°) değerlerin `ERR-PLAN-003` ile reddedildiği sınır testleriyle doğrulandı.
>   - **Y/X Eksen Takası:** $x_{\text{NTS}} = Y_{\text{jeo}} - Y_0$, $y_{\text{NTS}} = X_{\text{jeo}} - X_0$ eksen takası, mutlak AwayFromZero kuantizasyonu ve harita CW/CCW yönlerinin NTS düzleminde tam eşleştiği doğrulandı.
>   - **Konkav Ada MultiPolygon Bölünme Tespiti:** U-adada kollar arasından geçen kesimde `transitions > 2` durumu tespit edilerek `ERR-TOPO-004` (`TopoKonkavKesimCokParcali`) ile durulduğu, `RequiresEngineerReview == true` olduğu ve `SORU-023` uyarınca ilk tespitte taramanın kesildiği (`Attempts.Count == 1`) doğrulandı.
>   - **Temiz Mimari ve Tip İzolasyonu:** `tests/Imar18.Architecture.Tests` ile NetTopologySuite tiplerinin `Imar18.GeometryEngine` dışına sızmadığı ve `Domain`/`Application` katmanlarında `double`/`float` bulunmadığı teyit edildi.
>   - **Test Sonucu:** Çözüm genelinde 301 test (Domain: 218, GeometryEngine: 73, Architecture: 7, Application: 1, Infrastructure: 1, Pilot: 1) Release modunda %100 yeşil geçti. Sıfır hata ile P2.1–P2.3 alt adımları [x] olarak onaylandı.
> - **Açık kalan reviewer notları:**
>   - **M4:** Domain `GaussAreaCalculator` kendi üzerinde kesişen halkayı tespit etmez (NTS `IsValid` GeometryEngine'dedir). P3 tecviz/DOP girişinde halka GeometryEngine doğrulamasından geçmelidir.
>   - **M5/L4:** CAD/NCZ adaptörü köşeleri 1 mm'ye (≤ 6 ondalık) yuvarlamakla yükümlüdür; mutlak TM aralık kontrolü CRS'e bağlıdır. İkisi `SORU-025`/`SORU-028`'e bağlıdır.
>   - **L6:** `IProgress<T>` Application katmanında sarmalanacak.
> - **Kapsam dışı (ertelendi, `SORU-027/029/030`):** ProjNet dönüşümü ve 2D Helmert raporu, Y1 alan dengelemesi, ardışık çok parselli bölme, sırt sırta omurgalı ada, cephe/derinlik ölçütleri ve komşu halka (T-kavşak) güncellemesi.

### Faz P3 — Mühendis Girdili Tecviz, DOP ve Hak Eşleştirme Kuralları (`Imar18.Domain` & `Imar18.Application`)
* [x] **P3.1:** `TecvizGirdisi` (mühendis girdisi, RG 27.09.2022/31966) ve `TecvizChecker` (`|Gauss - Tapu| <= TecvizSınırıM2`) + yardımcı Md. 8(2)/8(5) hesaplayıcıları (`TC-TECVIZ-001..002`). Antigravity bağımsız test ve denetim onayı verildi (17 Kasım 2026).
* [x] **P3.2:** `DopCalculator` (§25.1: $R_1, D_1, R_2, D_2, R_3$; kamuya ayrılan alan $= R_1 - R_2$ türetilir; `PARK` $R_2$'ye girmez) (`TC-DOP-001..007`). + `Y1` yol terk dengelemesi (§25.4) ve %45 aşım karşılama sırası (§4.4). Antigravity bağımsız test ve denetim onayı verildi (17 Kasım 2026).
* [x] **P3.3:** `FR-F1.12` Hak Eşleştirme ($	ext{net hak} = 	ext{giren} \times (1 - \text{DOPO})$, mühendis girdisiyle `malik -> imar parseli` eşleştirmesi, $\text{dm}^2$ payda ve `Largest Remainder` $\pm 1\text{ dm}^2$ dengelemesi) ve `RevisionId` onay bağı (`D16`). + Md. 17/1-(d) hisse çözümlemesi ve geçici↔tescil kimlik eşlemesi. Antigravity bağımsız test ve denetim onayı verildi (17 Kasım 2026).

> **P3 Notları (17 Kasım 2026 — Claude Code üretimi ve Antigravity bağımsız denetim onayı):**
> - **Proje sahibi kararları:** §6'daki `SORU-008..030` aynı gün kararlarla güncellendi (`SORU-008`/`SORU-015` sentetik fixture varsayımıyla kısmen kapalı; `SORU-031` proje sahibi teyidine açıktır).
> - **Kod/Test eşlemesi:**
>   - P3.1 → `Imar18.Domain.Mevzuat.Tecviz`:
>     - `TecvizGirdisi`: private yapıcı + `Olustur`, `ERR-TECVIZ-004`.
>     - `TecvizChecker`: parsel bazlı esas; toplam sınır girilmişse çift-geçiş; sonuçlar farklıysa `ERR-TECVIZ-002`; eksik girdi `ERR-TECVIZ-003`.
>     - `TecvizKademeTablosu` + `ArsivOnayliTecvizYardimcisi` (Md. 8(2)): tablo **girdidir**, sabit yönetmelik değeri kodlanmadı.
>     - `Imar18.GeometryEngine.Tecviz.HataCemberiHesaplayici` (Md. 8(5)): NTS `Buffer(±m_p)`, NTS yalnızca internal; `IsValid` değilse `ERR-TOPO-001`.
>     - Testler: `TecvizCheckerTests`, `ArsivOnayliTecvizYardimcisiTests`, `HataCemberiHesaplayiciTests`. Proje-A/B/C tecviz farkları (0.12 / 0.20 / 1.97) Netcad sınırları (21.02 / 75.07 / 53.82) içinde doğrulandı.
>   - P3.2 → `Imar18.Domain.Mevzuat.Dop`:
>     - `DopGirdisi` / `DopCalculator`: tam `Fraction` ile hesap, 7 hane AwayFromZero; kamuya ayrılan = R1 − R2.
>     - %45 tavanı tam kesirde (`ERR-DOP-001`). R2 ≠ dengeleme toplamı `ERR-DOP-004` uyarısıdır, dağıtılmaz.
>     - `AsimKarsila`: tescil harici → belediye → muvafakatli kamu → kamulaştırma; kaynak yetmezse `ERR-DOP-005`.
>     - `YolTerkDengelemesi` (§25.4): önkoşul `TecvizDegerlendirmesi.Gecti`; fark yalnız Y1'e eklenir.
>     - Altın veri birebir: DOPO 0.1240195 / 0.2015451 / 0.1674996; Y1 1078.90 / 5367.04 / 2026.25; Proje-C 0.04 m² uyarısı.
>   - P3.3 → `Imar18.Domain.Mevzuat.Tahsis` + `Imar18.Domain.Revizyon`:
>     - `HakEslestirmeServisi`: giren alan malik bazında 2 ondalık (resmî cetvel gibi), net hak spec §4.4, imar parseli Σ ±1 dm² (`ERR-TAPU-005`), LR ile dm² payda kapanışı.
>     - Md. 17/1-(d) (`ERR-TAPU-007`) farklı malik sayısına göre uygulanır. Malik tahsis farkı `ERR-TAPU-006` uyarısıdır.
>     - `ParselKimlikEslemesi`: çift yönlü bire bir eşleme (`ERR-TAPU-008`).
>     - `RevisionId` / `RevizyonOzetiOlusturucu` (SHA-256, uzunluk önekli kanonik). `HesapRevizyonu`: tecviz + DOP + hak eşleştirme girdisi. `MuhendisOnayi.GecerliMi`.
>     - Proje-B 101/1: Σ pay = 353974 (`SORU-011`); eşit kalanlı 66397'lerden kimlik sırasında sonuncusu 1 dm² kaybeder.
> - **Yeni hata kodları (katalog 1.3.0):** `ERR-TECVIZ-004`, `ERR-DOP-003/004/005`, `ERR-TAPU-005/006/007/008` (`SORU-031`). `ERR-TECVIZ-001` teknik mesajına `{parcelId}` eklendi.
> - **Mimari:** Domain'de `double`/`float` yok. NTS yalnızca `HataCemberiHesaplayici` içinde. Application katmanına P3'te kod eklenmedi; CommandBus/audit ve kritik hata onay engeli P5.1–P5.2'dedir.
> - **Bağımsız inceleme (`reviewer`, 17 Kasım 2026):** "Request changes", Critical yok. Bulgular TDD ile düzeltildi:
>   - **High-1:** Md. 17/1-(d) kontrolü ve müstakillik farklı `MalikId` sayısına göre yapılıyor; aynı malikin satırları tescilde birleştiriliyor.
>   - **M1:** Y1 dengelemesi `TecvizDegerlendirmesi.Gecti` önkoşuluna bağlandı (çift-geçiş atlatılamaz).
>   - **M2:** `SORU-031` eklendi.
>   - **M3:** Aşım mesajında parsel kodu var; girdi eksikken `ERR-TECVIZ-002` üretilmiyor.
>   - **M4:** `HesapRevizyonu` tecviz sınırı/kaynak notu, R1..R3/D1..D2, ortak katılım ve toplam sınırı kapsıyor.
>   - **L1:** Aşım 2 ondalığa yuvarlanıyor.
>   - **L2:** Ortak katılım ≤ R1 − D1.
>   - **L3:** Doğruluk taşması `Result` hatası; `IsValid` kontrolü eklendi.
>   - **L4:** Revizyon sıralaması yinelenen kimlikte de deterministik; null listede `ArgumentException`.
> - **SORU-015 fixture (17 Kasım 2026, ikinci commit):** `test-data/synthetic/pilot-3-shared-parcel/proje_b_dagitim_eslesmesi.json` gerçek cetvel yalnızca yerelde okunarak tamamlandı (public dosyaya isim/gerçek ada-parsel kopyalanmadı; `check-no-real-data.ps1` OK). Eski satırlarla karşılaştırmada yalnızca 102/1 (7.57 m²) ve 103/7 (450.00 m², TAM) eklendi; alan/hisse değerleri değişmedi. `malikKodu` alanları cetvel sıra numarasına göre tutarlı hale getirildi (aynı malik farklı parsellerde farklı kod taşıyordu) ve bir `girenHisse` yazım hatası (4/24 → 1/24) düzeltildi. Uçtan uca `ProjeBDagitimEslesmesiTests` 40 malikin net hakkını resmî İTh değerleriyle doğrular; bu test giren alanın Largest Remainder yerine malik bazında yuvarlanması gerektiğini ortaya çıkardı (`SORU-031 g`).
> - **[ANTIGRAVITY TEST VE DENETİM ONAYI — P3 Mevzuat ve Hak Eşleştirme Motoru Bağımsız Denetimi (17 Kasım 2026)]:**
>   - **Denetim Kapsamı:** `code-review`, `imar18-tdd-dotnet`, `imar18-domain-rules` ve `imar18-mevzuat-profil` kuralları doğrultusunda `src/` üretim kodlarına kesinlikle dokunulmadan `tests/Imar18.Domain.Tests/AntigravityIndependentMevzuatAuditTests.cs` (17 yeni bağımsız xUnit testi) yazıldı ve koşturuldu.
>   - **Tecviz ve Sınır Denetimi:** `TecvizChecker` tam sınır eşitliğinde (|Gauss - Tapu| == Sınır) kabul, 0.01 m² aşımda `ERR-TECVIZ-001` (Critical), çift-geçişte parsel geçer/toplam aşar ve parsel aşar/toplam geçer durumlarında `ERR-TECVIZ-002` (Warning) + `ERR-TECVIZ-001` üretildiği; Proje-A (21.02 m²), Proje-B (75.07 m²), Proje-C (53.82 m²) altın tecviz değerlerinin sınır içinde kaldığı doğrulandı.
>   - **DOP ve %45 Tavan Denetimi:** `DopCalculator` tam kesir (Fraction) hassasiyetinde tavan denetimi yaptığı (0.45000001 durumunda yuvarlanmış değer 0.4500000 kalsa bile tam kesir > 0.45 olduğundan `ERR-DOP-001` ürettiği), tam 0.4500000 sınırında aşım saymadığı, sıfırdan farklı $D_1, D_2, R_3$ formülünü doğru uyguladığı, kamuya ayrılan alanı $R_1 - R_2$ olarak türettiği, yetersiz kaynakta `ERR-DOP-005` verdiği ve `YolTerkDengelemesi`nin tecviz geçmediğinde çalışmadığı, altın veride Y1'e farkı tam eklediği (Proje-A: 1078.90, Proje-B: 5367.04, Proje-C: 2026.25) doğrulandı.
>   - **Hak Eşleştirme ve Tahsis:** AAD Yönetmeliği Md. 17/1-(d) uyarınca muvafakatsız paydaşın tek başına müstakil parsele verilmesinin `ERR-TAPU-007` (Critical) ile engellendiği; aynı malikin farklı kadastro parsellerinden gelen paylarının birleştirilip müstakil parsel oluşturabildiği; 1 dm² üzeri parsel sapmalarının `ERR-TAPU-005` (Critical) ile reddedildiği; `Largest Remainder` ile $\text{dm}^2$ tescil payda kapanışının tam sağlandığı (toplam pay == payda) doğrulandı.
>   - **RevisionId ve Revizyona Bağlı Onay (AC-PILOT-06):** Tecviz sınırı, DOP alanı veya imar tahsis alanında 1 dm² veya oransal bir değişiklik olduğunda `RevisionId` SHA-256 değerinin değiştiği ve önceki `MuhendisOnayi` kaydının `onay.GecerliMi(yeniRevizyon) == false` ile anında düştüğü bağımsız testle kanıtlandı.
>   - **Proje-B Fixture ve KVKK Denetimi:** `proje_b_dagitim_eslesmesi.json` dosyasında 37 imar parseli, 40 malik ve 94 satır bulunduğu; tüm malik kodlarının `Malik-NN` anonim formatında olduğu ve gerçek isim/kimlik bilgisi barındırmadığı, `check-no-real-data.ps1` ile teyit edildi.
>   - **Mimari ve Tip İzolasyonu:** `tests/Imar18.Architecture.Tests` 7/7 yeşil; `Domain` ve `Application` katmanlarında `double`/`float` ve `NetTopologySuite` sızıntısı bulunmadığı teyit edildi.
>   - **Test Sonucu:** Çözüm genelinde 483 test (Domain: 391, GeometryEngine: 82, Architecture: 7, Application: 1, Infrastructure: 1, Pilot: 1) Release modunda %100 yeşil geçti ("0 test çalıştı" durumu yoktur). Sıfır hata ile P3.1–P3.3 alt adımları [x] olarak onaylandı.
> - **Kapsam dışı / açık:**
>   - `TC-DOP-001..006` mahsup kuralları (AAD Md. 14(4), 14(6), 14(8), 16(4..6)) spec'te formül olmadığından kodlanmadı (`SORU-031 f`).
>   - `SORU-023` kararı (75–105° içinde tek parçalı çözüm varsa kabul) `BisectionEngine`'e henüz uygulanmadı (P2 davranış değişikliği, ayrı görev).
>   - Geometri/CRS'in `RevisionId`'ye eklenmesi P4/P5'tedir.
> - **Test sonucu:** 460 test (Domain 368, GeometryEngine 82, Architecture 7, Application 1, Infrastructure 1, Pilot 1) Release modunda %100 yeşil; `dotnet build -c Release -warnaserror` 0 uyarı / 0 hata.
> - **Kapsam dışı / açık:**
>   - `TC-DOP-001..006` mahsup kuralları (AAD Md. 14(4), 14(6), 14(8), 16(4..6)) spec'te formül olmadığından kodlanmadı (`SORU-031 f`).
>   - `SORU-023` kararı (75–105° içinde tek parçalı çözüm varsa kabul) `BisectionEngine`'e henüz uygulanmadı (P2 davranış değişikliği, ayrı görev).
>   - Geometri/CRS'in `RevisionId`'ye eklenmesi P4/P5'tedir.

### Faz P4 — Birincil `.NCZ` Girişi, Yedek `.DXF`/`.DWG`, CRS Girdisi ve Temel UI
* [ ] **P4.0 (`D14`):** **NCZ fizibilite spike** (3 projenin `.NCZ` dosyalarından katman adları ve parsel köşe koordinatlarının çıkarılıp `Decimal` Gauss alanının resmî cetvel alanıyla birebir uyuşmasının doğrulanması; başarısız olursa `P4.1` `.DXF`/`.DWG` ile devam eder).
* [ ] **P4.1 (`FR-F2.9` / `TC-CAD-001`):** `ICadFormatAdapter` üzerinden **birincil Netcad `.ncz`** (`NczFormatAdapter`) ve **yedek `.dxf`/`.dwg`** (`ACadSharp`) okuma + `.CKS` desteği (`D14`).
* [ ] **P4.2:** Proje CRS/dilim ve pafta ölçeği girdisi (`D3` — hardcode yok) ve vektörel/manuel TAKBİS/kroki veri girişi.
* [ ] **P4.3:** Avalonia 11 + `Semi.Avalonia` ekranları, `SkiaSharp` 2D CAD tuvali (60 FPS pan/zoom) ve `IProgress<T>` arka plan çalıştırıcısı.

### Faz P5 — Mühendis Onayı, Temel Audit, Atomik Kayıt ve Taslak Çıktılar
* [ ] **P5.1:** `CommandBus` mutasyon-audit transaction bütünlüğü ve atomik yerel kayıt.
* [ ] **P5.2:** `ApprovalService`: Kritik hatada onay engeli (`AC-PILOT-05`) ve onayın `RevisionId` değerine bağlanması (`AC-PILOT-06`).
* [ ] **P5.3:** `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgalı taslak `QuestPDF` Fen Klasörü + `ClosedXML` Excel cetveli.

### Faz P6 — Anonim Fixture Aracı, Dört Pilot Senaryosu ve Saha Kullanımı (`Imar18.Pilot.Tests`)
* [ ] **P6.0 (`D15` — Yeni İş, Henüz Yazılmadı):** **`.NCZ`'den anonim koordinat fixture üretim aracı** (`<NetmapManager>` XML kişi/kurum/adres alanlarının boşaltılması, gömülü PNG silinmesi, koordinatların sabit ofsetle kaydırılıp `test-data/synthetic/` altına yazılması).
* [ ] **P6.1:** **Pilot 1 (`Proje-A`, `Proje-B`, `Proje-C`):** `test-data/synthetic/` altın veri doğrulama testleri (`AC-PILOT-01`, `SORU-011`, `SORU-014`).
* [ ] **P6.2:** **Pilot 2 (Karma Mülkiyet & DOP):** `test-data/synthetic/pilot-2-mixed-ownership/` testi (`AC-PILOT-02`).
* [ ] **P6.3:** **Pilot 3 (Müşterek Parsel):** `test-data/synthetic/pilot-3-shared-parcel/` `Fraction` ve `Largest Remainder` testi (`AC-PILOT-03`).
* [ ] **P6.4:** **Pilot 4 (Çıktı Sözleşmesi):** `test-data/synthetic/pilot-4-output-contract/` revizyona bağlı onay ve checksum testi (`AC-PILOT-04..06`).
* [ ] **P6.5 *(Yalnızca Proje Sahibi Onayıyla `[x]` Olur — `D10`)*:** Uygulamanın sahada kullanılması ve Bölüm 4'te tespit edilen tüm eksiklerin çözülmesi.

### Faz P7 — CI Kapıları ve Pilot Kapanış Raporu
* [ ] **P7.1 *(Yalnızca Proje Sahibi Onayıyla `[x]` Olur — `D10`)*:** Public GitHub CI kapılarının (`AC-PILOT-07`) ve `check-no-real-data.ps1` sentetik fixture denetiminin (`AC-PILOT-08`) doğrulanması.
* [ ] **P7.2 *(Yalnızca Proje Sahibi Onayıyla `[x]` Olur — `D10`)*:** Windows 10/11 manuel kullanıcı akışı kontrolü (`AC-PILOT-10`), Pilot Kapanış onayı ve proje sahibi tarafından `dev -> main` birleştirmesi.

## 4. Saha Testi Bulguları ve Eksik Çözüm Kayıtları (`D12.3`)

| Kayıt No | Pilot Senaryosu | Tespit Edilen Eksik / Saha Geri Bildirimi | İlgili Katman | Çözüm Durumu |
| ----- | ----- | ----- | ----- | ----- |
| `SAHA-001` | `Proje-A` (İki parselli düzenleme) | Gerçek veri yalnızca yerel `test-data/real/` altında tutulur (`.gitignore`, `D12`); `901/Y1` yol dengelemesi ($+0.12\text{ m}^2$) ve $\text{DOPO}=0.1240195$ sentetik altın veriye işlendi. | `Domain / Infrastructure` | Anonimleştirildi (`D4`, `D12`) |
| `SAHA-002` | `Proje-B` (5 ada, 40+ paydaş) | Gerçek veri yalnızca yerel `test-data/real/` altında tutulur (`.gitignore`, `D12`); $\text{dm}^2$ tescil paydası ($353974$) ve pay toplamı farkı (`353975` vs `353974` — `SORU-011`) kayıt altına alındı. | `Domain / Output` | Anonimleştirildi (`D4`, `D5`, `D12`) |
| `SAHA-003` | `Proje-C` (Park tahsisli düzenleme) | Gerçek veri yalnızca yerel `test-data/real/` altında tutulur (`.gitignore`, `D12`); `PARK (400.00 m²)` alanının $R_2$ dışında ($R_1-R_2$ içinde) olduğu ve $0.04\text{ m}^2$ fark (`SORU-014`) belgelendi. | `Domain / Geometry` | Anonimleştirildi (`D2`, `D5`, `D12`) |

## 5. Evre 2: PİLOT SONRASI Yol Haritası (Faz 11 – Faz 20 — Şu Anda Kilitli)

* [ ] **Faz 11 — Güvenlik Temeli (`FR-F6.11` / `AC-SEC-01..08`):** SQLCipher, `.imr18` şifreli konteyner, rastgele DEK, Argon2id KEK, DPAPI CurrentUser koruması.

* [ ] **Faz 12 — Audit ve Kanıt Zinciri (`FR-F5.12` / `AC-AUDIT-01..10`):** SHA-256 hash zinciri, Ed25519 imzalama, zaman damgası, atomik transaction ve bozulmada ReadOnly mod.

* [ ] **Faz 13 — Çok Kullanıcı ve Dosya Kilidi (`FR-F4.8` / `AC-LOCK-01..07`):** `.imr18.lock`, 60 sn stale lock devralma, ikinci oturumda ReadOnly mod (**Pilot 5**).

* [ ] **Faz 14 — Sürüm ve Profil Yönetimi (`FR-F1.8` & `FR-F5.11` / `AC-VER-01..06`):** `MevzuatProfili` sürümleme, eski profil arşivi, checksum/imza doğrulama ve tam `ParsidErrorMap`.

* [ ] **Faz 15 — Gelişmiş CAD (DXF/DWG/NCZ) ve CRS (`FR-F2.9` / `AC-DXF-01..07`):** Gelişmiş katmanlı `.dxf`/`.dwg`/`.ncz` yazma, etkileşimli Topoloji Onarım sihirbazı, snap ve epok kontrolleri.

* [ ] **Faz 16 — OCR ve Belge Okuma (`FR-F2.8` / `AC-OCR-01..06`):** Yerel `Tesseract` (`tur.traineddata`), SHA-256 doğrulama ve OCR çıktısının zorunlu mühendis doğrulamasına tabi tutulması.

* [ ] **Faz 17 — Erişilebilirlik ve UX (`FR-F4.9` / `AC-A11Y-01..07`):** Tam WCAG 2.2 AA uyumu, NVDA/Narrator ekran okuyucu, klavye tuzağı ve 4.5:1 kontrast testleri.

* [ ] **Faz 18 — Ürün İçi Doğal Dil / LLM Asistanı (`FR-F4.11` / `AC-AI-01..10`):** Doğal dil komut çubuğu, JSON şema doğrulama ve `GuardrailService` (LLM hesap yapmaz, mevzuat kararı vermez, dosyaya yazmaz).

* [ ] **Faz 19 — Telemetri ve Gözlemlenebilirlik (`FR-F4.10` / `AC-TEL-01..06`):** Opt-in telemetri, 90 gün saklama, SHA-256 `crashStackHash` ve tek tıkla kapatma.

* [ ] **Faz 20 — Release ve Dağıtım (`FR-F6.9` / `AC-REL-01..07`):** Üretim kod imzalama, `ProductionGate`, GitHub Release ve geri alma planı.

## 6. Açık Sorular ve Proje Sahibi / Harita Mühendisi Karar Kayıtları (`D1–D16`)

| Soru Kodu | Tarih | Soran Ajan | Soru / Belirsizlik | Proje Sahibi / Mühendis Kararı | Durum |
| ----- | ----- | ----- | ----- | ----- | ----- |
| `SORU-001` | 2026-11-11 | Sistem | v2.18'deki 6 eksik formül ve §15–§16 RTM kod çakışmaları nasıl çözülecek? | v2.19 spesifikasyonunda eşitlendi. | Kapandı |
| `SORU-002` | 2026-11-11 | Sistem | Geliştirme tek seferde mi yoksa Pilot ve Pilot Sonrası olarak iki aşamada mı yürütülecek? | Evre 1: Pilot (P0–P7) ve Evre 2: Pilot Sonrası (Faz 11–20) olarak ayrıldı. | Kapandı |
| `SORU-003` | 2026-11-11 | Sistem | CAD katmanında öncelik sırası ne olacak? | Birincil Netcad `.NCZ`, yedek `.DXF`/`.DWG` (`D14`). | Kapandı |
| `SORU-004` | 2026-11-11 | Sistem | Pilot Raporu ve Post-Pilot Roadmap kuralları nasıl entegre edilecek? | v2.19 §22–§23 içine birleştirildi. | Kapandı |
| `SORU-005` | 2026-11-11 | Sistem | `.claude/` ve `.agents/` nasıl senkronize tutulacak? | `D30` kapsamında birebir aynı tutuldu (`sync-agent-dirs.ps1`). | Kapandı |
| `SORU-006` | 2026-11-12 | Sistem | Saha projelerindeki cetvel ve `.NCZ` kuralları nasıl uygulanacak? | §25 (`D31`) ve `D1–D16` kararlarıyla güncellendi. | Kapandı |
| `SORU-007` | 2026-11-14 | P0 İnceleme | Tecviz sınırı sabit formülle mi yoksa mühendis girdisiyle mi çalışacak? | **Kapandı (mühendis kararı: tecviz elle girilir — `TecvizGirdisi`, RG 27.09.2022/31966, `D1`).** | **Kapandı** |
| `SORU-008` | 2026-11-14 | P0 İnceleme | `D1` (*Eski DOPO > Proje DOPO Alanlar*), `D2` (*Eski DOPO > Proje DOPO Tahsisler*) ve `R3` (*DOP Alınmayan Alanlar*) sıfırdan farklı olduğunda hesaplama kapsamı (hangi parseller, ne zaman) nasıl uygulanacak? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Sıfırdan farklı $D_1, D_2, R_3$ hesabı sentetik testle doğrulanır (`DopCalculator_Sifirdan_Farkli_D1_D2_R3_Genisletilmis_Formulu_Uygulamali`: 3700/9000 = 0.4111111); 3 altın veri setinde $D_1=D_2=R_3=0$ kalır. Gerçek saha örneği gelince altın veri eklenir. | **Kapandı** |
| `SORU-009` | 2026-11-14 | P0 İnceleme | Pafta ölçeği ($1/M$) ve CRS/dilim sabit mi olacak yoksa girdi mi? | **Kapandı (girdi olacak — pafta ölçeği ve CRS/dilim proje/parsel girdisidir, `D3`).** | **Kapandı** |
| `SORU-010` | 2026-11-14 | P0 İnceleme | `test-data/synthetic/` altındaki gerçek parsel alan değerleri ($8699.44$, $26629.48$, $14485.35\text{ m}^2$) kalan kimliklendirme riskine karşı sabit bir ofsetle değiştirilsin mi? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Mevcut altın veri alan değerleri korunur (ofset uygulanmaz). | **Kapandı** |
| `SORU-011` | 2026-11-14 | P0 İnceleme | `Proje-B` `101/1` parselinde ($3539.74\text{ m}^2$, payda $353974$) resmî çıktıdaki örnek payların toplamı $353975$ görünmektedir; testlerde `beklenenResmiCiktiPayToplami` ($353975$) mi yoksa `beklenenDogruLargestRemainderPayToplami` ($353974$) mi referans alınacak? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Testlerde `beklenenDogruLargestRemainderPayToplami` ($353974$) referans alınır; resmî çıktıdaki $353975$ referans değildir. | **Kapandı** |
| `SORU-012` | 2026-11-14 | P0 İnceleme | Spec §24.5'te yalnızca resmî Anthropic kaynakları belirtilirken `.claude/skills/` altında `mattpocock/skills` ve `obra/Superpowers` yerel Markdown uyarlamaları bulunmaktadır; bu 19 yardımcı beceri depoda kalsın mı? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** 19 yardımcı beceri depoda kalır ve `docs/SKILL_KAYNAKLARI.md` içinde belgelenir. | **Kapandı** |
| `SORU-013` | 2026-11-14 | P0 İnceleme | Netcad dengeleme cetvelinde çok parselli düzenlemede tek TOPLAM tecviz basılıyor (`Proje-A`: iki tapu parseli, tek değer $21.02\text{ m}^2$). Parsel bazlı `TecvizGirdisi` ile toplam tecviz nasıl birleşecek/karşılaştırılacak? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Parsel bazlı `TecvizGirdisi` esastır (bağlayıcı). Düzenleme toplamı tecviz sınırı da girilmişse **çift-geçiş şartı** uygulanır: hem parsel bazlı hem toplam bazlı kontrol geçmelidir; sonuçlar farklıysa ikisi yan yana gösterilir (`ERR-TECVIZ-002`). | **Kapandı** |
| `SORU-014` | 2026-11-14 | P0 İnceleme | `Proje-C` DOP sayfası $R_2$ ($12059.06\text{ m}^2$) ile dengeleme cetveli imar parsel toplamı ($12059.10\text{ m}^2$) neden $0.04\text{ m}^2$ farklı; testte hangisi referans alınacak? (`AC-PILOT` "sıfır sapma" hedefi bu soruya bağlı) | **Kapandı (proje sahibi kararı, 15 Kasım 2026):** Netcad DOP sayfası referanstır; $0.04\text{ m}^2$ fark uyarı olarak raporlanır, parsellere dağıtılmaz (spec §25.1). Bu bir proje kararıdır, mevzuattan çıkarılmış hüküm değildir. | **Kapandı** |
| `SORU-015` | 2026-11-14 | P0 İnceleme | `Proje-B` için anonim `malik -> imar parseli` eşleşme tablosu (mühendisin yaklaşık yer krokisi kararı) `Faz P3.3` (`FR-F1.12`) test fixture'ı olarak nasıl temin edilecek? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Anonim `proje_b_dagitim_eslesmesi.json` (902/1, 40 malik, 37 imar parseli, 94 satır) tamamlandı: 102/1 eksik satırı, 103/7 (450.00 m², TAM) eklendi; 105/2'de resmî cetvelin 36964 (−0.01 m²) toplamı belgelendi. `malikKodu` cetvel malik sıra numarasıyla sabit anonim kimliktir. Fixture `ProjeBDagitimEslesmesiTests` ile doğrulanır. | **Kapandı** |
| `SORU-016` | 2026-11-14 | P0 İnceleme | Mühendis tecvizi hangi yönetmelik yöntemiyle (arşiv onaylı / geçici koordinatlı) ve parsel mi yoksa düzenleme toplamı mı bazında alıyor? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** `SORU-013` ile aynı karar: parsel bazlı `TecvizGirdisi` esas, toplam tecviz girilmişse çift-geçiş şartı. Yöntem bilgisi (`KoordinatKalitesi`, `UretimYontemi`) girdi olarak kaydedilir. | **Kapandı** |
| `SORU-017` | 2026-11-15 | Claude | Arazi ve Arsa Düzenlemeleri Hakkında Yönetmelik 11 Eylül 2025 tarihli ve 33014 sayılı Resmî Gazete'deki değişiklikle yeniden düzenlendi. Spec'teki `AAD Md. 14/15/16` madde atıfları güncel numaralarla uyumlu mu? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** RG 11.09.2025/33014 sonrası AAD Md. 14, 15, 34 numaraları günceldir. | **Kapandı** |
| `SORU-018` | 2026-11-16 | Claude | Spec §5.4/§5.5 "bilinmeyen hata genel kodla dönmeli" der ancak §5.2 önek tablosunda genel bir önek yoktur. Varsayım: `ERR-GENEL-001` (`ErrorCategory.General`) eklendi. Spec §5.2 tablosuna `ERR-GENEL-` önekinin eklenmesi uygun mu? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Mevcut güvenli varsayım onaylandı: `ERR-GENEL-001` (`ErrorCategory.General`). | **Kapandı** |
| `SORU-019` | 2026-11-16 | Claude | `ERR-TECVIZ-*` hata seviyeleri spec §4.3.1'de "ErrorCatalog'daki tanıma göre" denilerek tanımsız bırakılmıştır. Varsayım: `ERR-TECVIZ-001` (sınır aşıldı) `Critical` + mühendis incelemesi, `ERR-TECVIZ-002` (parsel/toplam farklı, `SORU-013`) `Warning` + mühendis incelemesi, `ERR-TECVIZ-003` (girdi eksik) `Error`. `Critical` onayı engeller (`AC-PILOT-05`). Seviyeler doğru mu? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Varsayılan `ERR-TECVIZ-*` seviyeleri onaylandı (001 `Critical`, 002 `Warning`, 003 `Error`). | **Kapandı** |
| `SORU-020` | 2026-11-16 | Claude | `LargestRemainderAllocator` eşit kalan sırasında "en büyük başlangıç payı" ifadesi ağırlığın kendisi (`Fraction`) olarak yorumlandı; ayrı bir "başlangıç payı" alanı (örn. kadastro hissesi) tanımlanacaksa P3.3'te (`FR-F1.12`) hangi değerin kullanılacağı netleşmelidir. | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Mevcut varsayım onaylandı: eşit kalanda başlangıç payı = ağırlığın kendisi (`Fraction`). | **Kapandı** |
| `SORU-021` | 2026-11-16 | Claude (reviewer bulgusu) | (a) **KAPANDI (varsayımla):** `AuditLogRequired` `AppError`'a eklendi, spec §5.4 §5.3 ile eşitlendi; varsayılan kural Severity==Critical ⇒ true, diğerleri false. (b) Roadmap P1.3 "mevzuat referansı" bekler; kodlarda mevzuat değeri uydurulmadığı için alan eklenmedi. (c) Görevde adı geçen `ERR-GIRD-NCZ-001` üç segmentlidir (spec'te `ERR-<ÖNEK>-NNN`); alternatif `ERR-GIRD-004`. `AppError`'a `AuditLogRequired`/mevzuat referansı eklensin mi, NCZ kodu iki segmente çevrilsin mi? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Mevcut varsayımlar ve katalog seviyeleri onaylandı (`AuditLogRequired` Critical ⇒ true; `ERR-GIRD-NCZ-001` üç segmentli kalır). | **Kapandı** |
| `SORU-022` | 2026-11-17 | Claude (P2 architect) | `BisectionEngine` tercih açısı neye göre belirlenir (90°, komşu yan sınıra paralel, mühendis seçimi)? Eşit sapmada hangi yön önce denenir? 75–105° aralığı plan notuyla daraltılabilir mi? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Mevcut varsayım onaylandı (tercih/min/max/adım çağıran girdisi; eşit sapmada küçük açı önce). | **Kapandı** |
| `SORU-023` | 2026-11-17 | Claude (P2 architect) | Konkav adada bir açıda `MultiPolygon` oluşursa tarama hemen mi durmalı, yoksa başka açıda tek parçalı çözüm varsa kabul edilebilir mi? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** 75–105° içinde tek parçalı çözüm varsa kabul edilir; hiçbir açıda tek parçalı çözüm yoksa `ERR-TOPO-004`. **Not:** P2 `BisectionEngine` hâlâ ilk tespitte durur; davranış değişikliği P3 kapsamı dışında ayrı görev olarak yapılacaktır. | **Kapandı** |
| `SORU-024` | 2026-11-17 | Claude (P2 architect) | `postQuantizationAreaToleranceM2` değeri nereden gelir (mühendis / mevzuat profili) ve kapsamı ne (yalnız parça mı, parça + kalan mı)? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Tolerans mevzuat profili varsayılanından gelir, mühendis düzenleyebilir. | **Kapandı** |
| `SORU-025` | 2026-11-17 | Claude (P2 architect) | Kadastro/NCZ'den gelen mevcut köşeler de 1 mm'ye yuvarlanacak mı (resmî cetvel uyumu, P4.0 spike)? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** 1 mm ızgara dışı köşe uyarı ile raporlanır ve mühendis onayıyla ızgaraya çekilir (CAD adaptörü, P4). | **Kapandı** |
| `SORU-026` | 2026-11-17 | Claude (P2 architect) | Koordinat yarım değeri (0.0005 m) sıfırdan uzağa (AwayFromZero) mı yuvarlanır? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** AwayFromZero onaylandı; yeni hata kodları onaylı. | **Kapandı** |
| `SORU-027` | 2026-11-17 | Claude (P2 architect) | Parsel derinliği nasıl tanımlanır? Pilot 1 "13 × 16 m" neyi ifade eder? Çok kenarlı cephe, sırt sırta omurga ve ardışık çok parselli bölme hangi fazda? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Cephe/derinlik ölçütleri ve çok parselli bölme P4'te ele alınacaktır. | **Kapandı** |
| `SORU-028` | 2026-11-17 | Claude (P2 architect) | Desteklenen CRS kimlikleri neler (EPSG kodu mu)? Epok zorunlu mu? Pafta ölçeği için izinli değerler var mı? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** ITRF96 (epok 2005.00) TM 3° dilimleri 27–45 ve ED50 desteklenir; liste kodlaması P4'te. | **Kapandı** |
| `SORU-029` | 2026-11-17 | Claude (P2 architect) | Roadmap §4 P2 ile STATUS P2 uyuşmuyor: (a) roadmap `Point2D(decimal Y, decimal X)` der, kodda `GeodeticPoint(X, Y)` adlandırılmış argümanla kullanıldı; (b) roadmap P2.2 `AreaBalancingEngine` (Y1 dengeleme) ve P2.3 `ProjNet`/`HelmertTransformReport`, P2.4 6 ada tipini içerir, STATUS P2.1–P2.3 içermez; (c) yeni 7 hata kodu (`ERR-TOPO-003/004`, `ERR-PLAN-003/004/005`, `ERR-GIRD-005`, `ERR-CRS-003`) ve seviyeleri onaylanıyor mu? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Yeni 7 hata kodu ve seviyeleri onaylandı; Y1 dengelemesi P3.2'de, ProjNet/Helmert P4'te yapılacaktır. | **Kapandı** |
| `SORU-030` | 2026-11-17 | Claude (P2 reviewer H1) | Eğik ada kenarına düşen kesim köşesi 1 mm'ye yuvarlanınca kenarın tam üzerinde olmayabilir (≤ ~0.7 mm). Komşu parsel/yol halkası bu köşeyi bilmezse T-kavşak veya şerit oluşur. Komşu halkalara aynı kanonik köşe hangi fazda ve hangi kuralla eklenecek? Ada sınırından izin verilen sapma ne kadar? | **Kapandı (proje sahibi kararı, 17 Kasım 2026):** Komşu halka kanonik köşe güncellemesi P4'te yapılacaktır. | **Kapandı** |
| `SORU-031` | 2026-11-17 | Claude (P3 üretici) | P3 varsayımları onaylanıyor mu? (a) Yeni hata kodları ve seviyeleri (katalog 1.3.0): `ERR-TECVIZ-004` (girdi geçersiz, Error), `ERR-DOP-003` (girdi geçersiz, Error), `ERR-DOP-004` (R2 ≠ dengeleme toplamı, Warning + inceleme), `ERR-DOP-005` (%45 aşımı kaynaklarla karşılanamıyor, Critical), `ERR-TAPU-005` (imar parseli Σ eşleştirme ≠ alan ±1 dm², Critical), `ERR-TAPU-006` (malik tahsisi net haktan >1 dm² farklı, Warning + inceleme), `ERR-TAPU-007` (Md. 17/1-d muvafakatsız müstakil tahsis, Critical), `ERR-TAPU-008` (geçici↔tescil eşleme geçersiz, Error). (b) %45 tavanı tam kesir üzerinden kontrol edilir (0.45000001 → 7 hanede 0.4500000 olsa da aşım sayılır). (c) Malik toplam tahsis toleransı ±1 dm²'dir (çok parselli malikte LR birikimi uyarı üretebilir). (d) İmar parselindeki paydaş sayısı ve müstakillik farklı `MalikId` sayısına göre belirlenir; aynı kişinin farklı kadastro parsellerinden gelen payları tescilde tek satırda birleştirilir (reviewer High-1). (e) Md. 17/1-(d) kuralı yalnızca çok malikli kadastro parselinden gelen ve muvafakati olmayan paydaşın tek başına bir parsele tahsisini engeller. (f) `TC-DOP-001..006` mahsup kuralları (AAD Md. 14(4), 14(6), 14(8), 16(4..6)) için spec'te formül yoktur; mevzuat metni olmadan kodlanmadı. (g) Malik giren alanı resmî dağıtım cetveli gibi malik bazında round(tapu × hisse, 2, AwayFromZero) hesaplanır (Largest Remainder değil); Proje-B'de Σ giren = 26629.50 (tapu + 0.02) ve 40 malikin net hakkı cetvelin İTh sütunuyla birebir eşittir. (h) Proje-B 105/2'de resmî paylar 36964 toplar (payda 36965); LR +1 dm²'yi Malik-11'e verir ve malik toplamı net haktan +0.02 m² sapar → tek `ERR-TAPU-006` uyarısı. | **Proje sahibi / mühendis teyidi gerekli.** Varsayımlar kodda uygulandı ve testlerle sabitlendi. | **Açık** |

