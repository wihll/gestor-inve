using System.Net;
using System.Net.Http.Json;
using GestorInve.Api.Autenticacion;

namespace GestorInve.Api.Tests;

/// <summary>
/// El token de refresco es la credencial de larga vida: si se roba y sigue sirviendo, el
/// atacante mantiene el acceso indefinidamente. De ahí la rotación en cada uso y la revocación
/// de la cadena completa en cuanto se detecta una reutilización.
/// </summary>
[Collection(ColeccionApi.Nombre)]
[Trait("Categoria", "Integracion")]
public class RefrescoTests(ApiFixture api)
{
    private const string Clave = "Clave-Muy-Larga-2026";

    [Fact]
    public async Task Refrescar_entrega_tokens_nuevos()
    {
        var sesion = await SesionNuevaAsync();

        var renovada = await RefrescarAsync(sesion.TokenRefresco);

        Assert.NotEqual(sesion.TokenRefresco, renovada.TokenRefresco);
        Assert.False(string.IsNullOrWhiteSpace(renovada.TokenAcceso));

        // El token nuevo sirve para pedir datos.
        using var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new("Bearer", renovada.TokenAcceso);
        using var yo = await cliente.GetAsync(new Uri("/auth/yo", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, yo.StatusCode);
    }

    [Fact]
    public async Task El_token_de_refresco_anterior_deja_de_servir()
    {
        var sesion = await SesionNuevaAsync();
        await RefrescarAsync(sesion.TokenRefresco);

        using var cliente = api.CreateClient();
        using var reintento = await cliente.PostAsJsonAsync(
            "/auth/refresh",
            new PeticionRefresco(sesion.TokenRefresco));

        Assert.Equal(HttpStatusCode.Unauthorized, reintento.StatusCode);
    }

    /// <summary>
    /// Escenario de robo: el atacante usa un token ya consumido. No se puede saber si el que
    /// sigue vivo lo tiene la víctima o el atacante, así que caen los dos y la víctima
    /// tendrá que volver a entrar con su contraseña.
    /// </summary>
    [Fact]
    public async Task Reutilizar_un_token_consumido_revoca_la_cadena_completa()
    {
        var sesion = await SesionNuevaAsync();
        var renovada = await RefrescarAsync(sesion.TokenRefresco);

        using var cliente = api.CreateClient();

        using var replay = await cliente.PostAsJsonAsync(
            "/auth/refresh",
            new PeticionRefresco(sesion.TokenRefresco));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // El token legítimo también queda inservible: la familia entera fue revocada.
        using var legitimo = await cliente.PostAsJsonAsync(
            "/auth/refresh",
            new PeticionRefresco(renovada.TokenRefresco));

        Assert.Equal(HttpStatusCode.Unauthorized, legitimo.StatusCode);
    }

    [Fact]
    public async Task Cerrar_sesion_invalida_el_refresco()
    {
        var sesion = await SesionNuevaAsync();

        using var cliente = api.CreateClient();
        using var salida = await cliente.PostAsJsonAsync("/auth/logout", new PeticionRefresco(sesion.TokenRefresco));
        Assert.Equal(HttpStatusCode.NoContent, salida.StatusCode);

        using var reintento = await cliente.PostAsJsonAsync(
            "/auth/refresh",
            new PeticionRefresco(sesion.TokenRefresco));

        Assert.Equal(HttpStatusCode.Unauthorized, reintento.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-punto")]
    [InlineData("no-es-guid.secreto")]
    [InlineData("00000000000000000000000000000000.secreto-inventado")]
    public async Task Un_token_de_refresco_invalido_se_rechaza_sin_romper_nada(string token)
    {
        using var cliente = api.CreateClient();

        using var respuesta = await cliente.PostAsJsonAsync("/auth/refresh", new PeticionRefresco(token));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    /// <summary>El valor del refresco no debe aparecer en claro en la base de datos.</summary>
    [Fact]
    public async Task El_refresco_se_guarda_hasheado()
    {
        var sesion = await SesionNuevaAsync();
        var secreto = sesion.TokenRefresco.Split('.', 2)[1];

        var cadena = Environment.GetEnvironmentVariable("GESTORINVE_CONEXION_TEST")
            ?? "Host=127.0.0.1;Port=5432;Database=gestorinve;Username=gestorinve;Password=gestorinve";

        await using var conexion = new Npgsql.NpgsqlConnection(cadena);
        await conexion.OpenAsync();

        await using var comando = conexion.CreateCommand();
        comando.CommandText = "select count(*)::int from seguridad.tokens_refresco where hash_token = @valor";
        comando.Parameters.AddWithValue("@valor", secreto);

        Assert.Equal(0, (int)(await comando.ExecuteScalarAsync())!);
    }

    private async Task<RespuestaSesion> SesionNuevaAsync()
    {
        var nit = ApiFixture.NitNuevo();
        var correo = $"admin-{Guid.NewGuid():N}@empresa.co";

        using var cliente = api.CreateClient();
        using var alta = await cliente.PostAsJsonAsync(
            "/empresas",
            new PeticionAlta(nit, "Pyme de Pruebas S.A.S.", correo, "Administradora", Clave));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        using var login = await cliente.PostAsJsonAsync("/auth/login", new PeticionLogin(nit, correo, Clave));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return (await login.Content.ReadFromJsonAsync<RespuestaSesion>())!;
    }

    private async Task<RespuestaSesion> RefrescarAsync(string tokenRefresco)
    {
        using var cliente = api.CreateClient();
        using var respuesta = await cliente.PostAsJsonAsync("/auth/refresh", new PeticionRefresco(tokenRefresco));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSesion>())!;
    }
}
