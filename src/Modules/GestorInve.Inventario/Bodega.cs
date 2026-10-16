using GestorInve.Core;

namespace GestorInve.Inventario;

/// <summary>
/// Bodega de la empresa. Crear una crea su ubicación <see cref="Ubicacion.CodigoGeneral"/>:
/// la pyme de un solo cuarto nunca tiene que pensar en ubicaciones y, sobre todo, **el kardex
/// tiene una única forma**, jamás «a veces con ubicación y a veces sin». Esa bifurcación es la
/// que no se puede deshacer después sin reescribir los movimientos.
/// </summary>
public class Bodega : IEntidadDeEmpresa, IAuditable
{
    private readonly List<Ubicacion> ubicaciones = [];

    private Bodega()
    {
        // EF Core
        Codigo = string.Empty;
        Nombre = string.Empty;
        CreadoPor = string.Empty;
    }

    private Bodega(string codigo, string nombre)
    {
        Id = Guid.CreateVersion7();
        Codigo = Normalizar(codigo);
        Nombre = nombre.Trim();
        Activa = true;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    /// <summary>Código único dentro de la empresa.</summary>
    public string Codigo { get; private set; }

    public string Nombre { get; private set; }

    public bool Activa { get; private set; }

    public IReadOnlyCollection<Ubicacion> Ubicaciones => ubicaciones;

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    public static Bodega Crear(string codigo, string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        var bodega = new Bodega(codigo, nombre);
        bodega.ubicaciones.Add(Ubicacion.General(bodega));

        return bodega;
    }

    public static string Normalizar(string codigo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);

        return codigo.Trim().ToUpperInvariant();
    }

    public Ubicacion AgregarUbicacion(string codigo, string nombre)
    {
        var normalizado = Ubicacion.Normalizar(codigo);

        if (ubicaciones.Any(u => u.Codigo == normalizado))
        {
            throw new InvalidOperationException($"La bodega «{Nombre}» ya tiene la ubicación «{normalizado}».");
        }

        var ubicacion = Ubicacion.Crear(this, codigo, nombre);
        ubicaciones.Add(ubicacion);

        return ubicacion;
    }

    public void Renombrar(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        Nombre = nombre.Trim();
    }

    public void Desactivar() => Activa = false;
}

/// <summary>
/// Ubicación dentro de una bodega (pasillo, estante, nevera). Las existencias se cuentan por
/// (variante, bodega, ubicación), así que toda bodega tiene al menos la ubicación general.
/// </summary>
public class Ubicacion : IEntidadDeEmpresa, IAuditable
{
    /// <summary>Ubicación que se crea con la bodega, para quien no necesita dividirla.</summary>
    public const string CodigoGeneral = "GENERAL";

    private Ubicacion()
    {
        // EF Core
        Codigo = string.Empty;
        Nombre = string.Empty;
        CreadoPor = string.Empty;
    }

    private Ubicacion(Guid bodegaId, string codigo, string nombre)
    {
        Id = Guid.CreateVersion7();
        BodegaId = bodegaId;
        Codigo = Normalizar(codigo);
        Nombre = nombre.Trim();
        Activa = true;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public Guid BodegaId { get; private set; }

    /// <summary>Código único dentro de la bodega.</summary>
    public string Codigo { get; private set; }

    public string Nombre { get; private set; }

    public bool Activa { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    internal static Ubicacion General(Bodega bodega) => new(bodega.Id, CodigoGeneral, "General");

    internal static Ubicacion Crear(Bodega bodega, string codigo, string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        return new Ubicacion(bodega.Id, codigo, nombre);
    }

    public static string Normalizar(string codigo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);

        return codigo.Trim().ToUpperInvariant();
    }

    public void Desactivar() => Activa = false;
}
