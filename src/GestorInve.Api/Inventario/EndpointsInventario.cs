using System.Security.Claims;
using GestorInve.Api.Autenticacion;
using GestorInve.Inventario;
using GestorInve.Persistencia;
using GestorInve.Seguridad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Api.Inventario;

public sealed record PeticionBodega(string Codigo, string Nombre);

public sealed record RespuestaUbicacion(Guid Id, string Codigo, string Nombre, bool Activa);

public sealed record RespuestaBodega(Guid Id, string Codigo, string Nombre, bool Activa, IReadOnlyList<RespuestaUbicacion> Ubicaciones);

public sealed record PeticionUbicacion(string Codigo, string Nombre);

public sealed record PeticionMovimiento(
    TipoMovimiento Tipo,
    Guid VarianteId,
    Guid BodegaId,
    Guid? UbicacionId,
    decimal Cantidad,
    decimal? CostoUnitario,
    DateTimeOffset? Fecha,
    string? Referencia,
    string? Observacion);

public sealed record PeticionTraslado(
    Guid VarianteId,
    Guid BodegaOrigenId,
    Guid? UbicacionOrigenId,
    Guid BodegaDestinoId,
    Guid? UbicacionDestinoId,
    decimal Cantidad,
    DateTimeOffset? Fecha,
    string? Referencia,
    string? Observacion);

public sealed record PeticionReversa(string? Observacion);

public sealed record RespuestaMovimiento(
    Guid Id,
    TipoMovimiento Tipo,
    Guid VarianteId,
    Guid BodegaId,
    Guid UbicacionId,
    decimal Cantidad,
    string Unidad,
    decimal CostoUnitario,
    decimal CostoTotal,
    string Moneda,
    decimal SaldoCantidadDespues,
    decimal SaldoCostoPromedioDespues,
    DateTimeOffset Fecha,
    string Usuario,
    string? Referencia,
    string? Observacion,
    Guid? TrasladoId,
    Guid? MovimientoReversadoId,
    bool ForzadoNegativo);

public sealed record RespuestaExistencia(
    Guid VarianteId,
    Guid BodegaId,
    Guid UbicacionId,
    decimal Cantidad,
    string Unidad,
    decimal CostoPromedio,
    decimal Valorizado,
    string Moneda);

/// <summary>
/// Endpoints del inventario.
///
/// Dos cosas que no se ven en las firmas:
/// - **Forzar existencias negativas exige `inventario:ajustar`.** El endpoint resuelve el
///   permiso y se lo pasa al servicio como un dato; el dominio no conoce HTTP ni reclamaciones.
/// - Lo que pertenece a otra empresa responde **404 y no 403**, para no confirmar que existe.
/// </summary>
public static class EndpointsInventario
{
    public static IEndpointRouteBuilder MapEndpointsInventario(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/inventario").WithTags("Inventario");

        MapBodegas(grupo);
        MapMovimientos(grupo);
        MapConsultas(grupo);

        return rutas;
    }

    private static void MapBodegas(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/bodegas", async (GestorInveDbContext db, CancellationToken ct) =>
        {
            var bodegas = await db.Set<Bodega>()
                .Include(b => b.Ubicaciones)
                .OrderBy(b => b.Codigo)
                .ToListAsync(ct);

            return Results.Ok(bodegas.Select(Proyectar).ToList());
        })
            .RequireAuthorization(Permiso.InventarioLeer)
            .WithSummary("Bodegas con sus ubicaciones.");

        grupo.MapPost("/bodegas", async (
            PeticionBodega peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var bodega = Bodega.Crear(peticion.Codigo, peticion.Nombre);
            db.Add(bodega);

            return await GuardarAsync(db, () => Results.Created($"/inventario/bodegas/{bodega.Id}", Proyectar(bodega)), ct);
        })
            .RequireAuthorization(Permiso.InventarioAjustar)
            .WithSummary("Crea una bodega con su ubicación general.");

