namespace AIDrawer;

internal static class ApplicationDataPaths
{
    private const string ProductFolderName = "AI Drawer";

    internal static string AppDataRoot { get; } = ResolveAppDataRoot();

    private static string ResolveAppDataRoot()
    {
#if DEBUG
        var testRoot = Environment.GetEnvironmentVariable("AI_DRAWER_TEST_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(testRoot) && Path.IsPathFullyQualified(testRoot))
        {
            return Path.Combine(Path.GetFullPath(testRoot), ProductFolderName);
        }
#endif

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName);
    }
}
