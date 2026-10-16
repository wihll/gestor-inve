using GestorInve.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestorInve.Persistencia;

public static class ExtensionesPersistencia
{
    /// <summary>
    /// Registra el contexto con convención <c>snake_case</c> (los nombres de columna deben
    /// coincidir con los que usan las políticas RLS), el interceptor de auditoría y el de sesión.
    /// </summary>
    public static IServiceCollection AddPersistencia(this IServiceCollection servicios, string cadenaConexion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentException.ThrowIfNullOrWhiteSpace(cadenaConexion);

        servicios.AddSingleton<IReloj, RelojSistema>();

        servicios.AddDbContext<GestorInveDbContext>((proveedor, opciones) =>
            opciones.UseNpgsql(cadenaConexion)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    new InterceptorAuditoria(
                        proveedor.GetRequiredService<IReloj>(),
                        proveedor.GetRequiredService<IContextoTenant>()),
                    new InterceptorEmpresaEnSesion(proveedor.GetRequiredService<IContextoTenant>())));

        return servicios;
    }
}
