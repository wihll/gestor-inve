namespace GestorInve.Seguridad;

/// <summary>
/// Permisos del sistema, en formato <c>recurso:accion</c>. Son los nombres que viajan en el
/// token y contra los que se comprueban las políticas de autorización, así que renombrar uno
/// invalida los permisos ya concedidos: se añade uno nuevo y se migra.
/// </summary>
public static class Permiso
{
    public const string UsuariosLeer = "usuarios:leer";
    public const string UsuariosAdministrar = "usuarios:administrar";
    public const string RolesAdministrar = "roles:administrar";
    public const string EmpresaAdministrar = "empresa:administrar";
    public const string AuditoriaLeer = "auditoria:leer";
    public const string CatalogoLeer = "catalogo:leer";
    public const string CatalogoEscribir = "catalogo:escribir";
    public const string InventarioLeer = "inventario:leer";
    public const string InventarioMover = "inventario:mover";
    public const string InventarioAjustar = "inventario:ajustar";

    /// <summary>Todos los permisos conocidos; es la lista que recibe el rol de administrador en el alta.</summary>
    public static IReadOnlyList<string> Todos { get; } =
    [
        UsuariosLeer,
        UsuariosAdministrar,
        RolesAdministrar,
        EmpresaAdministrar,
        AuditoriaLeer,
        CatalogoLeer,
        CatalogoEscribir,
        InventarioLeer,
        InventarioMover,
        InventarioAjustar,
    ];

    public static bool Existe(string permiso) => Todos.Contains(permiso, StringComparer.Ordinal);
}
