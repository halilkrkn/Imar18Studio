# AGENTS.md — Imar18Studio Ortak Ajan Anayasası (v2.19 Birleştirilmiş / D27–D30)

Bu dosya, **Imar18Studio** projesinde çalışan tüm yapay zekâ ajanları (**Google Antigravity [Gemini + Claude modelleri]** ve **Claude Code [CLI / Eklenti]**) için bağlayıcı ortak talimat kaynağıdır.

Araca özel çalışma farkları `CLAUDE.md` ve `GEMINI.md` dosyalarında, anlık dosya kilitleri, aktif faz durumu (`Faz P0–P7` vs. `Faz 11–20`) ve açık sorular ise `docs/STATUS.md` dosyasında tutulur. **Bu dosya yalnızca proje sahibinin açık onayıyla değiştirilebilir.**

## 1. İki Evreli Proje Stratejisi ve Aktif Faz Kilidi (D29)

Proje, `Imar18Studio_Spesifikasyon_v2_19.md` §22 uyarınca **Evre 1: PİLOT (Faz P0 – Faz P7)** ve **Evre 2: PİLOT SONRASI (Faz 11 – Faz 20)** olarak ikiye ayrılmıştır.

> **AKTİF FAZ KİLİDİ:** Şu anda proje **`EVRE 1: PİLOT (FAZ P0–P7)`** modundadır. Proje sahibi `docs/STATUS.md` dosyasında aktif fazı `EVRE 2: PİLOT SONRASI (FAZ 11–20)` olarak değiştirene kadar **hiçbir ajan Faz 11–20 kapsamındaki özellikleri kodlayamaz.** Bu özellikler için yalnızca açıkça `"Pilot Dışı / Faz 11+ Stub"` olarak işaretlenmiş arayüzler bırakılabilir; sahte (fake) implementasyon yazılamaz.

### 1.1. Evre 1: PİLOT Kapsamı (Faz P0 – Faz P7)

