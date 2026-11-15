# Imar18Studio Sentetik ve Anonimleştirilmiş Altın Veri Seti (`test-data/synthetic/`)

> **Güvenlik Kuralı (`AC-PILOT-08`):** Bu klasöre yalnızca kişisel verileri (Malik Adı, TC Kimlik No, Baba Adı, Cilt/Sayfa) tamamen anonimleştirilmiş ve koordinatları yerel ofsetle kaydırılmış sentetik test verileri konulabilir. Gerçek müşteri/tapu dosyaları `test-data/real/` altında (`.gitignore` içinde) tutulur ve depoya asla yüklenmez.

> **Yerel Gerçek Veri:** Gerçek cetvel ve `.NCZ` dosyaları yalnızca geliştiricinin bilgisayarında `test-data/real/` altında durur (`.gitignore`). Kodlama ajanı bunları yerel doğrulama için okuyabilir, değerlerini bu klasöre veya başka bir public dosyaya kopyalamaz. Gerçek veriye bağlı testler `[Trait("Category", "RealData")]` taşır ve CI'da dışlanır.

## Klasör Yapısı
- `pilot-1-concave-parcel/`: Sentetik `.dxf`, `.dwg` ve `.ncz` L/U tipi konkav ada ve 13×16 m asgari ölçü test verileri.
- `pilot-2-mixed-ownership/`: Kapanan yol, terk, bağış, kamu mülkiyeti, önceki DOP ve %45 üst sınır aşımı test verileri.
- `pilot-3-shared-parcel/`: Yüksek paydalı müşterek hisseler, `Fraction` ve `Largest Remainder` dağıtım test verileri.
- `pilot-4-output-contract/`: `RevisionId` onay bağı, şema/şablon sürümü ve checksum doğrulama test verileri.

## Pre-Commit Gerçek Veri Koruması Kurulumu (`D12` / `AC-PILOT-08`)
Gerçek saha verilerinin (`test-data/real/`, `.pdf`, `.ncz`, `.dwg`) yanlışlıkla commit edilmesini önlemek için yerel Git hook'unu şu komutla etkinleştirin:
```bash
cp scripts/hooks/pre-commit .git/hooks/pre-commit && chmod +x .git/hooks/pre-commit
```
