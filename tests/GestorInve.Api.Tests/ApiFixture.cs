using System.IdentityModel.Tokens.Jwt;
using System.Text;
using GestorInve.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GestorInve.Api.Tests;

/// <summary>
/// La Api completa contra PostgreSQL real. Es la única forma de comprobar los flujos de
/// autenticación de punta a punta: con un doble de base de datos, el aislamiento por RLS
/// y la rotación de tokens no se ejercitarían.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ClaveFirma = "clave-de-pruebas-con-mas-de-32-bytes-para-hmac-sha256";

    /// <summary>Expuesta para las pruebas que necesitan comprobar algo por SQL crudo.</summary>
    public static string CadenaConexion { get; } =
        Environment.GetEnvironmentVariable("GESTORINVE_CONEXION_TEST")
        ?? "Host=127.0.0.1;Port=5432;Database=gestorinve;Username=gestorinve;Password=gestorinve";

    public async Task InitializeAsync()
    {
        using var alcance = Services.CreateScope();
        var fabrica = alcance.ServiceProvider.GetRequiredService<FabricaContextoEmpresa>();

        await using var contexto = fabrica.Crear(null, "pruebas");
        // Serializada: este ensamblado y el otro de integración migran la misma base a la vez.
        await MigracionSerializada.AplicarAsync(contexto);
    }

    public new Task DisposeAsync()
    {
        base.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Mismas opciones de JSON que usa la Api: los enum viajan como texto. Si las pruebas
    /// deserializaran con las opciones por omisión, estarían comprobando un contrato distinto
    /// del que ven los clientes reales.
    /// </summary>
    public static System.Text.Json.JsonSerializerOptions Json { get; } = new(
        System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>NIT único por prueba: el directorio de empresas lo exige único y las pruebas comparten base.</summary>
    public static string NitNuevo() => $"NIT{Guid.NewGuid():N}"[..15];

    /// <summary>
    /// Reemite un token válido (misma firma, mismo usuario y empresa) **sin las reclamaciones de
    /// permiso**, para comprobar que las políticas de autorización se aplican de verdad y no
    /// basta con estar autenticado. Se firma aquí porque no hay endpoint que emita tokens
    /// degradados: crearlo solo para las pruebas sería una puerta que no debe existir.
    /// </summary>
    public static string TokenSinPermisos(string tokenAcceso, params string[] permisosAQuitar)
    {
        var original = new JwtSecurityTokenHandler().ReadJwtToken(tokenAcceso);

        // Sin argumentos se quitan todos los permisos; con argumentos, solo los indicados,
        // para poder probar que falta **ese** permiso y no simplemente que falta autenticación.
        var claims = original.Claims
            .Where(c => c.Type != Api.Autenticacion.ClaimsGestorInve.Permiso
                        || (permisosAQuitar.Length > 0 && !permisosAQuitar.Contains(c.Value, StringComparer.Ordinal)))
            .ToList();

        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ClaveFirma));

        var token = new JwtSecurityToken(
            issuer: original.Issuer,
            audience: original.Audiences.First(),
            claims: claims,
            notBefore: original.ValidFrom,
            expires: original.ValidTo,
            signingCredentials: new SigningCredentials(clave, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:GestorInve", CadenaConexion);
        builder.UseSetting($"{Api.Autenticacion.OpcionesJwt.Seccion}:Clave", ClaveFirma);
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionApi : ICollectionFixture<ApiFixture>
{
    public const string Nombre = "api";
}
