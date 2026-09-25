using System.Numerics;
using System.Text.Json.Nodes;
using MikuSB.Configuration;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Progression;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Quest;

public enum QuestLevelType
{
    Chapter,
    Daily,
    Role
}

public readonly record struct QuestSettlementResult(JsonArray Rewards, NtfSyncPlayer Sync);
public readonly record struct ChapterStarAwardResult(JsonObject Response, NtfSyncPlayer Sync);
public readonly record struct QuestCompletionResult(int LevelCount, NtfSyncPlayer Sync);

public class QuestManager(PlayerInstance player) : BasePlayerManager(player)
{
    private const uint LevelStateGroupId = AttrIds.Quest.LevelStateGid;
    private const uint LevelPassGroupId = AttrIds.Quest.LevelPassGid;
    private const uint SettlementSeedGroupId = AttrIds.Quest.SettlementSeedGid;
    private const uint ChapterStarAwardGroupId = AttrIds.Quest.ChapterStarAwardGid;
    private const uint ChapterStarAwardMaskVersionSid = AttrIds.Quest.ChapterStarAwardMaskVersionSid;
    private const uint ChapterStarAwardMaskVersion = 1;
    private const uint LevelStarMask = 0b111;
    private const int MaxChapterStarAwardIndex = 31;
    private const uint LegacyUnlockedLevelPassTime = 1_700_000_000;
    private static readonly Logger Logger = new("Quest");
    private readonly SemaphoreSlim settlementLock = new(1, 1);
    private bool allLevelsCompletedForTesting = player.Data.CompleteAllQuestLevels;

    public void RemoveLegacyLevelUnlocks()
    {
        var levelIds = GameData.ChapterLevelData.Keys
            .Concat(GameData.DailyLevelData.Keys)
            .Concat(GameData.RoleLevelData.Keys)
            .Distinct();

        foreach (var levelId in levelIds)
        {
            var passAttr = Player.Attributes.Get(LevelPassGroupId, levelId);
            if (passAttr?.Val != LegacyUnlockedLevelPassTime)
                continue;

            Player.Attributes.Remove(LevelPassGroupId, levelId);

            Player.Attributes.Remove(LevelStateGroupId, levelId);
        }
    }

    public void MigrateChapterStarAwardMasks()
    {
        var versionAttr = Player.Attributes.Get(
            ChapterStarAwardGroupId,
            ChapterStarAwardMaskVersionSid);
        if (versionAttr?.Val >= ChapterStarAwardMaskVersion)
            return;

        foreach (var attr in Player.Attributes.All.Where(x =>
                     x.Gid == ChapterStarAwardGroupId && x.Sid != ChapterStarAwardMaskVersionSid))
        {
            attr.Val <<= 1;
        }

        if (versionAttr == null)
            versionAttr = Player.Attributes.GetOrCreate(
                ChapterStarAwardGroupId,
                ChapterStarAwardMaskVersionSid);

        versionAttr.Val = ChapterStarAwardMaskVersion;
        DatabaseHelper.SaveDatabaseType(Player.Data);
    }

    public async ValueTask<QuestCompletionResult> SetAllLevelsForTestingAsync(bool completed)
    {
        await settlementLock.WaitAsync();
        try
        {
            var sync = new NtfSyncPlayer();
            var levelConfigs = GetAllLevelConfigs().ToArray();
            var levelCount = levelConfigs
                .Select(x => x.LevelConfig.ID)
                .Distinct()
                .Count();

            allLevelsCompletedForTesting = completed;
            Player.Data.CompleteAllQuestLevels = completed;
            if (completed)
            {
                SyncTestingStateTo(sync);
            }
            else
            {
                foreach (var levelId in levelConfigs.Select(x => x.LevelConfig.ID).Distinct())
                {
                    SyncStoredLevelState(LevelStateGroupId, levelId, sync);
                    SyncStoredLevelState(LevelPassGroupId, levelId, sync);
                }
            }

            DatabaseHelper.SaveCompleteAllQuestLevels(Player.Data);
            Logger.Info($"All quest levels updated for testing. uid={Player.Uid} completed={completed} levelCount={levelCount}");
            return new QuestCompletionResult(levelCount, sync);
        }
        finally
        {
            settlementLock.Release();
        }
    }

