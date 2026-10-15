using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones;

/// <summary>
/// Aislamiento multiempresa en la base de datos (Row Level Security).
///
/// Es la defensa real: sigue vigente aunque alguien use <c>IgnoreQueryFilters()</c>, SQL crudo
/// o se conecte con psql. El filtro global de EF solo es comodidad y defensa en profundidad.
///
/// Detalles que importan:
/// - <c>force row level security</c>: sin esto el dueño de la tabla (el usuario de las migraciones)
///   ignoraría las políticas y el aislamiento sería falso en cualquier despliegue de un solo rol.
/// - <c>nullif(..., '')</c>: la sesión sin empresa trae cadena vacía, y <c>''::uuid</c> aborta con error.
///   Convertirla en NULL hace que la comparación no devuelva filas: cerrado por defecto.
/// - El alta de empresas (onboarding) necesita un rol con <c>bypassrls</c>; se define en H5.
/// </summary>
public partial class Rls : Migration
{
    private const string EmpresaDeLaSesion = "nullif(current_setting('gestorinve.empresa_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        Proteger(migrationBuilder, "seguridad", "empresas", "id");
        Proteger(migrationBuilder, "auditoria", "registros", "empresa_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        Desproteger(migrationBuilder, "seguridad", "empresas");
        Desproteger(migrationBuilder, "auditoria", "registros");
    }

    private static void Proteger(MigrationBuilder migrationBuilder, string esquema, string tabla, string columna)
    {
        migrationBuilder.Sql($"alter table {esquema}.{tabla} enable row level security;");
        migrationBuilder.Sql($"alter table {esquema}.{tabla} force row level security;");
        migrationBuilder.Sql($"""
            create policy empresa_actual on {esquema}.{tabla}
                using ({columna} = {EmpresaDeLaSesion})
                with check ({columna} = {EmpresaDeLaSesion});
            """);
    }

    private static void Desproteger(MigrationBuilder migrationBuilder, string esquema, string tabla)
    {
        migrationBuilder.Sql($"drop policy if exists empresa_actual on {esquema}.{tabla};");
        migrationBuilder.Sql($"alter table {esquema}.{tabla} no force row level security;");
        migrationBuilder.Sql($"alter table {esquema}.{tabla} disable row level security;");
    }
}
