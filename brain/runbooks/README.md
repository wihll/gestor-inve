---
estado: pendiente
---
# Runbooks
Instalación, backup/restore (pg_dump, WAL), release y migraciones: por escribir.

## Configuración obligatoria al desplegar (H5)
| Ajuste | Variable de entorno | Notas |
|---|---|---|
| Cadena de conexión | `ConnectionStrings__GestorInve` | Rol **sin** `bypassrls` y **sin** superusuario: un rol privilegiado ignora las políticas RLS y anula el aislamiento entre empresas. |
| Clave de firma de tokens | `Jwt__Clave` | ≥ 32 bytes aleatorios (`openssl rand -base64 48`). Sin ella el arranque falla a propósito. No reutilizar entre instalaciones. |
| Emisor y audiencia | `Jwt__Emisor`, `Jwt__Audiencia` | Opcionales; por defecto `gestor-inve`. |

Comprobaciones de salud: `/health` es liveness (no toca la base de datos) y `/ready` verifica la base. El orquestador debe usar `/health` para reinicios y `/ready` para enrutar tráfico.
