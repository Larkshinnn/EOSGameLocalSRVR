using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

/// <summary>
/// Durable per-sign-in-table progress, keyed by the SignIn table Id so the daily
/// log-in (Id 1) and scheduled event sign-ins (e.g. Id 114/115) progress independently.
/// The legacy global daily counters on <see cref="Player"/> are migrated into the
/// Id 1 state once and are no longer written (no dual-write aliases).
/// </summary>
public sealed class PlayerSignInState
{
    [BsonElement("id")]
    public int Id { get; set; }

    /// <summary>Cumulative claims for this sign; derives the wire Round/Day.</summary>
    [BsonElement("claim_count")]
    public long ClaimCount { get; set; }

    /// <summary>Unix seconds of the most recent claim; drives the 05:00 UTC business-day Got flag.</summary>
    [BsonElement("last_sign_in_time")]
    public long LastSignInTime { get; set; }
}

public partial class Player
{
    [BsonElement("sign_in_states")]
    public List<PlayerSignInState> SignInStates { get; set; } = new();
}
