---
name: resolving-merge-conflicts
description: (Matt Pocock Uyarlaması) Claude Code ve Google Antigravity geçişlerinde veya paralel git branch/worktree birleştirmelerinde oluşan çakışmaları Imar18Studio v2.19 kurallarına göre çözer.
---
# Hedef Odaklı Merge ve Dal Çakışması Çözümü (`resolving-merge-conflicts`)

1. Bir çakışma (`merge/rebase conflict`) oluştuğunda rastgele `ours/theirs` seçme; `docs/Imar18Studio_Spesifikasyon_v2_19.md` (`D17–D31`) ve `docs/STATUS.md` içindeki aktif faz hedefini oku.
2. `Decimal` / `Fraction(BigInteger)` tip güvenliğini koruyan ve birim testleri (`dotnet test`) geçiren tarafı birleştir; birleştirme sonrası `CleanArchitectureRulesTests` testini zorunlu olarak çalıştır.
