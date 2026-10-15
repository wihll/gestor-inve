using GestorInve.Core;

namespace GestorInve.Seguridad;

/// <summary>
/// Rol de una empresa: un nombre y el conjunto de permisos que concede. Cada empresa define
/// los suyos, así que el rol pertenece a una empresa y queda sujeto al aislamiento.
/// </summary>
public class Rol : IEntidadDeEmpresa, IAuditable
{
    private List<string> permisos = [];

    private Rol()
    {
        // EF Core
        Nombre = string.Empty;
        CreadoPor = string.Empty;
    }

    public Rol(string nombre, IEnumerable<string> permisos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(permisos);

        var pedidos = permisos.Distinct(StringComparer.Ordinal).ToList();
        var desconocidos = pedidos.Where(p => !Permiso.Existe(p)).ToList();

        if (desconocidos.Count > 0)
        {
            throw new ArgumentException(
                $"Permisos desconocidos: {string.Join(", ", desconocidos)}.",
                nameof(permisos));
        }

        Id = Guid.CreateVersion7();
        Nombre = nombre.Trim();
        this.permisos = pedidos;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid EmpresaId { get; private set; }

    public string Nombre { get; private set; }

    public IReadOnlyCollection<string> Permisos => permisos;

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    public void ReemplazarPermisos(IEnumerable<string> nuevos)
    {
        ArgumentNullException.ThrowIfNull(nuevos);

        var pedidos = nuevos.Distinct(StringComparer.Ordinal).ToList();
        var desconocidos = pedidos.Where(p => !Permiso.Existe(p)).ToList();

        if (desconocidos.Count > 0)
        {
            throw new ArgumentException(
                $"Permisos desconocidos: {string.Join(", ", desconocidos)}.",
                nameof(nuevos));
        }

        permisos = pedidos;
    }
}
