using System.Diagnostics;
using System.Runtime.InteropServices;
using GestorInve.Escritorio;

// Lanzador de escritorio: espera a que el servicio local responda y abre la aplicación en una
// ventana propia. No contiene lógica de negocio ni guarda nada; ver ADR 0011.
const string UrlPorOmision = "http://127.0.0.1:48620";

var destino = args.FirstOrDefault(a => !a.StartsWith('-'))
    ?? Environment.GetEnvironmentVariable("GESTORINVE_URL")
    ?? UrlPorOmision;

if (!Uri.TryCreate(destino, UriKind.Absolute, out var url) || !Navegador.EsLocal(url))
{
    Avisos.Error($"La dirección «{destino}» no es una dirección local de gestor-inve.");
    return 2;
}

if (!await Servicio.EsperarAsync(url, TimeSpan.FromSeconds(60)))
{
    Avisos.Error(
        "gestor-inve no está respondiendo.\n\n" +
        "El servicio puede estar arrancando todavía: espere un minuto y vuelva a abrirlo. " +
        "Si sigue igual, reinicie el equipo o revise que el servicio «GestorInve» esté iniciado.");
    return 1;
}

// `--solo-comprobar` deja probar el arranque en un equipo sin pantalla (el CI) sin abrir nada.
if (args.Contains("--solo-comprobar", StringComparer.Ordinal))
{
    return 0;
}

var esWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

var candidatos = esWindows
    ? Navegador.CandidatosWindows(Environment.GetEnvironmentVariable)
    : Navegador.CandidatosLinux(Environment.GetEnvironmentVariable("PATH"));

var navegador = Navegador.Elegir(candidatos, File.Exists);

if (navegador is null)
{
    // Sin navegador Chromium no hay ventana propia, pero la aplicación tiene que abrir igual.
    Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
    return 0;
}

// `Create` no es un detalle: sin él, `GetFolderPath` devuelve **cadena vacía** cuando la carpeta
// todavía no existe —una cuenta recién creada en Linux no tiene `~/.local/share`—, y el perfil
// acababa en la carpeta desde la que se lanzó el programa. Se vio al probarlo con un usuario nuevo.
var datosLocales = Environment.GetFolderPath(
    Environment.SpecialFolder.LocalApplicationData,
    Environment.SpecialFolderOption.Create);

if (string.IsNullOrEmpty(datosLocales))
{
    datosLocales = Path.GetTempPath();
}

var perfil = Navegador.CarpetaPerfil(
    navegador,
    datosLocales,
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

Directory.CreateDirectory(perfil);

var inicio = new ProcessStartInfo(navegador) { UseShellExecute = false };

foreach (var argumento in Navegador.Argumentos(url, perfil))
{
    inicio.ArgumentList.Add(argumento);
}

Process.Start(inicio);

return 0;
