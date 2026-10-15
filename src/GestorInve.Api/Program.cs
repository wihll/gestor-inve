var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

await app.RunAsync();

/// <summary>Expuesta para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
