using System.Text.RegularExpressions;

namespace WallhavenScreensaver;

internal sealed record ContentFilterDecision(
    bool Allowed,
    string Category,
    int Score,
    IReadOnlyList<string> BlockedTags,
    IReadOnlyList<string> Reasons);

internal static class ContentFilterPolicy
{
    public const int PolicyVersion = 5;
    public const int MaxMetadataChecksPerRefill = 16;
    private const int StrictScoreThreshold = 4;

    private static readonly string[] QueryExclusions =
    [
        "cleavage", "lingerie", "underwear", "panties", "bikini",
        "swimsuit", "ecchi", "schoolgirl", "loli"
    ];

    private static readonly HashSet<string> ReducedHardConcepts = NormalizedSet(
        "cleavage", "lingerie", "underwear", "panties", "panty", "bikini",
        "swimsuit", "bra", "sideboob", "underboob", "cameltoe", "ecchi",
        "schoolgirl", "school girl", "school uniform", "loli", "thong",
        "garter", "garter belt", "porn", "pornography", "pornstar",
        "porn star", "adult model", "adult content", "onlyfans", "tushy",
        "playboy", "playmate", "nude", "nudity", "naked", "erotic",
        "erotica", "sexual", "sex", "fetish", "bdsm", "ass", "butt",
        "buttocks", "booty", "boob", "boobs", "big boobs", "breast",
        "breasts", "big breasts", "large breasts", "huge breasts", "busty");

    private static readonly HashSet<string> StrictHardConcepts =
        new(ReducedHardConcepts.Concat(NormalizedSet(
            "upskirt", "skirt lift", "panty shot", "pantyshot", "no panties",
            "topless", "nipple", "nipples", "areola", "areolas",
            "bare breasts", "see through", "see through clothes",
            "transparent clothes", "transparent clothing", "open shirt",
            "crotch", "spread legs", "legs spread", "micro bikini",
            "microkini", "micro skirt", "sexualized", "sexualised",
            "seductive", "sensual gaze", "lustful look", "provocative")),
            StringComparer.Ordinal);

    private static readonly HashSet<string> FemaleSubjectWords = new(
    [
        "woman", "women", "girl", "girls", "female", "females",
        "schoolgirl", "schoolgirls"
    ], StringComparer.Ordinal);

    private static readonly HashSet<string> MaleSubjectWords = new(
    [
        "man", "men", "male", "males", "boy", "boys", "guy", "guys",
        "gentleman", "gentlemen", "father", "dad"
    ], StringComparer.Ordinal);

    private static readonly HashSet<string> StrictHumanLikeConcepts = NormalizedSet(
        "samurai", "warrior", "warriors", "knight", "knights", "soldier",
        "soldiers", "person", "human", "human character", "character portrait",
        "model", "celebrity", "singer", "actor", "actress", "cosplay",
        "cosplayer", "witch", "wizard", "maid", "nurse");

    private static readonly HashSet<string> AnimeSafeNonHumanConcepts = NormalizedSet(
        "mecha", "robot", "robots", "vehicle", "vehicles", "car", "cars",
        "motorcycle", "motorcycles", "aircraft", "airplane", "airplanes",
        "spacecraft", "spaceship", "spaceships", "ship", "ships", "train",
        "trains", "locomotive", "landscape", "scenery", "architecture",
        "building", "buildings", "abstract", "minimalism", "pattern",
        "texture", "typography", "logo", "planet", "planets", "space art",
        "animal", "animals", "cat", "cats", "dog", "dogs", "forest",
        "mountain", "mountains", "ocean", "cityscape", "environment");

