---
description: Imar18Studio Üretici (Claude Code Pro Extension: Fable 5.2 / Opus / Sonnet) ve Bağımsız Test/Denetçi (Google Antigravity: 18 Kasım Öncesi Opus/Sonnet 4.6 -> 18 Kasım Sonrası Gemini 4 Hydrogen & Gemini 3.8 Flash) Esnek Model Yönlendirme Kuralı.
trigger: model_decision
---
# Esnek Model ve Çift Platform (Üretici – Denetçi) Yönlendirme Rehberi

## 1. Sıfır Kırılma Güvencesi (`model: inherit`)
- Tüm alt ajanlar (`.claude/agents/*` ve `.agents/agents/*`) `model: inherit` olarak yapılandırılmıştır; hiçbir dosya belirli bir model adına teknik olarak bağımlı değildir.
- **18 Kasım Antigravity Model Değişikliği Uyumluluğu:**
  - **18 Kasım Öncesi (13–18 Kasım):** Google Antigravity içinde **Claude Opus 4.6**, **Claude Sonnet 4.6**, **gpt-oss-120b** ve **Gemini 3.8 Flash** doğrudan kullanılabilir. Bu 5 günlük pencerede özellikle `Faz P1`, `Faz P2` ve `Faz P3` çekirdek testlerinde Antigravity içindeki `Opus 4.6 / Sonnet 4.6` kotasından maksimum faydalanılır.
  - **18 Kasım Sonrası:** Google Antigravity'de ücretsiz katmanda Claude ve GPT modelleri kalkıp yalnızca **Gemini 4 Hydrogen** ve **Gemini 3.8 Flash** kaldığında:
    1. **Üretim Kodu (`src/`):** Zaten **Claude Pro** aboneliğinizle çalışan **Claude Code IDE Eklentisi** (`Claude Fable 5.2`, `Claude Opus`, `Claude Sonnet`) üzerinde yazıldığı için üretim kalitesinde **hiçbir değişiklik olmaz**.
    2. **Bağımsız Test, `dotnet test` Koşumu ve Git-Flow Merge (`tests/`):** Antigravity içinde **Gemini 4 Hydrogen** (ana test mühendisi ve denetçi olarak) ve **Gemini 3.8 Flash** (hızlı UI/XAML kontrolü, `STATUS.md` güncellemesi ve `git merge` işlemleri için) kullanılır.
    3. **Derin Mimari İnceleme Yedeği:** 18 Kasım sonrasında çok kritik bir fazda (ör. `Faz 11–12` Kriptografi) Claude tabanlı ikinci göz incelemesi gerekirse, aynı penceredeki **Claude Code Eklentisi** içinde `/code-review` komutu çalıştırılabilir.

## 2. Faz Bazlı Üretici (Claude Code Pro Extension) ve Denetçi (Google Antigravity) Matrisi

| Faz / Görev | Claude Code Pro Extension (Üretici — `src/`) | Google Antigravity (Denetçi — 18 Kasım Öncesi) | Google Antigravity (Denetçi — 18 Kasım Sonrası) |
|---|---|---|---|
| **Faz P1 (`Fraction`, `Largest Remainder`, `ErrorCatalog`)** | **Claude Sonnet** *(Hisse dengelemede **Fable 5.2**)* | **Claude Sonnet 4.6** / **gpt-oss-120b** | **Gemini 4 Hydrogen** (`xUnit` sınır testleri + `dotnet test`) |
| **Faz P2 (`Gauss`, `ProjNet`, 6 Ada `BisectionEngine`)** | **Claude Fable 5.2** *(veya **Claude Opus**)* | **Claude Opus 4.6** | **Gemini 4 Hydrogen** (Topoloji, alan korunumu ve açı testleri) |
| **Faz P3 (`DOPO`, `Tecviz`, `Y1` Dengeleme, Tahsis)** | **Claude Fable 5.2** *(veya **Claude Opus**)* | **Claude Sonnet 4.6** | **Gemini 4 Hydrogen** (`Proje 1..3` altın veri doğrulaması) |
| **Faz P4.1 (`.dxf`, `.dwg`, `.ncz`, `.cks` Adaptörleri)** | **Claude Sonnet** | **Claude Sonnet 4.6** | **Gemini 4 Hydrogen** (Gerçek `.NCZ`/`.CKS` okuma testleri) |
| **Faz P4.2 (Avalonia 11 + SkiaSharp 2D CAD Stüdyosu)** | **Claude Fable 5.2** (`UI_UX_Referans_v2_19.html` dönüşümü) | **Gemini 3.8 Flash** + **Sonnet 4.6** | **Gemini 4 Hydrogen** + **Gemini 3.8 Flash** (XAML/MVVM testi) |
| **Faz P5 (Resmî Cetveller: `FR.348`, `Fen Klasörü`, `FR.521`)** | **Claude Sonnet** | **Claude Sonnet 4.6** | **Gemini 4 Hydrogen** (PDF/Excel şablon ve `He/M2/Dm2` testi) |
| **Faz P6 & P7 (3 Gerçek Saha Projesi Kabulü & Revizyon)** | **Claude Fable 5.2** (Kök neden düzeltme) | **Claude Opus 4.6** | **Gemini 4 Hydrogen** (`Imar18.Pilot.Tests` tam saha koşumu) |
| **Faz 11–13 (`SQLCipher .imr18`, `Argon2id`, `Ed25519`)** | **Claude Opus** *(Güvenlik filtresi nedeniyle)* | **Claude Opus 4.6** | **Gemini 4 Hydrogen** *(+ Claude Code `/code-review`)* |
| **Faz 14–20 (OCR, WCAG, `GuardrailService`, Dağıtım)** | **Claude Sonnet** *(Faz 18 Guardrail: **Fable 5.2 / Opus**)* | **Gemini 3.8 Flash** + **Sonnet 4.6** | **Gemini 4 Hydrogen** + **Gemini 3.8 Flash** |
