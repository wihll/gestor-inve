using GestorInve.Core;
using GestorInve.Persistencia;
using GestorInve.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Persistencia.Tests;

/// <summary>Reloj congelado: la auditoría se verifica contra un valor conocido.</summary>
internal sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset Ahora { get; } = ahora;
}

/// <summary>
/// PostgreSQL real, no un doble: las políticas RLS y <c>jsonb</c> no existen en un proveedor
/// en memoria, así que probarlas ahí daría una garantía falsa. La cadena sale de
/// <c>GESTORINVE_CONEXION_TEST</c>; cada ejecución usa su propia base y la elimina al terminar.
/// </summary>
public sealed class BaseDatosFixture : IAsyncLifetime
{
    private static readonly string CadenaServidor =
        Environment.GetEnvironmentVariable("GESTORINVE_CONEXION_TEST")
        ?? "Host=127.0.0.1;Port=5432;Database=gestorinve;Username=gestorinve;Password=gestorinve";

    public string Cadena { get; private set; } = CadenaServidor;

    public async Task InitializeAsync()
    {
        await using var contexto = Crear(null);
        await contexto.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Contexto como lo arma la aplicación: con los dos interceptores.</summary>
    public GestorInveDbContext Crear(Guid? empresaId, IReloj? reloj = null, string usuario = "pruebas")
    {
        var tenant = new ContextoTenantFijo(empresaId, usuario);

        var opciones = new DbContextOptionsBuilder<GestorInveDbContext>()
            .UseNpgsql(Cadena)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new InterceptorAuditoria(reloj ?? new RelojSistema(), tenant),
                new InterceptorEmpresaEnSesion(tenant))
            .Options;

        return new GestorInveDbContext(opciones, tenant, [new ModuloSeguridadPersistencia()]);
    }

    /// <summary>Crea una empresa y devuelve su id. El alta se hace con la sesión puesta en ella misma.</summary>
    public async Task<Guid> CrearEmpresaAsync(string nombre)
    {
        var id = Guid.CreateVersion7();
        await using var contexto = Crear(id);

        // Sufijo aleatorio, no derivado del id: los primeros caracteres de un GUID v7 son la marca
        // de tiempo, así que dos empresas creadas en el mismo milisegundo chocarían con el NIT único.
        contexto.Add(new Empresa(id, $"NIT-{Guid.NewGuid():N}"[..20], nombre));
        await contexto.SaveChangesAsync();

        return id;
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionBaseDatos : ICollectionFixture<BaseDatosFixture>
{
    public const string Nombre = "postgres";
}
