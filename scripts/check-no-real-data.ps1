# Imar18Studio — Gerçek Saha Verisi Sızıntı Önleme Kapısı (AC-PILOT-08 / D12)
$ErrorActionPreference = "Stop"

$trackedFiles = git ls-files
$violations = @()

foreach ($f in $trackedFiles) {
    if ($f -match "^test-data/real/" -or $f -match "^test-data/raw/") {
        $violations += "YASAK DİZİN (real/raw): $f"
    }
    if ($f -match "\.(ncz|cks|dwg)$" -and $f -notmatch "^test-data/synthetic/") {
        $violations += "SENTETİK DIŞI CAD/CKS DOSYASI: $f"
    }
    if ($f -match "^test-data/synthetic/.*\.pdf$") {
        $violations += "SENTETİK DİZİNDE PDF YASAĞI: $f"
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("AC-PILOT-08 İHLALİ TESPİT EDİLDİ:`n" + ($violations -join "`n"))
    exit 1
}

Write-Host "OK (AC-PILOT-08): Git indeksinde gerçek saha verisi, PDF veya yetkisiz CAD dosyası bulunmadı." -ForegroundColor Green
exit 0
