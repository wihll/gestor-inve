using GestorInve.Catalogo;

namespace GestorInve.Catalogo.Tests;

public class ProductoTests
{
    private static readonly Guid Unidad = Guid.CreateVersion7();

    [Fact]
    public void Un_producto_sin_atributos_nace_con_una_variante_que_hereda_el_SKU()
    {
        var producto = Producto.Crear("mart-001", "Martillo de uña", Unidad);

        var variante = Assert.Single(producto.Variantes);
        Assert.Equal("MART-001", producto.Sku);
        Assert.Equal("MART-001", variante.Sku);
        Assert.Empty(variante.Atributos());
    }

    /// <summary>
    /// Con atributos declarados no se inventa una variante vacía: no existe «una camiseta sin
    /// talla». Las variantes reales las añade quien crea el producto.
    /// </summary>
    [Fact]
    public void Un_producto_con_atributos_nace_sin_variantes()
    {
        var producto = Producto.Crear("cam-polo", "Camiseta Polo", Unidad, ["talla", "color"]);

        Assert.Empty(producto.Variantes);
        Assert.Equal(["talla", "color"], producto.Atributos);
    }

    [Fact]
    public void Agregar_una_variante_con_todos_los_atributos()
    {
        var producto = Producto.Crear("cam-polo", "Camiseta Polo", Unidad, ["talla", "color"]);

        var variante = producto.AgregarVariante("CAM-POLO-M-ROJO", new Dictionary<string, string>
        {
            ["talla"] = "M",
            ["color"] = "rojo",
        });

        Assert.Single(producto.Variantes);
        Assert.Equal("M", variante.Atributos()["talla"]);
        Assert.Equal("rojo", variante.Atributos()["color"]);
    }

    [Fact]
    public void Una_variante_a_la_que_le_falta_un_atributo_se_rechaza()
    {
        var producto = Producto.Crear("cam-polo", "Camiseta Polo", Unidad, ["talla", "color"]);

        var error = Assert.Throws<ArgumentException>(() =>
            producto.AgregarVariante("CAM-POLO-M", new Dictionary<string, string> { ["talla"] = "M" }));

        Assert.Contains("color", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_variante_con_un_atributo_no_declarado_se_rechaza()
    {
        var producto = Producto.Crear("cam-polo", "Camiseta Polo", Unidad, ["talla"]);

        var error = Assert.Throws<ArgumentException>(() =>
            producto.AgregarVariante("CAM-POLO-M", new Dictionary<string, string>
            {
                ["talla"] = "M",
                ["material"] = "algodón",
            }));

        Assert.Contains("material", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dos variantes idénticas harían ambiguo qué existencias se mueven al vender «talla M roja».
    /// </summary>
    [Fact]
    public void No_puede_haber_dos_variantes_con_la_misma_combinacion()
    {
        var producto = Producto.Crear("cam-polo", "Camiseta Polo", Unidad, ["talla", "color"]);
        var combinacion = new Dictionary<string, string> { ["talla"] = "M", ["color"] = "rojo" };

        producto.AgregarVariante("CAM-POLO-M-ROJO", combinacion);

        Assert.Throws<InvalidOperationException>(() =>
            producto.AgregarVariante("CAM-POLO-M-ROJO-BIS", combinacion));
    }

    [Fact]
    public void El_orden_de_los_atributos_no_crea_combinaciones_distintas()
    {
        var producto = Producto.Crear("cam-polo", "Camiseta Polo", Unidad, ["talla", "color"]);

        producto.AgregarVariante("A", new Dictionary<string, string> { ["talla"] = "M", ["color"] = "rojo" });

        Assert.Throws<InvalidOperationException>(() =>
            producto.AgregarVariante("B", new Dictionary<string, string> { ["color"] = "rojo", ["talla"] = "M" }));
    }

    [Fact]
    public void Los_nombres_de_atributo_se_normalizan_a_minusculas()
    {
        var producto = Producto.Crear("cam-polo", "Camiseta Polo", Unidad, [" Talla ", "COLOR"]);

        Assert.Equal(["talla", "color"], producto.Atributos);

        var variante = producto.AgregarVariante("A", new Dictionary<string, string>
        {
            ["TALLA"] = "M",
            ["Color"] = "rojo",
        });

        Assert.Equal("M", variante.Atributos()["talla"]);
    }

    [Fact]
    public void Un_atributo_repetido_no_se_duplica()
    {
        var producto = Producto.Crear("cam-polo", "Camiseta Polo", Unidad, ["talla", "TALLA", "talla"]);

        Assert.Equal(["talla"], producto.Atributos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_SKU_vacio_se_rechaza(string sku)
    {
        Assert.ThrowsAny<ArgumentException>(() => Producto.Crear(sku, "Algo", Unidad));
    }

    [Fact]
    public void Un_producto_sin_unidad_de_medida_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => Producto.Crear("x", "Algo", Guid.Empty));
    }

    [Fact]
    public void La_trazabilidad_por_lote_y_serial_se_declara_en_el_producto()
    {
        var producto = Producto.Crear("med-001", "Acetaminofén", Unidad);

        producto.ExigirTrazabilidad(lote: true, serial: false);

        Assert.True(producto.ExigeLote);
        Assert.False(producto.ExigeSerial);
    }
}
