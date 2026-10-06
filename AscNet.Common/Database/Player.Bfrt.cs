using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

[BsonIgnoreExtraElements]
public sealed class BfrtTeamState
{
    [BsonElement("id")] public int Id { get; set; }
    [BsonElement("fight_team_list")] public List<List<uint>> FightTeamList { get; set; } = new();
    [BsonElement("logistics_team_list")] public List<List<uint>> LogisticsTeamList { get; set; } = new();
    [BsonElement("captain_pos_list")] public List<int> CaptainPosList { get; set; } = new();
    [BsonElement("first_fight_pos_list")] public List<int> FirstFightPosList { get; set; } = new();
}

[BsonIgnoreExtraElements]
public sealed class BfrtGroupState
{
    [BsonElement("id")] public int Id { get; set; }
    [BsonElement("count")] public int Count { get; set; }
    [BsonElement("is_recv_reward")] public bool IsRecvReward { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class BfrtState
{
    [BsonElement("teams")] public List<BfrtTeamState> Teams { get; set; } = new();
    [BsonElement("groups")] public List<BfrtGroupState> Groups { get; set; } = new();
    [BsonElement("progress_group_id")] public int ProgressGroupId { get; set; }
    [BsonElement("progress_stage_ids")] public List<int> ProgressStageIds { get; set; } = new();
    [BsonElement("course_reward_star")] public int CourseRewardStar { get; set; }
    [BsonElement("claimed_course_rewards")] public List<int> ClaimedCourseRewards { get; set; } = new();
}

public partial class Player
{
    [BsonElement("bfrt")] public BfrtState Bfrt { get; set; } = new();
}
