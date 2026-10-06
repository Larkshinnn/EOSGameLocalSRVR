using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer;
using AscNet.GameServer.Handlers;
using AscNet.Table.V2.share.fuben.trial;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace AscNet.Test;

internal partial class Program
{
    private static void ValidateTrialCompatibility()
    {
        PacketFactory.LoadPacketHandlers();
        const long uid = 48_903;
        Player player = CreateDrawCompatibilityPlayer(uid);
        Character character = CreateDrawCompatibilityCharacter(uid);
        using MongoCollectionOverride mongo = MongoCollectionOverride.InstallForDailySignInCompatibility(out _, out _, out _);
        using LoopbackSessionHarness harness = new(character, player, CreateDrawCompatibilityInventory(uid, []), "trial-loopback");
        TrialChallengeTable trial = TableReaderV2.Parse<TrialChallengeTable>().First();
        player.Trial.FinishedTrials.Add(trial.Id);

        InvokeRegisteredRequestHandler(nameof(TrialPassRewardRequest), harness.Session, 49_101,
            new TrialPassRewardRequest { TrialId = trial.Id });
        _ = ReadPushPayload<NotifyItemDataList>(harness, nameof(NotifyItemDataList), "Trial pass reward item push");
        _ = ReadPushPayload<NotifyEquipDataList>(harness, nameof(NotifyEquipDataList), "Trial pass reward equip push");
        TrialPassRewardResponse pass = ReadResponsePayload<TrialPassRewardResponse>(
            harness, 49_101, nameof(TrialPassRewardResponse), "Trial pass reward response");
        AssertEqual(0, pass.Code, "Trial pass reward code");
        AssertEqual(true, player.Trial.ClaimedTrials.Contains(trial.Id), "Trial pass reward persists claim");

        InvokeRegisteredRequestHandler(nameof(TrialPassRewardRequest), harness.Session, 49_102,
            new TrialPassRewardRequest { TrialId = trial.Id });
        AssertEqual(true, ReadResponsePayload<TrialPassRewardResponse>(harness, 49_102,
            nameof(TrialPassRewardResponse), "Trial pass replay response").Code != 0, "Trial pass replay rejects");
        AssertNoAvailablePacket(harness, "Trial pass replay");

        TrialTypeRewardTable typeReward = TableReaderV2.Parse<TrialTypeRewardTable>().First();
        player.Trial.FinishedTrials = TableReaderV2.Parse<TrialChallengeTable>()
            .Where(row => row.Type == typeReward.Type).Select(row => row.Id).ToList();
        InvokeRegisteredRequestHandler(nameof(TrialTypeRewardRequest), harness.Session, 49_103,
            new TrialTypeRewardRequest { Type = typeReward.Type });
        _ = ReadPushPayload<NotifyEquipDataList>(harness, nameof(NotifyEquipDataList), "Trial type reward equip push");
        AssertEqual(0, ReadResponsePayload<TrialTypeRewardResponse>(harness, 49_103,
            nameof(TrialTypeRewardResponse), "Trial type reward response").Code, "Trial type reward code");
        AssertEqual(true, player.Trial.ClaimedTypes.Contains(typeReward.Type), "Trial type reward persists claim");

        NotifyTrialData login = (NotifyTrialData)RequiredMethod(
            RequiredAscNetGameServerType("AscNet.GameServer.Handlers.TrialModule"), "BuildLoginData",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, [typeof(Player)]).Invoke(null, [player])!;
        AssertEqual(player.Trial.FinishedTrials.Count, login.FinishTrial.Count, "Trial login finished state");
        Player reloaded = BsonSerializer.Deserialize<Player>(player.ToBson());
        AssertEqual(player.Trial.ClaimedTrials.Count, reloaded.Trial.ClaimedTrials.Count, "Trial state survives relogin");
        Console.WriteLine("Trial compatibility: pass/type rewards, replay, login, and persistence passed.");
    }
}
