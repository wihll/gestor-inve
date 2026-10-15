---
estado: aceptado
fecha: 2026-10-15
---
# 0003 Persistencia y aislamiento multiempresa

## Contexto
Un ERP multiempresa filtra datos entre clientes si el aislamiento depende solo del código de aplicación: un `IgnoreQueryFilters()`, un informe con SQL crudo o una consulta por `psql` bastan para saltárselo.

## Decisión
1. **EF Core 10 + Npgsql**, contexto único `GestorInveDbContext` para todo el monolito modular.
2. **Aislamiento en dos capas**: filtro global de EF (comodidad) y **Row Level Security de PostgreSQL** (la defensa real). La política compara contra `current_setting('gestorinve.empresa_id')`, que publica `InterceptorEmpresaEnSesion` en cada apertura de conexión (el pool reutiliza conexiones entre empresas).
3. **`force row level security`** en toda tabla protegida: sin esto el dueño de la tabla ignora las políticas y el aislamiento sería falso en los despliegues de un solo rol, que es lo normal en una pyme.
4. **Cerrado por defecto**: sin empresa en la sesión no se ve ni se escribe nada. `nullif(..., '')::uuid` evita que la cadena vacía aborte el cast.
5. **Inversión de dependencia**: `Persistencia` no conoce los módulos; cada módulo aporta un `IModuloPersistencia` y el arranque los registra. Así los módulos siguen aislados entre sí y `Core` sigue sin ORM (hay un test de arquitectura que lo verifica).
6. **Auditoría por interceptor**: `InterceptorAuditoria` rellena el rastro de `IAuditable` con un `IReloj` inyectable y **sella** `EmpresaId` en las filas nuevas, para que el llamante no pueda elegir la empresa.
7. **Migraciones como código generado**: las escribe EF, así que `.editorconfig` las marca `generated_code = true`. No es una supresión sobre código propio.

## Pruebas: PostgreSQL real, no un doble
RLS y `jsonb` no existen en un proveedor en memoria, así que probarlos ahí daría una garantía falsa. **Testcontainers quedó descartado** porque el entorno de desarrollo usado no tiene demonio de Docker. En su lugar: PostgreSQL instalado en local y un contenedor de servicio en CI, con un **rol sin privilegios** (un superusuario o un rol con `BYPASSRLS` ignora las políticas; el CI lo verifica antes de correr las pruebas).

Las pruebas de integración se marcan `Categoria=Integracion`: el job de Windows no las corre, porque los contenedores de servicio de Actions solo existen en Linux. Un guardia en el CI falla si el filtro no ejecuta ninguna, para que renombrar la etiqueta no deje el aislamiento sin probar.

## Consecuencias
- El alta de empresas (onboarding) necesita un rol con `bypassrls`, porque la política de `empresas` exige que la sesión ya apunte a la empresa que se crea. **Pendiente de definir en H5.**
- `IContextoTenant` hoy se registra fijo y vacío; en H5 pasa a deducirse del usuario autenticado.
- Toda tabla nueva con `empresa_id` debe añadir su política RLS en la migración. Falta un test que falle si alguien olvida la política (**pendiente**).
