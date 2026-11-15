---
name: wayfinder
description: (Matt Pocock Uyarlaması) Çok adımlı karmaşık fazları (Faz P2 BisectionEngine, Faz P4 Avalonia CAD Tuvali ve Faz 11-20 Kurumsal Geçiş) mimari karar noktalarına ve bağımlılık haritasına böler.
---
# Karmaşık Fazlar İçin Karar Haritası (`wayfinder`)

1. Büyük bir faza (`Faz P2`, `Faz P4` veya `Faz 11+`) girerken önce tüm alt bileşenlerin girdi/çıktı sözleşmelerini çıkar.
2. Mimari belirsizlik veya alternatif algoritma varsa (ör. sırt sırta adada omurga hattı kırılması vs. 45° köşe pahı önceliği) bunları kod yazmadan önce karar düğümleri (`Decision Nodes`) olarak çöz ve `to-tickets` adımına temiz bir sıra aktar.
