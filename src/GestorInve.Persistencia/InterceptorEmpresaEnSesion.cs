using System.Data.Common;
using GestorInve.Core;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestorInve.Persistencia;

/// <summary>
/// Publica la empresa actual en la sesión de PostgreSQL (<c>gestorinve.empresa_id</c>), que es
/// lo que leen las políticas RLS. Se ejecuta en cada apertura de conexión porque el pool
/// reutiliza conexiones entre peticiones y, por tanto, entre empresas.
/// </summary>
public sealed class InterceptorEmpresaEnSesion(IContextoTenant tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Publicar(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await PublicarAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private static DbCommand Comando(DbConnection conexion, Guid? empresaId)
    {
        var comando = conexion.CreateCommand();

        // set_config con is_local=false: el valor vive en la conexión, no en una transacción.
        // Cadena vacía (y no NULL) para que el cast a uuid de la política falle en cerrado.
        comando.CommandText = $"select set_config('{GestorInveDbContext.AjusteEmpresa}', @empresa, false)";

        var parametro = comando.CreateParameter();
        parametro.ParameterName = "@empresa";
        parametro.Value = empresaId?.ToString() ?? string.Empty;
        comando.Parameters.Add(parametro);

        return comando;
    }

    private void Publicar(DbConnection conexion)
    {
        using var comando = Comando(conexion, tenant.EmpresaId);
        comando.ExecuteNonQuery();
    }

    private async Task PublicarAsync(DbConnection conexion, CancellationToken cancellationToken)
    {
        await using var comando = Comando(conexion, tenant.EmpresaId);
        await comando.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
