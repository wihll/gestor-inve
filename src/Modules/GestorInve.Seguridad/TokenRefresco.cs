using System.Security.Cryptography;
using GestorInve.Core;

namespace GestorInve.Seguridad;

/// <summary>
/// Token de refresco, guardado solo como hash y de un único uso.
///
/// Se guarda el hash y no el valor porque quien lea la tabla (una copia de seguridad, un volcado,
/// una inyección SQL) no debe poder usarlos. SHA-256 basta aquí, al contrario que con las
/// contraseñas: el token son 256 bits aleatorios, así que no hay nada que adivinar por fuerza bruta.
///
/// Cada refresco rota el token y encadena el nuevo en la misma <see cref="FamiliaId"/>. Si alguien
/// presenta un token ya usado, es señal de que fue robado: se revoca la familia completa, no solo
/// ese token, porque no se puede saber si el que sigue vivo lo tiene el usuario o el atacante.
/// </summary>
public class TokenRefresco : IEntidadDeEmpresa
{
    public const int BytesToken = 32;

    private TokenRefresco()
    {
        // EF Core
        HashToken = string.Empty;
    }

    private TokenRefresco(Guid usuarioId, string hashToken, Guid familiaId, DateTimeOffset creadoEn, DateTimeOffset expiraEn)
    {
        Id = Guid.CreateVersion7();
        UsuarioId = usuarioId;
        HashToken = hashToken;
        FamiliaId = familiaId;
        CreadoEn = creadoEn;
        ExpiraEn = expiraEn;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string HashToken { get; private set; }

    /// <summary>Cadena de rotaciones nacida de un mismo inicio de sesión.</summary>
    public Guid FamiliaId { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset ExpiraEn { get; private set; }

    public DateTimeOffset? UsadoEn { get; private set; }

    public DateTimeOffset? RevocadoEn { get; private set; }

    public Guid? ReemplazadoPorId { get; private set; }

    /// <summary>Crea un token nuevo y devuelve el valor en claro, que solo se ve en este momento.</summary>
    public static (TokenRefresco Token, string Valor) Emitir(
        Guid usuarioId,
        DateTimeOffset ahora,
        TimeSpan duracion,
        Guid? familiaId = null)
    {
        var valor = Base64UrlTextEncoder(RandomNumberGenerator.GetBytes(BytesToken));

        var token = new TokenRefresco(
            usuarioId,
            Hashear(valor),
            familiaId ?? Guid.CreateVersion7(),
            ahora,
            ahora + duracion);

        return (token, valor);
    }

    /// <summary>Hash determinista: es lo que se busca en la tabla cuando llega un token.</summary>
    public static string Hashear(string valor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valor);

        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(valor)));
    }

    public bool EsUtilizable(DateTimeOffset ahora) =>
        UsadoEn is null && RevocadoEn is null && ExpiraEn > ahora;

    /// <summary>Marca el token como consumido y lo encadena con el que lo reemplaza.</summary>
    public void Usar(DateTimeOffset ahora, Guid reemplazadoPorId)
    {
        if (UsadoEn is not null)
        {
            throw new InvalidOperationException("El token de refresco ya fue usado.");
        }

        UsadoEn = ahora;
        ReemplazadoPorId = reemplazadoPorId;
    }

    public void Revocar(DateTimeOffset ahora) => RevocadoEn ??= ahora;

    private static string Base64UrlTextEncoder(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
