using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones;

/// <summary>
/// Política RLS de la tabla de lotes, con el mismo criterio que las anteriores:
/// <c>force row level security</c> para que el dueño de la tabla tampoco la ignore, y
/// comparación contra <c>nullif(current_setting(...), '')::uuid</c> para que una sesión sin
/// empresa no vea nada.
///
/// Los lotes no son menos sensibles que el kardex: los códigos y vencimientos de lote de una
/// empresa dicen a quién le compra y con qué rotación. <c>PoliticasRlsTests</c> falla si alguna
/// tabla del modelo se queda sin política, así que esta migración no es opcional.
/// </summary>
public partial class RlsLotes : Migration
{
    private const string EmpresaDeLaSesion = "nullif(current_setting('gestorinve.empresa_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("alter table inventario.lotes enable row level security;");
        migrationBuilder.Sql("alter table inventario.lotes force row level security;");
        migrationBuilder.Sql($"""
            create policy empresa_actual on inventario.lotes
                using (empresa_id = {EmpresaDeLaSesion})
                with check (empresa_id = {EmpresaDeLaSesion});
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("drop policy if exists empresa_actual on inventario.lotes;");
        migrationBuilder.Sql("alter table inventario.lotes no force row level security;");
        migrationBuilder.Sql("alter table inventario.lotes disable row level security;");
    }
}
