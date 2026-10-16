using GestorInve.Catalogo;
using GestorInve.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Inventario;

/// <summary>Configuración del modelo de datos del inventario.</summary>
public sealed class ModuloInventarioPersistencia : IModuloPersistencia
{
    public const string Esquema = "inventario";

    /// <summary>Cantidades: 6 decimales, los máximos que admite una unidad.</summary>
    private const string TipoCantidad = "numeric(18,6)";

    /// <summary>Costo unitario con 6 decimales: redondear a dos en cada paso acumula deriva.</summary>
    private const string TipoCosto = "numeric(18,6)";

    /// <summary>Importes: 2 decimales.</summary>
    private const string TipoImporte = "numeric(18,2)";

    public string Nombre => "Inventario";

    public void Configurar(ModelBuilder modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        modelo.Entity<Bodega>(e =>
        {
            e.ToTable("bodegas", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Activa).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);
            e.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique();

            e.HasMany(x => x.Ubicaciones)
                .WithOne()
                .HasForeignKey(u => u.BodegaId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Navigation(x => x.Ubicaciones).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelo.Entity<Ubicacion>(e =>
        {
            e.ToTable("ubicaciones", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.BodegaId).IsRequired();
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Activa).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);
            e.HasIndex(x => new { x.BodegaId, x.Codigo }).IsUnique();
        });

        modelo.Entity<Movimiento>(e =>
        {
            e.ToTable("movimientos", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.VarianteId).IsRequired();
            e.Property(x => x.BodegaId).IsRequired();
            e.Property(x => x.UbicacionId).IsRequired();
            e.Property(x => x.Cantidad).HasColumnType(TipoCantidad).IsRequired();
            e.Property(x => x.UnidadCodigo).HasMaxLength(10).IsRequired();
            e.Property(x => x.UnidadDecimales).IsRequired();
            e.Property(x => x.CostoUnitario).HasColumnType(TipoCosto).IsRequired();
            e.Property(x => x.CostoTotal).HasColumnType(TipoImporte).IsRequired();
            e.Property(x => x.Moneda).HasMaxLength(3).IsRequired();
            e.Property(x => x.SaldoCantidadDespues).HasColumnType(TipoCantidad).IsRequired();
            e.Property(x => x.SaldoCostoPromedioDespues).HasColumnType(TipoCosto).IsRequired();
            e.Property(x => x.Fecha).IsRequired();
            e.Property(x => x.Usuario).HasMaxLength(100).IsRequired();
            e.Property(x => x.Referencia).HasMaxLength(100);
            e.Property(x => x.Observacion).HasMaxLength(1000);
            e.Property(x => x.ForzadoNegativo).IsRequired();

            // `CantidadFirmada` se calcula a partir del tipo; no es columna.
            e.Ignore(x => x.CantidadFirmada);

            // El kardex se consulta por celda y en orden cronológico.
            e.HasIndex(x => new { x.EmpresaId, x.VarianteId, x.BodegaId, x.UbicacionId, x.Fecha });
            e.HasIndex(x => x.TrasladoId);

            // **Único**: un movimiento se reversa a lo sumo una vez. Es la garantía que permite
            // no mutar el original y mantener el libro inmutable.
            e.HasIndex(x => x.MovimientoReversadoId).IsUnique();

            e.HasOne<Variante>().WithMany().HasForeignKey(x => x.VarianteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Bodega>().WithMany().HasForeignKey(x => x.BodegaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ubicacion>().WithMany().HasForeignKey(x => x.UbicacionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Movimiento>().WithMany().HasForeignKey(x => x.MovimientoReversadoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<Existencia>(e =>
        {
            e.ToTable("existencias", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.VarianteId).IsRequired();
            e.Property(x => x.BodegaId).IsRequired();
            e.Property(x => x.UbicacionId).IsRequired();
            e.Property(x => x.Cantidad).HasColumnType(TipoCantidad).IsRequired();
            e.Property(x => x.CostoPromedio).HasColumnType(TipoCosto).IsRequired();
            e.Property(x => x.UnidadCodigo).HasMaxLength(10).IsRequired();
            e.Property(x => x.UnidadDecimales).IsRequired();
            e.Property(x => x.Moneda).HasMaxLength(3).IsRequired();
            e.Property(x => x.ActualizadoEn).IsRequired();

            // Una sola fila por celda: es lo que se bloquea con FOR UPDATE.
            e.HasIndex(x => new { x.VarianteId, x.BodegaId, x.UbicacionId }).IsUnique();

            e.HasOne<Variante>().WithMany().HasForeignKey(x => x.VarianteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Bodega>().WithMany().HasForeignKey(x => x.BodegaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ubicacion>().WithMany().HasForeignKey(x => x.UbicacionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
