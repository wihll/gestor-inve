namespace GestorInve.Escritorio;

/// <summary>
/// Decide con qué navegador se abre la ventana de la aplicación y con qué argumentos.
///
/// La ventana propia es el **modo aplicación** de un navegador de la familia Chromium
/// (<c>--app=URL</c>): sin barra de direcciones ni pestañas, con su icono en la barra de tareas.
/// No se empaqueta un motor de navegador (ADR 0011): Edge viene en todo Windows 10 y 11, y un
/// motor propio serían cientos de megas y un componente más que actualizar por seguridad.
///
/// Toda la lógica es pura —recibe cómo comprobar si un archivo existe— para poder probarla sin
/// depender de qué haya instalado en la máquina que corre las pruebas.
/// </summary>
internal static class Navegador
{
    /// <summary>Rutas habituales en Windows, en orden de preferencia. Edge primero: siempre está.</summary>
    internal static IEnumerable<string> CandidatosWindows(Func<string, string?> entorno)
    {
        string?[] raices = [entorno("ProgramFiles(x86)"), entorno("ProgramFiles"), entorno("LocalAppData")];
        string[] relativos =
        [
            @"Microsoft\Edge\Application\msedge.exe",
            @"Google\Chrome\Application\chrome.exe",
            @"BraveSoftware\Brave-Browser\Application\brave.exe",
        ];

        return
            from relativo in relativos
            from raiz in raices
            where !string.IsNullOrWhiteSpace(raiz)
            select Path.Combine(raiz, relativo);
    }

    /// <summary>Ejecutables habituales en Linux, buscados en cada carpeta del <c>PATH</c>.</summary>
    internal static IEnumerable<string> CandidatosLinux(string? path)
    {
        string[] nombres =
        [
            "chromium", "chromium-browser", "google-chrome", "google-chrome-stable",
            "microsoft-edge", "microsoft-edge-stable", "brave-browser",
        ];

        var carpetas = (path ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries);

        // Con `/` explícito y no `Path.Combine`: son rutas de Linux sea cual sea el sistema que
        // ejecute este código, y en Windows `Path.Combine` las armaría con `\`.
        return from nombre in nombres from carpeta in carpetas select $"{carpeta.TrimEnd('/')}/{nombre}";
    }

    /// <summary>
    /// Carpeta del perfil propio. El caso que obliga a esta función es el Chromium de Ubuntu, que
    /// es un **snap**: por su confinamiento no puede escribir en carpetas ocultas del home, así
    /// que `~/.local/share/...` le está vedado y arrancaría con error. Los snaps sí pueden
    /// escribir en su propia carpeta `~/snap/&lt;nombre&gt;/common`.
    /// </summary>
    internal static string CarpetaPerfil(string navegador, string datosLocales, string home)
    {
        const string RaizSnap = "/snap/bin/";

        if (navegador.StartsWith(RaizSnap, StringComparison.Ordinal))
        {
            var snap = navegador[RaizSnap.Length..];

            return $"{home.TrimEnd('/')}/snap/{snap}/common/GestorInve";
        }

        return Path.Combine(datosLocales, "GestorInve", "perfil");
    }

    /// <summary>El primer candidato que existe, o <c>null</c> si no hay ningún navegador Chromium.</summary>
    internal static string? Elegir(IEnumerable<string> candidatos, Func<string, bool> existe) =>
        candidatos.FirstOrDefault(existe);

    /// <summary>
    /// Argumentos del modo aplicación. El perfil es **propio de gestor-inve** y no el del usuario:
    /// la sesión guarda su token de refresco en el almacenamiento del navegador, y en el perfil de
    /// uso diario quedaría al alcance de cualquier extensión que el usuario tenga instalada.
    /// </summary>
    internal static IReadOnlyList<string> Argumentos(Uri url, string carpetaPerfil) =>
    [
        $"--app={url}",
        $"--user-data-dir={carpetaPerfil}",
        "--no-first-run",
        "--no-default-browser-check",
    ];

    /// <summary>
    /// Solo se abre una dirección **local**. El lanzador recibe la URL por argumento, y sin esta
    /// comprobación un acceso directo manipulado lo convertiría en una forma de abrir cualquier
    /// página en una ventana sin barra de direcciones, que es justo donde no se ve a dónde se entró.
    /// </summary>
    internal static bool EsLocal(Uri url) =>
        url.Scheme == Uri.UriSchemeHttp && url.IsLoopback;
}
