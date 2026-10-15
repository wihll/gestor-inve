# gestor-inve
ERP de inventarios open source para pymes (Windows/Linux). Idioma de docs y UI: español.

## Cómo orientarte
1. Lee `brain/INDEX.md` antes de explorar el código; el cerebro explica el porqué y las reglas de dominio.
2. Si existe `graphify-out/GRAPH_REPORT.md`, úsalo para la estructura del código (regenerar con `/graphify .`).
3. Decisiones nuevas → ADR en `brain/decisiones/`. Mantén `brain/INDEX.md` al día.

## Reglas duras
- Dinero y cantidades: siempre `decimal`/`NUMERIC`, nunca float.
- El kardex es inmutable: se corrige con movimientos de reversa.
- No copiar código, UI ni marca de ERPs comerciales.

## Skills del proyecto (`.claude/skills/`)
`find-skills` (Vercel), `ponytail*` (menos código), `graphify` (mapa del código) y el set de agent-skills de Addy Osmani (spec, planificación, TDD, revisión, seguridad, CI/CD, ADRs…). Flujo recomendado: spec-driven → planning → incremental + TDD → code review → shipping.
