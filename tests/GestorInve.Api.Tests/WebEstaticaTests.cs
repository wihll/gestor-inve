using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace GestorInve.Api.Tests;

/// <summary>
/// En la instalación de escritorio la Api sirve también la web (ADR 0011). Lo que se prueba es
/// la frontera entre las dos: las rutas del enrutador del cliente devuelven el SPA, pero una
/// ruta de Api inexistente **sigue siendo un 404** y no un HTML con 200.
/// </summary>
[Collection(ColeccionApi.Nombre)]
[Trait("Categoria", "Integracion")]
public sealed class WebEstaticaTests : IDisposable
{
    private const string Marca = "<title>gestor-inve de prueba</title>";

    private readonly string carpeta = Path.Combine(Path.GetTempPath(), $"gestorinve-web-{Guid.NewGuid():N}");
    private readonly HttpClient cliente;

    public WebEstaticaTests(ApiFixture api)
    {
        ArgumentNullException.ThrowIfNull(api);

        Directory.CreateDirectory(Path.Combine(carpeta, "assets"));
        File.WriteAllText(Path.Combine(carpeta, "index.html"), $"<!doctype html><html><head>{Marca}</head></html>");
        File.WriteAllText(Path.Combine(carpeta, "assets", "app-abc123.js"), "console.log('ok');");

        cliente = api.WithWebHostBuilder(b => b.UseSetting("GESTORINVE_WEB", carpeta)).CreateClient();
    }

    public void Dispose()
    {
        cliente.Dispose();
        Directory.Delete(carpeta, recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/inventario")]
    [InlineData("/catalogo")]
    [InlineData("/kardex")]
    public async Task Las_rutas_del_cliente_devuelven_el_SPA_sin_cache(string ruta)
    {
        using var respuesta = await cliente.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("text/html", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Contains(Marca, await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Una copia vieja del índice apuntaría a bundles que la actualización ya borró.
        Assert.True(respuesta.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task Los_recursos_con_hash_se_sirven_como_inmutables()
    {
        using var respuesta = await cliente.GetAsync("/assets/app-abc123.js");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("immutable", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/no-existe")]
    [InlineData("/api")]
    [InlineData("/API/Inventario/nada")]
    public async Task Una_ruta_de_Api_inexistente_sigue_siendo_404_y_no_el_SPA(string ruta)
    {
        using var respuesta = await cliente.GetAsync(ruta);

        // Quien integra contra la Api espera un error, no una página con 200.
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.DoesNotContain(Marca, await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_POST_a_una_ruta_del_cliente_no_devuelve_el_SPA()
    {
        using var respuesta = await cliente.PostAsync("/inventario", content: null);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task La_Api_y_la_salud_siguen_respondiendo_con_la_web_activa()
    {
        using var lista = await cliente.GetAsync("/ready");
        using var sinSesion = await cliente.GetAsync("/api/auth/yo");

        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);
    }

    [Fact]
    public async Task Un_archivo_fuera_de_la_carpeta_web_no_se_puede_pedir()
    {
        using var respuesta = await cliente.GetAsync("/assets/..%2F..%2Fappsettings.json");

        // O 404 o el SPA, pero nunca el contenido de un archivo de fuera.
        Assert.DoesNotContain("Logging", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
