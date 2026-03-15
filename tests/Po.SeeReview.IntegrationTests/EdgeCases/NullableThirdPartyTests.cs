using Xunit;

namespace Po.SeeReview.IntegrationTests.EdgeCases;

/// <summary>
/// Validates nullable reference type enforcement across all projects (FR-003).
/// Only assertions with real failure conditions are kept here; design notes belong in docs/.
/// </summary>
public class NullableThirdPartyTests
{
    [Fact]
    public void ProjectFiles_ShouldEnableNullableReferenceTypes()
    {
        // Arrange
        var repoRoot = GetRepositoryRoot();

        // If Directory.Build.props at the root already sets <Nullable>enable</Nullable>,
        // all projects inherit it — no need to repeat it per-project.
        var buildPropsPath = Path.Combine(repoRoot, "Directory.Build.props");
        var buildPropsContent = File.Exists(buildPropsPath) ? File.ReadAllText(buildPropsPath) : string.Empty;
        var globalNullableEnabled = buildPropsContent.Contains("<Nullable>enable</Nullable>");

        if (globalNullableEnabled)
        {
            // All projects are covered via inheritance — test passes
            return;
        }

        // Fallback: check each project individually
        var projectFiles = Directory.GetFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .ToList();

        var projectsWithoutNullable = projectFiles
            .Where(f => !File.ReadAllText(f).Contains("<Nullable>enable</Nullable>"))
            .Select(Path.GetFileName)
            .ToList();

        // Assert
        Assert.Empty(projectsWithoutNullable);
    }

    [Fact]
    public void SourceCode_ShouldHaveNullChecksForThirdPartyResponses()
    {
        // Arrange
        var repoRoot = GetRepositoryRoot();
        var infrastructureFiles = Directory.GetFiles(Path.Combine(repoRoot, "src", "Po.SeeReview.Infrastructure"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .ToList();

        // Act
        var filesWithNullChecks = 0;
        
        foreach (var sourceFile in infrastructureFiles)
        {
            var content = File.ReadAllText(sourceFile);
            
            // Look for defensive null checks: if (...== null), ArgumentNullException, ?.
            if (content.Contains("== null") || 
                content.Contains("is null") ||
                content.Contains("ArgumentNullException") ||
                content.Contains("?."))
            {
                filesWithNullChecks++;
            }
        }

        // Assert
        var percentage = infrastructureFiles.Any() ? (filesWithNullChecks * 100.0 / infrastructureFiles.Count) : 0;
        Assert.True(percentage > 50, 
            $"At least 50% of infrastructure files should have null checks for third-party responses. Found: {percentage:F1}%");
    }

    private static string GetRepositoryRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        
        while (currentDir != null && 
               !File.Exists(Path.Combine(currentDir, "Directory.Packages.props")) &&
               !Directory.Exists(Path.Combine(currentDir, ".git")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        return currentDir ?? throw new InvalidOperationException("Could not find repository root");
    }
}
