namespace GestorInve.Api.Tests;

/// <summary>
/// La sonda del <c>HEALTHCHECK</c> deriva su destino de <c>ASPNETCORE_URLS</c>. Lo que se prueba
/// aquí es el caso que rompe el contenedor sin que nada lo avise: `0.0.0.0` es una dirección de
/// escucha, **no** una a la que se pueda conectar. Si la sonda la usara tal cual, fallaría
/// siempre, el contenedor nunca se declararía sano y `docker compose up --wait` se quedaría
/// esperando sin decir por qué.
/// </summary>
public class SondaTests
{
    [Theory]
    [InlineData("http://0.0.0.0:8080", "http://127.0.0.1:8080/ready")]
    [InlineData("http://+:5000", "http://127.0.0.1:5000/ready")]
    [InlineData("http://*:5000", "http://127.0.0.1:5000/ready")]
    public void Resuelve_los_comodines_al_bucle_local(string urls, string esperado) =>
        Assert.Equal(esperado, Sonda.Destino(urls).ToString());

    [Fact]
    public void Respeta_el_puerto_declarado() =>
        Assert.Equal("http://127.0.0.1:9123/ready", Sonda.Destino("http://127.0.0.1:9123").ToString());

    /// <summary>
    /// Con varias direcciones se toma la primera en HTTP: la sonda corre dentro del contenedor,
    /// donde un HTTPS con certificado propio solo añadiría una forma de fallar que no dice nada
    /// sobre la salud de la aplicación.
    /// </summary>
    [Fact]
    public void Prefiere_la_primera_direccion_http() =>
        Assert.Equal(
            "http://127.0.0.1:8080/ready",
            Sonda.Destino("https://0.0.0.0:8443;http://0.0.0.0:8080").ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://0.0.0.0:8443")]
    [InlineData("esto-no-es-una-url")]
    public void Sin_una_direccion_utilizable_cae_en_el_puerto_por_omision(string? urls) =>
        Assert.Equal("http://127.0.0.1:8080/ready", Sonda.Destino(urls).ToString());
}
