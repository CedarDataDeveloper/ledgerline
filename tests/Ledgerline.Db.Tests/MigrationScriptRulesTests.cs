using System.Text.RegularExpressions;
using Ledgerline.DbMigrator;

namespace Ledgerline.Db.Tests;

// Rules every migration script must follow. They read the real
// db\<database>\migrations folders in the repository.
public class MigrationScriptRulesTests
{
    // Indexed views (blueprint 7.4) need these options, so every script sets
    // them before doing anything else.
    private const string StandardHeader =
        """
        SET ANSI_NULLS ON;
        SET ANSI_PADDING ON;
        SET ANSI_WARNINGS ON;
        SET ARITHABORT ON;
        SET CONCAT_NULL_YIELDS_NULL ON;
        SET QUOTED_IDENTIFIER ON;
        SET NUMERIC_ROUNDABORT OFF;
        GO
        """;

    // Four digits, an underscore, then lower_snake_case: 0002_core.sql.
    private static readonly Regex NamingRule = new(@"^\d{4}_[a-z0-9_]+\.sql$");

    public static TheoryData<string> Databases => new() { "bankcore", "warehouse" };

    // Every file, not just *.sql: DbUp would silently skip a misnamed file
    // such as 0002_core.txt, so the naming rule must see it.
    private static string[] ScriptFiles(string database) =>
        Directory.GetFiles(MigrationScripts.FolderFor(database));

    [Theory]
    [MemberData(nameof(Databases))]
    public void Every_script_name_follows_the_naming_rule(string database)
    {
        var files = ScriptFiles(database);

        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.Matches(NamingRule, Path.GetFileName(file)));
    }

    // DbUp runs scripts in name order, so two scripts with the same number
    // would have no clear order between them.
    [Theory]
    [MemberData(nameof(Databases))]
    public void Every_script_number_is_unique(string database)
    {
        var numbers = ScriptFiles(database)
            .Select(file => Path.GetFileName(file)[..4])
            .ToList();

        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void Every_script_starts_with_the_standard_header(string database)
    {
        Assert.All(ScriptFiles(database), file =>
        {
            // Git may check files out with either line ending.
            var text = File.ReadAllText(file).Replace("\r\n", "\n");
            Assert.StartsWith(StandardHeader.Replace("\r\n", "\n") + "\n", text);
        });
    }
}
