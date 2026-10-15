using GestorInve.Api;
using GestorInve.Core;
using GestorInve.Persistencia;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

// Hasta H5 no hay usuarios: sin empresa en contexto, todo queda cerrado por defecto.
builder.Services.AddSingleton<IContextoTenant>(new ContextoTenantFijo(null, "sistema"));
builder.Services.AddSingleton<IEnumerable<IModuloPersistencia>>(ModulosDePersistencia.Todos);

var cadena = builder.Configuration.GetConnectionString("GestorInve");
if (!string.IsNullOrWhiteSpace(cadena))
{
    builder.Services.AddPersistencia(cadena);
    builder.Services.AddHealthChecks().AddDbContextCheck<GestorInveDbContext>("base-datos", tags: ["listo"]);
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

await app.RunAsync();

/// <summary>Expuesta para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
