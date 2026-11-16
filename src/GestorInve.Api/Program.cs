using System.Text;
using GestorInve.Api;
using GestorInve.Api.Autenticacion;
using GestorInve.Api.Catalogo;
using GestorInve.Api.Inventario;
using GestorInve.Core;
using GestorInve.Composicion;
using GestorInve.Core.Seguridad;
using GestorInve.Persistencia;
using GestorInve.Seguridad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// `--comprobar-salud` existe por el contenedor: la imagen de runtime de ASP.NET no trae
// `curl` ni `wget`, así que el `HEALTHCHECK` de Docker no tiene con qué consultar `/ready`.
// Antes que instalar un paquete entero en la imagen final, el propio ejecutable sabe
// preguntarle a la instancia que ya está escuchando y traducir la respuesta a un código de
// salida. No arranca el host: solo hace una petición y termina.
if (args.Contains("--comprobar-salud", StringComparer.Ordinal))
{
    return await Sonda.ComprobarAsync();
}

var builder = WebApplication.CreateBuilder(args);

// Instalado en Windows corre como servicio (ADR 0011). Fuera de un servicio esta llamada no
// hace nada, así que no afecta a Linux, a Docker ni a las pruebas.
builder.Host.UseWindowsService(opciones => opciones.ServiceName = "GestorInve");

// Configuración de la instalación local, en un archivo aparte. En Windows los secretos no pueden
// ir en el entorno del servicio: ese entorno vive en el registro, en una clave que cualquier
// usuario del equipo puede leer. Van en un archivo con permisos restringidos, y aquí solo llega
// su ruta. Si la variable está puesta, el archivo **tiene** que existir: arrancar sin él sería
// arrancar sin clave de firma ni base de datos.
if (Environment.GetEnvironmentVariable("GESTORINVE_CONFIG") is { Length: > 0 } rutaConfiguracion)
{
    builder.Configuration.AddJsonFile(rutaConfiguracion, optional: false, reloadOnChange: false);
}

// Los enum viajan como texto («Entrada»), no como número. Un entero en el contrato es
// ilegible y, peor, frágil: reordenar o insertar un valor del enum cambiaría en silencio el
// significado de los datos que ya enviaron los clientes.
builder.Services.ConfigureHttpJsonOptions(opciones =>
    opciones.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<IHasheadorContrasena, HasheadorContrasena>();
builder.Services.AddSingleton<IEnumerable<IModuloPersistencia>>(ModulosDePersistencia.Todos);

// La empresa de la petición sale del token validado, nunca de la entrada del cliente.
builder.Services.AddScoped<IContextoTenant, ContextoTenantHttp>();

var cadena = builder.Configuration.GetConnectionString("GestorInve");
var conPersistencia = !string.IsNullOrWhiteSpace(cadena);

if (conPersistencia)
{
    builder.Services.AddPersistencia(cadena!);
    builder.Services.AddHealthChecks().AddDbContextCheck<GestorInveDbContext>("base-datos", tags: ["listo"]);

    builder.Services.AddScoped<ServicioAltaEmpresa>();
    builder.Services.AddScoped<ServicioAutenticacion>();
    builder.Services.AddScoped<GestorInve.Inventario.ServicioMovimientos>();
}

var jwt = builder.Configuration.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>() ?? new OpcionesJwt();

if (conPersistencia)
{
    // Con base de datos configurada el sistema atiende peticiones reales: una clave de firma
    // ausente o débil aborta el arranque en lugar de aceptar tokens falsificables.
    jwt.Validar();
    builder.Services.AddSingleton(jwt);

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opciones =>
        {
            opciones.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Emisor,
                ValidateAudience = true,
                ValidAudience = jwt.Audiencia,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Clave)),
                ValidateLifetime = true,

                // Sin margen: el valor por omisión (5 minutos) alarga la vida de un token revocado.
                ClockSkew = TimeSpan.Zero,
            };
        });

    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(null)
        .AddDefaultPolicy("autenticado", politica => politica.RequireAuthenticatedUser());

    // Una política por permiso: el endpoint declara qué permiso exige y no interpreta roles.
    foreach (var permiso in Permiso.Todos)
    {
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(permiso, politica => politica
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimsGestorInve.Permiso, permiso));
    }
}

