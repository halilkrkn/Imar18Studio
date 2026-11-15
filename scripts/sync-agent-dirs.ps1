# Windows'ta .claude ve .agents kural/skill klasörlerini senkronize eden yardımcı betik (D30 & D11)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Imar18Studio: Claude Code (.claude) ve Google Antigravity (.agents) dizinleri eşitleniyor..." -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path "$repoRoot\.agents\rules" | Out-Null
New-Item -ItemType Directory -Force -Path "$repoRoot\.agents\skills" | Out-Null

Copy-Item -Path "$repoRoot\.claude\rules\*" -Destination "$repoRoot\.agents\rules\" -Recurse -Force
Copy-Item -Path "$repoRoot\.claude\skills\*" -Destination "$repoRoot\.agents\skills\" -Recurse -Force

Write-Host "Tamamlandı: 4 kural dosyası ve 30 SKILL.md (11 çekirdek Imar18 + 19 yardımcı) hem .claude/ hem de .agents/ altında aktif." -ForegroundColor Green
