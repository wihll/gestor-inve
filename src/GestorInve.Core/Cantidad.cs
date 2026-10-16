using System.Globalization;

namespace GestorInve.Core;

/// <summary>
/// Unidad de medida: un código y los decimales que admite.
///
/// Los decimales no son decoración: son lo que impide que existan «0,5 martillos». Cada producto
/// vive en una sola unidad y no hay conversiones (ver ADR 0005), así que aquí no hay factores ni
/// magnitudes; añadirlos más adelante es aditivo y no obliga a migrar el kardex.
/// </summary>
public readonly record struct Unidad
{
    /// <summary>Tope de decimales. Seis cubre gramos, mililitros y milímetros con margen.</summary>
    public const int DecimalesMaximos = 6;

    private readonly string? codigo;

    public Unidad(string codigo, int decimales)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentOutOfRangeException.ThrowIfNegative(decimales);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimales, DecimalesMaximos);

        this.codigo = codigo.Trim().ToUpperInvariant();
        Decimales = decimales;
    }

    /// <summary>
    /// Código corto (UND, KG, LT). Nunca <c>null</c>: <c>default(Unidad)</c> no pasa por el
    /// constructor, así que el campo se expone a través de esta propiedad como cadena vacía.
    /// </summary>
    public string Codigo => codigo ?? string.Empty;

    public int Decimales { get; }

    public override string ToString() => Codigo;
}

/// <summary>
/// Cantidad exacta (decimal) con su unidad.
///
/// Como <see cref="Dinero"/>, es un <c>struct</c>, así que <c>default</c> siempre es alcanzable
/// (slots de arreglo, deserialización, acumuladores) y no pasa por el constructor. Ese valor se
/// trata como «cero sin unidad»: el elemento neutro de la suma, que adopta la unidad del otro
/// operando, para que un acumulador (<c>total += linea</c>) funcione sin semilla.
/// </summary>
public readonly record struct Cantidad
{
    private readonly Unidad? unidad;

    public Cantidad(decimal valor, Unidad unidad)
    {
        if (string.IsNullOrEmpty(unidad.Codigo))
        {
            throw new ArgumentException("La unidad debe tener código.", nameof(unidad));
        }

        if (EscalaSignificativa(valor) > unidad.Decimales)
        {
            throw new ArgumentException(
                $"La unidad {unidad.Codigo} admite {unidad.Decimales} decimales; {valor} tiene más.",
                nameof(valor));
        }

        Valor = valor;
        this.unidad = unidad;
    }

    private Cantidad(decimal valor, Unidad? unidadYaValidada, bool _)
    {
        Valor = valor;
        unidad = unidadYaValidada;
    }

    public decimal Valor { get; }

    /// <summary>Unidad de la cantidad; una unidad sin código si está indefinida.</summary>
    public Unidad Unidad => unidad ?? default;

    /// <summary>Cierto para <c>default(Cantidad)</c>: cero sin unidad asignada.</summary>
    public bool EsIndefinida => unidad is null;

    public bool EsNegativa => Valor < 0m;

    public static Cantidad operator +(Cantidad a, Cantidad b) => Combinar(a, b, a.Valor + b.Valor);

    public static Cantidad operator -(Cantidad a, Cantidad b) => Combinar(a, b, a.Valor - b.Valor);

    /// <summary>
    /// Escala por un factor y **redondea a los decimales de la unidad**, alejándose de cero.
    /// El redondeo es explícito porque un porcentaje sobre piezas enteras no cabe: 7 × 0,33
    /// son 2 unidades, no 2,31. Sin esto, el resto se arrastraría al kardex.
    /// </summary>
    public static Cantidad operator *(Cantidad a, decimal factor) =>
        new(a.Redondear(a.Valor * factor), a.unidad, true);

    /// <summary>Redondea el valor actual a los decimales de su unidad.</summary>
    public Cantidad Normalizar() => new(Redondear(Valor), unidad, true);

    public override string ToString() => EsIndefinida
        ? Valor.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{Valor} {Unidad.Codigo}");

    /// <summary>
    /// Decimales que realmente importan: <c>3.00</c> tiene escala 2 pero representa un entero,
    /// así que los ceros de la derecha no deben invalidar el valor.
    /// </summary>
    private static int EscalaSignificativa(decimal valor)
    {
        var normalizado = valor / 1.000000000000000000000000000000000m;

        return (decimal.GetBits(normalizado)[3] >> 16) & 0xFF;
    }

    private static Cantidad Combinar(Cantidad a, Cantidad b, decimal valor) =>
        new(valor, UnidadComun(a, b), true);

    /// <summary>
    /// Unidad del resultado: un operando indefinido cede ante el otro, porque su valor es cero
    /// y no altera el resultado. Dos unidades reales distintas son un error: sumar kilos con
    /// litros no significa nada y debe fallar antes de llegar a la base de datos.
    /// </summary>
    private static Unidad? UnidadComun(Cantidad a, Cantidad b)
    {
        if (a.unidad is null)
        {
            return b.unidad;
        }

        if (b.unidad is null)
        {
            return a.unidad;
        }

        return a.unidad.Value.Codigo == b.unidad.Value.Codigo
            ? a.unidad
            : throw new InvalidOperationException(
                $"No se pueden operar unidades distintas ({a.unidad.Value.Codigo} y {b.unidad.Value.Codigo}).");
    }

    private decimal Redondear(decimal valor) =>
        unidad is null ? valor : Math.Round(valor, unidad.Value.Decimales, MidpointRounding.AwayFromZero);
}
