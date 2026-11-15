# CLAUDE.md — Claude Code (CLI / IDE Eklentisi) Özel Çalışma Talimatı

> **Zorunlu Ön Okuma:** Her oturumun başında önce ortak anayasa olan `AGENTS.md` dosyasını, `Imar18Studio_Spesifikasyon_v2_19.md` spesifikasyonunu ve anlık kilit/faz durumunu tutan `docs/STATUS.md` dosyasını oku.

## 1. Claude Code Klasör, Skill ve Alt Ajan Yapısı (`.claude/`)

Claude Code bu depoda aşağıdaki yerel yapılandırmayı kullanır ve `.agents/` dizini üzerinden **Google Antigravity** ile %100 uyumlu çalışır:

* **`.claude/settings.json` (D9):** `dotnet restore/build/test/format/list package` ile dal içi yerel `git status/diff/log/branch/checkout/switch/add/commit/merge --no-ff` komutlarına izin verir; **`git push` (`Bash(git push:*)`), `git reset --hard`, `dotnet publish`, `WebFetch` ve `WebSearch` işlemlerini kesin olarak engeller.** (`main` dalına merge ve `git push` yalnızca proje sahibi tarafından yapılır.)

* **`.claude/rules/*.md` (`.agents/rules/` ile ortak):** `architecture.md`, `testing.md`, `security.md` ve `model-routing.md`.

* **`.claude/agents/*.md`:**

  * `architect`: Zor geometri (`BisectionEngine`), mevzuat istisnaları ve mimari risk analizi (`effort: xhigh`, salt-okunur: `Read, Grep, Glob`).

  * `implementer`: Katı TDD (Red-Green-Refactor) ile C# 14 / .NET 10 kodlama (`effort: high`).

  * `reviewer`: Bağımsız PR, mimari, güvenlik ve Pilot kapsam ihlali incelemesi (`effort: high`).

  * `test-engineer`: xUnit birim, mimari ve `Imar18.Pilot.Tests` test matrisi üretimi (`effort: high`).

  * `docs-writer`: Sürüm notu, changelog ve rapor yazımı (`effort: low`).

* **`.claude/skills/*/SKILL.md` (`.agents/skills/` ile ortak):**

  * İş akışı becerileri: `/start-pilot-phase`, `/run-pilot-tests`, `/review-pull-request`, `/close-pilot-phase`.

  * Mühendislik becerileri: `imar18-tdd-dotnet`, `imar18-domain-rules`, `imar18-architecture`, `imar18-mevzuat-profil`, `imar18-cad-topology` (`.dxf`, `.dwg`, `.ncz`), `imar18-output-contracts`, `imar18-security-kvkk`.

* **`.claude/commands/*.md`:** `/pilot-status` ve `/pilot-next`.

## 2. Faz Bazlı Plugin Politikası (Bağlam Şişmesini Önleme Kuralı)

Yalnızca resmî `anthropics/claude-plugins-official` ve `anthropics/skills` kaynakları kullanılır; bilinmeyen üçüncü parti marketplace'ler kurulmaz:

1. **Her Zaman Aktif Çekirdek Plugin'ler:** `csharp-lsp`, `claude-md-management`, `commit-commands`, `security-guidance`, `skill-creator`.

2. **Faz Bazlı Aktif Edilecekler:**

   * **Faz P1–P3 (Domain & Geometri):** `feature-dev`, `pr-review-toolkit`, `code-review`, `code-simplifier`.

   * **Faz P4 (Çoklu CAD: DXF/DWG/NCZ & Avalonia UI):** `context7` (`ACadSharp`, `NTS`, `ProjNet`, `PdfPig` güncel sürüm dokümantasyonu için), `frontend-design`.

   * **Faz P5–P7 (Çıktı Merkezi & CI):** `context7`, `pr-review-toolkit`, `github` (+ `anthropics/skills` içinden `pdf` ve `xlsx` şablon referansları).

## 3. Çalışma Sırası ve Kritik Görev Protokolü

Her görevde şu 10 adımı izle:

 1. İlgili FR, AC, TC, pilot senaryosu ve `ERR-*` hata kodunu bul; `docs/STATUS.md` kilidini kontrol et ve `[GÖREV BAŞLANGIÇ RAPORU]` üret.

 2. Mevcut kodu ve testleri incele.

 3. Eksik veya çelişkili gereksinim varsa varsayım yapma; `docs/STATUS.md` → **Açık Sorular** tablosuna yaz ve kullanıcıya sor.

 4. Kritik görevlerde (Gauss alanı, tecviz, DOP, mahsup, `Fraction`/`Largest Remainder`, `BisectionEngine`, kuantizasyon, CRS, `.dxf`/`.dwg`/`.ncz` dönüşümü, onay, audit, PARSİD/Fen Klasörü) önce `architect` alt ajanıyla tasarım/risk analizi yap.

 5. `imar18-tdd-dotnet` becerisine göre önce başarısız xUnit testlerini yaz (`test-engineer` / `implementer`).

 6. Minimum değişiklikle C# kodunu uygula (`implementer`).

 7. Hedefli testleri, `Imar18.Architecture.Tests` ve `Imar18.Pilot.Tests` kontrollerini çalıştır (`0 test` çalışmasını hata say).

 8. Kritik değişikliklerde `reviewer` alt ajanıyla bağımsız inceleme yap.

 9. `docs/STATUS.md` kilidini kaldır ve `[GÖREV SONU RAPORU]` üret.

10. Proje sahibi onayı olmadan kapsam genişletme veya sonraki faza geçme.

## 4. Paket Sürümü Kilidi ve NuGet Hata Protokolü (`NU1101` / `NU1102` / `NU1103` — KESİN KURAL)

* **Paket sürümü hatası (`NU1101`/`NU1102`/`NU1103` vb.) olursa sürümü KENDİN DEĞİŞTİRME.**
* Önce hatanın tam metnini, paket adını ve istenen sürümü proje sahibine raporla; NuGet'te mevcut sürümleri listele (`dotnet package search <paket> --exact-match` veya `nuget.org`) ve önerini tek satırda yaz.
* **Proje sahibinin açık onayı olmadan `Directory.Build.props` ve `.csproj` dosyalarındaki `PackageReference` sürümlerini kesinlikle değiştirme.**

## 5. Faz ve Görev Sonu Bağlam/Token Koruma Kuralı (ZORUNLU ÇIKTI KURALI)

> **KURAL:** Herhangi bir geliştirme fazı veya görevi tamamlandığında, gereksiz bağlam birikimini ve token tüketimini önlemek için yanıtının en sonuna mutlaka şu uyarıyı ekle:
> **🛑 FAZ TAMAMLANDI: Token tasarrufu ve temiz bağlam için şimdi mevcut sohbeti kapatıp yeni bir sohbet oturumu açın.**

