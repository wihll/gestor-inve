using GestorInve.Escritorio;

namespace GestorInve.Escritorio.Tests;

public class NavegadorTests
{
    private static string? EntornoWindows(string nombre) => nombre switch
    {
        "ProgramFiles(x86)" => @"C:\Program Files (x86)",
        "ProgramFiles" => @"C:\Program Files",
        "LocalAppData" => @"C:\Users\ana\AppData\Local",
        _ => null,
    };

    [Fact]
    public void En_Windows_se_prefiere_Edge_que_siempre_esta()
    {
        var candidatos = Navegador.CandidatosWindows(EntornoWindows).ToList();

        Assert.Contains("msedge.exe", candidatos[0], StringComparison.Ordinal);

        // Con Edge y Chrome instalados, gana Edge.
        var elegido = Navegador.Elegir(candidatos, _ => true);
        Assert.Contains("msedge.exe", elegido, StringComparison.Ordinal);
    }

    [Fact]
    public void Si_solo_hay_Chrome_se_usa_Chrome()
    {
        var elegido = Navegador.Elegir(
            Navegador.CandidatosWindows(EntornoWindows),
            ruta => ruta.EndsWith("chrome.exe", StringComparison.Ordinal));

        Assert.Contains("chrome.exe", elegido, StringComparison.Ordinal);
    }

    [Fact]
    public void Sin_ningun_navegador_Chromium_no_se_elige_nada()
    {
        // El lanzador cae entonces al navegador por omisión: la aplicación abre igual.
        Assert.Null(Navegador.Elegir(Navegador.CandidatosWindows(EntornoWindows), _ => false));
        Assert.Null(Navegador.Elegir(Navegador.CandidatosLinux("/usr/bin:/snap/bin"), _ => false));
    }

    [Fact]
    public void Una_variable_de_entorno_ausente_no_rompe_la_busqueda()
    {
        var candidatos = Navegador.CandidatosWindows(_ => null).ToList();

        Assert.Empty(candidatos);
    }

    [Fact]
    public void En_Linux_se_busca_en_cada_carpeta_del_PATH()
    {
        var elegido = Navegador.Elegir(
            Navegador.CandidatosLinux("/usr/local/bin:/usr/bin:/snap/bin"),
            ruta => ruta == "/snap/bin/chromium");

        Assert.Equal("/snap/bin/chromium", elegido);
    }

    [Fact]
    public void Con_el_Chromium_snap_de_Ubuntu_el_perfil_va_donde_el_snap_puede_escribir()
    {
        // Un snap no puede escribir en carpetas ocultas del home: `~/.local/share` le está vedado.
        var perfil = Navegador.CarpetaPerfil("/snap/bin/chromium", "/home/ana/.local/share", "/home/ana");

        Assert.Equal("/home/ana/snap/chromium/common/GestorInve", perfil);
    }

    [Fact]
    public void Con_un_navegador_normal_el_perfil_va_en_los_datos_locales()
    {
        var perfil = Navegador.CarpetaPerfil("/usr/bin/google-chrome", "/home/ana/.local/share", "/home/ana");

        Assert.Equal(Path.Combine("/home/ana/.local/share", "GestorInve", "perfil"), perfil);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void En_Linux_un_PATH_vacio_no_rompe(string? path) =>
        Assert.Empty(Navegador.CandidatosLinux(path));

    [Fact]
    public void Los_argumentos_abren_en_modo_aplicacion_con_perfil_propio()
    {
        var argumentos = Navegador.Argumentos(new Uri("http://127.0.0.1:48620"), "/home/ana/.local/share/GestorInve/perfil");

        Assert.Contains("--app=http://127.0.0.1:48620/", argumentos);

        // Perfil propio: el token de refresco no queda al alcance de las extensiones del
        // navegador de uso diario.
        Assert.Contains("--user-data-dir=/home/ana/.local/share/GestorInve/perfil", argumentos);
    }

    [Theory]
    [InlineData("http://127.0.0.1:48620", true)]
    [InlineData("http://localhost:48620", true)]
    [InlineData("http://[::1]:48620", true)]
    [InlineData("https://127.0.0.1:48620", false)]
    [InlineData("http://192.168.1.10:48620", false)]
    [InlineData("http://ejemplo.com", false)]
    [InlineData("http://127.0.0.1.ejemplo.com", false)]
    [InlineData("file:///etc/passwd", false)]
    public void Solo_se_abre_una_direccion_local(string url, bool esperado) =>
        Assert.Equal(esperado, Navegador.EsLocal(new Uri(url)));
}
