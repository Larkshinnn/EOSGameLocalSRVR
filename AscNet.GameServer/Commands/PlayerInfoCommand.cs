using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.GameServer.Handlers;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AscNet.GameServer.Commands
{
    [CommandName("playerinfo")]
    internal sealed class PlayerInfoCommand : Command
    {
        public PlayerInfoCommand(Session session, string[] args, bool validate = true) : base(session, args, validate) { }

        public override string Help => "Show this player's runtime state and persisted profile/Warzone state.";

        public override void Execute()
        {
            Player player = session.player;
            var collection = Player.collection.Database.GetCollection<BsonDocument>("players");
            BsonDocument? persisted = collection.Find(new BsonDocument("_id", player.Id))
                .Project(Builders<BsonDocument>.Projection
                    .Include("player_data._id")
                    .Include("player_data.Name")
                    .Include("player_data.Level")
                    .Include("simulated_battlefield.arena_activity_no")
                    .Include("simulated_battlefield.arena_level")
                    .Include("simulated_battlefield.arena_challenge_id")
                    .Include("simulated_battlefield.arena_fluctuation_level"))
                .FirstOrDefault();
            BsonDocument? savedData = persisted?.GetValue("player_data", BsonNull.Value) as BsonDocument;
            BsonDocument? savedArena = persisted?.GetValue("simulated_battlefield", BsonNull.Value) as BsonDocument;
            NotifyArenaActivity activity = ArenaModule.BuildActivityForCommand(player);

            throw new CommandMessageCallbackException(
                $"Player uid={player.PlayerData.Id} name={player.PlayerData.Name} lv={player.PlayerData.Level} " +
                $"DB={Player.collection.CollectionNamespace.DatabaseNamespace.DatabaseName}.{Player.collection.CollectionNamespace.CollectionName} " +
                $"doc={player.Id} savedUid={savedData?.GetValue("_id", BsonNull.Value)} savedName={savedData?.GetValue("Name", BsonNull.Value)} " +
                $"WZ activity={player.SimulatedBattlefield.ArenaActivityNo} tier={player.SimulatedBattlefield.ArenaLevel} challenge={player.SimulatedBattlefield.ArenaChallengeId}; " +
                $"fluc runtime={player.SimulatedBattlefield.ArenaFluctuationLevel} " +
                $"mongo={savedArena?.GetValue("arena_fluctuation_level", BsonNull.Value) ?? BsonNull.Value} " +
                $"push={activity.FluctuationLevel}");
        }
    }
}
