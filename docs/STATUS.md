# STATUS.md — Imar18Studio Proje Durumu, Faz Kilidi ve İki Evreli (P0–P7 & Faz 11–20) Yol Haritası Takibi

> **Hedef Depo Konumu:** `docs/STATUS.md`
>
> **Referans Spesifikasyon:** `Imar18Studio_Spesifikasyon_v2_19.md` (Birleştirilmiş Nihai Sürüm: D17–D31 ve P0 İnceleme Düzeltmeleri D1–D16)
>
> **Son Güncelleme:** 16 Kasım 2026

## 1. Aktif Faz Kilidi (Proje Sahibi Onaylı)

| Parametre | Değer | Açıklama |
| ----- | ----- | ----- |
| **AKTİF EVRE** | **`EVRE 1: PİLOT (FAZ P0 – FAZ P7)`** | Yalnızca Faz P0–P7 kapsamındaki çekirdek modüller (Sayısal Çekirdek, Geometri, DOP/TecvizGirdisi, **Birincil `.NCZ` + Yedek `.DXF/.DWG` Girişi**, Temel UI, Revizyona Bağlı Onay, Temel Audit ve `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgalı Taslak Çıktı) geliştirilir. |
| **EVRE 2 DURUMU** | **`KİLİTLİ (FAZ 11 – FAZ 20: PİLOT SONRASI)`** | Faz 11–20 özellikleri Pilot kapanışına kadar **kodlanmaz**. |
| **Çift Platform Uyumu** | **Claude Code (`.claude/`) + Antigravity (`.agents/`)** | Tüm `SKILL.md` (**30 adet: 11 çekirdek Imar18 + 19 yardımcı**), 5 alt ajan ve `rules/*.md` (4 adet) dosyaları her iki araçta da ortak çalışır (`D30`, `D11`). |
| **Veri Güvenliği Sınırı** | **Sentetik / Anonim Veri (`AC-PILOT-08`)** | Public depoda yalnızca sentetik/anonimleştirilmiş `Proje-A`, `Proje-B`, `Proje-C` (`901..903`) kodları bulunur (`D4`, `D12`). |
| **Yerel Gerçek Veri (`test-data/real/`)** | **Gitignore'lu, yalnızca yerel (Proje Sahibi Kararı)** | Gerçek cetvel/NCZ dosyaları kodlama ajanının yerel doğrulaması için bilgisayarda tutulur; GitHub'a gitmez. Ajan bu verileri public dosyalara kopyalamaz; gerçek veriye bağlı testler `Category=RealData` taşır ve CI'da dışlanır. |
| **Riskler (`D2`)** | **`D1/D2/R3` Sıfırdan Farklı Altın Veri Eksik** | Mevcut 3 altın veri setinde $D_1 = D_2 = R_3 = 0$ durumundadır; sıfırdan farklı $D_1, D_2, R_3$ içeren altın veri henüz mevcut değildir (`SORU-008`). |

## 2. Aktif Çalışma ve Dosya Kilit Tablosu (D27 Eşzamanlılık Kontrolü)

| Ajan | Branch (Dal) | Kilitlenen Faz / Katman / Dosyalar | Durum | Başlangıç / Bitiş Zamanı | Not |
| ----- | ----- | ----- | ----- | ----- | ----- |
| Dokümantasyon & CI Ajanı | `fix/p0-rapor-duzeltmeleri` | `docs/*`, `.claude/*`, `.agents/*`, `.github/workflows/ci.yml`, `tests/Imar18.Architecture.Tests/*`, `test-data/synthetic/*` | **Kilit Açık (Tamamlandı)** | 2026-11-14 | P0 inceleme raporu ve harita mühendisi kararları (`D1–D16`) dosyalara işlendi; 15 Kasım 2026'da yeniden denetlenip F1–F8 düzeltildi. Git commit ve `dotnet build/test` doğrulaması proje sahibi tarafından yapılacaktır (sohbet ortamında git/dotnet yoktur; commit sayısı doğrulanmamıştır). |
| Claude Code (Üretici) | `feat/p1-sayisal-cekirdek` | `src/Imar18.Domain/Numerics/*`, `src/Imar18.Domain/Diagnostics/*`, `tests/Imar18.Domain.Tests/*`, `docs/STATUS.md`, `docs/PILOT_ROADMAP_v2_19.md` (yalnızca `IMR-*` → `ERR-*` ifadeleri) | **Kilit Açık (Üretim Tamamlandı)** | 2026-11-16 | P1.1–P1.3 Claude Code üretimi tamamlandı; **Antigravity bağımsız test ve denetimi bekleniyor.** `main`'e merge/`git push` yapılmadı. |

## 3. Evre 1: PİLOT İlerleme Takibi (Faz P0 – Faz P7) ve Bağlayıcı Önkoşullar (`D13`)

> **Faz Önkoşul ve Notları (`D8`, `D13`):**
> - **Smoke Test Notu (`D8`):** Faz P0'da oluşturulan `[Trait("Category", "Smoke")]` işaretli başlangıç testleri (`true.Should().BeTrue()`), **Faz P1'den itibaren gerçek birim ve entegrasyon testleriyle değiştirilecektir**.
> - **P3.1 Önkoşulu:** `P3.1` (`TecvizGirdisi` + `TecvizChecker`) uygulaması **`SORU-013`** ve **`SORU-016`** mühendis teyidine bağlıdır.
> - **P3.2 Önkoşulu:** `P3.2` (`DopCalculator`) uygulaması **`SORU-008`** ve **`SORU-014`** mühendis teyidine bağlıdır.
> - **P3.3 Önkoşulu:** `P3.3` (`FR-F1.12` Hak Eşleştirme) test fixture'ı **`SORU-015`**'e bağlıdır.
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
* [ ] **P2.1:** `CoordinateAxisSwapAdapter` ($x_{\text{NTS}} = Y_{\text{jeo}},\, y_{\text{NTS}} = X_{\text{jeo}}$) ve $Z=0$ yatay izdüşüm indirgeme kapısı.
* [ ] **P2.2:** İlk köşeye $(X_1, Y_1)$ ötelemeli yerel orijin Gauss (Shoelace) alan hesabı.
* [ ] **P2.3:** Proje CRS/dilim ve ölçek girdisi (`D3` — hardcode yok), iki döngülü `BisectionEngine`, canonical vertex kuantizasyonu ve konkav ada `MultiPolygon` güvenli duruşu (`TC-GEOM-001`).

### Faz P3 — Mühendis Girdili Tecviz, DOP ve Hak Eşleştirme Kuralları (`Imar18.Domain` & `Imar18.Application`)
* [ ] **P3.1 (`SORU-013` ve `SORU-016`'ya bağlı):** `TecvizGirdisi` (mühendis girdisi, RG 27.09.2022/31966) ve `TecvizChecker` (`|Gauss - Tapu| <= TecvizSınırıM2`) + yardımcı Md. 8(2)/8(5) hesaplayıcıları (`TC-TECVIZ-001..002`).
* [ ] **P3.2 (`SORU-008` ve `SORU-014`'e bağlı):** `DopCalculator` (§25.1: $R_1, D_1, R_2, D_2, R_3$; kamuya ayrılan alan $= R_1 - R_2$ türetilir; `PARK` $R_2$'ye girmez) (`TC-DOP-001..007`).
* [ ] **P3.3 (`SORU-015`'e bağlı):** `FR-F1.12` Hak Eşleştirme ($	ext{net hak} = 	ext{giren} \times (1 - \text{DOPO})$, mühendis girdisiyle `malik -> imar parseli` eşleştirmesi, $\text{dm}^2$ payda ve `Largest Remainder` $\pm 1\text{ dm}^2$ dengelemesi) ve `RevisionId` onay bağı (`D16`).

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
| `SORU-008` | 2026-11-14 | P0 İnceleme | `D1` (*Eski DOPO > Proje DOPO Alanlar*), `D2` (*Eski DOPO > Proje DOPO Tahsisler*) ve `R3` (*DOP Alınmayan Alanlar*) sıfırdan farklı olduğunda hesaplama kapsamı (hangi parseller, ne zaman) nasıl uygulanacak? | **Mühendis teyidi gerekli** (Pilot varsayılanı $D_1=D_2=R_3=0$). | **Açık** |
| `SORU-009` | 2026-11-14 | P0 İnceleme | Pafta ölçeği ($1/M$) ve CRS/dilim sabit mi olacak yoksa girdi mi? | **Kapandı (girdi olacak — pafta ölçeği ve CRS/dilim proje/parsel girdisidir, `D3`).** | **Kapandı** |
| `SORU-010` | 2026-11-14 | P0 İnceleme | `test-data/synthetic/` altındaki gerçek parsel alan değerleri ($8699.44$, $26629.48$, $14485.35\text{ m}^2$) kalan kimliklendirme riskine karşı sabit bir ofsetle değiştirilsin mi? | **Mühendis / Proje sahibi teyidi gerekli** (`D4`). | **Açık** |
| `SORU-011` | 2026-11-14 | P0 İnceleme | `Proje-B` `101/1` parselinde ($3539.74\text{ m}^2$, payda $353974$) resmî çıktıdaki örnek payların toplamı $353975$ görünmektedir; testlerde `beklenenResmiCiktiPayToplami` ($353975$) mi yoksa `beklenenDogruLargestRemainderPayToplami` ($353974$) mi referans alınacak? | **Mühendis teyidi gerekli** (`D5`). | **Açık** |
| `SORU-012` | 2026-11-14 | P0 İnceleme | Spec §24.5'te yalnızca resmî Anthropic kaynakları belirtilirken `.claude/skills/` altında `mattpocock/skills` ve `obra/Superpowers` yerel Markdown uyarlamaları bulunmaktadır; bu 19 yardımcı beceri depoda kalsın mı? | **Varsayılan: Kalsın ve `docs/SKILL_KAYNAKLARI.md` içinde belgelensin; proje sahibi teyidi bekleniyor** (`D11.2`). | **Açık** |
| `SORU-013` | 2026-11-14 | P0 İnceleme | Netcad dengeleme cetvelinde çok parselli düzenlemede tek TOPLAM tecviz basılıyor (`Proje-A`: iki tapu parseli, tek değer $21.02\text{ m}^2$). Parsel bazlı `TecvizGirdisi` ile toplam tecviz nasıl birleşecek/karşılaştırılacak? | **Kısmen kapandı (proje sahibi kararı, 15 Kasım 2026):** Tek parselde tek kontrol; çok parselde parsel bazlı ve toplam bazlı (`DuzenlemeToplamTecvizSiniriM2`, mühendis girdisi) iki kontrol hesaplanır, sonuçlar aynıysa tek mesaj, farklıysa ikisi gösterilir (spec §4.3.1). **Hangisinin bağlayıcı olduğu mühendis teyidi gerektirir.** | **Kısmen Kapandı** |
| `SORU-014` | 2026-11-14 | P0 İnceleme | `Proje-C` DOP sayfası $R_2$ ($12059.06\text{ m}^2$) ile dengeleme cetveli imar parsel toplamı ($12059.10\text{ m}^2$) neden $0.04\text{ m}^2$ farklı; testte hangisi referans alınacak? (`AC-PILOT` "sıfır sapma" hedefi bu soruya bağlı) | **Kapandı (proje sahibi kararı, 15 Kasım 2026):** Netcad DOP sayfası referanstır; $0.04\text{ m}^2$ fark uyarı olarak raporlanır, parsellere dağıtılmaz (spec §25.1). Bu bir proje kararıdır, mevzuattan çıkarılmış hüküm değildir. | **Kapandı** |
| `SORU-015` | 2026-11-14 | P0 İnceleme | `Proje-B` için anonim `malik -> imar parseli` eşleşme tablosu (mühendisin yaklaşık yer krokisi kararı) `Faz P3.3` (`FR-F1.12`) test fixture'ı olarak nasıl temin edilecek? | **Mühendis teyidi ve anonim eşleşme tablosu gerekli** (`D13`, `D16`). | **Açık** |
| `SORU-016` | 2026-11-14 | P0 İnceleme | Mühendis tecvizi hangi yönetmelik yöntemiyle (arşiv onaylı / geçici koordinatlı) ve parsel mi yoksa düzenleme toplamı mı bazında alıyor? | **Mühendis teyidi gerekli** (`D1.7`, `P3.1` bu soruya bağlıdır). | **Açık** |
| `SORU-017` | 2026-11-15 | Claude | Arazi ve Arsa Düzenlemeleri Hakkında Yönetmelik 11 Eylül 2025 tarihli ve 33014 sayılı Resmî Gazete'deki değişiklikle yeniden düzenlendi. Spec'teki `AAD Md. 14/15/16` madde atıfları güncel numaralarla uyumlu mu? | **Mühendis teyidi gerekli** (atıflar doğrulanmadı; hesap kuralları etkilenmez, yalnızca madde numaraları). | **Açık** |
| `SORU-018` | 2026-11-16 | Claude | Spec §5.4/§5.5 "bilinmeyen hata genel kodla dönmeli" der ancak §5.2 önek tablosunda genel bir önek yoktur. Varsayım: `ERR-GENEL-001` (`ErrorCategory.General`) eklendi. Spec §5.2 tablosuna `ERR-GENEL-` önekinin eklenmesi uygun mu? | **Proje sahibi teyidi gerekli.** Geçici varsayım kodda uygulandı. | **Açık** |
| `SORU-019` | 2026-11-16 | Claude | `ERR-TECVIZ-*` hata seviyeleri spec §4.3.1'de "ErrorCatalog'daki tanıma göre" denilerek tanımsız bırakılmıştır. Varsayım: `ERR-TECVIZ-001` (sınır aşıldı) `Critical` + mühendis incelemesi, `ERR-TECVIZ-002` (parsel/toplam farklı, `SORU-013`) `Warning` + mühendis incelemesi, `ERR-TECVIZ-003` (girdi eksik) `Error`. `Critical` onayı engeller (`AC-PILOT-05`). Seviyeler doğru mu? | **Mühendis / proje sahibi teyidi gerekli** (`SORU-013`, `SORU-016` ile bağlantılı). | **Açık** |
| `SORU-020` | 2026-11-16 | Claude | `LargestRemainderAllocator` eşit kalan sırasında "en büyük başlangıç payı" ifadesi ağırlığın kendisi (`Fraction`) olarak yorumlandı; ayrı bir "başlangıç payı" alanı (örn. kadastro hissesi) tanımlanacaksa P3.3'te (`FR-F1.12`) hangi değerin kullanılacağı netleşmelidir. | **Mühendis teyidi gerekli** (P3.3 fixture'ı, `SORU-015`). | **Açık** |
| `SORU-021` | 2026-11-16 | Claude (reviewer bulgusu) | (a) **KAPANDI (varsayımla):** `AuditLogRequired` `AppError`'a eklendi, spec §5.4 §5.3 ile eşitlendi; varsayılan kural Severity==Critical ⇒ true, diğerleri false. (b) Roadmap P1.3 "mevzuat referansı" bekler; kodlarda mevzuat değeri uydurulmadığı için alan eklenmedi. (c) Görevde adı geçen `ERR-GIRD-NCZ-001` üç segmentlidir (spec'te `ERR-<ÖNEK>-NNN`); alternatif `ERR-GIRD-004`. `AppError`'a `AuditLogRequired`/mevzuat referansı eklensin mi, NCZ kodu iki segmente çevrilsin mi? | **Proje sahibi teyidi gerekli.** Şimdilik kodda uygulanmadı / üç segmentli bırakıldı. | **Açık** |

