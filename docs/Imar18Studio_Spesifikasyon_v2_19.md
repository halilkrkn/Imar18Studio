# İMAR KANUNU 18. MADDE OTOMASYON STÜDYOSU (Imar18Studio)

**Kurumsal Yazılım Mimarisi, Fonksiyonel Gereksinimler (FR) Kataloğu, RTM, Kabul Kriterleri, Hata Yönetimi, TDD Geliştirme Spesifikasyonu, İki Evreli (Pilot P0–P7 & Pilot Sonrası Faz 11–20) Yol Haritası ve Çift Uyumlu (Claude Code + Google Antigravity) Ajan/Skill/Plugin Mimarisi**

| Alan | Değer |
|---|---|
| Sürüm | 2.19 (Birleştirilmiş Nihai Spesifikasyon + D27–D30 Kararları + Pilot P0–P7 & Faz 11–20 + Claude Code / Antigravity Ortak Skill & Plugin Mimarisi) |
| Tarih | 11 Kasım 2026 (v2.18 temel: 9 Kasım 2026) |
| Hedef Platform | Windows 10 / Windows 11 Masaüstü |
| Çalışma Modeli | Yerel / Offline-First; KVKK gerekliliklerini destekleyecek şekilde tasarlanan (uygunluğu ayrıca değerlendirilecek) |
| Depo ve Veri Politikası | Public GitHub (Yalnızca sentetik/anonim test verisi; gerçek tapu/malik/koordinat/sır yüklenemez) |
| Mimari Yaklaşım | Clean Architecture + DDD + CQRS / Command Pattern (`CommandBus`) |
| Geliştirme Metodolojisi | Katmanlı Katı TDD (Red-Green-Refactor) + Bağımsız Referans Doğrulaması |
| **Ajan Yürütücüsü** | **Google Antigravity (Gemini + Claude modelleri) ve Claude Code (eklenti/CLI) tam uyumlu hibrit yapı (`AGENTS.md`, `.claude/` ve `.agents/` ortak standardı)** |
| Ürün Sınıfı | Yetkili harita mühendisi destek yazılımı; akıllı taslak ve cetvel üretici |

> **Sürüm 2.19 Birleştirme Notu:** Bu doküman, nihai **v2.19 Spesifikasyonunun** tüm çekirdek mimari, matematiksel formül (§3, §4.1, §4.3, §4.4), RTM (§15–§18) ve **Çoklu CAD (`.dxf`, `.dwg`, `.ncz` — D28)** yapısını birebir koruyarak; **Pilot Sürüm Planı (Faz P0–P7)**, **Pilot Sonrası Yol Haritası (Faz 11–20)** ve **Claude Code + Google Antigravity Çift Uyumlu Klasör, Skill, Subagent ve Plugin Yol Haritası (§24)** bölümlerini tek bir ana üretim sözleşmesinde birleştirir.

## 0. v2.18 ve v2.19 Değişiklik Özeti

Bu sürüm, v2.17'de belirlenen 10 yapısal eksikliği, v2.18 dönüşümündeki 3 teknik engeli, çoklu CAD formatı ihtiyacını, iki evreli (Pilot / Pilot Sonrası) geçiş mimarisini ve çoklu ajan (Claude Code + Google Antigravity) skill/plugin orkestrasyonunu kapatır:

