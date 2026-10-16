---
estado: pendiente
actualizado: 2026-10-15
---
# Módulo seguridad
Proyecto: `src/Modules/GestorInve.Seguridad` (referencia a `GestorInve.Core` y `GestorInve.Persistencia`).

- `Empresa`: raíz del aislamiento multiempresa (no pertenece a otra empresa, *es* la empresa; su política RLS filtra por `id`).
- `RegistroAuditoria`: asiento de auditoría por empresa, con carga útil `jsonb`.
- `ModuloSeguridadPersistencia`: configuración del modelo (esquemas `seguridad` y `auditoria`).
- Pendiente: usuarios, roles, permisos, autenticación y el rol con `bypassrls` para el alta de empresas (H5).
