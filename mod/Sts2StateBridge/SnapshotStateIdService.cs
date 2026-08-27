using System.Security.Cryptography;
using System.Text.Json;

namespace Sts2StateBridge;

internal static class SnapshotStateIdService
{
    public static string Compute(SnapshotPayload snapshot)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            snapshot.Phase,
            snapshot.ScreenType,
            snapshot.InRun,
            snapshot.InCombat,
            snapshot.Run,
            snapshot.Combat,
            snapshot.Interaction
        });
        byte[] hash = SHA256.HashData(canonical);
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
