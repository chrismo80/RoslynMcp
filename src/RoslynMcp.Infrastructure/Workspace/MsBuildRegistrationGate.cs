using Microsoft.Build.Locator;

namespace RoslynMcp.Infrastructure.Workspace;

internal sealed class MsBuildRegistrationGate : IMSBuildRegistrationGate
{
    private static readonly object s_msbuildLock = new();
    private static bool s_msbuildRegistered;

    public void EnsureRegistered()
    {
        if (s_msbuildRegistered)
        {
            return;
        }

        lock (s_msbuildLock)
        {
            if (s_msbuildRegistered)
            {
                return;
            }

            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }

            s_msbuildRegistered = true;
        }
    }
}
