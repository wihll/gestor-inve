using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CapasDeCosto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capas_costo",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bodega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad_inicial = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    cantidad_restante = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unidad_codigo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unidad_decimales = table.Column<int>(type: "integer", nullable: false),
                    costo_unitario = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capas_costo", x => x.id);
                    table.ForeignKey(
                        name: "fk_capas_costo_bodegas_bodega_id",
                        column: x => x.bodega_id,
                        principalSchema: "inventario",
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_capas_costo_lotes_lote_id",
                        column: x => x.lote_id,
                        principalSchema: "inventario",
                        principalTable: "lotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_capas_costo_movimiento_movimiento_id",
                        column: x => x.movimiento_id,
                        principalSchema: "inventario",
                        principalTable: "movimientos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_capas_costo_ubicaciones_ubicacion_id",
                        column: x => x.ubicacion_id,
                        principalSchema: "inventario",
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_capas_costo_variantes_variante_id",
                        column: x => x.variante_id,
                        principalSchema: "catalogo",
                        principalTable: "variantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configuracion",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metodo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_capas_costo_bodega_id",
                schema: "inventario",
                table: "capas_costo",
                column: "bodega_id");

            migrationBuilder.CreateIndex(
                name: "ix_capas_costo_lote_id",
                schema: "inventario",
                table: "capas_costo",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_capas_costo_movimiento_id",
                schema: "inventario",
                table: "capas_costo",
                column: "movimiento_id");

            migrationBuilder.CreateIndex(
                name: "ix_capas_costo_ubicacion_id",
                schema: "inventario",
                table: "capas_costo",
                column: "ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_capas_costo_variante_id_bodega_id_ubicacion_id_lote_id_fech",
                schema: "inventario",
                table: "capas_costo",
                columns: new[] { "variante_id", "bodega_id", "ubicacion_id", "lote_id", "fecha", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_empresa_id",
                schema: "inventario",
                table: "configuracion",
                column: "empresa_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capas_costo",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "configuracion",
                schema: "inventario");
        }
    }
}
