using GestorInve.Core;

namespace GestorInve.Seguridad;

/// <summary>
/// Empresa: la raíz del aislamiento multiempresa. No implementa <see cref="IEntidadDeEmpresa"/>
/// porque no pertenece a otra empresa: <em>es</em> la empresa. Su propia política RLS la filtra por <c>id</c>.
/// </summary>
public class Empresa : IRaizDeEmpresa, IAuditable
{
    private Empresa()
    {
        // EF Core
        Nit = string.Empty;
        Nombre = string.Empty;
        CreadoPor = string.Empty;
    }

    public Empresa(Guid id, string nit, string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nit);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        if (id == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la empresa no puede ser vacío.", nameof(id));
        }

        Id = id;
        Nit = nit.Trim();
        Nombre = nombre.Trim();
        Activa = true;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Nit { get; private set; }

    public string Nombre { get; private set; }

    public bool Activa { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    public void Renombrar(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        Nombre = nombre.Trim();
    }

    public void Desactivar() => Activa = false;
}