    public void SyncTestingStateTo(NtfSyncPlayer sync)
    {
        if (!allLevelsCompletedForTesting)
            return;

        var states = new Dictionary<uint, uint>();
        foreach (var (levelType, levelConfig) in GetAllLevelConfigs())
            states[levelConfig.ID] = states.GetValueOrDefault(levelConfig.ID) |
                                     GetCompletedState(levelType, levelConfig);

        foreach (var (levelId, state) in states)
        {
            Player.Attributes.SyncTo(sync, LevelStateGroupId, levelId, state);
            Player.Attributes.SyncTo(sync, LevelPassGroupId, levelId, 1);
        }
    }

    private void SyncStoredLevelState(uint groupId, uint levelId, NtfSyncPlayer sync)
    {
        Player.Attributes.SyncTo(sync, groupId, levelId, Player.Attributes.GetValue(groupId, levelId));
    }

    public async ValueTask<QuestSettlementResult?> SettleLevelAsync(
        QuestLevelType levelType,
        uint levelId,
        int starMask,
        uint seed)
    {
        if (seed == 0 || starMask < 0 || starMask > LevelStarMask ||
            !Player.IsLevelSession(levelType, levelId, seed))
            return null;

        var levelConfig = ResolveLevelConfig(levelType, levelId);
        if (levelConfig == null)
            return null;

        await settlementLock.WaitAsync();
        try
        {
            var levelPass = Player.Attributes.GetOrCreate(LevelPassGroupId, levelId);
            var settlementSeed = Player.Attributes.GetOrCreate(SettlementSeedGroupId, levelId);
            var levelState = Player.Attributes.GetOrCreate(LevelStateGroupId, levelId);
            var isFirstClear = GetPassCount(levelId) == 0 &&
                               (levelType != QuestLevelType.Daily || (GetLevelState(levelType, levelId) & (1u << 8)) == 0);

            if (settlementSeed.Val == seed)
            {
                var duplicateSync = Player.RewardManager.BuildFullSync();
                var duplicateRewards = Player.RewardManager.ResolveLevelRewards(levelConfig, levelPass.Val <= 1, seed);
                return new QuestSettlementResult(duplicateRewards, duplicateSync);
            }

            var sync = new NtfSyncPlayer();
            await Player.RewardManager.GrantLevelRewardsAsync(levelConfig, isFirstClear, seed, sync);
            var progressionRewards = await ProgressionRewardService.GrantLevelClearRewardsAsync(
                Player,
                levelType,
                levelId,
                isFirstClear,
                sync);

            levelState.Val |= (uint)starMask & LevelStarMask;
            if (levelType == QuestLevelType.Daily)
                levelState.Val |= 1u << 8;
            levelPass.Val = levelPass.Val == uint.MaxValue ? uint.MaxValue : levelPass.Val + 1;
            Player.Attributes.SyncTo(sync, levelState);
            Player.Attributes.SyncTo(sync, levelPass);
            settlementSeed.Val = seed;
            Player.Attributes.SyncTo(sync, settlementSeed);

            Logger.Info($"Level settlement saved. uid={Player.Uid} levelType={levelType} levelId={levelId} " +
                        $"starMask={starMask} stateVal={levelState.Val} passVal={levelPass.Val}");

            DatabaseHelper.SaveDatabaseType(Player.Data);
            DatabaseHelper.SaveDatabaseType(Player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(Player.CharacterManager.CharacterData);
            var rewards = Player.RewardManager.ResolveLevelRewards(levelConfig, isFirstClear, seed);
            if (progressionRewards.Count > 0)
                rewards.Add(progressionRewards);

            return new QuestSettlementResult(rewards, sync);
        }
        finally
        {
            settlementLock.Release();
        }
    }

    public bool IsPlotLevel(uint levelId) =>
        GameData.ChapterLevelData.TryGetValue(levelId, out var levelConfig) && levelConfig.IsPlot();

    public async ValueTask<QuestSettlementResult?> SettlePlotLevelAsync(uint levelId)
    {
        if (!GameData.ChapterLevelData.TryGetValue(levelId, out var levelConfig) || !levelConfig.IsPlot())
            return null;

        await settlementLock.WaitAsync();
        try
        {
            var levelState = Player.Attributes.GetOrCreate(LevelStateGroupId, levelId);
            if ((GetLevelState(QuestLevelType.Chapter, levelId) & (1u << 8)) != 0)
                return new QuestSettlementResult(new JsonArray(), Player.RewardManager.BuildFullSync());

            var levelPass = Player.Attributes.GetOrCreate(LevelPassGroupId, levelId);
            var sync = new NtfSyncPlayer();
            await Player.RewardManager.GrantLevelRewardsAsync(levelConfig, true, levelId, sync);
            var progressionRewards = await ProgressionRewardService.GrantLevelClearRewardsAsync(
                Player,
                QuestLevelType.Chapter,
                levelId,
                true,
                sync);

            levelState.Val |= 1u << 8;
            if (levelPass.Val == 0)
                levelPass.Val = 1;

            Player.Attributes.SyncTo(sync, levelState);
            Player.Attributes.SyncTo(sync, levelPass);

            Logger.Info($"Plot settlement saved. uid={Player.Uid} levelId={levelId} " +
                        $"stateVal={levelState.Val} passVal={levelPass.Val}");

            DatabaseHelper.SaveDatabaseType(Player.Data);
            DatabaseHelper.SaveDatabaseType(Player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(Player.CharacterManager.CharacterData);
            var rewards = Player.RewardManager.ResolveLevelRewards(levelConfig, true, levelId);
            if (progressionRewards.Count > 0)
                rewards.Add(progressionRewards);

            return new QuestSettlementResult(rewards, sync);
        }
        finally
        {
            settlementLock.Release();
        }
    }

    private static ILevelRewardConfig? ResolveLevelConfig(QuestLevelType levelType, uint levelId) =>
        levelType switch
        {
            QuestLevelType.Chapter => GameData.ChapterLevelData.GetValueOrDefault(levelId),
            QuestLevelType.Daily => GameData.DailyLevelData.GetValueOrDefault(levelId),
            QuestLevelType.Role => GameData.RoleLevelData.GetValueOrDefault(levelId),
            _ => null
        };

    public bool CanEnterLevel(QuestLevelType levelType, uint levelId)
    {
        if (ResolveLevelConfig(levelType, levelId) == null)
            return false;

        if (allLevelsCompletedForTesting)
            return true;

        if (UseProgressionMode() &&
            levelType == QuestLevelType.Chapter &&
            ConfigManager.Config.Progression.RestrictMainChapterProgression)
        {
            return CanEnterProgressionChapterLevel(levelId);
        }

        var predecessorIds = GetLevelConfigs(levelType)
            .Where(level => level.NextId() == levelId)
            .Select(level => level.ID)
            .ToArray();

        return predecessorIds.Length == 0 || predecessorIds.Any(id => GetPassCount(id) > 0);
    }

    private static bool UseProgressionMode() =>
        string.Equals(ConfigManager.Config.ServerOption.GameMode, "Progression", StringComparison.OrdinalIgnoreCase);

    private bool CanEnterProgressionChapterLevel(uint levelId)
    {
        var limit = Math.Max(1, ConfigManager.Config.Progression.MainChapterLevelLimit);
        var orderedLevels = GetProgressionMainChapterLevelIds()
            .Take(limit)
            .ToArray();
        var index = Array.IndexOf(orderedLevels, levelId);
        if (index < 0)
            return false;

        if (index == 0)
            return true;

        return GetPassCount(orderedLevels[index - 1]) > 0;
    }

    private static IEnumerable<uint> GetProgressionMainChapterLevelIds()
    {
        var seen = new HashSet<uint>();
        foreach (var chapter in GameData.ChapterData.Values
                     .OfType<MainChapterExcel>()
                     .OrderBy(x => x.Difficult)
                     .ThenBy(x => x.ID))
        {
            foreach (var levelId in chapter.Level)
            {
                if (seen.Add(levelId) && GameData.ChapterLevelData.ContainsKey(levelId))
                    yield return levelId;
            }
        }
    }

    public bool SyncGuideLevelPassData(JsonNode? payload)
    {
        if (payload is not JsonObject root || root["tbData"] is not JsonArray rows)
            return false;

        if (allLevelsCompletedForTesting)
            return true;

        var updates = new List<(uint LevelId, uint PassCount)>();
        foreach (var rowNode in rows)
        {
            if (rowNode is not JsonArray row || row.Count < 2 ||
                !TryGetUInt(row[0], out var levelId) ||
                !TryGetUInt(row[1], out var passCount) ||
                levelId == 0 || !GameData.ChapterLevelData.ContainsKey(levelId))
                return false;

            updates.Add((levelId, passCount));
        }

        var changed = false;
        foreach (var (levelId, passCount) in updates)
        {
            var attr = Player.Attributes.GetOrCreate(LevelPassGroupId, levelId);
            if (passCount <= attr.Val)
                continue;

            attr.Val = passCount;
            changed = true;
        }

        if (changed)
            DatabaseHelper.SaveDatabaseType(Player.Data);

        return true;
    }

    public async ValueTask<ChapterStarAwardResult?> ClaimChapterStarAwardsAsync(
        bool isMain,
        uint difficult,
        uint chapterId,
        int awardIndex)
    {
        if (!GameData.ChapterData.TryGetValue(ChapterExcel.GetKey(isMain, difficult, chapterId), out var chapter))
            return null;

        await settlementLock.WaitAsync();
        try
        {
            var awards = chapter.StarAwards;
            var claimedAttr = Player.Attributes.GetOrCreate(
                ChapterStarAwardGroupId,
                (chapterId << 8) | difficult);
            var starCount = GetChapterStarCount(chapter);
            var selected = new List<(int Index, ChapterStarAward Award)>();

            if (awardIndex == -1)
            {
                for (var i = 0; i < awards.Count && i < MaxChapterStarAwardIndex; i++)
                {
                    var index = i + 1;
                    var claimBit = 1u << index;
                    if ((claimedAttr.Val & claimBit) == 0 && starCount >= awards[i].RequiredStars)
                        selected.Add((index, awards[i]));
                }
            }
            else
            {
                if (awardIndex < 1 || awardIndex > awards.Count || awardIndex > MaxChapterStarAwardIndex)
                    return null;

                var index = awardIndex - 1;
                var claimBit = 1u << awardIndex;
                if ((claimedAttr.Val & claimBit) != 0 || starCount < awards[index].RequiredStars)
                    return null;

                selected.Add((awardIndex, awards[index]));
            }

            if (selected.Count == 0)
                return null;

            var sync = new NtfSyncPlayer();
            var rewardRows = selected.SelectMany(x => x.Award.Rewards).ToArray();
            var grantedRewards = await Player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync);
            foreach (var (index, _) in selected)
                claimedAttr.Val |= 1u << index;

            Player.Attributes.SyncTo(sync, claimedAttr);
            DatabaseHelper.SaveDatabaseType(Player.Data);
            DatabaseHelper.SaveDatabaseType(Player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(Player.CharacterManager.CharacterData);

            return new ChapterStarAwardResult(
                new JsonObject { ["tbAward"] = grantedRewards },
                sync);
        }
        finally
        {
            settlementLock.Release();
        }
    }

    private IEnumerable<ILevelRewardConfig> GetLevelConfigs(QuestLevelType levelType) =>
        levelType switch
        {
            QuestLevelType.Chapter => GameData.ChapterLevelData.Values,
            QuestLevelType.Daily => GameData.DailyLevelData.Values,
            QuestLevelType.Role => GameData.RoleLevelData.Values,
            _ => []
        };

    private IEnumerable<(QuestLevelType LevelType, ILevelRewardConfig LevelConfig)> GetAllLevelConfigs()
    {
        foreach (var levelType in Enum.GetValues<QuestLevelType>())
        {
            foreach (var levelConfig in GetLevelConfigs(levelType))
                yield return (levelType, levelConfig);
        }
    }

    private static uint GetCompletedState(QuestLevelType levelType, ILevelRewardConfig levelConfig)
    {
        if (levelType == QuestLevelType.Chapter && levelConfig.IsPlot())
            return 1u << 8;

        return levelType == QuestLevelType.Daily
            ? LevelStarMask | (1u << 8)
            : LevelStarMask;
    }

    private uint GetPassCount(uint levelId) =>
        allLevelsCompletedForTesting ? 1 : Player.Attributes.GetValue(LevelPassGroupId, levelId);

    private uint GetLevelState(QuestLevelType levelType, uint levelId)
    {
        if (allLevelsCompletedForTesting && ResolveLevelConfig(levelType, levelId) is { } levelConfig)
            return GetCompletedState(levelType, levelConfig);

        return Player.Attributes.GetValue(LevelStateGroupId, levelId);
    }

    private uint GetChapterStarCount(ChapterExcel chapter)
    {
        ulong total = 0;
        foreach (var levelId in chapter.Level)
        {
            var value = GetLevelState(QuestLevelType.Chapter, levelId);
            total += (uint)BitOperations.PopCount(value & LevelStarMask);
        }

        return (uint)Math.Min(uint.MaxValue, total);
    }

    private static bool TryGetUInt(JsonNode? node, out uint value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var parsed))
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }
}
