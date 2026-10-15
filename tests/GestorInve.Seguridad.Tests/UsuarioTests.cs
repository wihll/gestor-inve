using GestorInve.Core;
using GestorInve.Core.Seguridad;
using GestorInve.Seguridad;

namespace GestorInve.Seguridad.Tests;

internal sealed class RelojManipulable(DateTimeOffset inicio) : IReloj
{
    public DateTimeOffset Ahora { get; private set; } = inicio;

    public void Avanzar(TimeSpan cuanto) => Ahora += cuanto;
}

public class UsuarioTests
{
    private const string Clave = "Clave-Muy-Larga-2026";
    private static readonly DateTimeOffset Inicio = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly HasheadorContrasena hasheador = new();

    [Fact]
    public void Con_la_clave_correcta_el_acceso_es_exitoso()
    {
        var (usuario, reloj) = CrearUsuario();

        var resultado = usuario.IntentarAutenticar(Clave, hasheador, reloj.Ahora);

        Assert.Equal(ResultadoAutenticacion.Exitoso, resultado);
        Assert.Equal(reloj.Ahora, usuario.UltimoAccesoEn);
        Assert.Equal(0, usuario.IntentosFallidos);
    }

    [Fact]
    public void Con_la_clave_incorrecta_se_cuenta_el_intento()
    {
        var (usuario, reloj) = CrearUsuario();

        var resultado = usuario.IntentarAutenticar("Clave-Equivocada-2026", hasheador, reloj.Ahora);

        Assert.Equal(ResultadoAutenticacion.CredencialesInvalidas, resultado);
        Assert.Equal(1, usuario.IntentosFallidos);
        Assert.Null(usuario.UltimoAccesoEn);
    }

    [Fact]
    public void A_los_intentos_permitidos_la_cuenta_se_bloquea()
    {
        var (usuario, reloj) = CrearUsuario();

        for (var i = 0; i < Usuario.IntentosPermitidos; i++)
        {
            usuario.IntentarAutenticar("Clave-Equivocada-2026", hasheador, reloj.Ahora);
        }

        Assert.True(usuario.EstaBloqueado(reloj.Ahora));
        Assert.Equal(reloj.Ahora + Usuario.DuracionBloqueo, usuario.BloqueadoHasta);
    }

    /// <summary>
    /// El bloqueo no se puede saltar acertando la clave: si no, un ataque por fuerza bruta
    /// solo tendría que seguir probando y el bloqueo no frenaría nada.
    /// </summary>
    [Fact]
    public void Mientras_esta_bloqueada_la_clave_correcta_tampoco_entra()
    {
        var (usuario, reloj) = CrearUsuario();
        Bloquear(usuario, reloj);

        var resultado = usuario.IntentarAutenticar(Clave, hasheador, reloj.Ahora);

        Assert.Equal(ResultadoAutenticacion.Bloqueado, resultado);
        Assert.Null(usuario.UltimoAccesoEn);
    }

    [Fact]
    public void Pasado_el_bloqueo_se_puede_volver_a_entrar_y_el_contador_se_reinicia()
    {
        var (usuario, reloj) = CrearUsuario();
        Bloquear(usuario, reloj);

        reloj.Avanzar(Usuario.DuracionBloqueo + TimeSpan.FromSeconds(1));
        var resultado = usuario.IntentarAutenticar(Clave, hasheador, reloj.Ahora);

        Assert.Equal(ResultadoAutenticacion.Exitoso, resultado);
        Assert.Equal(0, usuario.IntentosFallidos);
        Assert.Null(usuario.BloqueadoHasta);
    }

    [Fact]
    public void Un_acceso_correcto_reinicia_los_intentos_fallidos()
    {
        var (usuario, reloj) = CrearUsuario();
        usuario.IntentarAutenticar("Clave-Equivocada-2026", hasheador, reloj.Ahora);
        usuario.IntentarAutenticar("Clave-Equivocada-2026", hasheador, reloj.Ahora);

        usuario.IntentarAutenticar(Clave, hasheador, reloj.Ahora);

        Assert.Equal(0, usuario.IntentosFallidos);
    }

