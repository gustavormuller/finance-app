namespace Finance.Api.Tests;

/// <summary>
/// Where the tests find source and configuration files: the checkout the test binaries
/// were built from, located by walking up to the solution file.
/// </summary>
internal static class TestPaths
{
    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FinanceApp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("FinanceApp.slnx not found above the test binaries.");
    }
}
