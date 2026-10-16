using System.Security.Claims;
using GestorInve.Seguridad;
using Microsoft.AspNetCore.Mvc;

namespace GestorInve.Api.Autenticacion;

public sealed record PeticionLogin(string Nit, string Correo, string Contrasena);

public sealed record PeticionRefresco(string TokenRefresco);

public sealed record RespuestaSesion(string TokenAcceso, string TokenRefresco, DateTimeOffset ExpiraEn);

public sealed record PeticionAlta(string Nit, string RazonSocial, string CorreoAdmin, string NombreAdmin, string Contrasena);

public sealed record RespuestaYo(Guid UsuarioId, Guid EmpresaId, string Correo, string Nombre, IReadOnlyList<string> Permisos);

public static class EndpointsAutenticacion
{
    public static IEndpointRouteBuilder MapEndpointsAutenticacion(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/auth").WithTags("Autenticación");

        grupo.MapPost("/login", async (
            PeticionLogin peticion,
            ServicioAutenticacion servicio,
            CancellationToken cancelacion) =>
        {
            var resultado = await servicio.IniciarSesionAsync(
                new Credenciales(peticion.Nit, peticion.Correo, peticion.Contrasena),
                cancelacion);

            return Respuesta(resultado);
        })
        .AllowAnonymous()
        .WithSummary("Inicia sesión con NIT, correo y contraseña.");

        grupo.MapPost("/refresh", async (
            PeticionRefresco peticion,
            ServicioAutenticacion servicio,
            CancellationToken cancelacion) =>
        {
            var resultado = await servicio.RefrescarAsync(peticion.TokenRefresco, cancelacion);

            return Respuesta(resultado);
        })
        .AllowAnonymous()
        .WithSummary("Rota el token de refresco y entrega un token de acceso nuevo.");

        grupo.MapPost("/logout", async (
            PeticionRefresco peticion,
            ServicioAutenticacion servicio,
            CancellationToken cancelacion) =>
        {
            await servicio.CerrarSesionAsync(peticion.TokenRefresco, cancelacion);

            return Results.NoContent();
        })
        .AllowAnonymous()
        .WithSummary("Revoca la cadena de refrescos de la sesión.");

        grupo.MapGet("/yo", (ClaimsPrincipal principal) =>
        {
            var empresa = principal.FindFirstValue(ClaimsGestorInve.Empresa);

            return Results.Ok(new RespuestaYo(
                Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? principal.FindFirstValue("sub")!),
                Guid.Parse(empresa!),
                principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                [.. principal.FindAll(ClaimsGestorInve.Permiso).Select(c => c.Value)]));
        })
        .RequireAuthorization()
        .WithSummary("Devuelve la identidad y los permisos del token actual.");

        return rutas;
    }

    public static IEndpointRouteBuilder MapEndpointsEmpresa(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        rutas.MapPost("/empresas", async (
            PeticionAlta peticion,
            ServicioAltaEmpresa servicio,
            CancellationToken cancelacion) =>
        {
            var resultado = await servicio.AltaAsync(
                new SolicitudAlta(
                    peticion.Nit,
                    peticion.RazonSocial,
                    peticion.CorreoAdmin,
                    peticion.NombreAdmin,
                    peticion.Contrasena),
                cancelacion);

            return resultado.Exitoso
                ? Results.Created($"/empresas/{resultado.EmpresaId}", new { empresaId = resultado.EmpresaId })
                : Results.Problem(detail: resultado.Error, statusCode: StatusCodes.Status409Conflict);
        })
        .AllowAnonymous()
        .WithTags("Empresas")
        .WithSummary("Registra una empresa con su primer administrador.");

        // Endpoint de ejemplo para comprobar la autorización por permiso.
        rutas.MapGet("/usuarios", (ClaimsPrincipal principal) =>
            Results.Ok(new { usuario = principal.FindFirstValue(ClaimTypes.Email) }))
            .RequireAuthorization(Permiso.UsuariosLeer)
            .WithTags("Usuarios")
            .WithSummary("Listado de usuarios (requiere el permiso usuarios:leer).");

        return rutas;
    }

    /// <summary>
    /// Un fallo de credenciales y un usuario inexistente devuelven lo mismo: distinguirlos
    /// permitiría averiguar qué correos están registrados. El bloqueo sí se informa (423),
    /// porque el usuario legítimo necesita saber por qué no entra.
    /// </summary>
    private static IResult Respuesta(ResultadoLogin resultado) => resultado.Fallo switch
    {
        MotivoFallo.Ninguno => Results.Ok(new RespuestaSesion(
            resultado.Sesion!.TokenAcceso,
            resultado.Sesion.TokenRefresco,
            resultado.Sesion.ExpiraEn)),

        MotivoFallo.CuentaBloqueada => Results.Problem(
            detail: $"La cuenta está bloqueada temporalmente tras {Usuario.IntentosPermitidos} intentos fallidos.",
            statusCode: StatusCodes.Status423Locked),

        MotivoFallo.CuentaInactiva => Results.Problem(
            detail: "La cuenta está inactiva.",
            statusCode: StatusCodes.Status403Forbidden),

        _ => Results.Problem(
            detail: "Credenciales inválidas.",
            statusCode: StatusCodes.Status401Unauthorized),
    };
}
