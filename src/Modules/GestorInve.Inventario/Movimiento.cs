using GestorInve.Core;

namespace GestorInve.Inventario;

/// <summary>Tipo de movimiento. El signo sobre las existencias lo decide <see cref="TipoMovimientoExtensiones.Signo"/>.</summary>
public enum TipoMovimiento
{
    Entrada,
    Salida,
    TrasladoSalida,
    TrasladoEntrada,
    AjustePositivo,
    AjusteNegativo,
}

public static class TipoMovimientoExtensiones
{
    /// <summary>+1 si suma existencias, -1 si las resta. Única definición de los signos.</summary>
    public static int Signo(this TipoMovimiento tipo) => tipo switch
    {
        TipoMovimiento.Entrada or TipoMovimiento.TrasladoEntrada or TipoMovimiento.AjustePositivo => 1,
        TipoMovimiento.Salida or TipoMovimiento.TrasladoSalida or TipoMovimiento.AjusteNegativo => -1,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de movimiento desconocido."),
    };

    public static bool Suma(this TipoMovimiento tipo) => tipo.Signo() > 0;

    /// <summary>Movimientos que se registran de uno en uno; los traslados van en pareja.</summary>
    public static bool EsSimple(this TipoMovimiento tipo) => tipo
        is TipoMovimiento.Entrada
        or TipoMovimiento.Salida
        or TipoMovimiento.AjustePositivo
        or TipoMovimiento.AjusteNegativo;

    /// <summary>Un ajuste corrige el inventario sin documento que lo respalde: permiso más fuerte.</summary>
    public static bool EsAjuste(this TipoMovimiento tipo) => tipo
        is TipoMovimiento.AjustePositivo or TipoMovimiento.AjusteNegativo;

    /// <summary>Tipo que deshace a otro, para las reversas.</summary>
    public static TipoMovimiento Opuesto(this TipoMovimiento tipo) => tipo switch
    {
        TipoMovimiento.Entrada => TipoMovimiento.Salida,
        TipoMovimiento.Salida => TipoMovimiento.Entrada,
        TipoMovimiento.TrasladoEntrada => TipoMovimiento.TrasladoSalida,
        TipoMovimiento.TrasladoSalida => TipoMovimiento.TrasladoEntrada,
        TipoMovimiento.AjustePositivo => TipoMovimiento.AjusteNegativo,
        TipoMovimiento.AjusteNegativo => TipoMovimiento.AjustePositivo,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de movimiento desconocido."),
    };
}

/// <summary>
/// Asiento del kardex. **Inmutable**: nada lo modifica después de escrito. Un error se corrige
/// con una reversa, que es otro movimiento apuntando a este (`MovimientoReversadoId`, con índice
/// único, así cada asiento se reversa a lo sumo una vez).
///
/// Guarda su **saldo resultante** (`SaldoCantidadDespues`, `SaldoCostoPromedioDespues`) para que
/// el kardex se imprima y se audite leyendo el libro, sin recalcular desde el origen, y para
/// poder detectar un descuadre comparando el acumulado con la fila de existencias.
///
/// Guarda también la **unidad y la moneda** del momento: si mañana cambia la unidad del
/// producto, los movimientos históricos tienen que seguir significando lo mismo.
/// </summary>
public class Movimiento : IEntidadDeEmpresa
{
    private Movimiento()
    {
        // EF Core
        UnidadCodigo = string.Empty;
        Moneda = string.Empty;
        Usuario = string.Empty;
    }

