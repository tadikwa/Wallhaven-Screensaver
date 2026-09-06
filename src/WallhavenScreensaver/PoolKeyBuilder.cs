using System.Drawing;
using System.Security.Cryptography;
using System.Text;

namespace WallhavenScreensaver;

internal static class PoolKeyBuilder
{
    public static string Build(AppSettings settings, Size target)
    {
        var targetKey = settings.DisplayAwareFiltering &&
                        target.Width > 0 &&
                        target.Height > 0
            ? $"{target.Width}x{target.Height}:{WallhavenQueryBuilder.ClosestWallhavenRatio(target)}"
            : "any";

        var canonical = string.Join(
            "\u001f",
            settings.Sorting,
            settings.Category,
            settings.Query.Trim(),
            settings.ContentFilter,
            $"content-policy-{ContentFilterPolicy.PolicyVersion}",
            settings.DisplayAwareFiltering,
            targetKey);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
