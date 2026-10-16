using GestorInve.Core;

namespace GestorInve.Catalogo;

/// <summary>
/// Unidad de medida de la empresa (UND, KG, LT, BUL…). Persiste lo que <see cref="Unidad"/>
/// necesita para operar: el código y los decimales admitidos.
///
/// Es de cada empresa, no global, porque una pyme define las suyas («bulto», «arroba»).
/// No lleva factor de conversión: cada producto vive en una sola unidad (ADR 0005).
/// </summary>
public class UnidadMedida : IEntidadDeEmpresa, IAuditable
{
    private UnidadMedida()
    {
        // EF Core
        Codigo = string.Empty;
        Nombre = string.Empty;
        CreadoPor = string.Empty;
    }

    public UnidadMedida(string codigo, string nombre, int decimales)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        // Validar construyendo la unidad del dominio: una sola definición de qué es válido.
        var unidad = new Unidad(codigo, decimales);

        Id = Guid.CreateVersion7();
        Codigo = unidad.Codigo;
        Decimales = unidad.Decimales;
        Nombre = nombre.Trim();
        Activa = true;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public string Codigo { get; private set; }

    public string Nombre { get; private set; }

    public int Decimales { get; private set; }

    public bool Activa { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    /// <summary>Unidad del dominio, para construir cantidades.</summary>
    public Unidad ComoUnidad() => new(Codigo, Decimales);

    public void Renombrar(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        Nombre = nombre.Trim();
    }

    public void Desactivar() => Activa = false;
}
