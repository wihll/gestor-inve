using GestorInve.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Catalogo;

/// <summary>Configuración del modelo de datos del catálogo.</summary>
public sealed class ModuloCatalogoPersistencia : IModuloPersistencia
{
    public const string Esquema = "catalogo";

    /// <summary>
    /// Precisión de las existencias. 18 dígitos con 6 decimales cubre los decimales máximos de
    /// una unidad (<c>Unidad.DecimalesMaximos</c>) sin acercarse al límite de <c>NUMERIC</c>.
    /// </summary>
    private const string TipoCantidad = "numeric(18,6)";

    public string Nombre => "Catalogo";

    public void Configurar(ModelBuilder modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        modelo.Entity<UnidadMedida>(e =>
        {
            e.ToTable("unidades_medida", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Codigo).HasMaxLength(10).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
            e.Property(x => x.Decimales).IsRequired();
            e.Property(x => x.Activa).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);
            e.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique();
        });

        modelo.Entity<Categoria>(e =>
        {
            e.ToTable("categorias", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Activa).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);

            // Restrict y no Cascade: borrar una rama entera del árbol por accidente sería peor
            // que recibir un error pidiendo recolocar las hijas primero.
            e.HasOne<Categoria>().WithMany().HasForeignKey(x => x.PadreId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.EmpresaId, x.Nombre, x.PadreId }).IsUnique();
        });

        modelo.Entity<Producto>(e =>
        {
            e.ToTable("productos", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Sku).HasMaxLength(50).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(2000);
            e.Property(x => x.UnidadMedidaId).IsRequired();
            e.Property<List<string>>("atributos").HasColumnName("atributos").HasColumnType("text[]").IsRequired();
            e.Ignore(x => x.Atributos);
            e.Property(x => x.ExigeLote).IsRequired();
            e.Property(x => x.ExigeSerial).IsRequired();
            e.Property(x => x.Activo).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);

            // El SKU es único dentro de la empresa, no globalmente: dos clientes del mismo
            // despliegue pueden usar el mismo código para artículos distintos.
            e.HasIndex(x => new { x.EmpresaId, x.Sku }).IsUnique();

            e.HasOne<UnidadMedida>().WithMany().HasForeignKey(x => x.UnidadMedidaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Categoria>().WithMany().HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.SetNull);

            e.HasMany(x => x.Variantes)
                .WithOne()
                .HasForeignKey(v => v.ProductoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Navigation(x => x.Variantes).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelo.Entity<Variante>(e =>
        {
            e.ToTable("variantes", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.ProductoId).IsRequired();
            e.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            e.Property(x => x.AtributosJson).HasColumnName("atributos").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.StockMinimo).HasColumnType(TipoCantidad);
            e.Property(x => x.StockMaximo).HasColumnType(TipoCantidad);
            e.Property(x => x.Activa).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);

            e.HasIndex(x => new { x.EmpresaId, x.Sku }).IsUnique();
            e.HasIndex(x => x.ProductoId);

            e.HasMany(x => x.Codigos)
                .WithOne()
                .HasForeignKey(c => c.VarianteId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Navigation(x => x.Codigos).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelo.Entity<CodigoBarras>(e =>
        {
            e.ToTable("codigos_barras", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.VarianteId).IsRequired();
            e.Property(x => x.Codigo).HasMaxLength(64).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);

            // Único por empresa: si dos variantes compartieran código, escanear en la caja
            // no diría qué se está vendiendo.
            e.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique();
        });
    }
}