| Faz | Kapsam | Aktif Özel Skill'ler (`SKILL.md`) | Çıkış Ölçütü |
|---|---|---|---|
| **P0** | Spesifikasyon, RTM, sentetik veri politikası (`test-data/synthetic/`), `Imar18Studio.sln` (6 `src` + 6 `tests` projesi), `.claude`/`.agents` ortak dizinleri ve erken Public GitHub CI kurulumu | `start-pilot-phase`, `imar18-tdd-dotnet`, `imar18-architecture` | Mimari kurallar (`NetArchTest`) ve çalışan CI |
| **P1** | Sayısal Çekirdek: `Decimal` sözleşmesi, `BigInteger` tabanlı `Fraction`, deterministik `Largest Remainder` (eşit kalanda kararlı sıra) ve çekirdek `ErrorCatalog` | `imar18-domain-rules`, `imar18-tdd-dotnet` | Bağımsız referanslarla doğrulanan birim testler |
| **P2** | Geometri Motoru: Eksen takası (`CoordinateAxisSwapAdapter`), yerel orijinli Gauss alanı, iki döngülü `BisectionEngine` (75°–105°), kuantizasyon (`Decimal` Gauss yeniden doğrulamalı) ve konkav ada kontrolü | `imar18-cad-topology`, `imar18-domain-rules` | Topoloji, `MultiPolygon` güvenli duruşu ve alan korunumu (`TC-GEOM-001`) |
| **P3** | Uzman Onaylı Mevzuat Motoru: `TecvizChecker` (Md. 8/2), `HataCemberiHesaplayici` (Md. 8/5), `DopCalculator` (%45 sınırı, mahsup ve aşım karşılama sırası) | `imar18-mevzuat-profil`, `imar18-domain-rules` | İstisna ve sınır testleri (`TC-TECVIZ-001..002`, `TC-DOP-001..007`) |
| **P4** | Çoklu CAD Girişi (`ICadFormatAdapter`: **`.dxf`, `.dwg`, `.ncz`** — D28), desteklenen CRS (`ITRF96 TM 3°` / `ED50`), vektörel/manuel TAKBİS girişi ve temel Avalonia + SkiaSharp UI (60 FPS pan/zoom) | `imar18-cad-topology`, `imar18-architecture` | Eksen normalizasyonu (`TC-CAD-001`), girdi doğrulama ve manuel Windows kontrolü |
| **P5** | Mühendis Onayı (`RevisionId` bağlı; veri değişirse onay düşer), temel Audit (reddedilen girişimler dahil), atomik kayıt, `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgalı taslak PDF/Excel ve PARSİD sözleşmesi | `imar18-output-contracts`, `imar18-security-kvkk` (Pilot modu) | Onay-revizyon-checksum tutarlılığı (`AC-PILOT-04..06`) |
| **P6** | Dört Pilot Senaryosu (`Imar18.Pilot.Tests` + `test-data/synthetic/pilot-1..4/`), saha kullanımı ve eksiklerin kapatılması | `run-pilot-tests`, `review-pull-request` | Kritik bulgu olmaması ve uzman/mühendis incelemesi (`AC-PILOT-01..04`) |
| **P7** | CI kapılarının tam doğrulaması, sentetik fixture denetimi ve Pilot Kapanış Raporu | `close-pilot-phase` | Otomatik kontroller + manuel Windows doğrulaması (`AC-PILOT-07..10`) |

### 1.2. Evre 2: PİLOT SONRASI Kapsamı (Faz 11 – Faz 20 — Şu Anda Kilitli)

- **Faz 11 (`AC-SEC-01..08`):** SQLCipher, `.imr18` şifreli konteyner, rastgele DEK, Argon2id KEK, DPAPI CurrentUser koruması.
- **Faz 12 (`AC-AUDIT-01..10`):** SHA-256 hash zinciri, Ed25519 imzalama, zaman damgası, atomik transaction, bozulmada ReadOnly mod.
- **Faz 13 (`AC-LOCK-01..07`):** `.imr18.lock`, 60 sn stale lock devralma, ikinci oturumda ReadOnly mod (**Pilot 5**).
- **Faz 14 (`AC-VER-01..06`):** `MevzuatProfili` sürümleme, eski profil arşivi, checksum/imza doğrulama, PARSİD/Fen Klasörü şema kapıları.
- **Faz 15 (`AC-DXF-01..07`):** Gelişmiş katmanlı `.dxf`/`.dwg`/`.ncz` yazma, etkileşimli topoloji onarım sihirbazı, snap ve epok kontrolleri.
- **Faz 16 (`AC-OCR-01..06`):** Yerel `Tesseract` (`tur.traineddata`), SHA-256 doğrulama ve OCR çıktısının zorunlu mühendis doğrulamasına tabi tutulması.
- **Faz 17 (`AC-A11Y-01..07`):** Tam WCAG 2.2 AA, NVDA/Narrator ekran okuyucu, klavye tuzağı ve 4.5:1 kontrast testleri.
- **Faz 18 (`AC-AI-01..10`):** Ürün içi doğal dil / LLM niyet analizi ve açıklama üretimi (`GuardrailService` + JSON şema; **LLM asla hesap yapmaz, mevzuat kararı vermez, dosyaya yazmaz**).
- **Faz 19 (`AC-TEL-01..06`):** İsteğe bağlı (opt-in), 90 gün saklama, SHA-256 `crashStackHash`, kişisel veri göndermeme ve kapatabilme.
- **Faz 20 (`AC-REL-01..07`):** Üretim kod imzalama, `ProductionGate`, GitHub Release ve geri alma planı.

## 2. Çift Uyumlu (Claude Code + Google Antigravity) Klasör ve Skill Mimarisi (D30)

1. **Ortak Skill ve Kural Dizinleri (`.claude/` ↔ `.agents/`):**
   - Claude Code `.claude/rules/*.md` ve `.claude/skills/*/SKILL.md` dizinlerini; Google Antigravity ise `.agents/rules/*.md` ve `.agents/skills/*/SKILL.md` dizinlerini okur.
   - Her iki dizin de açık `SKILL.md` standardını (`name` ve `description` YAML frontmatter) ve kural frontmatter'ını (`trigger: always_on` / `model_decision`) kullandığı için Windows'ta `mklink /J .agents\skills .claude\skills` ve `mklink /J .agents\rules .claude\rules` ile birebir senkronize tutulur.
2. **Gerçek Saha Projelerini Anonimleştirme Protokolü (`test-data/synthetic/`):**
   - Proje sahibinin harita mühendisinden aldığı gerçek projeler incelenirken **Malik Adı, TC Kimlik No, Baba Adı, Cilt/Sayfa ve gerçek coğrafi konum ipuçları** temizlenir; koordinatlar sabit bir yerel ofsetle ($\Delta X, \Delta Y$) kaydırılarak `test-data/synthetic/pilot-1..4/` altına sentetik altın veri seti olarak kaydedilir (`AC-PILOT-08`). Gerçek ham dosyalar `.gitignore` ile dışarıda tutulur.

## 3. Taviz Verilmeyecek Mimari ve Güvenlik Kuralları

1. **Tip İzolasyonu (`Decimal` ve `Fraction` Zorunluluğu):**
   - `Imar18.Domain` ve `Imar18.Application` içindeki tüm tapu alanı, tecviz, DOP, mahsup, tahsis ve hisse hesaplarında yalnızca `System.Decimal` ve `Fraction(BigInteger Pay, BigInteger Payda)` kullanılır.
   - Kuantizasyon sonrası alan `Decimal` Gauss ile yeniden doğrulanır; topoloji bozulursa çözüm geri alınır.
2. **DTO, Geometri ve CommandBus İzolasyonu:**
   - `NetTopologySuite.Geometries` tipleri `Imar18.GeometryEngine` ve `Imar18.Infrastructure` dışına çıkamaz.
   - Tüm veri mutasyonları `CommandBus` üzerinden yürür; mutasyon ve başarılı işlem audit kaydı aynı transaction içinde tutulur. Reddedilen işlemler ve onaysız girişimler de kaydedilir.
3. **Onay – Revizyon Bağı (`AC-PILOT-06`):**
   - Mühendis onayı hesaplanan `RevisionId` / `StateHash` değerine bağlanır; veri değişirse onay otomatik olarak düşer.
4. **Eksen Sırası Dönüşüm Kapısı (`CoordinateAxisSwapAdapter` ve Çoklu CAD Normalizasyonu):**
   - Türkiye jeodezik sisteminde $X_{\text{jeo}}$ (Kuzey) ve $Y_{\text{jeo}}$ (Doğu) eksenleri NTS Kartezyen kapısında $x_{\text{NTS}} = Y_{\text{jeo}},\, y_{\text{NTS}} = X_{\text{jeo}}$ olarak eşlenir.
   - AutoCAD (`.dxf`, `.dwg`) Kartezyen $(X, Y)$ koordinatları ile Netcad (`.ncz`) Jeodezik $(Y, X)$ koordinatları kendi adaptörlerinde normalize edilir ve $Z \neq 0$ kotları $Z = 0$ yatay izdüşüm düzlemine indirgenir.
5. **Katı TDD ve Sıfır Halüsinasyon:**
   - Her davranış için önce bağımsız referans sonucu belirlenir ve başarısız xUnit testi yazılır; boş test seçimi (`0 test`) başarı sayılmaz.
   - Belirsiz mevzuat veya tasarım noktaları tahmin edilmez, `docs/STATUS.md` → **Açık Sorular** tablosuna yazılır.

## 4. Çoklu Ajan Koordinasyonu, Raporlama ve Kilit Protokolü (D27)

1. **Kilit Kontrolü (`docs/STATUS.md`):** Her ajan oturuma başlarken `AGENTS.md` ve `docs/STATUS.md` dosyalarını okur; başka bir ajanın kilitlediği dosyaya dokunmaz ve kendi çalıştığı `feature/pilot-...` dalını tabloya kaydeder.
2. **Görev Başlangıç ve Görev Sonu Raporu:** Ajanlar her görevde `Imar18Studio_Spesifikasyon_v2_19.md` §23.2'deki `[GÖREV BAŞLANGIÇ RAPORU]` ve `[GÖREV SONU RAPORU]` şablonlarını doldurur.
3. **Birleştirme (Merge) Şartı:** Küçük ve bağımsız PR'lar üretilir; tek PR'da birden fazla kritik özellik birleştirilmez. `develop` veya `main` dalına birleştirme öncesi `dotnet build -c Release -warnaserror`, `dotnet test -c Release`, `Imar18.Architecture.Tests` ve `Imar18.Pilot.Tests` projelerinin %100 yeşil geçmesi zorunludur.


## Git İzin ve Dal Sınırı (D9)
- `.claude/settings.json` teknik olarak yalnızca Claude Code'u bağlar; **Google Antigravity için bağlayıcı kural bu `AGENTS.md` dosyasıdır:** Dal içi yerel `git add/commit/merge --no-ff` serbesttir, ancak **`git push` ve `main` dalına merge kesinlikle yasaktır** (`main`'e merge işlemini yalnızca proje sahibi yapar).

## 5. Paket Sürümü Kilidi (`NU1101`/`NU1102`/`NU1103`) ve Faz Sonu Oturum Yenileme Kuralı (KESİN KURAL)

1. **Paket Sürümü Değiştirmeme Kuralı (`Directory.Build.props` & `.csproj`):**
   - Paket sürümü hatası (`NU1101`/`NU1102`/`NU1103` vb.) olursa sürümü **KENDİN DEĞİŞTİRME**.
   - Önce hatanın tam metnini, paket adını ve istenen sürümü proje sahibine raporla; NuGet'te mevcut sürümleri listele (`dotnet package search <paket> --exact-match` veya `nuget.org`) ve önerini tek satırda yaz.
   - Proje sahibinin onayı olmadan `Directory.Build.props` ve `.csproj` dosyalarındaki `PackageReference` sürümlerini değiştirme.
2. **Faz / Görev Tamamlanma Uyarısı (Token ve Bağlam Koruma):**
   - **KURAL:** Herhangi bir geliştirme fazı veya görevi tamamlandığında, gereksiz bağlam birikimini ve token tüketimini önlemek için yanıtının en sonuna mutlaka şu uyarıyı ekle:
   - **🛑 FAZ TAMAMLANDI: Token tasarrufu ve temiz bağlam için şimdi mevcut sohbeti kapatıp yeni bir sohbet oturumu açın.**

