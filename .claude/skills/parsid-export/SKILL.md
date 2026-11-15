---
name: parsid-export
description: PARSİD / TKGM e-Devlet şema doğrulama, deneysel iç format etiketleme (AC-PILOT-07) ve şema hatasında ihracat engelleme kurallarını uygular.
---
# PARSİD ve Resmî Şema İhracat Kuralları (`parsid-export`)

1. **Sahte Uyumluluk Yasağı (`AC-PILOT-07`):**
   - Resmi TKGM XSD/JSON şeması doğrulanmadan üretilen paketler yalnızca `"Deneysel İç Format — Resmî PARSİD Şeması Değildir"` etiketiyle üretilir.
2. **İhracat Engeli (Export Gate):**
   - Tecviz aşımı, `%45` DOP tavan aşımı, açık topoloji hatası veya geçersiz `RevisionId` (mühendis onayı düşmüş) varsa paket ihracatı kesin olarak engellenir.
