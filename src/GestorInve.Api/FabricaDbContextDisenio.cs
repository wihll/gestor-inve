using GestorInve.Core;
using GestorInve.Persistencia;
using GestorInve.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GestorInve.Api;

/// <summary>
/// Fábrica para las herramientas de EF (<c>dotnet ef migrations</c>). Vive en la Api porque es
/// el único proyecto que referencia a todos los módulos; así <c>Persistencia</c> sigue sin conocerlos.
/// La cadena sale de <c>GESTORINVE_CONEXION</c> y solo se usa en tiempo de diseño.
/// </summary>
public sealed class FabricaDbContextDisenio : IDesignTimeDbContextFactory<GestorInveDbContext>
{
    public GestorInveDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("GESTORINVE_CONEXION")
            ?? "Host=127.0.0.1;Port=5432;Database=gestorinve;Username=gestorinve;Password=gestorinve";

        var opciones = new DbContextOptionsBuilder<GestorInveDbContext>()
            .UseNpgsql(cadena)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GestorInveDbContext(
            opciones,
            new ContextoTenantFijo(null, "migraciones"),
            ModulosDePersistencia.Todos);
    }
}

/// <summary>Registro único de los módulos que aportan modelo de datos.</summary>
public static class ModulosDePersistencia
{
    public static IReadOnlyList<IModuloPersistencia> Todos { get; } =
        [new ModuloSeguridadPersistencia()];
}
