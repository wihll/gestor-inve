using Microsoft.EntityFrameworkCore;

namespace GestorInve.Persistencia;

/// <summary>
/// Aplica las migraciones pendientes **de una en una entre procesos**.
///
/// `MigrateAsync` a secas no es seguro si dos procesos migran la misma base a la vez: cada uno
/// calcula su lista de pendientes **antes** de tomar el bloqueo de la tabla de historial, así que
/// el segundo espera a que el primero termine y después repite las mismas migraciones
/// («relation "bodegas" already exists»). Pasa con dos instancias que arrancan juntas con
/// `GESTORINVE_MIGRAR=true`, y pasaba en el CI entre dos ensamblados de pruebas: estuvo latente
/// varios hitos y solo salió cuando cambió el orden de ejecución.
///
/// Un bloqueo consultivo de PostgreSQL lo serializa: quien llega segundo espera, y al entrar
/// recalcula los pendientes, que ya no hay.
/// </summary>
public static class MigracionSerializada
{
    /// <summary>Identificador del bloqueo consultivo. Arbitrario, pero fijo: es lo que comparten los procesos.</summary>
    private const long Cerrojo = 0x67_65_73_74_6F_72_69_6E; // «gestorin» en ASCII

    /// <returns>Las migraciones que este proceso aplicó; vacío si la base ya estaba al día.</returns>
    public static async Task<IReadOnlyList<string>> AplicarAsync(DbContext contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        // El bloqueo consultivo es de **sesión**: la conexión tiene que seguir abierta y ser la
        // misma durante toda la migración, o se soltaría solo al devolverla al pool.
        await contexto.Database.OpenConnectionAsync(ct);

        try
        {
            await contexto.Database.ExecuteSqlAsync($"select pg_advisory_lock({Cerrojo})", ct);

            try
            {
                var pendientes = (await contexto.Database.GetPendingMigrationsAsync(ct)).ToList();

                if (pendientes.Count > 0)
                {
                    await contexto.Database.MigrateAsync(ct);
                }

                return pendientes;
            }
            finally
            {
                await contexto.Database.ExecuteSqlAsync($"select pg_advisory_unlock({Cerrojo})", CancellationToken.None);
            }
        }
        finally
        {
            await contexto.Database.CloseConnectionAsync();
        }
    }
}
