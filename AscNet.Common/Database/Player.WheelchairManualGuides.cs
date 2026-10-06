using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

public partial class Player
{
    [BsonElement("wheelchair_manual_guide_reward_receipts")]
    public List<WheelchairManualGuideRewardReceipt> WheelchairManualGuideRewardReceipts { get; set; } = [];
}

public class WheelchairManualGuideRewardReceipt
{
    [BsonElement("claim_key")]
    public string ClaimKey { get; set; } = string.Empty;

    [BsonElement("event_cause")]
    public int EventCause { get; set; }

    [BsonElement("granted_at")]
    public long GrantedAt { get; set; }

    [BsonElement("goods")]
    public List<WheelchairManualGuideRewardCount> Goods { get; set; } = [];
}

public class WheelchairManualGuideRewardCount
{
    [BsonElement("template_id")]
    public int TemplateId { get; set; }

    [BsonElement("count")]
    public int Count { get; set; }
}