1. **Hata taksonomisi ve hata kodları:** ERR-* standart hata kimlikleri, önem seviyesi, kullanıcı mesajı ve düzeltme önerisi.
2. **Çok kullanıcılı senaryo:** Aynı `.imr18` dosyasının ikinci kez açılması, dosya kilidi, çakışma çözümü.
3. **OCR model lisansı:** `tur.traineddata` sürümü, lisans, checksum ve güncelleme politikası.
4. **Erişilebilirlik test planı:** WCAG 2.2 AA hedefi, klavye kısayolları, ekran okuyucu ve kontrast testi.
5. **CI/CD sözleşmesi:** Public GitHub uyumlu ücretsiz CI, build, test, mimari denetim, güvenlik taraması, sürüm kapısı ve imzalama.
6. **Telemetri gizlilik sözleşmesi:** Opt-in, toplanan alanlar, saklama süresi, anonimleştirme ve tek tıkla kapatma.
7. **Kullanıcı onayı UI akışı:** Mühendis onayı ekranı, onay-revizyon bağı (veri değişirse onayın düşmesi), onay metni, geri alma ve audit log.
8. **Sürüm geri alma:** MevzuatProfili değişince eski proje dosyasının açılması ve salt-okunur mod.
9. **PARSİD hata haritası:** Şema doğrulama hatası → kullanıcı mesajı → düzeltme önerisi eşlemesi.
10. **Fen Klasörü imza zinciri:** Ed25519 açık anahtar, mühendis onayı ve zaman damgası sırası.
11. **v2.19 Matematiksel ve RTM Mutabakatı:** Eksik 6 jeodezik/mevzuat formülünün tanımlanması, §9.2 CI güvenlik filtresinin tamamlanması ve §15–§18 RTM/FR izlenebilirlik tablolarının eşitlenmesi.
12. **Çoklu CAD Formatı Desteği (DXF, DWG, NCZ — D28 / FR-F2.9):** Yalnızca `.dxf` yerine `ICadFormatAdapter` soyutlamasıyla **AutoCAD `.dwg`** ve **Netcad `.ncz`** dosyalarının okunması ve dışa aktarılması.
13. **İki Evreli Yol Haritası (Pilot P0–P7 ve Pilot Sonrası Faz 11–20 — D29):** Çekirdek motorun önce Pilot aşamasında doğrulanması, saha eksikleri giderildikten sonra Faz 11–20 sırasıyla kurumsal güvenlik, OCR, erişilebilirlik ve ürün içi LLM (`GuardrailService`) modüllerinin devreye alınması.
14. **Claude Code + Google Antigravity Çift Uyumlu Klasör, Skill ve Plugin Mimarisi (D30 — §24):** `.claude/` ve `.agents/` dizinlerinin açık `SKILL.md` standardıyla senkronize edilmesi, 11 özel proje becerisi (`imar18-*` ve faz yönetimi skill'leri), 5 alt ajan profili, faz bazlı plugin/MCP kurulum sırası ve gerçek saha projelerinden anonim/sentetik test verisi (`test-data/synthetic/`) üretme protokolü.

## 1. Yönetici Özeti

Imar18Studio; 3194 sayılı İmar Kanunu'nun 18. maddesi kapsamındaki arazi ve arsa düzenlemelerinde, **CAD haritalarını (`.dxf`, `.dwg`, `.ncz`)**, tapu kayıtlarını (TAKBİS/WebTapu), yaklaşık yer krokilerini ve imar planı verilerini okuyarak yetkili harita mühendisine destek olan masaüstü uygulamasıdır.

### 1.1. Temel İşlevler

1. Tapu, kroki ve CAD sınırları (`.dxf`, `.dwg`, `.ncz`) arasında malik ve alan tutarlılığı denetimi.
2. Arşiv onaylı kümülatif tablo ve geçici koordinat profili (hata çemberi) yöntemleriyle yanılma sınırı kontrolü.
3. Kapanan yollar, terk, bağış, tahsis ve kamu mülkiyeti istisnalarının parsel bazında ayrık hesabı.
4. Toplam DOP kesintisinin düzenleme öncesi alanların %45'i ile sınırlandırılması ve aşım karşılama protokolü.
5. Cephe, derinlik ve asgari alan kriterlerine göre iki döngülü bisection bölme motoru.
6. Tam rasyonel (Fraction) hisse yönetimi ve Largest Remainder deterministik yuvarlama dağıtımı (eşit kalanlarda kararlı bağlayıcı sıra ile).
7. SkiaSharp tabanlı 2D çizim ekranı ve Türkçe kural tabanlı komut çubuğu (Pilot sonrasında opsiyonel `GuardrailService` korumalı doğal dil/LLM asistanı).
8. Katmanlı CAD çıktıları (`.dxf`, `.dwg`, `.ncz`), canlı formüllü Excel cetvelleri, Fen Klasörü PDF seti ve PARSİD veri paketi.

### 1.2. Hukuki, Operasyonel ve Veri Güvenliği Sınırı

- Yazılım, "akıllı taslak ve cetvel üretici mühendis asistanı" olarak konumlandırılır. Üretilen çıktılar, yetkili harita mühendisi tarafından kontrol edilip onaylanmadan resmî belge niteliği taşımaz. Uygulama içi onay yasal e-imza yerine geçmez.
- Tüm kişisel veriler kullanıcının yerel bilgisayarında, üretim sürümünde SQLCipher ile şifreli `.imr18` tek dosya konteynerinde tutulur.
- **Pilot Evresi Veri Sınırı:** Tam kriptografik anahtar sarmalama (SQLCipher + Argon2id + DPAPI) Faz 11'de devreye alınacağından, ilk geliştirme/CI pilot testleri yalnızca **sentetik veya anonimleştirilmiş verilerle** yürütülür.
- **Gerçek Saha Projelerini Anonimleştirme Kuralı (`test-data/synthetic/`):** Harita mühendisinden alınan gerçek uygulama projeleri depoya doğrudan konulmaz. Önce kişisel veriler (Malik Adı, Baba Adı, TC Kimlik No, Cilt/Sayfa/Kütük bilgileri) maskelenerek ve koordinatlar yerel/sentetik ofsetle ötelenerek `test-data/synthetic/pilot-1..4/` altına **Altın Veri Seti (Golden Dataset)** olarak dönüştürülür; böylece gerçek matematiksel/geometrik zorluklar %100 korunurken KVKK ve Public Repo güvenliği (`AC-PILOT-08`) tam sağlanır.
- **Yerel Gerçek Veri Politikası (`test-data/real/` — Proje Sahibi Kararı, 15 Kasım 2026):** Harita mühendisinden alınan gerçek cetvel ve `.NCZ` dosyaları yalnızca geliştiricinin bilgisayarında `test-data/real/` altında tutulur. Bu klasör `.gitignore` içindedir; `scripts/check-no-real-data.ps1` ve pre-commit hook'u depoya girmesini engeller. Kodlama ajanı (Claude Code / Antigravity) bu klasörü **yalnızca yerel doğrulama ve test için okuyabilir**; içindeki malik adı, TC, tapu ve koordinat değerlerini public dosyalara (kod, test, JSON, doküman, commit mesajı) KOPYALAMAZ. Gerçek veriye bağlı testler `[Trait("Category", "RealData")]` taşır, klasör yoksa atlanır ve CI'da `Category!=RealData` filtresiyle dışlanır.

## 2. Kesinleşen Teknoloji Yığını

| Katman | Teknoloji / Kütüphane | Mimari Rol |
|---|---|---|
| Çekirdek Dil & Runtime | C# 14 / .NET 10 LTS | Tip güvenliği, decimal hassasiyeti, performans |
| Masaüstü UI | Avalonia UI 11 | XAML/MVVM tabanlı masaüstü arayüzü |
| Tasarım Sistemi | Semi.Avalonia | Koyu tema, DataGrid, sihirbaz bileşenleri |
| MVVM | CommunityToolkit.Mvvm | Source Generator tabanlı ObservableProperty, RelayCommand |
| CAD Çizim Motoru | SkiaSharp | 2D grafik motoru, GPU hızlandırmalı CAD tuvali (60 FPS pan/zoom hedefi) |
| Mekânsal İndeks | NetTopologySuite + STRtree | Mekânsal indeksleme, topoloji, snap ve kesişim |
| Koordinat Dönüşümü | ProjNet + Metadata Adapter | ITRF96 TM 3° dilimler, ED50 parametrik dönüşüm |
| CAD Okuma / Yazma (DXF, DWG, NCZ) | `ACadSharp` (DWG/DXF birincil), `netDxf` (DXF yedek), `NczFormatAdapter` (Netcad `.ncz` ikili okuyucu + köprü adaptörü) | `ICadFormatAdapter` üzerinden katmanlı `.dxf`, `.dwg` ve `.ncz` I/O |
| PDF & OCR | UglyToad.PdfPig + Tesseract | Vektör PDF okuma (Pilot), yerel OCR worker (Faz 16) |
| Resmî Evrak Çıktısı | QuestPDF | TKGM standartlarında Fen Klasörü PDF seti |
| Excel Üretimi | ClosedXML | Canlı formüllü cetveller |
| Şifreli Veritabanı | SQLite + SQLCipher (AES-256) | Yerel `.imr18` tek dosya konteyneri (Pilot: yerel atomik + checksum; Faz 11: tam SQLCipher/Argon2id) |
| Kriptografi & İmza | NSec (Ed25519), Windows DPAPI | Denetim kaydı (Audit Log) bütünlük zinciri (Faz 12) |
| Donanım Güvenliği | TPM 2.0 + ECDSA P-256 | Donanım destekli lisanslama profili (Pilot sonrası ayrı karar) |
| Test Altyapısı | xUnit, FluentAssertions, NetArchTest.Rules | Katı TDD, mimari kurallar ve Pilot senaryo testleri |

### 2.1. Çoklu CAD Formatı Mimarisi (`ICadFormatAdapter` — Birincil: Netcad `.NCZ`, Yedek: `.DXF` / `.DWG` — D14)

Tüm CAD dosya işlemleri `Imar18.Application` katmanındaki format-bağımsız `ICadFormatAdapter` sözleşmesi üzerinden yürütülür. **Mühendis kararı (`D14`) uyarınca öncelik sırası: 1. Netcad `.NCZ` (Birincil), 2. `.DXF` / `.DWG` (Yedek — `.NCZ` çalışmazsa):**

1. **Birincil — Netcad `.NCZ` Motoru (`NczFormatAdapter` ve `P4.0` Fizibilite Spike):**
   - **Bilinen Teknik Bulgular *(kaynak: Claude statik analizi, doğrulanmadı)*:** `.NCZ` dosyası standart bir ZIP arşivi değil, özel kapsayıcı ikili (`binary`) formattır; içinde düz metin `<NetmapManager>` XML bloğu (39 alan), gömülü PNG önizlemesi (`IHDR..IEND`), 6 saha dosyasında ortak `~45 KB` zlib sıkıştırmalı blok (sabit şablon, geometri değil; içeriği henüz çözülmedi) bulunur ve koordinatlar ham `little-endian double` olarak görünmektedir (kayıt yapısı henüz çözülmedi).
   - **P4.0 Fizibilite Spike Kabulü:** 3 saha projesinin `.NCZ` dosyalarından katman adları ve parsel köşe koordinatları çıkarılır; `Decimal` Gauss alanı resmî cetvel alanıyla birebir uyuşur. Spike başarısız olursa proje sahibi kararıyla `P4.1` `.DXF` / `.DWG` yedek yoluyla devam edilir.
2. **İkincil (Yedek) — AutoCAD `.DXF` ve `.DWG` Motoru (`ACadSharpCadAdapter` & `NetDxfFallbackAdapter`):**
   - `.NCZ` ayrıştırmasının desteklenmediği durumlarda veya AutoCAD kaynaklı projelerde `ACadSharp` (`DxfReader`/`DwgReader`) ile `.dxf` ve `.dwg` dosyaları ortak `CadLayerDocument` modeline dönüştürülür.

### 2.2. Kriptografi ve Anahtar Yaşam Döngüsü

- **Veri Şifreleme (Faz 11):** Rastgele üretilen Data Encryption Key (DEK), kullanıcı parolasından Argon2id ile türetilen Key Encryption Key (KEK) ile sarılır.
- **Audit Log İmzası (Faz 12):** Her işlem kaydı SHA-256 hash zinciri ve Ed25519 anahtarı ile imzalanır. Özel anahtar `%LOCALAPPDATA%\Imar18Studio\keys\` altında DPAPI CurrentUser ile korunur.
- **İşlem Atomikliği ve Reddedilen İşlem Kaydı:** Veri mutasyonu ve başarılı işlem audit kaydı aynı SQLite transaction'ında commit edilir. **Reddedilen işlemler ve onaysız çıktı girişimleri de ayrı denetim izi olarak kaydedilir;** PDF checksum eksikliği genel audit yazımını engellemez, yalnızca "başarılı nihai çıktı" olayının tamamlanmasını engeller.

## 3. Katmanlı Mimari ve Taviz Verilmeyecek Kurallar

### 3.1. Çift Uyumlu (Claude Code + Google Antigravity) Depo ve Çözüm Şeması

Aşağıdaki dizin yapısı hem **Claude Code** (`.claude/`, `CLAUDE.md`) hem de **Google Antigravity** (`.agents/`, `AGENTS.md`, `GEMINI.md`) tarafından sıfır uyumsuzlukla okunacak şekilde tasarlanmıştır:

```text
Imar18Studio/
├── AGENTS.md                        # Ortak anayasa (Antigravity + Claude Code ortak referansı)
├── CLAUDE.md                        # Claude Code oturum başlangıç talimatı
├── GEMINI.md                        # Google Antigravity oturum başlangıç talimatı
├── .gitignore                       # settings.local.json, gerçek saha verileri ve derleme çıktıları hariç
├── README.md
├── Imar18Studio.sln
│
├── docs/
│   ├── STATUS.md                    # Aktif faz kilidi, ajan dosya kilit tablosu, saha bulguları ve açık sorular
│   ├── Imar18Studio_Spesifikasyon_v2_19.md
│   ├── pilot-roadmap.md             # Faz P0–P7 özet kılavuzu
│   ├── post-pilot-roadmap.md        # Faz 11–20 özet kılavuzu
│   └── model-routing.md             # Görev bazlı model ve effort yönlendirme tablosu
│
├── .claude/                         # Claude Code yapılandırması (.agents/ ile senkronize)
│   ├── settings.json                # Güvenli komut izinleri (build/test izinli; push/publish/web yasaklı)
│   ├── settings.local.json          # Kişisel yerel ayarlar (gitignore'da, repoya girmez)
│   ├── rules/                       # Kalıcı modüler kurallar (.agents/rules/ ile ortak)
│   │   ├── architecture.md
│   │   ├── testing.md
│   │   ├── security.md
│   │   └── model-routing.md
│   ├── agents/                      # Özel alt ajan (subagent) profilleri
│   │   ├── architect.md
│   │   ├── implementer.md
│   │   ├── reviewer.md
│   │   ├── test-engineer.md
│   │   └── docs-writer.md
│   ├── skills/                      # Açık SKILL.md standardı (.agents/skills/ ile birebir uyumlu)
│   │   ├── imar18-tdd-dotnet/SKILL.md
│   │   ├── imar18-domain-rules/SKILL.md
│   │   ├── imar18-architecture/SKILL.md
│   │   ├── imar18-mevzuat-profil/SKILL.md
│   │   ├── imar18-cad-topology/SKILL.md
│   │   ├── imar18-output-contracts/SKILL.md
│   │   ├── imar18-security-kvkk/SKILL.md
│   │   ├── start-pilot-phase/SKILL.md
│   │   ├── run-pilot-tests/SKILL.md
│   │   ├── review-pull-request/SKILL.md
│   │   └── close-pilot-phase/SKILL.md
│   └── commands/                    # Hızlı slash komutları (/pilot-status, /pilot-next)
│       ├── pilot-status.md
│       └── pilot-next.md
│
├── .agents/                         # Google Antigravity yerel keşif dizini (.claude/ ile senkron/junction)
│   ├── rules/                       # .claude/rules/*.md ile aynı dosyalar (YAML frontmatter trigger'lı)
│   └── skills/                      # .claude/skills/*/SKILL.md ile aynı beceriler
│
├── .github/
│   └── workflows/
│       └── ci.yml                   # Public GitHub ücretsiz Windows CI iş akışı
│
├── src/
│   ├── Imar18.Domain/
│   ├── Imar18.GeometryEngine/
│   ├── Imar18.Application/
│   ├── Imar18.Infrastructure/
│   ├── Imar18.Presentation.Avalonia/
│   └── Imar18.Bootstrapper/
│
├── tests/
│   ├── Imar18.Domain.Tests/
│   ├── Imar18.GeometryEngine.Tests/
│   ├── Imar18.Application.Tests/
│   ├── Imar18.Infrastructure.Tests/
│   ├── Imar18.Architecture.Tests/
│   └── Imar18.Pilot.Tests/
│
└── test-data/
    └── synthetic/                   # Yalnızca anonimleştirilmiş/sentetik altın veri seti (AC-PILOT-08)
        ├── README.md
        ├── pilot-1-concave-parcel/  # Sentetik .dxf, .dwg, .ncz konkav ada verisi
        ├── pilot-2-mixed-ownership/ # Kapanan yol, terk, kamu ve %45 DOP aşımı verisi
        ├── pilot-3-shared-parcel/   # Yüksek paydalı müşterek hisse ve TAKBİS eşleşme verisi
        └── pilot-4-output-contract/ # Şema, revizyon, onay ve checksum doğrulama verisi
```

### 3.2. Temel Mimari İlkeler

1. **Tip İzolasyonu ve Yuvarlama Sözleşmesi:** Tüm mülkiyet ve hak hesapları `System.Decimal` ve `Fraction` ile yürütülür. `double` yalnızca geometri motoru ve render matrislerinde kullanılır. Geometrik alanın (`double`) hak hesabına (`Decimal`) aktarımında kapıda 2 ondalık basamağa (`MidpointRounding.AwayFromZero`) yuvarlama sözleşmesi uygulanır. Kuantizasyon sonrası alan, `Decimal` Gauss alanı ile yeniden doğrulanır; topoloji bozulursa çözüm geri alınır.
2. **DTO ve Domain İzolasyonu:** `Imar18.Domain`; UI, dosya sistemi ve veritabanı uygulamalarına bağımlı olmaz. `NetTopologySuite.Geometries` tipleri Application sözleşmelerine veya dış DTO'lara sızamaz.
3. **Eksen Sırası Kontrolü:** Türkiye jeodezik sisteminde $X_{\text{jeo}}$ (Yukarı / Kuzey - Northing) ve $Y_{\text{jeo}}$ (Sağa / Doğu - Easting) eksenleri, NTS Kartezyen dönüşüm kapısında (`CoordinateAxisSwapAdapter`) terslenerek eşlenir:
   $$x_{\text{NTS}} = Y_{\text{jeo}} \text{ (Sağa / Doğu)}, \qquad y_{\text{NTS}} = X_{\text{jeo}} \text{ (Yukarı / Kuzey)}$$
   **Format Bazlı Eksen Kuralı:** AutoCAD (`.dxf`, `.dwg`) dosyalarından gelen Kartezyen $(X_{\text{CAD}} = \text{Sağa},\, Y_{\text{CAD}} = \text{Yukarı})$ koordinatlar ile Netcad (`.ncz`) dosyalarından gelen jeodezik $(Y = \text{Sağa},\, X = \text{Yukarı})$ koordinatlar kendi adaptörlerinde normalize edilerek iç modele tek tip $(X_{\text{jeo}} = \text{Kuzey},\, Y_{\text{jeo}} = \text{Doğu})$ olarak aktarılır. Dışa aktarımda hedef formatın eksen standardı korunur.
4. **Fraction & Deterministik Dağıtım:** Hisseler EBOB sadeleştirmeli `Fraction(BigInteger Pay, BigInteger Payda)` ile yönetilir. Yuvarlama farkları Largest Remainder yöntemiyle deterministik kurallarla dağıtılır; eşit kalanlı hisselerde kararlı bağlayıcı sıra (tie-breaker: büyük pay → küçük malik/hisse kimliği) kullanılır.
5. **CommandBus ve Revizyon-Onay Bağı:** Tüm veri mutasyon komutları `CommandBus` üzerinden yürütülür (salt okunur sorgular ve UI gezinmesi mutasyon sayılmaz). Mühendis onayı hesaplanan veri revizyonuna (`RevisionId` / `StateHash`) bağlanır; girdi, geometri veya parametre değişirse mevcut onay otomatik olarak geçersizleşir (`AC-PILOT-06`).
6. **UI Duyarlılığı:** 50 ms üzerindeki her işlem `IProgress<T>` ve iptal jetonu (`CancellationToken`) ile arka plan iş parçacığında çalıştırılır.

## 4. Çekirdek Matematiksel ve Geometrik Algoritmalar

### 4.1. Gauss Alanı Hesabı

Kapalı bir poligonun $n$ adet jeodezik köşe koordinatı $(X_i, Y_i)$ için kayan nokta hassasiyet kaybını (catastrophic cancellation) önlemek amacıyla, önce tüm koordinatlar ilk köşeye $(X_1, Y_1)$ ötelenerek yerel orijine taşınır:

$$X'_i = X_i - X_1, \qquad Y'_i = Y_i - Y_1 \quad (i = 1, 2, \dots, n)$$

Ardından Gauss (Shoelace) alan bağıntısı uygulanır ($X'_{n+1} = X'_1$, $Y'_{n+1} = Y'_1$, $X'_0 = X'_n$, $Y'_0 = Y'_n$):

$$A = \frac{1}{2} \left| \sum_{i=1}^{n} X'_i \left( Y'_{i+1} - Y'_{i-1} \right) \right| = \frac{1}{2} \left| \sum_{i=1}^{n} \left( X'_i Y'_{i+1} - X'_{i+1} Y'_i \right) \right|$$

CAD dosyalarından (`.dxf`, `.dwg`, `.ncz`) gelen 3 boyutlu ($Z \neq 0$ kotlu) veriler, alan ve topoloji işlemlerinden önce **$XY$ yatay izdüşüm ($Z = 0$ TM projeksiyon)** düzlemine indirgenir.

### 4.2. İki Döngülü Bisection ve Kuantizasyon

- **Dış Döngü:** Cepheye diklik açısı 75° ile 105° arasında taranır.
- **İç Döngü:** Paralel kesim çizgisi ada omurgası boyunca kaydırılarak hedef alana hassasiyetle yakınsar.
- **Kuantizasyon:** Köşe koordinatları ızgaraya yuvarlanır. Paylaşılan sınırlar canonical vertex kimliğiyle ortak yuvarlanır. Kuantizasyon sonrası NTS `IsValid`, `Decimal` Gauss alanı ve `postQuantizationAreaToleranceM2` doğrulanır; topoloji bozulursa çözüm geri alınır. Konkav adalarda çok parçalı (MultiPolygon) kesimler tespit edilirse bisection durdurulup mühendis müdahalesi istenir.

### 4.3. Yanılma Sınırı (Tecviz) Modelleri ve `TecvizGirdisi` (Mühendis Girdisi Esası — D1)

> **Dayanak Mevzuat:** *"Tapu Planlarında Yanılma Sınırının Belirlenmesi Hakkında Yönetmelik"* (Resmî Gazete: **27.09.2022, Sayı: 31966** — [Lexpera Mevzuat Metni](https://www.lexpera.com.tr/resmi-gazete/metin/tapu-planlarinda-yanilma-sinirinin-belirlenmesi-hakkinda-yonetmelik-31966)). Yönetmelik uyarınca yüzölçümü yanılma sınırı; parselin orijinal üretim yöntemine, pafta ölçeğine ($1/M$) ve koordinat kalitesine (`Arşiv Onaylı` / `Geçici Koordinatlı`) bağlıdır.
> **Bağlayıcı Mühendis Kararı (`D1`):** Tecviz sınırı kadastro parseli (veya düzenleme toplamı — bkz. `SORU-013`, `SORU-016`) başına **MÜHENDİS GİRDİSİDİR**. Pafta ölçeği ve üretim yöntemi her projede değiştiği ve sınır kadastro (TKGM / Netcad dengeleme raporu) tarafından verildiği için sabit formül kodlanmaz; mühendis uygulama üzerinden elle girip düzeltebilir.

#### 4.3.1. Birincil Model: `TecvizGirdisi` ve `TecvizChecker` (`FR-F1.4` / `TC-TECVIZ-001`)
```csharp
public enum KoordinatKalitesi { ArsivOnayli, Gecici }

public sealed record TecvizGirdisi(
    int OlcekPaydaM,                  // Örn. 1000 (1/1000 ölçek — projeden/parselden girilir)
    string UretimYontemi,             // Örn. "Sayısal Fotogrametrik / Prizmatik / Grafikten Sayısallaştırma"
    KoordinatKalitesi KoordinatKalite,// ArsivOnayli | Gecici
    decimal TecvizSiniriM2,           // Mühendis tarafından girilen tecviz sınırı (m², Decimal)
    string KaynakNotu                 // Örn. "TKGM Fen Kayıt / Netcad Dengeleme Raporu"
);
```
- **`TecvizChecker` Davranışı (`FR-F1.4` / `TC-TECVIZ-001`):** `TecvizChecker` sabit bir formül hesaplamaz; yalnızca `|GaussAlanı - TapuAlanı| <= TecvizGirdisi.TecvizSiniriM2` (`System.Decimal`) kontrolünü yapar.
- **Çok Parselli Düzenlemede Çift Kontrol Kuralı (Proje Sahibi Kararı, `SORU-013`):** Düzenlemede tek kadastro parseli varsa kontrol tektir (parsel = toplam) ve yalnızca tek sonuç gösterilir. Birden fazla kadastro parseli varsa `TecvizChecker` iki sonuç hesaplar: **parsel bazlı** (her kadastro parseli için kendi `TecvizSiniriM2` değeri) ve **toplam bazlı** (düzenleme alan toplamı için girilen `DuzenlemeToplamTecvizSiniriM2`; Netcad dengeleme cetvelindeki TECVİZ satırı). İki sonuç aynıysa tek mesaj gösterilir; farklıysa iki sonuç yan yana gösterilir ve hangisinin bağlayıcı olduğu mühendis teyidine bırakılır. Hata seviyesi `ErrorCatalog`'daki `ERR-TECVIZ-*` tanımına göre belirlenir; yeni bir geçersiz kılma mekanizması eklenmez. Toplam sınır da mühendis girdisidir, formülle hesaplanmaz.

#### 4.3.2. Arşiv Onaylı Koordinat Profili (TPYS Md. 8(2) — YARDIMCI HESAPLAYICI, VARSAYILAN DEĞİL)
> **Uyarı:** Bu kümülatif tablo **yalnızca ARŞİV ONAYLI geometri için yardımcı hesaplayıcıdır (varsayılan değildir)**; aşağıdaki tablo sayıları **yönetmelik görselinden DOĞRULANMADI (TASLAK)**. Mühendis açıkça seçmedikçe otomatik uygulanmaz; asıl kontrol `TecvizGirdisi.TecvizSiniriM2` üzerinden yürür.

Parsel alanı $A$ ($\text{m}^2$) için kademe aralıkları $[l_k, u_k]$ ve marjinal oranlar $r_k$ olmak üzere yardımcı kümülatif kademe fonksiyonu *(TASLAK — yönetmelik görselinden doğrulanmadı)*:

$$\Delta A_{\max}(A) = \sum_{k=1}^{7} r_k \cdot \max\bigl(0,\, \min(A, u_k) - l_k\bigr)$$

| Alan Aralığı ($[l_k, u_k]$) | Marjinal Oran ($r_k$) *(Doğrulanmadı)* | Kademe Sonu Kümülatif Sınır ($\text{m}^2$) *(Doğrulanmadı)* |
|---|---|---|
| 0–10 m² | 0,05 | 0,50 |
| 10–100 m² | 0,02 | 2,30 |
| 100–500 m² | 0,01 | 6,30 |
| 500–1000 m² | 0,005 | 8,80 |
| 1000–5000 m² | 0,004 | 24,80 |
| 5000–25000 m² | 0,003 | 84,80 |
| 25000 m² üzeri | 0,0015 | — |

*(TASLAK — yönetmelik görselinden doğrulanmadı)* $A > 25000\text{ m}^2$ için açık ifade: $\Delta A_{\max}(A) = 84{,}80 + 0{,}0015 \times (A - 25000)$.

#### 4.3.3. Geçici Koordinat Profili (TPYS Md. 8(5) — YARDIMCI HESAPLAYICI, VARSAYILAN DEĞİL / `TC-TECVIZ-002`)
> **Uyarı:** Bu model **geçici koordinatlı parsel için yardımcı hesaptır (yardımcı hesaplayıcı, varsayılan değil)**.

Her köşe için nokta konum doğruluğu **$m_p$ ($r = m_p = \sqrt{m_x^2 + m_y^2}$)** yarıçapında hata çemberi çizilir. İç ve dış yanılma sınırları ($A_{\min}, A_{\max}$), bu çemberlerin oluşturduğu iç ve dış alan zarflarından yardımcı bilgi olarak hesaplanır.

### 4.4. DOP ve Mahsup Ayrımı

Düzenleme Ortaklık Payı Oranı ($\text{DOPO}$), düzenlemeye giren ve DOP kesintisine tabi parsellerin mahsup (bağış, önceki terk/DOP) sonrası toplam net alanı ($\sum A_{\text{giren}}$) ile imar adalarında tahsis edilen toplam net imar parseli alanı ($\sum A_{\text{tahsis}}$) üzerinden aşağıdaki bağıntıyla hesaplanır:

$$\text{DOPO} = \frac{\sum A_{\text{giren}} - \sum A_{\text{tahsis}}}{\sum A_{\text{giren}}}, \qquad \text{DOPO} \le 0{,}4500000$$

Her bir parselin ($i$) net DOP kesintisi ve tahsis hakkı:

$$\text{DOP}_i = \text{round}\left(A_{\text{giren}, i} \times \text{DOPO},\, 2\right), \qquad A_{\text{tahsis}, i} = A_{\text{giren}, i} - \text{DOP}_i$$

Parsel ayrımı:

- Özel mülkiyet
- Kamu hizmetine tahsisli kamu taşınmazları
- Kamunun özel mülkiyeti
- Kapanan yol
- Terk
- Bağış
- Önceki DOP

`ParselMevzuatSinifiKaydi` enum yapısıyla ayrı modellenir.

**Yasal üst sınır:** DOPO en fazla %45 ($0{,}45$) olabilir. Aşan kısım tescil harici alan, belediye mülkiyeti, muvafakatli kamu taşınmazı ve kamulaştırma sırasıyla karşılanır.

## 5. Hata Taksonomisi ve Hata Kodları

### 5.1. Amaç

Her hata için standart bir kimlik, önem seviyesi, kullanıcı mesajı, teknik detay ve önerilen aksiyon tanımlanır. Hata mesajları string olarak UI'de tutulmaz; merkezi `ErrorCatalog` kullanılır.

### 5.2. Hata Kategorileri

| Kod öneki | Kategori | Örnek |
|---|---|---|
| ERR-GIRD- | Girdi / dosya | DXF/DWG/NCZ okunamadı, DWG/NCZ sürümü uyumsuz, PDF bozuk |
| ERR-CRS- | Koordinat sistemi | CRS eksik, epok uyumsuz |
| ERR-TOPO- | Topoloji | Self-intersection, açık poligon |
| ERR-TAPU- | Tapu / kroki | Malik eşleşmedi, hisse eksik |
| ERR-TECVIZ- | Tecviz | Yanılma sınırı aşıldı |
| ERR-DOP- | DOP | Yüzde 45 aşıldı, mahsubu eksik |
| ERR-PLAN- | Plan kuralı | Cephe, derinlik, asgari alan |
| ERR-KUANT- | Kuantizasyon | Alan sapması, köşe çakışması |
| ERR-CIKTI- | Çıktı | PARSİD şema, Fen Klasörü şablon, CAD dışa aktarım hatası |
| ERR-GUVEN- | Güvenlik | Audit zinciri bozuk, yetkisiz erişim |

### 5.3. Hata Veri Modeli

```json
{
  "code": "ERR-TOPO-001",
  "severity": "Critical",
  "category": "Topology",
  "userMessage": "Parsel sınırı kendi üzerinde kesişiyor.",
  "technicalMessage": "Self-intersection detected in polygon {parcelId}.",
  "recommendedAction": "Topoloji Onar ekranını kullanın veya CAD (DXF/DWG/NCZ) köşe sırasını kontrol edin.",
  "retryable": false,
  "requiresEngineerReview": true,
  "auditLogRequired": true
}
```

### 5.4. C# Modeli

```csharp
public sealed record AppError(
    string Code,
    ErrorSeverity Severity,
    ErrorCategory Category,
    string UserMessage,
    string TechnicalMessage,
    string RecommendedAction,
    bool Retryable,
    bool RequiresEngineerReview,
    bool AuditLogRequired);
```

### 5.5. Test Adları

- `HataKatalogu_Tum_HataKodlari_Benzersiz_Olmali()`
- `HataKatalogu_KritikHatalar_EngineerReview_Isteyebilir()`
- `HataKatalogu_KullaniciMesaji_TeknikMesajdan_Ayri_Olmali()`
- `HataKatalogu_BilinmeyenHata_GenelKodla_Dondurmeli()`
- `CadFormatAdapter_Dxf_Dwg_Ncz_Dosyalarini_Ayni_IcModele_Donusturmeli()`

### 5.6. Kabul Kriterleri

- **AC-ERR-01:** Tüm hata kodları benzersiz, sürümlü ve `ErrorCatalog` içinde tanımlı olmalıdır.
- **AC-ERR-02:** Kritik hatalar mühendis incelemesi gerektirmelidir.
- **AC-ERR-03:** Kullanıcı mesajı teknik detay içermemelidir.
- **AC-CAD-01:** Uygulama `.dxf`, `.dwg` ve `.ncz` uzantılı CAD dosyalarını `ICadFormatAdapter` üzerinden okuyabilmeli ve katman/poligon/nokta/yazı nesnelerini tek tip iç modele dönüştürebilmelidir.
- **AC-CAD-02:** `.ncz` dosyalarındaki jeodezik $(Y, X)$ eksen sırası ile `.dxf`/`.dwg` dosyalarındaki Kartezyen $(X, Y)$ eksen sırası otomatik normalize edilmeli ve `Z != 0` kotları `Z = 0` yatay izdüşüm düzlemine indirgenmelidir.
- **AC-CAD-03:** Bozuk veya desteklenmeyen sürümdeki `.dwg` / `.ncz` dosyalarında uygulama çökmemeli, `ERR-GIRD-` kategorisinde anlaşılır kullanıcı mesajı ve dönüşüm önerisi üretmelidir.

## 6. Çok Kullanıcılı Senaryo ve Dosya Kilidi (Faz 13)

### 6.1. Amaç

Aynı `.imr18` dosyasının aynı anda iki kez yazma modunda açılmasını engellemek (Pilot aşamasında tek yazıcı varsayılır; Faz 13'te tam kilit devreye alınır).

### 6.2. Çözüm: Lock Dosyası

```
proje.imr18
proje.imr18.lock
```

`proje.imr18.lock` içeriği:

```json
{
  "machineId": "SHA256(CPU+MB+DISK)",
  "processId": 12345,
  "userName": "muhendis",
  "openedAt": "2026-11-09T00:00:00Z",
  "heartbeatAt": "2026-11-09T00:00:30Z",
  "appVersion": "1.0.0"
}
```

### 6.3. Kilit Alma Akışı

1. `.imr18.lock` var mı kontrol et.
2. Varsa heartbeat yaşı 60 saniyeden eski mi kontrol et.
3. Eski ise "stale lock" kabul et ve kilidi devral.
4. Yeni ise "dosya başka oturumda açık" uyarısı ver.
5. Salt-okunur mod öner.
6. Kullanıcı "yine de aç" derse yalnızca ReadOnly modda aç.

### 6.4. Davranış Matrisi

| Durum | Davranış |
|---|---|
| Lock yok | Yazma modunda aç |
| Lock var, heartbeat taze | Salt-okunur aç |
| Lock var, heartbeat eski | Kilidi devral, yazma modunda aç |
| Aynı makine, aynı PID | Zaten açık oturum; ikinci pencereyi engelle |
| Farklı makine | Salt-okunur öner |

### 6.5. Test Adları

- `CokKullanicili_AyniDosya_IkinciAcilista_ReadOnlyModa_Gecmeli()`
- `CokKullanicili_StaleLock_60SaniyeSonra_Devralinmali()`
- `CokKullanicili_AyniMakineAyniPID_IkinciPencere_Engellenmeli()`
- `CokKullanicili_FarkliMakine_ReadOnly_Onerilmeli()`
- `CokKullanicili_KilitDosyasi_Bozuk_Ise_GuvenliGeriKazanim_Yapmali()`

### 6.6. Kabul Kriterleri

- **AC-MULTI-01 (AC-LOCK-01):** Aynı `.imr18` dosyası aynı anda yalnızca bir oturumda yazma modunda açılabilir.
- **AC-MULTI-02 (AC-LOCK-02):** İkinci oturum varsayılan olarak salt-okunur modda açılmalıdır.
- **AC-MULTI-03 (AC-LOCK-03):** Stale lock 60 saniye sonra devralınabilmelidir.
- **AC-MULTI-04 (AC-LOCK-06/07):** Lock dosyası bozuksa sistem çökmemeli, güvenli kurtarma sunmalıdır.

## 7. OCR Model Lisansı ve Güncelleme Politikası (Faz 16)

### 7.1. Amaç

`tur.traineddata` için sürüm, kaynak, checksum, lisans ve güncelleme politikasını sürümlenmiş yapıda tutmak (Pilot aşamasında manuel/vektörel girdi kullanılır; OCR Faz 16'da devreye girer).

### 7.2. Veri Modeli

```json
{
  "ocrModel": {
    "language": "tur",
    "modelSet": "tessdata_best",
    "version": "4.1.0",
    "source": "https://github.com/tesseract-ocr/tessdata_best",
    "license": "Apache-2.0",
    "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    "oem": 1,
    "installedAt": "2026-11-09T00:00:00Z",
    "verified": true
  }
}
```

### 7.3. Politika

- Model, kurulum paketine gömülür; çalışma sırasında internetten indirilmez.
- Her model dosyası SHA-256 ile doğrulanır.
- Model sürümü `OcrModelProfile` içinde tutulur.
- Model güncellemesi yalnızca kullanıcı onayıyla yapılır.
- Model checksum uyuşmazsa OCR devre dışı kalır, uygulama çökmez.
- **Doğrulama Zorunluluğu (`AC-OCR-05..06`):** OCR sonucu hiçbir zaman doğrudan hesap girdisi olarak kullanılmaz; mühendis doğrulamasına ve onayına tabi tutulur.

### 7.4. Test Adları

- `OcrModel_Tur_Model_Checksum_Dogrulanmali()`
- `OcrModel_Tur_Model_Lisans_Bilgisi_Zorunlu_Olmali()`
- `OcrModel_Tur_Model_Guncelleme_KullaniciOnayi_Ister()`
- `OcrModel_Tur_Model_ChecksumUyumsuzsa_OCR_DevreDisi_Kalmali()`
- `OcrModel_Sonucu_KullaniciDogrulamasi_Olmadan_Hesaba_Girmemeli()`

### 7.5. Kabul Kriterleri

- **AC-OCR-01:** `tur.traineddata` sürümü, kaynak, lisans ve SHA-256 bilgisi profile yazılmalıdır.
- **AC-OCR-02:** Checksum uyuşmazsa OCR devre dışı kalmalı, uygulama çökmemelidir.
- **AC-OCR-03:** Model güncellemesi yalnızca kullanıcı onayıyla yapılmalıdır.
- **AC-OCR-05/06:** OCR çıktısı doğrudan hesap motoruna bağlanmamalı, kullanıcı doğrulamasından geçmelidir.

## 8. Erişilebilirlik Test Planı (Pilot Temel & Faz 17 Tam WCAG 2.2 AA)

### 8.1. Hedef Seviye

WCAG 2.2 AA (Pilot aşamasında temel klavye/odak/etiket desteği sağlanır; Faz 17'de tam ekran okuyucu ve kontrast sertifikasyonu tamamlanır).

### 8.2. Zorunlu Kontroller

| Alan | Kural | Test |
|---|---|---|
| Klavye | Tüm temel işlemler klavyeyle yapılabilmeli | Klavye gezinme testi |
| Klavye tuzağı | Her odaklanılabilir bileşenden çıkılabilmeli | Tab döngüsü testi |
| Odak görünür | Odak göstergesi görünür ve kontrastlı olmalı | Görsel test |
| Kontrast | Metin en az 4.5:1, büyük metin 3:1 | Kontrast testi |
| Ekran okuyucu | Tüm buton/alan anlamlı etikete sahip | NVDA / Narrator testi |
| Hata mesajı | Hata, ekran okuyucuya duyurulmalı | Erişilebilirlik testi |
| Renk | Renk tek başına bilgi taşımaz | Renk bağımsızlığı testi |

### 8.3. Klavye Kısayolları

| Komut | Kısayol |
|---|---|
| Kaydet | Ctrl+S |
| Geri al | Ctrl+Z |
| Yinele | Ctrl+Y |
| Yakınlaştır | Ctrl++ |
| Uzaklaştır | Ctrl+- |
| Sığdır | Ctrl+0 |
| Komut çubuğu | Ctrl+K |
| Parsel seç | Ctrl+P |
| DOP paneli | Ctrl+D |
| Yardım | F1 |

### 8.4. Test Adları

- `Erisilebilirlik_Klavye_Ile_TumTemelIslemler_Yapilabilmeli()`
- `Erisilebilirlik_KlavyeTuzagi_Olmamali()`
- `Erisilebilirlik_OdakGostergesi_Gorunur_Olmali()`
- `Erisilebilirlik_Kontrast_Orani_4_5_Uzerinde_Olmali()`
- `Erisilebilirlik_EkranOkuyucu_TumButonlar_Etiketli_Olmali()`
- `Erisilebilirlik_HataMesaji_EkranOkuyucuya_Duyurulmali()`

### 8.5. Kabul Kriterleri

- **AC-A11Y-01:** Tüm temel işlemler klavyeyle yapılabilir olmalıdır.
- **AC-A11Y-02:** Klavye tuzağı oluşmamalıdır.
- **AC-A11Y-03:** Metin kontrastı en az 4.5:1 olmalıdır.
- **AC-A11Y-04:** Odak göstergesi görünür ve kontrastlı olmalıdır.
- **AC-A11Y-05:** Tüm etkileşimli bileşenler ekran okuyucu için etiketli olmalıdır.
- **AC-A11Y-06/07:** Hata mesajları ekran okuyucuya duyurulmalı ve renk tek başına bilgi taşımamalıdır.

## 9. CI/CD Sözleşmesi ve Public GitHub Politikası

### 9.1. Amaç ve Public GitHub Kuralları

- CI, Faz P7'ye kadar bekletilmez; **Faz P0/P1'de kurulur** ve her fazda genişletilir.
- Public depoda standart GitHub-hosted runner (`windows-latest`) kullanılarak ücretsiz CI hedeflenir; ücretli/larger/GPU runner, Claude API Action veya ücretli servisler otomatik çağrılmaz.
- `permissions: contents: read` ve PR başına `concurrency` tanımlanır; güvenilmeyen PR'larda secret, publish yetkisi veya `pull_request_target` ile kod çalıştırma yasaktır.
- Her job ayrı bir sanal makinedir; `--no-build` yalnızca aynı job içinde build adımı çalıştıysa kullanılır.
- `dotnet list package --vulnerable` çıktısı tek başına job'u kırmadığı için script içinde kontrol edilerek açık varsa `exit 1` ile CI başarısız kılınır.
- Boş test seçimi veya sıfır test çalışması başarı sayılmaz; beklenen test sayısı doğrulanır.

### 9.2. GitHub Actions Sözleşmesi (`.github/workflows/ci.yml`)

```yaml
name: Imar18Studio CI
on:
  push:
    branches: [ main, dev ]
  pull_request:
    branches: [ main, dev ]
  workflow_dispatch:

permissions:
  contents: read

concurrency:
  group: ci-${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  build-test:
    runs-on: windows-latest
    env:
      DOTNET_CLI_TELEMETRY_OPTOUT: 1
      DOTNET_NOLOGO: 1
    steps:
      - uses: actions/checkout@v4
      - name: Check no real field data in repository (AC-PILOT-08)
        shell: pwsh
        run: ./scripts/check-no-real-data.ps1
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore Imar18Studio.sln
      - run: dotnet build Imar18Studio.sln -c Release --no-restore -warnaserror
      - run: dotnet test Imar18Studio.sln -c Release --no-build --filter "Category!=FullCad&Category!=UiSnapshot" --logger trx --collect:"XPlat Code Coverage"
      - run: dotnet test tests/Imar18.Architecture.Tests/Imar18.Architecture.Tests.csproj -c Release --no-build --logger trx
      - run: dotnet test tests/Imar18.Pilot.Tests/Imar18.Pilot.Tests.csproj -c Release --no-build --logger trx
      - name: Zero-Test Gate (Sıfır Test Kapısı)
        shell: pwsh
        run: |
          $trxFiles = Get-ChildItem -Path . -Filter "*.trx" -Recurse
          if (-not $trxFiles -or $trxFiles.Count -eq 0) {
            Write-Error "SIFIR TEST HATASI: Hiçbir .trx test sonuç dosyası üretilmedi!"
            exit 1
          }
          foreach ($file in $trxFiles) {
            [xml]$xml = Get-Content $file.FullName
            $total = [int]$xml.TestRun.ResultSummary.Counters.total
            Write-Host "$($file.Name) -> Toplam Çalışan Test: $total"
            if ($total -le 0) {
              Write-Error "SIFIR TEST HATASI: $($file.FullName) içinde 0 test çalıştı!"
              exit 1
            }
          }
      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: "**/*.trx"
          retention-days: 7

  security:
    runs-on: windows-latest
    env:
      DOTNET_CLI_TELEMETRY_OPTOUT: 1
      DOTNET_NOLOGO: 1
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore Imar18Studio.sln
      - name: Check vulnerable packages
        shell: pwsh
        run: |
          $out = dotnet list Imar18Studio.sln package --vulnerable --include-transitive | Out-String
          Write-Host $out
          if ($out -match "has the following vulnerable packages") {
            Write-Error "Vulnerable package detected!"
            exit 1
          }
      - run: dotnet test tests/Imar18.Infrastructure.Tests/Imar18.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~Security|FullyQualifiedName~Guvenlik|FullyQualifiedName~AuditLog|Category=Smoke"

  release-gate:
    needs: [build-test, security]
    runs-on: windows-latest
    if: github.ref == 'refs/heads/main' && github.event_name == 'workflow_dispatch'
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      # Faz 20 stub: ProductionGate filtresi (dotnet publish adımı Faz 20'ye kadar eklenmez)
      - run: dotnet test Imar18Studio.sln -c Release --filter "FullyQualifiedName~ProductionGate|Category=Smoke"
```

### 9.3. Zorunlu Kapılar

| Kapı | Koşul |
|---|---|
| Build | Derleme hatası yok (`-warnaserror`) |
| Test | Tüm testler yeşil (sıfır test geçersiz) |
| Mimari | NetArchTest ihlali yok |
| Güvenlik | Kritik güvenlik açığı yok (exit code ile zorunlu) |
| RTM | Eksik satır yok |
| Mevzuat profili | Onaysız profil yok |
| PARSİD | Şema sürümü tanımlı (yoksa yalnızca deneysel iç format) |
| Fen Klasörü | Şablon sürümü tanımlı |
| İmza | Üretim Release'de (Faz 20) kod imzalama zorunlu |

### 9.4. Test Adları

- `CI_Build_Basarili_Olmali()`
- `CI_TumTestler_Gecmeli()`
- `CI_NetArchTest_Ihlal_Olmamali()`
- `CI_Guvenlik_Taramasi_KritikAcik_Olmamali()`
- `CI_RTM_Eksiksiz_Olmali()`
- `CI_MevzuatProfili_Onayli_Olmali()`
- `CI_Parsid_SemaSurumu_Tanimli_Olmali()`
- `CI_FenKlasoru_SablonSurumu_Tanimli_Olmali()`

### 9.5. Kabul Kriterleri

- **AC-CI-01 (AC-PILOT-07):** Her PR ve push'ta build ve test otomatik çalışmalıdır.
- **AC-CI-02:** NetArchTest ihlali CI'ı kırmalıdır.
- **AC-CI-03:** Kritik güvenlik açığı build/release'i engellemelidir.
- **AC-CI-04:** Onaysız mevzuat profili release'i engellemelidir.
- **AC-CI-05:** PARSİD / Fen Klasörü şema sürümü tanımsızsa resmi ihracat engellenmelidir.

## 10. Telemetri Gizlilik Sözleşmesi (Faz 19)

### 10.1. Amaç

Telemetri Pilot aşamasında tamamen dışarıdadır; Faz 19'da eklendiğinde varsayılan olarak kapalı, opt-in ve minimal olmalı, kullanıcı istediği an kapatabilmelidir (`AC-TEL-06`).

### 10.2. Toplanan Alanlar

| Alan | Amaç | Kişisel veri mi? |
|---|---|---|
| appVersion | Hata teşhisi | Hayır |
| osVersion | Uyumluluk | Hayır |
| errorCategory | Hata analizi | Hayır |
| errorCode | Hata analizi | Hayır |
| durationMs | Performans | Hayır |
| featureUsed | Kullanım analizi | Hayır |
| crashStackHash | Çökme analizi | Hayır |
| userId | Kullanıcı takibi | Hayır, **toplanmaz** |
| machineId | Cihaz takibi | Hayır, **toplanmaz** |
| projectName | Proje takibi | Hayır, **toplanmaz** |
| malikAdi | Kişisel veri | Hayır, **toplanmaz** |
| tcKimlik | Kişisel veri | Hayır, **toplanmaz** |

### 10.3. Politika

- Varsayılan: kapalı (`DOTNET_CLI_TELEMETRY_OPTOUT=1`)
- Opt-in: kullanıcı açıkça onay verir ve dilediğinde kapatabilir
- Saklama süresi: 90 gün
- Anonimleştirme: crashStackHash SHA-256
- Kişisel veri: hiçbir koşulda gönderilmez
- Ağ erişimi: yalnızca telemetri endpoint'ine, opt-in sonrası

### 10.4. Test Adları

- `Telemetri_Varsayilan_Kapali_Olmali()`
- `Telemetri_KullaniciOnayi_Olmadan_Gonderim_Yapmamali()`
- `Telemetri_KisiselVeri_HicbirAlan_Gondermemeli()`
- `Telemetri_SaklamaSuresi_90Gun_Olmali()`
- `Telemetri_CrashStack_SHA256_ile_Anonimlestirilmeli()`
- `Telemetri_Kullanici_Istediginde_Kapatabilmeli()`

### 10.5. Kabul Kriterleri

- **AC-TEL-01:** Telemetri varsayılan olarak kapalı olmalıdır.
- **AC-TEL-02:** Kullanıcı onayı olmadan hiçbir veri gönderilmemelidir.
- **AC-TEL-03:** Kişisel veri, proje adı, malik adı ve TC Kimlik gönderilmemelidir.
- **AC-TEL-04:** Telemetri saklama süresi 90 gündür.
- **AC-TEL-05:** Crash stack yalnızca SHA-256 hash olarak saklanmalıdır.
- **AC-TEL-06:** Kullanıcı telemetriyi istediği zaman devre dışı bırakabilmelidir.

## 11. Kullanıcı Onayı UI Akışı ve Revizyon Bağı

### 11.1. Amaç

Mühendis onayı, ayrı bir ekran ve açık bir onay akışı olmalı. Onay; "kaydet" değil, nihai çıktı üretme yetkisi verir ve **hesaplanan revizyona (`RevisionId`) sıkıca bağlanır**.

### 11.2. Akış

1. Hesaplama tamamlandı ve `RevisionId` / `StateHash` üretildi
2. Uyarı / hata listesi göster
3. Kritik hata varsa onay engellenir (`AC-ONAY-01` / `AC-PILOT-05`)
4. Mühendis önizleme görür
5. Onay metnini okur
6. Onay verir (`RevisionId` ile eşleşir)
7. Audit Log kaydı oluşur (reddedilen/onaysız girişimler de kaydedilir)
8. Çıktı üretilebilir (Pilot aşamasında üretilen PDF/Excel dosyalarına `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` ibaresi basılır)
9. **Veri Değişikliği Kuralı (`AC-PILOT-06`):** Onay verildikten sonra herhangi bir parsel, hisse, geometri veya parametre değişirse mevcut onay otomatik olarak geçersizleşir.

### 11.3. Onay Ekranı İçeriği

Proje adı · Revizyon özeti (`RevisionId`) · Parsel sayısı · Toplam alan · DOP toplamı · Tecviz uyarı sayısı · Kritik hata sayısı · Mevzuat profili sürümü · PARSİD şema sürümü · Fen Klasörü şablon sürümü · Onay metni · Onay butonu · Geri al butonu

### 11.4. Onay Metni

> Bu çıktıyı nihai olarak onaylıyorum. Tecviz, DOP, tahsis ve geometri kontrollerini inceledim. Kritik hata bulunmadığını teyit ediyorum.

### 11.5. Geri Alma

- Onay sonrası 24 saat içinde geri alınabilir.
- Geri alma, Audit Log'a ayrı kayıt olarak yazılır.
- Nihai çıktı zaten üretilmişse "revizyon gerekli" uyarısı çıkar.
- Geri alma, eski çıktıyı silmez; yeni revizyon oluşturur.

### 11.6. Test Adları

- `KullaniciOnayi_KritikHata_Var_Ise_Engellenmeli()`
- `KullaniciOnayi_OnayMetni_Olmadan_Gecilememeli()`
- `KullaniciOnayi_AuditLog_Kaydi_Olusturmali()`
- `KullaniciOnayi_VeriDegisirse_Otomatik_Gecersizlesmeli()`
- `KullaniciOnayi_24Saat_Icinde_GeriAlinabilmeli()`
- `KullaniciOnayi_GeriAlma_AuditLog_Kaydi_Olusturmali()`

### 11.7. Kabul Kriterleri

- **AC-ONAY-01 (AC-PILOT-05):** Kritik hata varsa onay engellenmelidir.
- **AC-ONAY-02:** Onay metni okunmadan onay verilememelidir.
- **AC-ONAY-03:** Onay ve reddedilen girişimler Audit Log kaydı üretmelidir.
- **AC-ONAY-04:** Onay 24 saat içinde geri alınabilmelidir.
- **AC-ONAY-05:** Geri alma, ayrı Audit Log kaydı üretmelidir.
- **AC-PILOT-06:** Onay ilgili revizyona bağlıdır; veri değiştiğinde onay otomatik düşmelidir.

## 12. Sürüm Geri Alma (Faz 14)

### 12.1. Amaç

MevzuatProfili değiştiğinde eski proje dosyası açılabilmeli; ancak hesap sonucu otomatik güncellenmemeli (Pilot aşamasında tek doğrulanmış uzman profili kullanılır; Faz 14'te çoklu profil arşivi devreye girer).

### 12.2. Profil Sürümü Saklama

```json
{
  "profileId": "TPYS_2022",
  "version": "1.0.0",
  "effectiveDate": "2022-09-27",
  "checksum": "SHA256",
  "approvedAt": "2026-11-09T00:00:00Z",
  "approvedBy": "Harita Mühendisi",
  "legalReviewedBy": "Mevzuat Uzmanı"
}
```

### 12.3. Proje Tarafında

```json
{
  "projectProfile": {
    "profileId": "TPYS_2022",
    "version": "1.0.0",
    "checksum": "SHA256"
  }
}
```

### 12.4. Davranış

| Durum | Davranış |
|---|---|
| Aynı sürüm | Sorunsuz aç |
| Yeni sürüm var | Kullanıcıya "profil güncellendi" uyarısı |
| Eski sürüm kaldırıldı | Eski profil arşivden yüklenir |
| Profil checksum uyuşmuyor | Proje salt-okunur açılır |
| Profil imzası geçersiz | Proje salt-okunur açılır |

### 12.5. Test Adları

- `SurumGeriAlma_EskiProfil_Ile_Proje_Acilabilmeli()`
- `SurumGeriAlma_YeniProfil_Var_Ise_Uyari_Verilmeli()`
- `SurumGeriAlma_EskiProfil_Kaldirildi_Ise_Arsivden_Yuklenmeli()`
- `SurumGeriAlma_ChecksumUyumsuzsa_SaltOkunur_Acilmali()`
- `SurumGeriAlma_ImzaGecersizse_SaltOkunur_Acilmali()`

### 12.6. Kabul Kriterleri

- **AC-VER-01:** Eski profil sürümü kaldırılsa bile eski proje açılabilmelidir.
- **AC-VER-02:** Yeni profil sürümü varsa kullanıcı uyarılmalıdır.
- **AC-VER-03:** Checksum uyuşmazsa proje salt-okunur açılmalıdır.
- **AC-VER-04:** Profil imzası geçersizse proje salt-okunur açılmalıdır.
- **AC-VER-05/06:** PARSİD şema sürümü veya Fen Klasörü şablon sürümü tanımsızsa resmi ihracat engellenmelidir (`AC-PILOT-09`).

## 13. PARSİD Hata Haritası

### 13.1. Amaç

Her PARSİD doğrulama hatası için kullanıcı mesajı, teknik detay ve düzeltme önerisi tanımlamak. Doğrulanmış resmi PARSİD şeması sağlanmadığı durumda çıktı yalnızca "deneysel iç format" olarak adlandırılır; sahte PARSİD uyumluluğu iddia edilmez.

### 13.2. Hata Haritası

| Hata | Kullanıcı mesajı | Düzeltme önerisi |
|---|---|---|
| Şema sürümü eksik | PARSİD şema sürümü tanımlı değil | PARSİD ayarlarından hedef sürümü seçin |
| Zorunlu alan eksik | Zorunlu alan eksik: parcelId | Parsel kimliğini kontrol edin |
| Geometri geçersiz | Parsel geometrisi geçersiz | Topoloji Onar ekranını kullanın |
| CRS uyumsuz | Koordinat sistemi uyumsuz | CRS ayarlarını kontrol edin |
| Malik eksik | Malik bilgisi eksik | Tapu verisini kontrol edin |
| Hisse eksik | Hisse bilgisi eksik | Tapu verisini kontrol edin |
| DOP tutarsız | DOP değeri tutarsız | DOP hesabını yeniden çalıştırın |
| Alan tutarsız | Alan değeri tutarsız | Gauss alan doğrulamasını çalıştırın |

### 13.3. Veri Modeli

```json
{
  "code": "ERR-CIKTI-001",
  "parsidField": "parcelId",
  "userMessage": "Zorunlu alan eksik: parcelId",
  "recommendedAction": "Parsel kimliğini kontrol edin.",
  "severity": "Critical",
  "blocksExport": true
}
```

### 13.4. Test Adları

- `ParsidHataHaritasi_SemaSurumuEksik_KullaniciMesaji_Uretmeli()`
- `ParsidHataHaritasi_ZorunluAlanEksik_DuzeltmeOnerisi_Vermeli()`
- `ParsidHataHaritasi_GeometriGecersiz_TopolojiOnar_Onermeli()`
- `ParsidHataHaritasi_CrsUyumsuz_CrsAyarlari_Onermeli()`
- `ParsidHataHaritasi_KritikHata_Ihraci_Engellemeli()`

### 13.5. Kabul Kriterleri

- **AC-PARSID-01:** Her PARSİD hatası için kullanıcı mesajı ve düzeltme önerisi tanımlı olmalıdır.
- **AC-PARSID-02:** Kritik hata varsa ihracat engellenmelidir.
- **AC-PARSID-03:** Şema sürümü tanımsızsa resmi ihracat engellenmelidir.

## 14. Fen Klasörü İmza Zinciri (Pilot Checksum & Faz 12 Ed25519 Kanıt Zinciri)

### 14.1. Amaç

Fen Klasörü üretiminde mühendis onayı, Ed25519 imzası (Faz 12), zaman damgası ve PDF checksum sırasının korunması. Pilot aşamasında üretilen checksum hukuki/kriptografik imza sayılmaz ve çıktı `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` olarak damgalanır; Faz 12'de tam Ed25519 kanıt zinciri devreye girer.

### 14.2. İmza Zinciri Sırası

1. Hesaplama tamamlandı (`RevisionId` üretildi)
2. Kritik hata yok
3. Mühendis onayı verildi (ilgili `RevisionId` için)
4. Ed25519 açık anahtar doğrulandı (Faz 12)
5. Zaman damgası eklendi
6. PDF üretildi
7. PDF checksum hesaplandı
8. Başarılı çıktı Audit Log kaydı eklendi (reddedilen girişimler ise PDF checksum beklenmeden anında kaydedilir)

### 14.3. İmza Zinciri Veri Modeli

```json
{
  "fenKlasoru": {
    "templateVersion": "TKGM_2023_4_v1",
    "engineerApproval": {
      "approvedAt": "2026-11-09T00:00:00Z",
      "approvedBy": "Mühendis",
      "approvalHash": "SHA256"
    },
    "publicKey": "Ed25519 public key",
    "signature": "Ed25519 signature",
    "timestamp": "2026-11-09T00:00:00Z",
    "pdfChecksum": "SHA256"
  }
}
```

### 14.4. Sıra Kuralı

- Onay yoksa imza yok.
- İmza yoksa zaman damgası yok.
- Zaman damgası yoksa PDF nihai değil.
- PDF checksum yoksa "başarılı nihai çıktı" Audit Log kaydı yok.

### 14.5. Test Adları

- `FenKlasoru_ImzaZinciri_Sira_Dogrulanmali()`
- `FenKlasoru_OnayOlmadan_Imza_Uretilmemeli()`
- `FenKlasoru_ImzaOlmadan_ZamanDamgasi_Eklenmemeli()`
- `FenKlasoru_ZamanDamgasiOlmadan_Pdf_Nihai_Degil()`
- `FenKlasoru_PdfChecksum_Olmadan_BasariliCikti_AuditLog_Kaydi_Olusmamali()`

### 14.6. Kabul Kriterleri

- **AC-FEN-01 (AC-AUDIT-07):** Mühendis onayı olmadan imza üretilmemelidir.
- **AC-FEN-02 (AC-AUDIT-08):** İmza olmadan zaman damgası eklenmemelidir.
- **AC-FEN-03 (AC-AUDIT-09):** Zaman damgası olmadan PDF nihai kabul edilmez.
- **AC-FEN-04 (AC-AUDIT-10):** PDF checksum olmadan başarılı çıktı Audit Log kaydı oluşmaz.
- **AC-FEN-05:** İmza zinciri sırası bozulursa üretim reddedilir ve reddedilen girişim loglanır.

## 15. Gereksinim İzlenebilirlik Matrisi (RTM) Güncellemesi

| Mevzuat / Kaynak | İş Kuralı | FR Kodu | Test Kodu (TC) | Çekirdek Modül | Kabul Kriteri | Evre |
|---|---|---|---|---|---|---|
| TPYS (RG 27.09.2022/31966) & Md. 8(2) | `TecvizGirdisi` (Mühendis girdisi esas) + Md. 8(2) arşiv onaylı yardımcı hesaplayıcı (doğrulanmadı) | FR-F1.4 | TC-TECVIZ-001 | TecvizGirdisi / TecvizChecker | AC-CALC-02, AC-PILOT-02 | Pilot (P3) |
| TPYS Md. 8(5) | Geçici koordinatlı parsel hata çemberi zarf hesabı (yardımcı hesaplayıcı, varsayılan değil) | FR-F1.5 | TC-TECVIZ-002 | HataCemberiHesaplayici | AC-CALC-11, AC-PILOT-02 | Pilot (P3) |
| AAD Md. 14(4) | Kapanan yol mahsubu | FR-F1.6 | TC-DOP-001 | DopCalculator | AC-CALC-05, AC-PILOT-02 | Pilot (P3) |
| AAD Md. 14(6) | Terk alanı ek kesintisi | FR-F1.6 | TC-DOP-002 | DopCalculator | AC-CALC-06, AC-PILOT-02 | Pilot (P3) |
| AAD Md. 14(8) | Bağış/tahsis mahsubu | FR-F1.6 | TC-DOP-003 | DopCalculator | AC-CALC-06, AC-PILOT-02 | Pilot (P3) |
| AAD Md. 16(4) | Kamu mülkiyeti muafiyeti | FR-F1.6 | TC-DOP-004 | DopCalculator | AC-CALC-06, AC-PILOT-02 | Pilot (P3) |
| AAD Md. 16(5) | Tamamlayıcı kesinti şartı | FR-F1.6 | TC-DOP-005 | DopCalculator | AC-CALC-08, AC-PILOT-02 | Pilot (P3) |
| AAD Md. 16(6) | Ruhsatlı yapıda ilave DOP yasağı | FR-F1.6 | TC-DOP-006 | DopCalculator | AC-CALC-08, AC-PILOT-02 | Pilot (P3) |
| AAD Md. 15 | %45 aşımında karşılama sırası | FR-F1.7 | TC-DOP-007 | DopCalculator | AC-CALC-07, AC-PILOT-02 | Pilot (P3) |
| AAD Md. 17/1-(d) & Saha Kuralı (D16) | Net hak hesabı (`giren × (1 - DOPO)`) ve mühendis girdisiyle `malik -> imar parseli` hak eşleştirmesi (`Σ parsel`, `Σ malik`, `dm²` payda ve `Largest Remainder` ±1 dm² dengelemesi) | FR-F1.12 | TC-TAHSIS-001 | ShareResolutionService / AllocationMatrixService | AC-PILOT-03 | Pilot (P3.3) |
| Uygulama İlkesi | İki döngülü bisection & kuantizasyon | FR-F3.2, 3.4 | TC-GEOM-001 | BisectionEngine | AC-CALC-10, AC-PILOT-01 | Pilot (P2) |
| TKGM 2023/4 | Fen Klasörü standart ek seti | FR-F5.3 | TC-CIKTI-001 | FenKlasoruGenerator | AC-OUT-03, AC-PILOT-04 | Pilot (P5) |
| TKGM PARSİD | PARSİD şema sürüm uyumu | FR-F5.6 | TC-CIKTI-002 | ParsidExporter | AC-OUT-04, AC-PILOT-04 | Pilot (P5) |
| Uygulama İlkesi | Hata taksonomisi | FR-F1.11 | TC-ERR-001 | ErrorCatalog | AC-ERR-01..03 | Pilot (P1/P5) |
| Uygulama İlkesi | Çoklu CAD formatı (DXF, DWG, NCZ) | FR-F2.9 | TC-CAD-001 | ICadFormatAdapter / NczFormatAdapter | AC-CAD-01..03, AC-DXF-01..07 | Pilot (P4) & Faz 15 |
| Uygulama İlkesi | Crash-safe atomik dosya yazma ve yedekleme | FR-F6.10 | TC-IO-001 | AtomicProjectWriter | AC-IO-01 | Pilot (P5) & Faz 11 |
| Uygulama İlkesi | Kullanıcı onayı ve revizyon bağı | FR-F5.10 | TC-ONAY-001 | ApprovalService | AC-ONAY-01..05, AC-PILOT-06 | Pilot (P5) & Faz 12 |
| Uygulama İlkesi | CI/CD kapıları | FR-F6.9 | TC-CI-001 | ProductionGate | AC-CI-01..05, AC-PILOT-07 | Pilot (P0/P7) & Faz 20 |
| Uygulama İlkesi | Şifreli depolama ve anahtar yönetimi | FR-F6.11 | TC-SEC-001 | SqlCipherKeyManager | AC-SEC-01..08 | Pilot Sonrası (Faz 11) |
| Uygulama İlkesi | Fen Klasörü Ed25519 imza ve kanıt zinciri | FR-F5.12 | TC-FEN-001 | FenKlasoruSigner | AC-FEN-01..05, AC-AUDIT-01..10 | Pilot Sonrası (Faz 12) |
| Uygulama İlkesi | Çok kullanıcılı kilit | FR-F4.8 | TC-MULTI-001 | ProjectLockService | AC-MULTI-01..04, AC-LOCK-01..07 | Pilot Sonrası (Faz 13) |
| Uygulama İlkesi | Sürüm geri alma ve profil yönetimi | FR-F1.8 | TC-VER-001 | ProfileVersionService | AC-VER-01..06 | Pilot Sonrası (Faz 14) |
| Uygulama İlkesi | PARSİD hata haritası | FR-F5.11 | TC-PARSID-001 | ParsidErrorMap | AC-PARSID-01..03 | Pilot (P5) & Faz 14 |
| Uygulama İlkesi | OCR lisans / checksum ve doğrulama | FR-F2.8 | TC-OCR-001 | OcrModelProfile | AC-OCR-01..06 | Pilot Sonrası (Faz 16) |
| Uygulama İlkesi | Erişilebilirlik (WCAG 2.2 AA) | FR-F4.9 | TC-A11Y-001 | AccessibilityService | AC-A11Y-01..07 | Pilot (P4) & Faz 17 |
| Uygulama İlkesi | Ürün içi doğal dil / LLM Guardrail | FR-F4.11 | TC-AI-001 | GuardrailService | AC-AI-01..10 | Pilot Sonrası (Faz 18) |
| Uygulama İlkesi | Telemetri gizlilik | FR-F4.10 | TC-TEL-001 | TelemetryService | AC-TEL-01..06 | Pilot Sonrası (Faz 19) |

## 16. Yeni FR Kodları

- **FR-F1.8:** MevzuatProfili sürüm geri alma ve salt-okunur uyumluluk modu (Faz 14)
- **FR-F1.11:** Hata taksonomisi ve ErrorCatalog (Pilot P1/P5)
- **FR-F1.12 (D16 — Kodlama: Faz P3.3, P1–P2 kapsamı dışıdır):** DOPO oranında tüm hisselerden kesinti yapılır ($\text{net hak} = \text{giren} \times (1 - \text{DOPO})$); ardından tapu kaydındaki isimlere ve yaklaşık yer krokisine göre `malik -> imar parseli` eşleştirmesi YAPILIR. Eşleştirme mühendis girdisidir; yazılım $\sum \text{parsel} = \text{parsel alanı}$, $\sum \text{malik} = \text{net hak}$ ve $\text{dm}^2$ payda toplamını doğrular ve `Largest Remainder` ile $\pm 1\text{ dm}^2$ farkı dağıtır (§25.8 ile uyumludur).
- **FR-F2.8:** OCR model lisansı, checksum doğrulaması ve zorunlu mühendis onayı (Faz 16)
- **FR-F2.9:** Çoklu CAD formatı desteği (`ICadFormatAdapter` ile `.dxf`, `.dwg` ve Netcad `.ncz` okuma/yazma — Pilot P4 & Faz 15)
- **FR-F4.8:** Çok kullanıcılı dosya kilidi ve salt-okunur mod (Faz 13)
- **FR-F4.9:** Erişilebilirlik test planı ve WCAG 2.2 AA hedefi (Pilot temel & Faz 17 tam)
- **FR-F4.10:** Telemetri gizlilik sözleşmesi ve opt-in (Faz 19)
- **FR-F4.11:** Ürün içi doğal dil / LLM niyet ayrıştırıcısı ve `GuardrailService` (Faz 18)
- **FR-F5.10:** Kullanıcı onayı UI akışı, revizyon bağı ve geri alma (Pilot P5 & Faz 12)
- **FR-F5.11:** PARSİD hata haritası ve ihracat engelleme (Pilot P5 & Faz 14)
- **FR-F5.12:** Fen Klasörü imza zinciri ve zaman damgası (Pilot checksum P5 & Faz 12 Ed25519)
- **FR-F6.9:** CI/CD sözleşmesi ve sürüm kapıları (Pilot P0/P7 & Faz 20)
- **FR-F6.10:** Crash-safe atomik dosya yazma ve yedekleme (`AtomicProjectWriter` — Pilot P5)
- **FR-F6.11:** SQLCipher, Argon2id DEK/KEK ve DPAPI anahtar yaşam döngüsü (Faz 11)

## 17. Kabul Kriterleri Özeti

Her kriterin tanımı ilgili bölümde ve §22'deki iki evreli yol haritasında verilmiştir:
- **Çekirdek & Modül Kriterleri:** AC-ERR-01..03 (§5.6) · AC-CAD-01..03 (§5.6) · AC-MULTI-01..04 (§6.6) · AC-OCR-01..06 (§7.5 & §22) · AC-A11Y-01..07 (§8.5 & §22) · AC-CI-01..05 (§9.5) · AC-TEL-01..06 (§10.5) · AC-ONAY-01..05 (§11.7) · AC-VER-01..06 (§12.6) · AC-PARSID-01..03 (§13.5) · AC-FEN-01..05 (§14.6) · AC-IO-01 (§15).
- **Pilot Kapanış Kriterleri (§22.1):** `AC-PILOT-01..10`.
- **Pilot Sonrası Faz Kriterleri (§22.2):** `AC-SEC-01..08` (Faz 11) · `AC-AUDIT-01..10` (Faz 12) · `AC-LOCK-01..07` (Faz 13) · `AC-VER-01..06` (Faz 14) · `AC-DXF-01..07` (Faz 15) · `AC-OCR-01..06` (Faz 16) · `AC-A11Y-01..07` (Faz 17) · `AC-AI-01..10` (Faz 18) · `AC-TEL-01..06` (Faz 19) · `AC-REL-01..07` (Faz 20).

## 18. Üretim Önkoşulu Doğrulama Tablosu (Pilot vs. Pilot Sonrası Davranışı)

| Önkoşul Kalemi | İlgili Bölüm / FR | Sorumlu Rol | Kapanış Koşulu / Kanıt (Pilot Sonrası) | Geçici / Pilot Davranışı (Faz P0–P7) |
|---|---|---|---|---|
| Hata taksonomisi | FR-F1.11 | Yazılım Mimarı | ErrorCatalog eksiksiz ve test edildi | Çekirdek ErrorCatalog ve kritik hata engeli aktif |
| Çoklu CAD (DXF, DWG, NCZ) | FR-F2.9 | Yazılım Mimarı / Harita Mühendisi | Gelişmiş DXF/DWG/NCZ yazma, topoloji onarım ve epok (Faz 15) | Açıkça tanımlı profilde DXF/DWG/NCZ okuma ve eksen normalizasyonu |
| Şifreli saklama & Anahtar | FR-F6.11 | Yazılım Mimarı | SQLCipher, Argon2id DEK/KEK ve DPAPI (Faz 11) | Sentetik veriyle yerel pilot kaydı, atomik yazım ve checksum |
| Çok kullanıcılı kilit | FR-F4.8 | Yazılım Mimarı | Lock dosyası, 60 sn stale lock ve ReadOnly mod (Faz 13) | Tek yazıcı / tek oturum varsayımı |
| OCR lisansı ve okuma | FR-F2.8 | Yazılım Mimarı | Model checksum, lisans ve mühendis doğrulama akışı (Faz 16) | OCR devre dışı; manuel/sentetik veya vektörel girdi |
| Erişilebilirlik | FR-F4.9 | UI/UX Tasarımcısı | Tam WCAG 2.2 AA ekran okuyucu ve kontrast testleri (Faz 17) | Temel klavye, odak ve etiket desteği |
| Ürün içi LLM Asistanı | FR-F4.11 | Yazılım Mimarı | JSON şema + `GuardrailService` + mühendis önizlemesi (Faz 18) | Ürün içi LLM Pilot dışında; deterministik komut çubuğu |
| CI/CD ve İmzalama | FR-F6.9 | DevOps | ProductionGate ve üretim kod imzalama (Faz 20) | Public GitHub ücretsiz CI aktif; üretim CD/imzalama Pilot dışında |
| Crash-safe atomik yazma | FR-F6.10 | Yazılım Mimarı | AtomicProjectWriter ve .bak kurtarma testi tamam | Atomik kayıt ve temel işlem bütünlüğü |
| Telemetri gizlilik | FR-F4.10 | Ürün Sahibi | Opt-in, 90 gün saklama ve kapatma testi (Faz 19) | Telemetri Pilot dışında (tamamen kapalı) |
| Kullanıcı onayı | FR-F5.10 | Ürün Sahibi | Onay ekranı, revizyon bağı, 24s geri alma ve audit log | Revizyona bağlı onay ve kritik hata engeli zorunlu |
| Sürüm geri alma | FR-F1.8 | Yazılım Mimarı | Eski profil arşivi ve imza/checksum doğrulama (Faz 14) | Tek uzman onaylı profil (`TPYS_2022`) ile çalışma |
| PARSİD hata haritası | FR-F5.11 | Harita Mühendisi | Hata haritası ve şema sürümü ihracat engelleme | Doğrulanmış şema varsa kontrol; yoksa deneysel iç format uyarısı |
| Fen Klasörü imza zinciri | FR-F5.12 | Harita Mühendisi | Ed25519 + Zaman Damgası + PDF Checksum + Audit (Faz 12) | Checksum + `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgalı taslak PDF |

## 19. Pilot Senaryoları (Pilot Faz P6 Doğrulama Seti)

Pilot kapanışı öncesinde `tests/Imar18.Pilot.Tests` ve `test-data/synthetic/pilot-1..4/` altında aşağıdaki **4 ana Pilot senaryosu** (ve Faz 13'te 5. senaryo) bağımsız referanslarla doğrulanır:

| Pilot | Girdi (`test-data/synthetic/`) | Beklenen Doğrulama | Kabul Kriteri |
|---|---|---|---|
| **Pilot 1 — Konkav Ada & Çoklu CAD** | Sentetik/anonim L/U geometrisi (`.dxf`, `.dwg`, `.ncz`), 13 × 16 m ayrı cephe/derinlik/asgari alan parametreleri | Geçerli iki döngülü bisection bölmesi veya açıklanabilir güvenli duruş; kuantizasyon sonrası `Decimal` Gauss alan toleransı korunumu | `AC-PILOT-01` |
| **Pilot 2 — Karma Mülkiyet & DOP** | Sentetik/anonim kapanan yol, terk, kamu mülkiyeti, önceki DOP'lu parsel ve %45 aşımı | Uzman doğrulamalı mahsup/istisna sırası; %45 sınır aşımında karşılama sırası ve hata/iş akışı | `AC-PILOT-02` |
| **Pilot 3 — Müşterek Parsel** | Yüksek paydalı sentetik/anonim müşterek hisseler ve TAKBİS-kroki eşleştirmesi | Tam rasyonel (`Fraction`) temsil, toplam alan korunumu, Largest Remainder ile deterministik fark dağıtımı | `AC-PILOT-03` |
| **Pilot 4 — Çıktı Sözleşmesi & Revizyon** | Şema/şablon sürümü, `RevisionId`, mühendis onayı, veri değişikliği, checksum | Revizyonla bağlı onay (veri değişirse onayın düşmesi); eksik sürümde engel; `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgalı çıktı ve audit kaydı | `AC-PILOT-04..06`, `AC-PILOT-09` |
| **Pilot 5 — Çok Kullanıcılı Kilit *(Faz 13)*** | Aynı `.imr18` dosyasının iki oturumda açılması ve 60 sn stale lock | İkinci oturumun ReadOnly açılması ve 60 sn sonra stale lock devralma | `AC-LOCK-01..07` |

## 20. Bağlayıcı Mimari Kararlar Güncellemesi

- **D17 (Hata Taksonomisi):** Tüm hatalar ERR-* kodlarıyla tanımlanır; kullanıcı mesajı ve teknik detay ayrıdır.
- **D18 (Çok Kullanıcılı Kilit):** Aynı `.imr18` dosyası aynı anda yalnızca bir oturumda yazma modunda açılabilir (Faz 13).
- **D19 (OCR Lisansı ve Doğrulama):** OCR model sürümü, lisans ve checksum profile yazılır; OCR çıktısı mühendis doğrulaması olmadan hesaba girmez (Faz 16).
- **D20 (Erişilebilirlik):** WCAG 2.2 AA hedefi; klavye, kontrast ve ekran okuyucu testleri zorunludur (Faz 17).
- **D21 (CI/CD ve Public GitHub):** Public depoda standart Windows runner üzerinde ücretsiz CI çalışır; boş test veya paket açığı CI'ı kırar.
- **D22 (Telemetri):** Pilot dışında; Faz 19'da varsayılan kapalı, opt-in, minimal, anonimleştirilmiş ve kapatılabilir.
- **D23 (Kullanıcı Onayı ve Revizyon Bağı):** Mühendis onayı ilgili revizyona bağlanır; veri değişirse onay düşer. Reddedilen işlemler de audit log'a yazılır.
- **D24 (Sürüm Geri Alma):** Eski profil sürümü kaldırılsa bile eski proje açılabilir; checksum/imza uyuşmazsa salt-okunur mod (Faz 14).
- **D25 (PARSİD Hata Haritası):** Her hata için kullanıcı mesajı, teknik detay ve düzeltme önerisi tanımlıdır; doğrulanmış şema yoksa çıktı deneysel iç format olarak işaretlenir.
- **D26 (Fen Klasörü İmza Zinciri):** Onay → imza → zaman damgası → PDF checksum → başarılı çıktı audit log sırası zorunludur (Faz 12).
- **D27 (Ajan Çalışma Modeli):** Ortak kurallar `AGENTS.md`'de tutulur; `CLAUDE.md` ve `GEMINI.md` yalnızca araca özel farkları içerir. Ajanlar ayrı branch'lerde çalışır, aynı dosyada aynı anda çalışmaz (`docs/STATUS.md`), `main`'e yalnızca tüm testler yeşilken birleşir.
- **D28 (Çoklu CAD Formatı: DXF, DWG ve Netcad NCZ):** Sistem `ICadFormatAdapter` arayüzü üzerinden AutoCAD `.dxf`/`.dwg` (`ACadSharp`) ve Netcad `.ncz` (`NczFormatAdapter`) formatlarını birinci sınıf girdi/çıktı olarak destekler.
- **D29 (İki Evreli Geçiş: Pilot P0–P7 ve Pilot Sonrası Faz 11–20):** Ürün önce çekirdek deterministik motoru doğrulayan **Pilot (Faz P0–P7)** ve saha onayından sonra sırasıyla açılan **Pilot Sonrası (Faz 11–20)** olarak iki evrede geliştirilir.
- **D30 (Claude Code ve Google Antigravity Çift Uyumlu Skill/Kural Mimarisi) — YENİ:** Tüm proje becerileri açık `SKILL.md` (YAML frontmatter `name` + `description`) standardında, kurallar ise YAML frontmatter `trigger` destekli Markdown formatında yazılır. `.claude/skills/` ile `.agents/skills/` ve `.claude/rules/` ile `.agents/rules/` birebir aynı içeriği paylaşır; böylece hem Claude Code hem de Google Antigravity hiçbir uyum sorunu olmadan aynı kuralları ve becerileri çalıştırır.

## 21. Sürüm Notu (v2.19 Birleştirilmiş Sürüm)

Bu sürümde yapılan birleştirmeler:

- 6 matematiksel/jeodezik formül, §15–§18 RTM mutabakatı ve **D28 Çoklu CAD (`.dxf`, `.dwg`, `.ncz`)** mimarisi korundu.
- `tests/Imar18.Pilot.Tests` test projesi, `CommandBus` mutasyon kuralı, onay-revizyon bağı (`AC-PILOT-06`) ve reddedilen işlemlerin audit log'a yazılması kuralı eklendi.
- Public GitHub ücretsiz CI güvenliği (`permissions: contents: read`, `concurrency`, güvenlik açığında `exit 1`, sentetik veri sınırı) §9'a işlendi.
- **§22'ye** Faz P0–P7 Pilot Planı (`AC-PILOT-01..10`) ve Faz 11–20 Pilot Sonrası Yol Haritası eklendi.
- **§23'e** Ajan Model Yönlendirme Matrisi, Görev Başlangıç/Sonu Rapor Şablonları ve Yasak Davranışlar eklendi.
- **§24'e** Claude Code + Google Antigravity Çift Uyumlu Klasör, Skill (`imar18-*`), Subagent ve Faz Bazlı Plugin/MCP Yol Haritası (`D30`) eklendi.

## 22. İki Evreli Uygulama Yol Haritası: Pilot (Faz P0–P7) ve Pilot Sonrası (Faz 11–20)

### 22.1. Evre 1 — Faz Faz Pilot Planı (Faz P0 – Faz P7)

| Faz | İş Paketi | Çıkış Ölçütü |
|---|---|---|
| **P0** | Spesifikasyon, riskler, RTM, sentetik veri politikası (`test-data/synthetic/`), çift uyumlu `.claude`/`.agents` iskeleti ve erken CI kurulumu | Belirsizlik listesi, onaylı kapsam ve çalışan temel GitHub Actions CI |
| **P1** | Sayısal çekirdek: `Decimal` sözleşmesi, `BigInteger` tabanlı `Fraction`, deterministik `Largest Remainder` ve çekirdek `ErrorCatalog` | Bağımsız referans sonuçlarıyla %100 geçen birim ve mimari testler |
| **P2** | Geometri motoru: Eksen takası, yerel orijinli Gauss alanı, iki döngülü `BisectionEngine`, kuantizasyon (`Decimal` Gauss yeniden doğrulamalı) ve konkav ada kontrolü | Topoloji, `MultiPolygon` duruşu ve alan korunumu testleri (`TC-GEOM-001`) |
| **P3** | Uzman onaylı DOP/tecviz kuralları (`TecvizChecker`, `HataCemberiHesaplayici`, `DopCalculator` %45 sınırı ve mahsuplar) | İstisna, mahsup ve üst sınır aşım testleri (`TC-TECVIZ-001..002`, `TC-DOP-001..007`) |
| **P4** | **P4.0: `.NCZ` fizibilite spike** + **P4.1:** Önce Netcad `.ncz` girişi, `.dxf`/`.dwg` (`ACadSharp`) yedek (`ICadFormatAdapter` — `D14`), proje CRS/ölçek girdisi (`D3`), vektörel/manuel TAKBİS ve Avalonia + SkiaSharp UI | Girdi doğrulama, eksen normalizasyonu (`TC-CAD-001`) ve manuel Windows kontrolü |
| **P5** | Mühendis onayı (`RevisionId` bağlı), temel audit (reddedilenler dahil), atomik kayıt, `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgalı taslak PDF/Excel ve PARSİD sözleşmesi | Onay-revizyon-checksum tutarlılığı ve veri değişiminde onayın düşmesi testi |
| **P6** | Dört pilot senaryosu (`Imar18.Pilot.Tests` + anonimleştirilmiş gerçek saha projeleri), saha kullanımı, regresyon ve eksiklerin kapatılması | Kritik bulgu olmaması ve harita mühendisi / uzman incelemesi |
| **P7** | CI kapılarının tam doğrulaması ve Pilot Kapanış Raporu | Otomatik kontroller + manuel Windows doğrulaması + proje sahibi kapanış kararı |

#### Pilot Kabul Kriterleri (`AC-PILOT-01..10`)

| Kimlik | Kriter | Kanıt |
|---|---|---|
| **AC-PILOT-01** | Konkav ada (`.dxf`/`.dwg`/`.ncz`) güvenli bölünür veya açıklanabilir biçimde durur | Pilot 1 test raporu |
| **AC-PILOT-02** | DOP/tecviz istisnaları uzman onaylı profile uyar | Pilot 2 ve uzman referansı |
| **AC-PILOT-03** | Hisse toplamı ve deterministik Largest Remainder dağıtımı korunur | Pilot 3 testleri |
| **AC-PILOT-04** | Taslak çıktı (`"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"`) ve sürüm/checksum akışı izlenebilir | Pilot 4 raporu |
| **AC-PILOT-05** | Kritik hatada mühendis onayı engellenir | Negatif onay testi |
| **AC-PILOT-06** | Onay ilgili revizyona bağlıdır; veri değişirse onay otomatik geçersizleşir | Revizyon/değişiklik testi |
| **AC-PILOT-07** | PR ve push üzerinde Public GitHub CI otomatik çalışır | GitHub Actions kayıtları |
| **AC-PILOT-08** | Public depoda ve CI artifact'larında yalnızca güvenli sentetik/anonim fixture bulunur | Yayın öncesi inceleme |
| **AC-PILOT-09** | Desteklenmeyen CRS veya tanımsız şema için sahte başarı üretilmez | Negatif sözleşme testleri |
| **AC-PILOT-10** | Windows 10/11 temel kullanıcı akışı manuel olarak doğrulanır | Manuel kontrol formu |

### 22.2. Evre 2 — Pilot Sonrası Yol Haritası (Faz 11 – Faz 20)

Pilot sürümü sahada kullanılıp eksikleri çözüldükten ve `AC-PILOT-01..10` kapatıldıktan sonra sırasıyla yürütülecek fazlar:

1. **Faz 11 — Güvenlik Temeli (`FR-F6.11` / `AC-SEC-01..08`):** SQLCipher, `.imr18` şifreli konteyner, rastgele DEK, Argon2id KEK, DPAPI CurrentUser koruması ve bozuk anahtar kurtarma.
2. **Faz 12 — Audit ve Kanıt Zinciri (`FR-F5.12` / `AC-AUDIT-01..10`):** SHA-256 hash zinciri, Ed25519 imzalama, zaman damgası, atomik transaction ve bozulmada ReadOnly mod.
3. **Faz 13 — Çok Kullanıcı ve Dosya Kilidi (`FR-F4.8` / `AC-LOCK-01..07`):** `.imr18.lock`, 60 sn stale lock devralma, ikinci oturumda ReadOnly mod ve bozuk lock kurtarma (**Pilot 5**).
4. **Faz 14 — Sürüm ve Profil Yönetimi (`FR-F1.8` & `FR-F5.11` / `AC-VER-01..06`):** `MevzuatProfili` sürümleme, eski profil arşivi, checksum/imza doğrulama ve tam `ParsidErrorMap`.
5. **Faz 15 — Gelişmiş CAD (DXF/DWG/NCZ) ve CRS (`FR-F2.9` / `AC-DXF-01..07`):** Gelişmiş katmanlı `.dxf`/`.dwg`/`.ncz` yazma, etkileşimli Topoloji Onarım sihirbazı, snap ve epok kontrolleri.
6. **Faz 16 — OCR ve Belge Okuma (`FR-F2.8` / `AC-OCR-01..06`):** Yerel `Tesseract` (`tur.traineddata`), SHA-256 doğrulama ve OCR çıktısının zorunlu mühendis doğrulamasına tabi tutulması.
7. **Faz 17 — Erişilebilirlik ve UX (`FR-F4.9` / `AC-A11Y-01..07`):** Tam WCAG 2.2 AA uyumu, NVDA/Narrator ekran okuyucu, klavye tuzağı, 4.5:1 kontrast ve gelişmiş CAD çizim deneyimi.
8. **Faz 18 — Ürün İçi Doğal Dil / LLM Asistanı (`FR-F4.11` / `AC-AI-01..10`):** Doğal dil komut çubuğu, JSON şema doğrulama ve `GuardrailService` (LLM hesap yapmaz, mevzuat kararı vermez, dosyaya yazmaz).
9. **Faz 19 — Telemetri ve Gözlemlenebilirlik (`FR-F4.10` / `AC-TEL-01..06`):** İsteğe bağlı (opt-in) telemetri, 90 gün saklama, SHA-256 `crashStackHash` ve tek tıkla kapatma.
10. **Faz 20 — Release ve Dağıtım (`FR-F6.9` / `AC-REL-01..07`):** Üretim kod imzalama (3 bağımsız HWID 2/3 eşleşme / TPM opsiyonu değerlendirmesiyle), `ProductionGate`, GitHub Release ve geri alma planı.

## 23. Ajan Model Yönlendirme, Görev Rapor Şablonları ve Yasak Davranışlar

### 23.1. Geliştirme Modeli Yönlendirme Politikası

| Görev Türü | Alt Ajan Rolü (`.claude/agents/*`) | Claude Code Modeli | Google Antigravity Modeli | Hedef Effort |
|---|---|---|---|---|
| Kapsam, zor geometri (`BisectionEngine`), kriptografi ve mimari tasarım | `architect` | Erişilebilir en güçlü Claude (Opus) | Gemini Pro / Claude (Derin Düşünme) | `high` / `xhigh` |
| Kritik hesap (`Decimal`/`Fraction`, DOP, tecviz), audit, onay, çıktı kodu | `implementer` | Erişilebilir Opus | Claude Sonnet / Opus (Düşünme Modu) | `high` |
| Normal geliştirme, `.dxf`/`.dwg`/`.ncz` I/O adaptörleri, Avalonia UI, CI | `implementer` | Erişilebilir Opus / Sonnet | Gemini Pro | `medium` |
| Bağımsız test matrisi, boundary/negatif testler ve Pilot fixture testleri | `test-engineer` | Erişilebilir Sonnet | Gemini Pro / Claude Sonnet | `high` |
| Bağımsız PR ve kod incelemesi (mimari, güvenlik, kapsam ihlali) | `reviewer` | Erişilebilir Sonnet | Claude Sonnet | `high` |
| Dokümantasyon, kullanıcı/hata metinleri, sürüm notu ve changelog | `docs-writer` | Erişilebilir Haiku / Sonnet | Gemini Flash | `low` |

### 23.2. Zorunlu Görev Başlangıç ve Görev Sonu Rapor Şablonları

```text
[GÖREV BAŞLANGIÇ RAPORU]
Görev / Faz / FR-AC-TC:
Risk Seviyesi:
Seçilen Alt Ajan / Model / Effort:
Gerçekten Kullanılan Model/Ayar:
Gerekçe:
Onay Gereksinimi:
Kilitlenen Dosyalar (docs/STATUS.md):
Plan:
```

```text
[GÖREV SONU RAPORU]
## Görev Özeti
- Yapılan iş:
- Etkilenen Faz ve FR/AC/TC:
- Kullanılan model ve effort:
## Değişiklikler
- Eklenen / Değiştirilen / Silinen dosyalar:
## Testler
- Çalıştırılan komutlar:
- Geçen / Başarısız / Atlanan test sayısı:
- Çalıştırılamayan kontroller ve nedeni:
## Riskler ve Kapsam
- Mimari / güvenlik bulguları:
- Pilot kapsamı etkisi:
- Mühendis / proje sahibi onayı gerektiren kararlar:
- Açık sorular (docs/STATUS.md):
## Sonraki Adım
- Önerilen sonraki küçük PR / görev:
```

### 23.3. Kesin Yasak Davranışlar

- Mevzuat hükmü, koordinat dönüşüm parametresi, tecviz/DOP toleransı veya test referans değeri uydurmak.
- Pilot bitmeden ve proje sahibi onaylamadan Faz 11–20 arasındaki özellikleri sessizce kodlamaya çalışmak.
- `dotnet test` çalıştırmadan "testler geçti" demek veya sıfır test çalışan filtreyi başarı saymak.
- Doğrulanmamış şemayı "resmî PARSİD uyumlu" diye sunmak veya Pilot checksum'unu "hukuki e-imza" gibi göstermek.
- Audit kaydını yalnızca başarılı PDF'ye bağlayıp reddedilen/hatalı girişimleri kayıtsız bırakmak.
- Ürün içi LLM'i (Faz 18) hesap motoru gibi kullanmak veya kişisel veriyi dış servise göndermek.
- Gerçek tapu, malik, TC kimlik veya müşteri koordinatlarını public depoya, CI loguna veya test fixture'larına koymak.

## 24. Claude Code ve Google Antigravity Çift Uyumlu Skill, Subagent ve Plugin Mimarisi (D30)

Bu bölüm, `claude_klasor_yapisi.md` ve `claude_plugins_roadmap.pdf` içindeki yapıların **Imar18Studio v2.19** mimarisine uyarlanmış ve hem **Claude Code** hem de **Google Antigravity** üzerinde sıfır uyum sorunuyla çalışacak şekilde birleştirilmiş halidir.

### 24.1. Çift Platform Uyumluluk Mekanizması (Nasıl İkisinde Birden Çalışır?)

1. **Ortak Talimat Kaynağı (`AGENTS.md` + `CLAUDE.md` + `GEMINI.md`):**
   - Google Antigravity kök dizindeki `AGENTS.md` ve `GEMINI.md` dosyalarını, Claude Code ise `CLAUDE.md` dosyasını her oturumda otomatik yükler. Üç dosya da aynı faz kilidini (`docs/STATUS.md`) referans alır.
2. **Açık `SKILL.md` Standardı (`.claude/skills/` ↔ `.agents/skills/`):**
   - Google Antigravity, Anthropic'in açık `SKILL.md` (YAML frontmatter `name` ve `description`) standardını yerel olarak destekler ve `.agents/skills/<skill-adi>/SKILL.md` dizinini tarar; Claude Code ise `.claude/skills/<skill-adi>/SKILL.md` dizinini tarar.
   - Windows geliştirme ortamında tek komutla oluşturulan dizin bağlantısı (`mklink /J .agents\skills .claude\skills` ve `mklink /J .agents\rules .claude\rules`) sayesinde tek bir skill veya kural dosyası yazıldığında her iki araçta da anında ve aynı şekilde çalışır.
3. **Ortak Kural Dosyaları (`.claude/rules/*.md` ↔ `.agents/rules/*.md`):**
   - Kural dosyalarının başına eklenen YAML frontmatter (`description` ve `trigger: always_on` veya `trigger: model_decision`) Google Antigravity tarafından doğrudan yorumlanırken, Claude Code tarafından da standart Markdown üstbilgisi olarak okunur.

### 24.2. Güvenli İzin Yapılandırması (`.claude/settings.json` — D9)

Dal içi `git add`, `git commit`, `git checkout/branch` ve `git merge --no-ff` işlemleri izinli; **`git push` kesinlikle yasaktır** (`settings.json` yalnızca Claude Code'u bağlar; Google Antigravity için aynı kural `AGENTS.md` ve `rules/security.md` içinde bağlayıcıdır: `git push` ve `main`'e merge yasaktır):

```json
{
  "permissions": {
    "allow": [
      "Read",
      "Grep",
      "Glob",
      "Bash(dotnet restore:*)",
      "Bash(dotnet build:*)",
      "Bash(dotnet test:*)",
      "Bash(dotnet format:*)",
      "Bash(dotnet list package:*)",
      "Bash(git status:*)",
      "Bash(git diff:*)",
      "Bash(git log:*)",
      "Bash(git branch:*)",
      "Bash(git checkout:*)",
      "Bash(git switch:*)",
      "Bash(git add:*)",
      "Bash(git commit:*)",
      "Bash(git merge --no-ff:*)"
    ],
    "deny": [
      "Bash(dotnet publish:*)",
      "Bash(git push:*)",
      "Bash(git push --force:*)",
      "Bash(git reset --hard:*)",
      "WebFetch",
      "WebSearch"
    ]
  },
  "env": {
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    "DOTNET_NOLOGO": "1"
  }
}
```

*(Not: `.claude/settings.local.json` yalnızca geliştiricinin kendi makinesinde `git status`, `git diff`, `git log` gibi kişisel izinler için kullanılır ve `.gitignore` içinde tutularak repoya asla eklenmez.)*

### 24.3. Projeye Özel 30 Skill (`11 Çekirdek Imar18 + 19 Yardımcı SKILL.md`) Kataloğu ve Faz Sırası

Bağlam penceresini (context window) şişirmemek ve ajanları her fazda doğru mühendislik kurallarına kilitlemek için yazılacak 11 özel beceri:

| Sıra | Skill Adı (`SKILL.md`) | Aktif Olduğu Faz | Görevi ve Projemize Uyarlanmış Kapsamı |
|---|---|---|---|
| 1 | `start-pilot-phase` | Tüm Fazlar (P0–P7, 11–20) | `docs/STATUS.md` ve yol haritasını okur, faz giriş koşullarını ve kapsam kilidini denetler, küçük sıralı görev listesi üretir. |
| 2 | `run-pilot-tests` | Tüm Fazlar (P1–P7) | İlgili birim, mimari ve `Imar18.Pilot.Tests` senaryolarını koşturur; `0 test` çalışmasını hata sayar ve test sayılarını raporlar. |
| 3 | `review-pull-request` | Tüm Fazlar | Bağımsız PR incelemesi yapar (`Decimal` izolasyonu, NTS sızıntısı, onay-revizyon bağı, Pilot kapsam ihlali kontrolü). |
| 4 | `close-pilot-phase` | Tüm Fazlar | Faz çıkış ölçütlerini, açık riskleri ve mühendis/proje sahibi onay gereksinimini denetleyerek faz kapanış raporu üretir. |
| 5 | `imar18-tdd-dotnet` | Faz P0 / P1 başı | Katı Red-Green-Refactor döngüsünü, Türkçe test isimlendirme standardını ve boundary/negatif test zorunluluğunu uygular. |
| 6 | `imar18-domain-rules` | Faz P1 & P3 | `Decimal`, `Fraction(BigInteger, BigInteger)`, eşit kalanda kararlı bağlayıcı sıralı `Largest Remainder`, tecviz ve %45 DOP/mahsup kurallarını zorlar. |
| 7 | `imar18-architecture` | Faz P0 & Tüm Fazlar | Clean Architecture, CQRS/`CommandBus`, Domain saflığı, NTS sızıntı yasağı ve `NetArchTest` kurallarını korur. |
| 8 | `imar18-mevzuat-profil` | Faz P3 & Faz 14 | `TPYS_2022` uzman onaylı mevzuat profili, altın veri seti (`test-data/synthetic/`) ve profil checksum doğrulamasını yönetir. |
| 9 | **`imar18-cad-topology`** *(D28 uyarlaması)* | Faz P2, P4 & Faz 15 | **Yalnızca DXF değil; `.dxf`, `.dwg` (`ACadSharp`) ve Netcad `.ncz` (`NczFormatAdapter`)** eksen normalizasyonunu ($X,Y \leftrightarrow Y,X$), $Z=0$ izdüşümünü, iki döngülü bisection ve canonical vertex snapping + `Decimal` Gauss yeniden doğrulamasını zorlar. |
| 10 | `imar18-output-contracts` | Faz P5 & Faz 12/14 | Taslak çıktılarda `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgasını, `RevisionId` onay bağını, `ClosedXML` canlı formüllerini, `QuestPDF` Fen Klasörü ve PARSİD şema kontrollerini zorlar. |
| 11 | **`imar18-security-kvkk`** *(İki evreli uyarlama)* | Pilot P5 (Temel) & **Faz 11–12 (Tam)** | **Pilot'ta:** Sentetik veri sınırı (`AC-PILOT-08`), atomik kayıt ve reddedilen işlem audit kaydını zorlar. **Faz 11–12'de:** SQLCipher, Argon2id DEK/KEK, DPAPI CurrentUser ve Ed25519 hash zincirini devreye alır. |

### 24.4. Beş Özel Alt Ajan (`.claude/agents/*.md`) Profili

1. **`architect.md` (`effort: xhigh`, Salt-Okunur Araçlar: `Read, Grep, Glob`):** Zor geometri (`BisectionEngine`, konkav ada), mevzuat istisnaları ve mimari kararları analiz eder; kod yazmaz, tasarım kararı, risk analizi ve test matrisi üretir.
2. **`implementer.md` (`effort: high`, Araçlar: `Read, Grep, Glob, Bash, Edit, Write`):** Önce başarısız testi yazar, ardından minimum değişiklikle C#/.NET 10 kodunu uygular ve `NetArchTest` + birim testlerini çalıştırır.
3. **`reviewer.md` (`effort: high`, Araçlar: `Read, Grep, Glob, Bash`):** Kod değişikliklerini bağımsız gözle inceler; `double` sızıntısı, NTS sızıntısı, eksik negatif test veya Pilot kapsamı ihlali varsa `Request changes / Block` kararı verir.
4. **`test-engineer.md` (`effort: high`, Araçlar: `Read, Grep, Glob, Bash, Edit, Write`):** FR/AC/TC ve `test-data/synthetic/` altın veri setine göre unit, integration, architecture ve pilot doğrulama testlerini yazar.
5. **`docs-writer.md` (`effort: low`, Araçlar: `Read, Grep, Glob, Write`):** Hesap veya mevzuat yorumu yapmadan yalnızca sürüm notu, changelog, UI metinleri ve faz raporlarını hazırlar.

### 24.5. Faz Bazlı Plugin ve MCP Yükleme Yol Haritası (Bağlam Şişmesini Önleyen Strateji)

Her aktif plugin bağlam penceresinde (context window) token tükettiği için "hepsini ilk gün aktif et" yerine **Her Zaman Aktif Çekirdek Set** + **Faz Bazlı Aktif Set** stratejisi uygulanır. Üçüncü parti bilinmeyen marketplace'ler güvenlik nedeniyle kesinlikle kurulmaz; yalnızca resmî `anthropics/claude-plugins-official` ve `anthropics/skills` kaynakları kullanılır.

| Evre / Faz | Claude Code Plugin'leri (`claude-plugins-official`) | Google Antigravity Karşılığı (`mcp_config.json` & Yerel Özellikler) | Kullanım Amacı |
|---|---|---|---|
| **Her Zaman Aktif (Faz P0'dan itibaren)** | `csharp-lsp`, `claude-md-management`, `commit-commands`, `security-guidance`, `skill-creator` | Yerel C# LSP (OmniSharp/Roslyn), `AGENTS.md`/`GEMINI.md` yönetimi, yerleşik güvenlik taraması | C# 14 tip farkındalığı, tehlikeli dosya/kod deseni uyarısı, standart commit ve kural yönetimi |
| **Faz P1 – P3 (Domain & Geometri)** | `feature-dev`, `pr-review-toolkit`, `code-review`, `code-simplifier` | `.agents/skills/` altındaki `imar18-*` becerileri + çoklu ajan inceleme akışı | `Fraction`, DOP/tecviz ve `BisectionEngine` karmaşıklığını düşürme, 6 yönlü PR ve tip incelemesi |
| **Faz P4 (CAD: DXF/DWG/NCZ & UI)** | `context7`, `frontend-design` | `mcp_config.json` içine `context7` MCP sunucusu + Avalonia UI bileşen becerisi | `ACadSharp`, `NetTopologySuite`, `ProjNet`, `PdfPig` ve `Avalonia 11` güncel sürüm dokümantasyonunu çekme |
| **Faz P5 – P7 (Çıktı Merkezi & CI)** | `context7`, `pr-review-toolkit`, `github` (+ `anthropics/skills` içinden `pdf` ve `xlsx` referansları) | `mcp_config.json` içine `context7` ve `github` MCP sunucuları | `QuestPDF`, `ClosedXML` formül doğrulaması, PARSİD şema kontrolü ve PR/CI takibi |
| **Faz 11 – 20 (Pilot Sonrası)** | `security-guidance`, `pr-review-toolkit`, `code-review` | `imar18-security-kvkk` (Tam mod) + güvenlik denetim ajanı | SQLCipher, Argon2id, DPAPI, Ed25519, `.imr18.lock` ve OCR güvenlik denetimi |
## 25. Gerçek Saha Projeleri (Proje 1, 2 ve 3) Analizi ve Bağlayıcı Saha Kuralları (D31)

Harita mühendisliği saha uygulamasından alınan 3 gerçek İmar Kanunu 18. Madde projesi (`1.zip`: 901 Ada 1–2 Parseller, `2.zip`: 902 Ada 1 Parsel [5 Ada, 40+ Hissedar], `3.zip`: 903 Ada 1 Parsel [3 Ada + Park Alanı]) incelenerek aşağıdaki **9 bağlayıcı saha ve cetvel kuralı (`D31`)** spesifikasyona eklenmiştir:

### 25.1. Resmi DOP / OKO Hesap Bağıntısı ($R_1, D_1, R_2, D_2, R_3$) ve `.CKS` Formatı
Saha projelerindeki (`DOP.CKS`, `DOP.pdf` ve `ÖZET CETVELİ.pdf`) resmi gösterimle birebir uyumlu olarak `DopCalculator` aşağıdaki genişletilmiş formülü ve değişken adlarını kullanır:

$$\text{DOPO} = \frac{R_1 - D_1 - R_2 + D_2 + R_3}{R_1 - D_1}, \qquad \text{OKO} = \frac{\sum A_{\text{ortak\_katilim}}}{R_1 - D_1}$$

- **$R_1$ ("Düzenlemeye Giren Alanlar"):** Düzenlemeye giren parsel alanları toplamı.
- **$D_1$ ("Eski DOPO > Proje DOPO Alanlar"):** Eski DOP oranı proje DOP oranından büyük olan parsellerin alanı.
- **$R_2$ ("İmar Alanları"):** Yalnızca imar parsellerinin (tahsis) alan toplamıdır; PARK, yol, dini tesis, belediye hizmet alanı $R_2$'ye GİRMEZ. Kamuya ayrılan alan $= R_1 - R_2$ olarak TÜRETİLİR (kullanıcı girdisi değil). Pilot varsayılanı $D_1 = D_2 = R_3 = 0$; sıfırdan farklı senaryolar `SORU-008` ile açık kalır.
- **$D_2$ ("Eski DOPO > Proje DOPO Tahsisler"):** Eski DOPO > Proje DOPO olan parsellere yapılan tahsis alanı.
- **$R_3$ ("DOP Alınmayan Alanlar"):** Düzenlemeye giren ancak DOP alınmayan alanlar.
- **Proje-C (`PARK` Kuralı):** `PARK (400.00 m²)` $R_2$'nin dışındadır; kamuya ayrılan alana ($R_1 - R_2$) dahildir.
- **Resmî Referans Kuralı (Proje Sahibi Kararı, `SORU-014` Kapandı, 15 Kasım 2026):** Netcad'in **DOP sayfası (özet cetveli) değerleri referanstır** ($R_2$, DOPO ve kamuya ayrılan alan). Dengeleme cetvelindeki imar parsel alanları toplamı DOP sayfasındaki $R_2$'den farklıysa (Proje-C: $12059.10$ ve $12059.06$, fark $0.04\text{ m}^2$) motor DOP sayfasındaki değere göre doğrular ve farkı **uyarı** olarak raporlar; farkı parsel alanlarına dağıtmaz. Bu kural bir **proje kararıdır**: Arazi ve Arsa Düzenlemeleri Hakkında Yönetmelik (RG 22.02.2020/31047) DOPO'yu 'toplam DOP miktarının düzenlemeye giren parsellerin toplam yüzölçümüne oranı' olarak tanımlar ve yedi basamak ister; iki cetvel arasındaki farkın hangisiyle kapanacağını bulduğum metin parçalarında belirtmez (doğrulanmadı). Proje-A ve Proje-B'de fark yoktur.
- **`.CKS` Çıktı/Girdi Desteği:** Sistem, Netcad İmar uyumlu `.CKS` DOP özet dosyasını (`CP857` / `UTF-8`) okuyabilir ve üretebilir.

### 25.2. Tescil Sayfası "Alan $\times 100$ ($\text{dm}^2$) Paydalı Hisse" ve `TAM` Gösterimi Kuralı
- **Müstakil Tahsisler:** Tek malikli imar parsellerinde hisse sütununa `1/1` yerine resmî cetvel standardı olan **`TAM`** yazılır (`2/TESCİL SAYFASI-1..5.pdf`).
- **Müşterek Tahsislerde $\text{dm}^2$ Payda Kuralı:** Birden fazla malikin hisseli olarak girdiği imar parsellerinde (Örn. Proje 2 `101/1` parsel, Alan = $3539{,}74\text{ m}^2$), tescil sayfasında ortak payda parsel alanının 100 katı ($353974\text{ dm}^2$) olarak alınır ve her malikin hissesi $\text{NetTahsis}_{i} \times 100 / (\text{ParselAlanı} \times 100)$ (Örn. `66387/353974`, `7334/353974`) şeklinde tam sayı pay/payda olarak yazılır; toplam pay ile payda arasındaki $\pm 1\text{ dm}^2$ fark **Largest Remainder** ile en büyük kalanlı hissedarda dengelenir.

### 25.3. Geçici İmar Parsel Kodu (`902/1/C1`) $\leftrightarrow$ Kalıcı Tescil Kodu (`103/1`) Çift Kimlik Yapısı
- Hesap, rölöve ve alan dengeleme aşamasında yeni oluşan parseller **`<KadastroAda>/<KadastroParsel>/<AdaHarfi><SıraNo>`** (Örn. `902/1/A1`, `902/1/B1..B8`, `902/1/C1..C10`, kamuya terk yol için `902/1/Y1`, park için `903/1/PARK`) geçici kimliğiyle izlenir; `Özet Cetveli`, `Tescile Esas Dağıtım Cetveli`, `Fen Klasörü` ve `Tescil Sayfası` çıktılarında ise otomatik olarak **`<YeniİmarAdaNo>/<YeniParselNo>`** (Örn. `101/1`, `102/1..8`, `103/1..10`) kalıcı tescil kimliğine eşlenir.

### 25.4. Dengeleme Alan Hesabında Tecviz İçi Farkın `Y1` (Yol Terk) Alanında Dengelenmesi
- Kadastro tapu alanı ile Gauss hesap alanı arasındaki tecviz sınırları içindeki küçük fark (Örn. Proje 1: $+0{,}12\text{ m}^2$, Proje 2: $+0{,}20\text{ m}^2$, Proje 3: $+1{,}97\text{ m}^2$), oluşan yeni imar parsellerinin tam/hedef alanlarını (`430.00 m²`, `450.00 m²` vb.) bozmamak için imar parsellerine dağıtılmaz (`Düzeltme = +0.00`); **kamuya terk edilen yol (`Y1`) alanı üzerinde dengelenir**.

### 25.5. Üçlü Alan Hesap Cetveli Seti (`Noktalı`, `Cepheli`, `Dengeleme`) ve `Koordine Özet`
Fen Klasörü üretiminde tek bir alan tablosu yerine birbirini doğrulayan 4 teknik cetvel üretilir:
1. **Koordine Özet (`ITRF-96`):** Nokta No, $Y$ (Sağa), $X$ (Yukarı) koordinat listesi.
2. **Noktalı Alan Hesabı:** Ada, Parsel, Hesap Alanı ve poligonu oluşturan sıralı köşe nokta numaraları dizisi (`59, 60, 61, 77, 78...`).
3. **Cepheli Alan Hesabı:** Her parsel için ardışık köşe noktaları, $(Y, X)$ koordinatları ve iki köşe arası kenar uzunluğu (**`Cephe` sütunu**) ile Gauss alanı.
4. **Dengeleme Alan Hesabı:** Ada/Parsel, Hesaplanan Alan, Düzeltme, Dengelenmiş Alan ve alt toplamda `Tapu Alanı / Hesaplanan / Fark / Tecviz` karşılaştırma kutusu.

### 25.6. İki Yönlü Dağıtım Cetveli ve `He. / M2 / Dm2` Alan Ayrımı
1. **Mal Sahipleri Araştırma ve Özet Formu (Kadastro Parseline Sıralı):** Kadastro malik sırasına göre her malikin hangi imar parsellerine (`101/1`, `102/1`...) ne kadar payla gittiğini gösterir.
2. **Tescile Esas Dağıtım Cetveli (İmar Parseline Sıralı):** Yeni oluşan her imar parselinin altında hangi kadastro maliklerinin hangi hisse ve payla toplandığını gösterir.
3. **`He. | M2 | Dm2` Sütun Formatı:** `Fen Klasörü` ve `Tescil Sayfası` çıktılarında yüzölçümü tek sütun yerine resmî şablona uygun olarak **Hektar (`He.`), Metrekare (`M2`) ve Desimetrekare (`Dm2`)** (Örn. $3539{,}74\text{ m}^2 \to \text{He: } 0,\; \text{M2: } 3539,\; \text{Dm2: } 74$) olarak 3 ayrı sütunda basılır.

### 25.7. Netcad 8.5 (`.NCZ`) Gerçek Katman Sözleşmesi ve Gömülü `<NetmapManager>` XML Okuma
`NczFormatAdapter`, Netcad 8.5 (`8.5.0.1654`) dosyalarındaki gömülü PNG önizlemesini (`IHDR..IEND`), `<NetmapManager>` XML proje üstverisini (`_IL`, `_ILCE`, `_MAHALLE`, `_PAFTA`, `_FEN_KAYITNO`, `GeoParselDef`) ve sahada kullanılan standart katman adlarını (`TK_PARSEL_GİREN`, `TK_PARSEL_OLUŞAN`, `TK_PARSEL_YOLATERK`, `DÜZENLEME_SINIRI`, `ADA_KENARLT`, `PLAN_ADAKENARI`, `PL_PARK`, `KADASTRO_HAT`, `NOKTA_PARSEL_YENI`, `TESCİLLİ_NOKTA`) doğrudan tanır.


#### 25.7.1. `.NCZ` Anonimleştirme ve Sentetik Koordinat Fixture Üretim Aracı Gereksinimi (`FR-F6.12` / `Faz P6.0` — D15)
- Gerçek `.NCZ` dosyalarından güvenli test fixture'ı üretmek üzere `Faz P6.0` kapsamında yazılacak anonimleştirme aracı şu işlemleri zorunlu olarak uygular *(Not: P0 aşamasında yalnızca gereksinim olarak belgelenmiştir, araç kodu P6.0'da yazılacaktır)*:
  1. `.NCZ` içindeki `<NetmapManager>` XML bloğunda yer alan tüm kişi, kurum, adres ve kayıt no alanları (`_BASVURAN_KISI`, `_BILIRKISI*`, `_HARITA_MUDURU`, `_KAD_TEK*`, `_KURUM_ADRESI`, `_APL_*` vb.) tamamen boşaltılır veya sentetik yer tutucuya çevrilir.
  2. Gömülü PNG önizleme bloğu (`IHDR..IEND`) silinir.
  3. Tüm köşe koordinatları sabit bir yerel ofsetle $(\Delta Y, \Delta X)$ kaydırılır (alan ve iç açılar değişmez).
  4. Üretilen anonim çıktı yalnızca `test-data/synthetic/` altına yazılır.

### 25.8. AAD Yönetmeliği Md. 17/1-(d) Muvafakatlı Hisse Çözümlemesi (Fiili Kullanım Esası)
Çok malikli müşterek kadastro parsellerinde (Proje 2 ve 3 `TEKNİK RAPOR.pdf`), maliklerin belediyeye verdikleri muvafakat ve fiili zemin kullanım durumlarına göre iştirak grupları oluşturularak müstakil veya gruplu imar parseli tahsisi (`Hisse Çözümlemesi`) yapılabilir.

### 25.9. Standart Resmi Evrak Şablonları (`FR.348`, `FR.521 / EK-17` ve Dönüşüm Parametreleri)
`QuestPDF` çıktı motoru; **Değişiklik Haritaları Yapım ve Kontrol Bilgileri Kapağı (`FR.348 / EK-2`)**, **Rölöve Ölçü Krokisi (`FR.521 / EK-17`)**, **2B Helmert/Afin Dönüşüm Parametreleri (`A, B, C, D, CX, CY, Ölçek, Dönüklük, Vy, Vx`)** ve **Belediye Encümen Kararı / Askı İlanı / Teknik Rapor** taslak şablonlarını standart set olarak içerir.
