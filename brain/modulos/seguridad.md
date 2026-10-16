---
estado: parcial
actualizado: 2026-10-15
---
# Módulo Seguridad
Proyecto: `src/Modules/GestorInve.Seguridad` (referencia a `GestorInve.Core` y `GestorInve.Persistencia`).

## Entidades
- `Empresa`: raíz del aislamiento (es la empresa, no pertenece a otra; su política RLS filtra por `id`).
- `Usuario`: credenciales, estado y **bloqueo por intentos fallidos**. Decide el acceso con `IntentarAutenticar`.
- `Rol` + `Permiso`: RBAC con permisos `recurso:accion`; los roles son de cada empresa.
- `UsuarioRol`: asignación **explícita** (no tabla de unión implícita) para que tenga `empresa_id` y política RLS.
- `TokenRefresco`: hash del token, un solo uso, familias para revocar la cadena.
- `RegistroAuditoria`: asiento de auditoría por empresa, carga útil `jsonb`.
- `DirectorioEmpresa`: NIT → empresa. **Única tabla sin RLS, a propósito** (ver su documentación y el ADR 0004).

## Servicios
- `ServicioAltaEmpresa`: alta de empresa con su primer administrador y resolución de NIT. No requiere privilegios especiales.

## Pendiente
- Alta y gestión de usuarios y roles por el administrador.
- Tabla consultable de auditoría de accesos (hoy solo `ILogger` y el contador del usuario).
- Recuperación de contraseña, segundo factor, límite de peticiones por IP.
