using System.Text.Json.Serialization;
using Firin.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "FirinAdisyon");

builder.Services.AddSingleton<FirinPaths>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.Services.GetRequiredService<FirinPaths>(); // klasörleri hemen oluştur

app.UseExceptionHandler();
app.UseStatusCodePages();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
