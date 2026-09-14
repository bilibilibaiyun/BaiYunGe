using WixToolset.BootstrapperApplicationApi;

namespace BaiYunGe.Bootstrapper;

internal static class Program
{
    private static int Main()
    {
        ManagedBootstrapperApplication.Run(new InstallerBootstrapper());
        return 0;
    }
}
