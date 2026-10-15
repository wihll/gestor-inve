# CONSTRAINTS — contrato de calidad
Un cambio que debilite cualquiera de estos puntos requiere un ADR que lo justifique.

| Dimensión | Umbral | Cómo se verifica |
|---|---|---|
| Compilación | 0 warnings (`TreatWarningsAsErrors`) | CI `dotnet build -warnaserror` |
| Nulabilidad | `Nullable=enable` en todos los proyectos | `Directory.Build.props` |
| Tipos monetarios | Sin `float`/`double` en dominio | Test de arquitectura |
| Cobertura de dominio | ≥ 90 % líneas en `GestorInve.Core` | CI la mide y publica (coverlet); **umbral bloqueante pendiente (H2)** |
| Tests | Sin `[Fact(Skip=...)]`, sin tests eliminados para dar verde | Revisión de diff |
| Supresiones | Sin `#pragma warning disable` ni `[SuppressMessage]` nuevos sin ADR | Revisión de diff |
| Dependencias | Licencia compatible con AGPL; sin vulnerabilidades conocidas altas | Dependabot + `dotnet list package --vulnerable` |
| Arquitectura | `Core` sin dependencias; módulos no se referencian | NetArchTest |
| Portabilidad | Build y tests verdes en Ubuntu y Windows | Matriz de CI |
