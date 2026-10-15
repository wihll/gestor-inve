using GestorInve.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestorInve.Persistencia;

/// <summary>
/// Rellena el rastro de auditoría de <see cref="IAuditable"/> y sella
/// <see cref="IEntidadDeEmpresa.EmpresaId"/> en las filas nuevas, para que nadie
/// pueda crear datos a nombre de otra empresa desde la aplicación.
/// </summary>
public sealed class InterceptorAuditoria(IReloj reloj, IContextoTenant tenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Sellar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Sellar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private string Usuario => string.IsNullOrWhiteSpace(tenant.Usuario) ? "desconocido" : tenant.Usuario;

    private static void Fijar(EntityEntry entrada, string propiedad, object? valor) =>
        entrada.Property(propiedad).CurrentValue = valor;

    private void Sellar(DbContext? db)
    {
        if (db is null)
        {
            return;
        }

        var ahora = reloj.Ahora;

        foreach (var entrada in db.ChangeTracker.Entries())
        {
            if (entrada.State is EntityState.Added)
            {
                SellarEmpresa(entrada);
            }

            if (entrada.Entity is not IAuditable)
            {
                continue;
            }

            switch (entrada.State)
            {
                case EntityState.Added:
                    Fijar(entrada, nameof(IAuditable.CreadoEn), ahora);
                    Fijar(entrada, nameof(IAuditable.CreadoPor), Usuario);
                    break;

                case EntityState.Modified:
                    Fijar(entrada, nameof(IAuditable.ActualizadoEn), ahora);
                    Fijar(entrada, nameof(IAuditable.ActualizadoPor), Usuario);
                    break;

                default:
                    break;
            }
        }
    }

    private void SellarEmpresa(EntityEntry entrada)
    {
        if (entrada.Entity is not IEntidadDeEmpresa)
        {
            return;
        }

        var empresaEnContexto = tenant.EmpresaId
            ?? throw new InvalidOperationException(
                $"No se puede insertar {entrada.Metadata.ClrType.Name} sin empresa en el contexto.");

        entrada.Property(nameof(IEntidadDeEmpresa.EmpresaId)).CurrentValue = empresaEnContexto;
    }
}
