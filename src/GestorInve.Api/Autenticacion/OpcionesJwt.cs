namespace GestorInve.Api.Autenticacion;

/// <summary>
/// Configuración de los tokens. La clave de firma no tiene valor por defecto a propósito:
/// un sistema que arranca con una clave «de ejemplo» es un sistema donde cualquiera puede
/// fabricar tokens válidos, y eso no debe poder pasar inadvertido en una instalación.
/// </summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";

    /// <summary>Mínimo para HMAC-SHA256: una clave más corta que el hash no aporta más seguridad.</summary>
    public const int BytesMinimosClave = 32;

    public string Emisor { get; set; } = "gestor-inve";

    public string Audiencia { get; set; } = "gestor-inve";

    /// <summary>Clave de firma simétrica. Obligatoria.</summary>
    public string Clave { get; set; } = string.Empty;

    /// <summary>Vida del token de acceso. Corta: no se puede revocar, solo esperar a que caduque.</summary>
    public TimeSpan DuracionAcceso { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Vida del token de refresco, que sí se puede revocar.</summary>
    public TimeSpan DuracionRefresco { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Falla el arranque si la configuración no sirve, en vez de fallar al primer login.</summary>
    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(Clave))
        {
            throw new InvalidOperationException(
                $"Falta la clave de firma de los tokens. Configure `{Seccion}:Clave` "
                + "(variable de entorno Jwt__Clave) con al menos "
                + $"{BytesMinimosClave} bytes aleatorios.");
        }

        var bytes = System.Text.Encoding.UTF8.GetByteCount(Clave);
        if (bytes < BytesMinimosClave)
        {
            throw new InvalidOperationException(
                $"La clave de firma tiene {bytes} bytes; se requieren al menos {BytesMinimosClave}.");
        }

        if (DuracionAcceso <= TimeSpan.Zero || DuracionRefresco <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Las duraciones de los tokens deben ser positivas.");
        }
    }
}
