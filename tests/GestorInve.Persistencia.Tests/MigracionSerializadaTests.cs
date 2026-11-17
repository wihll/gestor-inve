using GestorInve.Composicion;
using GestorInve.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestorInve.Persistencia.Tests;

/// <summary>
/// Varios procesos migrando la misma base a la vez. Con `MigrateAsync` a secas el segundo repite
/// las migraciones del primero («relation already exists»): pasó en el CI entre dos ensamblados
/// de pruebas, y pasaría con dos instancias que arrancan juntas con `GESTORINVE_MIGRAR=true`.
///
/// Usa una base **propia** (`gestorinve_migracion`), que vacía al empezar: la carrera solo existe
/// partiendo de una base sin esquema, y vaciar la compartida rompería a las demás pruebas.
/// </summary>
[Trait("Categoria", "Integracion")]
public class MigracionSerializadaTests
{
    private static readonly string Cadena =
        Environment.GetEnvironmentVariable("GESTORINVE_CONEXION_MIGRACION")
        ?? "Host=127.0.0.1;Port=5432;Database=gestorinve_migracion;Username=gestorinve;Password=gestorinve";

    private static GestorInveDbContext Crear() =>
        new FabricaContextoEmpresa(Cadena, new RelojSistema(), ModulosDePersistencia.Todos).Crear(null, "pruebas");

    [Fact]
    public async Task Ocho_migraciones_simultaneas_sobre_una_base_vacia_terminan_bien_y_solo_una_migra()
    {
        await VaciarAsync();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var contexto = Crear();

            return await MigracionSerializada.AplicarAsync(contexto);
        }));

        // Exactamente una aplicó el esquema; las demás esperaron y encontraron la base al día.
        Assert.Single(resultados, r => r.Count > 0);
        Assert.Equal(7, resultados.Count(r => r.Count == 0));

        await using var comprobacion = Crear();
        Assert.Empty(await comprobacion.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Sobre_una_base_al_dia_no_aplica_nada()
    {
        await using (var primero = Crear())
        {
            await MigracionSerializada.AplicarAsync(primero);
        }

        await using var segundo = Crear();

        Assert.Empty(await MigracionSerializada.AplicarAsync(segundo));
    }

    /// <summary>Deja la base sin esquema, como recién creada. El rol es su dueño, así que puede.</summary>
    private static async Task VaciarAsync()
    {
        await using var conexion = new NpgsqlConnection(Cadena);
        await conexion.OpenAsync();

        var esquemas = new List<string>();

        await using (var consulta = conexion.CreateCommand())
        {
            consulta.CommandText = """
                select nspname from pg_namespace
                where nspname not like 'pg\_%' and nspname not in ('information_schema', 'public')
                """;

            await using var lector = await consulta.ExecuteReaderAsync();

            while (await lector.ReadAsync())
            {
                esquemas.Add(lector.GetString(0));
            }
        }

        foreach (var esquema in esquemas)
        {
            await using var borrar = conexion.CreateCommand();
            borrar.CommandText = $"drop schema \"{esquema}\" cascade";
            await borrar.ExecuteNonQueryAsync();
        }

        await using var historial = conexion.CreateCommand();
        historial.CommandText = "drop table if exists public.\"__EFMigrationsHistory\"";
        await historial.ExecuteNonQueryAsync();
    }
}
