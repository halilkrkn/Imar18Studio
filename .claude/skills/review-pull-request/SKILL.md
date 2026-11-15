---
name: review-pull-request
description: PR veya dal değişikliklerini Imar18Studio mimari, sayısal hassasiyet, güvenlik ve Pilot kapsam kurallarına göre bağımsız biçimde inceler.
---
# Bağımsız PR İnceleme (`/review-pull-request`) Becerisi

1. Değişen tüm dosyaları ve testleri incele (`reviewer` alt ajan kontrol listesini uygula).
2. Aşağıdaki 8 kritere göre değerlendirme yap:
   - **Test Kapsamı:** Boundary, negatif, boş ve taşma senaryoları var mı?
   - **Mimari İzolasyon:** Domain'e I/O veya Application'a `NetTopologySuite` sızmış mı?
   - **Sayısal Doğruluk:** Hak/alan/DOP hesaplarında `Decimal` ve `Fraction` dışına (`double`) çıkılmış mı?
   - **Çoklu CAD ve Kuantizasyon:** `.dxf`/`.dwg`/`.ncz` eksen normalizasyonu ve kuantizasyon sonrası `Decimal` Gauss doğrulaması korunmuş mu?
   - **Onay ve Audit:** `RevisionId` onay bağı ve reddedilen işlem loglaması tam mı?
   - **Çıktı Sözleşmesi:** Pilot damgası ve PARSİD/Fen Klasörü şablon sürümü kontrol edilmiş mi?
   - **Veri Güvenliği:** Public depoya gerçek kişisel veri veya koordinat sızmış mı (`AC-PILOT-08`)?
   - **Kapsam Disiplini:** Pilot dışı (Faz 11–20) özellik eklenmiş mi?
3. Sonucu `Approve` / `Request changes` / `Block` kararı, kritik/orta bulgular ve eksik test adlarıyla raporla; otomatik merge yapma.
