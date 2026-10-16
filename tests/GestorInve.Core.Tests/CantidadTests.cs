using GestorInve.Core;

namespace GestorInve.Core.Tests;

public class CantidadTests
{
    private static readonly Unidad Unidades = new("UND", 0);
    private static readonly Unidad Kilos = new("KG", 3);
    private static readonly Unidad Litros = new("LT", 3);

    [Fact]
    public void Suma_de_la_misma_unidad_es_exacta()
    {
        var resultado = new Cantidad(0.1m, Kilos) + new Cantidad(0.2m, Kilos);

        Assert.Equal(new Cantidad(0.3m, Kilos), resultado);
    }

    [Fact]
    public void Resta_de_la_misma_unidad()
    {
        Assert.Equal(new Cantidad(6.5m, Kilos), new Cantidad(10m, Kilos) - new Cantidad(3.5m, Kilos));
    }

    [Fact]
    public void Operar_unidades_distintas_lanza_excepcion()
    {
        var kilos = new Cantidad(1m, Kilos);
        var litros = new Cantidad(1m, Litros);

        Assert.Throws<InvalidOperationException>(() => kilos + litros);
        Assert.Throws<InvalidOperationException>(() => kilos - litros);
    }

    /// <summary>
    /// La razón de ser de los decimales por unidad: medio martillo no existe. Si esto se
    /// permitiera, el kardex acabaría con existencias imposibles de contar en una bodega.
    /// </summary>
    [Fact]
    public void Una_unidad_sin_decimales_rechaza_valores_fraccionados()
    {
        Assert.Throws<ArgumentException>(() => new Cantidad(0.5m, Unidades));
        Assert.Throws<ArgumentException>(() => new Cantidad(3.01m, Unidades));
    }

    [Fact]
    public void Una_unidad_con_decimales_los_admite_hasta_su_limite()
    {
        Assert.Equal(1.234m, new Cantidad(1.234m, Kilos).Valor);
        Assert.Throws<ArgumentException>(() => new Cantidad(1.2345m, Kilos));
    }

    [Fact]
    public void Los_ceros_de_mas_no_cuentan_como_decimales_significativos()
    {
        // 3,00 UND es 3 unidades; la escala sobrante no debería invalidar el valor.
        Assert.Equal(3m, new Cantidad(3.00m, Unidades).Valor);
    }

    [Fact]
    public void Default_es_elemento_neutro_y_adopta_la_unidad_del_otro_operando()
    {
        Cantidad total = default;

        total += new Cantidad(2m, Kilos);
        total += new Cantidad(1.5m, Kilos);

        Assert.Equal(new Cantidad(3.5m, Kilos), total);
    }

    [Fact]
    public void Sumar_una_lista_desde_default_no_lanza()
    {
        Cantidad[] lineas = [new(1m, Unidades), new(2m, Unidades), new(3m, Unidades)];

        var total = lineas.Aggregate(default(Cantidad), (acc, linea) => acc + linea);

        Assert.Equal(new Cantidad(6m, Unidades), total);
    }

    [Fact]
    public void Default_esta_indefinida_y_su_unidad_no_es_nula()
    {
        Cantidad sinInicializar = default;

        Assert.True(sinInicializar.EsIndefinida);
        Assert.Equal(0m, sinInicializar.Valor);
        Assert.Equal(string.Empty, sinInicializar.Unidad.Codigo);
        Assert.Equal("0", sinInicializar.ToString());
    }

    [Fact]
    public void Multiplicar_por_un_factor_redondea_a_los_decimales_de_la_unidad()
    {
        // 7 unidades al 33 % serían 2,31: en piezas enteras son 2.
        Assert.Equal(new Cantidad(2m, Unidades), new Cantidad(7m, Unidades) * 0.33m);

        // En kilos el resultado cabe con tres decimales y no se pierde.
        Assert.Equal(new Cantidad(2.31m, Kilos), new Cantidad(7m, Kilos) * 0.33m);
    }

    [Fact]
    public void Multiplicar_un_default_no_lanza()
    {
        Assert.True((default(Cantidad) * 5m).EsIndefinida);
    }

    [Fact]
    public void Es_negativa_distingue_salidas_de_entradas()
    {
        Assert.True(new Cantidad(-1m, Unidades).EsNegativa);
        Assert.False(new Cantidad(0m, Unidades).EsNegativa);
        Assert.False(default(Cantidad).EsNegativa);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Una_unidad_sin_codigo_es_invalida(string? codigo)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Unidad(codigo!, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void Los_decimales_de_la_unidad_deben_ser_razonables(int decimales)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Unidad("KG", decimales));
    }

    [Fact]
    public void El_codigo_de_la_unidad_se_normaliza_a_mayusculas()
    {
        Assert.Equal("KG", new Unidad(" kg ", 3).Codigo);
    }

    [Fact]
    public void ToString_muestra_valor_y_unidad()
    {
        Assert.Equal("2.5 KG", new Cantidad(2.5m, Kilos).ToString());
    }

    /// <summary>
    /// Una unidad sin inicializar no identifica nada: aceptarla dejaría cantidades «de algo»
    /// imposibles de sumar o de mostrar. Solo <c>default(Cantidad)</c>, que vale cero, puede
    /// quedarse sin unidad.
    /// </summary>
    [Fact]
    public void No_se_puede_construir_una_cantidad_con_una_unidad_sin_inicializar()
    {
        var error = Assert.Throws<ArgumentException>(() => new Cantidad(5m, default));

        Assert.Contains("código", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void La_unidad_se_imprime_con_su_codigo()
    {
        Assert.Equal("KG", Kilos.ToString());
        Assert.Equal(string.Empty, default(Unidad).ToString());
    }
}
