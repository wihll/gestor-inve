namespace GestorInve.Api;

/// <summary>
/// Sonda de salud para el <c>HEALTHCHECK</c> del contenedor. Consulta <c>/ready</c> —el que
/// toca la base de datos— en la instancia que ya está escuchando, y devuelve el código de
/// salida que Docker interpreta: 0 sana, 1 no sana.
/// </summary>
internal static class Sonda
{
    /// <summary>
    /// Se deriva de <c>ASPNETCORE_URLS</c> para que la sonda no quede desincronizada del puerto
    /// real. Si esa variable trae comodines (<c>0.0.0.0</c>, <c>+</c>, <c>*</c>), que no son
    /// direcciones a las que se pueda conectar, se sustituyen por el bucle local.
    /// </summary>
    internal static Uri Destino(string? urls)
    {
        var primera = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        // Los comodines se sustituyen **antes** de parsear. `http://+:5000` y `http://*:5000`
        // son formas que Kestrel acepta y que `Uri` no sabe leer: hacerlo después dejaría la
        // sonda en el puerto por omisión, apuntando a donde nadie escucha, y el contenedor
        // nunca se declararía sano. Lo descubrió esta prueba, no el runtime.
        var normalizada = primera is null
            ? null
            : System.Text.RegularExpressions.Regex.Replace(
                primera,
                @"^http://(\+|\*|0\.0\.0\.0|\[::\])(?=[:/]|$)",
                "http://127.0.0.1",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (normalizada is null || !Uri.TryCreate(normalizada, UriKind.Absolute, out var url))
        {
            return new Uri("http://127.0.0.1:8080/ready");
        }

        return new Uri($"http://{url.Host}:{url.Port}/ready");
    }

    public static async Task<int> ComprobarAsync()
    {
        var destino = Destino(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));

        // Tiempo corto a propósito: el `HEALTHCHECK` ya reintenta. Una sonda que se queda
        // colgada no informa de nada y mantiene el contenedor en «starting» indefinidamente.
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        try
        {
            using var respuesta = await cliente.GetAsync(destino);

            if (respuesta.IsSuccessStatusCode)
            {
                return 0;
            }

            await Console.Error.WriteLineAsync(
                $"{destino} respondió {(int)respuesta.StatusCode}.");

            return 1;
        }
        catch (Exception excepcion) when (excepcion is HttpRequestException or TaskCanceledException)
        {
            // Durante el arranque esto es lo normal y no es un fallo: el `start-period` del
            // `HEALTHCHECK` cubre esa ventana. Se traza para que `docker inspect` lo muestre.
            await Console.Error.WriteLineAsync($"{destino} no respondió: {excepcion.Message}");

            return 1;
        }
    }
}
