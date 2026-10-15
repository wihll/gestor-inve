using System.Globalization;

namespace GestorInve.Core;

/// <summary>Valor monetario exacto (decimal) con su moneda ISO 4217.</summary>
public readonly record struct Dinero
{
    private const int DecimalesPorDefecto = 2;

    public decimal Monto { get; }

    public string Moneda { get; }

    public Dinero(decimal monto, string moneda)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moneda);
        if (moneda.Length != 3 || !moneda.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("La moneda debe ser un código ISO 4217 de 3 letras.", nameof(moneda));
        }

        Monto = monto;
        Moneda = moneda.ToUpperInvariant();
    }

    public static Dinero operator +(Dinero a, Dinero b) => new(a.Monto + b.Monto, MismaMoneda(a, b));

    public static Dinero operator -(Dinero a, Dinero b) => new(a.Monto - b.Monto, MismaMoneda(a, b));

    public static Dinero operator *(Dinero a, decimal factor) => new(a.Monto * factor, a.Moneda);

    /// <summary>Redondea alejándose de cero (convención contable).</summary>
    public Dinero Redondear(int decimales = DecimalesPorDefecto) =>
        new(Math.Round(Monto, decimales, MidpointRounding.AwayFromZero), Moneda);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Monto} {Moneda}");

    private static string MismaMoneda(Dinero a, Dinero b) =>
        a.Moneda == b.Moneda
            ? a.Moneda
            : throw new InvalidOperationException($"No se pueden operar monedas distintas ({a.Moneda} y {b.Moneda}).");
}
