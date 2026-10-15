using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorInve.Api.Autenticacion;
using GestorInve.Seguridad;
using Microsoft.AspNetCore.Mvc;

namespace GestorInve.Api.Tests;

[Collection(ColeccionApi.Nombre)]
[Trait("Categoria", "Integracion")]
public class AutenticacionTests(ApiFixture api)
{
    private const string Clave = "Clave-Muy-Larga-2026";

    [Fact]
    public async Task Alta_de_empresa_y_primer_acceso()
    {
        var (nit, correo) = await AltaAsync();

        var sesion = await LoginAsync(nit, correo, Clave);

        Assert.False(string.IsNullOrWhiteSpace(sesion.TokenAcceso));
        Assert.False(string.IsNullOrWhiteSpace(sesion.TokenRefresco));
        Assert.True(sesion.ExpiraEn > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task No_se_puede_registrar_dos_veces_el_mismo_NIT()
    {
        var (nit, _) = await AltaAsync();

        using var cliente = api.CreateClient();
        using var respuesta = await cliente.PostAsJsonAsync(
            "/empresas",
            new PeticionAlta(nit, "Otra S.A.S.", "otro@empresa.co", "Otro", Clave));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
    }

    /// <summary>
    /// Un NIT que no existe y una contraseña equivocada deben ser indistinguibles: si no,
    /// cualquiera podría averiguar qué empresas y qué usuarios están registrados.
    /// </summary>
    [Fact]
    public async Task Un_NIT_inexistente_y_una_clave_mala_responden_igual()
    {
        var (nit, correo) = await AltaAsync();

        using var cliente = api.CreateClient();

        using var nitFalso = await cliente.PostAsJsonAsync(
            "/auth/login",
            new PeticionLogin(ApiFixture.NitNuevo(), correo, Clave));
        using var claveMala = await cliente.PostAsJsonAsync(
            "/auth/login",
            new PeticionLogin(nit, correo, "Clave-Equivocada-2026"));
        using var usuarioFalso = await cliente.PostAsJsonAsync(
            "/auth/login",
            new PeticionLogin(nit, "nadie@empresa.co", Clave));

        Assert.Equal(HttpStatusCode.Unauthorized, nitFalso.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, claveMala.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, usuarioFalso.StatusCode);

        // Se compara el contenido significativo: `traceId` cambia en cada petición por diseño
        // y no dice nada del motivo del fallo.
        var cuerpos = new List<string>();
        foreach (var respuesta in new[] { nitFalso, claveMala, usuarioFalso })
        {
            var problema = await respuesta.Content.ReadFromJsonAsync<ProblemDetails>();
            cuerpos.Add($"{problema!.Status}|{problema.Title}|{problema.Detail}");
        }

        Assert.Single(cuerpos.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Tras_agotar_los_intentos_la_cuenta_se_bloquea_y_la_clave_correcta_tampoco_entra()
    {
        var (nit, correo) = await AltaAsync();
        using var cliente = api.CreateClient();

        for (var i = 0; i < Usuario.IntentosPermitidos; i++)
        {
            using var fallo = await cliente.PostAsJsonAsync(
                "/auth/login",
                new PeticionLogin(nit, correo, "Clave-Equivocada-2026"));
            Assert.Equal(HttpStatusCode.Unauthorized, fallo.StatusCode);
        }

        using var bloqueado = await cliente.PostAsJsonAsync("/auth/login", new PeticionLogin(nit, correo, Clave));

        Assert.Equal(HttpStatusCode.Locked, bloqueado.StatusCode);
    }

    [Fact]
    public async Task Sin_token_los_endpoints_protegidos_responden_401()
    {
        using var cliente = api.CreateClient();

        using var yo = await cliente.GetAsync(new Uri("/auth/yo", UriKind.Relative));
        using var usuarios = await cliente.GetAsync(new Uri("/usuarios", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, yo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, usuarios.StatusCode);
    }

    [Fact]
    public async Task El_token_identifica_al_usuario_y_trae_sus_permisos()
    {
        var (nit, correo) = await AltaAsync();
        var sesion = await LoginAsync(nit, correo, Clave);

        using var cliente = ClienteCon(sesion.TokenAcceso);
        var yo = await cliente.GetFromJsonAsync<RespuestaYo>("/auth/yo");

        Assert.NotNull(yo);
        Assert.Equal(correo, yo.Correo);
        Assert.NotEqual(Guid.Empty, yo.EmpresaId);

        // El primer usuario es administrador: recibe todos los permisos conocidos.
        Assert.Equal([.. Permiso.Todos.Order(StringComparer.Ordinal)], [.. yo.Permisos.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Un_endpoint_con_permiso_deja_pasar_al_administrador()
    {
        var (nit, correo) = await AltaAsync();
        var sesion = await LoginAsync(nit, correo, Clave);

        using var cliente = ClienteCon(sesion.TokenAcceso);
        using var respuesta = await cliente.GetAsync(new Uri("/usuarios", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_token_firmado_con_otra_clave_se_rechaza()
    {
        var (nit, correo) = await AltaAsync();
        var sesion = await LoginAsync(nit, correo, Clave);

        // Se altera la firma manteniendo cabecera y cuerpo.
        var partes = sesion.TokenAcceso.Split('.');
        var manipulado = $"{partes[0]}.{partes[1]}.{new string('A', partes[2].Length)}";

        using var cliente = ClienteCon(manipulado);
        using var respuesta = await cliente.GetAsync(new Uri("/auth/yo", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Dos_empresas_pueden_usar_el_mismo_correo_y_cada_token_queda_en_la_suya()
    {
        const string Compartido = "gerente@comun.co";
        var (nitA, _) = await AltaAsync(Compartido);
        var (nitB, _) = await AltaAsync(Compartido);

        var sesionA = await LoginAsync(nitA, Compartido, Clave);
        var sesionB = await LoginAsync(nitB, Compartido, Clave);

        using var clienteA = ClienteCon(sesionA.TokenAcceso);
        using var clienteB = ClienteCon(sesionB.TokenAcceso);

        var yoA = await clienteA.GetFromJsonAsync<RespuestaYo>("/auth/yo");
        var yoB = await clienteB.GetFromJsonAsync<RespuestaYo>("/auth/yo");

        Assert.NotEqual(yoA!.EmpresaId, yoB!.EmpresaId);
        Assert.NotEqual(yoA.UsuarioId, yoB.UsuarioId);
    }

    [Fact]
    public async Task El_usuario_de_una_empresa_no_entra_por_el_NIT_de_otra()
    {
        var (_, correoA) = await AltaAsync();
        var (nitB, _) = await AltaAsync();

        using var cliente = api.CreateClient();
        using var respuesta = await cliente.PostAsJsonAsync("/auth/login", new PeticionLogin(nitB, correoA, Clave));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    private HttpClient ClienteCon(string token)
    {
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return cliente;
    }

    private async Task<(string Nit, string Correo)> AltaAsync(string? correo = null)
    {
        var nit = ApiFixture.NitNuevo();
        var correoFinal = correo ?? $"admin-{Guid.NewGuid():N}@empresa.co";

        using var cliente = api.CreateClient();
        using var respuesta = await cliente.PostAsJsonAsync(
            "/empresas",
            new PeticionAlta(nit, "Pyme de Pruebas S.A.S.", correoFinal, "Administradora", Clave));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (nit, correoFinal);
    }

    private async Task<RespuestaSesion> LoginAsync(string nit, string correo, string contrasena)
    {
        using var cliente = api.CreateClient();
        using var respuesta = await cliente.PostAsJsonAsync("/auth/login", new PeticionLogin(nit, correo, contrasena));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSesion>())!;
    }
}
