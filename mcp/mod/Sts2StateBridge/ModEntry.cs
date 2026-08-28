using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2StateBridge;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        Log.Info("[Sts2StateBridge] initialized");
        BridgeConfiguration.Load();
        GameThread.Initialize();
        BridgeServer.Start();
    }
}
