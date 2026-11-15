# Imar18Studio — 30 Adet `SKILL.md` Kaynak, Lisans ve Güvenlik Taraması Kataloğu (`D11.2`)

> **Toplam Beceri Sayısı (`ls .claude/skills | wc -l`):** **30 adet** (`11 çekirdek Imar18 + 19 yardımcı/uyarlama`).
> **Spec §24.5 Notu (`SORU-012`):** Spec §24.5'te *"Üçüncü parti bilinmeyen marketplace'ler kurulmaz; yalnızca resmî `anthropics/claude-plugins-official` ve `anthropics/skills` kaynakları kullanılır"* ifadesi yer alırken, kullanıcı talebiyle `mattpocock/skills` ve `obra/Superpowers` metodolojileri dış marketplace bağlantısı yapılmadan **yerel, statik ve zararsız Markdown (`SKILL.md`) talimatları** olarak uyarlanmıştır. Bu durum `docs/STATUS.md` §6 içinde **`SORU-012`** olarak kayıt altına alınmıştır (Varsayılan: Yerel uyarlamalar kalsın ve bu tabloda belgelensin).

## 1. 30 Adet `SKILL.md` Envanteri ve Güvenlik Denetimi

| # | Skill Klasörü | Kategori | Kaynak | Lisans | Harici Komut / URL Taraması Sonucu |
|---|---|---|---|---|---|
| 1 | `start-pilot-phase` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | Yalnızca yerel `git checkout -b` ve `STATUS.md` okuma (Güvenli) |
| 2 | `run-pilot-tests` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | Yalnızca yerel `dotnet test` (Güvenli) |
| 3 | `review-pull-request` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | Salt-okunur denetim talimatı, dış URL yok (Güvenli) |
| 4 | `close-pilot-phase` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | Yerel `git commit` ve `dev` merge talimatı (Güvenli) |
| 5 | `imar18-tdd-dotnet` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | Yerel `xUnit` / TDD kuralları, dış URL yok (Güvenli) |
| 6 | `imar18-domain-rules` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | `Decimal`, `Fraction`, `TecvizGirdisi`, `DOPO` kuralları (Güvenli) |
| 7 | `imar18-architecture` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | Clean Architecture ve `NetArchTest` kuralları (Güvenli) |
| 8 | `imar18-mevzuat-profil` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | `TPYS` (RG 31966) ve `TecvizGirdisi` kuralları (Güvenli) |
| 9 | `imar18-cad-topology` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | `.ncz` (birincil) ve `.dxf/.dwg` (yedek) topoloji kuralları (Güvenli) |
| 10 | `imar18-output-contracts` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | `QuestPDF` / `ClosedXML` ve Pilot filigran kuralları (Güvenli) |
| 11 | `imar18-security-kvkk` | 11 Çekirdek Imar18 | Imar18 Özgü | Proje İçi | `AC-PILOT-08` ve Faz 11–12 güvenlik kuralları (Güvenli) |
| 12 | `turkish-legal-domain` | 19 Yardımcı (Domain) | Imar18 Özgü (`D31`) | Proje İçi | 3194 Md. 18 ve 9 saha kuralı, dış komut yok (Güvenli) |
| 13 | `nettopologysuite` | 19 Yardımcı (Teknik) | Imar18 Özgü (NTS Referanslı) | Proje İçi | `1 mm` kuantizasyon ve 6 ada `BisectionEngine` kuralı (Güvenli) |
| 14 | `projnet-crs` | 19 Yardımcı (Teknik) | Imar18 Özgü (ProjNet Referanslı) | Proje İçi | Proje CRS girdisi ve `Y/X` eksen takası kuralı (Güvenli) |
| 15 | `acadsharp-cad` | 19 Yardımcı (Teknik) | Imar18 Özgü (ACadSharp Referanslı) | Proje İçi | `.dxf/.dwg` yedek adaptör ve $Z=0$ izdüşüm kuralı (Güvenli) |
| 16 | `ncz-netcad` | 19 Yardımcı (Teknik) | Imar18 Özgü (`D14` & `D31`) | Proje İçi | Birincil `.ncz` ve `.cks` okuma kuralı, dış URL yok (Güvenli) |
| 17 | `avalonia-mvvm` | 19 Yardımcı (Teknik) | Imar18 Özgü | Proje İçi | Avalonia 11 + SkiaSharp MVVM kuralları, dış URL yok (Güvenli) |
| 18 | `sqlcipher-security` | 19 Yardımcı (Teknik) | Imar18 Özgü | Proje İçi | Faz 11–13 şifreleme ve Pilot kapsam kilidi (Güvenli) |
| 19 | `parsid-export` | 19 Yardımcı (Teknik) | Imar18 Özgü | Proje İçi | `AC-PILOT-07` sahte şema engeli (Güvenli) |
| 20 | `fen-klasoru-signing` | 19 Yardımcı (Teknik) | Imar18 Özgü | Proje İçi | `FR.348`, `FR.521`, `He/M2/Dm2` ve `RevisionId` bağı (Güvenli) |
| 21 | `agent-conflict-guard` | 19 Yardımcı (Süreç) | Imar18 Özgü | Proje İçi | `feat/* -> dev -> main` ve `STATUS.md` kilidi (Güvenli) |
| 22 | `grill-with-docs` | 19 Yardımcı (Metodoloji) | `mattpocock/skills` Uyarlaması | Doğrulanmadı (Yerel yeniden yazım) | Harici script/URL yok; yalnızca `SORU-*` üretme talimatı (Güvenli) |
| 23 | `to-spec` | 19 Yardımcı (Metodoloji) | `mattpocock/skills` Uyarlaması | Doğrulanmadı (Yerel yeniden yazım) | Harici script/URL yok; `FR/AC/TC` yazım talimatı (Güvenli) |
| 24 | `to-tickets` | 19 Yardımcı (Metodoloji) | `mattpocock/skills` Uyarlaması | Doğrulanmadı (Yerel yeniden yazım) | Harici script/URL yok; atomik görev kartı talimatı (Güvenli) |
| 25 | `implement` | 19 Yardımcı (Metodoloji) | `mattpocock/skills` + `Superpowers` Uyarlaması | Doğrulanmadı / Apache-2.0 | Yalnızca yerel `dotnet test` komutu içerir (Güvenli) |
| 26 | `code-review` | 19 Yardımcı (Metodoloji) | `mattpocock/skills` Uyarlaması | Doğrulanmadı (Yerel yeniden yazım) | Salt-okunur iki eksenli inceleme talimatı (Güvenli) |
| 27 | `systematic-debugging` | 19 Yardımcı (Metodoloji) | `obra/Superpowers` Uyarlaması | Apache-2.0 (`obra/superpowers`) | Harici script/URL yok; 4 adımlı kök neden analizi (Güvenli) |
| 28 | `improve-codebase-architecture` | 19 Yardımcı (Metodoloji) | `mattpocock/skills` Uyarlaması | Doğrulanmadı (Yerel yeniden yazım) | Harici script/URL yok; derin modül analizi (Güvenli) |
| 29 | `wayfinder` | 19 Yardımcı (Metodoloji) | `mattpocock/skills` Uyarlaması | Doğrulanmadı (Yerel yeniden yazım) | Harici script/URL yok; karar düğümü analizi (Güvenli) |
| 30 | `resolving-merge-conflicts` | 19 Yardımcı (Metodoloji) | `mattpocock/skills` Uyarlaması | Doğrulanmadı (Yerel yeniden yazım) | Yalnızca yerel `dotnet test` doğrulama talimatı (Güvenli) |

- **Harici Resmi Anthropic Becerileri (`install-official-plugins.ps1`):** `anthropics/skills` deposundaki `pdf`, `xlsx`, `docx`, `frontend-design`, `skill-creator` becerileri **`2c5958ecac7cee2b583967a2c4981d0f4a75c579`** commit SHA'sına sabitlenmiştir (Lisans: **Apache-2.0**, PR #2146 ile doğrulandı).
