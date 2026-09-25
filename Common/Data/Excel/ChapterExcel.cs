using MikuSB.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.Data.Excel;

public sealed record ChapterStarAward(
    uint RequiredStars,
    IReadOnlyList<IReadOnlyList<uint>> Rewards);

public abstract class ChapterExcel : ExcelResource
{
    public uint ID { get; set; }
    public uint Difficult { get; set; }
    public List<uint> Level { get; set; } = [];

    [JsonProperty("LevelSort")]
    public JToken? LevelSortRaw { get; set; }

    [JsonIgnore]
    public IReadOnlyList<uint> OrderedLevels => ParseLevelSort(LevelSortRaw, Level);

    [JsonProperty("StarAward")]
    public JToken? StarAwardRaw { get; set; }

    [JsonIgnore]
    public IReadOnlyList<ChapterStarAward> StarAwards => ParseStarAwards(StarAwardRaw);

    public static ulong GetKey(bool isMain, uint difficult, uint chapterId) =>
        (isMain ? 1UL << 63 : 0) | ((ulong)chapterId << 8) | difficult;

    protected static IReadOnlyList<uint> ParseLevelSort(JToken? token, IReadOnlyList<uint> fallback)
    {
        if (token is not JArray array)
            return fallback;

        var levels = new List<uint>();
        AppendLevelSortValues(array, levels);
        return levels.Count > 0 ? levels : fallback;
    }

    private static void AppendLevelSortValues(JArray array, List<uint> levels)
    {
        foreach (var item in array)
        {
            if (item is JArray nested)
            {
                AppendLevelSortValues(nested, levels);
                continue;
            }

            var value = ReadUInt(item);
            if (value > 0)
                levels.Add(value);
        }
    }

    protected static IReadOnlyList<ChapterStarAward> ParseStarAwards(JToken? token)
    {
        if (token is not JObject awards)
            return [];

        return awards.Properties()
            .Select(property => new
            {
                IsValid = uint.TryParse(property.Name, out var requiredStars),
                RequiredStars = requiredStars,
                Rewards = ParseRewardRows(property.Value)
            })
            .Where(x => x.IsValid && x.RequiredStars > 0 && x.Rewards.Count > 0)
            .OrderBy(x => x.RequiredStars)
            .Select(x => new ChapterStarAward(x.RequiredStars, x.Rewards))
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<uint>> ParseRewardRows(JToken token)
    {
        if (token is not JArray rows)
            return [];

        return rows
            .OfType<JArray>()
            .Select(row => (IReadOnlyList<uint>)row.Select(ReadUInt).ToArray())
            .Where(row => row.Count >= 5)
            .ToArray();
    }

    private static uint ReadUInt(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Integer => token.Value<uint>(),
            JTokenType.Float => (uint)Math.Max(0, token.Value<decimal>()),
            JTokenType.String when uint.TryParse(token.Value<string>(), out var value) => value,
            _ => 0
        };
    }
}

[ResourceEntity("chapter/chapter_main.json")]
public sealed class MainChapterExcel : ChapterExcel
{
    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.ChapterData[GetKey(true, Difficult, ID)] = this;
    }
}

[ResourceEntity("chapter/chapter_branch.json")]
public sealed class BranchChapterExcel : ChapterExcel
{
    public override uint GetId() => ID;

    public override void Loaded()
    {
        GameData.ChapterData[GetKey(false, Difficult, ID)] = this;
    }
}
