using GestorInve.Core;

namespace GestorInve.Core.Tests;

public class DineroTests
{
    [Fact]
    public void Suma_de_la_misma_moneda_es_exacta_sin_error_de_coma_flotante()
    {
        var resultado = new Dinero(0.1m, "COP") + new Dinero(0.2m, "COP");

        Assert.Equal(new Dinero(0.3m, "COP"), resultado);
    }

    [Fact]
    public void Resta_de_la_misma_moneda()
    {
        var resultado = new Dinero(10m, "COP") - new Dinero(3.5m, "COP");

        Assert.Equal(new Dinero(6.5m, "COP"), resultado);
    }

    [Fact]
    public void Operar_monedas_distintas_lanza_excepcion()
    {
        var cop = new Dinero(1m, "COP");
        var usd = new Dinero(1m, "USD");

        Assert.Throws<InvalidOperationException>(() => cop + usd);
        Assert.Throws<InvalidOperationException>(() => cop - usd);
    }

    [Theory]
    [InlineData("0.125", "0.13")]
    [InlineData("0.124", "0.12")]
    [InlineData("-0.125", "-0.13")]
    public void Redondear_usa_dos_decimales_alejandose_de_cero(string entrada, string esperado)
    {
        var dinero = new Dinero(decimal.Parse(entrada, System.Globalization.CultureInfo.InvariantCulture), "COP");

        Assert.Equal(
            decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture),
            dinero.Redondear().Monto);
    }

    [Fact]
    public void Multiplicar_por_un_factor_conserva_la_moneda()
    {
        var resultado = new Dinero(100m, "COP") * 0.19m;

        Assert.Equal(new Dinero(19m, "COP"), resultado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CO")]
    [InlineData("COPX")]
    [InlineData("c0p")]
    public void Moneda_invalida_lanza_excepcion(string? moneda)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Dinero(1m, moneda!));
    }

    [Fact]
    public void La_moneda_se_normaliza_a_mayusculas()
    {
        Assert.Equal("COP", new Dinero(1m, "cop").Moneda);
    }
}
