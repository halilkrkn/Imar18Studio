# Imar18Studio — Nihai Pilot Yol Haritası (PILOT_ROADMAP v2.19)
> **Amaç:** Bu belge, **Claude Code** ve **Google Antigravity** ajanlarının insan müdahalesi olmadan `Imar18Studio` Pilot (MVP) sürümünü adım adım kodlaması, test etmesi ve doğrulaması için hazırlanmış **otonom yürütme yol haritasıdır (Autonomous Agent Execution Roadmap)**.
> **Referans Anayasa:** `docs/Imar18Studio_Spesifikasyon_v2_19.md` (`D17–D31` kilitli)
> **İlerleme Takibi:** `docs/STATUS.md`
> **Kapsam Kilidi (`D27`):** `Faz P0 – Faz P7` tamamlanıp saha kabulü alınmadan `docs/POST_PILOT_ROADMAP_v2_19.md` (`Faz 11–20`) adımlarına geçilmesi **kesinlikle yasaktır**.

---

## 1. Otonom Ajan Çalışma Protokolü (Claude Code & Google Antigravity)

Hem **Claude Code** (`.claude/`) hem de **Google Antigravity** (`.agents/`) bu yol haritasını okuyarak kendi kendine ilerler:

1. **Durum Okuma:** Ajan her oturum başında `docs/STATUS.md` dosyasını okur ve `[ ]` durumundaki ilk alt adımı (`P0.x` .. `P7.x`) tespit eder.
2. **TDD Döngüsü (`Red → Green → Refactor`):**
   - Önce `test-engineer` alt ajanı ilgili adımın kabul kriterlerini (`AC-PILOT-*`, `TC-*`) karşılayan birim/entegrasyon testlerini yazar.
   - Ardından `implementer` alt ajanı yalnızca testi geçecek minimum üretim kodunu (`Decimal` ve `Fraction(BigInteger)` kurallarına sadık kalarak) yazar.
   - `reviewer` ve `architect` alt ajanları `CleanArchitectureRulesTests` ve sayısal izolasyon kurallarını denetler.
3. **Doğrulama & İlerleme:**
   - `dotnet test Imar18Studio.sln --filter "Category!=FullCad&Category!=UiSnapshot"` komutu `%100` yeşil olduğunda ajan `docs/STATUS.md` içindeki ilgili adımı `[x]` olarak işaretler ve bir sonraki adıma geçer.
4. **Tetikleme Komutları:**
   - **Claude Code:** `/pilot-status` (durumu kontrol et) ve `/pilot-next` (sıradaki fazı otonom kodla ve doğrula)
   - **Google Antigravity:** `@pilot-status` ve `@pilot-next` iş akışları

---

## 2. Faz P0 — Depo İskeleti, Mimari Korkuluklar ve Altın Veri Seti

