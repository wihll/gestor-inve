using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Lotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_existencias_variante_id_bodega_id_ubicacion_id",
                schema: "inventario",
                table: "existencias");

            migrationBuilder.AddColumn<Guid>(
                name: "lote_id",
                schema: "inventario",
                table: "movimientos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "operacion_id",
                schema: "inventario",
                table: "movimientos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lote_id",
                schema: "inventario",
                table: "existencias",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lotes",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    vencimiento = table.Column<DateOnly>(type: "date", nullable: true),
                    es_serial = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_lotes_variantes_variante_id",
                        column: x => x.variante_id,
                        principalSchema: "catalogo",
                        principalTable: "variantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_lote_id",
                schema: "inventario",
                table: "movimientos",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_operacion_id",
                schema: "inventario",
                table: "movimientos",
                column: "operacion_id");

            // En SQL explícito y no con `CreateIndex`: hace falta `nulls not distinct`, que EF
            // Core no modela. Sin esa cláusula PostgreSQL considera **distintos** dos NULL, así
            // que el índice dejaría crear dos filas de existencias para la misma celda sin lote.
            // Con dos filas, dos transacciones bloquearían filas diferentes con su
            // `SELECT … FOR UPDATE` y el libro dejaría de cuadrar: es la garantía de H7.
            // Requiere PostgreSQL 15 o superior, que ya era el mínimo del proyecto.
            migrationBuilder.Sql(
                """
                create unique index ix_existencias_celda
                    on inventario.existencias (variante_id, bodega_id, ubicacion_id, lote_id)
                    nulls not distinct;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_existencias_lote_id",
                schema: "inventario",
                table: "existencias",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_lotes_empresa_id_variante_id_vencimiento",
                schema: "inventario",
                table: "lotes",
                columns: new[] { "empresa_id", "variante_id", "vencimiento" });

            migrationBuilder.CreateIndex(
                name: "ix_lotes_variante_id_codigo",
                schema: "inventario",
                table: "lotes",
                columns: new[] { "variante_id", "codigo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_existencias_lotes_lote_id",
                schema: "inventario",
                table: "existencias",
                column: "lote_id",
                principalSchema: "inventario",
                principalTable: "lotes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_movimientos_lotes_lote_id",
                schema: "inventario",
                table: "movimientos",
                column: "lote_id",
                principalSchema: "inventario",
                principalTable: "lotes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_existencias_lotes_lote_id",
                schema: "inventario",
                table: "existencias");

            migrationBuilder.DropForeignKey(
                name: "fk_movimientos_lotes_lote_id",
                schema: "inventario",
                table: "movimientos");

            migrationBuilder.DropTable(
                name: "lotes",
                schema: "inventario");

            migrationBuilder.DropIndex(
                name: "ix_movimientos_lote_id",
                schema: "inventario",
                table: "movimientos");

            migrationBuilder.DropIndex(
                name: "ix_movimientos_operacion_id",
                schema: "inventario",
                table: "movimientos");

            migrationBuilder.Sql("drop index inventario.ix_existencias_celda;");

            migrationBuilder.DropIndex(
                name: "ix_existencias_lote_id",
                schema: "inventario",
                table: "existencias");

            migrationBuilder.DropColumn(
                name: "lote_id",
                schema: "inventario",
                table: "movimientos");

            migrationBuilder.DropColumn(
                name: "operacion_id",
                schema: "inventario",
                table: "movimientos");

            migrationBuilder.DropColumn(
                name: "lote_id",
                schema: "inventario",
                table: "existencias");

            migrationBuilder.CreateIndex(
                name: "ix_existencias_variante_id_bodega_id_ubicacion_id",
                schema: "inventario",
                table: "existencias",
                columns: new[] { "variante_id", "bodega_id", "ubicacion_id" },
                unique: true);
        }
    }
}
