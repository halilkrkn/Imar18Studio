# Imar18Studio — Nihai Pilot Sonrası Yol Haritası (POST_PILOT_ROADMAP v2.19)
> **Amaç:** Bu belge, Pilot (`Faz P0 – P7`) saha testleri başarıyla tamamlandıktan sonra **Claude Code** ve **Google Antigravity** ajanlarının `Imar18Studio` uygulamasını kurumsal tam sürüme (`v2.19 Enterprise Release`) taşıması için izleyeceği **Pilot Sonrası Otonom Yol Haritasıdır**.
> **Ön Koşul Kilidi (`D27`):** `docs/STATUS.md` içinde `Faz P0 – P7` tümüyle `[x]` olmadan bu dosyadaki hiçbir faz başlatılamaz.

---

## Faz 11 — Şifreli Yerel Veritabanı (`SQLCipher .imr18`) ve `Argon2id / DPAPI` (`AC-SEC-01..08`)
- **11.1:** `Imar18.Infrastructure/Persistence` altında `SQLite + SQLCipher` (`AES-256`) tabanlı tek dosyalık `.imr18` proje konteynerinin kodlanması.
- **11.2:** `Argon2id` ($64	ext{ MB}$ bellek, $3$ iterasyon) anahtar türetme ve Windows `DPAPI` (`ProtectedData`) yerel anahtar kasası entegrasyonu.
- **11.3:** Otomatik yedekleme (`.imr18.bak`), WAL modu ve bozuk dosya kurtarma (`PRAGMA cipher_integrity_check`) testleri.

## Faz 12 — `Ed25519` Kriptografik Denetim İzi (`AuditLog`) ve Reddedilen İşlem Kaydı (`AC-AUDIT-01..10`)
- **12.1:** `NSec.Cryptography` (`Ed25519`) ve `SHA-256` önceki-kayıt özet zinciri (`PrevHash → CurrentHash → Signature`) ile değiştirilemez `AuditChainService` kodlanması.
- **12.2:** Yalnızca onaylanan işlemlerin değil, mevzuat/tecviz/DOP sınırları nedeniyle **reddedilen işlemlerin** ve geçersiz kılınan mühendis onaylarının (`InvalidatedApproval`) da denetim zincirine yazılması.

## Faz 13 — Çok Kullanıcılı Dosya Kilitleme (`ProjectLockService` / `.imr18.lock`) (`AC-LOCK-01..07`)
- **13.1:** Yerel ağ (SMB/NAS) paylaşımlarında eşzamanlı yazma çakışmalarını önleyen `.imr18.lock` kalp-atışı (`Heartbeat`, $15	ext{ sn}$), salt-okunur (`Read-Only`) açılış modu ve bayat kilit devralma (`Stale Lock Recovery`) mekanizması.

## Faz 14 — Mevzuat Profil Versiyonlama (`ProfileVersionService`) (`AC-VER-01..06`)
- **14.1:** İmar planı notları, belediye encümen kuralları ve yönetmelik parametrelerinin (`MaxDopRate = 0.45m`, min cephe/derinlik/alan) JSON şeması olarak imzalanıp `.imr18` içine gömülmesi; eski projeler açıldığında oluşturulduğu tarihteki mevzuat profiliyle deterministik yeniden hesaplanması.

## Faz 15 — Gelişmiş CAD Topoloji Onarımı ve Otomatik Poligon Kapatma (`AC-DXF-01..07`)
- **15.1:** `.dxf`, `.dwg` ve `.ncz` dosyalarındaki açık poligon uçlarının (`Undershoot / Overshoot` $\le 5	ext{ cm}$), kendi kendini kesen (`Self-Intersecting / Bowtie`) kenarların ve mükerrer köşe noktalarının otomatik topoloji onarım sihirbazı ile temizlenmesi.

## Faz 16 — Çevrimdışı `Tesseract OCR` (`tur.traineddata`) ve `PdfPig` Çapraz Doğrulama (`AC-OCR-01..06`)
- **16.1:** Taranmış tapu kütüğü ve eski fen klasörü PDF'lerinin yerel `Tesseract 5` (`tur.traineddata`) ile okunması, düşük güven skorlu (`Confidence < %85`) hücrelerin kullanıcıya sarı vurguyla onaylatılması; hiçbir verinin buluta gönderilmemesi (`%100` Air-Gapped KVKK uyumu).

## Faz 17 — Tam `WCAG 2.2 AA` Erişilebilirlik, Klavye Navigasyonu ve Yüksek Kontrast (`AC-A11Y-01..07`)
- **17.1:** CAD tuvali dahil tüm arayüz bileşenlerinin klavye kısayolları, ekran okuyucu (`AutomationProperties.Name`) ve saha/güneş ışığı yüksek kontrast temasıyla donatılması.

## Faz 18 — `GuardrailService` Korumalı Ürün İçi Doğal Dil Komut Asistanı (`AC-AI-01..10`)
- **18.1:** Komut çubuğunda (`Ctrl+K`) doğal dil ifadelerini (örn. *"103 adadaki C1 ve C2 köşe parsellerine 45 derece pah kır ve DOP oranını yeniden hesapla"*) yapılandırılmış `CommandIntent` nesnesine çeviren asistan.
- **18.2:** **Kesin Güvenlik Kuralı:** Doğal dil katmanı **asla** alan, DOP, hisse veya koordinat hesabı yapamaz; yalnızca deterministik `Application` komutlarını tetikler ve `GuardrailService` doğrulamasından geçmeyen hiçbir parametre uygulanmaz.

## Faz 19 — Opt-In Anonim Telemetri (`TelemetryService`) ve Tanılama Paketi (`AC-TEL-01..06`)
- **19.1:** Varsayılan olarak kapalı (`Opt-In`), parsel/malik/koordinat verilerini `PiiScrubber` ile maskeleyen yerel çökme ve performans tanılama paketi üreticisi.

## Faz 20 — `ProductionGate` Sürüm Kapısı, Pilot Filigranının Kaldırılması ve MSIX/Velopack Dağıtımı (`AC-REL-01..07`)
- **20.1:** Tüm `AC-*` kabul kriterleri ve mimari/güvenlik testleri `%100` geçtiğinde `"PİLOT — RESMÎ İŞLEMDE KULLANILAMAZ"` filigranının kaldırılıp EV Code Signing sertifikalı kurumsal kurulum paketinin üretilmesi.
