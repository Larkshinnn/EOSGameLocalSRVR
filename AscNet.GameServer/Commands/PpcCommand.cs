using AscNet.GameServer.Handlers;

namespace AscNet.GameServer.Commands
{
    [CommandName("ppc")]
    internal sealed class PpcCommand : Command
    {
        public PpcCommand(Session session, string[] args, bool validate = true) : base(session, args, validate) { }

        public override string Help => "Use /ppc tiers, /ppc tier <LevelType>, /ppc intensive bosses|boss <1-7>|zones|zone <SectionId>, /ppc reset, /ppc codex reset, or /ppc attempts <count|reset>.";

        [Argument(0, @"^(tiers|tier|reset|codex|attempts|intensive)$", "PPC command", ArgumentFlags.IgnoreCase)]
        string Action { get; set; } = string.Empty;

        [Argument(1, @"^(reset|[0-9]+|bosses|boss|zones|zone)$", "PPC command argument", ArgumentFlags.Optional | ArgumentFlags.IgnoreCase)]
        string Value { get; set; } = string.Empty;

        [Argument(2, @"^[0-9]+$", "Boss group or zone section ID", ArgumentFlags.Optional | ArgumentFlags.IgnoreCase)]
        string ExtraValue { get; set; } = string.Empty;

        public override void Execute()
        {
            if (Action.Equals("intensive", StringComparison.OrdinalIgnoreCase))
            {
                if (Value.Equals("bosses", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(ExtraValue))
                {
                    throw new CommandMessageCallbackException("Available intensive boss groups: "
                        + string.Join(", ", BossModule.GetIntensiveBossGroupsForCommand())
                        + ". Use /ppc intensive boss <1-7>.");
                }
                if (Value.Equals("boss", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(ExtraValue, out int bossGroup))
                    BossModule.SetIntensiveBossFromCommand(session, bossGroup);
                if (Value.Equals("zones", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(ExtraValue))
                {
                    IReadOnlyList<int> zones = BossModule.GetIntensiveZonesForCommand(session);
                    throw new CommandMessageCallbackException(zones.Count == 0
                        ? "No zones are available. Select an intensive boss with /ppc intensive boss <1-7>."
                        : "Available intensive zone SectionIds: " + string.Join(", ", zones)
                            + ". Use /ppc intensive zone <SectionId>.");
                }
                if (Value.Equals("zone", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(ExtraValue, out int sectionId))
                    BossModule.SetIntensiveZoneFromCommand(session, sectionId);
                throw new CommandMessageCallbackException(Help);
            }

            if (Action.Equals("tiers", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(Value))
                    throw new CommandMessageCallbackException("Usage: /ppc tiers");
                IReadOnlyList<int> levels = BossModule.GetTierTypesForCommand(session);
                throw new CommandMessageCallbackException(levels.Count == 0
                    ? "No PPC tiers are available for your Commandant level in this activity."
                    : $"Valid PPC LevelType IDs: {string.Join(", ", levels)}. Use /ppc tier <LevelType>.");
            }

            if (Action.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(Value))
                    throw new CommandMessageCallbackException("Usage: /ppc reset");
                BossModule.SetChallengeAttemptsFromCommand(session, 0);
                return;
            }

            if (Action.Equals("codex", StringComparison.OrdinalIgnoreCase))
            {
                if (!Value.Equals("reset", StringComparison.OrdinalIgnoreCase))
                    throw new CommandMessageCallbackException("Usage: /ppc codex reset");
                BossModule.ResetCodexScoresFromCommand(session);
                return;
            }

            if (Action.Equals("attempts", StringComparison.OrdinalIgnoreCase))
            {
                if (Value.Equals("reset", StringComparison.OrdinalIgnoreCase))
                {
                    BossModule.SetChallengeAttemptsFromCommand(session, 0);
                    return;
                }
                if (!int.TryParse(Value, out int attempts))
                    throw new CommandMessageCallbackException("Usage: /ppc attempts <count|reset>");
                BossModule.SetChallengeAttemptsFromCommand(session, attempts);
                return;
            }

            if (string.IsNullOrEmpty(Value))
                throw new CommandMessageCallbackException("Usage: /ppc tier <LevelType>. Use /ppc tiers to list valid IDs.");

            BossModule.SetTierFromCommand(session, int.Parse(Value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