    private Movimiento(
        TipoMovimiento tipo,
        Guid varianteId,
        Guid bodegaId,
        Guid ubicacionId,
        Guid? loteId,
        Cantidad cantidad,
        Dinero costoUnitario,
        SaldoValorizado saldoDespues,
        DateTimeOffset fecha,
        string usuario,
        string? referencia,
        string? observacion,
        Dinero? costoTotal)
    {
        Id = Guid.CreateVersion7();
        Tipo = tipo;
        VarianteId = varianteId;
        BodegaId = bodegaId;
        UbicacionId = ubicacionId;
        LoteId = loteId;

        Cantidad = cantidad.Valor;
        UnidadCodigo = cantidad.Unidad.Codigo;
        UnidadDecimales = cantidad.Unidad.Decimales;

        CostoUnitario = costoUnitario.Monto;
        Moneda = costoUnitario.Moneda;

        // El total se puede imponer, y bajo PEPS hay que hacerlo: el costo de una salida es la
        // suma exacta de las capas consumidas (10×1.000 + 20×2.000 = 50.000), mientras que el
        // unitario es el ponderado que se deriva de él (1.666,666667) y no lo reproduce al
        // multiplicar. Lo que tiene que cuadrar con la contabilidad es el total, no el unitario.
        CostoTotal = (costoTotal ?? (costoUnitario * cantidad.Valor)).Redondear().Monto;

        SaldoCantidadDespues = saldoDespues.Cantidad.Valor;
        SaldoCostoPromedioDespues = saldoDespues.CostoPromedio.Monto;

        Fecha = fecha;
        Usuario = usuario;
        Referencia = referencia?.Trim();
        Observacion = observacion?.Trim();
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public TipoMovimiento Tipo { get; private set; }

    /// <summary>La variante es la unidad de inventario (ADR 0005); nunca el producto.</summary>
    public Guid VarianteId { get; private set; }

    public Guid BodegaId { get; private set; }

    public Guid UbicacionId { get; private set; }

    /// <summary>Lote del asiento, o nulo si el producto no exige trazabilidad (ADR 0009).</summary>
    public Guid? LoteId { get; private set; }

    /// <summary>Cantidad **siempre positiva**; el sentido lo da <see cref="Tipo"/>.</summary>
    public decimal Cantidad { get; private set; }

    public string UnidadCodigo { get; private set; }

    public int UnidadDecimales { get; private set; }

    public decimal CostoUnitario { get; private set; }

    public decimal CostoTotal { get; private set; }

    public string Moneda { get; private set; }

    public decimal SaldoCantidadDespues { get; private set; }

    public decimal SaldoCostoPromedioDespues { get; private set; }

    /// <summary>Fecha del movimiento según el negocio, que puede no ser la de registro.</summary>
    public DateTimeOffset Fecha { get; private set; }

    public string Usuario { get; private set; }

    /// <summary>Documento que lo origina (factura, remisión, orden).</summary>
    public string? Referencia { get; private set; }

    public string? Observacion { get; private set; }

    /// <summary>Agrupa los dos movimientos de un traslado como una sola operación.</summary>
    public Guid? TrasladoId { get; private set; }

    /// <summary>
    /// Agrupa los asientos que una **sola petición** generó. Aparece cuando una salida FEFO
    /// consume varios lotes: el libro necesita un asiento por lote para poder reconstruir el
    /// saldo de cada uno, y este identificador deja que la pantalla y los reportes lo presenten
    /// como la operación única que fue. Ver ADR 0009.
    /// </summary>
    public Guid? OperacionId { get; private set; }

    /// <summary>Movimiento que este reversa. Índice único: cada asiento se reversa una sola vez.</summary>
    public Guid? MovimientoReversadoId { get; private set; }

    /// <summary>
    /// Cierto si esta salida se permitió dejando el saldo negativo, con el permiso de ajuste.
    /// Queda marcado para que el descuadre sea visible y no silencioso.
    /// </summary>
    public bool ForzadoNegativo { get; private set; }

    /// <summary>Cantidad con su signo, para cuadrar el libro contra las existencias.</summary>
    public decimal CantidadFirmada => Cantidad * Tipo.Signo();

    public Cantidad ComoCantidad() => new(Cantidad, new Unidad(UnidadCodigo, UnidadDecimales));

    public Dinero ComoCostoUnitario() => new(CostoUnitario, Moneda);

    internal static Movimiento Registrar(
        TipoMovimiento tipo,
        Guid varianteId,
        Guid bodegaId,
        Guid ubicacionId,
        Guid? loteId,
        Cantidad cantidad,
        Dinero costoUnitario,
        SaldoValorizado saldoDespues,
        DateTimeOffset fecha,
        string usuario,
        string? referencia = null,
        string? observacion = null,
        Guid? trasladoId = null,
        Guid? movimientoReversadoId = null,
        bool forzadoNegativo = false,
        Guid? operacionId = null,
        Dinero? costoTotal = null) =>
        new(tipo, varianteId, bodegaId, ubicacionId, loteId, cantidad, costoUnitario, saldoDespues, fecha, usuario, referencia, observacion, costoTotal)
        {
            TrasladoId = trasladoId,
            MovimientoReversadoId = movimientoReversadoId,
            ForzadoNegativo = forzadoNegativo,
            OperacionId = operacionId,
        };
}
