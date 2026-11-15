---
name: agent-conflict-guard
description: Claude Code ve Google Antigravity ajanları arasında Git-Flow dal stratejisini (feat/faz-* -> dev -> main), docs/STATUS.md dosya kilidini ve vardiyalı devir-teslimi yönetir.
---
# Ajan Çakışma Koruyucusu ve Git-Flow Dal Disiplini (`agent-conflict-guard`)

1. **Üç Katmanlı Git-Flow Dal (Branch) Yapısı (`feat/* → dev → main`):**
   - `main`: Yalnızca saha kabulü bitmiş **Evre 1: Pilot (`v1.0.0-pilot`)** ve **Evre 2: Kurumsal Tam Sürüm (`v2.19.0-enterprise`)** bittiğinde `dev` dalından birleştirilen kararlı dal.
   - `dev`: Doğrulanmış fazların birleştiği ana entegrasyon dalı.
   - `feat/pX-Y-<kisa-ad>` (örn. `feat/p1-1-fraction`, `feat/p2-4-bisection`, `feat/p3-2-dopo`): Her faz veya alt adım için `dev` dalından açılan görev dalı.
2. **Üretici (Claude Code) ↔ Denetçi (Google Antigravity) Dal Paslaşması:**
   - **Claude Code (Üretici):** Göreve başlarken `git checkout dev && git checkout -b feat/<faz-kodu>` ile yeni dalı açar, `src/` kodunu yazar ve bu dala `git commit` atar. Asla doğrudan `dev` veya `main` dalına kod yazmaz.
   - **Google Antigravity (Denetçi):** Aynı `feat/<faz-kodu>` dalında bağımsız testleri (`tests/`) yazar ve `dotnet test Imar18Studio.sln` + `CleanArchitectureRulesTests` testlerini koşturur. Tüm testler `%100` yeşil olduğunda `feat/<faz-kodu>` dalını `git checkout dev && git merge --no-ff feat/<faz-kodu>` komutuyla `dev` dalına birleştirir!
3. **Ana Sürüm Birleştirmesi (`dev → main`):**
   - `Faz P0–P7` tamamlandığında `dev` dalı `main` dalına birleştirilir (`git checkout main && git merge --no-ff dev && git tag v1.0.0-pilot`).
   - `Faz 11–20` tamamlandığında `dev` dalı tekrar `main` dalına birleştirilir (`git tag v2.19.0-enterprise`).
