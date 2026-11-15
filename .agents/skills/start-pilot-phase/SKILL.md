---
name: start-pilot-phase
description: Aktif faz için dev dalından yeni feat/pX-* dalı açar, giriş koşullarını ve docs/STATUS.md kilidini doğrular.
---
# Faz Başlangıç ve Dal Açma (`/start-pilot-phase`) Becerisi

1. `docs/STATUS.md` ve `docs/PILOT_ROADMAP_v2_19.md` (veya `POST_PILOT_ROADMAP_v2_19.md`) dosyasını oku.
2. Çalışma ağacının temiz olduğunu (`git status`) ve aktif dalın `dev` olduğunu kontrol et; ardından ilgili faz için `git checkout -b feat/<faz-kodu>` (ör. `feat/p1-numeric-core`) dalını oluştur.
3. `docs/STATUS.md` üzerinde ilgili fazı `[/]` olarak işaretle ve aktif dal adını yaz.
