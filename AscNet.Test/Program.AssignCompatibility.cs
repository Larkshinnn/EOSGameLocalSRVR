using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.GameServer;
using AscNet.GameServer.Game;
using AscNet.GameServer.Handlers;
using AscNet.Common.Util;
using AscNet.Table.V2.share.equip;
using MessagePack;
using MongoDB.Bson;

namespace AscNet.Test;

internal partial class Program
{
    private static void ValidateAssignCompatibility()
    {
        PacketFactory.LoadPacketHandlers();

        AssertEqual("AssignGetDataRequestHandler", GetRegisteredRequestHandlerMethod("AssignGetDataRequest").Name, "AssignGetDataRequest registered handler method");
        AssertEqual("AssignSetTeamRequestHandler", GetRegisteredRequestHandlerMethod("AssignSetTeamRequest").Name, "AssignSetTeamRequest registered handler method");
        AssertEqual("AssignSetCharacterRequestHandler", GetRegisteredRequestHandlerMethod("AssignSetCharacterRequest").Name, "AssignSetCharacterRequest registered handler method");
        AssertEqual("AssignResetStageRequestHandler", GetRegisteredRequestHandlerMethod("AssignResetStageRequest").Name, "AssignResetStageRequest registered handler method");
        AssertEqual("AssignGetRewardRequestHandler", GetRegisteredRequestHandlerMethod("AssignGetRewardRequest").Name, "AssignGetRewardRequest registered handler method");
        AssertMailNamedMapKeys(typeof(AssignGetDataResponse), null, ["AssignInfo"], "AssignGetDataResponse");
        AssertMailNamedMapKeys(typeof(AssignInfo), null, ["ChapterRecords", "GroupRecords", "GroupTeamRecords"], "AssignInfo");
        AssertMailNamedMapKeys(typeof(AssignGroupInfo), null, ["GroupId", "Count", "IsPerfect", "FinishStageIds"], "Assign group record");
        AssertMailNamedMapKeys(typeof(AssignGroupTeamInfo), null, ["GroupId", "TeamInfoList", "CaptainPosList", "FirstFightPosList"], "Assign team record");
        AssertMailNamedMapKeys(typeof(AssignSetTeamRequest), null, ["GroupId", "TeamList", "FirstFightPosList", "CaptainPosList"], "AssignSetTeamRequest");
        AssertMailNamedMapKeys(typeof(AssignResetStageRequest), null, ["GroupId", "StageId"], "AssignResetStageRequest");
        AssertMailNamedMapKeys(typeof(AssignGetRewardResponse), null, ["Code", "RewardList"], "AssignGetRewardResponse");


        const long playerId = 88_101;
        Character character = CreateDrawCompatibilityCharacter(playerId);
        character.Characters.AddRange(
        [
            new CharacterData { Id = 1_531_005, Level = 80, Ability = 6_500 },
            new CharacterData { Id = 1_011_004, Level = 80, Ability = 6_500 },
            new CharacterData { Id = 1_021_005, Level = 80, Ability = 6_500 },
            new CharacterData { Id = 1_321_003, Level = 80, Ability = 6_500 }
        ]);
        Inventory inventory = CreateDrawCompatibilityInventory(playerId, []);
        Player player = CreateDrawCompatibilityPlayer(playerId);

        using (MongoCollectionOverride mongo = MongoCollectionOverride.InstallForDailySignInCompatibility(
            out RecordingMongoCollectionProxy<Player> playerSaves,
            out RecordingMongoCollectionProxy<Character> characterSaves,
            out RecordingMongoCollectionProxy<Inventory> inventorySaves))
        using (LoopbackSessionHarness harness = new(character, player, inventory, "assign-compat-test"))
        {
            harness.Session.stage = CreateLoginAccountCompatibilityStage(playerId);
            AssignInfo initial = AssignInfoPayload(harness, 88_100);
            AssertEqual(0, initial.ChapterRecords.Count, "fresh Border Pact has no completed chapter records");
            AssertEqual(24, initial.GroupRecords.Count, "all Border Pact groups are initialized");
            AssertEqual(101, initial.GroupRecords[0].GroupId, "first Border Pact group id");
            AssertEqual(803, initial.GroupRecords[^1].GroupId, "final Border Pact group id");
            AssertEqual(0, initial.GroupTeamRecords.Count, "fresh Border Pact has no saved team records");
            int savesBeforeLockedGroup = playerSaves.ReplaceOneCalls;
            InvokeRegisteredRequestHandler("AssignSetTeamRequest", harness.Session, 88_101, new AssignSetTeamRequest
            {
                GroupId = 102,
                FirstFightPosList = [1, 1, 1],
                TeamList = [[1_531_005], [1_011_004, 1_021_005], [1_321_003]],
                CaptainPosList = [1, 1, 1]
            });
            AssertEqual(20003140, ReadResponsePayload<AssignSetTeamResponse>(
                harness, 88_101, "AssignSetTeamResponse", "locked AssignSetTeamResponse").Code,
                "Border Pact rejects a team for a locked successor group");
            AssertEqual(savesBeforeLockedGroup, playerSaves.ReplaceOneCalls,
                "locked Border Pact team does not persist");
            InvokeRegisteredRequestHandler(nameof(PreFightRequest), harness.Session, 88_1001,
                new PreFightRequest
                {
                    PreFightData = new()
                    {
                        StageId = 14_010_121,
                        CardIds = [1_531_005],
                        CaptainPos = 1,
                        FirstFightPos = 1
                    }
                });
            AssertEqual(20003140, ReadResponsePayload<PreFightResponse>(
                harness, 88_1001, nameof(PreFightResponse), "locked Assign PreFightResponse").Code,
                "Border Pact rejects pre-fight for a locked successor group");
            AssertEqual(null, harness.Session.fight, "locked Border Pact pre-fight does not start a fight");

            List<List<long>> firstGroupTeam = [[1_531_005, 1_011_004], [1_021_005], [1_321_003]];
            InvokeRegisteredRequestHandler("AssignSetTeamRequest", harness.Session, 88_102, new AssignSetTeamRequest
            {
                GroupId = 101,
                FirstFightPosList = [1, 1, 1],
                TeamList = firstGroupTeam,
                CaptainPosList = [1, 1, 1]
            });
            AssertEqual(0, ReadResponsePayload<AssignSetTeamResponse>(harness, 88_102, "AssignSetTeamResponse", "AssignSetTeamResponse").Code, "AssignSetTeamResponse code");

            AssignInfo saved = AssignInfoPayload(harness, 88_103);
            AssignGroupTeamInfo savedTeam = saved.GroupTeamRecords.Single(record => record.GroupId == 101);
            AssertEqual(true, savedTeam.TeamInfoList.SelectMany(team => team).SequenceEqual(firstGroupTeam.SelectMany(team => team)), "AssignGetDataResponse returns TeamInfoList using the client contract");

            byte[] savedState = harness.Session.player.ToBson();
            InvokeRegisteredRequestHandler("AssignSetTeamRequest", harness.Session, 88_104, new AssignSetTeamRequest
            {
                GroupId = 101,
                FirstFightPosList = [1, 1, 1],
                TeamList = [[99_999_999, 1_011_004], [1_021_005], [1_321_003]],
                CaptainPosList = [1, 1, 1]
            });
            AssertEqual(true, ReadResponsePayload<AssignSetTeamResponse>(harness, 88_104, "AssignSetTeamResponse", "AssignSetTeamResponse").Code != 0, "unowned Assign member rejected");
            AssertEqual(Convert.ToHexString(savedState), Convert.ToHexString(harness.Session.player.ToBson()), "invalid Assign team does not mutate player");

            PreFightRequest mismatch = new() { PreFightData = new() { StageId = 14_010_111, CardIds = [1_011_004, 1_531_005], CaptainPos = 1, FirstFightPos = 1 } };
            InvokeRegisteredRequestHandler(nameof(PreFightRequest), harness.Session, 88_105, mismatch);
            AssertEqual(true, ReadResponsePayload<PreFightResponse>(harness, 88_105, nameof(PreFightResponse), "Assign mismatched pre-fight").Code != 0, "Border Pact rejects a team different from the saved formation");
            AssertEqual(null, harness.Session.fight, "rejected Border Pact pre-fight does not start a fight");

            PreFightRequest preFight = new() { PreFightData = new() { StageId = 14_010_111, CardIds = [1_531_005, 1_011_004], CaptainPos = 1, FirstFightPos = 1 } };
            InvokeRegisteredRequestHandler(nameof(PreFightRequest), harness.Session, 88_106, preFight);
            PreFightResponse preFightResponse = ReadResponsePayload<PreFightResponse>(harness, 88_106, nameof(PreFightResponse), "Assign saved-team pre-fight");
            AssertEqual(0, preFightResponse.Code, "Border Pact sub-stage pre-fight code");

            InvokeRegisteredRequestHandler(nameof(FightSettleRequest), harness.Session, 88_107, CreateMissingStageSettleRequest(14_010_111, preFightResponse.FightData.FightId, playerId));
            FightSettleResponse settle = ReadAssignSettle(harness, 88_107);
            AssertEqual(0, settle.Code, "Border Pact sub-stage settle code");
            AssignGroupInfo progressed = AssignInfoPayload(harness, 88_108).GroupRecords.Single(record => record.GroupId == 101);
            AssertEqual(true, progressed.FinishStageIds.SequenceEqual([14_010_111]), "Border Pact persists the completed sub-stage");
            AssertEqual(0, progressed.Count, "Border Pact group remains incomplete after its first sub-stage");

            InvokeRegisteredRequestHandler("AssignResetStageRequest", harness.Session, 88_109, new AssignResetStageRequest { GroupId = 101, StageId = 14_010_111 });
            AssertEqual(0, ReadResponsePayload<AssignResetStageResponse>(harness, 88_109, "AssignResetStageResponse", "AssignResetStageResponse").Code, "AssignResetStageResponse code");
            AssertEqual(0, AssignInfoPayload(harness, 88_110).GroupRecords.Single(record => record.GroupId == 101).FinishStageIds.Count, "Border Pact reset removes stage progress");

            AssignGroupState firstGroup = harness.Session.player.Assign.Groups.Single(group => group.GroupId == 101);
            firstGroup.Count = 1;
            firstGroup.IsPerfect = true;
            harness.Session.player.Assign.Groups.AddRange(
            [
                new AssignGroupState { GroupId = 102, Count = 1, IsPerfect = true },
                new AssignGroupState { GroupId = 103, Count = 1, IsPerfect = true }
            ]);
            InvokeRegisteredRequestHandler("AssignSetCharacterRequest", harness.Session, 88_111, new AssignSetCharacterRequest { ChapterId = 2_001, CharacterId = 1_531_005 });
            AssertEqual(20182013, ReadResponsePayload<AssignSetCharacterResponse>(
                harness, 88_111, "AssignSetCharacterResponse", "ineligible AssignSetCharacterResponse").Code,
                "completed Border Pact chapter rejects an ineligible occupying character");
            List<EquipTable> resonanceTemplates = TableReaderV2.Parse<EquipTable>()
                .Where(row => row.Quality >= 6)
                .Take(6)
                .ToList();
            if (resonanceTemplates.Count != 6)
                throw new InvalidDataException("Border Pact occupation compatibility requires six 6-star equipment templates.");
            character.Equips.AddRange(resonanceTemplates.Select((row, index) => new EquipData
            {
                Id = (uint)(88_200 + index),
                TemplateId = (uint)row.Id,
                CharacterId = 1_531_005,
                ResonanceInfo =
                [
                    new ResonanceInfo { Slot = 1, CharacterId = 1_531_005 },
                    new ResonanceInfo { Slot = 2, CharacterId = 1_531_005 }
                ]
            }));
            InvokeRegisteredRequestHandler("AssignSetCharacterRequest", harness.Session, 88_112, new AssignSetCharacterRequest { ChapterId = 2_001, CharacterId = 1_531_005 });
            AssertEqual(0, ReadResponsePayload<AssignSetCharacterResponse>(
                harness, 88_112, "AssignSetCharacterResponse", "eligible AssignSetCharacterResponse").Code,
                "completed Border Pact chapter accepts an eligible occupying character");
            List<dynamic> loginRecords = InvokeAssignLoginRecords(harness.Session);
            AssertEqual(1, loginRecords.Count, "NotifyLogin includes only completed Border Pact chapters");
            AssertEqual(2_001, Convert.ToInt32(loginRecords[0].ChapterId), "NotifyLogin completed Border Pact chapter id");
            int inventorySavesBeforeReward = inventorySaves.ReplaceOneCalls;
            int characterSavesBeforeReward = characterSaves.ReplaceOneCalls;
            InvokeRegisteredRequestHandler("AssignGetRewardRequest", harness.Session, 88_113,
                new AssignGetRewardRequest { ChapterId = 2_001 });
            AssignGetRewardResponse reward = ReadAssignReward(harness, 88_113);
            AssertEqual(0, reward.Code, "Border Pact chapter reward code");
            AssertEqual(3, reward.RewardList.Count, "Border Pact chapter reward goods");
            AssertEqual(true, harness.Session.player.Assign.Chapters.Single(
                chapter => chapter.ChapterId == 2_001).IsGetReward,
                "Border Pact chapter reward receipt persists");
            const string claimKey = "assign-chapter:2001";
            AssertEqual(true, inventory.AppliedRewardClaims.Contains(claimKey, StringComparer.Ordinal),
                "Border Pact inventory reward receipt persists");
            AssertEqual(true, character.AppliedRewardClaims.Contains(claimKey, StringComparer.Ordinal),
                "Border Pact character reward receipt persists");
            AssertEqual(inventorySavesBeforeReward + 1, inventorySaves.ReplaceOneCalls,
                "Border Pact reward saves inventory");
            AssertEqual(characterSavesBeforeReward + 1, characterSaves.ReplaceOneCalls,
                "Border Pact reward saves character");
            InvokeRegisteredRequestHandler("AssignGetRewardRequest", harness.Session, 88_114,
                new AssignGetRewardRequest { ChapterId = 2_001 });
            AssertEqual(20182016, ReadResponsePayload<AssignGetRewardResponse>(
                harness, 88_114, "AssignGetRewardResponse", "duplicate AssignGetRewardResponse").Code,
                "Border Pact rejects a duplicate chapter reward");
            AssertEqual(inventorySavesBeforeReward + 1, inventorySaves.ReplaceOneCalls,
                "duplicate Border Pact reward does not save inventory");
            AssertEqual(characterSavesBeforeReward + 1, characterSaves.ReplaceOneCalls,
                "duplicate Border Pact reward does not save character");

            ValidateBorderPactCommand(harness, character, playerSaves);
        }
    }

