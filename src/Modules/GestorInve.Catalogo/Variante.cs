using System.Text.Json;
using GestorInve.Core;

namespace GestorInve.Catalogo;

/// <summary>
/// **La unidad de inventario del sistema.** Todo lo que se cuenta, mueve, cuesta y valora se
/// refiere a una variante, nunca a un producto (ADR 0005). El kardex de H7 apuntará aquí.
///
/// Los valores de atributo se guardan en una columna <c>jsonb</c> en lugar de tablas
/// normalizadas: para un catálogo de pyme basta, se consulta con índices GIN y evita tres
/// tablas más antes de tener movimientos. El ADR deja escrita la alternativa.
/// </summary>
public class Variante : IEntidadDeEmpresa, IAuditable
{
    private readonly List<CodigoBarras> codigos = [];

    private Variante()
    {
        // EF Core
        Sku = string.Empty;
        AtributosJson = "{}";
        CreadoPor = string.Empty;
    }

    private Variante(Guid productoId, string sku, IReadOnlyDictionary<string, string> atributos)
    {
        Id = Guid.CreateVersion7();
        ProductoId = productoId;
        Sku = Producto.NormalizarSku(sku);

        // Claves ordenadas: el JSON es estable y dos variantes iguales producen el mismo texto.
        AtributosJson = JsonSerializer.Serialize(
            atributos.OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));

        Activa = true;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public Guid ProductoId { get; private set; }

    /// <summary>Código propio de la variante; único dentro de la empresa.</summary>
    public string Sku { get; private set; }

    /// <summary>Combinación de atributos serializada (`{"color":"rojo","talla":"M"}`).</summary>
    public string AtributosJson { get; private set; }

    /// <summary>Existencias mínimas deseadas, para las alertas de reposición (por bodega en H7).</summary>
    public decimal? StockMinimo { get; private set; }

    public decimal? StockMaximo { get; private set; }

    public bool Activa { get; private set; }

    public IReadOnlyCollection<CodigoBarras> Codigos => codigos;

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    internal static Variante Crear(Producto producto, string sku, IReadOnlyDictionary<string, string> atributos) =>
        new(producto.Id, sku, atributos);

    /// <summary>Variante única de un producto sin variedad real: hereda el SKU y no tiene atributos.</summary>
    internal static Variante PorDefecto(Producto producto) =>
        new(producto.Id, producto.Sku, new Dictionary<string, string>(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, string> Atributos() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(AtributosJson)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);

    internal bool MismaCombinacion(IReadOnlyDictionary<string, string> otros)
    {
        var propios = Atributos();

        return propios.Count == otros.Count
               && propios.All(p => otros.TryGetValue(p.Key, out var valor)
                                   && string.Equals(valor, p.Value, StringComparison.Ordinal));
    }

    /// <summary>
    /// Añade un código de barras. Vive en la variante y no en el producto porque es lo que se
    /// escanea en la caja, y escanear debe identificar exactamente qué se está vendiendo.
    /// </summary>
    public CodigoBarras AgregarCodigo(string codigo)
    {
        var normalizado = CodigoBarras.Normalizar(codigo);

        if (codigos.Any(c => c.Codigo == normalizado))
        {
            throw new InvalidOperationException($"La variante ya tiene el código «{normalizado}».");
        }

        var nuevo = new CodigoBarras(Id, normalizado);
        codigos.Add(nuevo);

        return nuevo;
    }

    /// <summary>
    /// Fija los límites de existencias. Se validan juntos: un mínimo por encima del máximo
    /// haría que una alerta de reposición y una de exceso se dispararan a la vez.
    /// </summary>
    public void FijarLimitesDeStock(decimal? minimo, decimal? maximo)
    {
        if (minimo is < 0m || maximo is < 0m)
        {
            throw new ArgumentException("Los límites de existencias no pueden ser negativos.");
        }

        if (minimo is { } min && maximo is { } max && min > max)
        {
            throw new ArgumentException($"El mínimo ({min}) no puede superar el máximo ({max}).");
        }

        StockMinimo = minimo;
        StockMaximo = maximo;
    }

    public void Desactivar() => Activa = false;
}

/// <summary>Código de barras de una variante. Único por empresa: escanear no puede ser ambiguo.</summary>
public class CodigoBarras : IEntidadDeEmpresa, IAuditable
{
    private CodigoBarras()
    {
        // EF Core
        Codigo = string.Empty;
        CreadoPor = string.Empty;
    }

    internal CodigoBarras(Guid varianteId, string codigo)
    {
        Id = Guid.CreateVersion7();
        VarianteId = varianteId;
        Codigo = codigo;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public Guid VarianteId { get; private set; }

    public string Codigo { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    /// <summary>Sin espacios y en mayúsculas: los lectores añaden basura y el usuario, guiones.</summary>
    public static string Normalizar(string codigo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);

        return codigo.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
