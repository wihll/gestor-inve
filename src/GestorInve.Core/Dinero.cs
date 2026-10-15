using System.Globalization;

namespace GestorInve.Core;

/// <summary>Valor monetario exacto (decimal) con su moneda ISO 4217.</summary>
/// <remarks>
/// Como <c>struct</c>, <c>default(Dinero)</c> siempre es alcanzable (slots de arreglo,
/// deserialización, acumuladores) y no pasa por el constructor. Ese valor se trata como
/// «cero sin moneda»: el elemento neutro de la suma, que adopta la moneda del otro operando.
/// Así un acumulador (<c>total += linea</c>) funciona sin exigir una semilla con moneda.
/// </remarks>
public readonly record struct Dinero
{
    private const int DecimalesPorDefecto = 2;

    private readonly string? _moneda;

    public Dinero(decimal monto, string moneda)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moneda);
        if (moneda.Length != 3 || !moneda.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("La moneda debe ser un código ISO 4217 de 3 letras.", nameof(moneda));
        }

        Monto = monto;
        _moneda = moneda.ToUpperInvariant();
    }

    private Dinero(decimal monto, string? monedaYaValidada, bool _)
    {
        Monto = monto;
        _moneda = monedaYaValidada;
    }

    public decimal Monto { get; }

    /// <summary>Código ISO 4217, o cadena vacía si el valor está indefinido (nunca <c>null</c>).</summary>
    public string Moneda => _moneda ?? string.Empty;

    /// <summary>Cierto para <c>default(Dinero)</c>: cero sin moneda asignada.</summary>
    public bool EsIndefinido => _moneda is null;

    public static Dinero operator +(Dinero a, Dinero b) => new(a.Monto + b.Monto, MonedaComun(a, b), true);

    public static Dinero operator -(Dinero a, Dinero b) => new(a.Monto - b.Monto, MonedaComun(a, b), true);

    public static Dinero operator *(Dinero a, decimal factor) => new(a.Monto * factor, a._moneda, true);

    /// <summary>Redondea alejándose de cero (convención contable).</summary>
    public Dinero Redondear(int decimales = DecimalesPorDefecto) =>
        new(Math.Round(Monto, decimales, MidpointRounding.AwayFromZero), _moneda, true);

    public override string ToString() => EsIndefinido
        ? Monto.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{Monto} {Moneda}");

    /// <summary>
    /// Moneda resultante de operar dos valores: un operando indefinido cede ante el otro
    /// (su monto es cero, así que no altera el resultado). Dos monedas reales distintas son un error.
    /// </summary>
    private static string? MonedaComun(Dinero a, Dinero b)
    {
        if (a._moneda is null)
        {
            return b._moneda;
        }

        if (b._moneda is null)
        {
            return a._moneda;
        }

        return a._moneda == b._moneda
            ? a._moneda
            : throw new InvalidOperationException($"No se pueden operar monedas distintas ({a._moneda} y {b._moneda}).");
    }
}
