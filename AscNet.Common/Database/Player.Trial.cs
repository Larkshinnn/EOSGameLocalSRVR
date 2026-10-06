using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

[BsonIgnoreExtraElements]
public sealed class TrialState
{
    [BsonElement("finished_trials")] public List<int> FinishedTrials { get; set; } = new();
    [BsonElement("claimed_trials")] public List<int> ClaimedTrials { get; set; } = new();
    [BsonElement("claimed_types")] public List<int> ClaimedTypes { get; set; } = new();
}

public partial class Player
{
    [BsonElement("trial")] public TrialState Trial { get; set; } = new();
}
