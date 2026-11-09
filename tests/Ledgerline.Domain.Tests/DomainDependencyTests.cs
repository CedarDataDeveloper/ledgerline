using System.Xml.Linq;

namespace Ledgerline.Domain.Tests;

public class DomainDependencyTests
{
    // The domain holds the rules that protect money, so it must not depend on
    // a database library or on any outer layer. This reads the project file
    // and fails if a package or project reference has been added to it.
    [Fact]
    public void Domain_project_declares_no_package_or_project_references()
    {
        var projectFile = Path.Combine(
            RepositoryRoot(), "src", "Ledgerline.Domain", "Ledgerline.Domain.csproj");

        var references = XDocument.Load(projectFile)
            .Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .ToList();

        Assert.Empty(references);
    }

    // Tests run from a bin folder deep inside the repository, so walk up
    // until the folder that holds the solution file.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Ledgerline.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Ledgerline.slnx was not found above the test folder.");
    }
}
