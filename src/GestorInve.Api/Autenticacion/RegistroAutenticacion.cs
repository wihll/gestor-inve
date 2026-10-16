namespace GestorInve.Api.Autenticacion;

/// <summary>
/// Trazas de autenticación con <c>LoggerMessage</c> (generadas en compilación, sin asignaciones
/// cuando el nivel está apagado). Ninguna registra la contraseña ni el token; el correo tampoco,
/// para no dejar un listado de usuarios en los ficheros de registro.
/// </summary>
internal static partial class RegistroAutenticacion
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Intento de acceso con un NIT no registrado.")]
    public static partial void NitNoRegistrado(this ILogger registro);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Intento de acceso con un usuario inexistente en la empresa {Empresa}.")]
    public static partial void UsuarioInexistente(this ILogger registro, Guid empresa);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Contraseña incorrecta para {Usuario}; intentos fallidos: {Intentos}.")]
    public static partial void ContrasenaIncorrecta(this ILogger registro, Guid usuario, int intentos);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Acceso rechazado por bloqueo: usuario {Usuario}.")]
    public static partial void CuentaBloqueada(this ILogger registro, Guid usuario);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Acceso correcto de {Usuario} en la empresa {Empresa}.")]
    public static partial void AccesoCorrecto(this ILogger registro, Guid usuario, Guid empresa);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "Token de refresco reutilizado; familia {Familia} revocada completa.")]
    public static partial void RefrescoReutilizado(this ILogger registro, Guid familia);
}
