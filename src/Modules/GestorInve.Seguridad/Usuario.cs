using GestorInve.Core;
using GestorInve.Core.Seguridad;

namespace GestorInve.Seguridad;

public enum ResultadoAutenticacion
{
    Exitoso,
    CredencialesInvalidas,
    Bloqueado,
    Inactivo,
}

/// <summary>
/// Usuario de una empresa. Concentra la decisión de dejar entrar o no: contraseña, estado y
/// bloqueo por intentos fallidos viven aquí, no repartidos por los endpoints.
/// </summary>
public class Usuario : IEntidadDeEmpresa, IAuditable
{
    /// <summary>Intentos fallidos consecutivos antes de bloquear.</summary>
    public const int IntentosPermitidos = 5;

    private readonly List<UsuarioRol> asignaciones = [];

    private Usuario()
    {
        // EF Core
        Correo = string.Empty;
        NombreCompleto = string.Empty;
        HashContrasena = string.Empty;
        CreadoPor = string.Empty;
    }

    private Usuario(string correo, string nombreCompleto, string hashContrasena)
    {
        Id = Guid.CreateVersion7();
        Correo = correo;
        NombreCompleto = nombreCompleto;
        HashContrasena = hashContrasena;
        Activo = true;
        CreadoPor = string.Empty;
    }

    /// <summary>Cuánto dura el bloqueo tras agotar los intentos.</summary>
    public static TimeSpan DuracionBloqueo { get; } = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public string Correo { get; private set; }

    public string NombreCompleto { get; private set; }

    public string HashContrasena { get; private set; }

    public bool Activo { get; private set; }

    public int IntentosFallidos { get; private set; }

    public DateTimeOffset? BloqueadoHasta { get; private set; }

    public DateTimeOffset? UltimoAccesoEn { get; private set; }

    /// <summary>Asignaciones persistidas; los roles se exponen aparte para comodidad del dominio.</summary>
    public IReadOnlyCollection<UsuarioRol> Asignaciones => asignaciones;

    /// <summary>Roles efectivos. Requiere que las asignaciones vengan con su rol cargado.</summary>
    public IReadOnlyCollection<Rol> Roles => [.. asignaciones.Select(a => a.Rol).OfType<Rol>()];

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    public static Usuario Crear(
        string correo,
        string nombreCompleto,
        string contrasena,
        IHasheadorContrasena hasheador)
    {
        ArgumentNullException.ThrowIfNull(hasheador);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombreCompleto);

        return new Usuario(NormalizarCorreo(correo), nombreCompleto.Trim(), hasheador.Hashear(contrasena));
    }

    /// <summary>Normaliza igual que al crear, para buscar por correo sin sorpresas de mayúsculas.</summary>
    public static string NormalizarCorreo(string correo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correo);

        var limpio = correo.Trim().ToLowerInvariant();
        var partes = limpio.Split('@');

        if (partes.Length != 2
            || partes[0].Length == 0
            || partes[1].Length == 0
            || !partes[1].Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException("El correo no tiene un formato válido.", nameof(correo));
        }

        return limpio;
    }

    public bool EstaBloqueado(DateTimeOffset ahora) => BloqueadoHasta is { } hasta && hasta > ahora;

    /// <summary>
    /// Decide el acceso y actualiza el estado del bloqueo. El orden importa: primero el bloqueo
    /// y el estado, y solo después la contraseña; comprobar la clave antes permitiría saltarse
    /// el bloqueo acertándola, que es justo lo que el bloqueo debe impedir.
    /// </summary>
    public ResultadoAutenticacion IntentarAutenticar(
        string contrasena,
        IHasheadorContrasena hasheador,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(hasheador);

        if (!Activo)
        {
            return ResultadoAutenticacion.Inactivo;
        }

        if (EstaBloqueado(ahora))
        {
            return ResultadoAutenticacion.Bloqueado;
        }

        if (BloqueadoHasta is not null)
        {
            // El bloqueo expiró: se parte de cero.
            BloqueadoHasta = null;
            IntentosFallidos = 0;
        }

        if (!hasheador.Verificar(contrasena, HashContrasena))
        {
            IntentosFallidos++;

            if (IntentosFallidos >= IntentosPermitidos)
            {
                BloqueadoHasta = ahora + DuracionBloqueo;
            }

            return ResultadoAutenticacion.CredencialesInvalidas;
        }

        IntentosFallidos = 0;
        BloqueadoHasta = null;
        UltimoAccesoEn = ahora;

        return ResultadoAutenticacion.Exitoso;
    }

    public bool Verificar(string contrasena, IHasheadorContrasena hasheador)
    {
        ArgumentNullException.ThrowIfNull(hasheador);

        return hasheador.Verificar(contrasena, HashContrasena);
    }

    public bool CambiarContrasena(string actual, string nueva, IHasheadorContrasena hasheador)
    {
        ArgumentNullException.ThrowIfNull(hasheador);
        PoliticaContrasena.Validar(nueva, nameof(nueva));

        if (!hasheador.Verificar(actual, HashContrasena))
        {
            return false;
        }

        HashContrasena = hasheador.Hashear(nueva);
        return true;
    }

    public void AsignarRol(Rol rol)
    {
        ArgumentNullException.ThrowIfNull(rol);

        if (!asignaciones.Any(a => a.RolId == rol.Id))
        {
            asignaciones.Add(new UsuarioRol(this, rol));
        }
    }

    public void QuitarRol(Guid rolId) => asignaciones.RemoveAll(a => a.RolId == rolId);

    public void Desactivar() => Activo = false;

    public void Activar() => Activo = true;

    /// <summary>Permisos efectivos: la unión de los de sus roles, sin duplicados.</summary>
    public IReadOnlyCollection<string> Permisos() =>
        [.. Roles.SelectMany(r => r.Permisos).Distinct(StringComparer.Ordinal)];
}
