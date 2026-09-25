using System.Text.Json.Nodes;
using MikuSB.Configuration;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Quest;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Progression;

public static class ProgressionRewardService
{
    public static async ValueTask<JsonArray> GrantLevelClearRewardsAsync(
        PlayerInstance player,
        QuestLevelType levelType,
        uint levelId,
        bool isFirstClear,
        NtfSyncPlayer sync)
    {
        var rewards = new JsonArray();
        if (!IsProgressionMode())
            return rewards;

        var rows = ResolveRewardRows(levelType, levelId, isFirstClear).ToArray();
        if (rows.Length == 0)
            return rewards;

        return await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
    }

    private static bool IsProgressionMode() =>
        string.Equals(ConfigManager.Config.ServerOption.GameMode, "Progression", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<IReadOnlyList<uint>> ResolveRewardRows(
        QuestLevelType levelType,
        uint levelId,
        bool isFirstClear)
    {
        foreach (var entry in ConfigManager.Config.Progression.LevelClearRewards)
        {
            if (entry.LevelId != levelId)
                continue;

            if (entry.FirstClearOnly && !isFirstClear)
                continue;

            if (!MatchesLevelType(entry, levelType))
                continue;

            foreach (var reward in entry.Rewards)
            {
                if (reward.Length >= 5 && reward[4] > 0)
                    yield return reward;
            }
        }
    }

    private static bool MatchesLevelType(LevelClearReward entry, QuestLevelType levelType) =>
        Enum.TryParse<QuestLevelType>(entry.LevelType, ignoreCase: true, out var configuredType) &&
        configuredType == levelType;
}
