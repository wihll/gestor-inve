using GestorInve.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Seguridad;

/// <summary>Configuración del modelo de datos del módulo de seguridad.</summary>
public sealed class ModuloSeguridadPersistencia : IModuloPersistencia
{
    public const string Esquema = "seguridad";

    public string Nombre => "Seguridad";

    public void Configurar(ModelBuilder modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        modelo.Entity<Empresa>(e =>
        {
            e.ToTable("empresas", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Nit).HasMaxLength(20).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Activa).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);
            e.HasIndex(x => x.Nit).IsUnique();
        });

        modelo.Entity<RegistroAuditoria>(e =>
        {
            e.ToTable("registros", GestorInveDbContext.EsquemaAuditoria);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Entidad).HasMaxLength(200).IsRequired();
            e.Property(x => x.ClaveEntidad).HasMaxLength(200).IsRequired();
            e.Property(x => x.Accion).HasMaxLength(50).IsRequired();
            e.Property(x => x.Datos).HasColumnType("jsonb");
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);
            e.HasIndex(x => new { x.EmpresaId, x.CreadoEn });
        });
    }
}
