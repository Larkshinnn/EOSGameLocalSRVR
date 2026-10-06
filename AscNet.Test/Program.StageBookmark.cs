using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer;
using MessagePack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Newtonsoft.Json.Linq;
using StageRow = AscNet.Table.V2.share.fuben.StageTable;
using StageState = AscNet.Common.Database.Stage;

namespace AscNet.Test
{
    internal partial class Program
    {
        private static void ValidateStageBookmarkCompatibility()
        {
            // EN XMovieAgency persists one replaceable checkpoint, fetched lazily after relog.
            using MongoCollectionOverride collections = MongoCollectionOverride.InstallForStudyProgressionCompatibility(
                out RecordingMongoCollectionProxy<StageState> stageCollection);
            using LoopbackSessionHarness harness = new(CreateDrawCompatibilityCharacter(88_093), sessionId: "stage-bookmark");
            harness.Session.stage = new StageState { Uid = 88_093, Stages = new() };
            StageRow[] stages = TableReaderV2.Parse<StageRow>()
                .Where(stage => stage.Type is 1 or 25 or 57 or 87).Take(2).ToArray();
            int packetId = 1;
            AssertEqual(JTokenType.Null, Get()["StageBookmarkData"]!.Type, "fresh account has no bookmark");

            AddStageBookmarkRequest first = new()
            {
                StageId = checked((int)stages[0].StageId), MovieId = "bookmark-story-a", ActionId = 12,
                OptionInfos = new() { [3] = 2, [8] = 1 }
            };
            Add(first, 0);
            AssertCheckpoint(first);
            Add(first, 0);
            AssertCheckpoint(first);

            byte[] saved = (stageCollection.LastReplacement ?? throw new InvalidDataException("Bookmark was not saved.")).ToBson();
            using (LoopbackSessionHarness relog = new(CreateDrawCompatibilityCharacter(88_093), sessionId: "stage-bookmark-relog"))
            {
                relog.Session.stage = BsonSerializer.Deserialize<StageState>(saved);
                JObject restored = Exchange(relog, "GetStageBookmarkRequest", [0xc0], "GetStageBookmarkResponse");
                AssertEqual(true, JToken.DeepEquals(JObject.FromObject(first), restored["StageBookmarkData"]), "relog restores checkpoint and numeric option map");
            }

            // Rejections must retain the prior resumable checkpoint, including malformed wire types.
            int unsupportedStage = checked((int)TableReaderV2.Parse<StageRow>().First(stage => stage.Type is not (1 or 25 or 57 or 87)).StageId);
            foreach (object invalid in new object[]
            {
                new { StageId = int.MaxValue, first.MovieId, first.ActionId, first.OptionInfos },
                new { StageId = unsupportedStage, first.MovieId, first.ActionId, first.OptionInfos },
                new { first.StageId, MovieId = "", first.ActionId, first.OptionInfos },
                new { first.StageId, first.MovieId, ActionId = 0, first.OptionInfos },
                new { first.StageId, first.MovieId, first.ActionId },
                new { first.StageId, first.MovieId, first.ActionId, OptionInfos = new Dictionary<int, int> { [3] = 0 } },
                new { StageId = "bad", first.MovieId, first.ActionId, first.OptionInfos }
            })
            {
                Add(invalid, 5);
                AssertCheckpoint(first);
            }
            AssertEqual(5, Exchange(harness, "AddStageBookmarkRequest", [0xc0], "AddStageBookmarkResponse").Value<int>("Code"), "nil add rejected");
            AssertCheckpoint(first);
            AssertEqual(true, saved.AsSpan().SequenceEqual(stageCollection.LastReplacement!.ToBson()), "rejected bookmarks never replace durable checkpoint");

            AddStageBookmarkRequest replacement = new()
            {
                StageId = checked((int)stages[1].StageId), MovieId = "bookmark-story-b", ActionId = 25, OptionInfos = new()
            };
            stageCollection.ThrowOnReplaceOne = true;
            try
            {
                Add(replacement, 2);
                AssertCheckpoint(first);
                AssertEqual(2, Exchange(harness, "DeleteStageBookmarkRequest", [0xc0], "DeleteStageBookmarkResponse").Value<int>("Code"), "failed delete reports persistence error");
                AssertCheckpoint(first);
            }
            finally
            {
                stageCollection.ThrowOnReplaceOne = false;
            }
            AssertEqual(0, Exchange(harness, "DeleteStageBookmarkRequest", [0xc0], "DeleteStageBookmarkResponse").Value<int>("Code"), "failed delete can be retried");
            AssertEqual(JTokenType.Null, Get()["StageBookmarkData"]!.Type, "successful delete retry clears checkpoint");
            Add(replacement, 0);
            AssertCheckpoint(replacement);
            harness.Session.stage = BsonSerializer.Deserialize<StageState>(stageCollection.LastReplacement!.ToBson());
            AssertCheckpoint(replacement);
            for (int i = 0; i < 2; i++)
            {
                AssertEqual(0, Exchange(harness, "DeleteStageBookmarkRequest", [0xc0], "DeleteStageBookmarkResponse").Value<int>("Code"), "delete is idempotent");
                AssertEqual(JTokenType.Null, Get()["StageBookmarkData"]!.Type, "deleted checkpoint absent");
            }
            harness.Session.stage = BsonSerializer.Deserialize<StageState>(stageCollection.LastReplacement!.ToBson());
            AssertEqual(JTokenType.Null, Get()["StageBookmarkData"]!.Type, "delete survives reload");

            void Add(object request, int code) => AssertEqual(code,
                Exchange(harness, "AddStageBookmarkRequest", MessagePackSerialize(request.GetType(), request), "AddStageBookmarkResponse").Value<int>("Code"), "bookmark add result");
            JObject Get() => Exchange(harness, "GetStageBookmarkRequest", [0x80], "GetStageBookmarkResponse");
            void AssertCheckpoint(AddStageBookmarkRequest expected) => AssertEqual(true,
                JToken.DeepEquals(JObject.FromObject(expected), Get()["StageBookmarkData"]), "current checkpoint replaces rather than appends");
            JObject Exchange(LoopbackSessionHarness target, string name, byte[] content, string responseName)
            {
                int id = packetId++;
                GetRegisteredRequestHandler(name)(target.Session, new Packet.Request { Id = id, Name = name, Content = content });
                Packet packet = target.ReadPacket("bookmark response");
                AssertEqual(Packet.ContentType.Response, packet.Type, "bookmark does not require unsolicited pushes");
                Packet.Response response = MessagePackSerializer.Deserialize<Packet.Response>(packet.Content);
                AssertEqual(id, response.Id, "bookmark response correlation");
                AssertEqual(responseName, response.Name, "bookmark response name");
                JObject payload = JObject.Parse(MessagePackSerializer.ConvertToJson(response.Content));
                if (responseName == "GetStageBookmarkResponse")
                {
                    AssertEqual(0, payload.Value<int>("Code"), "bookmark fetch succeeds");
                    AssertEqual("Code,StageBookmarkData", string.Join(",", payload.Properties().Select(property => property.Name)), "retail singular nullable bookmark schema");
                }
                AssertNoAvailablePacket(target, "bookmark has no extra response");
                return payload;
            }
        }
    }
}
