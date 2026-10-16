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

## Invariante: `default(Dinero)`
`Dinero` es un `struct`, así que `default` es inevitable (slots de arreglo, deserialización, acumuladores) y no pasa por el constructor. Se define como **cero sin moneda**: elemento neutro de la suma que adopta la moneda del otro operando (`EsIndefinido == true`, `Moneda == ""`, nunca `null`). Así `total += linea` sobre una lista funciona sin semilla con moneda, y dos monedas reales distintas siguen siendo error.

## Persistencia (H2)
- `src/GestorInve.Persistencia` — EF Core 10 + Npgsql, `snake_case`, contexto único. Referencia solo a `Core`.
- Aislamiento multiempresa en dos capas: filtro global de EF y **RLS** en PostgreSQL (`force row level security`). Detalle y consecuencias en [[decisiones/0003-persistencia-y-aislamiento]].
- Interceptores: `InterceptorEmpresaEnSesion` (publica la empresa en la sesión de PostgreSQL) y `InterceptorAuditoria` (rastro de `IAuditable` y sellado de `EmpresaId`).
- Migraciones en `src/GestorInve.Persistencia/Migraciones`; la fábrica de diseño vive en la Api, el único proyecto que ve todos los módulos.
- `Core` añade los contratos puros `IReloj`, `IContextoTenant`, `IEntidadDeEmpresa`, `IRaizDeEmpresa`, `IAuditable`.
- `PoliticasRlsTests` deriva del modelo de EF qué tablas deben estar protegidas, así que una tabla nueva sin política RLS rompe el CI en vez de dejar de aislar en silencio.
- Api: `/health` es liveness y no toca la base de datos; `/ready` sí la comprueba (verificado: con la base caída, `/health` 200 y `/ready` 503).

## Seguridad (H5)
- `Core/Seguridad`: `HasheadorContrasena` (PBKDF2-SHA512, 210k iteraciones, formato versionado) y la política de contraseñas. Sin dependencias.
- `Modules/GestorInve.Seguridad`: `Usuario` (con bloqueo por intentos), `Rol` y `Permiso` (RBAC `recurso:accion`), `UsuarioRol` **explícito** para que quede sujeto al RLS, `TokenRefresco` (hash, un solo uso, familias), `DirectorioEmpresa` (NIT → empresa, la única tabla sin RLS a propósito) y `ServicioAltaEmpresa`.
- `Api/Autenticacion`: JWT HS256 de 15 min sin margen de reloj, `/auth/login`, `/auth/refresh` (rotación y revocación de familia al detectar reutilización), `/auth/logout`, `/auth/yo`, `/empresas`. `ContextoTenantHttp` deduce la empresa **solo del token validado**.
- El arranque falla si falta la clave de firma o mide menos de 32 bytes.
- Detalle y motivos en [[decisiones/0004-autenticacion-y-autorizacion]].

## Catálogo (H6)
- `src/GestorInve.Composicion` — **raíz de composición**: la única lista de módulos con modelo de datos (`ModulosDePersistencia.Todos`). Es el único proyecto que referencia a todos los módulos.
- `src/Modules/GestorInve.Catalogo` — `UnidadMedida`, `Categoria`, `Producto`, `Variante`, `CodigoBarras`. Esquema `catalogo`, con RLS como el resto.
- `Core` añade `Unidad` y `Cantidad`: decimal con unidad, que rechaza más decimales de los que la unidad admite y redondea alejándose de cero al escalar.
- **La variante lleva el stock, nunca el producto**: el kardex de H7 apuntará a variantes. Motivos y ruta hacia las conversiones de unidad en [[decisiones/0005-catalogo-variantes-y-unidades]].
- `src/GestorInve.Api/Catalogo` — CRUD con permisos `catalogo:*`. Primer uso real de `ContextoTenantHttp` con datos de negocio; hay pruebas de que una empresa no alcanza los datos de otra **a través de la API**.

## Inventario (H7)
- `src/Modules/GestorInve.Inventario` — `Bodega`, `Ubicacion`, `Movimiento` (el kardex) y `Existencia`. Esquema `inventario`, con RLS y un **disparador que impide modificar o borrar un movimiento**.
- `Core` añade `SaldoValorizado`: toda la aritmética del costeo promedio, pura y probada con propiedades.
- Las existencias se cuentan por (variante, bodega, **ubicación**); toda bodega nace con su ubicación `GENERAL`.
- Concurrencia con bloqueo pesimista (`SELECT … FOR UPDATE`) sobre la celda; medido: sin él, 10 salidas simultáneas dejan 90 en vez de 50.
- **Las dependencias entre módulos ahora se declaran** en `ArquitecturaTests` (`Inventario → Catalogo`) y el grafo no puede tener ciclos. Motivos en [[decisiones/0006-kardex-inmutable-y-costeo]].
