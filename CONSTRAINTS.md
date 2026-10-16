# CONSTRAINTS — contrato de calidad
Un cambio que debilite cualquiera de estos puntos requiere un ADR que lo justifique.

| Dimensión | Umbral | Cómo se verifica |
|---|---|---|
| Compilación | 0 warnings (`TreatWarningsAsErrors`) | CI `dotnet build -warnaserror` |
| Nulabilidad | `Nullable=enable` en todos los proyectos | `Directory.Build.props` |
| Tipos monetarios | Sin `float`/`double` en dominio | Test de arquitectura |
| Cobertura de dominio | ≥ 90 % líneas en `GestorInve.Core` | CI, umbral bloqueante (coverlet). Hoy: 100 % líneas, 92,85 % ramas |
| Tests | Sin `[Fact(Skip=...)]`, sin tests eliminados para dar verde | Revisión de diff |
| Supresiones | Sin `#pragma warning disable` ni `[SuppressMessage]` nuevos sin ADR | Revisión de diff |
| Dependencias | Licencia compatible con AGPL; sin vulnerabilidades conocidas altas | Dependabot + `dotnet list package --vulnerable` |
| Arquitectura | `Core` sin dependencias; los módulos solo dependen de lo que **declaran**, y el grafo no tiene ciclos (ADR 0006) | NetArchTest |
| Portabilidad | Build y pruebas unitarias verdes en Ubuntu y Windows | Matriz de CI |
| Aislamiento multiempresa | RLS activo y probado contra PostgreSQL real con un rol sin privilegios | Job de integración (el CI verifica el rol y que las pruebas se ejecuten) |
| Política RLS por tabla | Toda entidad `IEntidadDeEmpresa`/`IRaizDeEmpresa` con `enable` + `force` + política que consulta la sesión | `PoliticasRlsTests`, derivado del modelo de EF |
| Contraseñas | PBKDF2-SHA512 ≥ 210.000 iteraciones, sal por contraseña, comparación en tiempo constante | `HasheadorContrasenaTests` |
| Respuestas de acceso | Un usuario, un NIT inexistente y una clave errónea responden igual y cuestan igual | `AutenticacionTests` |
| Tokens | Acceso ≤ 15 min sin margen de reloj; refresco hasheado, de un solo uso y con revocación de familia | `OpcionesJwtTests`, `RefrescoTests` |
| Clave de firma | Obligatoria y ≥ 32 bytes; sin ella el arranque falla | `OpcionesJwtTests` + arranque verificado |
| Empresa de la petición | Solo desde el token validado, nunca desde entrada del cliente | `ContextoTenantHttp` + `AutenticacionTests` |
| Escala de cantidades | Una `Cantidad` nunca excede los decimales de su unidad | `CantidadTests` + propiedades con FsCheck |
| Aislamiento por la API | Una petición autenticada no alcanza datos de otra empresa (404, no 403) | `CatalogoTests` |
| El kardex cuadra | La suma firmada de los movimientos es igual a las existencias, también bajo concurrencia | `InventarioTests` |
| Kardex inmutable | Ni `UPDATE` ni `DELETE` sobre un movimiento, garantizado por disparador en la base | `InventarioTests` (SQL crudo) |
| Existencias negativas | Solo con `inventario:ajustar`, y el movimiento queda marcado | `InventarioTests` |
| Contrato de la Api | Los tipos de la web se generan del OpenAPI; un cambio de contrato rompe el build | Job `web` del CI (`tsc --noEmit`) |
| Sesión en la web | Un solo refresco en vuelo; tres 401 simultáneos no pueden provocar más de un refresco | `tests/cliente.test.ts` |
| Flujo de usuario | Registro, entrada, salida y kardex cuadrado, en navegador real contra la Api | Job `e2e` del CI |
| Código generado | Las migraciones de EF se marcan `generated_code`; no cuenta como supresión | `.editorconfig` + ADR 0003 |
| Un solo camino de actualización | El esquema se aplica con `GESTORINVE_MIGRAR=true`; nunca por omisión. Lo usan el CI, el contenedor y el instalador | Job `compose` del CI (cuenta las tablas) + ADR 0008 |
| Rol de la base en la instalación | `scripts/init-db.sh` crea el rol sin `SUPERUSER` ni `BYPASSRLS` y **aborta** si quedó privilegiado | El propio script + job `compose` |
| Secretos sin valor por omisión | El compose falla si faltan `POSTGRES_CLAVE`, `GESTORINVE_CLAVE_BD` o `JWT_CLAVE` | Job `compose` (comprobación invertida: sin `.env` debe fallar) |
| Despliegue ejecutable | El `docker-compose.yml` se construye y se levanta en cada PR; la web y la Api responden en el mismo origen | Job `compose` del CI |
| Análisis de seguridad | CodeQL con `security-extended` sobre C#, TypeScript y los propios workflows | `codeql.yml`, semanal y por PR |
