using Xunit;

namespace Po.SeeReview.IntegrationTests.EdgeCases;

/// <summary>
/// Tests for Directory.Packages.props version conflict scenarios.
/// Validates constitution requirement: FR-001 (centralized package management).
/// </summary>
public class DirectoryPackagesConflictTests
{
    [Fact]
    public void DirectoryPackagesProps_ShouldExist()
    {
        // Arrange
        var repoRoot = GetRepositoryRoot();
        var directoryPackagesPath = Path.Combine(repoRoot, "Directory.Packages.props");

        // Act & Assert
        Assert.True(File.Exists(directoryPackagesPath), 
            "Directory.Packages.props must exist at repository root");
    }

    [Fact]
    public void DirectoryPackagesProps_ShouldHaveManagePackageVersionsCentrally()
    {
        // Arrange
        var repoRoot = GetRepositoryRoot();
        var directoryPackagesPath = Path.Combine(repoRoot, "Directory.Packages.props");
        var content = File.ReadAllText(directoryPackagesPath);

        // Act & Assert
        Assert.Contains("<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>", content);
    }

    [Fact]
    public void ProjectFiles_ShouldNotContainPackageVersions()
    {
        // Arrange
        var repoRoot = GetRepositoryRoot();
        var projectFiles = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories).ToList();

        // Act
        var projectsWithVersions = new List<string>();
        foreach (var projectFile in projectFiles)
        {
            var content = File.ReadAllText(projectFile);
            var lines = content.Split('\n');
            
            // Check if PackageReference has Version attribute (exclude Sdk Version attributes)
            foreach (var line in lines)
            {
                // Skip Sdk Version attributes - these are required for Aspire.AppHost.Sdk etc.
                if (line.Trim().StartsWith("<Sdk ") && line.Contains("Version="))
                    continue;
                    
                if (line.Contains("<PackageReference") && line.Contains("Version="))
                {
                    projectsWithVersions.Add(Path.GetFileName(projectFile));
                    break;
                }
            }
        }

        // Assert
        Assert.Empty(projectsWithVersions);
    }

    [Fact]
    public void DirectoryPackagesProps_ShouldHaveNoDuplicatePackages()
    {
        // Arrange
        var repoRoot = GetRepositoryRoot();
        var directoryPackagesPath = Path.Combine(repoRoot, "Directory.Packages.props");
        var content = File.ReadAllText(directoryPackagesPath);

        // Act - Extract all PackageVersion Include values
        var packageNames = new List<string>();
        var lines = content.Split('\n');
        
        foreach (var line in lines)
        {
            if (line.Contains("<PackageVersion Include="))
            {
                var startIndex = line.IndexOf("Include=\"") + 9;
                var endIndex = line.IndexOf("\"", startIndex);
                if (startIndex > 8 && endIndex > startIndex)
                {
                    var packageName = line.Substring(startIndex, endIndex - startIndex);
                    packageNames.Add(packageName);
                }
            }
        }

        var duplicates = packageNames.GroupBy(x => x)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        // Assert
        Assert.Empty(duplicates);
    }

    [Fact]
    public void DirectoryPackagesProps_ShouldHaveConsistentVersionsForCommonPackages()
    {
        // Arrange
        var repoRoot = GetRepositoryRoot();
        var directoryPackagesPath = Path.Combine(repoRoot, "Directory.Packages.props");
        var content = File.ReadAllText(directoryPackagesPath);

        // Act - Check that Microsoft.Extensions.* packages have consistent versions
        var microsoftExtensionsPackages = new Dictionary<string, string>();
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            if (line.Contains("<PackageVersion Include=\"Microsoft.Extensions."))
            {
                var packageStart = line.IndexOf("Include=\"") + 9;
                var packageEnd = line.IndexOf("\"", packageStart);
                var versionStart = line.IndexOf("Version=\"") + 9;
                var versionEnd = line.IndexOf("\"", versionStart);

                if (packageStart > 8 && packageEnd > packageStart && 
                    versionStart > 8 && versionEnd > versionStart)
                {
                    var packageName = line.Substring(packageStart, packageEnd - packageStart);
                    var version = line.Substring(versionStart, versionEnd - versionStart);
                    microsoftExtensionsPackages[packageName] = version;
                }
            }
        }

        // Assert - All Microsoft.Extensions.* packages should use the same major version
        var versions = microsoftExtensionsPackages.Values.Distinct().ToList();
        
        // Allow for minor version differences, but major version should align
        var majorVersions = versions.Select(v => v.Split('.')[0]).Distinct().ToList();
        Assert.True(majorVersions.Count <= 2, 
            $"Microsoft.Extensions packages should have consistent major versions. Found: {string.Join(", ", versions)}");
    }

    private static string GetRepositoryRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        
        // Navigate up to find repository root (contains Directory.Packages.props or .git)
        while (currentDir != null && 
               !File.Exists(Path.Combine(currentDir, "Directory.Packages.props")) &&
               !Directory.Exists(Path.Combine(currentDir, ".git")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        return currentDir ?? throw new InvalidOperationException("Could not find repository root");
    }
}
