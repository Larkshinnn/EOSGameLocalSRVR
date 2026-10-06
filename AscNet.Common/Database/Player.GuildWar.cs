using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

public partial class Player
{
    [BsonElement("guild_war")]
    public GuildWarState GuildWar { get; set; } = new();
}

/// <summary>Durable Guild War popup action state (ordered, distinct positive action IDs the client has played).</summary>
public sealed class GuildWarState
{
    [BsonElement("played_action_ids")]
    public List<int> PlayedActionIds { get; set; } = [];
}
