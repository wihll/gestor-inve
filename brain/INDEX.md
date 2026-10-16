# Cerebro de gestor-inve

Base de conocimiento en Markdown. **Punto de entrada obligatorio** para cualquier agente antes de explorar el código.
Regla: consultar aquí primero; solo después grep/leer fuentes. Cada nota tiene frontmatter (`estado`, `actualizado`) y enlaces `[[nota]]`.

## Mapa
| Carpeta | Contenido |
|---|---|
| `vision.md` | Objetivo, alcance, no-objetivos, licencia |
| `arquitectura.md` | Stack, monolito modular, invariantes técnicas y la estructura real de la solución (H1) |
| `roadmap.md` | Fases 1-4 con criterios de salida |
| `plan-esqueleto.md` | Hitos H0-H10 para crear el proyecto y la Fase 1 |
| `decisiones/` | ADRs: 0001 stack, 0002 licencia, 0003 persistencia y RLS, 0004 autenticación, 0005 catálogo; se supersede, no se edita |
| `dominio/` | Glosario y reglas: kardex, costeo (PEPS/promedio), lotes, impuestos |
| `modulos/` | Una nota por módulo: responsabilidad, entidades, eventos, API |
| `normativa/` | DIAN, UBL 2.1, PUC, retenciones (fuente + fecha de verificación) |
| `runbooks/` | Instalación, backup/restore, release, migraciones, [activar graphify](runbooks/activar-graphify.md) |

## Convenciones del cerebro
- Una idea por nota, <200 líneas; enlazar en vez de duplicar.
- Toda decisión no trivial → ADR (`decisiones/_plantilla.md`).
- Actualizar `actualizado:` y este índice al crear/mover notas.
- `graphify-out/` (grafo del código) complementa este cerebro: el cerebro explica el **porqué**, el grafo el **qué está conectado con qué**.
