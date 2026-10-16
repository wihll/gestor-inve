using System.Collections.Immutable;

namespace GestorInve.Core;

/// <summary>Método con el que se valora la salida de mercancía. Ver ADR 0010.</summary>
public enum MetodoCosteo
{
    /// <summary>Promedio móvil ponderado: el que una pyme entiende sin formación contable.</summary>
    PromedioPonderado,

    /// <summary>Primeras entradas, primeras salidas, con capas de costo.</summary>
    Peps,
}

/// <summary>
/// Una capa de costo: mercancía que entró a un costo y todavía no ha salido del todo. Es el
/// estado que hace posible PEPS, y no representa nada más: no hay capas negativas, porque una
/// capa negativa no corresponde a ninguna mercancía.
/// </summary>
/// <param name="Cantidad">Lo que queda por consumir de esta capa.</param>
/// <param name="CostoUnitario">Costo al que entró, que ya no cambia.</param>
public readonly record struct CapaCosto(Cantidad Cantidad, Dinero CostoUnitario)
{
    public Dinero Valorizada() => CostoUnitario * Cantidad.Valor;
}

/// <summary>
/// Saldo valorizado por PEPS: la lista **ordenada** de capas vivas, de la más antigua a la más
/// nueva. Inmutable y sin base de datos, como <see cref="SaldoValorizado"/>, para poder
/// comprobar la aritmética con propiedades y no solo con ejemplos.
///
/// Dos reglas que no son obvias y que se prueban:
/// - **Una salida consume capas de la más antigua a la más nueva**, y su costo es el ponderado
///   de lo consumido. Es toda la diferencia con el promedio, y es la que cambia el impuesto.
/// - **Con saldo ≤ 0, una entrada reinicia las capas.** Arrastrar el estado anterior a través de
///   un saldo negativo produce valorizaciones absurdas que nadie detecta, igual que en el
///   promedio ponderado y por el mismo motivo.
/// </summary>
public readonly record struct SaldoPeps
{
    private readonly ImmutableList<CapaCosto>? capas;

    private SaldoPeps(ImmutableList<CapaCosto> capas, Cantidad cantidad, string moneda)
    {
        this.capas = capas;
        Cantidad = cantidad;
        Moneda = moneda;
    }

    /// <summary>Capas vivas, de la más antigua a la más nueva.</summary>
    public IReadOnlyList<CapaCosto> Capas => capas ?? ImmutableList<CapaCosto>.Empty;

    /// <summary>
    /// Cantidad del saldo. **Puede ser negativa** tras una salida forzada, y entonces no hay
    /// capas: el saldo negativo es una deuda de mercancía, no mercancía con costo.
    /// </summary>
    public Cantidad Cantidad { get; }

    public string Moneda { get; }

    public static SaldoPeps Vacio(Unidad unidad, string moneda) =>
        new(ImmutableList<CapaCosto>.Empty, new Cantidad(0m, unidad), moneda);

    /// <summary>
    /// Reconstruye el saldo a partir de las capas guardadas. <paramref name="cantidadRegistrada"/>
    /// es la de la fila de existencias: se pasa aparte en vez de deducirla de las capas porque un
    /// saldo negativo no tiene capas que lo expliquen, y porque si las dos cosas se separasen hay
    /// que poder verlo (<see cref="Cuadra"/>).
    /// </summary>
    public static SaldoPeps De(IEnumerable<CapaCosto> capas, Cantidad cantidadRegistrada, string moneda)
    {
        ArgumentNullException.ThrowIfNull(capas);

        return new SaldoPeps([.. capas], cantidadRegistrada, moneda);
    }

    /// <summary>
    /// La suma de las capas coincide con la cantidad registrada. Falso significa que las capas y
    /// la existencia se separaron, que es el fallo silencioso que hay que poder detectar.
    /// Un saldo negativo cuadra solo si no quedan capas.
    /// </summary>
    public bool Cuadra() =>
        Cantidad.Valor <= 0m
            ? Capas.Count == 0
            : Capas.Sum(c => c.Cantidad.Valor) == Cantidad.Valor;

    /// <summary>Valor en libros: la suma de las capas vivas.</summary>
    public Dinero Valorizado() =>
        new Dinero(Capas.Sum(c => c.CostoUnitario.Monto * c.Cantidad.Valor), Moneda).Redondear();

    /// <summary>
    /// Costo implícito por unidad: valor en libros entre cantidad. Es lo que se guarda en la fila
    /// de existencias, donde bajo PEPS la columna `costo_promedio` significa esto.
    /// Con saldo no positivo se conserva el costo de la capa más reciente, o cero si no hay.
    /// </summary>
    public Dinero CostoImplicito()
    {
        if (Cantidad.Valor <= 0m || Capas.Count == 0)
        {
            return Capas.Count == 0
                ? new Dinero(0m, Moneda)
                : Capas[^1].CostoUnitario;
        }

        var valor = Capas.Sum(c => c.CostoUnitario.Monto * c.Cantidad.Valor);

        return new Dinero(valor / Cantidad.Valor, Moneda).Redondear(SaldoValorizado.DecimalesCosto);
    }

    public SaldoPeps AplicarEntrada(Cantidad entrada, Dinero costoUnitario)
    {
        if (entrada.Valor <= 0m)
        {
            throw new ArgumentException("La cantidad del movimiento debe ser positiva.", nameof(entrada));
        }

        if (costoUnitario.Monto < 0m)
        {
            throw new ArgumentException("El costo unitario no puede ser negativo.", nameof(costoUnitario));
        }

        var nueva = new CapaCosto(entrada, costoUnitario);

        // Saldo no positivo: las capas se reinician. La entrada no «paga» una deuda de mercancía
        // a su propio costo; el saldo negativo ya quedó asentado en el libro con el costo que le
        // tocó, y lo que entra ahora empieza de cero. Misma regla que el promedio ponderado.
        return Cantidad.Valor <= 0m
            ? new SaldoPeps([nueva], entrada, Moneda)
            : new SaldoPeps((capas ?? ImmutableList<CapaCosto>.Empty).Add(nueva), Cantidad + entrada, Moneda);
    }

    /// <summary>
    /// Consume <paramref name="salida"/> de las capas más antiguas y devuelve el saldo restante
    /// junto con el **costo total** de lo que salió.
    ///
    /// Si no alcanza, se consume todo y el faltante se valora al costo de la última capa tocada
    /// —o al costo implícito si no había capas—. Eso solo ocurre en una salida forzada, que exige
    /// el permiso de ajuste y queda marcada en el libro.
    /// </summary>
    public (SaldoPeps Restante, Dinero CostoTotal) ConsumirSalida(Cantidad salida)
    {
        if (salida.Valor <= 0m)
        {
            throw new ArgumentException("La cantidad del movimiento debe ser positiva.", nameof(salida));
        }

        var restantes = new List<CapaCosto>(Capas);
        var porConsumir = salida.Valor;
        var costo = 0m;
        var ultimoCosto = CostoImplicito().Monto;

        while (porConsumir > 0m && restantes.Count > 0)
        {
            var capa = restantes[0];
            var tomar = Math.Min(porConsumir, capa.Cantidad.Valor);

            costo += tomar * capa.CostoUnitario.Monto;
            ultimoCosto = capa.CostoUnitario.Monto;
            porConsumir -= tomar;

            if (tomar == capa.Cantidad.Valor)
            {
                restantes.RemoveAt(0);
            }
            else
            {
                restantes[0] = capa with
                {
                    Cantidad = new Cantidad(capa.Cantidad.Valor - tomar, capa.Cantidad.Unidad),
                };
            }
        }

        // Lo que no había en capas se valora al último costo conocido. No se crea una capa
        // negativa: no representaría ninguna mercancía.
        costo += porConsumir * ultimoCosto;

        var cantidadRestante = Cantidad - salida;
        var saldo = cantidadRestante.Valor <= 0m
            ? new SaldoPeps(ImmutableList<CapaCosto>.Empty, cantidadRestante, Moneda)
            : new SaldoPeps([.. restantes], cantidadRestante, Moneda);

        return (saldo, new Dinero(costo, Moneda).Redondear());
    }
}
