namespace Ledgerline.DbMigrator;

// Finds the migration scripts in the repository itself, so there is only one
// copy of them: the db\<database>\migrations folders that git tracks.
public static class MigrationScripts
{
    // database is the folder name under db\: "bankcore" or "warehouse".
    public static string FolderFor(string database) =>
        Path.Combine(RepositoryRoot(), "db", database, "migrations");

    // The program and the tests run from a bin folder deep inside the
    // repository, so walk up until the folder that holds the solution file.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Ledgerline.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Ledgerline.slnx was not found above " + AppContext.BaseDirectory + ".");
    }
}
