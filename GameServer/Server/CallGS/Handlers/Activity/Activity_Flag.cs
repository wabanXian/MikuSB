using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_Flag")]
public class Activity_Flag : CallGSHandler<JsonNode>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        var player = context.Player;
        var sync = new NtfSyncPlayer();

        if (request is JsonObject root)
        {
            var id = ReadUInt(root, "nId", "nID", "nActivityId", "nActId", "nFlagId");
            if (id > 0)
            {
                var flag = ReadBool(root, "bFlag", "bOpen", "bRead", "bValue") ?? true;
                var attr = player.Attributes.Set(AttrIds.Activity.GroupId, id, flag ? 1u : 0u);
                player.Attributes.SyncTo(sync, attr);
                DatabaseHelper.SaveDatabaseType(player.Data);
            }
        }

        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }

    private static uint ReadUInt(JsonObject root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (root[key] is JsonValue value && value.TryGetValue<uint>(out var result))
                return result;
        }

        return 0;
    }

    private static bool? ReadBool(JsonObject root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (root[key] is not JsonValue value)
                continue;

            if (value.TryGetValue<bool>(out var boolResult))
                return boolResult;

            if (value.TryGetValue<uint>(out var intResult))
                return intResult != 0;
        }

        return null;
    }
}
