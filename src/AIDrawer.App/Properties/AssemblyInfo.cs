using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AIDrawer.App.Tests")]
[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The WinUI window owns process-lifetime resources and releases them through its explicit ExitApplication path.",
    Scope = "type",
    Target = "~T:AIDrawer.MainWindow")]
[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The XAML page releases its coordinator through PersistAndDisposeWorkspaceAsync during the owning window's shutdown path.",
    Scope = "type",
    Target = "~T:AIDrawer.MainPage")]
