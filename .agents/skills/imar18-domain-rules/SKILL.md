---
name: imar18-domain-rules
description: Imar18.Domain katmanında Decimal, Fraction(BigInteger), Largest Remainder, TPYS Md. 8 tecviz ve AAD Yönetmeliği %45 DOP/mahsup kurallarını zorlar.
---
# Imar18Studio Domain ve Hesap Kuralları Becerisi

Bu beceri `Imar18.Domain` ve `Imar18.Application` içindeki tüm mülkiyet, alan, hisse, tecviz ve DOP hesaplarında zorunludur:

1. **Kesin Tip İzolasyonu:**
   - Tüm tapu alanı, tecviz, DOP, mahsup, tahsis ve hisse hesaplarında yalnızca `System.Decimal` (`m` sonekli sabitler) ve EBOB sadeleştirmeli `Fraction(BigInteger Pay, BigInteger Payda)` kullanılır.
   - `double` veya `float` kullanımı kesinlikle yasaktır.
2. **Deterministik Largest Remainder (En Büyük Kalan) Dağıtımı:**
   - Hisse veya parsel tahsis alanı yuvarlamalarında toplam alan (`Decimal`, 2 ondalık basamak) kuruş/santimetrekare bazında tam korunur ($\sum A_i = A_{	ext{toplam}}$).
   - Kalan farklar (`0.01m` adımlarıyla) en büyük kesirli kalana sahip hissedardan başlayarak dağıtılır.
   - **Eşit Kalan Bağlayıcı Sırası (Tie-Breaker):** Kesirli kalanlar eşitse sırasıyla (1) en büyük rasyonel hisse payına (`Fraction`), (2) yine eşitse deterministik küçük malik/hisse kimliğine (`OwnerId` / `ShareId` alfabetik/sayısal sıra) öncelik verilir.
3. **Yanılma Sınırı (Tecviz — TPYS Md. 8):**
   - **Md. 8(2) Arşiv Onaylı Kümülatif Fonksiyon:**
     $$\Delta A_{\max}(A) = \sum_{k=1}^{7} r_k \cdot \max(0, \min(A, u_k) - l_k)$$
     Kademeler: `0–10 m² (%5 -> 0.50)`, `10–100 m² (%2 -> 2.30)`, `100–500 m² (%1 -> 6.30)`, `500–1000 m² (%0.5 -> 8.80)`, `1000–5000 m² (%0.4 -> 24.80)`, `5000–25000 m² (%0.3 -> 84.80)`, `>25000 m² (%0.15)`.
   - **Md. 8(5) Geçici Koordinat Profili:** Köşe nokta konum doğruluğu $m_p$ yarıçaplı hata çemberi iç/dış zarf alanı ile kontrol edilir.
4. **DOP ve Mahsup Hesabı (AAD Yönetmeliği):**
   - $	ext{DOPO} = (\sum A_{	ext{giren}} - \sum A_{	ext{tahsis}}) / \sum A_{	ext{giren}} \le 0.4500000m$.
   - Kapanan yol, terk, bağış, kamu hizmetine tahsisli alan ve önceki DOP kesintileri `ParselMevzuatSinifiKaydi` üzerinden ayrık hesaplanır; önceki terk/bağış oranı DOPO'dan düşükse tamamlayıcı kesinti yapılır, yüksek veya eşitse ilave DOP kesilmez.
   - %45 aşımı varsa sırasıyla: (1) Tescil harici alan, (2) Belediye mülkiyeti, (3) Muvafakatli kamu taşınmazı, (4) Kamulaştırma adımları uygulanır.

- **Tecviz Kuralı (D1):** Tecviz sınırı mühendis girdisidir (`TecvizGirdisi.TecvizSiniriM2`); sabit kodlanmaz; `TecvizChecker` yalnızca girilen sınırı (`|GaussAlanı - TapuAlanı| <= TecvizSınırıM2`) denetler.

## Resmî DOP Değişken Tanımları (§25.1 / D2 — Tek Tanım)
- **$R_1$ ("Düzenlemeye Giren Alanlar"):** Düzenlemeye giren parsel alanları toplamı.
- **$D_1$ ("Eski DOPO > Proje DOPO Alanlar"):** Eski DOP oranı proje DOP oranından büyük olan parsellerin alanı.
- **$R_2$ ("İmar Alanları"):** Yalnızca imar parsellerinin (tahsis) alan toplamıdır; PARK, yol, dini tesis, belediye hizmet alanı $R_2$'ye GİRMEZ. Kamuya ayrılan alan $= R_1 - R_2$ olarak TÜRETİLİR (kullanıcı girdisi değil). Pilot varsayılanı $D_1 = D_2 = R_3 = 0$; sıfırdan farklı senaryolar `SORU-008` ile açık kalır.
- **$D_2$ ("Eski DOPO > Proje DOPO Tahsisler"):** Eski DOPO > Proje DOPO olan parsellere yapılan tahsis alanı.
- **$R_3$ ("DOP Alınmayan Alanlar"):** Düzenlemeye giren ancak DOP alınmayan alanlar.
