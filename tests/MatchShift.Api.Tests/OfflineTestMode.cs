using System.Runtime.CompilerServices;

namespace MatchShift.Api.Tests;

internal static class OfflineTestMode
{
    /// <summary>
    /// API tests must never call Azure, even on a machine where FOUNDRY_PROJECT_ENDPOINT is set.
    /// Environment configuration is read by WebApplication.CreateBuilder, so this applies to every test host.
    /// </summary>
    [ModuleInitializer]
    internal static void ForceOfflineTemplate()
    {
        Environment.SetEnvironmentVariable("AgentWorkflow__Mode", "OfflineTemplate");
        Environment.SetEnvironmentVariable("Narration__Provider", "template");
        Environment.SetEnvironmentVariable("Foundry__Enabled", "false");
    }
}
