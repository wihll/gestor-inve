using System.Linq.Expressions;
using GestorInve.Core;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Persistencia;

/// <summary>
/// Contexto único del monolito modular. El aislamiento multiempresa es de doble capa:
/// este filtro global (comodidad y defensa en profundidad) y las políticas RLS de PostgreSQL
/// (la defensa real, que sigue vigente aunque alguien use <c>IgnoreQueryFilters</c> o SQL crudo).
/// </summary>
public class GestorInveDbContext(
    DbContextOptions<GestorInveDbContext> opciones,
    IContextoTenant contextoTenant,
    IEnumerable<IModuloPersistencia> modulos) : DbContext(opciones)
{
    /// <summary>Nombre del ajuste de sesión de PostgreSQL que alimenta las políticas RLS.</summary>
    public const string AjusteEmpresa = "gestorinve.empresa_id";

    public const string EsquemaAuditoria = "auditoria";

    /// <summary>Expuesta para los filtros globales; EF la traduce a un parámetro de la consulta.</summary>
    public Guid? EmpresaIdActual => contextoTenant.EmpresaId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var module in modulos)
        {
            module.Configurar(modelBuilder);
        }

        AplicarFiltrosDeEmpresa(modelBuilder);
    }

    /// <summary>
    /// Añade <c>empresa_id = @empresaActual</c> a toda entidad de empresa. Si no hay empresa en
    /// contexto, la comparación contra NULL no devuelve filas: el comportamiento es «cerrado por defecto».
    /// </summary>
    private void AplicarFiltrosDeEmpresa(ModelBuilder modelo)
    {
        var empresaActual = Expression.Property(Expression.Constant(this), nameof(EmpresaIdActual));

        foreach (var tipo in modelo.Model.GetEntityTypes())
        {
            if (!typeof(IEntidadDeEmpresa).IsAssignableFrom(tipo.ClrType) || tipo.BaseType is not null)
            {
                continue;
            }

            var entidad = Expression.Parameter(tipo.ClrType, "e");
            var empresaDeLaFila = Expression.Convert(
                Expression.Property(entidad, nameof(IEntidadDeEmpresa.EmpresaId)),
                typeof(Guid?));

            var filtro = Expression.Lambda(Expression.Equal(empresaDeLaFila, empresaActual), entidad);

            modelo.Entity(tipo.ClrType).HasQueryFilter(filtro);
        }
    }
}
