using AscNet.GameServer.Handlers;

namespace AscNet.GameServer.Commands
{
    [CommandName("warzone")]
    internal sealed class WarzoneCommand : Command
    {
        public WarzoneCommand(Session session, string[] args, bool validate = true) : base(session, args, validate) { }

        public override string Help => "Use /warzone reset, /warzone tiers, /warzone tier <Id|name>, /warzone zones, /warzone zone <AreaId|auto>, or /warzone fluctuation <level>.";

        [Argument(0, @"^[A-Za-z]+$|^[0-9]+$", "Tier ID/name or 'reset', 'tiers', 'tier', 'zones', 'zone', or 'fluctuation' action", ArgumentFlags.IgnoreCase)]
        string Operation { get; set; } = string.Empty;

        [Argument(1, @"^(auto|[A-Za-z]+|[0-9]+)$", "Tier ID/name, AreaId, fluctuation level, or 'auto'", ArgumentFlags.Optional | ArgumentFlags.IgnoreCase)]
        string Value { get; set; } = string.Empty;

        public override void Execute()
        {
            if (Operation.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(Value))
                    throw new CommandMessageCallbackException("Usage: /warzone reset");
                ArenaModule.ResetFromCommand(session);
                return;
            }

            if (Operation.Equals("zones", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(Value))
                    throw new CommandMessageCallbackException("Usage: /warzone zones");
                IReadOnlyList<(int Id, string Name)> zones = ArenaModule.GetZonesForCommand(session);
                throw new CommandMessageCallbackException(zones.Count == 0
                    ? "No zones are available for your current Warzone tier."
                    : "Available Warzone AreaIds: " + string.Join(", ", zones.Select(zone => $"{zone.Id} ({zone.Name})"))
                        + ". Use /warzone zone <AreaId> to select one.");
            }

            if (Operation.Equals("tiers", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(Value))
                    throw new CommandMessageCallbackException("Usage: /warzone tiers");
                IReadOnlyList<(int Id, string Name)> tiers = ArenaModule.GetTiersForCommand(session);
                throw new CommandMessageCallbackException(tiers.Count == 0
                    ? "No Warzone tiers are available at your Commandant level."
                    : "Available Warzone tiers: " + string.Join(", ", tiers.Select(tier => $"{tier.Id} ({tier.Name})"))
                        + ". Use /warzone tier <Id|name>.");
            }

            if (Operation.Equals("zone", StringComparison.OrdinalIgnoreCase))
            {
                if (Value.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    ArenaModule.SetZoneFromCommand(session, 0);
                    return;
                }
                if (!int.TryParse(Value, out int areaId))
                    throw new CommandMessageCallbackException("Usage: /warzone zone <AreaId|auto>. Use /warzone zones to list IDs.");
                ArenaModule.SetZoneFromCommand(session, areaId);
                return;
            }

            if (Operation.Equals("fluctuation", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(Value, out int fluctuationLevel))
                    throw new CommandMessageCallbackException("Usage: /warzone fluctuation <level> (0 or higher).");
                ArenaModule.SetFluctuationFromCommand(session, fluctuationLevel);
                throw new CommandMessageCallbackException($"Warzone fluctuation set to {fluctuationLevel}.");
            }

            if (Operation.Equals("tier", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(Value))
                    throw new CommandMessageCallbackException("Usage: /warzone tier <Id|name>");
                ArenaModule.SetTierFromCommand(session, Value);
                return;
            }

            if (!string.IsNullOrEmpty(Value))
                throw new CommandMessageCallbackException("Usage: /warzone tiers, /warzone tier <Id|name>, /warzone zones, /warzone zone <AreaId|auto>, or /warzone fluctuation <level>");
            ArenaModule.SetTierFromCommand(session, Operation);
        }
    }
}
