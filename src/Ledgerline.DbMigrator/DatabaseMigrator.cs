using DbUp;
using DbUp.Engine.Output;
using Microsoft.Data.SqlClient;

namespace Ledgerline.DbMigrator;

// What one migration run did. ScriptsApplied lists the scripts committed in
// this run, in order. When something fails, Error holds the reason and
// FailedScript names the script, if a script was running at the time.
// Scripts before the failed one stay applied.
public sealed record MigrationResult(
    bool Successful,
    IReadOnlyList<string> ScriptsApplied,
    string? FailedScript,
    Exception? Error);

// Creates a database if it is missing, then applies the numbered SQL scripts
// it has not run yet. Schema changes live in these scripts (ADR-010).
public static class DatabaseMigrator
{
    public static MigrationResult Migrate(string server, string database, string scriptsFolder)
    {
        // Windows authentication, so no password is ever stored. A local
        // instance has a self-signed certificate, so the connection trusts it.
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
        }.ConnectionString;

        // DbUp's own log would print the connection string and every batch.
        // The caller prints a short summary from the result instead.
        var quiet = new NoOpUpgradeLog();

        try
        {
            EnsureDatabase.For.SqlDatabase(connectionString, quiet);

            // Scripts run in name order. Each one gets its own transaction, so
            // a script that fails part way is rolled back completely and is not
            // recorded in dbo.SchemaVersions (DbUp's default journal table).
            var upgrader = DeployChanges.To
                .SqlDatabase(connectionString)
                .WithScriptsFromFileSystem(scriptsFolder)
                .WithTransactionPerScript()
                .LogTo(quiet)
                .Build();

            // DatabaseUpgradeResult.Scripts does not reliably list what ran,
            // so compare the pending list before the run with the journal after.
            var pending = upgrader.GetScriptsToExecute().Select(script => script.Name).ToList();
            var result = upgrader.PerformUpgrade();
            var journal = upgrader.GetExecutedScripts();
            var applied = pending.Where(journal.Contains).ToList();

            return new MigrationResult(result.Successful, applied, result.ErrorScript?.Name, result.Error);
        }
        catch (Exception error)
        {
            // An unreachable server or a missing scripts folder fails before
            // any script runs. Report it like a failed script rather than crash.
            return new MigrationResult(false, [], null, error);
        }
    }
}