        grupo.MapPost("/bodegas/{id:guid}/ubicaciones", async (
            Guid id,
            PeticionUbicacion peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var bodega = await db.Set<Bodega>()
                .Include(b => b.Ubicaciones)
                .SingleOrDefaultAsync(b => b.Id == id, ct);

            if (bodega is null)
            {
                return Results.NotFound();
            }

            try
            {
                var ubicacion = bodega.AgregarUbicacion(peticion.Codigo, peticion.Nombre);

                // `Add` explícito: la entidad nace con su Id puesto, así que al aparecer en la
                // colección de un agregado ya cargado EF la tomaría por existente (ADR 0005).
                db.Add(ubicacion);

                return await GuardarAsync(
                    db,
                    () => Results.Created($"/inventario/bodegas/{id}/ubicaciones/{ubicacion.Id}", Proyectar(ubicacion)),
                    ct);
            }
            catch (InvalidOperationException error)
            {
                return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
            .RequireAuthorization(Permiso.InventarioAjustar)
            .WithSummary("Añade una ubicación a una bodega.");
    }

    private static void MapMovimientos(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/movimientos", async (
            PeticionMovimiento peticion,
            ServicioMovimientos servicio,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            // Un ajuste es una corrección contable: exige el permiso más fuerte.
            if (!peticion.Tipo.EsSimple())
            {
                return Results.Problem(
                    detail: "Los traslados se registran en /inventario/traslados.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (peticion.Tipo.EsAjuste() && !PuedeAjustar(principal))
            {
                return Results.Problem(
                    detail: "Un ajuste de inventario requiere el permiso inventario:ajustar.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var resultado = await servicio.RegistrarAsync(
                new SolicitudMovimiento(
                    peticion.Tipo,
                    peticion.VarianteId,
                    peticion.BodegaId,
                    peticion.UbicacionId,
                    peticion.Cantidad,
                    peticion.CostoUnitario,
                    peticion.Fecha,
                    peticion.Referencia,
                    peticion.Observacion),
                permitirNegativo: PuedeAjustar(principal),
                ct);

            return Respuesta(resultado, m => Results.Created($"/inventario/movimientos/{m[0].Id}", Proyectar(m[0])));
        })
            .RequireAuthorization(Permiso.InventarioMover)
            .WithSummary("Registra una entrada, una salida o un ajuste.");

        grupo.MapPost("/traslados", async (
            PeticionTraslado peticion,
            ServicioMovimientos servicio,
            CancellationToken ct) =>
        {
            var resultado = await servicio.TrasladarAsync(
                new SolicitudTraslado(
                    peticion.VarianteId,
                    peticion.BodegaOrigenId,
                    peticion.UbicacionOrigenId,
                    peticion.BodegaDestinoId,
                    peticion.UbicacionDestinoId,
                    peticion.Cantidad,
                    peticion.Fecha,
                    peticion.Referencia,
                    peticion.Observacion),
                ct);

            return Respuesta(resultado, m => Results.Created(
                $"/inventario/traslados/{m[0].TrasladoId}",
                m.Select(Proyectar).ToList()));
        })
            .RequireAuthorization(Permiso.InventarioMover)
            .WithSummary("Traslada existencias entre ubicaciones, conservando el costo.");

        grupo.MapPost("/movimientos/{id:guid}/reversa", async (
            Guid id,
            PeticionReversa peticion,
            ServicioMovimientos servicio,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var resultado = await servicio.ReversarAsync(
                id,
                peticion.Observacion,
                permitirNegativo: PuedeAjustar(principal),
                ct);

            return Respuesta(resultado, m => Results.Created($"/inventario/movimientos/{m[0].Id}", Proyectar(m[0])));
        })
            .RequireAuthorization(Permiso.InventarioAjustar)
            .WithSummary("Reversa un movimiento creando su opuesto; el original no se toca.");
    }

    private static void MapConsultas(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/existencias", async (
            GestorInveDbContext db,
            Guid? varianteId,
            Guid? bodegaId,
            CancellationToken ct) =>
        {
            var consulta = db.Set<Existencia>().AsQueryable();

            if (varianteId is { } variante)
            {
                consulta = consulta.Where(e => e.VarianteId == variante);
            }

            if (bodegaId is { } bodega)
            {
                consulta = consulta.Where(e => e.BodegaId == bodega);
            }

            var existencias = await consulta.ToListAsync(ct);

            return Results.Ok(existencias.Select(Proyectar).ToList());
        })
            .RequireAuthorization(Permiso.InventarioLeer)
            .WithSummary("Existencias por variante, bodega y ubicación.");

        grupo.MapGet("/kardex", async (
            GestorInveDbContext db,
            Guid varianteId,
            Guid? bodegaId,
            CancellationToken ct) =>
        {
            var consulta = db.Set<Movimiento>().Where(m => m.VarianteId == varianteId);

            if (bodegaId is { } bodega)
            {
                consulta = consulta.Where(m => m.BodegaId == bodega);
            }

            var movimientos = await consulta
                .OrderBy(m => m.Fecha)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);

            return Results.Ok(movimientos.Select(Proyectar).ToList());
        })
            .RequireAuthorization(Permiso.InventarioLeer)
            .WithSummary("Kardex de una variante en orden cronológico.");
    }

