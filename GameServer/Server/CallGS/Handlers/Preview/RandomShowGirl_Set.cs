using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Preview;

[CallGSApi("RandomShowGirl_Set")]
public class RandomShowGirl_Set : CallGSHandler<JsonNode>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        var player = context.Player;
        var sync = new NtfSyncPlayer();
        var changed = false;

        if (request is JsonObject root)
        {
            var slot = ReadUInt(root, "nIndex", "nIdx", "nSlot", "nPos", "nID", "nId");
            var characterId = ReadUInt(root, "nCardID", "nCardId", "nGirlID", "nGirlId", "nRoleID", "nRoleId", "nItemID", "nItemId");
            var enabled = ReadBool(root, "bFlag", "bOpen", "bEnable", "bEnabled") ?? true;

            if (slot > 0 && slot <= AttrIds.Preview.RandomGirlEndSid)
            {
                var sid = AttrIds.Preview.RandomGirlStartSid + slot - 1;
                if (characterId == 0 || player.CharacterManager.GetCharacterByGUID(characterId) != null)
                {
                    changed |= SetAndSync(player, sync, AttrIds.Preview.RandomShowGid, sid, characterId);
                    changed |= SetAndSync(player, sync, AttrIds.Preview.RandomShowFormGid, sid, enabled ? 1u : 0u);
                }
            }

            changed |= ApplyArray(player, sync, root, "tbGirl");
            changed |= ApplyArray(player, sync, root, "tbGirls");
            changed |= ApplyArray(player, sync, root, "tbCard");
            changed |= ApplyArray(player, sync, root, "tbCards");
        }

        if (!changed)
            EnsureStarterRandomShowState(player, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }

    private static bool ApplyArray(Game.Player.PlayerInstance player, NtfSyncPlayer sync, JsonObject root, string key)
    {
        if (root[key] is not JsonArray array)
            return false;

        var changed = false;
        for (var i = 0; i < array.Count && i <= AttrIds.Preview.RandomGirlEndSid; i++)
        {
            var characterId = ReadArrayEntry(array[i]);
            if (characterId == 0 || player.CharacterManager.GetCharacterByGUID(characterId) == null)
                continue;

            var sid = AttrIds.Preview.RandomGirlStartSid + (uint)i;
            changed |= SetAndSync(player, sync, AttrIds.Preview.RandomShowGid, sid, characterId);
            changed |= SetAndSync(player, sync, AttrIds.Preview.RandomShowFormGid, sid, 1);
        }

        return changed;
    }

    private static void EnsureStarterRandomShowState(Game.Player.PlayerInstance player, NtfSyncPlayer sync)
    {
        var i = 0u;
        foreach (var character in player.CharacterManager.CharacterData.Characters.Take(3))
        {
            var sid = AttrIds.Preview.RandomGirlStartSid + i;
            SetAndSync(player, sync, AttrIds.Preview.RandomShowGid, sid, character.Guid);
            SetAndSync(player, sync, AttrIds.Preview.RandomShowFormGid, sid, 1);
            i++;
        }
    }

    private static bool SetAndSync(Game.Player.PlayerInstance player, NtfSyncPlayer sync, uint gid, uint sid, uint value)
    {
        var existing = player.Attributes.Get(gid, sid);
        if (existing?.Val == value)
        {
            player.Attributes.SyncTo(sync, gid, sid, value);
            return false;
        }

        var attr = player.Attributes.Set(gid, sid, value);
        player.Attributes.SyncTo(sync, attr);
        return true;
    }

    private static uint ReadArrayEntry(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<uint>(out var id))
            return id;

        if (node is JsonObject obj)
            return ReadUInt(obj, "nCardID", "nCardId", "nGirlID", "nGirlId", "nRoleID", "nRoleId", "nItemID", "nItemId", "nID", "nId");

        return 0;
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
