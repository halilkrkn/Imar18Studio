# Imar18Studio — Claude Code & Google Antigravity Ortak Skill/Plugin ve Çalışma Rehberi

## 1. Dökümanlarınızdaki Skill ve Plugin Depolarının Durumu

Bana verdiğiniz `claude_plugins_roadmap.pdf` ve `claude_klasor_yapisi.md` içindeki tüm bileşenler **2 katman** halinde pakete entegre edilmiştir:

### Katman A — Projeye Özel Yazılan 11 Adet `SKILL.md` (ZIP İçinde Hazır ve İki Platformda %100 Çalışır)
Bu 11 beceri dosyası hem `.claude/skills/` (Claude Code için) hem de `.agents/skills/` (Google Antigravity için) klasörlerine birebir yerleştirilmiştir:
1. `imar18-tdd-dotnet` (Katı Red-Green-Refactor ve xUnit disiplini)
2. `imar18-domain-rules` (`Decimal`, `Fraction(BigInteger)`, `Largest Remainder`, `dm²` tescil paydası, `DOPO` ve `Tecviz` kuralları)
3. `imar18-architecture` (Clean Architecture, `NetArchTest` ve `NetTopologySuite` izolasyonu)
4. `imar18-mevzuat-profil` (3194 Md. 18, AAD Yönetmeliği ve altın veri seti doğrulaması)
5. `imar18-cad-topology` (PDF'teki `imar18-dxf-topology` becerisinin `.dxf`, `.dwg`, `.ncz` ve `.cks` destekli genişletilmiş hali)
6. `imar18-output-contracts` (Fen Klasörü `He/M2/Dm2`, 4'lü alan cetveli, `FR.348/FR.521` ve `ClosedXML`/`QuestPDF` sözleşmeleri)
7. `imar18-security-kvkk` (`SQLCipher .imr18`, `Argon2id`, `DPAPI`, `Ed25519` ve KVKK/Anonimleştirme kuralları)
8. `start-pilot-phase` (Faz başlangıç kontrolü)
9. `run-pilot-tests` (Kategori filtreli test koşumu)
10. `review-pull-request` (Bağımsız PR incelemesi)
11. `close-pilot-phase` (Faz kapanış ve `STATUS.md` güncelleme raporu)

### Katman B — Harici Resmi Anthropic Depoları (`anthropics/claude-plugins-official` ve `anthropics/skills`)
PDF'inizde yer alan harici GitHub depolarının (`anthropics/claude-plugins-official`, `anthropics/skills`, `agentskills/agentskills`) uyumluluk durumu:
- **`anthropics/skills` (`pdf`, `xlsx`, `docx`, `frontend-design`, `skill-creator`):** Standart `SKILL.md` formatında olduğu için (`agentskills` standardı) **hem Claude Code'da hem de Google Antigravity'de %100 ortak çalışır**. ZIP içindeki `scripts/install-official-plugins.ps1` betiğini çalıştırdığınızda GitHub'dan otomatik çekilip hem `.claude/skills/` hem `.agents/skills/` içine kopyalanır.
- **`context7` MCP Sunucusu (`@upstash/context7-mcp@4.6.0`):** `mcp_config.json` içinde sabit sürüm (`4.6.0`) ile tanımlıdır ve her iki platformda ortak çalışır. (`github` MCP sunucusu ise `mcp_config.json` içinde varsayılan olarak tanımlı değildir; güvenlik ve token tasarrufu için yalnızca yerel Git CLI kullanılır — D11.4).
- **`anthropics/claude-plugins-official` (`csharp-lsp`, `feature-dev`, `pr-review-toolkit`, `code-review`, `security-guidance`):** Bunlar Claude Code CLI'a özel `/plugin install` eklentileridir. Claude Code içinde doğrudan `/plugin install ...` ile çalışır; Google Antigravity tarafında ise bunların birebir karşılığı olarak `.agents/agents/` altındaki 5 alt ajan (`architect`, `implementer`, `test-engineer`, `reviewer`, `docs-writer`) ve Antigravity'nin yerleşik C# LSP sunucusu görev yapar.
