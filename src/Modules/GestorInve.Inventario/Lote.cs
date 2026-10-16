using GestorInve.Core;

namespace GestorInve.Inventario;

/// <summary>
/// Lote de una variante. Un **serial** es un lote con <see cref="EsSerial"/> en cierto y
/// existencia que solo puede ser 0 o 1: el número de serie de una impresora se rastrea igual que
/// el número de lote de un medicamento, y la diferencia está en la regla de cantidad, no en la
/// estructura. Ver ADR 0009.
///
/// Pertenece a la **variante**, no a la bodega: el mismo lote puede estar repartido en varias
/// celdas, y su vencimiento es el mismo en todas.
/// </summary>
public class Lote : IEntidadDeEmpresa, IAuditable
{
    private Lote()
    {
        // EF Core
        Codigo = string.Empty;
        CreadoPor = string.Empty;
    }

    private Lote(Guid varianteId, string codigo, DateOnly? vencimiento, bool esSerial)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            throw new ArgumentException("El lote necesita código.", nameof(codigo));
        }

        Id = Guid.CreateVersion7();
        VarianteId = varianteId;
        Codigo = codigo.Trim();
        Vencimiento = vencimiento;
        EsSerial = esSerial;
        CreadoPor = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public Guid VarianteId { get; private set; }

    /// <summary>Código del lote o número de serie. Único dentro de su variante.</summary>
    public string Codigo { get; private set; }

    /// <summary>
    /// Fecha de vencimiento, si el artículo la tiene. Es <c>DateOnly</c> y no un instante a
    /// propósito: un lote vence un día, no a una hora, y guardarlo con hora y zona haría que el
    /// mismo lote estuviera vencido o no según dónde se consulte.
    /// </summary>
    public DateOnly? Vencimiento { get; private set; }

    public bool EsSerial { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public string CreadoPor { get; private set; }

    public DateTimeOffset? ActualizadoEn { get; private set; }

    public string? ActualizadoPor { get; private set; }

    public static Lote Nuevo(Guid varianteId, string codigo, DateOnly? vencimiento, bool esSerial = false) =>
        new(varianteId, codigo, vencimiento, esSerial);

    /// <summary>
    /// Vencido **al día indicado**. La fecha se pasa en vez de leer el reloj aquí para que la
    /// regla sea comprobable y para que un movimiento con fecha de negocio anterior se juzgue
    /// con la fecha que le corresponde, no con la de hoy.
    /// </summary>
    public bool EstaVencido(DateOnly dia) => Vencimiento is { } fecha && fecha < dia;
}
