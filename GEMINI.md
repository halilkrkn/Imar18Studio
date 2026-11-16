# GEMINI.md — Google Antigravity (Gemini + Claude Modelleri) Özel Çalışma Talimatı

> **Zorunlu Ön Okuma:** Her görevin başında önce ortak anayasa olan `AGENTS.md` dosyasını, `Imar18Studio_Spesifikasyon_v2_19.md` spesifikasyonunu ve anlık kilit/faz durumunu tutan `docs/STATUS.md` dosyasını oku.

## 1. Google Antigravity ve Claude Code Tam Uyumluluk Yapısı (`.agents/` ↔ `.claude/`)

Google Antigravity 2.0, açık `SKILL.md` standardını ve `.agents/` çalışma alanı dizinini yerel olarak destekler (`D30`):

- **`.agents/rules/*.md` (`.claude/rules/` ile senkronize):** `architecture.md`, `testing.md`, `security.md` ve `model-routing.md` kuralları YAML frontmatter (`trigger: always_on` veya `trigger: model_decision`) ile Antigravity tarafından otomatik yüklenir.
- **`.agents/skills/*/SKILL.md` (`.claude/skills/` ile senkronize):**
  - İş akışı becerileri: `start-pilot-phase`, `run-pilot-tests`, `review-pull-request`, `close-pilot-phase`.
  - Mühendislik becerileri: `imar18-tdd-dotnet`, `imar18-domain-rules`, `imar18-architecture`, `imar18-mevzuat-profil`, `imar18-cad-topology` (`.dxf`, `.dwg`, `.ncz`), `imar18-output-contracts`, `imar18-security-kvkk`.
- **MCP Sunucu Eşlemesi (`mcp_config.json`):** Claude Code'da kullanılan `context7` (güncel `ACadSharp`, `NetTopologySuite`, `ProjNet`, `PdfPig`, `Avalonia`, `QuestPDF`, `ClosedXML` dokümantasyonu) ve `github` eklentileri, Google Antigravity içinde `mcp_config.json` üzerinden aynı işlevle çalıştırılır.

## 2. Google Antigravity Rolü ve Odak Alanı (Evre 1: Pilot P0–P7)

1. **Faz P0 — Gerçek Saha Projelerini Sentetik Altın Veri Setine (`test-data/synthetic/`) Dönüştürme:**
   - Proje sahibinin harita mühendisinden aldığı gerçek projeler incelenirken kişisel veriler (Malik Adı, TC Kimlik No, Cilt/Sayfa) anonimleştirilir ve koordinatlar sabit yerel ofsetle kaydırılarak `test-data/synthetic/pilot-1..4/` altına kaydedilir (`AC-PILOT-08`).
2. **Faz P4 — Çoklu CAD Girdi/Çıktı (`Imar18.Infrastructure` — `FR-F2.9` / `D28`):**
   - `ICadFormatAdapter` üzerinden `ACadSharpCadAdapter` ile **`.dxf`** ve **`.dwg`** dosyalarının katmanlı okunması/yazılması (`netDxf` yedek adaptörüyle).
   - `NczFormatAdapter` (`NativeNczBinaryParser`) ile **Netcad `.ncz`** dosyalarından tabaka, nokta, çoklu doğru/parsel poligonu, yazı ve ada/parsel özniteliklerinin okunması/yazılması (`Windows-1254`/`CP857`/`UTF-8`).
   - `UglyToad.PdfPig` ile vektörel PDF / örnek TAKBİS verilerinin okunması.
3. **Faz P4 — Masaüstü UI ve CAD Tuvali (`Imar18.Presentation.Avalonia`):**
   - Avalonia UI 11 + `Semi.Avalonia` koyu tema ekranları, `*.dxf;*.dwg;*.ncz` dosya diyalogları ve temel klavye/odak/etiket erişilebilirliği.
   - `SkiaSharp` tabanlı 2D CAD önizleme ve çizim tuvali (60 FPS pan/zoom hedefi).
   - 50 ms üzeri işlemler için `IProgress<T>` ve `CancellationToken` ile arka plan yürütmesi.
4. **Faz P5 — Onay, Temel Audit, Atomik Kayıt ve Taslak Çıktılar (`Imar18.Application` & `Imar18.Infrastructure`):**
   - `CommandBus` üzerinden mutasyon ve başarılı işlem audit kaydının aynı transaction içinde tutulması; reddedilen işlemlerin ve onaysız girişimlerin de kaydedilmesi.
   - `ApprovalService`: Onayın hesaplanan `RevisionId` değerine bağlanması ve veri değiştiğinde onayın otomatik düşmesi (`AC-PILOT-06`).
   - `ClosedXML` ile taslak Excel cetvelleri ve `QuestPDF` ile `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` damgalı taslak Fen Klasörü PDF + checksum üretimi.

## 3. Antigravity İş Akışı ve Raporlama Kuralları

1. **Kilit ve Raporlama (`docs/STATUS.md`):**
   - Göreve başlarken `docs/STATUS.md` kontrol edilir, kilit kaydı girilir ve `Imar18Studio_Spesifikasyon_v2_19.md` §23.2'deki `[GÖREV BAŞLANGIÇ RAPORU]` üretilir.
   - Görev tamamlandığında test sonuçlarıyla birlikte `[GÖREV SONU RAPORU]` üretilir ve kilit kaldırılır.
2. **Pilot Kapsam Kilidi:**
   - `docs/STATUS.md` içinde aktif faz `EVRE 1: PİLOT (FAZ P0–P7)` olduğu sürece Faz 11–20 arasındaki özellikler (SQLCipher/Argon2id/DPAPI, Ed25519 imzalama, `.imr18.lock`, `Tesseract` OCR, tam WCAG sertifikasyonu, Faz 18 ürün içi LLM, Faz 19 telemetri, Faz 20 üretim kod imzalama) kodlanmaz.

## 4. Paket Sürümü Kilidi ve NuGet Hata Protokolü (`NU1101` / `NU1102` / `NU1103` — KESİN KURAL)

* **Paket sürümü hatası (`NU1101`/`NU1102`/`NU1103` vb.) olursa sürümü KENDİN DEĞİŞTİRME.**
* Önce hatanın tam metnini, paket adını ve istenen sürümü proje sahibine raporla; NuGet'te mevcut sürümleri listele (`dotnet package search <paket> --exact-match` veya `nuget.org`) ve önerini tek satırda yaz.
* **Proje sahibinin açık onayı olmadan `Directory.Build.props` ve `.csproj` dosyalarındaki `PackageReference` sürümlerini kesinlikle değiştirme.**

## 5. Faz ve Görev Sonu Bağlam/Token Koruma Kuralı (ZORUNLU ÇIKTI KURALI)

> **KURAL:** Bir geliştirme fazının tamamı (tüm alt adımlarıyla) tamamlandığında, gereksiz bağlam birikimini ve token tüketimini önlemek için yanıtının en sonuna mutlaka şu uyarıyı ekle:
> **🛑 FAZ TAMAMLANDI: Token tasarrufu ve temiz bağlam için şimdi mevcut sohbeti kapatıp yeni bir sohbet oturumu açın.**

