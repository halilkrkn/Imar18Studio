---
name: close-pilot-phase
description: Antigravity bağımsız test onayı sonrasında feat/pX-* dalını dev dalına (--no-ff) birleştirir, STATUS.md tablosunu [x] yapar ve evre sonunda main'e aktarır.
---
# Faz Kapanış ve `dev` Dalına Merge (`/close-pilot-phase`) Becerisi

1. Aktif `feat/<faz-kodu>` dalında tüm birim, mimari (`Imar18.Architecture.Tests`) ve altın veri testlerinin `%100` yeşil olduğunu doğrula.
2. `docs/STATUS.md` üzerinde ilgili fazı `[x]` olarak güncelle ve dalda son commit'i oluştur: `git add . && git commit -m "feat(<faz>): tamamlandi ve Antigravity tarafindan dogrulandi"`.
3. Görev dalını `dev` dalına birleştir:
   - `git checkout dev`
   - `git merge --no-ff feat/<faz-kodu> -m "merge: feat/<faz-kodu> -> dev"`
4. Eğer `Faz P7` (Pilot sonu) veya `Faz 20` (Kurumsal Tam Sürüm sonu) tamamlandıysa, proje sahibi onayıyla `dev` dalını `main` dalına birleştir (`git checkout main && git merge --no-ff dev`).
5. Faz kapanış yanıtının en sonuna mutlaka şu uyarıyı ekle:
   `🛑 FAZ TAMAMLANDI: Token tasarrufu ve temiz bağlam için şimdi mevcut sohbeti kapatıp yeni bir sohbet oturumu açın.`

