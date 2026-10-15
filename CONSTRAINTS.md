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
| Arquitectura | `Core` sin dependencias; módulos no se referencian | NetArchTest |
| Portabilidad | Build y pruebas unitarias verdes en Ubuntu y Windows | Matriz de CI |
| Aislamiento multiempresa | RLS activo y probado contra PostgreSQL real con un rol sin privilegios | Job de integración (el CI verifica el rol y que las pruebas se ejecuten) |
| Política RLS por tabla | Toda entidad `IEntidadDeEmpresa`/`IRaizDeEmpresa` con `enable` + `force` + política que consulta la sesión | `PoliticasRlsTests`, derivado del modelo de EF |
| Contraseñas | PBKDF2-SHA512 ≥ 210.000 iteraciones, sal por contraseña, comparación en tiempo constante | `HasheadorContrasenaTests` |
| Respuestas de acceso | Un usuario, un NIT inexistente y una clave errónea responden igual y cuestan igual | `AutenticacionTests` |
| Tokens | Acceso ≤ 15 min sin margen de reloj; refresco hasheado, de un solo uso y con revocación de familia | `OpcionesJwtTests`, `RefrescoTests` |
| Clave de firma | Obligatoria y ≥ 32 bytes; sin ella el arranque falla | `OpcionesJwtTests` + arranque verificado |
| Empresa de la petición | Solo desde el token validado, nunca desde entrada del cliente | `ContextoTenantHttp` + `AutenticacionTests` |
| Código generado | Las migraciones de EF se marcan `generated_code`; no cuenta como supresión | `.editorconfig` + ADR 0003 |
