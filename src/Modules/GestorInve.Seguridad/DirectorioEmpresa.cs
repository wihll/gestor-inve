namespace GestorInve.Seguridad;

/// <summary>
/// Índice NIT → empresa. **Deliberadamente sin RLS**, y es la única tabla del sistema que lo está.
///
/// Por qué tiene que existir: con RLS, encontrar al usuario que inicia sesión exige saber ya su
/// empresa, y averiguar la empresa por su NIT exigiría leer <c>seguridad.empresas</c>, que está
/// filtrada por esa misma empresa. Sin un índice fuera del aislamiento, nadie podría iniciar
/// sesión salvo pasando el UUID de su empresa a mano, o usando un rol con <c>bypassrls</c>
/// (mucho peor: un rol privilegiado en el camino de autenticación).
///
/// Qué expone, y por qué es aceptable: solo que un NIT está registrado y su identificador
/// opaco. Los NIT aparecen en cualquier factura, y el identificador no da acceso a nada: la
/// empresa de la sesión la fija el servidor desde el token validado, nunca el cliente.
///
/// No implementa <see cref="Core.IEntidadDeEmpresa"/> ni <see cref="Core.IRaizDeEmpresa"/>, así
/// que <c>PoliticasRlsTests</c> no le exige política. La exención es intencionada y está aquí
/// escrita para que nadie la confunda con un olvido.
/// </summary>
public class DirectorioEmpresa
{
    private DirectorioEmpresa()
    {
        // EF Core
        Nit = string.Empty;
    }

    public DirectorioEmpresa(string nit, Guid empresaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nit);

        if (empresaId == Guid.Empty)
        {
            throw new ArgumentException("La empresa no puede ser vacía.", nameof(empresaId));
        }

        Nit = Normalizar(nit);
        EmpresaId = empresaId;
        Activa = true;
    }

    public string Nit { get; private set; }

    public Guid EmpresaId { get; private set; }

    public bool Activa { get; private set; }

    /// <summary>Normaliza igual al escribir y al buscar: sin espacios, mayúsculas, sin guiones.</summary>
    public static string Normalizar(string nit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nit);

        return nit.Trim().ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
    }

    public void Desactivar() => Activa = false;
}
