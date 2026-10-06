using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.Table.V2.share.photomode;

namespace AscNet.GameServer.Commands
{
    [CommandName("scene")]
    internal class SceneCommand : Command
    {
        public SceneCommand(Session session, string[] args, bool validate = true) : base(session, args, validate) { }

        public override string Help => "Unlock every home scene background with 'all'.";

        [Argument(0, @"^unlock$", "The operation selected (unlock)")]
        string Op { get; set; } = string.Empty;

        [Argument(1, @"^all$", "Unlock every catalog scene background")]
        string Target { get; set; } = string.Empty;

        public override void Execute()
        {
            if (Op != "unlock")
                throw new InvalidOperationException("Invalid operation!");

            List<int> catalogIds = TableReaderV2.Parse<BackgroundTable>()
                .Where(background => background.Id > 0 && background.SceneModelId > 0)
                .Select(background => background.Id)
                .Distinct()
                .Order()
                .ToList();

            List<int> owned = session.player.OwnedBackgroundIds ?? new List<int>();
            List<int> added = catalogIds.Where(id => !owned.Contains(id)).ToList();

            if (added.Count == 0)
                throw new CommandMessageCallbackException("All scene backgrounds are already unlocked.");

            List<int> original = owned.ToList();
            session.player.OwnedBackgroundIds = owned.Union(catalogIds).Distinct().Order().ToList();

            try
            {
                session.player.SaveChecked();
            }
            catch
            {
                session.player.OwnedBackgroundIds = original;
                throw new CommandMessageCallbackException("Failed to persist scene unlocks.");
            }

            foreach (int id in added)
                session.SendPush(new NotifyAddBackground { BackgroundId = id });

            throw new CommandMessageCallbackException($"Unlocked {added.Count} scene background(s).");
        }
    }
}