    [Fact]
    public void Un_usuario_inactivo_no_entra_ni_con_la_clave_correcta()
    {
        var (usuario, reloj) = CrearUsuario();
        usuario.Desactivar();

        Assert.Equal(ResultadoAutenticacion.Inactivo, usuario.IntentarAutenticar(Clave, hasheador, reloj.Ahora));
    }

    [Fact]
    public void El_correo_se_normaliza()
    {
        var usuario = Usuario.Crear("  Contadora@Empresa.CO  ", "Ana", Clave, hasheador);

        Assert.Equal("contadora@empresa.co", usuario.Correo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-arroba")]
    [InlineData("dos@@arrobas.co")]
    [InlineData("@sin-usuario.co")]
    [InlineData("sin-dominio@")]
    public void Se_rechaza_un_correo_invalido(string correo)
    {
        Assert.ThrowsAny<ArgumentException>(() => Usuario.Crear(correo, "Ana", Clave, hasheador));
    }

    [Fact]
    public void La_contrasena_no_se_guarda_en_claro()
    {
        var usuario = Usuario.Crear("ana@empresa.co", "Ana", Clave, hasheador);

        Assert.DoesNotContain(Clave, usuario.HashContrasena, StringComparison.Ordinal);
        Assert.StartsWith("pbkdf2-sha512$", usuario.HashContrasena, StringComparison.Ordinal);
    }

    [Fact]
    public void Cambiar_la_contrasena_exige_la_actual()
    {
        var usuario = Usuario.Crear("ana@empresa.co", "Ana", Clave, hasheador);

        Assert.False(usuario.CambiarContrasena("Clave-Equivocada-2026", "Clave-Nueva-Larga-2026", hasheador));
        Assert.True(usuario.Verificar(Clave, hasheador));

        Assert.True(usuario.CambiarContrasena(Clave, "Clave-Nueva-Larga-2026", hasheador));
        Assert.True(usuario.Verificar("Clave-Nueva-Larga-2026", hasheador));
        Assert.False(usuario.Verificar(Clave, hasheador));
    }

    [Fact]
    public void Cambiar_la_contrasena_aplica_la_politica_de_longitud()
    {
        var usuario = Usuario.Crear("ana@empresa.co", "Ana", Clave, hasheador);

        Assert.ThrowsAny<ArgumentException>(() => usuario.CambiarContrasena(Clave, "corta", hasheador));
    }

    [Fact]
    public void Los_permisos_salen_de_los_roles_asignados_sin_duplicados()
    {
        var usuario = Usuario.Crear("ana@empresa.co", "Ana", Clave, hasheador);
        usuario.AsignarRol(new Rol("Bodega", [Permiso.InventarioLeer, Permiso.InventarioMover]));
        usuario.AsignarRol(new Rol("Auditoría", [Permiso.InventarioLeer, Permiso.AuditoriaLeer]));

        var permisos = usuario.Permisos();

        Assert.Equal(
            [Permiso.AuditoriaLeer, Permiso.InventarioLeer, Permiso.InventarioMover],
            permisos.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Asignar_dos_veces_el_mismo_rol_no_lo_duplica()
    {
        var usuario = Usuario.Crear("ana@empresa.co", "Ana", Clave, hasheador);
        var rol = new Rol("Bodega", [Permiso.InventarioLeer]);

        usuario.AsignarRol(rol);
        usuario.AsignarRol(rol);

        Assert.Single(usuario.Roles);
    }

    private (Usuario Usuario, RelojManipulable Reloj) CrearUsuario()
    {
        var reloj = new RelojManipulable(Inicio);
        return (Usuario.Crear("ana@empresa.co", "Ana", Clave, hasheador), reloj);
    }

    private void Bloquear(Usuario usuario, RelojManipulable reloj)
    {
        for (var i = 0; i < Usuario.IntentosPermitidos; i++)
        {
            usuario.IntentarAutenticar("Clave-Equivocada-2026", hasheador, reloj.Ahora);
        }
    }
}
