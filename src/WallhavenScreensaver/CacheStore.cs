using System.Text.RegularExpressions;

namespace WallhavenScreensaver;

internal sealed record CachedWallpaper(
    string Id,
    string PoolKey,
    string Path,
    DateTimeOffset AddedAtUtc);

internal sealed record DownloadReservation(
    string Id,
    string PoolKey,
    string PendingPath,
    string TempPath,
    string FinalPath);

internal sealed record CacheLease(
    string Id,
    string PoolKey,
    string Path);

internal sealed record CacheStats(
    int Files,
    long Bytes,
    int Pending,
    int Leased);

internal sealed class CacheStore
{
    private static readonly Regex NormalFilePattern = new(
        "^p_(?<pool>[0-9a-f]{16})__(?<id>[A-Za-z0-9]+)\\.(?<ext>jpg|jpeg|png|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LeaseFilePattern = new(
        "^\\.lease-[0-9a-f]{32}__(?<pool>[0-9a-f]{16})__(?<id>[A-Za-z0-9]+)\\.(?<ext>jpg|jpeg|png|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PendingFilePattern = new(
        "^\\.pending-(?<id>[A-Za-z0-9]+)\\.lock$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _root;
    private readonly int _maxFiles;
    private readonly long _maxBytes;

    public CacheStore(
        string? root = null,
        int maxFiles = 50,
        long maxBytes = 500L * 1024L * 1024L)
    {
        _root = root ?? AppPaths.CacheDirectory;
        _maxFiles = Math.Max(8, maxFiles);
        _maxBytes = Math.Max(100L * 1024L * 1024L, maxBytes);
        Directory.CreateDirectory(_root);
        CleanupStaleArtifacts();
    }

    public int CountPool(string poolKey) =>
        EnumeratePool(poolKey).Count;

    public IReadOnlyList<CachedWallpaper> EnumeratePool(string poolKey)
    {
        Directory.CreateDirectory(_root);

        return Directory
            .EnumerateFiles(_root)
            .Select(path => TryParseNormal(path, out var item) ? item : null)
            .Where(x => x is not null &&
                        string.Equals(
                            x.PoolKey,
                            poolKey,
                            StringComparison.OrdinalIgnoreCase))
            .Cast<CachedWallpaper>()
            .Where(x => File.Exists(x.Path))
            .ToList();
    }

    public DownloadReservation? TryReserveDownload(
        string id,
        string poolKey,
        string extension)
    {
        CleanupStaleArtifacts();

        if (ContainsIdAnywhere(id))
            return null;

        extension = NormalizeExtension(extension);
        var pendingPath = Path.Combine(_root, $".pending-{id}.lock");

        try
        {
            using var stream = new FileStream(
                pendingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            using var writer = new StreamWriter(stream);
            writer.Write(DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (IOException)
        {
            return null;
        }

        if (ContainsCachedOrLeasedId(id))
        {
            try { File.Delete(pendingPath); } catch { }
            return null;
        }

        var finalPath =
            Path.Combine(_root, $"p_{poolKey}__{id}{extension}");
        var tempPath =
            finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        return new DownloadReservation(
            id,
            poolKey,
            pendingPath,
            tempPath,
            finalPath);
    }

    public void CommitDownload(DownloadReservation reservation)
    {
        if (!File.Exists(reservation.TempPath))
            throw new FileNotFoundException(
                "Temporary wallpaper download is missing.",
                reservation.TempPath);

        File.Move(
            reservation.TempPath,
            reservation.FinalPath,
            overwrite: true);

        File.SetLastWriteTimeUtc(
            reservation.FinalPath,
            DateTime.UtcNow);

        try { File.Delete(reservation.PendingPath); } catch { }
        EnforceLimits();
    }

    public void CancelDownload(DownloadReservation reservation)
    {
        try { File.Delete(reservation.TempPath); } catch { }
        try { File.Delete(reservation.PendingPath); } catch { }
    }

    public CacheLease? TryLease(
        string poolKey,
        HistorySnapshot history,
        bool allowRecent)
    {
        CleanupStaleArtifacts();

        var available = EnumeratePool(poolKey)
            .Where(x => !history.IsSeenToday(x.Id))
            .ToList();

        if (!allowRecent)
            available = available.Where(x => !history.IsRecent(x.Id)).ToList();

        if (available.Count == 0)
            return null;

        IEnumerable<CachedWallpaper> ordered;

        if (allowRecent)
        {
            ordered = available
                .OrderBy(x =>
                    history.LastSeenUtc(x.Id) ??
                    DateTimeOffset.MinValue)
                .ThenBy(x => x.AddedAtUtc);
        }
        else
        {
            ordered = available.OrderBy(_ => Random.Shared.Next());
        }

        foreach (var item in ordered)
        {
            var extension = Path.GetExtension(item.Path);
            var leasePath = Path.Combine(
                _root,
                $".lease-{Guid.NewGuid():N}__{poolKey}__{item.Id}{extension}");

            try
            {
                File.Move(item.Path, leasePath);
                File.SetLastWriteTimeUtc(leasePath, DateTime.UtcNow);
                return new CacheLease(item.Id, poolKey, leasePath);
            }
            catch (IOException)
            {
                // Another process may have leased it first.
            }
        }

        return null;
    }

    public void CommitDisplay(CacheLease lease)
    {
        try { File.Delete(lease.Path); } catch { }
    }

    public void FailDisplay(CacheLease lease)
    {
        // A failed actual decode/display never consumes the ID in history. The
        // broken local file is removed so it cannot fail on every rotation.
        try { File.Delete(lease.Path); } catch { }
    }

    public void ClearAll()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            foreach (var file in Directory.EnumerateFiles(_root))
            {
                try { File.Delete(file); } catch { }
            }
        }

        Directory.CreateDirectory(_root);
    }

    public CacheStats Stats()
    {
        CleanupStaleArtifacts();

        var normal = Directory
            .EnumerateFiles(_root)
            .Where(path => NormalFilePattern.IsMatch(Path.GetFileName(path)))
            .Select(path => new FileInfo(path))
            .ToList();

        var pending = Directory
            .EnumerateFiles(_root, ".pending-*.lock")
            .Count();

        var leased = Directory
            .EnumerateFiles(_root, ".lease-*")
            .Count();

        return new CacheStats(
            normal.Count,
            normal.Sum(x => x.Length),
            pending,
            leased);
    }

    public void EnforceLimits()
    {
        CleanupStaleArtifacts();

        var files = Directory
            .EnumerateFiles(_root)
            .Where(path => NormalFilePattern.IsMatch(Path.GetFileName(path)))
            .Select(path => new FileInfo(path))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ToList();

        long bytes = 0;
        var count = 0;

        foreach (var file in files)
        {
            var keep =
                count < _maxFiles &&
                bytes + file.Length <= _maxBytes;

            if (keep)
            {
                count++;
                bytes += file.Length;
            }
            else
            {
                try { file.Delete(); } catch { }
            }
        }
    }

    private bool ContainsIdAnywhere(string id)
    {
        if (ContainsCachedOrLeasedId(id))
            return true;

        return Directory
            .EnumerateFiles(_root, ".pending-*.lock")
            .Select(Path.GetFileName)
            .Any(name =>
            {
                var match = PendingFilePattern.Match(name ?? "");
                return match.Success &&
                       string.Equals(
                           match.Groups["id"].Value,
                           id,
                           StringComparison.OrdinalIgnoreCase);
            });
    }

    private bool ContainsCachedOrLeasedId(string id)
    {
        foreach (var path in Directory.EnumerateFiles(_root))
        {
            var name = Path.GetFileName(path);

            var normal = NormalFilePattern.Match(name);
            if (normal.Success &&
                string.Equals(
                    normal.Groups["id"].Value,
                    id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var lease = LeaseFilePattern.Match(name);
            if (lease.Success &&
                string.Equals(
                    lease.Groups["id"].Value,
                    id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void CleanupStaleArtifacts()
    {
        Directory.CreateDirectory(_root);
        var now = DateTime.UtcNow;

        foreach (var file in Directory.EnumerateFiles(_root))
        {
            try
            {
                var info = new FileInfo(file);
                var name = info.Name;

                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
                    now - info.LastWriteTimeUtc > TimeSpan.FromMinutes(10))
                {
                    info.Delete();
                    continue;
                }

                if (PendingFilePattern.IsMatch(name) &&
                    now - info.LastWriteTimeUtc > TimeSpan.FromMinutes(5))
                {
                    info.Delete();
                    continue;
                }

                if (LeaseFilePattern.IsMatch(name) &&
                    now - info.LastWriteTimeUtc > TimeSpan.FromMinutes(10))
                {
                    info.Delete();
                    continue;
                }

                // Pre-redesign Windows cache files were named simply <id>.jpg.
                // They have no profile/policy identity and must not be reused by
                // the new pool-aware cache. Remove them during migration.
                var extension = info.Extension.ToLowerInvariant();
                if (!NormalFilePattern.IsMatch(name) &&
                    !LeaseFilePattern.IsMatch(name) &&
                    !PendingFilePattern.IsMatch(name) &&
                    extension is ".jpg" or ".jpeg" or ".png" or ".webp")
                {
                    info.Delete();
                }
            }
            catch { }
        }
    }

    private static bool TryParseNormal(
        string path,
        out CachedWallpaper item)
    {
        var match =
            NormalFilePattern.Match(Path.GetFileName(path));

        if (!match.Success)
        {
            item = null!;
            return false;
        }

        item = new CachedWallpaper(
            match.Groups["id"].Value,
            match.Groups["pool"].Value,
            path,
            new DateTimeOffset(
                File.GetLastWriteTimeUtc(path),
                TimeSpan.Zero));

        return true;
    }

    private static string NormalizeExtension(string extension)
    {
        extension = (extension ?? "").Trim().ToLowerInvariant();
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        return extension is ".jpg" or ".jpeg" or ".png" or ".webp"
            ? extension
            : ".jpg";
    }
}
