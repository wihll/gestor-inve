using GestorInve.Core;

namespace GestorInve.Catalogo;

/// <summary>
/// Ficha comercial de un artículo. **No lleva existencias**: el stock siempre vive en una
/// <see cref="Variante"/> (ADR 0005). Un producto es «Camiseta Polo» y sus variantes son las
/// combinaciones concretas que se cuentan en la bodega.
///
/// Crear un producto crea siempre su primera variante, incluso cuando no hay nada que variar
/// (un martillo). Así el kardex de H7 referencia un único tipo de entidad y no necesita
/// distinguir «productos con variantes» de «productos sin ellas».
/// </summary>
public class Producto : IEntidadDeEmpresa, IAuditable
{
    private readonly List<Variante> variantes = [];
    private List<string> atributos = [];

    private Producto()
    {
        // EF Core
        Sku = string.Empty;
        Nombre = string.Empty;
        CreadoPor = string.Empty;
    }

    private Producto(string sku, string nombre, Guid unidadMedidaId, IEnumerable<string> atributos)
    {
        Id = Guid.CreateVersion7();
        Sku = NormalizarSku(sku);
        Nombre = nombre.Trim();
        UnidadMedidaId = unidadMedidaId;
        this.atributos = [.. atributos.Select(NormalizarAtributo).Distinct(StringComparer.Ordinal)];
        Activo = true;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    /// <summary>Código del producto. Único dentro de la empresa, no entre empresas.</summary>
    public string Sku { get; private set; }

    public string Nombre { get; private set; }

    public string? Descripcion { get; private set; }

    public Guid UnidadMedidaId { get; private set; }

    public Guid? CategoriaId { get; private set; }

    /// <summary>
    /// Nombres de los atributos que distinguen a sus variantes («talla», «color»). Vacío en un
    /// producto sin variedad real. Cada variante debe traer exactamente estos atributos.
    /// </summary>
    public IReadOnlyCollection<string> Atributos => atributos;

    /// <summary>Declara que los movimientos de este producto deben indicar lote (entidades en H7).</summary>
    public bool ExigeLote { get; private set; }

    /// <summary>Declara que cada unidad se identifica con un serial (entidades en H7).</summary>
    public bool ExigeSerial { get; private set; }

    public bool Activo { get; private set; }

    public IReadOnlyCollection<Variante> Variantes => variantes;

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    /// <summary>
    /// Crea el producto con su variante por defecto, que hereda el SKU. Para un artículo sin
    /// variedad real ésa es la única que existirá y el usuario nunca tiene que pensar en ella.
    /// </summary>
    public static Producto Crear(
        string sku,
        string nombre,
        Guid unidadMedidaId,
        IEnumerable<string>? atributos = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        if (unidadMedidaId == Guid.Empty)
        {
            throw new ArgumentException("El producto necesita una unidad de medida.", nameof(unidadMedidaId));
        }

        var producto = new Producto(sku, nombre, unidadMedidaId, atributos ?? []);

        // Sin atributos declarados la variante por defecto no lleva ninguno; con atributos
        // queda pendiente de completar y el llamante debe añadir las variantes reales.
        if (producto.atributos.Count == 0)
        {
            producto.variantes.Add(Variante.PorDefecto(producto));
        }

        return producto;
    }

    public static string NormalizarSku(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        return sku.Trim().ToUpperInvariant();
    }

    public static string NormalizarAtributo(string atributo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(atributo);

        return atributo.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Añade una variante con sus valores de atributo. Rechaza combinaciones que no cuadren con
    /// los atributos declarados y combinaciones repetidas: dos variantes idénticas harían
    /// ambiguo qué existencias se mueven.
    /// </summary>
    public Variante AgregarVariante(string sku, IReadOnlyDictionary<string, string> valores)
    {
        ArgumentNullException.ThrowIfNull(valores);

        var normalizados = valores.ToDictionary(
            p => NormalizarAtributo(p.Key),
            p => p.Value.Trim(),
            StringComparer.Ordinal);

        var faltan = atributos.Where(a => !normalizados.ContainsKey(a)).ToList();
        var sobran = normalizados.Keys.Where(k => !atributos.Contains(k, StringComparer.Ordinal)).ToList();

        if (faltan.Count > 0 || sobran.Count > 0)
        {
            throw new ArgumentException(
                $"Los atributos de la variante no coinciden con los del producto. "
                + $"Faltan: [{string.Join(", ", faltan)}]. No declarados: [{string.Join(", ", sobran)}].",
                nameof(valores));
        }

        if (variantes.Any(v => v.MismaCombinacion(normalizados)))
        {
            throw new InvalidOperationException(
                $"Ya existe una variante de «{Nombre}» con esa combinación de atributos.");
        }

        var variante = Variante.Crear(this, sku, normalizados);
        variantes.Add(variante);

        return variante;
    }

    public void CambiarDescripcion(string? descripcion) => Descripcion = descripcion?.Trim();

    public void CambiarCategoria(Guid? categoriaId) => CategoriaId = categoriaId;

    public void ExigirTrazabilidad(bool lote, bool serial)
    {
        ExigeLote = lote;
        ExigeSerial = serial;
    }

    public void Renombrar(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        Nombre = nombre.Trim();
    }

    public void Desactivar() => Activo = false;
}