    private static readonly Dictionary<string, int> StrictWeightedSignals =
        new(StringComparer.Ordinal)
        {
            [Normalize("stockings")] = 3,
            [Normalize("fishnet")] = 3,
            [Normalize("fishnet stockings")] = 3,
            [Normalize("pantyhose")] = 3,
            [Normalize("thighhighs")] = 3,
            [Normalize("thigh highs")] = 3,
            [Normalize("thigh high socks")] = 3,
            [Normalize("thighs")] = 2,
            [Normalize("miniskirt")] = 3,
            [Normalize("short shorts")] = 2,
            [Normalize("hot pants")] = 3,
            [Normalize("bodysuit")] = 3,
            [Normalize("leotard")] = 3,
            [Normalize("bunny suit")] = 3,
            [Normalize("bunny girl")] = 3,
            [Normalize("bare shoulders")] = 2,
            [Normalize("bare midriff")] = 2,
            [Normalize("midriff")] = 2,
            [Normalize("crop top")] = 2,
            [Normalize("armpits")] = 2,
            [Normalize("barefoot")] = 2,
            [Normalize("feet")] = 2,
            [Normalize("toes")] = 2,
            [Normalize("thigh strap")] = 2,
            [Normalize("high heels")] = 1,
            [Normalize("kneeling")] = 2,
            [Normalize("squatting")] = 2,
            [Normalize("bent legs")] = 1,
            [Normalize("legs up")] = 2,
            [Normalize("rear view")] = 2,
            [Normalize("looking back")] = 1,
            [Normalize("looking over shoulder")] = 2,
            [Normalize("parted lips")] = 1,
            [Normalize("blushing")] = 1,
            [Normalize("bed")] = 2,
            [Normalize("bedroom")] = 2,
            [Normalize("pillow")] = 1,
            [Normalize("maid outfit")] = 2
        };

    public static string Description(ContentFilterMode mode) => mode switch
    {
        ContentFilterMode.Standard =>
            "SFW Wallhaven uniquement, sans exclusion locale supplémentaire.",
        ContentFilterMode.Reduced =>
            "Écarte les tags adultes/suggestifs à fort signal.",
        ContentFilterMode.Strict =>
            "Très conservateur : sujets féminins et Anime/People ambigus sont rejetés.",
        _ => ""
    };

    public static string Compose(string userQuery, ContentFilterMode mode)
    {
        var query = (userQuery ?? "").Trim();

        if (Regex.IsMatch(query, "^id:[0-9]+$", RegexOptions.IgnoreCase))
            return query;

        if (mode == ContentFilterMode.Standard)
            return query;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
            parts.Add(query);

        parts.AddRange(QueryExclusions.Select(x => "-" + x));
        return string.Join(" ", parts);
    }

    public static bool RequiresMetadataInspection(ContentFilterMode mode) =>
        mode != ContentFilterMode.Standard;

    public static ContentFilterDecision Evaluate(
        string wallhavenCategory,
        IEnumerable<string> tags,
        ContentFilterMode mode)
    {
        var category = Normalize(wallhavenCategory);
        if (string.IsNullOrWhiteSpace(category))
            category = "unknown";

        if (mode == ContentFilterMode.Standard)
            return new(true, category, 0, [], []);

        var normalizedTags = tags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => (Original: x, Normalized: Normalize(x)))
            .ToList();

        var reducedBlocked =
            MatchingOriginalTags(normalizedTags, ReducedHardConcepts);

        if (mode == ContentFilterMode.Reduced)
        {
            return reducedBlocked.Count == 0
                ? new(true, category, 0, [], [])
                : new(
                    false, category, 100, reducedBlocked,
                    reducedBlocked.Select(x => "hard:" + x).ToList());
        }

        var hardBlocked =
            MatchingOriginalTags(normalizedTags, StrictHardConcepts);

        if (hardBlocked.Count > 0)
        {
            return new(
                false, category, 100, hardBlocked,
                hardBlocked.Select(x => "hard:" + x).ToList());
        }

