# Contribuir a gestor-inve
Antes de tocar código lee `brain/INDEX.md` (decisiones, dominio, reglas).

## Flujo
1. Una rama por hito/issue (`h2-persistencia`, `fix/kardex-reversa`). PR pequeño, CI verde.
2. **TDD** en dominio: test rojo → verde → refactor. Sin lógica de negocio sin test.
3. Commits en formato Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).
4. Decisiones no triviales → ADR en `brain/decisiones/`. Actualiza `brain/INDEX.md`.

## Reglas duras
- Dinero y cantidades: `decimal` / `NUMERIC`. Nunca `float`/`double`.
- Kardex inmutable: se corrige con movimientos de reversa, jamás con `UPDATE`.
- Los módulos no se referencian entre sí; se comunican por eventos de dominio.
- No copiar código, UI ni marca de ERPs comerciales.
- Calidad: ver `CONSTRAINTS.md` (no se rebaja el listón para pasar CI).

## Licencia
Al contribuir aceptas que tu aporte se publique bajo AGPL-3.0.
