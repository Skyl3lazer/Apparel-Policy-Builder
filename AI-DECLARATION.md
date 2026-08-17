---
version: "0.1.2"
level: copilot
processes:
  design: assist
  implementation: pair
  testing: copilot
  documentation: assist
  review: hint
  deployment: copilot
components:
  Source/: pair
  Languages/: assist
  Patches/: assist
  .github/workflows/: copilot
---

This format is based on [AI-DECLARATION.md](https://ai-declaration.md/en/0.1.2).

## Notes

- Claude Code wrote the C# under `Source/` piece by piece. Each piece was directed and reviewed by the author.
- The architecture and design decisions are the author's. Claude Code acted on parts of the work under direction.
- Claude Code wrote the release workflow under `.github/workflows`.
- Test plans and in-game verification were driven by Claude Code.
