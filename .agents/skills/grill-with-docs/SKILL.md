---
name: grill-with-docs
description: (Matt Pocock Uyarlaması) Gereksinim, mevzuat veya saha verisi belirsizliklerini Imar18Studio_Spesifikasyon_v2_19.md (D17-D31) ile çapraz sorgular; varsayım yapmak yerine ADR ve SORU-* üretir.
---
# Dokümanla Sorgulama ve Netleştirme (`grill-with-docs`)

1. Bir faz veya özelliğe başlamadan önce `docs/Imar18Studio_Spesifikasyon_v2_19.md`, `docs/PILOT_ROADMAP_v2_19.md` ve `test-data/synthetic/` altın veri setlerini incele.
2. Eğer uç bir senaryoda (ör. hisseli parselde eşit kalan, konkav adada çoklu poligon bölünmesi, `.ncz` katman adı uyuşmazlığı) spesifikasyonda açık kural yoksa **asla kendi başına mevzuat kuralı uydurma (`D27`)**.
3. Belirsizliği `docs/STATUS.md` içinde `SORU-xxx` koduyla kayıt altına al ve seçenekleri mevzuat dayanaklarıyla kullanıcıya sun.
