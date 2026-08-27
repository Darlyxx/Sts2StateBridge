using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sts2StateBridge;

internal static class CombatStateIdService
{
    public static string Compute(CombatSnapshotPayload combat)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(combat);
        byte[] hash = SHA256.HashData(canonical);
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
