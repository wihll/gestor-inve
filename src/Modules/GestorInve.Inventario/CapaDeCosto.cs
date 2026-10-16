using GestorInve.Core;

namespace GestorInve.Inventario;

/// <summary>
/// Capa de costo de una celda del inventario: mercancía que entró a un costo y todavía no ha
/// salido del todo. Es el estado que hace posible PEPS (ADR 0010).
///
/// Solo existe para las empresas que costean por PEPS. En promedio ponderado no se crea ninguna:
/// el promedio no necesita recordar de dónde vino cada unidad, y crear capas que nadie consulta
/// sería ensuciar la base con estado muerto que después hay que mantener cuadrado.
///
/// La aritmética **no** vive aquí, sino en <see cref="SaldoPeps"/>: esta clase solo guarda.
/// </summary>
public class CapaDeCosto : IEntidadDeEmpresa
{
    private CapaDeCosto()
    {
        // EF Core
        UnidadCodigo = string.Empty;
        Moneda = string.Empty;
    }

    private CapaDeCosto(
        Guid varianteId,
        Guid bodegaId,
        Guid ubicacionId,
        Guid? loteId,
        Guid movimientoId,
        Cantidad cantidad,
        Dinero costoUnitario,
        DateTimeOffset fecha)
    {
        Id = Guid.CreateVersion7();
        VarianteId = varianteId;
        BodegaId = bodegaId;
        UbicacionId = ubicacionId;
        LoteId = loteId;
        MovimientoId = movimientoId;
        CantidadInicial = cantidad.Valor;
        CantidadRestante = cantidad.Valor;
        UnidadCodigo = cantidad.Unidad.Codigo;
        UnidadDecimales = cantidad.Unidad.Decimales;
        CostoUnitario = costoUnitario.Monto;
        Moneda = costoUnitario.Moneda;
        Fecha = fecha;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public Guid VarianteId { get; private set; }

    public Guid BodegaId { get; private set; }

    public Guid UbicacionId { get; private set; }

    public Guid? LoteId { get; private set; }

    /// <summary>
    /// Movimiento que creó la capa. No es decorativo: es lo que permite auditar de qué entrada
    /// salió cada peso del costo de ventas, que es la pregunta que hace un revisor fiscal.
    /// </summary>
    public Guid MovimientoId { get; private set; }

    public decimal CantidadInicial { get; private set; }

    /// <summary>Lo que queda por consumir. Cero significa capa agotada, y se conserva: el histórico es la auditoría.</summary>
    public decimal CantidadRestante { get; private set; }

    public string UnidadCodigo { get; private set; }

    public int UnidadDecimales { get; private set; }

    public decimal CostoUnitario { get; private set; }

    public string Moneda { get; private set; }

    /// <summary>Fecha del movimiento que la originó. Es el criterio de antigüedad de PEPS.</summary>
    public DateTimeOffset Fecha { get; private set; }

    internal static CapaDeCosto Nueva(
        Guid varianteId,
        Guid bodegaId,
        Guid ubicacionId,
        Guid? loteId,
        Guid movimientoId,
        Cantidad cantidad,
        Dinero costoUnitario,
        DateTimeOffset fecha) =>
        new(varianteId, bodegaId, ubicacionId, loteId, movimientoId, cantidad, costoUnitario, fecha);

    public CapaCosto ComoCapa() => new(
        new Cantidad(CantidadRestante, new Unidad(UnidadCodigo, UnidadDecimales)),
        new Dinero(CostoUnitario, Moneda));

    internal void Consumir(decimal cantidad)
    {
        if (cantidad < 0m || cantidad > CantidadRestante)
        {
            throw new InvalidOperationException(
                $"No se puede consumir {cantidad} de una capa con {CantidadRestante} restantes.");
        }

        CantidadRestante -= cantidad;
    }

    /// <summary>Reinicio de capas con saldo ≤ 0 (ADR 0010): la capa queda agotada, no se borra.</summary>
    internal void Agotar() => CantidadRestante = 0m;
}
