---
estado: propuesto
actualizado: 2026-10-15
---
# Arquitectura
- Backend: .NET 9, ASP.NET Core, EF Core. Frontend: React + TypeScript + Vite. BD: PostgreSQL 16.
- Monolito modular: `Core` (dominio puro) + `Modules/*` + `Api` + `Worker`.
- Invariantes: kardex = event log inmutable (correcciones por reversa); saldos = proyección; `decimal`/`NUMERIC` siempre; multi-empresa con `tenant_id` + RLS; auditoría completa; concurrencia optimista en saldos.
- Distribución: Docker Compose + MSI (WiX) + .deb/.rpm; shell opcional Tauri/WebView2.
Pendiente de ratificar en [[decisiones/0001-stack]].
