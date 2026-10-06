using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace AscNet.Common.Database;

public sealed class WheelchairManualState
{
    public bool IsSeniorManualUnlock { get; set; }
    public List<int> ClaimedRewardIds { get; set; } = [];
    public HashSet<int> AcknowledgedBluePoints { get; set; } = [];
    public HashSet<long> AcknowledgedRedPoints { get; set; } = [];
}

public partial class Player
{
    [BsonElement("wheelchair_manual_states")]
    [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
    public Dictionary<int, WheelchairManualState> WheelchairManualStates { get; set; } = new();
}
