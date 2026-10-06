using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

[BsonIgnoreExtraElements]
public sealed class AssignChapterState
{
    [BsonElement("chapter_id")] public int ChapterId { get; set; }
    [BsonElement("character_id")] public long CharacterId { get; set; }
    [BsonElement("is_get_reward")] public bool IsGetReward { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class AssignGroupState
{
    [BsonElement("group_id")] public int GroupId { get; set; }
    [BsonElement("count")] public int Count { get; set; }
    [BsonElement("is_perfect")] public bool IsPerfect { get; set; }
    [BsonElement("finish_stage_ids")] public List<int> FinishStageIds { get; set; } = new();
}

[BsonIgnoreExtraElements]
public sealed class AssignTeamState
{
    [BsonElement("group_id")] public int GroupId { get; set; }
    [BsonElement("team_info_list")] public List<List<long>> TeamInfoList { get; set; } = new();
    [BsonElement("captain_pos_list")] public List<int> CaptainPosList { get; set; } = new();
    [BsonElement("first_fight_pos_list")] public List<int> FirstFightPosList { get; set; } = new();
}

[BsonIgnoreExtraElements]
public sealed class AssignState
{
    [BsonElement("chapters")] public List<AssignChapterState> Chapters { get; set; } = new();
    [BsonElement("groups")] public List<AssignGroupState> Groups { get; set; } = new();
    [BsonElement("teams")] public List<AssignTeamState> Teams { get; set; } = new();
}

public partial class Player
{
    [BsonElement("assign")] public AssignState Assign { get; set; } = new();
}
