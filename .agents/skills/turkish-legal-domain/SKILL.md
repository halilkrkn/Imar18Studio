---
name: turkish-legal-domain
description: 3194 sayılı İmar Kanunu Md. 18, AAD Yönetmeliği, TPYS, DOPO (R1-D1-R2+D2+R3), dm² tescil paydası, Md. 17/1-d hisse çözümlemesi ve Y1 yol dengelemesi kurallarını uygular.
---
# Türk İmar Mevzuatı ve Saha Kuralları (`turkish-legal-domain`)

1. **Resmî DOPO Formülü (`D31 Kural 1`):**
   - `DOPO = (R1 - D1 - R2 + D2 + R3) / (R1 - D1)` formülünü `Decimal` (7 ondalık basamak) ile hesapla.
   - `DOPO > 0.45m` ise `IMR-E-3001` fırlat ve `OKO / KOP` (`R3`) kesinti/kamulaştırma akışını zorunlu kıl.
2. **Tescil Paydası (`D31 Kural 2`):**
   - Müstakil parselde (`Paydaş == 1`) hisseyi `"TAM"` yaz.
   - Hisseli parselde tescil paydasını `ParselAlanı_m2 * 100` (`dm²` tam sayı, örn. `3539.74 m² -> 353974`) yap ve `Largest Remainder` ile dengele.
3. **Alan Dengeleme (`D31 Kural 4`):**
   - Tapu alanı ile hesap alanı farkı tecviz (mühendis girdisi `TecvizSınırıM2`) içindeyse oluşan imar parsellerini `+0.00` tut, farkı kamuya terk edilen yol (`Y1`) parselinde dengele.
4. **Muvafakatli Hisse Çözümlemesi (`D31 Kural 8`):**
   - `AAD Yönetmeliği Md. 17/1-(d)` kapsamında muvafakatli paydaşları müstakil imar parsellerine ata.
5. **Sıfır Varsayım (`D27`):** Mevzuatta veya saha verisinde belirsiz bir eşik varsa asla uydurma; `grill-with-docs` becerisiyle `SORU-*` kaydı aç.

- **Tecviz Kuralı (D1):** Tecviz sınırı mühendis girdisidir (`TecvizGirdisi.TecvizSiniriM2`); sabit kodlanmaz; `TecvizChecker` yalnızca girilen sınırı (`|GaussAlanı - TapuAlanı| <= TecvizSınırıM2`) denetler.

## Resmî DOP Değişken Tanımları (§25.1 / D2 — Tek Tanım)
- **$R_1$ ("Düzenlemeye Giren Alanlar"):** Düzenlemeye giren parsel alanları toplamı.
- **$D_1$ ("Eski DOPO > Proje DOPO Alanlar"):** Eski DOP oranı proje DOP oranından büyük olan parsellerin alanı.
- **$R_2$ ("İmar Alanları"):** Yalnızca imar parsellerinin (tahsis) alan toplamıdır; PARK, yol, dini tesis, belediye hizmet alanı $R_2$'ye GİRMEZ. Kamuya ayrılan alan $= R_1 - R_2$ olarak TÜRETİLİR (kullanıcı girdisi değil). Pilot varsayılanı $D_1 = D_2 = R_3 = 0$; sıfırdan farklı senaryolar `SORU-008` ile açık kalır.
- **$D_2$ ("Eski DOPO > Proje DOPO Tahsisler"):** Eski DOPO > Proje DOPO olan parsellere yapılan tahsis alanı.
- **$R_3$ ("DOP Alınmayan Alanlar"):** Düzenlemeye giren ancak DOP alınmayan alanlar.
