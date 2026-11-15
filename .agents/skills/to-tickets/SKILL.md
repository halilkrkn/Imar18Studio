---
name: to-tickets
description: (Matt Pocock Uyarlaması) PILOT_ROADMAP_v2_19.md ve POST_PILOT_ROADMAP_v2_19.md içindeki fazları bağımlılık sırasına göre küçük, tek-PR'lık TDD görev kartlarına (ticket) böler.
---
# Fazları Bağımlılıklı TDD Görev Kartlarına Bölme (`to-tickets`)

1. Aktif fazı (`docs/STATUS.md`) oku ve her bir alt adımı (`P1.1`, `P1.2`, vb.) en fazla 1–3 sınıf ve 1 test sınıfı içerecek atomik görev kartlarına ayır.
2. Her görev kartında şu 5 alanı zorunlu tut:
   - **Bağımlı Olduğu Önceki Görev:**
   - **İlgili `FR / AC / TC` Kodları:**
   - **Önce Yazılacak Başarısız Testler (`Red`):**
   - **Üretim Kodu Hedef Dosyaları (`Green`):**
   - **Doğrulama Komutu (`dotnet test --filter ...`):**
