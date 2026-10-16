using GestorInve.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestorInve.Persistencia;

public static class ExtensionesPersistencia
{
    /// <summary>
    /// Registra el contexto (convención <c>snake_case</c>: los nombres de columna deben coincidir
    /// con los que usan las políticas RLS), los interceptores y la fábrica por empresa.
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

        servicios.AddSingleton(proveedor => new FabricaContextoEmpresa(
            cadenaConexion,
            proveedor.GetRequiredService<IReloj>(),
            proveedor.GetRequiredService<IEnumerable<IModuloPersistencia>>()));

        return servicios;
    }
}
