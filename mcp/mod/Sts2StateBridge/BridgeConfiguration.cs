using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2StateBridge;

internal static class BridgeConfiguration
{
    private const string FileName = "Sts2StateBridge.config.json";

    public static bool WriteEnabled { get; private set; }

    public static void Load()
    {
        WriteEnabled = false;

        try
        {
            string? assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                Log.Error("[Sts2StateBridge] cannot resolve Mod directory; write actions remain disabled");
                return;
            }

            string path = Path.Combine(assemblyDirectory, FileName);
            if (!File.Exists(path))
            {
                Log.Info($"[Sts2StateBridge] {FileName} not found; write actions remain disabled");
                return;
            }

            BridgeConfigurationPayload? payload = JsonSerializer.Deserialize<BridgeConfigurationPayload>(
                File.ReadAllText(path));
            WriteEnabled = payload?.WriteEnabled == true;
            Log.Info($"[Sts2StateBridge] write actions enabled: {WriteEnabled}");
        }
        catch (Exception exception)
        {
            WriteEnabled = false;
            Log.Error($"[Sts2StateBridge] failed to read {FileName}; write actions remain disabled: {exception}");
        }
    }
}

internal sealed class BridgeConfigurationPayload
{
    [JsonPropertyName("write_enabled")]
    public bool WriteEnabled { get; init; }
}
