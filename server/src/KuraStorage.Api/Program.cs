var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet(
    "/api/v1/system/health",
    () => Results.Ok(new { api = "AVAILABLE", protocolVersion = 1, storage = "UNAVAILABLE" }));

app.Run();

public partial class Program;
