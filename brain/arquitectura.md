---
estado: propuesto
actualizado: 2026-10-15
---
# Arquitectura
- Backend: .NET 10, ASP.NET Core, EF Core. Frontend: React + TypeScript + Vite. BD: PostgreSQL 16.
- Monolito modular: `Core` (dominio puro) + `Modules/*` + `Api` + `Worker`.
- Invariantes: kardex = event log inmutable (correcciones por reversa); saldos = proyección; `decimal`/`NUMERIC` siempre; multi-empresa con `tenant_id` + RLS; auditoría completa; concurrencia optimista en saldos.
- Distribución: Docker Compose + MSI (WiX) + .deb/.rpm; shell opcional Tauri/WebView2.
Pendiente de ratificar en [[decisiones/0001-stack]].

## Estructura real (H1)
- `GestorInve.slnx`, `Directory.Build.props` (nullable, warnings-as-errors, analizadores), `Directory.Packages.props` (versiones centralizadas), `global.json` (SDK 10).
- `src/GestorInve.Core` — `Dinero` (decimal + moneda ISO), `IEventoDominio`. Sin dependencias.
- `src/Modules/GestorInve.{Catalogo,Inventario,Compras,Ventas,Terceros,Contabilidad,FacturaElectronica,Seguridad}` — solo referencian a Core.
- `src/GestorInve.Api` — ASP.NET Core minimal API: `/health`, OpenAPI en Development. Es el único que referencia a los módulos.
- `src/GestorInve.Worker` — host vacío para jobs.
- `tests/` — `Core.Tests` (TDD de dominio), `Api.Tests` (WebApplicationFactory), `Architecture.Tests` (NetArchTest: Core sin dependencias, módulos aislados, sin float/double en Core).
- Pendiente: `Cantidad` y unidades (H6), FsCheck (H7), EF Core/Postgres (H2).
