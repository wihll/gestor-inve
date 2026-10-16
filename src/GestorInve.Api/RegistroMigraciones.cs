namespace GestorInve.Api;

/// <summary>Traza de la actualización del esquema, con `LoggerMessage` como exige el analizador.</summary>
internal static partial class RegistroMigraciones
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Aplicando {Cantidad} migraciones pendientes: {Migraciones}")]
    public static partial void Aplicando(this ILogger registro, int cantidad, string migraciones);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "El esquema ya estaba al día.")]
    public static partial void SinPendientes(this ILogger registro);
}