    private static void ValidateBorderPactCommand(LoopbackSessionHarness harness, Character character, RecordingMongoCollectionProxy<Player> playerSaves)
    {
        Session session = harness.Session;
        string status = RunBorderPactCommand(session, "borderpact status");
        AssertEqual(true, status.Contains("2001: groups 3/3, reward claimed, occupant Selena: Pianissimo (1531005)"), "/borderpact status reports a completed, claimed, occupied chapter");
        AssertEqual(true, status.Contains("2002: groups 0/3, reward locked, occupant none"), "/borderpact status reports an untouched chapter");

        int savesBeforeComplete = playerSaves.ReplaceOneCalls;
        AssertEqual(true, RunBorderPactCommand(session, "borderpact complete 2").Contains("Completed Border Pact chapters 2002"), "/borderpact complete accepts a chapter index");
        AssertEqual(savesBeforeComplete + 1, playerSaves.ReplaceOneCalls, "/borderpact complete persists once");
        AssignInfo completed = AssignInfoPayload(harness, 88_115);
        foreach (int groupId in new[] { 201, 202, 203 })
        {
            AssignGroupInfo group = completed.GroupRecords.Single(record => record.GroupId == groupId);
            AssertEqual(1, group.Count, $"/borderpact complete clears group {groupId} like a settle");
            AssertEqual(true, group.IsPerfect, $"/borderpact complete marks group {groupId} perfect");
            AssertEqual(0, group.FinishStageIds.Count, $"/borderpact complete leaves no partial stages in group {groupId}");
        }
        AssignChapterInfo completedChapter = completed.ChapterRecords.Single(record => record.ChapterId == 2_002);
        AssertEqual(false, completedChapter.IsGetReward, "/borderpact complete leaves the chapter reward claimable");
        AssertEqual(0L, completedChapter.CharacterId, "/borderpact complete does not occupy the chapter");
        AssertEqual(0, completed.GroupRecords.Single(record => record.GroupId == 301).Count, "/borderpact complete 2002 leaves other chapters untouched");
        AssertEqual(2, InvokeAssignLoginRecords(session).Count, "NotifyLogin includes the command-completed chapter");
        int savesBeforeRepeat = playerSaves.ReplaceOneCalls;
        string repeated = RunBorderPactCommand(session, "assign complete 2002");
        AssertEqual(true, repeated.Contains("already complete"), $"/assign alias reports an already completed chapter ({repeated})");
        AssertEqual(savesBeforeRepeat, playerSaves.ReplaceOneCalls, "repeated /borderpact complete does not persist");

        // The command-completed chapter unlocks its successor group exactly like a real clear.
        CharacterData lotus = character.AddCharacter(1_021_001).Character;
        InvokeRegisteredRequestHandler("AssignSetTeamRequest", session, 88_116, new AssignSetTeamRequest
        {
            GroupId = 301,
            FirstFightPosList = [1, 1, 1],
            TeamList = [[1_021_001], [1_531_005], [1_011_004, 1_021_005, 1_321_003]],
            CaptainPosList = [1, 1, 1]
        });
        AssertEqual(0, ReadResponsePayload<AssignSetTeamResponse>(harness, 88_116, "AssignSetTeamResponse", "successor AssignSetTeamResponse").Code,
            "command-completed chapter unlocks the next chapter's first group");

        AssertEqual(true, RunBorderPactCommand(session, "borderpact assign 2001 none").Contains("no longer occupied"), "/borderpact assign none clears the occupant");
        AssertEqual("{}", AssignFightSkillPlus(harness, 88_117, lotus), "unoccupied Border Pact sends no fight skill plus");

        string ambiguous = RunBorderPactCommand(session, "borderpact assign 2001 lucia");
        AssertEqual(true, ambiguous.Contains("ambiguous") && ambiguous.Contains("1021001") && ambiguous.Contains("1021005"), "/borderpact assign lists ambiguous owned characters");
        AssertEqual(true, RunBorderPactCommand(session, "borderpact assign 2001 Selena: Pianissimo").Contains("now occupies chapter 2001"), "/borderpact assign resolves a multi-word LogName");
        AssertEqual(1_531_005L, session.player.Assign.Chapters.Single(record => record.ChapterId == 2_001).CharacterId, "/borderpact assign persists the occupant");
        Dictionary<int, int> expectedPlus = CharacterPower.GetAssignmentLevels(session, lotus);
        AssertEqual(true, expectedPlus.Count > 0, "occupied Border Pact chapter grants skill plus levels");
        AssertEqual(Newtonsoft.Json.JsonConvert.SerializeObject(expectedPlus),
            AssignFightSkillPlus(harness, 88_118, lotus), "occupied Border Pact sends NpcData.CharacterSkillPlus");
        // A character whose quality Npc row is absent from the share Npc table still gets the plus (Career stands in for Npc.Type).
        AscNet.Table.V2.share.character.quality.CharacterQualityTable? npcLess = TableReaderV2.Parse<AscNet.Table.V2.share.character.quality.CharacterQualityTable>()
            .FirstOrDefault(row => TableReaderV2.Parse<AscNet.Table.V2.share.fight.npc.NpcTable>().All(npc => npc.Id != row.NpcId)
                && session.character.Characters.All(owned => owned.Id != row.CharacterId));
        if (npcLess is not null)
        {
            CharacterData npcLessCharacter = character.AddCharacter((uint)npcLess.CharacterId).Character;
            AssertEqual(true, CharacterPower.GetAssignmentLevels(session, npcLessCharacter).Count > 0,
                $"Border Pact skill plus covers character {npcLess.CharacterId} without a share Npc row");
        }

        RunBorderPactCommand(session, "borderpact assign 2002 pianissimo");
        AssertEqual(0L, session.player.Assign.Chapters.Single(record => record.ChapterId == 2_002).CharacterId, "a character cannot occupy two Border Pact chapters");

        string auto = RunBorderPactCommand(session, "borderpact autoassign");
        AssertEqual(true, auto.Contains("Not completed (use /borderpact complete): 2003"), "/borderpact autoassign reports incomplete chapters");
        List<long> occupants = session.player.Assign.Chapters.Where(record => record.CharacterId > 0).Select(record => record.CharacterId).ToList();
        AssertEqual(occupants.Count, occupants.Distinct().Count(), "/borderpact autoassign keeps one chapter per character");
        AssertEqual(1_531_005L, session.player.Assign.Chapters.Single(record => record.ChapterId == 2_001).CharacterId, "/borderpact autoassign keeps existing occupants");

        RunBorderPactCommand(session, "borderpact reset");
        AssignInfo reset = AssignInfoPayload(harness, 88_119);
        AssertEqual(0, reset.ChapterRecords.Count + reset.GroupTeamRecords.Count + reset.GroupRecords.Sum(record => record.Count), "/borderpact reset clears Border Pact progress");
    }

