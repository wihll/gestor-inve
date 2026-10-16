using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorInve.Api.Autenticacion;
using GestorInve.Api.Catalogo;
using GestorInve.Api.Inventario;
using GestorInve.Core;
using GestorInve.Inventario;
using Npgsql;

namespace GestorInve.Api.Tests;

/// <summary>
/// Costeo PEPS contra la Api y PostgreSQL reales (ADR 0010). Lo que se prueba no es que los
/// endpoints respondan, sino las dos cosas que pueden arruinar una contabilidad sin avisar: que
/// el costo de salida sea el de las capas antiguas y no un promedio, y que **las capas y la
/// existencia no se separen**, porque si se separan la valorización miente y nada lo detecta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
[Trait("Categoria", "Integracion")]
public class CosteoPepsTests(ApiFixture api)
{
    private const string Clave = "Clave-Muy-Larga-2026";

    [Fact]
    public async Task El_metodo_por_omision_es_promedio_ponderado()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();

        var configuracion = await cliente.GetFromJsonAsync<RespuestaConfiguracion>(
            "/api/inventario/configuracion",
            ApiFixture.Json);

        Assert.Equal(MetodoCosteo.PromedioPonderado, configuracion!.Metodo);
        Assert.True(configuracion.SePuedeCambiar);
    }

    [Fact]
    public async Task Una_salida_en_PEPS_cuesta_lo_que_costaron_las_unidades_mas_antiguas()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(cliente);
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 20m, 2000m);

        var salida = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 30m, null);

        // 10 × 1.000 + 20 × 2.000 = 50.000 exactos. El promedio habría cobrado 30 × 1.666,67.
        // El total es lo que tiene que cuadrar con la contabilidad; el unitario se deriva de él.
        Assert.Equal(50_000m, salida.CostoTotal);
        Assert.Equal(1666.666667m, salida.CostoUnitario);
        Assert.Equal(0m, salida.SaldoCantidadDespues);
    }

    [Fact]
    public async Task La_misma_historia_cuesta_distinto_en_PEPS_que_en_promedio()
    {
        var pepsCliente = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(pepsCliente);
        var pepsEscenario = await EscenarioAsync(pepsCliente);

        var promedioCliente = await ClienteDeEmpresaNuevaAsync();
        var promedioEscenario = await EscenarioAsync(promedioCliente);

        foreach (var (cliente, escenario) in new[]
                 {
                     (pepsCliente, pepsEscenario), (promedioCliente, promedioEscenario),
                 })
        {
            await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
            await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 3000m);
        }

        var salidaPeps = await MoverAsync(pepsCliente, pepsEscenario, TipoMovimiento.Salida, 10m, null);
        var salidaPromedio = await MoverAsync(promedioCliente, promedioEscenario, TipoMovimiento.Salida, 10m, null);

        // PEPS saca las 10 baratas y deja 30.000 en inventario; el promedio saca a 2.000 y deja
        // 20.000. Esa diferencia es la razón de ofrecer los dos métodos: cambia el impuesto.
        Assert.Equal(10_000m, salidaPeps.CostoTotal);
        Assert.Equal(20_000m, salidaPromedio.CostoTotal);

        Assert.Equal(30_000m, (await ExistenciaAsync(pepsCliente, pepsEscenario)).Valorizado);
        Assert.Equal(20_000m, (await ExistenciaAsync(promedioCliente, promedioEscenario)).Valorizado);
    }

    /// <summary>
    /// La invariante que amarra las capas al saldo, comprobada **en la base** y no en memoria: si
    /// la persistencia de las capas se desincronizara de la existencia, la valorización mentiría
    /// y ninguna pantalla lo notaría.
    /// </summary>
    [Fact]
    public async Task Las_capas_guardadas_cuadran_con_la_existencia()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(cliente);
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 7m, 2500m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 12m, null);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 4m, 900m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 3m, null);

        var existencia = await ExistenciaAsync(cliente, escenario);
        var (cantidadCapas, valorCapas) = await CapasAsync(cliente, escenario.VarianteId);

        Assert.Equal(existencia.Cantidad, cantidadCapas);
        Assert.Equal(existencia.Valorizado, Math.Round(valorCapas, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Conservación del valor en el libro completo: lo que entró es lo que salió más lo que
    /// queda. Es donde un redondeo mal puesto al consumir capas hace daño de verdad, porque el
    /// descuadre es de céntimos y nadie lo mira hasta el cierre.
    /// </summary>
    [Fact]
    public async Task El_valor_que_entro_es_el_que_salio_mas_el_que_queda()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(cliente);
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 13m, 1333m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 7m, 777m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 9m, null);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 11m, 2121m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 6m, null);

        var kardex = await KardexAsync(cliente, escenario.VarianteId);
        var existencia = await ExistenciaAsync(cliente, escenario);

        var entro = kardex.Where(m => m.Tipo.Suma()).Sum(m => m.CostoTotal);
        var salio = kardex.Where(m => !m.Tipo.Suma()).Sum(m => m.CostoTotal);

        Assert.Equal(entro, salio + existencia.Valorizado);
    }

    [Fact]
    public async Task Un_traslado_en_PEPS_lleva_las_capas_con_su_costo_y_su_antiguedad()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(cliente);
        var escenario = await EscenarioAsync(cliente);
        var destino = await CrearBodegaAsync(cliente, "SEC", "Secundaria");

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 3000m);

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/traslados",
            new PeticionTraslado(
                escenario.VarianteId, escenario.BodegaId, null, destino.Id, null, 15m, null, "TRA-1", null));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var existencias = (await cliente.GetFromJsonAsync<List<RespuestaExistencia>>(
            $"/api/inventario/existencias?varianteId={escenario.VarianteId}"))!;

        var enDestino = Assert.Single(existencias, e => e.BodegaId == destino.Id);

        // Se trasladan 10 a 1.000 y 5 a 3.000 = 25.000. Lo que importa es lo que viene después:
        // una salida de 10 del destino tiene que costar 10 × 1.000, no 10 × 1.666,67. Si el
        // traslado hubiera fundido las capas en una sola al costo ponderado, costaría el promedio.
        Assert.Equal(15m, enDestino.Cantidad);
        Assert.Equal(25_000m, enDestino.Valorizado);

        var salida = await MoverAsync(
            cliente,
            new Escenario(escenario.VarianteId, destino.Id),
            TipoMovimiento.Salida,
            10m,
            null);

        Assert.Equal(10_000m, salida.CostoTotal);
    }

    [Fact]
    public async Task Reversar_una_salida_en_PEPS_devuelve_la_mercancia_a_su_antiguedad()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(cliente);
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        var salida = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 10m, null);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 5000m);

        using var respuesta = await cliente.PostAsJsonAsync(
            $"/api/inventario/movimientos/{salida.Id}/reversa",
            new PeticionReversa("Devolución del cliente"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        // La mercancía devuelta vuelve con la antigüedad del asiento original, así que sale
        // **antes** que la entrada posterior: 10 × 1.000 y no 10 × 5.000. Si la reversa la pusiera
        // al final de la cola, la devolución de un cliente cambiaría el costo de ventas futuro.
        var siguiente = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 10m, null);

        Assert.Equal(10_000m, siguiente.CostoTotal);
    }

    [Fact]
    public async Task Con_saldo_negativo_la_entrada_siguiente_reinicia_las_capas()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(cliente);
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1500m);
        var forzada = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 14m, null);

        Assert.True(forzada.ForzadoNegativo);
        Assert.Equal(-4m, forzada.SaldoCantidadDespues);
        // 10 de la capa más 4 al último costo conocido: no se inventan capas negativas.
        Assert.Equal(21_000m, forzada.CostoTotal);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 5m, 900m);

        var existencia = await ExistenciaAsync(cliente, escenario);
        var (cantidadCapas, valorCapas) = await CapasAsync(cliente, escenario.VarianteId);

        // La entrada **no** paga la deuda a su costo: lo anterior ya quedó asentado y esto
        // empieza de cero. Arrastrar el estado a través de un negativo da valorizaciones absurdas.
        Assert.Equal(5m, existencia.Cantidad);
        Assert.Equal(4500m, existencia.Valorizado);
        Assert.Equal(5m, cantidadCapas);
        Assert.Equal(4500m, valorCapas);
    }

    [Fact]
    public async Task Cambiar_el_metodo_con_movimientos_registrados_se_rechaza()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(cliente);
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 1m, 100m);

        using var respuesta = await cliente.PutAsJsonAsync(
            "/api/inventario/configuracion",
            new PeticionConfiguracion(MetodoCosteo.PromedioPonderado),
            ApiFixture.Json);

        // Cambiarlo recalcularía costos ya asentados, y el kardex es inmutable.
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);

        var configuracion = await cliente.GetFromJsonAsync<RespuestaConfiguracion>(
            "/api/inventario/configuracion",
            ApiFixture.Json);

        Assert.Equal(MetodoCosteo.Peps, configuracion!.Metodo);
        Assert.False(configuracion.SePuedeCambiar);
    }

    [Fact]
    public async Task El_metodo_de_una_empresa_no_afecta_a_otra()
    {
        var conPeps = await ClienteDeEmpresaNuevaAsync();
        await FijarPepsAsync(conPeps);

        var conPromedio = await ClienteDeEmpresaNuevaAsync();

        var escenarioPeps = await EscenarioAsync(conPeps);
        var escenarioPromedio = await EscenarioAsync(conPromedio);

        await MoverAsync(conPeps, escenarioPeps, TipoMovimiento.Entrada, 10m, 1000m);
        await MoverAsync(conPeps, escenarioPeps, TipoMovimiento.Entrada, 10m, 3000m);
        await MoverAsync(conPromedio, escenarioPromedio, TipoMovimiento.Entrada, 10m, 1000m);
        await MoverAsync(conPromedio, escenarioPromedio, TipoMovimiento.Entrada, 10m, 3000m);

        var salidaPeps = await MoverAsync(conPeps, escenarioPeps, TipoMovimiento.Salida, 10m, null);
        var salidaPromedio = await MoverAsync(conPromedio, escenarioPromedio, TipoMovimiento.Salida, 10m, null);

        // La configuración es por empresa y el RLS la aísla: dos empresas en la misma base
        // costean distinto sin interferirse.
        Assert.Equal(10_000m, salidaPeps.CostoTotal);
        Assert.Equal(20_000m, salidaPromedio.CostoTotal);
    }

    [Fact]
    public async Task En_promedio_ponderado_no_se_crea_ninguna_capa()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 4m, null);

        var (cantidad, _) = await CapasAsync(cliente, escenario.VarianteId);

        // El promedio no necesita recordar de dónde vino cada unidad. Crear capas que nadie
        // consulta sería estado muerto que después hay que mantener cuadrado.
        Assert.Equal(0m, cantidad);
    }

    private sealed record Escenario(Guid VarianteId, Guid BodegaId);

    /// <summary>
    /// Lee las capas por SQL crudo con el superusuario de la imagen: aquí interesa el estado
    /// guardado, no lo que la Api decide mostrar. Es la única forma de comprobar que la
    /// persistencia y la aritmética no se separaron.
    /// </summary>
    private static async Task<(decimal Cantidad, decimal Valor)> CapasAsync(HttpClient cliente, Guid varianteId)
    {
        // La empresa sale del propio token y hay que ponerla en la sesión: la tabla lleva
        // `force row level security`, así que **ni el dueño** ve las filas sin fijarla. Que esto
        // sea necesario es la prueba de que el aislamiento también cubre las capas de costo.
        var yo = await cliente.GetFromJsonAsync<RespuestaYo>("/api/auth/yo");

        await using var conexion = new NpgsqlConnection(ApiFixture.CadenaConexion);
        await conexion.OpenAsync();

        await using (var sesion = conexion.CreateCommand())
        {
            sesion.CommandText = "select set_config('gestorinve.empresa_id', @empresa, false)";
            sesion.Parameters.AddWithValue("@empresa", yo!.EmpresaId.ToString());
            await sesion.ExecuteNonQueryAsync();
        }

        await using var comando = conexion.CreateCommand();
        comando.CommandText = """
            select coalesce(sum(cantidad_restante), 0),
                   coalesce(sum(cantidad_restante * costo_unitario), 0)
            from inventario.capas_costo
            where variante_id = @variante
            """;
        comando.Parameters.AddWithValue("variante", varianteId);

        await using var lector = await comando.ExecuteReaderAsync();
        await lector.ReadAsync();

        return (lector.GetDecimal(0), lector.GetDecimal(1));
    }

    private static async Task FijarPepsAsync(HttpClient cliente)
    {
        using var respuesta = await cliente.PutAsJsonAsync(
            "/api/inventario/configuracion",
            new PeticionConfiguracion(MetodoCosteo.Peps),
            ApiFixture.Json);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    private async Task<HttpClient> ClienteDeEmpresaNuevaAsync()
    {
        var nit = ApiFixture.NitNuevo();
        var correo = $"admin-{Guid.NewGuid():N}@empresa.co";

        using var anonimo = api.CreateClient();
        using var alta = await anonimo.PostAsJsonAsync(
            "/api/empresas",
            new PeticionAlta(nit, "Pyme de Pruebas S.A.S.", correo, "Administradora", Clave));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        using var login = await anonimo.PostAsJsonAsync("/api/auth/login", new PeticionLogin(nit, correo, Clave));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var sesion = (await login.Content.ReadFromJsonAsync<RespuestaSesion>())!;
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.TokenAcceso);

        return cliente;
    }

    private static async Task<RespuestaBodega> CrearBodegaAsync(HttpClient cliente, string codigo, string nombre)
    {
        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/bodegas",
            new PeticionBodega(codigo, nombre));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaBodega>())!;
    }

    private static async Task<Escenario> EscenarioAsync(HttpClient cliente)
    {
        using var unidadRespuesta = await cliente.PostAsJsonAsync(
            "/api/catalogo/unidades",
            new PeticionUnidad("UND", "Unidad", 0));

        var unidad = unidadRespuesta.StatusCode == HttpStatusCode.Created
            ? (await unidadRespuesta.Content.ReadFromJsonAsync<RespuestaUnidad>())!
            : (await cliente.GetFromJsonAsync<List<RespuestaUnidad>>("/api/catalogo/unidades"))!
                .Single(u => u.Codigo == "UND");

        using var productoRespuesta = await cliente.PostAsJsonAsync(
            "/api/catalogo/productos",
            new PeticionProducto($"SKU-{Guid.NewGuid():N}"[..12], "Artículo", unidad.Id, null, null, null, false, false));
        Assert.Equal(HttpStatusCode.Created, productoRespuesta.StatusCode);
        var producto = (await productoRespuesta.Content.ReadFromJsonAsync<RespuestaProducto>())!;

        using var bodegaRespuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/bodegas",
            new PeticionBodega("PRI", "Principal"));

        var bodega = bodegaRespuesta.StatusCode == HttpStatusCode.Created
            ? (await bodegaRespuesta.Content.ReadFromJsonAsync<RespuestaBodega>())!
            : (await cliente.GetFromJsonAsync<List<RespuestaBodega>>("/api/inventario/bodegas"))!
                .Single(b => b.Codigo == "PRI");

        return new Escenario(producto.Variantes.Single().Id, bodega.Id);
    }

    private static async Task<RespuestaMovimiento> MoverAsync(
        HttpClient cliente,
        Escenario escenario,
        TipoMovimiento tipo,
        decimal cantidad,
        decimal? costo)
    {
        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/movimientos",
            new PeticionMovimiento(
                tipo, escenario.VarianteId, escenario.BodegaId, null, cantidad, costo, null, "DOC-1", null),
            ApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var movimientos = (await respuesta.Content.ReadFromJsonAsync<List<RespuestaMovimiento>>(ApiFixture.Json))!;

        return Assert.Single(movimientos);
    }

    private static async Task<RespuestaExistencia> ExistenciaAsync(HttpClient cliente, Escenario escenario)
    {
        var existencias = await cliente.GetFromJsonAsync<List<RespuestaExistencia>>(
            $"/api/inventario/existencias?varianteId={escenario.VarianteId}&bodegaId={escenario.BodegaId}");

        return existencias!.Single();
    }

    private static async Task<List<RespuestaMovimiento>> KardexAsync(HttpClient cliente, Guid varianteId) =>
        (await cliente.GetFromJsonAsync<List<RespuestaMovimiento>>(
            $"/api/inventario/kardex?varianteId={varianteId}",
            ApiFixture.Json))!;
}