    /// <summary>Forzar negativos y ajustar son la misma potestad: corregir el inventario.</summary>
    private static bool PuedeAjustar(ClaimsPrincipal principal) =>
        principal.HasClaim(ClaimsGestorInve.Permiso, Permiso.InventarioAjustar);

    private static IResult Respuesta(
        ResultadoMovimiento resultado,
        Func<IReadOnlyList<Movimiento>, IResult> alTenerExito) => resultado.Fallo switch
        {
            FalloMovimiento.Ninguno => alTenerExito(resultado.Movimientos),

            // 404 para lo que no existe **en esta empresa**: no se confirma que exista en otra.
            FalloMovimiento.VarianteInexistente
                or FalloMovimiento.BodegaInexistente
                or FalloMovimiento.UbicacionInexistente
                or FalloMovimiento.MovimientoInexistente =>
                Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status404NotFound),

            FalloMovimiento.SinExistencias
                or FalloMovimiento.YaReversado
                or FalloMovimiento.MismaUbicacion =>
                Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status409Conflict),

            FalloMovimiento.TrazabilidadNoSoportada =>
                Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status501NotImplemented),

            _ => Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status400BadRequest),
        };

    private static async Task<IResult> GuardarAsync(
        GestorInveDbContext db,
        Func<IResult> alGuardar,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);

            return alGuardar();
        }
        catch (DbUpdateException error)
            when (error.InnerException is Npgsql.PostgresException { SqlState: "23505" } duplicado)
        {
            return Results.Problem(
                detail: $"Ya existe un registro con esos datos ({duplicado.ConstraintName}).",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static RespuestaBodega Proyectar(Bodega b) => new(
        b.Id,
        b.Codigo,
        b.Nombre,
        b.Activa,
        [.. b.Ubicaciones.Select(Proyectar)]);

    private static RespuestaUbicacion Proyectar(Ubicacion u) => new(u.Id, u.Codigo, u.Nombre, u.Activa);

    private static RespuestaMovimiento Proyectar(Movimiento m) => new(
        m.Id,
        m.Tipo,
        m.VarianteId,
        m.BodegaId,
        m.UbicacionId,
        m.Cantidad,
        m.UnidadCodigo,
        m.CostoUnitario,
        m.CostoTotal,
        m.Moneda,
        m.SaldoCantidadDespues,
        m.SaldoCostoPromedioDespues,
        m.Fecha,
        m.Usuario,
        m.Referencia,
        m.Observacion,
        m.TrasladoId,
        m.MovimientoReversadoId,
        m.ForzadoNegativo);

    private static RespuestaExistencia Proyectar(Existencia e) => new(
        e.VarianteId,
        e.BodegaId,
        e.UbicacionId,
        e.Cantidad,
        e.UnidadCodigo,
        e.CostoPromedio,
        e.Saldo().Valorizado().Monto,
        e.Moneda);
}
