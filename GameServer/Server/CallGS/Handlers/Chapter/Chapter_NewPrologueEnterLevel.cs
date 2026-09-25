using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Quest;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_NewPrologueEnterLevel")]
public class Chapter_NewPrologueEnterLevel : CallGSHandler<JsonNode>
{
    private static readonly Random Random = new();

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        var levelId = ReadUInt(request, "nID", "nId", "nLevelID", "nLevelId");
        var teamId = ReadUInt(request, "nTeamID", "nTeamId", "nTeam", "nFormationID", "nFormationId");

        if (levelId == 0 ||
            !GameData.ChapterLevelData.ContainsKey(levelId) ||
            !context.Player.QuestManager.CanEnterLevel(QuestLevelType.Chapter, levelId))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (teamId == 0)
            teamId = 1;

        var seed = (uint)Random.Next(1, 1000000000);
        context.Player.BeginLevelSession(QuestLevelType.Chapter, levelId, seed, teamId);
        return Task.FromResult(CallGSResult.Ok($"{{\"nSeed\":{seed}}}"));
    }

    private static uint ReadUInt(JsonNode? root, params string[] keys)
    {
        if (root is JsonValue value && value.TryGetValue<uint>(out var rootValue))
            return rootValue;

        if (root is not JsonObject obj)
            return 0;

        foreach (var key in keys)
        {
            if (obj[key] is JsonValue node && node.TryGetValue<uint>(out var result))
                return result;
        }

        return 0;
    }
}