var app = builder.Build();

// Aplicar migraciones al arrancar es **opcional y explícito** (`GESTORINVE_MIGRAR=true`).
// Nunca por omisión: en una instalación real, migrar sin que nadie lo haya pedido puede
// bloquear tablas o aplicar un cambio de esquema a destiempo. Con la bandera puesta, el
// instalador, el contenedor y el CI tienen un único camino de actualización, y queda registrado.
if (conPersistencia
    && app.Configuration.GetValue("GESTORINVE_MIGRAR", defaultValue: false))
{
    await using var alcance = app.Services.CreateAsyncScope();
    var contexto = alcance.ServiceProvider.GetRequiredService<GestorInveDbContext>();
    var registro = alcance.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("GestorInve.Migraciones");

    var pendientes = (await contexto.Database.GetPendingMigrationsAsync()).ToList();

    if (pendientes.Count > 0)
    {
        // El `IsEnabled` no es decorativo: CA1873 exige no pagar el `string.Join` si el nivel
        // está apagado, y la traza de la actualización se consulta justo cuando algo falló.
        if (registro.IsEnabled(LogLevel.Information))
        {
            registro.Aplicando(pendientes.Count, string.Join(", ", pendientes));
        }

        await contexto.Database.MigrateAsync();
    }
    else
    {
        registro.SinPendientes();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness: el proceso responde. No toca la base de datos a propósito.
app.MapHealthChecks("/health", new() { Predicate = _ => false });

// Readiness: además, la base de datos contesta.
app.MapHealthChecks("/ready");

if (conPersistencia)
{
    app.UseAuthentication();
    app.UseAuthorization();

    // **Todo** lo de la Api vive bajo `/api`. No es cosmético: la web y la Api se sirven en el
    // mismo origen (ADR 0008) y, sin este prefijo, `/inventario` es a la vez una ruta del
    // enrutador del cliente y el prefijo de un grupo de la Api. El proxy la reenviaba a la Api,
    // que no tiene esa ruta, así que abrir `http://servidor/inventario` —un favorito, un enlace
    // compartido, una recarga— devolvía 404. Lo destapó el job de despliegue de H4.
    //
    // `/health` y `/ready` se quedan fuera a propósito: son del proceso, no de la Api, y los
    // consulta el contenedor, no el navegador.
    var api = app.MapGroup("/api");

    api.MapEndpointsAutenticacion();
    api.MapEndpointsEmpresa();
    api.MapEndpointsCatalogo();
    api.MapEndpointsInventario();
}

// En la instalación de escritorio no hay nginx: la propia Api sirve la web (ADR 0011). Solo
// ocurre si la carpeta existe, así que en Docker —donde la sirve nginx— y en las pruebas no
// cambia nada.
var raizWeb = app.Configuration["GESTORINVE_WEB"] ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
var indice = Path.Combine(raizWeb, "index.html");

if (File.Exists(indice))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(raizWeb)),
        OnPrepareResponse = contexto =>
        {
            // Los nombres de Vite llevan hash de contenido, así que son inmutables.
            if (contexto.Context.Request.Path.StartsWithSegments("/assets", StringComparison.Ordinal))
            {
                contexto.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            }
        },
    });

    // SPA: una ruta que el servidor no conoce la decide el enrutador del cliente. Pero **no**
    // bajo `/api`: una ruta de Api inexistente tiene que seguir siendo un 404 y no un HTML con
    // 200, o un cliente que integre recibiría una página donde esperaba un error.
    app.MapFallback(async contexto =>
    {
        var esLectura = HttpMethods.IsGet(contexto.Request.Method) || HttpMethods.IsHead(contexto.Request.Method);

        if (!esLectura || contexto.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            contexto.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // `index.html` no se cachea: apunta a bundles con hash, y una copia vieja dejaría al
        // usuario pidiendo archivos que la actualización ya borró.
        contexto.Response.ContentType = "text/html; charset=utf-8";
        contexto.Response.Headers.CacheControl = "no-cache";
        await contexto.Response.SendFileAsync(indice);
    });
}

await app.RunAsync();

return 0;

/// <summary>Expuesta para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
