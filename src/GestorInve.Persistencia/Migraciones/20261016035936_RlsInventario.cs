using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones;

/// <summary>
/// Políticas RLS del inventario, con el mismo criterio que las migraciones anteriores:
/// <c>force row level security</c> para que el dueño de la tabla tampoco las ignore, y
/// comparación contra <c>nullif(current_setting(...), '')::uuid</c> para que una sesión sin
/// empresa no vea nada.
///
/// Aquí el riesgo es mayor que en el catálogo: el kardex contiene los costos de compra de cada
/// cliente, que es información competitivamente sensible. <c>PoliticasRlsTests</c> falla si
/// alguna tabla se queda sin política.
/// </summary>
public partial class RlsInventario : Migration
{
    private const string EmpresaDeLaSesion = "nullif(current_setting('gestorinve.empresa_id', true), '')::uuid";

    private static readonly string[] Tablas = ["bodegas", "ubicaciones", "movimientos", "existencias"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var tabla in Tablas)
        {
            migrationBuilder.Sql($"alter table inventario.{tabla} enable row level security;");
            migrationBuilder.Sql($"alter table inventario.{tabla} force row level security;");
            migrationBuilder.Sql($"""
                create policy empresa_actual on inventario.{tabla}
                    using (empresa_id = {EmpresaDeLaSesion})
                    with check (empresa_id = {EmpresaDeLaSesion});
                """);
        }

        // El kardex es un libro: nada lo actualiza ni lo borra, ni siquiera por error de código.
        // Esta es la garantía que no depende de que la aplicación se porte bien.
        migrationBuilder.Sql("""
            create or replace function inventario.impedir_cambio_de_movimiento()
            returns trigger language plpgsql as $$
            begin
                raise exception
                    'El kardex es inmutable: un movimiento no se modifica ni se borra, se reversa.';
            end;
            $$;
            """);

        migrationBuilder.Sql("""
            create trigger movimientos_inmutables
            before update or delete on inventario.movimientos
            for each row execute function inventario.impedir_cambio_de_movimiento();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop trigger if exists movimientos_inmutables on inventario.movimientos;");
        migrationBuilder.Sql("drop function if exists inventario.impedir_cambio_de_movimiento();");

        foreach (var tabla in Tablas)
        {
            migrationBuilder.Sql($"drop policy if exists empresa_actual on inventario.{tabla};");
            migrationBuilder.Sql($"alter table inventario.{tabla} no force row level security;");
            migrationBuilder.Sql($"alter table inventario.{tabla} disable row level security;");
        }
    }
}
