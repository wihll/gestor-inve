using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GestorInve.Core.Seguridad;

/// <summary>Política de contraseñas del sistema.</summary>
public static class PoliticaContrasena
{
    public const int LongitudMinima = 12;

    public static void Validar(string? contrasena, string nombreParametro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contrasena, nombreParametro);

        if (contrasena.Length < LongitudMinima)
        {
            throw new ArgumentException(
                $"La contraseña debe tener al menos {LongitudMinima} caracteres.",
                nombreParametro);
        }
    }
}

/// <summary>Hasheo y verificación de contraseñas; se inyecta para poder cambiar de algoritmo.</summary>
public interface IHasheadorContrasena
{
    string Hashear(string contrasena);

    bool Verificar(string contrasena, string hashGuardado);

    /// <summary>Consume el mismo trabajo que una verificación real, para no delatar si el usuario existe.</summary>
    bool VerificarContraFantasma(string contrasena);
}

/// <summary>
/// Hasheo de contraseñas con PBKDF2-HMAC-SHA512 y 210.000 iteraciones, el mínimo que recomienda
/// OWASP para esta primitiva. Se usa PBKDF2 y no Argon2id porque está en la BCL y no añade una
/// dependencia de criptografía de terceros; el formato lleva algoritmo e iteraciones delante,
/// así que endurecerlo o migrar a Argon2id más adelante no invalida las claves ya guardadas.
///
/// Formato: <c>pbkdf2-sha512$iteraciones$salBase64$hashBase64</c>
/// </summary>
public sealed class HasheadorContrasena : IHasheadorContrasena
{
    private const string Algoritmo = "pbkdf2-sha512";
    private const int Iteraciones = 210_000;
    private const int BytesSal = 16;
    private const int BytesHash = 64;

    private static readonly HashAlgorithmName Hmac = HashAlgorithmName.SHA512;

    public string Hashear(string contrasena)
    {
        PoliticaContrasena.Validar(contrasena, nameof(contrasena));

        var sal = RandomNumberGenerator.GetBytes(BytesSal);
        var hash = Derivar(contrasena, sal, Iteraciones);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Algoritmo}${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>
    /// Comparación en tiempo constante. Un hash con formato inesperado devuelve <c>false</c>
    /// en lugar de lanzar: un error distinto según el dato guardado sería un canal lateral.
    /// </summary>
    public bool Verificar(string contrasena, string hashGuardado)
    {
        if (string.IsNullOrEmpty(contrasena) || string.IsNullOrWhiteSpace(hashGuardado))
        {
            return false;
        }

        var partes = hashGuardado.Split('$');
        if (partes.Length != 4 || !string.Equals(partes[0], Algoritmo, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(partes[1], CultureInfo.InvariantCulture, out var iteraciones) || iteraciones <= 0)
        {
            return false;
        }

        if (!TryBase64(partes[2], out var sal) || !TryBase64(partes[3], out var esperado))
        {
            return false;
        }

        var calculado = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, Hmac, esperado.Length);

        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    /// <summary>
    /// Verificación contra un hash inexistente, con el mismo coste que una real. Se llama cuando
    /// el usuario no existe o está inactivo: sin esto, el tiempo de respuesta distinguiría qué
    /// correos están registrados (enumeración de usuarios).
    /// </summary>
    public bool VerificarContraFantasma(string contrasena)
    {
        var sal = RandomNumberGenerator.GetBytes(BytesSal);
        var fantasma = Derivar("contraseña que nadie usa", sal, Iteraciones);
        var calculado = Rfc2898DeriveBytes.Pbkdf2(contrasena ?? string.Empty, sal, Iteraciones, Hmac, BytesHash);

        // El resultado siempre es falso; se compara para que el trabajo no se elimine como código muerto.
        return CryptographicOperations.FixedTimeEquals(calculado, fantasma) && false;
    }

    private static byte[] Derivar(string contrasena, byte[] sal, int iteraciones) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(contrasena), sal, iteraciones, Hmac, BytesHash);

    private static bool TryBase64(string texto, out byte[] bytes)
    {
        bytes = [];
        var destino = new byte[((texto.Length * 3) / 4) + 3];

        if (!Convert.TryFromBase64String(texto, destino, out var escritos) || escritos == 0)
        {
            return false;
        }

        bytes = destino[..escritos];
        return true;
    }
}
