using System.Security.Claims;
using GestorInve.Core;

namespace GestorInve.Api.Autenticacion;

/// <summary>Nombres de las reclamaciones propias que viajan en el token.</summary>
public static class ClaimsGestorInve
{
    public const string Empresa = "empresa_id";
    public const string Permiso = "permiso";
}

/// <summary>
/// Empresa y usuario de la petición, tomados **solo** del token ya validado.
///
/// Nunca de una cabecera, un parámetro de consulta ni el cuerpo: si el cliente pudiera influir
/// en la empresa, el aislamiento multiempresa se caería con un solo encabezado falsificado.
/// Sin usuario autenticado no hay empresa, y el contexto queda cerrado por defecto.
/// </summary>
public sealed class ContextoTenantHttp(IHttpContextAccessor acceso) : IContextoTenant
{
    public Guid? EmpresaId
    {
        get
        {
            var principal = acceso.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var valor = principal.FindFirstValue(ClaimsGestorInve.Empresa);

            return Guid.TryParse(valor, out var empresa) ? empresa : null;
        }
    }

    public string Usuario
    {
        get
        {
            var principal = acceso.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true)
            {
                return "anonimo";
            }

            return principal.FindFirstValue(ClaimTypes.Email)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "desconocido";
        }
    }
}
