using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorInve.Api.Autenticacion;
using GestorInve.Api.Catalogo;
using GestorInve.Api.Inventario;
using GestorInve.Inventario;

namespace GestorInve.Api.Tests;

/// <summary>
/// Trazabilidad por lotes y seriales (ADR 0009), contra la Api y PostgreSQL reales. Lo que se
/// prueba aquí no es que los endpoints respondan, sino las reglas que hacen que la trazabilidad
/// signifique algo: que el libro de cada lote cuadre por separado, que FEFO elija bien y parta
/// el asiento, y que lo que no debe salir no salga.
/// </summary>
[Collection(ColeccionApi.Nombre)]
[Trait("Categoria", "Integracion")]
public class LotesTests(ApiFixture api)
{
    private const string Clave = "Clave-Muy-Larga-2026";

    [Fact]
    public async Task Cada_lote_lleva_su_propio_saldo_y_su_propio_promedio()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        var l1 = await CrearLoteAsync(cliente, escenario.VarianteId, "L1", Hoy.AddDays(30));
        var l2 = await CrearLoteAsync(cliente, escenario.VarianteId, "L2", Hoy.AddDays(60));

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m, l1.Id);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 3000m, l2.Id);

        var existencias = await ExistenciasAsync(cliente, escenario);

        // Lo que importa: **no** se promedian entre sí. Si lo hicieran, los dos lotes saldrían a
        // 2000 y la trazabilidad no serviría para distinguir lo que costó cada uno.
        Assert.Equal(1000m, Assert.Single(existencias, e => e.LoteId == l1.Id).CostoPromedio);
        Assert.Equal(3000m, Assert.Single(existencias, e => e.LoteId == l2.Id).CostoPromedio);
        Assert.Equal(40_000m, existencias.Sum(e => e.Valorizado));
    }

    [Fact]
    public async Task Una_salida_sin_lote_consume_por_vencimiento_mas_proximo_y_parte_el_asiento()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        // Se crean en orden inverso al de vencimiento a propósito: si FEFO ordenara por fecha de
        // creación o por código en vez de por vencimiento, la prueba lo notaría.
        var lejano = await CrearLoteAsync(cliente, escenario.VarianteId, "AAA-LEJANO", Hoy.AddDays(90));
        var proximo = await CrearLoteAsync(cliente, escenario.VarianteId, "ZZZ-PROXIMO", Hoy.AddDays(5));

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m, lejano.Id);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 2000m, proximo.Id);

        // 12 unidades: agotan el lote próximo (10) y toman 2 del lejano.
        var asientos = await MoverVariosAsync(cliente, escenario, TipoMovimiento.Salida, 12m, null, lote: null);

        Assert.Equal(2, asientos.Count);
        Assert.Equal(proximo.Id, asientos[0].LoteId);
        Assert.Equal(10m, asientos[0].Cantidad);
        Assert.Equal(lejano.Id, asientos[1].LoteId);
        Assert.Equal(2m, asientos[1].Cantidad);

        // Los dos asientos son la misma operación, y se puede demostrar.
        Assert.NotNull(asientos[0].OperacionId);
        Assert.Equal(asientos[0].OperacionId, asientos[1].OperacionId);

        // Cada asiento sale al costo **de su lote**, no a un promedio inventado entre ambos.
        Assert.Equal(2000m, asientos[0].CostoUnitario);
        Assert.Equal(1000m, asientos[1].CostoUnitario);

        var existencias = await ExistenciasAsync(cliente, escenario);
        Assert.Equal(0m, Assert.Single(existencias, e => e.LoteId == proximo.Id).Cantidad);
        Assert.Equal(8m, Assert.Single(existencias, e => e.LoteId == lejano.Id).Cantidad);
    }

    [Fact]
    public async Task El_kardex_de_cada_lote_cuadra_con_su_existencia()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        var l1 = await CrearLoteAsync(cliente, escenario.VarianteId, "L1", Hoy.AddDays(10));
        var l2 = await CrearLoteAsync(cliente, escenario.VarianteId, "L2", Hoy.AddDays(20));

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 7m, 500m, l1.Id);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 5m, 800m, l2.Id);
        await MoverVariosAsync(cliente, escenario, TipoMovimiento.Salida, 9m, null, lote: null);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 3m, 900m, l1.Id);

        var kardex = await KardexAsync(cliente, escenario.VarianteId);
        var existencias = await ExistenciasAsync(cliente, escenario);

        // La garantía de H7, ahora **por lote**: la suma firmada del libro de un lote tiene que
        // ser igual a su existencia. Si la dimensión estuviera mal llevada, dos lotes se
        // mezclarían y el total seguiría cuadrando aunque cada lote estuviera mal.
        foreach (var existencia in existencias)
        {
            var delLote = kardex.Where(m => m.LoteId == existencia.LoteId).ToList();
            var firmada = delLote.Sum(m => m.Cantidad * m.Tipo.Signo());

            Assert.Equal(existencia.Cantidad, firmada);
            Assert.Equal(existencia.Cantidad, delLote[^1].SaldoCantidadDespues);
        }

        Assert.Equal(6m, existencias.Sum(e => e.Cantidad));
    }

    [Fact]
    public async Task Un_lote_vencido_no_sale_como_despacho_pero_si_como_ajuste()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        var vencido = await CrearLoteAsync(cliente, escenario.VarianteId, "CADUCO", Hoy.AddDays(-1));

        // La entrada del lote vencido sí se permite: recibir mercancía que llega caducada es un
        // hecho que el inventario tiene que poder registrar, justamente para poder reclamarla.
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m, vencido.Id);

        using var salida = await cliente.PostAsJsonAsync(
            "/api/inventario/movimientos",
            Peticion(escenario, TipoMovimiento.Salida, 4m, null, vencido.Id));

        Assert.Equal(HttpStatusCode.Conflict, salida.StatusCode);

        // La baja existe y es explícita: un ajuste negativo, que exige `inventario:ajustar` y
        // queda en el libro como lo que es, una baja y no una venta.
        var baja = await MoverAsync(cliente, escenario, TipoMovimiento.AjusteNegativo, 10m, null, vencido.Id);

        Assert.Equal(0m, baja.SaldoCantidadDespues);
    }

    [Fact]
    public async Task FEFO_no_elige_un_lote_vencido_aunque_sea_el_mas_proximo()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        var vencido = await CrearLoteAsync(cliente, escenario.VarianteId, "CADUCO", Hoy.AddDays(-2));
        var vigente = await CrearLoteAsync(cliente, escenario.VarianteId, "VIGENTE", Hoy.AddDays(15));

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m, vencido.Id);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m, vigente.Id);

        var asiento = await MoverVariosAsync(cliente, escenario, TipoMovimiento.Salida, 4m, null, lote: null);

        // Si FEFO tomara el vencido por ser el más próximo, la salida fallaría siempre en cuanto
        // hubiera mercancía caducada en la bodega: el lote vencido se salta, no se elige.
        Assert.Equal(vigente.Id, Assert.Single(asiento).LoteId);
    }

    [Fact]
    public async Task FEFO_no_alcanza_si_lo_disponible_esta_en_lotes_vencidos()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        var vencido = await CrearLoteAsync(cliente, escenario.VarianteId, "CADUCO", Hoy.AddDays(-2));
        var vigente = await CrearLoteAsync(cliente, escenario.VarianteId, "VIGENTE", Hoy.AddDays(15));

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m, vencido.Id);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 3m, 1000m, vigente.Id);

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/movimientos",
            Peticion(escenario, TipoMovimiento.Salida, 5m, null, lote: null));

        // Hay 13 unidades en la bodega, pero solo 3 se pueden despachar. Contar las vencidas como
        // disponibles sería prometer mercancía que no se debe entregar.
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_producto_sin_trazabilidad_rechaza_un_movimiento_con_lote()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var conLote = await EscenarioAsync(cliente, exigeLote: true);
        var sinLote = await EscenarioAsync(cliente, exigeLote: false);

        var lote = await CrearLoteAsync(cliente, conLote.VarianteId, "L1", Hoy.AddDays(30));

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/movimientos",
            Peticion(sinLote with { BodegaId = conLote.BodegaId }, TipoMovimiento.Entrada, 1m, 100m, lote.Id));

        // Aceptar una dimensión que el producto no declara crearía existencias que ninguna
        // pantalla sabría mostrar y que el saldo sin lote no contaría.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_lote_de_otra_variante_no_sirve()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var primero = await EscenarioAsync(cliente, exigeLote: true);
        var segundo = await EscenarioAsync(cliente, exigeLote: true);

        var loteAjeno = await CrearLoteAsync(cliente, segundo.VarianteId, "L1", Hoy.AddDays(30));

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/movimientos",
            Peticion(primero, TipoMovimiento.Entrada, 1m, 100m, loteAjeno.Id));

        // Un lote es del artículo, no del inventario: compartirlo entre variantes confundiría
        // dos cosas que vencen en fechas distintas.
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_lote_de_otra_empresa_responde_404_y_no_403()
    {
        var clienteA = await ClienteDeEmpresaNuevaAsync();
        var clienteB = await ClienteDeEmpresaNuevaAsync();

        var escenarioA = await EscenarioAsync(clienteA, exigeLote: true);
        var escenarioB = await EscenarioAsync(clienteB, exigeLote: true);
        var loteB = await CrearLoteAsync(clienteB, escenarioB.VarianteId, "L1", Hoy.AddDays(30));

        using var respuesta = await clienteA.PostAsJsonAsync(
            "/api/inventario/movimientos",
            Peticion(escenarioA, TipoMovimiento.Entrada, 1m, 100m, loteB.Id));

        // 404 y no 403: un 403 confirmaría que ese identificador existe en otra empresa.
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);

        var lotesVisibles = await clienteA.GetFromJsonAsync<List<RespuestaLote>>("/api/inventario/lotes");
        Assert.DoesNotContain(loteB.Id, lotesVisibles!.Select(l => l.Id));
    }

    [Fact]
    public async Task Un_serial_solo_admite_cantidad_uno_y_no_puede_estar_en_dos_sitios()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: false, exigeSerial: true);

        var serial = await CrearLoteAsync(cliente, escenario.VarianteId, "SN-0001", null, esSerial: true);

        using var dos = await cliente.PostAsJsonAsync(
            "/api/inventario/movimientos",
            Peticion(escenario, TipoMovimiento.Entrada, 2m, 100m, serial.Id));

        Assert.Equal(HttpStatusCode.Conflict, dos.StatusCode);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 1m, 100m, serial.Id);

        using var repetido = await cliente.PostAsJsonAsync(
            "/api/inventario/movimientos",
            Peticion(escenario, TipoMovimiento.Entrada, 1m, 100m, serial.Id));

        // Un serial que está en dos sitios a la vez no identifica nada.
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }

    [Fact]
    public async Task El_traslado_conserva_el_lote_y_su_costo()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);
        var destino = await CrearBodegaAsync(cliente, "SEC", "Secundaria");

        var lote = await CrearLoteAsync(cliente, escenario.VarianteId, "L1", Hoy.AddDays(30));
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1500m, lote.Id);

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/traslados",
            new PeticionTraslado(
                escenario.VarianteId, escenario.BodegaId, null, destino.Id, null, 4m, null, "TRA-1", null, lote.Id));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var asientos = (await respuesta.Content.ReadFromJsonAsync<List<RespuestaMovimiento>>(ApiFixture.Json))!;

        // Mover mercancía no cambia su lote ni lo que costó.
        Assert.All(asientos, m => Assert.Equal(lote.Id, m.LoteId));
        Assert.All(asientos, m => Assert.Equal(1500m, m.CostoUnitario));
    }

    [Fact]
    public async Task La_reversa_devuelve_la_mercancia_a_su_lote()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        var lote = await CrearLoteAsync(cliente, escenario.VarianteId, "L1", Hoy.AddDays(30));
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m, lote.Id);
        var salida = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 4m, null, lote.Id);

        using var respuesta = await cliente.PostAsJsonAsync(
            $"/api/inventario/movimientos/{salida.Id}/reversa",
            new PeticionReversa("Devolución"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var reversa = (await respuesta.Content.ReadFromJsonAsync<RespuestaMovimiento>(ApiFixture.Json))!;

        Assert.Equal(lote.Id, reversa.LoteId);
        Assert.Equal(10m, reversa.SaldoCantidadDespues);
    }

    [Fact]
    public async Task Reversar_una_salida_de_un_lote_que_ya_vencio_sigue_siendo_posible()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        // Vence hoy: la salida de hoy se permite (vencido es `fecha < hoy`), y la reversa tendrá
        // que entrar aunque el lote esté ya en el límite. Si la reversa volviera a pasar por las
        // reglas de trazabilidad, corregir un error quedaría bloqueado por el calendario.
        var lote = await CrearLoteAsync(cliente, escenario.VarianteId, "LIMITE", Hoy);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m, lote.Id);
        var salida = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 4m, null, lote.Id);

        using var respuesta = await cliente.PostAsJsonAsync(
            $"/api/inventario/movimientos/{salida.Id}/reversa",
            new PeticionReversa(null));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    [Fact]
    public async Task Dos_lotes_con_el_mismo_codigo_en_la_misma_variante_se_rechazan()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        await CrearLoteAsync(cliente, escenario.VarianteId, "L1", Hoy.AddDays(30));

        using var repetido = await cliente.PostAsJsonAsync(
            $"/api/inventario/variantes/{escenario.VarianteId}/lotes",
            new PeticionLote("L1", Hoy.AddDays(60), false));

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }

    [Fact]
    public async Task Los_lotes_se_listan_por_vencimiento_mas_proximo_primero()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        await CrearLoteAsync(cliente, escenario.VarianteId, "C", Hoy.AddDays(60));
        await CrearLoteAsync(cliente, escenario.VarianteId, "A", Hoy.AddDays(10));
        await CrearLoteAsync(cliente, escenario.VarianteId, "B", null);

        var lotes = await cliente.GetFromJsonAsync<List<RespuestaLote>>(
            $"/api/inventario/lotes?varianteId={escenario.VarianteId}");

        // Es el orden en el que se van a consumir, así que es el que tiene sentido ver. Los sin
        // vencimiento van al final y no primero, que es lo que haría un `order by` ingenuo.
        Assert.Equal(["A", "C", "B"], lotes!.Select(l => l.Codigo));
    }

    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Escenario(Guid VarianteId, Guid BodegaId);

    private static PeticionMovimiento Peticion(
        Escenario escenario,
        TipoMovimiento tipo,
        decimal cantidad,
        decimal? costo,
        Guid? lote) =>
        new(tipo, escenario.VarianteId, escenario.BodegaId, null, cantidad, costo, null, "DOC-1", null, lote);

    private async Task<(string Nit, string Correo)> AltaAsync()
    {
        var nit = ApiFixture.NitNuevo();
        var correo = $"admin-{Guid.NewGuid():N}@empresa.co";

        using var cliente = api.CreateClient();
        using var alta = await cliente.PostAsJsonAsync(
            "/api/empresas",
            new PeticionAlta(nit, "Pyme de Pruebas S.A.S.", correo, "Administradora", Clave));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        return (nit, correo);
    }

    private async Task<HttpClient> ClienteDeEmpresaNuevaAsync()
    {
        var (nit, correo) = await AltaAsync();

        using var anonimo = api.CreateClient();
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

    private static async Task<RespuestaLote> CrearLoteAsync(
        HttpClient cliente,
        Guid varianteId,
        string codigo,
        DateOnly? vencimiento,
        bool esSerial = false)
    {
        using var respuesta = await cliente.PostAsJsonAsync(
            $"/api/inventario/variantes/{varianteId}/lotes",
            new PeticionLote(codigo, vencimiento, esSerial));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaLote>())!;
    }

    private static async Task<Escenario> EscenarioAsync(
        HttpClient cliente,
        bool exigeLote,
        bool exigeSerial = false)
    {
        // La unidad se crea una vez por empresa; si ya existe, se reutiliza la que hay.
        using var unidadRespuesta = await cliente.PostAsJsonAsync(
            "/api/catalogo/unidades",
            new PeticionUnidad("UND", "Unidad", 0));

        var unidad = unidadRespuesta.StatusCode == HttpStatusCode.Created
            ? (await unidadRespuesta.Content.ReadFromJsonAsync<RespuestaUnidad>())!
            : (await cliente.GetFromJsonAsync<List<RespuestaUnidad>>("/api/catalogo/unidades"))!
                .Single(u => u.Codigo == "UND");

        using var productoRespuesta = await cliente.PostAsJsonAsync(
            "/api/catalogo/productos",
            new PeticionProducto(
                $"SKU-{Guid.NewGuid():N}"[..12],
                "Artículo con trazabilidad",
                unidad.Id,
                null,
                null,
                null,
                exigeLote,
                exigeSerial));

        Assert.Equal(HttpStatusCode.Created, productoRespuesta.StatusCode);
        var producto = (await productoRespuesta.Content.ReadFromJsonAsync<RespuestaProducto>())!;

        // La bodega también se reutiliza: varias llamadas en la misma empresa comparten «PRI».
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
        decimal? costo,
        Guid? lote) =>
        Assert.Single(await MoverVariosAsync(cliente, escenario, tipo, cantidad, costo, lote));

    private static async Task<List<RespuestaMovimiento>> MoverVariosAsync(
        HttpClient cliente,
        Escenario escenario,
        TipoMovimiento tipo,
        decimal cantidad,
        decimal? costo,
        Guid? lote)
    {
        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/inventario/movimientos",
            Peticion(escenario, tipo, cantidad, costo, lote));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<List<RespuestaMovimiento>>(ApiFixture.Json))!;
    }

    private static async Task<List<RespuestaExistencia>> ExistenciasAsync(HttpClient cliente, Escenario escenario) =>
        (await cliente.GetFromJsonAsync<List<RespuestaExistencia>>(
            $"/api/inventario/existencias?varianteId={escenario.VarianteId}&bodegaId={escenario.BodegaId}"))!;

    private static async Task<List<RespuestaMovimiento>> KardexAsync(HttpClient cliente, Guid varianteId) =>
        (await cliente.GetFromJsonAsync<List<RespuestaMovimiento>>(
            $"/api/inventario/kardex?varianteId={varianteId}",
            ApiFixture.Json))!;
}
