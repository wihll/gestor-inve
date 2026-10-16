using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GestorInve.Core;
using GestorInve.Core.Seguridad;
using GestorInve.Persistencia;
using GestorInve.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace GestorInve.Api.Autenticacion;

public sealed record Credenciales(string Nit, string Correo, string Contrasena);

public sealed record Sesion(string TokenAcceso, string TokenRefresco, DateTimeOffset ExpiraEn);

public sealed record ResultadoLogin(Sesion? Sesion, MotivoFallo Fallo);

public enum MotivoFallo
{
    Ninguno,
    CredencialesInvalidas,
    CuentaBloqueada,
    CuentaInactiva,
    RefrescoInvalido,
}

/// <summary>
/// Inicio de sesión, rotación de tokens y cierre.
///
/// Dos propiedades que no se ven en la firma de los métodos:
/// - Un correo o un NIT que no existen responden igual, y con el mismo coste, que una contraseña
///   equivocada: así nadie puede averiguar qué clientes o qué usuarios están registrados.
/// - El refresco es de un solo uso. Si llega un token ya consumido, se revoca su familia
///   completa: significa que alguien tiene una copia y no se puede saber quién es el legítimo.
/// </summary>
public sealed class ServicioAutenticacion(
    FabricaContextoEmpresa fabrica,
    ServicioAltaEmpresa altas,
    IHasheadorContrasena hasheador,
    IReloj reloj,
    OpcionesJwt opciones,
    ILogger<ServicioAutenticacion> registro)
{
    public async Task<ResultadoLogin> IniciarSesionAsync(Credenciales credenciales, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(credenciales);

        var empresaId = await altas.ResolverEmpresaAsync(credenciales.Nit, cancelacion);

        if (empresaId is null)
        {
            // Mismo trabajo que una verificación real: el tiempo de respuesta no debe delatar
            // si el NIT está registrado.
            hasheador.VerificarContraFantasma(credenciales.Contrasena);
            registro.NitNoRegistrado();

            return new ResultadoLogin(null, MotivoFallo.CredencialesInvalidas);
        }

        await using var contexto = fabrica.Crear(empresaId, "login");

        string correo;
        try
        {
            correo = Usuario.NormalizarCorreo(credenciales.Correo);
        }
        catch (ArgumentException)
        {
            hasheador.VerificarContraFantasma(credenciales.Contrasena);
            return new ResultadoLogin(null, MotivoFallo.CredencialesInvalidas);
        }

        var usuario = await contexto.Set<Usuario>()
            .Include(u => u.Asignaciones)
            .ThenInclude(a => a.Rol)
            .SingleOrDefaultAsync(u => u.Correo == correo, cancelacion);

        if (usuario is null)
        {
            hasheador.VerificarContraFantasma(credenciales.Contrasena);
            registro.UsuarioInexistente(empresaId.Value);

            return new ResultadoLogin(null, MotivoFallo.CredencialesInvalidas);
        }

        var resultado = usuario.IntentarAutenticar(credenciales.Contrasena, hasheador, reloj.Ahora);

        // Se guarda también en el fallo: si no, el contador de intentos no avanzaría
        // y el bloqueo por fuerza bruta no existiría en la práctica.
        await contexto.SaveChangesAsync(cancelacion);

        switch (resultado)
        {
            case ResultadoAutenticacion.Bloqueado:
                registro.CuentaBloqueada(usuario.Id);
                return new ResultadoLogin(null, MotivoFallo.CuentaBloqueada);

            case ResultadoAutenticacion.Inactivo:
                return new ResultadoLogin(null, MotivoFallo.CuentaInactiva);

            case ResultadoAutenticacion.CredencialesInvalidas:
                registro.ContrasenaIncorrecta(usuario.Id, usuario.IntentosFallidos);
                return new ResultadoLogin(null, MotivoFallo.CredencialesInvalidas);

            default:
                break;
        }

        var sesion = await EmitirSesionAsync(contexto, empresaId.Value, usuario, null, cancelacion);
        registro.AccesoCorrecto(usuario.Id, empresaId.Value);

        return new ResultadoLogin(sesion, MotivoFallo.Ninguno);
    }

    public async Task<ResultadoLogin> RefrescarAsync(string tokenRefresco, CancellationToken cancelacion = default)
    {
        if (!TryPartir(tokenRefresco, out var empresaId, out var secreto))
        {
            return new ResultadoLogin(null, MotivoFallo.RefrescoInvalido);
        }

        await using var contexto = fabrica.Crear(empresaId, "refresco");

        var hash = TokenRefresco.Hashear(secreto);
        var token = await contexto.Set<TokenRefresco>().SingleOrDefaultAsync(t => t.HashToken == hash, cancelacion);

        if (token is null)
        {
            return new ResultadoLogin(null, MotivoFallo.RefrescoInvalido);
        }

        if (token.UsadoEn is not null)
        {
            // Reutilización: alguien presentó un token ya consumido. No se puede distinguir al
            // usuario legítimo del atacante, así que caen todos los de la cadena.
            await RevocarFamiliaAsync(contexto, token.FamiliaId, cancelacion);
            registro.RefrescoReutilizado(token.FamiliaId);

            return new ResultadoLogin(null, MotivoFallo.RefrescoInvalido);
        }

        if (!token.EsUtilizable(reloj.Ahora))
        {
            return new ResultadoLogin(null, MotivoFallo.RefrescoInvalido);
        }

        var usuario = await contexto.Set<Usuario>()
            .Include(u => u.Asignaciones)
            .ThenInclude(a => a.Rol)
            .SingleOrDefaultAsync(u => u.Id == token.UsuarioId, cancelacion);

        if (usuario is null || !usuario.Activo)
        {
            await RevocarFamiliaAsync(contexto, token.FamiliaId, cancelacion);
            return new ResultadoLogin(null, MotivoFallo.RefrescoInvalido);
        }

        var sesion = await EmitirSesionAsync(contexto, empresaId, usuario, token, cancelacion);

        return new ResultadoLogin(sesion, MotivoFallo.Ninguno);
    }

    /// <summary>Cierra la sesión revocando la familia entera: el cierre vale para esa cadena de refrescos.</summary>
    public async Task CerrarSesionAsync(string tokenRefresco, CancellationToken cancelacion = default)
    {
        if (!TryPartir(tokenRefresco, out var empresaId, out var secreto))
        {
            return;
        }

        await using var contexto = fabrica.Crear(empresaId, "logout");

        var hash = TokenRefresco.Hashear(secreto);
        var token = await contexto.Set<TokenRefresco>().SingleOrDefaultAsync(t => t.HashToken == hash, cancelacion);

        if (token is not null)
        {
            await RevocarFamiliaAsync(contexto, token.FamiliaId, cancelacion);
        }
    }

    private static async Task RevocarFamiliaAsync(DbContext contexto, Guid familiaId, CancellationToken cancelacion)
    {
        var familia = await contexto.Set<TokenRefresco>()
            .Where(t => t.FamiliaId == familiaId && t.RevocadoEn == null)
            .ToListAsync(cancelacion);

        foreach (var hermano in familia)
        {
            hermano.Revocar(DateTimeOffset.UtcNow);
        }

        await contexto.SaveChangesAsync(cancelacion);
    }

    /// <summary>
    /// El token de refresco lleva delante la empresa porque al refrescar el token de acceso
    /// puede estar caducado y no se puede confiar en sus reclamaciones. El identificador no es
    /// un secreto: la empresa efectiva siempre la fija el servidor desde el token validado.
    /// </summary>
    private static bool TryPartir(string? tokenRefresco, out Guid empresaId, out string secreto)
    {
        empresaId = Guid.Empty;
        secreto = string.Empty;

        if (string.IsNullOrWhiteSpace(tokenRefresco))
        {
            return false;
        }

        var partes = tokenRefresco.Split('.', 2);

        if (partes.Length != 2 || !Guid.TryParseExact(partes[0], "N", out empresaId) || partes[1].Length == 0)
        {
            return false;
        }

        secreto = partes[1];
        return true;
    }

    private async Task<Sesion> EmitirSesionAsync(
        GestorInveDbContext contexto,
        Guid empresaId,
        Usuario usuario,
        TokenRefresco? anterior,
        CancellationToken cancelacion)
    {
        var (nuevo, secreto) = TokenRefresco.Emitir(
            usuario.Id,
            reloj.Ahora,
            opciones.DuracionRefresco,
            anterior?.FamiliaId);

        contexto.Add(nuevo);
        anterior?.Usar(reloj.Ahora, nuevo.Id);

        await contexto.SaveChangesAsync(cancelacion);

        var expira = reloj.Ahora + opciones.DuracionAcceso;

        return new Sesion(
            GenerarTokenAcceso(empresaId, usuario, expira),
            $"{empresaId:N}.{secreto}",
            expira);
    }

    private string GenerarTokenAcceso(Guid empresaId, Usuario usuario, DateTimeOffset expira)
    {
        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opciones.Clave));

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))),
            new(ClaimTypes.Email, usuario.Correo),
            new(ClaimTypes.Name, usuario.NombreCompleto),
            new(ClaimsGestorInve.Empresa, empresaId.ToString()),
            .. usuario.Permisos().Select(p => new Claim(ClaimsGestorInve.Permiso, p)),
        ];

        var token = new JwtSecurityToken(
            issuer: opciones.Emisor,
            audience: opciones.Audiencia,
            claims: claims,
            notBefore: reloj.Ahora.UtcDateTime,
            expires: expira.UtcDateTime,
            signingCredentials: new SigningCredentials(clave, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
