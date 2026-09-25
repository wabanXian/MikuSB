using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

[CallGSApi("Item_Exchange")]
public class Item_Exchange : CallGSHandler<JsonNode>
{
    private const uint MaxExchangeCount = 10;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        if (!TryReadGdpln(request, out var gdpln) || gdpln.Count == 0 || gdpln.Count > MaxExchangeCount)
            return CallGSResult.Error("error.BadParam");

        var exchange = FindExchange(gdpln);
        if (exchange == null || exchange.Cash.Count < 2)
            return CallGSResult.Error("error.BadParam");

        var moneyType = exchange.Cash[0];
        var unitPrice = exchange.Cash[1];
        var totalPrice = checked(unitPrice * gdpln.Count);
        var sid = AttrIds.Currency.GetSid(moneyType);
        var moneyAttr = context.Player.Attributes.GetOrCreate(AttrIds.Currency.GroupId, sid);
        if (moneyAttr.Val < totalPrice)
            return CallGSResult.Error("tip.Mall_Cost_Not_Enough");

        var sync = new NtfSyncPlayer();
        moneyAttr.Val -= totalPrice;
        context.Player.Attributes.SyncTo(sync, moneyAttr);
        if (moneyType == AttrIds.Currency.Money)
        {
            foreach (var (key, value) in context.Player.BuildMoneySync())
                sync.Money[key] = value;
        }

        var reward = new JsonArray((int)gdpln.Genre, (int)gdpln.Detail, (int)gdpln.Particular, (int)gdpln.Level, (int)gdpln.Count);
        var templateId = (uint)GameResourceTemplateId.FromGdpl(gdpln.Genre, gdpln.Detail, gdpln.Particular, gdpln.Level);
        if (GameData.SuppliesData.TryGetValue(templateId, out var supplies))
        {
            var item = await context.Player.InventoryManager.AddSuppliesItem(supplies, gdpln.Count, sendPacket: false);
            if (item != null)
                sync.Items.Add(item.ToProto());
        }
        else
        {
            return CallGSResult.Error("error.BadParam", sync);
        }

        DatabaseHelper.SaveDatabaseType(context.Player.Data);
        DatabaseHelper.SaveDatabaseType(context.Player.InventoryManager.InventoryData);

        var awards = new JsonArray(reward.DeepClone());
        var response = new JsonObject
        {
            ["tbGDPLN"] = ToJsonArray(gdpln),
            ["tbAward"] = awards.DeepClone(),
            ["tbAwards"] = awards.DeepClone(),
            ["tbReward"] = awards.DeepClone(),
            ["tbRewards"] = awards.DeepClone(),
            ["nMoneyType"] = (int)moneyType,
            ["nCost"] = (int)totalPrice
        };

        return CallGSResult.Ok(response, sync);
    }

    private static bool TryReadGdpln(JsonNode? request, out ExchangeGdpln gdpln)
    {
        gdpln = default;
        if (request is not JsonObject obj ||
            !obj.TryGetPropertyValue("tbGDPLN", out var node) ||
            node is not JsonArray array ||
            array.Count < 5)
        {
            return false;
        }

        var values = new uint[5];
        for (var i = 0; i < values.Length; i++)
        {
            if (array[i] is not JsonValue value || !value.TryGetValue<uint>(out values[i]))
                return false;
        }

        gdpln = new ExchangeGdpln(values[0], values[1], values[2], values[3], values[4]);
        return true;
    }

    private static JsonArray ToJsonArray(ExchangeGdpln gdpln) =>
        new((int)gdpln.Genre, (int)gdpln.Detail, (int)gdpln.Particular, (int)gdpln.Level, (int)gdpln.Count);

    private static ExchangeRow? FindExchange(ExchangeGdpln gdpln) =>
        LoadExchangeRows().FirstOrDefault(x =>
            x.Genre == gdpln.Genre &&
            x.Detail == gdpln.Detail &&
            x.Particular == gdpln.Particular &&
            x.Level == gdpln.Level);

    private static IReadOnlyList<ExchangeRow> LoadExchangeRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "item", "exchange.json");
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ExchangeRow>>(File.ReadAllText(path), CallGSJson.Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private readonly record struct ExchangeGdpln(uint Genre, uint Detail, uint Particular, uint Level, uint Count);

    private sealed class ExchangeRow
    {
        [JsonPropertyName("G")]
        public uint Genre { get; set; }

        [JsonPropertyName("D")]
        public uint Detail { get; set; }

        [JsonPropertyName("P")]
        public uint Particular { get; set; }

        [JsonPropertyName("L")]
        public uint Level { get; set; }

        public List<uint> Cash { get; set; } = [];
    }
}
