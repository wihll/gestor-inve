using GestorInve.Core;

namespace GestorInve.Seguridad;

/// <summary>
/// Asignación de un rol a un usuario. Es una entidad explícita, y no una tabla de unión
/// implícita, justamente para que quede sujeta al aislamiento: el interceptor le sella la
/// empresa y la prueba de políticas exige que tenga su RLS como cualquier otra tabla con datos
/// de un cliente. Una tabla de unión implícita no tendría ni <c>empresa_id</c> ni política, y
/// el olvido pasaría inadvertido.
/// </summary>
public class UsuarioRol : IEntidadDeEmpresa
{
    private UsuarioRol()
    {
        // EF Core
    }

    public UsuarioRol(Usuario usuario, Rol rol)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentNullException.ThrowIfNull(rol);

        UsuarioId = usuario.Id;
        RolId = rol.Id;
        Rol = rol;
    }

    public Guid UsuarioId { get; private set; }

    public Guid RolId { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public Rol? Rol { get; private set; }
}
