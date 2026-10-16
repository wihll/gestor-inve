using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorInve.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventario");

            migrationBuilder.CreateTable(
                name: "bodegas",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bodegas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ubicaciones",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bodega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ubicaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_ubicaciones_bodegas_bodega_id",
                        column: x => x.bodega_id,
                        principalSchema: "inventario",
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "existencias",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bodega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    costo_promedio = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unidad_codigo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unidad_decimales = table.Column<int>(type: "integer", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_existencias", x => x.id);
                    table.ForeignKey(
                        name: "fk_existencias_bodegas_bodega_id",
                        column: x => x.bodega_id,
                        principalSchema: "inventario",
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_existencias_ubicaciones_ubicacion_id",
                        column: x => x.ubicacion_id,
                        principalSchema: "inventario",
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_existencias_variantes_variante_id",
                        column: x => x.variante_id,
                        principalSchema: "catalogo",
                        principalTable: "variantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimientos",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    variante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bodega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unidad_codigo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unidad_decimales = table.Column<int>(type: "integer", nullable: false),
                    costo_unitario = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    costo_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    saldo_cantidad_despues = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    saldo_costo_promedio_despues = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    observacion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    traslado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movimiento_reversado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    forzado_negativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos", x => x.id);
                    table.ForeignKey(
                        name: "fk_movimientos_bodegas_bodega_id",
                        column: x => x.bodega_id,
                        principalSchema: "inventario",
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimientos_movimientos_movimiento_reversado_id",
                        column: x => x.movimiento_reversado_id,
                        principalSchema: "inventario",
                        principalTable: "movimientos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimientos_ubicaciones_ubicacion_id",
                        column: x => x.ubicacion_id,
                        principalSchema: "inventario",
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimientos_variantes_variante_id",
                        column: x => x.variante_id,
                        principalSchema: "catalogo",
                        principalTable: "variantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bodegas_empresa_id_codigo",
                schema: "inventario",
                table: "bodegas",
                columns: new[] { "empresa_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_existencias_bodega_id",
                schema: "inventario",
                table: "existencias",
                column: "bodega_id");

            migrationBuilder.CreateIndex(
                name: "ix_existencias_ubicacion_id",
                schema: "inventario",
                table: "existencias",
                column: "ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_existencias_variante_id_bodega_id_ubicacion_id",
                schema: "inventario",
                table: "existencias",
                columns: new[] { "variante_id", "bodega_id", "ubicacion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_bodega_id",
                schema: "inventario",
                table: "movimientos",
                column: "bodega_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_empresa_id_variante_id_bodega_id_ubicacion_id_f",
                schema: "inventario",
                table: "movimientos",
                columns: new[] { "empresa_id", "variante_id", "bodega_id", "ubicacion_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_movimiento_reversado_id",
                schema: "inventario",
                table: "movimientos",
                column: "movimiento_reversado_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_traslado_id",
                schema: "inventario",
                table: "movimientos",
                column: "traslado_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_ubicacion_id",
                schema: "inventario",
                table: "movimientos",
                column: "ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_variante_id",
                schema: "inventario",
                table: "movimientos",
                column: "variante_id");

            migrationBuilder.CreateIndex(
                name: "ix_ubicaciones_bodega_id_codigo",
                schema: "inventario",
                table: "ubicaciones",
                columns: new[] { "bodega_id", "codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "existencias",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "movimientos",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "ubicaciones",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "bodegas",
                schema: "inventario");
        }
    }
}
