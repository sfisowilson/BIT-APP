using System.Globalization;
using System.Runtime.CompilerServices;

namespace Afrobotics.Bit.Tests;

/// <summary>
/// Mirrors the invariant-culture fix in dotnet-api/Program.cs. The test host never runs
/// Program.cs, so without this, any test that exercises code shelling out to ffmpeg with
/// interpolated decimal args (e.g. VideoChunkingService) fails on a comma-decimal locale.
/// </summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
