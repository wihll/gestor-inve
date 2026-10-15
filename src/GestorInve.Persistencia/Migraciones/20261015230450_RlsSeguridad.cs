using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones;

/// <summary>
/// Políticas RLS de las tablas de seguridad. Mismo criterio que la migración <c>Rls</c>:
/// <c>force row level security</c> para que el dueño tampoco las ignore, y la comparación
/// contra <c>nullif(current_setting(...), '')::uuid</c> para que la sesión sin empresa no vea nada.
///
/// Sin esto, las credenciales y los tokens de refresco de todas las empresas quedarían
/// accesibles con una sola consulta. <c>PoliticasRlsTests</c> falla si alguna se olvida.
/// </summary>
public partial class RlsSeguridad : Migration
{
    private const string EmpresaDeLaSesion = "nullif(current_setting('gestorinve.empresa_id', true), '')::uuid";

    private static readonly string[] Tablas = ["usuarios", "roles", "usuario_roles", "tokens_refresco"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var tabla in Tablas)
        {
            migrationBuilder.Sql($"alter table seguridad.{tabla} enable row level security;");
            migrationBuilder.Sql($"alter table seguridad.{tabla} force row level security;");
            migrationBuilder.Sql($"""
                create policy empresa_actual on seguridad.{tabla}
                    using (empresa_id = {EmpresaDeLaSesion})
                    with check (empresa_id = {EmpresaDeLaSesion});
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var tabla in Tablas)
        {
            migrationBuilder.Sql($"drop policy if exists empresa_actual on seguridad.{tabla};");
            migrationBuilder.Sql($"alter table seguridad.{tabla} no force row level security;");
            migrationBuilder.Sql($"alter table seguridad.{tabla} disable row level security;");
        }
    }
}
