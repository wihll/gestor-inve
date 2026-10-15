using GestorInve.Core;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Persistencia;

/// <summary>
/// Arma las opciones del contexto en un único sitio. Importa que sea uno: los interceptores
/// capturan el contexto de empresa, así que una configuración distinta en producción y en las
/// pruebas significaría probar un cableado que nadie usa.
/// </summary>
public static class ConstructorContexto
{
    public static DbContextOptions<GestorInveDbContext> Opciones(
        string cadenaConexion,
        IReloj reloj,
        IContextoTenant tenant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cadenaConexion);
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentNullException.ThrowIfNull(tenant);

        return new DbContextOptionsBuilder<GestorInveDbContext>()
            .UseNpgsql(cadenaConexion)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new InterceptorAuditoria(reloj, tenant),
                new InterceptorEmpresaEnSesion(tenant))
            .Options;
    }
}

/// <summary>
/// Crea contextos atados a una empresa concreta, para las operaciones que todavía no tienen
/// un usuario autenticado del que deducirla: el inicio de sesión (que ya resolvió la empresa
/// por el directorio), el alta de empresas y los trabajos del <c>Worker</c>.
///
/// No es una puerta trasera al aislamiento: el contexto creado queda sujeto a las mismas
/// políticas RLS, solo que con la empresa indicada en la sesión.
/// </summary>
public sealed class FabricaContextoEmpresa(
    string cadenaConexion,
    IReloj reloj,
    IEnumerable<IModuloPersistencia> modulos)
{
    public GestorInveDbContext Crear(Guid? empresaId, string usuario = "sistema")
    {
        var tenant = new ContextoTenantFijo(empresaId, usuario);

        return new GestorInveDbContext(
            ConstructorContexto.Opciones(cadenaConexion, reloj, tenant),
            tenant,
            modulos);
    }
}
