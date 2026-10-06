using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.GameServer.Handlers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace AscNet.Test;

internal static partial class Program
{
    private static void ValidateWheelchairManualPurchaseCompatibility()
    {
        using MongoCollectionOverride storage = MongoCollectionOverride.InstallForDailySignInCompatibility(
            out RecordingMongoCollectionProxy<Player> players,
            out RecordingMongoCollectionProxy<Character> characters,
            out RecordingMongoCollectionProxy<Inventory> inventories);
        const long uid = 468420;
        Player player = CreateDrawCompatibilityPlayer(uid);
        player.PlayerData.Level = 80;
        Inventory inventory = CreateDrawCompatibilityInventory(uid, [new Item { Id = Inventory.FreeGem, Count = 499 }]);
        using LoopbackSessionHarness harness = new(CreateDrawCompatibilityCharacter(uid), player, inventory, "manual-purchase");
        harness.Session.stage = CreateLoginAccountCompatibilityStage(uid);
        harness.Session.stage.Stages[10010104] = new StageDatum { StageId = 10010104, Passed = true };
        int packetId = 468420;
        PurchaseResponse Buy(uint id, int count = 1)
        {
            int sequence = packetId++;
            InvokeRegisteredRequestHandler(nameof(PurchaseRequest), harness.Session, sequence,
                new PurchaseRequest { Id = id, Count = count, DiscountId = -1, UiTypeList = [11], Param = new Dictionary<string, object>() });
            return ReadResponsePayload<PurchaseResponse>(harness, sequence, nameof(PurchaseResponse), "manual purchase response", maxPacketsToRead: 32);
        }
        GetPurchaseListResponse List()
        {
            int sequence = packetId++;
            InvokeRegisteredRequestHandler(nameof(GetPurchaseListRequest), harness.Session, sequence, new GetPurchaseListRequest { UiTypeList = [11] });
            return ReadResponsePayload<GetPurchaseListResponse>(harness, sequence, nameof(GetPurchaseListResponse), "manual gifts");
        }
        System.Collections.IDictionary Gift(GetPurchaseListResponse list, int id) => list.PurchaseInfoList
            .Select(value => RequiredDynamicMap((object)value, "manual gift"))
            .Single(map => RequiredDynamicInteger(map, "Id", "manual gift") == id);
        foreach (int id in new[] { 90842, 90843, 90844, 90845 })
        {
            System.Collections.IDictionary gift = Gift(List(), id);
            AssertEqual(0, RequiredDynamicInteger(gift, "BuyTimes", "fresh gift"), "fresh player never inherits captured sold-out counts");
            AssertEqual(0, RequiredDynamicInteger(gift, "LastBuyTime", "fresh gift"), "fresh player never inherits captured purchase timestamps");
        }
        AssertEqual(20053031, Buy(90842, 0).Code, "zero purchase count rejected");
        AssertEqual(20053031, Buy(90842, -1).Code, "negative purchase count rejected");
        AssertEqual(20053005, Buy(90842, int.MaxValue).Code, "overflow-sized count rejected before mutation");
        AssertEqual(20053001, Buy(int.MaxValue).Code, "unknown package rejected");
        player.PlayerData.Level = 19;
        AssertEqual(20053030, Buy(90842).Code, "gift level gate enforced server-side");
        player.PlayerData.Level = 80;
        players.ThrowOnReplaceOne = true;
        AssertEqual(2, Buy(90842).Code, "failed intent save rejects without granting");
        players.ThrowOnReplaceOne = false;
        AssertEqual(0, inventory.Items.Count(item => item.Id == 30013), "failed intent leaves rewards absent");
        AssertEqual(0, player.PurchaseBuyTimes.Count, "failed intent leaves counts unchanged");
        AssertEqual(0, Buy(90842).Code, "fresh level gift claim succeeds");
        long granted = inventory.Items.Single(item => item.Id == 30013).Count;
        AssertEqual(10L, granted, "configured free gift awarded");
        AssertEqual(20053005, Buy(90842).Code, "repeat limited gift rejected");
        AssertEqual(granted, inventory.Items.Single(item => item.Id == 30013).Count, "repeat gift cannot regrant");
        AssertEqual(1, RequiredDynamicInteger(Gift(List(), 90842), "BuyTimes", "claimed gift"), "list reflects current player's purchase");
        AssertEqual(20012004, Buy(90846).Code, "insufficient virtual currency rejected");
        AssertEqual(499L, inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count, "insufficient currency is not clamped/debited");
        inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count = 500;
        inventories.ThrowOnReplaceOne = true;
        AssertEqual(2, Buy(90846).Code, "inventory persistence failure remains retryable");
        inventories.ThrowOnReplaceOne = false;
        AssertEqual(500L, inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count, "failed inventory save cannot debit memory");
        AssertEqual(0, inventory.Items.Count(item => item.Id == 50005), "failed inventory save cannot grant memory");
        characters.ThrowOnReplaceOne = true;
        AssertEqual(2, Buy(90846).Code, "later document failure keeps durable inventory receipt");
        characters.ThrowOnReplaceOne = false;
        AssertEqual(0L, inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count, "durable receipt includes exact debit");
        long partialGrant = inventory.Items.Single(item => item.Id == 50005).Count;
        AssertEqual(20053031, Buy(90847).Code, "outstanding purchase prevents unrelated overlapping debit");
        harness.Session.player = BsonSerializer.Deserialize<Player>(players.LastReplacement!.ToBson());
        AssertEqual(0, Buy(90846).Code, "reloaded pending purchase resumes despite already-debited balance");
        AssertEqual(partialGrant, inventory.Items.Single(item => item.Id == 50005).Count, "receipt retry does not duplicate rewards");
        AssertEqual(0L, inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count, "receipt retry does not debit twice");
        AssertEqual(1, harness.Session.player.PurchaseBuyTimes[90846], "retry finalizes count once");
        AssertEqual(true, harness.Session.player.PurchaseLastBuyTimes[90846] > 0, "purchase time survives finalization");
        foreach (uint id in new uint[] { 90843, 90844, 90845, 90847, 90848 })
        {
            inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count = 500;
            AssertEqual(0, Buy(id).Code, $"configured manual package {id} succeeds");
        }
        for (int bought = 1; bought < 5; bought++)
        {
            inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count = 500;
            AssertEqual(0, Buy(90846).Code, "booster purchases through configured limit");
        }
        inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count = 500;
        AssertEqual(20053005, Buy(90846).Code, "booster limit rejects next debit and grant");
        AssertEqual(500L, inventory.Items.Single(item => item.Id == Inventory.FreeGem).Count, "sold-out booster preserves balance");
        harness.Session.player = BsonSerializer.Deserialize<Player>(players.LastReplacement!.ToBson());
        AssertEqual(5, RequiredDynamicInteger(Gift(List(), 90846), "BuyTimes", "relogged booster"), "durably saved history is visible after relog");
        Player second = CreateDrawCompatibilityPlayer(uid + 1);
        harness.Session.player = second;
        AssertEqual(0, RequiredDynamicInteger(Gift(List(), 90842), "BuyTimes", "second player"), "purchase history isolated across players");
    }
}
