using GestorInve.Core;

namespace GestorInve.Inventario;

/// <summary>
/// Ajustes del inventario de una empresa. Hoy solo el método de costeo.
///
/// Vive en el esquema `inventario` y no en la entidad `Empresa` por dos razones: el módulo de
/// inventario no declara dependencia del de seguridad (ADR 0006) y añadirla por un enum no se
/// justifica; y el ajuste pertenece a quien lo usa. Ver ADR 0010.
/// </summary>
public class ConfiguracionInventario : IEntidadDeEmpresa
{
    private ConfiguracionInventario()
    {
        // EF Core
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public MetodoCosteo Metodo { get; private set; }

    /// <summary>Cuándo quedó fijado el método, para poder explicar un costo histórico.</summary>
    public DateTimeOffset ActualizadoEn { get; private set; }

    /// <summary>
    /// Promedio ponderado por omisión: es el que una pyme entiende sin formación contable
    /// (ADR 0006), y el comportamiento que ya tenían las empresas creadas antes de H8.
    /// </summary>
    public static ConfiguracionInventario PorOmision(DateTimeOffset ahora) =>
        new() { Id = Guid.CreateVersion7(), Metodo = MetodoCosteo.PromedioPonderado, ActualizadoEn = ahora };

    internal void Cambiar(MetodoCosteo metodo, DateTimeOffset ahora)
    {
        Metodo = metodo;
        ActualizadoEn = ahora;
    }
}
