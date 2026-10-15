using GestorInve.Core;

namespace GestorInve.Persistencia;

/// <summary>Reloj de producción.</summary>
public sealed class RelojSistema : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
}

/// <summary>
/// Contexto de empresa fijo, para el arranque, los trabajos del <c>Worker</c> y las pruebas.
/// En la Api se sustituye por uno que lo deduce del usuario autenticado (H5).
/// </summary>
public sealed class ContextoTenantFijo(Guid? empresaId, string usuario = "sistema") : IContextoTenant
{
    public Guid? EmpresaId { get; } = empresaId;

    public string Usuario { get; } = usuario;
}
