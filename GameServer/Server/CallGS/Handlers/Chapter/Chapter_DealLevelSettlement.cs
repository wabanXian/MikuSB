using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Quest;
using MikuSB.GameServer.Game.BossPvp;
using MikuSB.Proto;
using MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;
using MikuSB.GameServer.Server.CallGS.Handlers.Tower;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Util;
using MikuSB.GameServer.Server.CallGS;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_DealLevelSettlement")]
public class Chapter_DealLevelSettlement : CallGSHandler<DealLevelSettlementParam>
{
    private static readonly Logger Logger = new("Chapter");
    private const bool NewJourneyIsMainChapter = true;
    private const uint NewJourneyDifficult = 1;
    private const uint NewJourneyInternalChapterId = 25;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, DealLevelSettlementParam request)
    {
        var (payload, extraSync) = await BuildSettlementPayloadAsync(context.Player, request.SCmd, request.TbParam);
        var response = new JsonObject
        {
            ["sCmd"] = request.SCmd ?? "Chapter_LevelSettlement",
            ["tbParam"] = payload
        };

        return CallGSResult.Ok(response, extraSync);
    }

    private static async ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> BuildSettlementPayloadAsync(
        PlayerInstance player,
        string? sCmd,
        JsonNode? tbParam)
    {
        if (string.Equals(sCmd, "Chapter_LevelSettlement", StringComparison.Ordinal))
            return await HandleLevelSettlementAsync(player, QuestLevelType.Chapter, tbParam);

        if (string.Equals(sCmd, "Daily_LevelSettlement", StringComparison.Ordinal) ||
            string.Equals(sCmd, "Role_LevelSettlement", StringComparison.Ordinal))
        {
            var levelType = string.Equals(sCmd, "Daily_LevelSettlement", StringComparison.Ordinal)
                ? QuestLevelType.Daily
                : QuestLevelType.Role;
            return await HandleLevelSettlementAsync(player, levelType, tbParam);
        }

        if (string.Equals(sCmd, "Chapter_NewPrologueSettlement", StringComparison.Ordinal))
        {
            return await HandleNewPrologueSettlementAsync(player, tbParam);
        }

        if (string.Equals(sCmd, "BossPvpLogic_LevelSettlement", StringComparison.Ordinal))
        {
            var normalized = NormalizeBossPvpSettlement(tbParam);
            var (response, sync) = BossPvpService.HandleSettlement(player, normalized);
            return (response, sync);
        }

        if (string.Equals(sCmd, "BossPvpLogic_LevelFail", StringComparison.Ordinal))
        {
            var (response, sync) = BossPvpService.HandleFail(player, tbParam);
            return (response, sync);
        }

        if (string.Equals(sCmd, "TowerLevel_LevelSettlement", StringComparison.Ordinal))
        {
            var (response, sync) = TowerLevel_LevelSettlement.HandleSettlement(player, tbParam);
            return (response, sync);
        }

        if (string.Equals(sCmd, "TowerEventChapter_LevelSettlement", StringComparison.Ordinal))
        {
            var (response, sync) = TowerEventChapter_LevelSettlement.HandleSettlement(player, tbParam);
            return (response, sync);
        }

        if (string.Equals(sCmd, "VirCaptureTower_LevelSettlement", StringComparison.Ordinal))
        {
            var (response, sync) = VirCaptureTower_LevelSettlement.HandleSettlement(player, tbParam);
            return (response, sync);
        }

        if (string.Equals(sCmd, "DreamCard_LevelSettlement", StringComparison.Ordinal))
        {
            var (response, sync) = DreamCard_LevelSettlement.HandleSettlement(player, tbParam);
            return (response, sync);
        }

        return (tbParam?.DeepClone() ?? new JsonObject(), null);
    }

    internal static async ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleNewPrologueSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var request = tbParam?.Deserialize<NewPrologueSettlementParam>(CallGSJson.Options);
        if (request == null || request.LevelId == 0)
        {
            Logger.Error($"Invalid plot settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        var result = await player.QuestManager.SettleNewPrologueLevelAsync(request.LevelId);
        if (result == null)
        {
            Logger.Error($"Rejected plot settlement: levelId={request.LevelId}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        var response = new JsonObject();
        if (tbParam is JsonObject source && source.TryGetPropertyValue("bWaitServer", out var bWaitServer))
            response["bWaitServer"] = bWaitServer?.DeepClone();

        var showAwards = FlattenRewardCategories(result.Value.Rewards);
        var sync = result.Value.Sync;
        if (IsNewJourneyLevel(request.LevelId))
        {
            var chapterAward = await player.QuestManager.ClaimChapterStarAwardsAsync(
                NewJourneyIsMainChapter,
                NewJourneyDifficult,
                NewJourneyInternalChapterId,
                -1);
            if (chapterAward != null)
            {
                sync.MergeFrom(chapterAward.Value.Sync);
                AppendAwardResponse(showAwards, chapterAward.Value.Response);
            }
        }

        response["tbShowAward"] = showAwards;
        return (response, sync);
    }

    private static (JsonNode Payload, NtfSyncPlayer? Sync) BuildEmptyNewPrologueResponse(JsonNode? tbParam)
    {
        var response = new JsonObject();
        if (tbParam is JsonObject source && source.TryGetPropertyValue("bWaitServer", out var bWaitServer))
            response["bWaitServer"] = bWaitServer?.DeepClone();
        response["tbShowAward"] = new JsonArray();
        return (response, null);
    }

    private static JsonArray FlattenRewardCategories(JsonArray categories)
    {
        var rewards = new JsonArray();
        foreach (var category in categories)
        {
            if (category is not JsonArray categoryRewards)
                continue;

            foreach (var reward in categoryRewards)
                rewards.Add(reward?.DeepClone());
        }

        return rewards;
    }

    private static void AppendAwardResponse(JsonArray rewards, JsonObject response)
    {
        if (!response.TryGetPropertyValue("tbAward", out var awardNode) || awardNode is not JsonArray awards)
            return;

        foreach (var award in awards)
            rewards.Add(award?.DeepClone());
    }

    private static bool IsNewJourneyLevel(uint levelId) =>
        GameData.ChapterData.TryGetValue(
            ChapterExcel.GetKey(NewJourneyIsMainChapter, NewJourneyDifficult, NewJourneyInternalChapterId),
            out var chapter) &&
        chapter.Level.Contains(levelId);

    private static async ValueTask<(JsonNode Payload, NtfSyncPlayer Sync)> HandleLevelSettlementAsync(
        PlayerInstance player,
        QuestLevelType levelType,
        JsonNode? tbParam)
    {
        var req = tbParam?.Deserialize<LevelSettlementParam>(CallGSJson.Options);
        if (req == null || req.LevelId == 0)
        {
            Logger.Error($"Invalid level settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        if (levelType == QuestLevelType.Chapter && player.QuestManager.IsPlotLevel(req.LevelId))
        {
            var plotResult = await player.QuestManager.SettlePlotLevelAsync(req.LevelId);
            if (plotResult == null)
            {
                Logger.Error($"Rejected plot level settlement: levelId={req.LevelId}");
                return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
            }

            return (plotResult.Value.Rewards, plotResult.Value.Sync);
        }

        var result = await player.QuestManager.SettleLevelAsync(levelType, req.LevelId, req.StarMask, req.Seed);
        if (result == null)
        {
            Logger.Error($"Rejected level settlement: type={levelType} levelId={req.LevelId} seed={req.Seed}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, new NtfSyncPlayer());
        }

        return (result.Value.Rewards, result.Value.Sync);
    }

    private static JsonNode? NormalizeBossPvpSettlement(JsonNode? tbParam)
    {
        if (tbParam is not JsonObject obj)
            return tbParam;

        var clone = obj.DeepClone() as JsonObject ?? obj;
        if (clone.TryGetPropertyValue("ResidueTime", out var residueNode) &&
            residueNode is JsonValue residueValue &&
            residueValue.TryGetValue<double>(out var residueTime))
        {
            clone["ResidueTime"] = (int)Math.Max(0, Math.Round(residueTime, MidpointRounding.AwayFromZero));
        }

        return clone;
    }
}

public sealed class DealLevelSettlementParam
{
    [JsonPropertyName("sCmd")]
    public string? SCmd { get; set; }

    [JsonPropertyName("tbParam")]
    public JsonNode? TbParam { get; set; }
}

public sealed class LevelSettlementParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }

    [JsonPropertyName("nStar")]
    public int StarMask { get; set; }

    [JsonPropertyName("nSeed")]
    public uint Seed { get; set; }
}

public sealed class NewPrologueSettlementParam
{
    [JsonPropertyName("nID")]
    public uint LevelId { get; set; }
}
