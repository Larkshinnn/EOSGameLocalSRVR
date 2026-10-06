using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

public partial class Player
{
    /// <summary>Durable home-scene ownership (ordered, distinct scene/background IDs the commandant has unlocked).</summary>
    [BsonElement("owned_background_ids")]
    public List<int> OwnedBackgroundIds { get; set; } = [];
}
