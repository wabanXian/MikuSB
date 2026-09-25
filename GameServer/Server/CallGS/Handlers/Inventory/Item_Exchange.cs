using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

[CallGSApi("Item_Exchange")]
public class Item_Exchange : CallGSHandler<JsonNode>
{
    private static readonly uint[] AllowedGdpl = [5, 21, 1, 4];
    private const uint MaxExchangeCount = 10;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        var responseRewards = new JsonArray();
        var sync = new NtfSyncPlayer();

        if (TryReadAllowedGdpln(request, out var requestedCount))
        {
            var templateId = (uint)GameResourceTemplateId.FromGdpl(
                AllowedGdpl[0],
                AllowedGdpl[1],
                AllowedGdpl[2],
                AllowedGdpl[3]);
            if (GameData.SuppliesData.TryGetValue(templateId, out var supplies))
            {
                var existing = context.Player.InventoryManager.GetNormalItemByTemplateId(templateId);
                var currentCount = existing?.ItemCount ?? 0;
                var targetCount = Math.Min(requestedCount, MaxExchangeCount);
                if (currentCount < targetCount)
                    existing = await context.Player.InventoryManager.AddSuppliesItem(supplies, targetCount - currentCount, sendPacket: false);

                existing ??= context.Player.InventoryManager.GetNormalItemByTemplateId(templateId);
                if (existing != null)
                    sync.Items.Add(existing.ToProto());

                responseRewards.Add(new JsonArray(5, 21, 1, 4, (int)targetCount));
                DatabaseHelper.SaveDatabaseType(context.Player.InventoryManager.InventoryData);
            }
        }

        var response = new JsonObject
        {
            ["tbAward"] = CloneArray(responseRewards),
            ["tbReward"] = CloneArray(responseRewards),
            ["tbRewards"] = CloneArray(responseRewards)
        };

        return CallGSResult.Ok(response, sync);
    }

    private static bool TryReadAllowedGdpln(JsonNode? request, out uint count)
    {
        count = 0;
        if (request is not JsonObject obj ||
            !obj.TryGetPropertyValue("tbGDPLN", out var node) ||
            node is not JsonArray gdpln ||
            gdpln.Count < 5)
        {
            return false;
        }

        var values = new uint[5];
        for (var i = 0; i < values.Length; i++)
        {
            if (gdpln[i] is not JsonValue value || !value.TryGetValue<uint>(out values[i]))
                return false;
        }

        if (!AllowedGdpl.SequenceEqual(values.Take(4)) || values[4] == 0 || values[4] > MaxExchangeCount)
            return false;

        count = values[4];
        return true;
    }

    private static JsonArray CloneArray(JsonArray source)
    {
        var clone = new JsonArray();
        foreach (var item in source)
            clone.Add(item?.DeepClone());
        return clone;
    }
}