        var femaleTags = normalizedTags
            .Where(x => IsFemaleFocusedTag(x.Normalized))
            .Select(x => x.Original)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Normalize)
            .ToList();

        if (femaleTags.Count > 0)
        {
            return new(
                false, category, 10, femaleTags,
                new[] { "strict:female_subject" }
                    .Concat(femaleTags.Select(x => "subject:" + x))
                    .ToList());
        }

        var maleSignal =
            normalizedTags.Any(x => HasWord(x.Normalized, MaleSubjectWords)) ||
            normalizedTags.Any(x =>
                x.Normalized is "anime boys" or "anime boy" or
                    "male character" or "male characters");

        var safeAnimeNonHuman = normalizedTags.Any(x =>
            MatchesAnyConcept(x.Normalized, AnimeSafeNonHumanConcepts));

        switch (category)
        {
            case "people":
                if (!maleSignal)
                    return new(false, category, 10, [], ["strict:people_unclassified"]);
                break;

            case "anime":
                if (!maleSignal && !safeAnimeNonHuman)
                    return new(false, category, 10, [], ["strict:anime_unclassified"]);
                break;

            case "general":
                var ambiguousHuman = normalizedTags.Any(x =>
                    MatchesAnyConcept(x.Normalized, StrictHumanLikeConcepts));
                if (ambiguousHuman && !maleSignal)
                {
                    return new(
                        false, category, 10, [],
                        ["strict:general_human_unclassified"]);
                }
                break;

            default:
                return new(
                    false, category, 10, [],
                    ["strict:unknown_category"]);
        }

        var scoreBreakdown = new List<(string Tag, int Points)>();
        foreach (var (original, normalized) in normalizedTags)
        {
            foreach (var (concept, points) in StrictWeightedSignals)
            {
                if (ContainsPhrase(normalized, concept))
                    scoreBreakdown.Add((original, points));
            }
        }

        var score = scoreBreakdown.Sum(x => x.Points);
        if (score >= StrictScoreThreshold)
        {
            var cues = scoreBreakdown
                .GroupBy(x => Normalize(x.Tag))
                .Select(g => g.OrderByDescending(x => x.Points).First())
                .OrderByDescending(x => x.Points)
                .ToList();

            return new(
                false, category, score,
                cues.Select(x => x.Tag).ToList(),
                new[] { $"strict:risk_score={score}" }
                    .Concat(cues.Select(x => $"cue:{x.Tag}+{x.Points}"))
                    .ToList());
        }

        return new(true, category, score, [], []);
    }

    private static List<string> MatchingOriginalTags(
        IEnumerable<(string Original, string Normalized)> tags,
        HashSet<string> concepts) =>
        tags
            .Where(x => MatchesAnyConcept(x.Normalized, concepts))
            .Select(x => x.Original)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Normalize)
            .ToList();

    private static bool MatchesAnyConcept(
        string normalizedTag,
        HashSet<string> concepts) =>
        concepts.Any(concept => ContainsPhrase(normalizedTag, concept));

    private static bool ContainsPhrase(string value, string phrase) =>
        value == phrase ||
        value.StartsWith(phrase + " ", StringComparison.Ordinal) ||
        value.EndsWith(" " + phrase, StringComparison.Ordinal) ||
        value.Contains(" " + phrase + " ", StringComparison.Ordinal);

    private static bool IsFemaleFocusedTag(string normalized)
    {
        if (HasWord(normalized, FemaleSubjectWords))
            return true;

        return normalized is
            "anime girl" or "anime girls" or
            "video game girl" or "video game girls" or
            "female character" or "female characters" or
            "fantasy girl" or "fox girl" or "cat girl" or
            "bunny girl" or "horse girls" or "girls with guns";
    }

    private static bool HasWord(string normalized, HashSet<string> words) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(words.Contains);

    private static HashSet<string> NormalizedSet(params string[] values) =>
        new(values.Select(Normalize), StringComparer.Ordinal);

    private static string Normalize(string value) =>
        Regex.Replace(
                (value ?? "")
                    .ToLowerInvariant()
                    .Replace('_', ' ')
                    .Replace('-', ' '),
                "\\s+",
                " ")
            .Trim();
}
