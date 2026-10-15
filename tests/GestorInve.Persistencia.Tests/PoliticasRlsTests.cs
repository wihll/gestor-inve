using GestorInve.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestorInve.Persistencia.Tests;

/// <summary>
/// Red de seguridad del aislamiento: olvidar la política RLS de una tabla nueva no rompe nada
/// visible, solo deja de aislar en silencio. La expectativa se deriva del modelo de EF, así que
/// cada entidad de empresa que se añada en el futuro queda cubierta sin tocar esta prueba.
/// </summary>
[Collection(ColeccionBaseDatos.Nombre)]
[Trait("Categoria", "Integracion")]
public class PoliticasRlsTests(BaseDatosFixture bd)
{
    private sealed record Tabla(string Esquema, string Nombre)
    {
        public override string ToString() => $"{Esquema}.{Nombre}";
    }

    private sealed record EstadoRls(bool Activo, bool Forzado, int Politicas, string Expresiones);

    [Fact]
    public async Task Toda_entidad_de_empresa_tiene_RLS_activo_forzado_y_una_politica_que_mira_la_sesion()
    {
        await using var contexto = bd.Crear(null);
        var tablas = TablasQueDebenEstarProtegidas(contexto);

        // Sin esta comprobación, un cambio que dejara el modelo vacío haría pasar la prueba sin probar nada.
        Assert.NotEmpty(tablas);

        var problemas = new List<string>();

        foreach (var tabla in tablas)
        {
            var estado = await LeerEstadoAsync(contexto, tabla);

            if (estado is null)
            {
                problemas.Add($"{tabla}: la tabla no existe en la base de datos.");
                continue;
            }

            if (!estado.Activo)
            {
                problemas.Add($"{tabla}: falta `enable row level security`.");
            }

            if (!estado.Forzado)
            {
                problemas.Add($"{tabla}: falta `force row level security` (el dueño ignoraría la política).");
            }

            if (estado.Politicas == 0)
            {
                problemas.Add($"{tabla}: no tiene ninguna política RLS.");
            }
            else if (!estado.Expresiones.Contains(GestorInveDbContext.AjusteEmpresa, StringComparison.Ordinal))
            {
                problemas.Add(
                    $"{tabla}: ninguna política consulta `{GestorInveDbContext.AjusteEmpresa}`, "
                    + $"así que no aísla por empresa. Expresiones: {estado.Expresiones}");
            }
        }

        Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
    }

    /// <summary>
    /// Se derivan del modelo: entidades que pertenecen a una empresa
    /// (<see cref="IEntidadDeEmpresa"/>) y la propia raíz (<see cref="IRaizDeEmpresa"/>).
    /// </summary>
    private static List<Tabla> TablasQueDebenEstarProtegidas(DbContext contexto) =>
        [.. contexto.Model.GetEntityTypes()
            .Where(t => typeof(IEntidadDeEmpresa).IsAssignableFrom(t.ClrType)
                        || typeof(IRaizDeEmpresa).IsAssignableFrom(t.ClrType))
            .Select(t => new Tabla(t.GetSchema() ?? "public", t.GetTableName()!))
            .Distinct()
            .OrderBy(t => t.ToString(), StringComparer.Ordinal)];

    private static async Task<EstadoRls?> LeerEstadoAsync(DbContext contexto, Tabla tabla)
    {
        var conexion = (NpgsqlConnection)contexto.Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open)
        {
            await conexion.OpenAsync();
        }

        await using var comando = conexion.CreateCommand();
        comando.CommandText = """
            select c.relrowsecurity,
                   c.relforcerowsecurity,
                   (select count(*) from pg_policy p where p.polrelid = c.oid) as politicas,
                   coalesce((
                       select string_agg(coalesce(pol.qual, '') || ' ' || coalesce(pol.with_check, ''), ' | ')
                       from pg_policies pol
                       where pol.schemaname = n.nspname and pol.tablename = c.relname
                   ), '') as expresiones
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = @esquema and c.relname = @tabla
            """;
        comando.Parameters.AddWithValue("@esquema", tabla.Esquema);
        comando.Parameters.AddWithValue("@tabla", tabla.Nombre);

        await using var lector = await comando.ExecuteReaderAsync();

        if (!await lector.ReadAsync())
        {
            return null;
        }

        return new EstadoRls(
            lector.GetBoolean(0),
            lector.GetBoolean(1),
            (int)lector.GetInt64(2),
            lector.GetString(3));
    }
}
