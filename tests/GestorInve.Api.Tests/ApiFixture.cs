using GestorInve.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestorInve.Api.Tests;

/// <summary>
/// La Api completa contra PostgreSQL real. Es la única forma de comprobar los flujos de
/// autenticación de punta a punta: con un doble de base de datos, el aislamiento por RLS
/// y la rotación de tokens no se ejercitarían.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ClaveFirma = "clave-de-pruebas-con-mas-de-32-bytes-para-hmac-sha256";

    private static readonly string Cadena =
        Environment.GetEnvironmentVariable("GESTORINVE_CONEXION_TEST")
        ?? "Host=127.0.0.1;Port=5432;Database=gestorinve;Username=gestorinve;Password=gestorinve";

    public async Task InitializeAsync()
    {
        using var alcance = Services.CreateScope();
        var fabrica = alcance.ServiceProvider.GetRequiredService<FabricaContextoEmpresa>();

        await using var contexto = fabrica.Crear(null, "pruebas");
        await contexto.Database.MigrateAsync();
    }

    public new Task DisposeAsync()
    {
        base.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>NIT único por prueba: el directorio de empresas lo exige único y las pruebas comparten base.</summary>
    public static string NitNuevo() => $"NIT{Guid.NewGuid():N}"[..15];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:GestorInve", Cadena);
        builder.UseSetting($"{Api.Autenticacion.OpcionesJwt.Seccion}:Clave", ClaveFirma);
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionApi : ICollectionFixture<ApiFixture>
{
    public const string Nombre = "api";
}
