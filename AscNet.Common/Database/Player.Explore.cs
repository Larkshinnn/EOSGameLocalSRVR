using MongoDB.Bson.Serialization.Attributes;
using AscNet.Common.MsgPack;

namespace AscNet.Common.Database;

[BsonIgnoreExtraElements]
public sealed class ExploreEnduranceState
{
    [BsonElement("id")] public int Id { get; set; }
    [BsonElement("use")] public int Use { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class ExploreChapterState
{
    [BsonElement("id")] public int Id { get; set; }
    [BsonElement("endurance_infos")] public List<ExploreEnduranceState> EnduranceInfos { get; set; } = new();
    [BsonElement("reward_status")] public int RewardStatus { get; set; }
    [BsonElement("finish_nodes")] public List<int> FinishNodes { get; set; } = new();
    [BsonElement("unlock_events")] public List<int> UnlockEvents { get; set; } = new();
}

public partial class Player
{
    [BsonElement("explore")] public List<ExploreChapterState> Explore { get; set; } = new();
}
