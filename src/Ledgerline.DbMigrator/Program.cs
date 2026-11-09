using Ledgerline.DbMigrator;

// Creates BankCore and Warehouse if they are missing and applies their
// pending migration scripts.
//
// Usage: dotnet run --project src/Ledgerline.DbMigrator -- [--server <name>]
// The server comes from --server, else LEDGERLINE_SQL_SERVER, else localhost.

string? server = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--server" && i + 1 < args.Length)
    {
        server = args[++i];
    }
    else
    {
        Console.Error.WriteLine("Usage: Ledgerline.DbMigrator [--server <name>]");
        return 2;
    }
}

if (string.IsNullOrWhiteSpace(server))
{
    server = Environment.GetEnvironmentVariable("LEDGERLINE_SQL_SERVER");
}
if (string.IsNullOrWhiteSpace(server))
{
    server = "localhost";
}

// Each database and its folder under db\ in the repository.
var databases = new[]
{
    (Name: "BankCore", Folder: "bankcore"),
    (Name: "Warehouse", Folder: "warehouse"),
};

foreach (var (name, folder) in databases)
{
    var result = DatabaseMigrator.Migrate(server, name, MigrationScripts.FolderFor(folder));

    foreach (var script in result.ScriptsApplied)
    {
        Console.WriteLine($"{name}: applied {script}");
    }

    if (!result.Successful)
    {
        var where = result.FailedScript is null ? "" : $" in {result.FailedScript}";
        Console.Error.WriteLine($"{name} on {server}: migration failed{where}: {result.Error?.Message}");
        return 1;
    }

    if (result.ScriptsApplied.Count == 0)
    {
        Console.WriteLine($"{name}: nothing pending");
    }
}

Console.WriteLine($"Migrations complete on {server}.");
return 0;
