using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GestorInve.Escritorio;

/// <summary>Consulta al servicio local hasta que esté listo.</summary>
internal static class Servicio
{
    /// <summary>
    /// Espera a que <c>/ready</c> responda bien. Justo después de encender el equipo el servicio
    /// puede tardar —arranca PostgreSQL y aplica migraciones—, y abrir la ventana antes mostraría
    /// un error del navegador que el usuario no sabría interpretar.
    /// </summary>
    internal static async Task<bool> EsperarAsync(Uri url, TimeSpan limite)
    {
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var lista = new Uri(url, "/ready");
        var reloj = Stopwatch.StartNew();

        while (reloj.Elapsed < limite)
        {
            try
            {
                using var respuesta = await cliente.GetAsync(lista);

                if (respuesta.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                // Todavía no escucha: es lo normal durante el arranque.
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return false;
    }
}

/// <summary>
/// Mensajes al usuario. El lanzador no tiene consola (en Windows es una aplicación de ventana),
/// así que un error escrito en la salida estándar no lo vería nadie.
/// </summary>
internal static partial class Avisos
{
    private const string Titulo = "gestor-inve";

    internal static void Error(string mensaje)
    {
        Console.Error.WriteLine(mensaje);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _ = MessageBoxW(IntPtr.Zero, mensaje, Titulo, 0x10 /* MB_ICONERROR */);
            return;
        }

        // En Linux no hay un cuadro de diálogo universal: se intenta con los que suelen estar.
        foreach (var (programa, argumentos) in new (string, string[])[]
                 {
                     ("zenity", ["--error", $"--title={Titulo}", $"--text={mensaje}"]),
                     ("kdialog", ["--title", Titulo, "--error", mensaje]),
                     ("notify-send", [Titulo, mensaje]),
                 })
        {
            try
            {
                var inicio = new ProcessStartInfo(programa) { UseShellExecute = false };

                foreach (var argumento in argumentos)
                {
                    inicio.ArgumentList.Add(argumento);
                }

                using var proceso = Process.Start(inicio);
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Ese programa no está instalado: se prueba el siguiente.
            }
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr ventana, string texto, string titulo, uint tipo);
}
