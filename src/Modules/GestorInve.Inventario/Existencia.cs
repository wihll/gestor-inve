using GestorInve.Core;

namespace GestorInve.Inventario;

/// <summary>
/// Saldo de una celda del inventario: (variante, bodega, ubicación, lote). Es una **proyección** del
/// kardex, no la verdad: la verdad es el libro de movimientos, y esta fila existe para poder
/// consultar y bloquear el saldo sin recorrer el histórico.
///
/// Toda operación la bloquea con <c>SELECT … FOR UPDATE</c> antes de escribir: dos salidas
/// simultáneas de la misma celda no pueden leer el mismo saldo.
/// </summary>
public class Existencia : IEntidadDeEmpresa
{
    private Existencia()
    {
        // EF Core
        UnidadCodigo = string.Empty;
        Moneda = string.Empty;
    }

    private Existencia(Guid varianteId, Guid bodegaId, Guid ubicacionId, Guid? loteId, Unidad unidad, string moneda)
    {
        Id = Guid.CreateVersion7();
        VarianteId = varianteId;
        BodegaId = bodegaId;
        UbicacionId = ubicacionId;
        LoteId = loteId;
        UnidadCodigo = unidad.Codigo;
        UnidadDecimales = unidad.Decimales;
        Moneda = moneda;
        Cantidad = 0m;
        CostoPromedio = 0m;
    }

    public Guid Id { get; private set; }

    /// <summary>La sella el interceptor de persistencia con la empresa del contexto.</summary>
    public Guid EmpresaId { get; private set; }

    public Guid VarianteId { get; private set; }

    public Guid BodegaId { get; private set; }

    public Guid UbicacionId { get; private set; }

    /// <summary>
    /// Lote de esta celda, o nulo en un producto sin trazabilidad —que es la mayoría—. No se usa
    /// un lote sintético «sin lote» porque obligaría a crear una fila de catálogo por cada
    /// variante para no decir nada. Ver ADR 0009.
    /// </summary>
    public Guid? LoteId { get; private set; }

    public decimal Cantidad { get; private set; }

    public decimal CostoPromedio { get; private set; }

    public string UnidadCodigo { get; private set; }

    public int UnidadDecimales { get; private set; }

    public string Moneda { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    internal static Existencia Nueva(
        Guid varianteId,
        Guid bodegaId,
        Guid ubicacionId,
        Guid? loteId,
        Unidad unidad,
        string moneda) =>
        new(varianteId, bodegaId, ubicacionId, loteId, unidad, moneda);

    /// <summary>Saldo valorizado actual, para pasárselo a la aritmética del costeo.</summary>
    public SaldoValorizado Saldo() => SaldoValorizado.De(
        new Cantidad(Cantidad, new Unidad(UnidadCodigo, UnidadDecimales)),
        new Dinero(CostoPromedio, Moneda));

    internal void Aplicar(SaldoValorizado saldo, DateTimeOffset ahora)
    {
        Cantidad = saldo.Cantidad.Valor;
        CostoPromedio = saldo.CostoPromedio.Monto;
        ActualizadoEn = ahora;
    }
}