    private static string RunBorderPactCommand(Session session, string line)
    {
        if (!AscNet.GameServer.Commands.CommandFactory.commands.ContainsKey("borderpact"))
            AscNet.GameServer.Commands.CommandFactory.LoadCommands();
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        AscNet.GameServer.Commands.Command command = AscNet.GameServer.Commands.CommandFactory.CreateCommand(parts[0], session, parts[1..])
            ?? throw new InvalidDataException($"Command {parts[0]} is not registered.");
        try { command.Execute(); return "Command executed!"; }
        catch (AscNet.GameServer.Commands.CommandMessageCallbackException ex) { return ex.Message; }
    }

    private static string AssignFightSkillPlus(LoopbackSessionHarness harness, int packetId, CharacterData subject)
    {
        InvokeRegisteredRequestHandler(nameof(PreFightRequest), harness.Session, packetId,
            new PreFightRequest { PreFightData = new() { StageId = 14_010_311, CardIds = [subject.Id], CaptainPos = 1, FirstFightPos = 1 } });
        PreFightResponse response = ReadResponsePayload<PreFightResponse>(harness, packetId, nameof(PreFightResponse), "Border Pact skill plus pre-fight");
        AssertEqual(0, response.Code, "Border Pact skill plus pre-fight code");
        Newtonsoft.Json.Linq.JObject payload = Newtonsoft.Json.Linq.JObject.Parse(MessagePackSerializer.ConvertToJson(MessagePackSerializer.Serialize(response)));
        Newtonsoft.Json.Linq.JToken npc = payload["FightData"]!["RoleData"]!.Single()!["NpcData"]!.Children<Newtonsoft.Json.Linq.JProperty>()
            .Select(property => property.Value)
            .Single(value => value["Character"]!.Value<uint>("Id") == subject.Id);
        return npc["CharacterSkillPlus"]!.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static AssignInfo AssignInfoPayload(LoopbackSessionHarness harness, int packetId)
    {
        InvokeRegisteredRequestHandler("AssignGetDataRequest", harness.Session, packetId, new AssignGetDataRequest());
        AssignGetDataResponse response = ReadResponsePayload<AssignGetDataResponse>(harness, packetId, "AssignGetDataResponse", "AssignGetDataResponse");
        if (response.AssignInfo.GroupRecords is null || response.AssignInfo.GroupTeamRecords is null)
            throw new InvalidDataException("AssignGetDataResponse must contain GroupRecords and GroupTeamRecords.");
        return response.AssignInfo;
    }

    private static List<dynamic> InvokeAssignLoginRecords(Session session)
    {
        Type module = RequiredAscNetGameServerType("AscNet.GameServer.Handlers.AssignModule");
        return (List<dynamic>)(RequiredMethod(module, "BuildLoginChapterRecords", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, [typeof(Session)]).Invoke(null, [session])
            ?? throw new InvalidDataException("AssignModule.BuildLoginChapterRecords returned nil."));
    }

    private static FightSettleResponse ReadAssignSettle(LoopbackSessionHarness harness, int packetId)
    {
        for (int index = 0; index < 8; index++)
        {
            Packet packet = harness.ReadPacket("Assign settle packet");
            if (packet.Type == Packet.ContentType.Push)
                continue;
            Packet.Response response = MessagePackSerializer.Deserialize<Packet.Response>(packet.Content);
            AssertEqual(packetId, response.Id, "Assign settle response id");
            AssertEqual(nameof(FightSettleResponse), response.Name, "Assign settle response name");
            return MessagePackSerializer.Deserialize<FightSettleResponse>(response.Content);
        }
        throw new InvalidDataException("Assign settle response was not emitted.");
    }
    private static AssignGetRewardResponse ReadAssignReward(LoopbackSessionHarness harness, int packetId)
    {
        for (int index = 0; index < 12; index++)
        {
            Packet packet = harness.ReadPacket("Assign reward packet");
            if (packet.Type == Packet.ContentType.Push)
                continue;
            Packet.Response response = MessagePackSerializer.Deserialize<Packet.Response>(packet.Content);
            AssertEqual(packetId, response.Id, "Assign reward response id");
            AssertEqual(nameof(AssignGetRewardResponse), response.Name, "Assign reward response name");
            return MessagePackSerializer.Deserialize<AssignGetRewardResponse>(response.Content);
        }
        throw new InvalidDataException("Assign reward response was not emitted.");
    }

}
