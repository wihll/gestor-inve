using GestorInve.Core;

namespace GestorInve.Seguridad;

/// <summary>
/// Asiento de auditoría: qué entidad cambió, cómo y quién lo hizo. Pertenece a una empresa,
/// así que queda sujeto al filtro global y a las políticas RLS como cualquier otro dato.
/// </summary>
public class RegistroAuditoria : IEntidadDeEmpresa, IAuditable
{
    private RegistroAuditoria()
    {
        // EF Core
        Entidad = string.Empty;
        ClaveEntidad = string.Empty;
        Accion = string.Empty;
        CreadoPor = string.Empty;
    }

    public RegistroAuditoria(string entidad, string claveEntidad, string accion, string? datos = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entidad);
        ArgumentException.ThrowIfNullOrWhiteSpace(claveEntidad);
        ArgumentException.ThrowIfNullOrWhiteSpace(accion);

        Id = Guid.CreateVersion7();
        Entidad = entidad;
        ClaveEntidad = claveEntidad;
        Accion = accion;
        Datos = datos;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>La fija el interceptor con la empresa del contexto; nunca la elige quien escribe.</summary>
    public Guid EmpresaId { get; private set; }

    public string Entidad { get; private set; }

    public string ClaveEntidad { get; private set; }

    public string Accion { get; private set; }

    /// <summary>Carga útil en JSON (columna <c>jsonb</c>).</summary>
    public string? Datos { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }
}
