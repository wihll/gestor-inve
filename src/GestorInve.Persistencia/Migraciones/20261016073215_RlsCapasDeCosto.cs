using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones;

/// <summary>
/// Políticas RLS de las tablas de costeo, con el mismo criterio que las anteriores:
/// <c>force row level security</c> para que el dueño de la tabla tampoco las ignore, y
/// comparación contra <c>nullif(current_setting(...), '')::uuid</c> para que una sesión sin
/// empresa no vea nada.
///
/// Las capas de costo son el dato más sensible del inventario: contienen el precio al que cada
/// empresa compra, lote por lote. <c>PoliticasRlsTests</c> falla si alguna tabla del modelo se
/// queda sin política, así que esta migración no es opcional.
/// </summary>
public partial class RlsCapasDeCosto : Migration
{
    private const string EmpresaDeLaSesion = "nullif(current_setting('gestorinve.empresa_id', true), '')::uuid";

    private static readonly string[] Tablas = ["capas_costo", "configuracion"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

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
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (var tabla in Tablas)
        {
            migrationBuilder.Sql($"drop policy if exists empresa_actual on inventario.{tabla};");
            migrationBuilder.Sql($"alter table inventario.{tabla} no force row level security;");
            migrationBuilder.Sql($"alter table inventario.{tabla} disable row level security;");
        }
    }
}
