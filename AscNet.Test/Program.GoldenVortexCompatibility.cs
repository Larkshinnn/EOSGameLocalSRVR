using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer;
using AscNet.Table.V2.share.fuben.explore;
using MessagePack;

namespace AscNet.Test;

internal partial class Program
{
    private static void ValidateGoldenVortexCompatibility()
    {
        PacketFactory.LoadPacketHandlers();
        AssertEqual("ExploreFinishNodeRequestHandler", GetRegisteredRequestHandlerMethod(nameof(ExploreFinishNodeRequest)).Name, "ExploreFinishNodeRequest registered dispatch");
        AssertEqual("ExploreGetRewardRequestHandler", GetRegisteredRequestHandlerMethod(nameof(ExploreGetRewardRequest)).Name, "ExploreGetRewardRequest registered dispatch");
        AssertEqual(4, TableReaderV2.Parse<ExploreChapterTable>().Count, "authoritative Explore chapters");
        AssertEqual(97, TableReaderV2.Parse<ExploreNodeTable>().Count, "authoritative Explore nodes");
        AssertEqual(16, TableReaderV2.Parse<ExploreBuffItemTable>().Count, "authoritative Explore buffs");

        NotifyExploreData fresh = new();
        NotifyExploreData progressed = new()
        {
            ChapterDatas = [new ExploreChapterData
            {
                Id = 1,
                EnduranceInfos = [new ExploreEnduranceInfo { Id = 123, Use = 4 }],
                FinishNodes = [1, 2],
                UnlockEvents = [302010301]
            }]
        };
        AssertEqual(0, MessagePackSerializer.Deserialize<NotifyExploreData>(MessagePackSerializer.Serialize(fresh)).ChapterDatas.Count, "fresh Explore login shape");
        NotifyExploreData roundTrip = MessagePackSerializer.Deserialize<NotifyExploreData>(MessagePackSerializer.Serialize(progressed));
        AssertEqual(2, roundTrip.ChapterDatas[0].FinishNodes.Count, "progressed Explore login shape");
        AssertEqual(302010301, roundTrip.ChapterDatas[0].UnlockEvents[0], "authoritative unlock event wire value");
        const long uid = 98_701;
        Character character = CreateDrawCompatibilityCharacter(uid);
        character.Characters.Add(CreateLoginAccountCompatibilityCharacter(1021001, fashionId: 3021001));
        Player player = CreateDrawCompatibilityPlayer(uid);
        using MongoCollectionOverride mongo = MongoCollectionOverride.InstallForDailySignInCompatibility(out _, out _, out _);
        using LoopbackSessionHarness harness = new(character, player, CreateDrawCompatibilityInventory(uid, []), "golden-vortex-loopback");
        harness.Session.stage = CreateLoginAccountCompatibilityStage(uid);
        InvokeRequestHandler(harness, nameof(ExploreFinishNodeRequest), 98_711, new ExploreFinishNodeRequest { Id = 1 });
        AssertEqual(0, ReadResponsePayload<ExploreFinishNodeResponse>(harness.ReadPacket("story success"), nameof(ExploreFinishNodeResponse)).Code, "story node success");
        InvokeRequestHandler(harness, nameof(PreFightRequest), 98_714, new PreFightRequest
        {
            PreFightData = new()
            {
                StageId = 30020101,
                ChallengeCount = 1,
                CardIds = [1021001, 0, 0],
                RobotIds = []
            }
        });
        PreFightResponse preFight = ReadResponsePayload<PreFightResponse>(
            harness.ReadPacket("battle with empty team slots"),
            nameof(PreFightResponse));
        if (preFight.Code == 20046008)
            throw new InvalidDataException("Golden Vortex empty team slots consumed endurance");
        InvokeRequestHandler(harness, nameof(ExploreFinishNodeRequest), 98_712, new ExploreFinishNodeRequest { Id = 1 });
        AssertEqual(20046007, ReadResponsePayload<ExploreFinishNodeResponse>(harness.ReadPacket("story duplicate"), nameof(ExploreFinishNodeResponse)).Code, "story duplicate error");
        InvokeRequestHandler(harness, nameof(ExploreFinishNodeRequest), 98_713, new ExploreFinishNodeRequest { Id = 3 });
        AssertEqual(20046006, ReadResponsePayload<ExploreFinishNodeResponse>(harness.ReadPacket("story prerequisite"), nameof(ExploreFinishNodeResponse)).Code, "story prerequisite error");
        Console.WriteLine("Golden Vortex compatibility: registered dispatch, authoritative tables, typed login shapes, story success/duplicate/prerequisite passed.");
    }
}
