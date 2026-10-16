using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestorInve.Api.Tests;

/// <summary>
/// <c>/health</c> es liveness y no debe tocar la base de datos: si la tumbara un fallo de BD,
/// el orquestador reiniciaría procesos sanos en vez de esperar a que la BD vuelva.
/// </summary>
public class SaludTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_responde_200_sin_base_de_datos_configurada()
    {
        using var cliente = factory.CreateClient();

        using var respuesta = await cliente.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync());
    }
}
