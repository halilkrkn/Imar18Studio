# Imar18Studio — Git-Flow (`feat/* → dev → main`) Destekli Claude Code (Üretici) & Google Antigravity (Denetçi) Komut Şablonları

Bu kılavuz; her faz adımında **`dev` dalından ayrı bir `feat/pX-*` dalı açarak** Claude Code Eklentisine (`src/`) kod yazdıran, Google Antigravity IDE'de (`tests/`) tüm testler `%100` yeşil geçince **`dev` dalına birleştiren (`merge --no-ff`)** ve proje/evre bittiğinde **`main` dalına aktaran** tam otomatik komut şablonlarını içerir.

## 0. İlk Kurulumda `main` ve `dev` Dallarını Oluşturma (Tek Seferlik)

ZIP dosyasını klasöre çıkarttığınızda Antigravity alt terminalinde şu komutları bir kez çalıştırarak `main` ve `dev` dallarını kurun:

```
git init
git checkout -b main
powershell -ExecutionPolicy Bypass -File .\scripts\install-official-plugins.ps1
dotnet restore Imar18Studio.sln
dotnet test Imar18Studio.sln
git add .
git commit -m "chore: Imar18Studio P0 kurulumu (main)"
git checkout -b dev

```

Bu kılavuz; **Claude Code IDE Eklentisi** (`Claude Fable 5.2`, `Opus 4.6 -> 5.5`, `Sonnet 4.6 -> 5.5` — [Claude Code VS Code Docs](https://code.claude.com/docs/en/vs-code)) ile üretim kodunu (`src/`) yazdırıp, hemen ardından **Google Antigravity IDE** (`Opus 4.6 -> 5.5`, `Sonnet 4.6 -> 5.5`, `gpt-oss-120b`, `Gemini 3.8-Flash` — [Google Antigravity Rules Docs](https://antigravity.google/docs/rules/)) ile bağımsız test (`tests/`) ve kod denetimi (`/code-review`) yaptırmanız için hazırlanmış **kopyala-yapıştır komut şablonlarını** içerir.

## 1. Her Fazda Tekrarlanan Standart 3 Adımlı Döngü (Evrensel Şablon)

Hangi fazda olursanız olun (`Faz P1..P7` veya `Faz 11..20`), her fazı şu **3 adımlı paslaşmayla** ilerletebilirsiniz:

### Adım A — Claude Code Eklentisine Yapıştırılacak "Üretici (Builder)" Komutu

> **Seçilecek Model:** Güncel model ve effort seçimi için `.claude/rules/model-routing.md` (veya `.agents/rules/model-routing.md`) dosyasına bakın (`D10`).

```
CLAUDE.md, docs/STATUS.md, docs/Imar18Studio_Spesifikasyon_v2_19.md ve docs/PILOT_ROADMAP_v2_19.md dosyalarını oku.
Rolün: ÜRETİCİ AJAN (implementer + architect).
1. docs/STATUS.md içindeki sıradaki tamamlanmamış fazı ([ ]) tespit et, git checkout dev && git checkout -b feat/<faz-kodu> komutuyla o faza özel yeni bir Git dalı aç ve STATUS.md içinde durumu [/] (Üretimde - Claude Code - Dal: feat/<faz-kodu>) olarak işaretle.
2. to-tickets ve implement becerilerini kullanarak yalnızca bu fazın kapsamındaki üretim kodlarını (src/ altında) ve temel TDD birim testlerini yaz.
3. Kesin Kırmızı Çizgiler:
   - Alan, hisse, DOPO ve tecviz hesaplarında yalnızca System.Decimal ve Fraction(BigInteger) kullan; double/float kesinlikle kullanma.
   - NetTopologySuite tiplerini Imar18.GeometryEngine dışına sızdırma.
   - D31 saha kurallarına (DOPO = (R1-D1-R2+D2+R3)/(R1-D1), dm² tescil paydası = Alan*100, Y1 yol dengelemesi) birebir uy.
4. dotnet build Imar18Studio.sln ve dotnet test Imar18Studio.sln komutlarını çalıştırarak derleme ve temel testlerin geçtiğini doğrula.
5. Paket sürümü hatası (NU1101/NU1102/NU1103 vb.) olursa sürümü KENDİN DEĞİŞTİRME; hatanın tam metnini, paket adını, istenen sürümü ve `dotnet package search <paket> --exact-match` çıktısını raporlayıp önerini tek satırda yaz (onayım olmadan Directory.Build.props ve csproj'daki PackageReference sürümlerini değiştirme).
6. İşlem bittiğinde docs/STATUS.md içine "Claude Code Üretimi Tamamlandı -> Antigravity Bağımsız Test ve Denetimi Bekleniyor" notunu düş, değiştirilen dosyaların listesini raporla ve yanıtının en sonuna mutlaka "🛑 FAZ TAMAMLANDI: Token tasarrufu ve temiz bağlam için şimdi mevcut sohbeti kapatıp yeni bir sohbet oturumu açın." uyarısını ekle.


```

### Adım B — Google Antigravity IDE'ye Yapıştırılacak "Bağımsız Test & Denetçi (Verifier)" Komutu

> **Seçilecek Model:** Güncel model ve effort seçimi için `.claude/rules/model-routing.md` (veya `.agents/rules/model-routing.md`) dosyasına bakın (`D10`).

```
GEMINI.md, AGENTS.md, docs/STATUS.md, docs/Imar18Studio_Spesifikasyon_v2_19.md ve docs/PILOT_ROADMAP_v2_19.md dosyalarını oku.
Rolün: BAĞIMSIZ TEST VE DENETÇİ AJAN (test-engineer + reviewer).
Claude Code az önce docs/STATUS.md içinde [/] olarak işaretli fazın src/ üretim kodlarını tamamladı.
1. Üretim kodlarını (src/) değiştirme; yalnızca denetle ve tests/ altındaki test projelerini genişlet.
2. code-review ve imar18-tdd-dotnet becerilerini kullanarak şu 4 denetimi yap:
   - Sınır (boundary), negatif, sıfır, taşma (overflow), eşit kalan (tie-break) ve bozuk girdi testleri eksikse tests/ altına yeni xUnit test metotları ekle.
   - test-data/synthetic/ altındaki 3 gerçek saha projesinin (Proje 1: 901/1-2, Proje 2: 902/1, Proje 3: 903/1) altın verileriyle uyumunu doğrula (beklenen değerler ve tolerans: Proje-B payda referansı SORU-011'e bağlı; Proje-C için referans Netcad DOP sayfasıdır, SORU-014 Kapandı).
   - dotnet test tests/Imar18.Architecture.Tests ile katman ve Decimal/Fraction izolasyonunu doğrula.
   - Tüm çözümü dotnet test Imar18Studio.sln ile koştur ve "0 test çalıştı" gibi sahte yeşil durum olmadığından emin ol.
3. Eğer bir hata, mevzuat sapması veya başa dönmesi gereken bir test bulursan:
   - Üretim kodunu kendin yamama; docs/STATUS.md içine "[ANTIGRAVITY TEST BULGUSU]" başlığıyla hatanın kök nedenini, beklenen/gerçekleşen değeri ve başarısız olan xUnit test adını yaz.
4. Eğer tüm testler ve CleanArchitectureRulesTests %100 yeşil geçerse:
   - Yalnızca Faz P1–P5 alt adımlarında docs/STATUS.md içinde ilgili adımı [x] (Tamamlandı ve Antigravity Tarafından Doğrulandı) olarak işaretle (P6.5, P7.1 ve P7.2 adımları AC-PILOT-10 gereği yalnızca proje sahibi onayıyla [x] yapılır),
   - git add . && git commit -m "test(<faz-kodu>): Antigravity bagimsiz test ve inceleme onayi" komutunu çalıştır,
   - git checkout dev && git merge --no-ff feat/<faz-kodu> komutuyla bu fazın dalını dev dalına birleştir (KESİN KURAL: main dalına merge ETME ve git push YAPMA; main'e merge yalnızca proje sahibi tarafından, P7.2 kapanış onayından sonra yapılır),
   - Paket sürümü hatası (NU1101/NU1102/NU1103 vb.) olursa onayım olmadan Directory.Build.props ve csproj'daki PackageReference sürümlerini değiştirme,
   - Yanıtının en sonuna mutlaka "🛑 FAZ TAMAMLANDI: Token tasarrufu ve temiz bağlam için şimdi mevcut sohbeti kapatıp yeni bir sohbet oturumu açın." uyarısını ekle.


```

### Adım C — (Yalnızca Antigravity Hata Bulursa) Claude Code'a Yapıştırılacak "Kök Neden Düzeltme" Komutu

> **Seçilecek Model:** Güncel model ve effort seçimi için `.claude/rules/model-routing.md` (veya `.agents/rules/model-routing.md`) dosyasına bakın (`D10`).

```
docs/STATUS.md içindeki "[ANTIGRAVITY TEST BULGUSU]" bölümünü ve Antigravity'nin tests/ altına eklediği başarısız xUnit testlerini oku.
systematic-debugging becerisini (4 adımlı kök neden analizi) kullanarak rastgele yuvarlama denemesi yapmadan hatanın gerçek matematiksel/geometrik kök nedenini tespit et.
Yalnızca ilgili src/ kodunu düzelt, dotnet test Imar18Studio.sln komutunun %100 yeşil geçtiğini doğrula ve docs/STATUS.md dosyasını tekrar Antigravity onayı için güncelle.


```

## 2. Fazlara Özel Hazır Başlangıç Komutları (`Faz P1 – Faz P6` ve `Faz 11 – 20`)

Aşağıdaki komutları ilgili faza geldiğinizde doğrudan kopyalayıp yapıştırabilirsiniz:

### Faz P1 — Sayısal Çekirdek (`Fraction`, `Largest Remainder`, `dm²` Payda, `ErrorCatalog`)

* **1. Claude Code (`Sonnet 4.6 -> 5.5` veya `Fable 5.2`):**

```
docs/PILOT_ROADMAP_v2_19.md Faz P1 (P1.1 Fraction, P1.2 LargestRemainderAllocator, P1.3 ErrorCatalog) adımlarını ÜRETİCİ AJAN olarak kodla. Müstakil parselde "TAM", hisseli parselde Alan_m2 * 100 (dm² tam sayı payda) kuralını (D31 Kural 2) ve eşit kalanda deterministik sıralamayı uygula. dotnet test ile doğrula ve docs/STATUS.md'yi Antigravity testi için güncelle.


```

* **2. Google Antigravity (`Sonnet 4.6 -> 5.5` + `gpt-oss-120b`):**

```
Faz P1 için BAĞIMSIZ TEST VE DENETÇİ AJAN olarak çalış. tests/Imar18.Domain.Tests altında test-data/synthetic/pilot-3-shared-parcel/saha_proje_2_hisse_golden.json (902 Ada 1 Parsel, 26629.48 m², 40+ paydaş, 1/24, 1/72, 19/320, 1/176 kesirleri) verilerini kullanarak toplam dağıtılan alanın tam 26629.48 m² ve tüm imar parsellerinde pay toplamının ParselAlanı*100 olduğunu doğrulayan kapsamlı sınır testlerini yaz ve koştur. Tüm testler yeşilse STATUS.md'de Faz P1'i [x] yap.


```

### Faz P2 — Geometri Motoru (`Gauss`, `Y/X` Eksen Takası, `ProjNet` ve 6 Ada `BisectionEngine`)

* **1. Claude Code (`Claude Fable 5.2` — `effort: high`):**

```
docs/PILOT_ROADMAP_v2_19.md Faz P2 (P2.1 Kuantizasyon ve Y/X Eksen Takası, P2.2 Gauss Alan ve Y1 Dengeleme, P2.3 ProjNet ITRF96/ED50 ve 2D Helmert, P2.4 6 Gerçek İmar Adası Geometrisinde BisectionEngine) adımlarını ÜRETİCİ AJAN olarak kodla. Düz dikdörtgen, sırt sırta omurgalı (103/104 Ada), 45° köşe pahlı, yamuk, üçgen ve konkav L-ada tiplerinde 75°-105° diklik ve 1 mm (0.001 m) kuantizasyon kurallarını uygula.


```

* **2. Google Antigravity (`Opus 4.6 -> 5.5` — `effort: high`):**

```
Faz P2 için BAĞIMSIZ TEST VE DENETÇİ AJAN olarak çalış. tests/Imar18.GeometryEngine.Tests altında 6 ada geometrisinin tamamı için alan korunumu, 1 mm kuantizasyon sonrası topoloji geçerliliği (IsValid), konkav adada MultiPolygon bölünme tespiti ve NetTopologySuite tiplerinin dışarı sızmadığını (CleanArchitectureRulesTests) denetleyen testleri yaz ve koştur.


```

### Faz P3 — İmar 18. Madde Kuralları (`TecvizChecker`, `DopCalculator`, `Md. 17/1-d` Tahsis)

* **1. Claude Code (`Claude Fable 5.2` — `effort: high`):**

```
docs/PILOT_ROADMAP_v2_19.md Faz P3 (P3.1 TecvizChecker, P3.2 DopCalculator R1..R3/D1..D2 formülü, P3.3 Tahsis Matrisi, Md. 17/1-d Hisse Çözümlemesi, Geçici/Kalıcı Parsel ID eşlemesi ve SHA-256 RevisionId mühendis onayı) adımlarını ÜRETİCİ AJAN olarak kodla.


```

* **2. Google Antigravity (`Sonnet 4.6 -> 5.5`):**

```
Faz P3 için BAĞIMSIZ TEST VE DENETÇİ AJAN olarak çalış. Proje 1 (DOPO=0.1240195, Y1=+0.12 m², Tecviz sınırı=21.02 m² (mühendis girdisi)), Proje 2 (DOPO=0.2015451, Y1=+0.20 m², Tecviz sınırı=75.07 m² (mühendis girdisi)) ve Proje 3 (Y1=+1.97 m², PARK=400.00 m², Tecviz sınırı=53.82 m² (mühendis girdisi)) altın veri setleriyle DOPO, Tecviz ve Y1 yol dengelemesinin beklenen değerlerle çalıştığını (Proje 3'te R2 referansı Netcad DOP sayfasıdır: 12059.06; girilen tecviz sınırı TecvizChecker ile denetlenir) ve girdi değiştiğinde RevisionId onayının otomatik düştüğünü (AC-PILOT-06) test et.


```

### Faz P4 & P5 — Çoklu CAD (`.dxf/.dwg/.ncz/.cks`), Avalonia 11 + SkiaSharp UI ve Resmî Cetveller

* **1. Claude Code (`Claude Fable 5.2` UI için + `Sonnet 4.6 -> 5.5` CAD/Cetvel için):**

```
docs/PILOT_ROADMAP_v2_19.md Faz P4 (DXF, DWG, Netcad 8.5 NCZ Windows-1254, CKS adaptörleri ve docs/UI_UX_Referans_v2_19.html tasarımına birebir uygun Avalonia 11 + SkiaSharp 2D CAD Stüdyosu) ve Faz P5 (FR.348 Kapak, 4'lü Alan Cetveli, İki Yönlü Dağıtım Cetveli, He/M2/Dm2 Fen Klasörü, FR.521 Rölöve Krokisi ve Pilot Filigranı) adımlarını ÜRETİCİ AJAN olarak kodla.


```

* **2. Google Antigravity (`Gemini 3.8-Flash` UI kontrolü + `Sonnet 4.6 -> 5.5` Cetvel/CAD testi):**

```
Faz P4 ve P5 için BAĞIMSIZ TEST VE DENETÇİ AJAN olarak çalış. DOP_Ornek.CKS ve örnek DXF/DWG/NCZ katmanlarının (TK_PARSEL_GİREN, TK_PARSEL_OLUŞAN, TK_PARSEL_YOLATERK, PL_PARK) doğru okunduğunu, üretilen Excel/PDF cetvellerde "PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ" filigranının ve He/M2/Dm2 sütunlarının eksiksiz yer aldığını ve Presentation katmanının Infrastructure katmanına doğrudan erişmediğini doğrula.


```

### Faz 11–20 — Pilot Sonrası Kurumsal Sürüm (`SQLCipher .imr18`, `Ed25519`, OCR ve Doğal Dil Asistanı)

* **1. Claude Code (`Opus 4.6 -> 5.5` Kriptografi için + `Fable 5.2` Faz 18 Guardrail için):**

```
docs/STATUS.md üzerinde Pilot (Faz P0-P7) tamamlandı. Şimdi docs/POST_PILOT_ROADMAP_v2_19.md içindeki sıradaki fazı (Faz 11 SQLCipher/Argon2id/DPAPI -> Faz 12 Ed25519 Audit -> Faz 13 .imr18.lock -> Faz 14-20) ÜRETİCİ AJAN olarak TDD ile kodla. Faz 18'de doğal dil asistanının asla matematiksel hesap yapmamasını sağlayan GuardrailService kurallarını (AC-AI-01..10) zorunlu tut.


```

* **2. Google Antigravity (`Opus 4.6 -> 5.5` + `gpt-oss-120b`):**

```
docs/POST_PILOT_ROADMAP_v2_19.md içindeki aktif kurumsal faz için BAĞIMSIZ TEST VE DENETÇİ AJAN olarak çalış. Şifreli .imr18 bozuk dosya/yanlış parola reddi, Ed25519 zincir kırılması tespiti, reddedilen işlemlerin audit kaydı ve GuardrailService güvenlik sınırları için saldırı/sınır testlerini (adversarial tests) yaz, koştur ve doğrula.


```