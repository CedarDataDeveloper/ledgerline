var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Ledgerline API: no endpoints yet (LL-007).");

app.Run();
