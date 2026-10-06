using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

[BsonIgnoreExtraElements]
public sealed class AwarenessChapterState
{
    [BsonElement("chapter_id")] public int ChapterId { get; set; }
    [BsonElement("character_id")] public long CharacterId { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class AwarenessChallengeState
{
    [BsonElement("chapter_id")] public int ChapterId { get; set; }
    [BsonElement("count")] public int Count { get; set; }
    [BsonElement("finish_stage_ids")] public List<int> FinishStageIds { get; set; } = new();
}

[BsonIgnoreExtraElements]
public sealed class AwarenessTeamState
{
    [BsonElement("chapter_id")] public int ChapterId { get; set; }
    [BsonElement("team_info_list")] public List<List<long>> TeamInfoList { get; set; } = new();
    [BsonElement("captain_pos_list")] public List<int> CaptainPosList { get; set; } = new();
    [BsonElement("first_fight_pos_list")] public List<int> FirstFightPosList { get; set; } = new();
}

[BsonIgnoreExtraElements]
public sealed class AwarenessState
{
    [BsonElement("chapters")] public List<AwarenessChapterState> Chapters { get; set; } = new();
    [BsonElement("challenges")] public List<AwarenessChallengeState> Challenges { get; set; } = new();
    [BsonElement("teams")] public List<AwarenessTeamState> Teams { get; set; } = new();
}

public partial class Player
{
    [BsonElement("awareness")] public AwarenessState Awareness { get; set; } = new();
}
