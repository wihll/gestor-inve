using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones;

/// <summary>
/// Políticas RLS de las tablas del catálogo. Mismo criterio que las migraciones <c>Rls</c> y
/// <c>RlsSeguridad</c>: <c>force row level security</c> para que el dueño de la tabla tampoco
/// las ignore, y comparación contra <c>nullif(current_setting(...), '')::uuid</c> para que una
/// sesión sin empresa no vea nada.
///
/// Sin esto, el catálogo completo de todos los clientes (precios de costo futuros incluidos)
/// quedaría accesible con una sola consulta. <c>PoliticasRlsTests</c> falla si alguna se olvida.
/// </summary>
public partial class RlsCatalogo : Migration
{
    private const string EmpresaDeLaSesion = "nullif(current_setting('gestorinve.empresa_id', true), '')::uuid";

    private static readonly string[] Tablas =
        ["unidades_medida", "categorias", "productos", "variantes", "codigos_barras"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var tabla in Tablas)
        {
            migrationBuilder.Sql($"alter table catalogo.{tabla} enable row level security;");
            migrationBuilder.Sql($"alter table catalogo.{tabla} force row level security;");
            migrationBuilder.Sql($"""
                create policy empresa_actual on catalogo.{tabla}
                    using (empresa_id = {EmpresaDeLaSesion})
                    with check (empresa_id = {EmpresaDeLaSesion});
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var tabla in Tablas)
        {
            migrationBuilder.Sql($"drop policy if exists empresa_actual on catalogo.{tabla};");
            migrationBuilder.Sql($"alter table catalogo.{tabla} no force row level security;");
            migrationBuilder.Sql($"alter table catalogo.{tabla} disable row level security;");
        }
    }
}
