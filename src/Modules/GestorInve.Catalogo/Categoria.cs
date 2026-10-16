using GestorInve.Core;

namespace GestorInve.Catalogo;

/// <summary>
/// Categoría del catálogo, organizada en árbol («Aseo» → «Detergentes»).
///
/// La invariante que importa es que el árbol no tenga ciclos: una categoría que es su propia
/// antecesora rompe cualquier recorrido (informes, menús, totales por categoría) con un bucle
/// infinito. Como el padre se comprueba contra una cadena que el llamante aporta, la validación
/// vive en <see cref="MoverBajo"/> y no en un disparador de base de datos.
/// </summary>
public class Categoria : IEntidadDeEmpresa, IAuditable
{
    private Categoria()
    {
        // EF Core
        Nombre = string.Empty;
        CreadoPor = string.Empty;
    }

    public Categoria(string nombre, Categoria? padre = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        Id = Guid.CreateVersion7();
        Nombre = nombre.Trim();
        Activa = true;
        CreadoPor = string.Empty;

        if (padre is not null)
        {
            MoverBajo(padre, [padre]);
        }
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public string Nombre { get; private set; }

    public Guid? PadreId { get; private set; }

    public bool Activa { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    /// <summary>
    /// Cuelga la categoría de otra. <paramref name="cadenaDelPadre"/> son los antecesores del
    /// nuevo padre, del más cercano al más lejano; quien llama los lee de la base de datos.
    /// Si esta categoría aparece ahí, el movimiento crearía un ciclo y se rechaza.
    /// </summary>
    public void MoverBajo(Categoria padre, IReadOnlyCollection<Categoria> cadenaDelPadre)
    {
        ArgumentNullException.ThrowIfNull(padre);
        ArgumentNullException.ThrowIfNull(cadenaDelPadre);

        if (padre.Id == Id)
        {
            throw new InvalidOperationException("Una categoría no puede ser su propio padre.");
        }

        if (cadenaDelPadre.Any(c => c.Id == Id))
        {
            throw new InvalidOperationException(
                $"Mover «{Nombre}» bajo «{padre.Nombre}» crearía un ciclo en el árbol de categorías.");
        }

        PadreId = padre.Id;
    }

    public void MoverARaiz() => PadreId = null;

    public void Renombrar(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        Nombre = nombre.Trim();
    }

    public void Desactivar() => Activa = false;
}