### P0.1 — Temel Dokümanlar ve Ajan Kuralları (`[x]` Hazır)
- `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `docs/STATUS.md`, `docs/Imar18Studio_Spesifikasyon_v2_19.md`
- `.claude/` ve `.agents/` altında 11 `SKILL.md`, 5 alt ajan, 4 kural dosyası ve `.claude/settings.json`

### P0.2 — Anonimleştirilmiş Gerçek Saha Altın Veri Setleri (`[x]` Hazır)
- `test-data/synthetic/pilot-1-concave-parcel/saha_proje_1_golden.json` ve `DOP_Ornek.CKS` (**Proje 1:** 901 Ada 1 ve 2 parseller, $R_1 = 8699.44\text{ m}^2$, `901/Y1` = $1078.90\text{ m}^2$, $\text{DOPO} = 0.1240195$)
- `test-data/synthetic/pilot-2-mixed-ownership/saha_proje_3_park_golden.json` (**Proje 3:** 903 Ada 1 parsel, $R_1 = 14485.35\text{ m}^2$, `903/1/Y1` = $2026.25\text{ m}^2$, `903/1/PARK` = $400.00\text{ m}^2$)
- `test-data/synthetic/pilot-3-shared-parcel/saha_proje_2_hisse_golden.json` (**Proje 2:** 902 Ada 1 parsel, $R_1 = 26629.48\text{ m}^2$, $\text{DOPO} = 0.2015451$, 40+ paydaşlı `dm²` tescil paydası ve 5 imar adası `101..105`)

### P0.4 — Çözüm İskeleti (`Imar18Studio.sln`), `NetArchTest` ve GitHub CI (`[x]` Hazır)
- **Hedef:** 6 `src/` (`Imar18.Domain`, `Imar18.Application`, `Imar18.GeometryEngine`, `Imar18.Infrastructure`, `Imar18.Presentation.Avalonia`, `Imar18.Bootstrapper`) ve 6 `tests/` projesinin oluşturulması.
- **Mimari Kurallar (`CleanArchitectureRulesTests.cs`):**
  1. `Imar18.Domain` dış katmanlara ve `NetTopologySuite`'e bağımlı olamaz.
  2. `Imar18.Domain` ve `Imar18.Application` içindeki alan/oran/tutar alanlarında `double` veya `float` kullanılamaz (`AC-PILOT-01`).
  3. `NetTopologySuite` tipleri `Imar18.GeometryEngine` dışına (`public` imza olarak) sızamaz (`AC-PILOT-02`).
  4. `Imar18.Application` katmanı `Infrastructure` veya `Presentation.Avalonia` katmanlarına referans veremez.
  5. `Imar18.GeometryEngine` katmanı `Infrastructure` veya `Presentation.Avalonia` katmanlarına referans veremez.
- **Kabul Kriteri:** `dotnet test Imar18Studio.sln` komutu 0 hata ile geçer.

---

## 3. Faz P1 — Sayısal Çekirdek ve Hata Kataloğu (`Imar18.Domain`)

### P1.1 — `Fraction` (`BigInteger` Pay/Payda) ve `Ebeb` Sadeleştirme
- **Görev:** `src/Imar18.Domain/Numerics/Fraction.cs` sınıfını `BigInteger Numerator` ve `BigInteger Denominator` ile oluştur.
- **Kurallar (`§4.1`, `D31 Kural 2`):**
  - Negatif payda kabul edilmez; her aritmetik işlem sonrası `BigInteger.GreatestCommonDivisor` ile otomatik sadeleştirilir.
  - `ToDecimal(int scale = 6)` ve `FromDecimal(decimal value)` dönüşümleri `MidpointRounding.AwayFromZero` kullanır.
  - **Tescil Paydası Dönüşümü (`D31 Kural 2`):** Müstakil parselde (`Paydaş Sayısı == 1`) tescil hissesi `"TAM"` döner; hisseli parselde tescil paydası parsel alanının $\text{dm}^2$ tam sayı karşılığıdır ($\text{Payda}_{\text{tescil}} = \text{ Alan}_{\text{m}^2} \times 100$, örn. $3539.74\text{ m}^2 \implies 353974$).
- **Testler:** `tests/Imar18.Domain.Tests/Numerics/FractionTests.cs` (`1/24`, `1/72`, `19/320`, `1/176` gerçek saha kesirleriyle doğrulama).

### P1.2 — `LargestRemainderAllocator` ($\sum = \text{Tam Alan}$ Garantisi)
- **Görev:** `src/Imar18.Domain/Numerics/LargestRemainderAllocator.cs` servisini kodla.
- **Kurallar (`§4.2`, `D31 Kural 2`):**
  - $\text{m}^2$ tahsislerinde kuruş/santimetrekare hassasiyetinde ($0.01\text{ m}^2$), tescil paylarında ise $1\text{ dm}^2$ tam sayı pay hassasiyetinde en büyük kalan (`Largest Remainder`) algoritmasını çalıştırır.
  - Eşit kalan (`tie-break`) durumunda: (1) En büyük başlangıç payına sahip malik, (2) Malik/Parsel ID sözlük sırası (`StringComparer.Ordinal`).
- **Testler:** `Proje-B` (`902/1`, $26629.48\text{ m}^2$, 40+ paydaş) üzerinde dağıtılan payların toplamının tam olarak $26629.48\text{ m}^2$ ve imar parsellerinde $\sum \text{Pay} = \text{ParselAlanı} \times 100$ (`beklenenDogruLargestRemainderPayToplami = 353974` vs `beklenenResmiCiktiPayToplami = 353975` — **referans `SORU-011`'e bağlı**) olduğunu doğrulayan birim testler.

### P1.3 — `ErrorCatalog` (`IMR-E-xxxx`, `IMR-W-xxxx`, `IMR-I-xxxx`) ve `Result<T>`
- **Görev:** `src/Imar18.Domain/Diagnostics/ErrorCatalog.cs` ve `Result<T>` yapılarını oluştur.
- **Kurallar (`§13`):** Tüm hata kodları Türkçe mesaj, mevzuat referansı (örn. `AAD Yönetmeliği Md. 11`, `3194 Md. 18`) ve çözüm önerisi içerir.

---

## 4. Faz P2 — Geometri Motoru (`Imar18.GeometryEngine`)

### P2.1 — Koordinat Kuantizasyonu ($0.001\text{ m}$) ve Eksen Takası (`Y_Sağa, X_Yukarı`)
- **Görev:** `CoordinateQuantizer` ve `GeodeticAxisAdapter` sınıflarını kodla.
- **Kurallar (`§5.1`, `§5.2`):**
  - Türkiye haritacılık standardı olan Jeodezik `(Y: Sağa/Doğu, X: Yukarı/Kuzey)` koordinatlarını `NetTopologySuite` içindeki Kartezyen `(X = Y_Jeodezik, Y = X_Jeodezik)` düzlemine çevirir ve dışarıya yalnızca Domain `Point2D(decimal Y, decimal X)` döner.
  - Tüm koordinatlar $1\text{ mm}$ ($0.001\text{ m}$) ızgarasına yuvarlanır (`PrecisionModel(1000.0)`).

### P2.2 — Gauss (Shoelace) Alan Hesabı ve 4'lü Alan Cetveli Motoru (`D31 Kural 4 & 5`)
- **Görev:** `GaussAreaCalculator` ve `AreaBalancingEngine` sınıflarını oluştur.
- **Kurallar (`§5.3`, `D31 Kural 4 & 5`):**
  - $2A = \sum_{i=1}^{n} X_i (Y_{i+1} - Y_{i-1})$ ve $2A = \sum_{i=1}^{n} Y_i (X_{i-1} - X_{i+1})$ çift kontrolünü `decimal` ile yapar.
  - Kenar uzunluklarını ($s_i = \sqrt{\Delta Y^2 + \Delta X^2}$, `Cephe`) hesaplar.
  - **Dengeleme Alan Hesabı (`D31 Kural 4`):** $\Delta A = A_{\text{tapu}} - A_{\text{hesap}}$ farkı tecviz sınırları içindeyse yeni oluşan imar parsellerinin (`A1..En`) alanlarını değiştirmeyip (`+0.00`), farkı kamuya terk edilen yol (`Y1`) parselinde dengeler (`Proje 1`: $+0.12\text{ m}^2$, `Proje 2`: $+0.20\text{ m}^2$, `Proje 3`: $+1.97\text{ m}^2$).

### P2.3 — `ProjNet` Dönüşümü (Proje CRS/Dilim ve Ölçek Girdisi — Hardcode Yok, `D3`) ve 2D Helmert/Afin Raporu (`D31 Kural 9`)
- **Görev:** `CoordinateTransformService` ve `HelmertTransformReport` sınıflarını oluştur.
- **Kurallar:** `A, B, C, D, CX, CY` parametrelerini, ölçek faktörünü ve nokta bazlı `Vy, Vx` 잔차 (uyuşum) tablosunu üretir.

### P2.4 — 6 Gerçek İmar Adası Geometrisinde `BisectionEngine` (`75°–105°`)
- **Görev:** `ParcelSubdivisionEngine` (`BisectionEngine`) sınıfını 6 gerçek saha geometrisi için kodla:
  1. Düz Dikdörtgen Ada
  2. Sırt Sırta (Çift Cepheli Omurgalı) Ada (`Proje 2` — 103 ve 104 Adalar)
  3. $45^\circ$ Pahlı Köşe Parselli Ada (`Proje 2` — `C1, C2, C6, C7`)
  4. Yamuk / Trapez Ada (`Proje 1` — 102 Ada)
  5. Üçgen / Kama Ada
  6. Konkav L-Ada / Eğrisel Ada (`MultiPolygon` bölünme tespiti + $75^\circ\text{–}105^\circ$ diklik kontrolü + minimum cephe/derinlik kontrolü)

---

## 5. Faz P3 — İmar 18. Madde Domain Kuralları (`Imar18.Domain` & `Imar18.Application`)

### P3.1 — `TecvizGirdisi` ve `TecvizChecker` (Mühendis Girdisi Esası — D1, `SORU-013` ve `SORU-016`'ya Bağlı)
- **Görev:** *"Tapu Planlarında Yanılma Sınırının Belirlenmesi Hakkında Yönetmelik"* (RG 27.09.2022, Sayı 31966) uyarınca `TecvizGirdisi` (`ÖlçekPayda M`, `ÜretimYöntemi`, `KoordinatKalitesi [ArşivOnaylı | Geçici]`, `TecvizSınırıM2` [mühendis girer], `KaynakNotu`) modelini ve yalnızca `|GaussAlanı - TapuAlanı| <= TecvizSınırıM2` (`Decimal`) kontrolü yapan `TecvizChecker` servisini kodla. Sabit/doğrulanmamış klasik tecviz formülü (`0.0004·M·√A + 0.0003·A` — TASLAK-DOĞRULANMADI, kullanılmaz) kodlanmaz; TPYS Md. 8(2) ve Md. 8(5) yalnızca "yardımcı hesaplayıcı (varsayılan değil)" olarak tutulur.
- **Doğrulama:** Altın veri setlerindeki `21.02 m²` (`Proje-A`), `75.07 m²` (`Proje-B`) ve `53.82 m²` (`Proje-C`) değerleri "Netcad raporundan alınan referans tecviz sınırı, mühendis girdisi" olarak `TecvizGirdisi` üzerinden doğrulanır (`TC-TECVIZ-001..002`).
- **Çok Parselli Çift Kontrol (Proje Sahibi Kararı, `SORU-013`):** Tek kadastro parselinde tek sonuç; birden fazla parselde parsel bazlı ve toplam bazlı (`DuzenlemeToplamTecvizSiniriM2`, mühendis girdisi) iki sonuç hesaplanır. Sonuçlar aynıysa tek mesaj, farklıysa ikisi yan yana gösterilir (spec §4.3.1).

### P3.2 — `DopCalculator` (Resmî `R1..R3 / D1..D2` Formülü ve `%45` Tavanı — `D31 Kural 1`)
- **Görev:** `DopCalculator` servisini kodla:
  $$\text{DOPO} = \frac{R_1 - D_1 - R_2 + D_2 + R_3}{R_1 - D_1}$$
- **$R_1$ ("Düzenlemeye Giren Alanlar"):** Düzenlemeye giren parsel alanları toplamı.
- **$D_1$ ("Eski DOPO > Proje DOPO Alanlar"):** Eski DOP oranı proje DOP oranından büyük olan parsellerin alanı.
- **$R_2$ ("İmar Alanları"):** Yalnızca imar parsellerinin (tahsis) alan toplamıdır; PARK, yol, dini tesis, belediye hizmet alanı $R_2$'ye GİRMEZ. Kamuya ayrılan alan $= R_1 - R_2$ olarak TÜRETİLİR (kullanıcı girdisi değil). Pilot varsayılanı $D_1 = D_2 = R_3 = 0$; sıfırdan farklı senaryolar `SORU-008` ile açık kalır.
- **$D_2$ ("Eski DOPO > Proje DOPO Tahsisler"):** Eski DOPO > Proje DOPO olan parsellere yapılan tahsis alanı.
- **$R_3$ ("DOP Alınmayan Alanlar"):** Düzenlemeye giren ancak DOP alınmayan alanlar.
- **Kurallar:** $\text{DOPO}$ 7 ondalık basamak (`0.1240195`, `0.2015451`) olarak hesaplanır; $\text{DOPO} > 0.45$ ise `IMR-E-3001` (`Madde 18/3 %45 DOP Tavan Aşımı`) fırlatılır ve `OKO / KOP` akışı tetiklenir.
- **Resmî Referans (Proje Sahibi Kararı, `SORU-014` Kapandı):** `Proje-C` için Netcad DOP sayfası referanstır ($R_2 = 12059.06$, DOPO $= 0.1674996$, kamuya ayrılan alan $2426.29$). Dengeleme cetvelindeki parsel toplamı ($12059.10$) ile $0.04\text{ m}^2$ fark **uyarı** olarak raporlanır, parsellere dağıtılmaz (spec §25.1).
- **Doğrulama:** `TC-DOP-001..007` + `saha_proje_1..3` altın veri testleri.

### P3.3 — Hak Eşleştirme (`FR-F1.12` / `D16`), Tahsis Matrisi, İştirak/Hisse Çözümlemesi (`Md. 17/1-d`) ve Çift Parsel ID Eşlemesi (`D31 Kural 3 & 8`)
- **Hak Eşleştirme Kuralı (`FR-F1.12` — P1/P2 kapsamına alınmaz, P3.3'te kodlanır):** DOPO oranında tüm hisselerden kesinti yapılır (`net hak = giren × (1 - DOPO)`); ardından tapu kaydındaki isimlere ve yaklaşık yer krokisine göre `malik -> imar parseli` eşleştirmesi mühendis girdisi olarak yapılır. Yazılım `Σ parsel = parsel alanı`, `Σ malik = net hak` ve `dm²` payda toplamını doğrular ve `Largest Remainder` ile `±1 dm²` farkı dağıtır (`SORU-015` fixture'ına bağlıdır).
- **Görev:** `AllocationMatrixService`, `ShareResolutionService` ve `ParcelIdMappingService` sınıflarını kodla.
- **Kurallar:**
  - Geçici ada/parsel (`902/1/C1`) ile kalıcı tescil ada/parsel (`103/1`) çift yönlü eşlenir.
  - `AAD Yönetmeliği Md. 17/1-(d)` kapsamında muvafakatli paydaşlar müstakil imar parsellerine (`Hisse Çözümlemesi`), diğer paydaşlar hisseli imar parsellerine tahsis edilir.
  - **Revizyon Bağlı Mühendis Onayı (`AC-PILOT-06`):** Her hesap revizyonu benzersiz bir `RevisionId` (`SHA-256`) üretir; geometri veya tahsis değiştiğinde önceki `ApprovalRecord` otomatik olarak düşer.

---

## 6. Faz P4 — Çoklu CAD/CKS I/O (`Imar18.Infrastructure`) ve Masaüstü Stüdyo (`Imar18.Presentation.Avalonia`)

### P4.0 — Netcad `.NCZ` Fizibilite Spike (`D14` — Birincil Öncelik)
- **Bilinen Statik Analiz Bulguları *(kaynak: Claude statik analizi, doğrulanmadı)*:**
  - `.NCZ` kapsayıcı özel ikili formattır (ZIP değildir).
  - İçinde düz metin `<NetmapManager>` XML (39 alan) ve gömülü PNG önizlemesi (`IHDR..IEND`) yer alır.
  - 6 dosyada ortak `~45 KB` zlib bloğu bulunmaktadır (sabit şablon, geometri değil; içeriği çözülmedi).
  - Koordinatlar ham `little-endian double` olarak görünmektedir (kayıt yapısı henüz çözülmedi).
- **Kabul Kriteri:** 3 projenin `.NCZ` dosyalarından katman adları ve parsel köşe koordinatları çıkarılır; hesaplanan `Decimal` Gauss alanı resmî cetvel alanıyla birebir uyuşur. Spike başarısız olursa `P4.1` `.DXF`/`.DWG` yedek adaptörü ile devam edilir (karar proje sahibindedir).

### P4.1 — Birincil `.NCZ` (`NczFormatAdapter`), Yedek `.DXF`/`.DWG` (`ACadSharp`) ve `.CKS` Adaptörleri (`D14`, `D28`, `D31`)
- **Görev:** `ICadFormatAdapter` soyutlaması korunarak **birincil öncelikle** `NczFormatAdapter` ve `CksFileService` (`.CKS`), **yedek (fallback) olarak** ise `ACadSharp` / `netDxf` tabanlı `.dxf` ve `.dwg` adaptörlerini kodla.
- **Standart Katman Eşlemesi:** `TK_PARSEL_GİREN`, `TK_PARSEL_OLUŞAN`, `TK_PARSEL_YOLATERK`, `DÜZENLEME_SINIRI`, `ADA_KENARLT`, `PLAN_ADAKENARI`, `PL_PARK`, `NOKTA_PARSEL_YENI`, `TESCİLLİ_NOKTA`.

### P4.2 — Avalonia 11 + SkiaSharp 2D CAD Tuvali, Proje CRS/Dilim/Ölçek Girdisi (Hardcode Yok, `D3`) ve 5 Adımlı Sihirbaz (`Imar18Studio_UI_UX_Referans_v2_19.html`)
- **Görev:** `docs/UI_UX_Referans_v2_19.html` referans tasarımına birebir sadık kalarak:
  - Sol: Katman Yöneticisi ve 5 Adımlı Sihirbaz (`1. Girdi & Sınır` → `2. DOP & Özet` → `3. Ada Bölümleme` → `4. Tahsis & Hisse` → `5. Cetvel & Çıktı`)
  - Orta: SkiaSharp 2D CAD Tuvali (6 ada geometrisi, `Y/X` canlı koordinat, diklik/cephe rozetleri)
  - Sağ: Geometri & Mevzuat Denetçisi + Canlı `DOP / KOP / Tecviz` KPI kartları
  - Alt: Çift Yönlü Tahsis Matrisi, `dm²` Tescil Hissesi ve Komut Çubuğu (`Ctrl+K`)

---

## 7. Faz P5 — Resmî Cetvel, PDF/Excel/CAD Çıktı Sözleşmeleri (`D31 Kural 5, 6, 9`)

### P5.1 — `ClosedXML` (.xlsx) ve `QuestPDF` (.pdf) Cetvel Üreticileri
- **Üretilecek Zorunlu Resmî Çıktılar:**
  1. `FR.348 (EK-2)` Değişiklik Tasarımı Kapak Sayfası ve İçindekiler
  2. `DOPO / OKO` Özet Formu (`R1, D1, R2, D2, R3`) ve `.CKS` çıktısı
  3. 4'lü Alan ve Koordinat Cetvelleri (`Koordine Özet`, `Noktalı Alan`, `Cepheli Alan`, `Dengeleme Alan`)
  4. `Mal Sahipleri Araştırma ve Özet Formu` (Kadastro parseline göre sıralı)
  5. `Tescile Esas Dağıtım Cetveli` (İmar ada/parseline göre sıralı, `TAM` veya `Pay / (Alan*100)` `dm²` paydalı)
  6. `Fen Klasörü` ve `Tescil Sayfası` (`He. | M2 | Dm2` 3 sütunlu alan formatı)
  7. `FR.521 (EK-17)` Rölöve Ölçü Krokisi ve Dönüşüm Parametreleri Raporu
- **Pilot Filigran Zorunluluğu (`AC-PILOT-07`):** Pilot aşamasında üretilen tüm PDF/XLSX/DXF çıktılarına `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` filigranı ve `RevisionId` özeti basılır.

---

## 8. Faz P6 & P7 — 3 Gerçek Saha Projesiyle Uçtan Uca Doğrulama ve Saha Revizyonu

- **P6.0 (`.NCZ` Anonimleştirme ve Koordinat Fixture Üretim Aracı — `D15`, Henüz Yazılmadı):** `.NCZ` içindeki `<NetmapManager>` XML'inin tüm kişi, kurum, adres ve kayıt no alanları (`_BASVURAN_KISI`, `_BILIRKISI*`, `_HARITA_MUDURU`, `_KAD_TEK*`, `_KURUM_ADRESI`, `_APL_*` vb.) boşaltılır; gömülü PNG önizleme silinir; koordinatlar sabit ofsetle kaydırılır ve çıktı yalnızca `test-data/synthetic/` altına yazılır.

- **P6.1:** `Proje-A (901/1-2)`, `Proje-B (902/1)` ve `Proje-C (903/1)` altın veri setlerinin `Imar18.Pilot.Tests` altında uçtan uca koşturulup doğrulanması (`AC-PILOT-01..10`; **Proje-B pay toplamı referansı `SORU-011`'e, Proje-C 0.04 m² R2/dengeleme farkı referansı `SORU-014`'e bağlıdır**).
- **P7.1:** Harita mühendisi saha geri bildirimlerinin kapatılması ve `docs/POST_PILOT_ROADMAP_v2_19.md` kilidinin açılması.
