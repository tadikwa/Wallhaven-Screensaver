using System.Drawing;

namespace WallhavenScreensaver;

internal static class WallhavenQueryBuilder
{
    public static Uri Build(AppSettings settings, Size target, int? pageOverride = null)
    {
        var sorting = settings.Sorting switch
        {
            WallhavenSorting.Trending => "toplist",
            WallhavenSorting.Popular => "toplist",
            WallhavenSorting.Newest => "date_added",
            _ => "random"
        };

        var categories = settings.Category switch
        {
            WallhavenCategory.General => "100",
            WallhavenCategory.Anime => "010",
            WallhavenCategory.People => "001",
            _ => "111"
        };

        var parameters = new Dictionary<string, string>
        {
            ["purity"] = "100",
            ["categories"] = categories,
            ["sorting"] = sorting,
            ["order"] = "desc"
        };

        if (settings.Sorting == WallhavenSorting.Trending)
            parameters["topRange"] = "1d";
        else if (settings.Sorting == WallhavenSorting.Popular)
            parameters["topRange"] = "1M";

        // Non-random modes otherwise keep returning the same leading API page.
        // Rotating among the first pages expands the candidate set while
        // preserving the selected ranking mode.
        if (settings.Sorting != WallhavenSorting.Random)
            parameters["page"] = (pageOverride ?? Random.Shared.Next(1, 13)).ToString();

        if (settings.DisplayAwareFiltering && target.Width > 0 && target.Height > 0)
        {
            parameters["atleast"] = $"{target.Width}x{target.Height}";
            parameters["ratios"] = ClosestWallhavenRatio(target);
        }

        var encoded = string.Join("&", parameters.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return new Uri($"https://wallhaven.cc/api/v1/search?{encoded}");
    }

    public static string ClosestWallhavenRatio(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return "16x9";

        var target = (double)size.Width / size.Height;
        var ratios = new (string Name, double Value)[]
        {
            ("48x9", 48d / 9d),
            ("32x9", 32d / 9d),
            ("21x9", 21d / 9d),
            ("16x9", 16d / 9d),
            ("16x10", 16d / 10d),
            ("3x2", 3d / 2d),
            ("4x3", 4d / 3d),
            ("5x4", 5d / 4d),
            ("1x1", 1d),
            ("10x16", 10d / 16d),
            ("9x16", 9d / 16d),
            ("9x18", 9d / 18d)
        };

        return ratios.OrderBy(x => Math.Abs(x.Value - target)).First().Name;
    }
}
