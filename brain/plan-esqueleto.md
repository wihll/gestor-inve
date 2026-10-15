---
estado: propuesto
actualizado: 2026-10-15
---
# Plan de creación del proyecto (esqueleto + Fase 1)
Supuestos (ratificar en [[decisiones/0001-stack]] y ADR 0002 licencia): .NET 10 + PostgreSQL 16 + React/TS, AGPLv3. Metodología: spec-driven → planning → incremental + TDD → review.

## Hitos (cada uno = 1 PR pequeño, CI verde)
| # | Hito | Entregable | Criterio de salida |
|---|---|---|---|
| H0 | Gobierno | LICENSE, ADR 0002, CONTRIBUTING, CONSTRAINTS.md, plantillas issue/PR, `.editorconfig` | Repo con reglas y umbrales de calidad escritos |
| H1 | Solución .NET | `GestorInve.sln`: `Core`, `Modules.*`, `Api`, `Worker`, `tests/*`; Directory.Build.props (nullable, warnings-as-errors, analizadores) | `dotnet build/test` verde |
| H2 | Persistencia | EF Core + Npgsql, migraciones, `tenant_id` + RLS, auditoría, Testcontainers | Test de integración contra Postgres real |
| H3 | Web | `web/` Vite + React + TS, cliente OpenAPI generado, layout base, i18n es-CO | `npm test` y build verdes |
| H4 | Docker/CI | `docker-compose.yml` (db, api, web), GitHub Actions matriz ubuntu+windows, CodeQL, Dependabot | Pipeline verde en ambos SO |
| H5 | Seguridad | JWT + refresh, RBAC, usuarios/roles/empresas, auditoría de accesos | Tests de autorización; revisión de seguridad |
| H6 | Catálogo | Productos, unidades y conversiones, categorías, códigos de barras, lotes/seriales | CRUD + validaciones con tests de dominio |
| H7 | Bodegas y kardex | Bodegas/ubicaciones; movimientos (entrada, salida, traslado, ajuste); kardex inmutable con reversas | Property-based tests de invariantes |
| H8 | Costeo | Promedio ponderado y PEPS con casos auditados | Suite de casos validada por un contador |
| H9 | Reportes e import | Existencias, kardex, valorización, stock mínimo; import/export XLSX/CSV | Reportes cuadran con kardex |
| H10 | Distribución | Instalador MSI (WiX) y .deb/.rpm, publish self-contained, guía de instalación | Instalación limpia en Windows y Ubuntu |

## Orden y dependencias
H0 → H1 → (H2 ∥ H3) → H4 → H5 → H6 → H7 → H8 → H9 → H10. H4 puede adelantarse tras H1 para tener CI desde el primer commit de código.

## Primer sprint (esta semana)
H0 + H1 + H4 mínimo (build/test en CI). Después de H1 correr `/graphify .` para el primer mapa.

## Reglas por hito
- Antes de codificar: spec corta en `brain/modulos/<m>.md` y tareas en un GitHub Project.
- TDD en dominio (kardex, costeo). `decimal` siempre.
- Al cerrar: ADRs nuevos, actualizar `brain/INDEX.md`, `/ponytail-review` y `/code-review`.

## Riesgos
Alcance (mitigar con hitos pequeños), errores de costeo (property tests + validación contable), instalador Windows (probar en VM temprano).
