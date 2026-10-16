using GestorInve.Core;
using GestorInve.Core.Seguridad;
using GestorInve.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Seguridad;

public sealed record SolicitudAlta(string Nit, string RazonSocial, string CorreoAdmin, string NombreAdmin, string Contrasena);

public sealed record ResultadoAlta(bool Exitoso, Guid EmpresaId, string? Error)
{
    public static ResultadoAlta Fallo(string error) => new(false, Guid.Empty, error);

    public static ResultadoAlta Ok(Guid empresaId) => new(true, empresaId, null);
}

/// <summary>
/// Alta de una empresa con su primer administrador.
///
/// **No necesita un rol con <c>bypassrls</c>**, al contrario de lo que suponía el ADR 0003: el
/// identificador de la empresa se genera aquí, así que se puede poner esa empresa en la sesión
/// antes de insertar y la política (<c>id = empresa de la sesión</c>) se cumple. Evitar un rol
/// privilegiado en el camino de alta es una ganancia de seguridad, no un atajo.
/// </summary>
public sealed class ServicioAltaEmpresa(
    FabricaContextoEmpresa fabrica,
    IHasheadorContrasena hasheador,
    IReloj reloj)
{
    public const string RolAdministrador = "Administrador";

    public async Task<ResultadoAlta> AltaAsync(SolicitudAlta solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var nit = DirectorioEmpresa.Normalizar(solicitud.Nit);
        var empresaId = Guid.CreateVersion7();

        // Sesión puesta en la empresa que se está creando: así las políticas RLS se cumplen
        // sin privilegios especiales.
        await using var contexto = fabrica.Crear(empresaId, solicitud.CorreoAdmin);

        if (await contexto.Set<DirectorioEmpresa>().AnyAsync(d => d.Nit == nit, cancelacion))
        {
            return ResultadoAlta.Fallo("Ya existe una empresa registrada con ese NIT.");
        }

        var empresa = new Empresa(empresaId, nit, solicitud.RazonSocial);
        var administrador = Usuario.Crear(
            solicitud.CorreoAdmin,
            solicitud.NombreAdmin,
            solicitud.Contrasena,
            hasheador);

        var rol = new Rol(RolAdministrador, Permiso.Todos);
        administrador.AsignarRol(rol);

        contexto.Add(empresa);
        contexto.Add(rol);
        contexto.Add(administrador);
        contexto.Add(new DirectorioEmpresa(nit, empresaId));

        await contexto.SaveChangesAsync(cancelacion);

        return ResultadoAlta.Ok(empresaId);
    }

    /// <summary>Resuelve el NIT sin empresa en la sesión: el directorio está fuera del aislamiento a propósito.</summary>
    public async Task<Guid?> ResolverEmpresaAsync(string nit, CancellationToken cancelacion = default)
    {
        if (string.IsNullOrWhiteSpace(nit))
        {
            return null;
        }

        var normalizado = DirectorioEmpresa.Normalizar(nit);

        await using var contexto = fabrica.Crear(null, "login");

        var entrada = await contexto.Set<DirectorioEmpresa>()
            .SingleOrDefaultAsync(d => d.Nit == normalizado && d.Activa, cancelacion);

        return entrada?.EmpresaId;
    }

    /// <summary>Marca el momento del alta; el reloj se inyecta para que las pruebas lo controlen.</summary>
    public DateTimeOffset Ahora => reloj.Ahora;
}
