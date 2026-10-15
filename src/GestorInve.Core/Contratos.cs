namespace GestorInve.Core;

/// <summary>Reloj inyectable: el dominio nunca lee <see cref="DateTimeOffset.UtcNow"/> directamente.</summary>
public interface IReloj
{
    DateTimeOffset Ahora { get; }
}

/// <summary>Empresa (tenant) activa en la operación en curso.</summary>
public interface IContextoTenant
{
    /// <summary>Empresa actual, o <c>null</c> cuando la operación no está asociada a ninguna.</summary>
    Guid? EmpresaId { get; }

    /// <summary>Usuario que ejecuta la operación, para la auditoría.</summary>
    string Usuario { get; }
}

/// <summary>Entidad que pertenece a una empresa y queda sujeta al aislamiento multiempresa.</summary>
public interface IEntidadDeEmpresa
{
    Guid EmpresaId { get; }
}

/// <summary>Entidad cuyo rastro de creación y modificación se registra automáticamente.</summary>
public interface IAuditable
{
    DateTimeOffset CreadoEn { get; }

    string CreadoPor { get; }

    DateTimeOffset? ActualizadoEn { get; }

    string? ActualizadoPor { get; }
}
