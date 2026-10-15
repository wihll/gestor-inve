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

        modelo.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Correo).HasMaxLength(320).IsRequired();
            e.Property(x => x.NombreCompleto).HasMaxLength(200).IsRequired();
            e.Property(x => x.HashContrasena).HasMaxLength(512).IsRequired();
            e.Property(x => x.Activo).IsRequired();
            e.Property(x => x.IntentosFallidos).IsRequired();
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);

            // El correo es único dentro de la empresa, no globalmente: la misma persona puede
            // existir en dos empresas distintas del mismo despliegue.
            e.HasIndex(x => new { x.EmpresaId, x.Correo }).IsUnique();

            e.HasMany(x => x.Asignaciones)
                .WithOne()
                .HasForeignKey(a => a.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Navigation(x => x.Asignaciones).UsePropertyAccessMode(PropertyAccessMode.Field);

            // `Roles` es una proyección de las asignaciones, no una navegación: sin esto EF la
            // descubre por convención e intenta escribir en una lista de solo lectura.
            e.Ignore(x => x.Roles);
        });

        modelo.Entity<Rol>(e =>
        {
            e.ToTable("roles", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
            e.Property<List<string>>("permisos").HasColumnName("permisos").HasColumnType("text[]").IsRequired();
            e.Ignore(x => x.Permisos);
            e.Property(x => x.CreadoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActualizadoPor).HasMaxLength(100);
            e.HasIndex(x => new { x.EmpresaId, x.Nombre }).IsUnique();
        });

        modelo.Entity<UsuarioRol>(e =>
        {
            e.ToTable("usuario_roles", Esquema);
            e.HasKey(x => new { x.UsuarioId, x.RolId });
            e.Property(x => x.EmpresaId).IsRequired();
            e.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.RolId).OnDelete(DeleteBehavior.Cascade);
        });

        modelo.Entity<TokenRefresco>(e =>
        {
            e.ToTable("tokens_refresco", Esquema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.UsuarioId).IsRequired();
            e.Property(x => x.HashToken).HasMaxLength(64).IsRequired();
            e.Property(x => x.FamiliaId).IsRequired();

            // Se busca por hash en cada refresco; único para que no haya dos filas por token.
            e.HasIndex(x => x.HashToken).IsUnique();
            e.HasIndex(x => x.FamiliaId);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });
        modelo.Entity<DirectorioEmpresa>(e =>
        {
            e.ToTable("directorio_empresas", Esquema);
            e.HasKey(x => x.Nit);
            e.Property(x => x.Nit).HasMaxLength(20);
            e.Property(x => x.EmpresaId).IsRequired();
            e.Property(x => x.Activa).IsRequired();
        });
    }
}
