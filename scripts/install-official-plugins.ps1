# Imar18Studio — Resmi Anthropic Skill Kurulum & Eşitleme Betiği (D11 — Commit SHA Pinli)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
# SHA doğrulandı (15 Kasım 2026): 2c5958ec... = "Move Office skills to the Apache 2.0 license (#2146)". Depo HEAD'i değildir. Yeni sürüme geçmek için `git ls-remote` ile yeni SHA'yı doğrulayıp aşağıdaki satırı güncelleyin.
$pinnedAnthropicSkillsSha = "2c5958ecac7cee2b583967a2c4981d0f4a75c579" # PR #2146: Move Office skills to Apache 2.0 license

Write-Host "=======================================================================" -ForegroundColor Cyan
Write-Host " Imar18Studio: Claude Code (.claude) & Antigravity (.agents) Eşitleme  " -ForegroundColor Cyan
Write-Host "=======================================================================" -ForegroundColor Cyan

# 1. Yerel .claude ve .agents dizinlerini senkronize et (30 SKILL.md: 11 çekirdek Imar18 + 19 yardımcı, 5 Subagent, 4 Kural)
& "$PSScriptRoot\sync-agent-dirs.ps1"

# 2. anthropics/skills deposundan resmi pdf, xlsx, docx ve skill-creator becerilerini sabit SHA ile indir
if (Get-Command git -ErrorAction SilentlyContinue) {
    $tempDir = Join-Path $env:TEMP "imar18_official_skills"
    if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
    New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
    Write-Host "`n[1/2] anthropics/skills resmi deposu sabit commit SHA ($pinnedAnthropicSkillsSha) ile çekiliyor..." -ForegroundColor Yellow
    Push-Location $tempDir
    git init | Out-Null
    git remote add origin https://github.com/anthropics/skills.git
    git fetch --depth 1 origin $pinnedAnthropicSkillsSha
    git checkout $pinnedAnthropicSkillsSha | Out-Null
    Pop-Location
    $officialSkills = @("pdf", "xlsx", "docx", "frontend-design", "skill-creator")
    foreach ($s in $officialSkills) {
        $srcPath = Join-Path $tempDir "skills\$s"
        if (Test-Path $srcPath) {
            Copy-Item -Path $srcPath -Destination "$repoRoot\.claude\skills\anthropic-$s" -Recurse -Force
            Copy-Item -Path $srcPath -Destination "$repoRoot\.agents\skills\anthropic-$s" -Recurse -Force
            Write-Host "  + Eklendi (Claude + Antigravity ortak): anthropic-$s" -ForegroundColor Green
        }
    }
    Remove-Item -Recurse -Force $tempDir
}

Write-Host "`n[2/2] Toplam 30 SKILL.md (11 çekirdek Imar18 + 19 yardımcı) hem .claude/skills/ hem de .agents/skills/ altında aktif!" -ForegroundColor Green
