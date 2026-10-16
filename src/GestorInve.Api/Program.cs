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

var builder = WebApplication.CreateBuilder(args);

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
    app.MapEndpointsAutenticacion();
    app.MapEndpointsEmpresa();
    app.MapEndpointsCatalogo();
    app.MapEndpointsInventario();
}

await app.RunAsync();

/// <summary>Expuesta para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
