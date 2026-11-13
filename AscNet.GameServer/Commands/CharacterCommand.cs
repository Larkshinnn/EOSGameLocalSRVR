using System.Text.RegularExpressions;
using AscNet.Common.Database;
using AscNet.Common.Util;
using AscNet.Common.MsgPack;
using AscNet.GameServer.Handlers;
using AscNet.Table.V2.share.character;
using AscNet.Table.V2.share.character.quality;

namespace AscNet.GameServer.Commands
{
    [CommandName("character")]
    internal class CharacterCommand : Command
    {
        public CharacterCommand(Session session, string[] args, bool validate = true) : base(session, args, validate) { }

        public override string Help => "Use /character add <frame name|id|all>, /character rank <frame name|id> <B|A|S|SS|SSS|SSS3|SSS6|SSS+|quality> [star 0-9], or /character find <name>. Frame names may contain spaces, e.g. /character rank lucia pyroath SSS+ or /character rank pyroath SS 2.";

        [Argument(0, @"^add$|^rank$|^find$", "The operation selected (add, rank, find)", ArgumentFlags.IgnoreCase)]
        string Op { get; set; } = string.Empty;

        [Argument(1, @"^\S+$", "The target character: frame name (e.g. Pyroath, Lucia Pyroath, Lucia: Pyroath), character id, or 'all' for add")]
        string Target { get; set; } = string.Empty;

        private static readonly Regex RankToken = new(@"^([0-6]|B|A|S|SS|SSS|SSS3|SSS6|SSS\+)$", RegexOptions.IgnoreCase);
        private static readonly Regex StarToken = new(@"^[0-9]$");

        public override void Execute()
        {
            switch (Op.ToLowerInvariant())
            {
                case "add":
                    RewardApplicationResult result;
                    if (Target.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        HashSet<uint> ownedCharacterIds = session.character.Characters
                            .Select(character => character.Id)
                            .ToHashSet();
                        IEnumerable<Reward> rewards = TableReaderV2.Parse<CharacterTable>()
                            .Where(character => Character.IsOwnableCharacter((uint)character.Id)
                                && !ownedCharacterIds.Contains((uint)character.Id))
                            .Select(character => new Reward { Id = character.Id, Type = RewardType.Character });

                        result = RewardHandler.ApplyRewards(rewards, session);
                    }
                    else
                    {
                        CharacterTable added = ResolveCharacter(args[1..], ownedOnly: false);
                        result = RewardHandler.ApplyRewards([ new Reward() { Id = added.Id, Type = RewardType.Character } ], session);
                    }
                    session.inventory.SaveChecked();
                    session.character.SaveChecked();
                    if (result.DormFurnitureChanged || result.GatherRewardIds.Count > 0 || result.HeadPortraitData.Heads.Count > 0)
                        session.player.SaveChecked();
                    result.SendPushes(session);
                    break;
                case "rank":
                    SetRank();
                    break;
                case "find":
                    FindCharacters();
                    break;
                default:
                    throw new InvalidOperationException("Invalid operation!");
            }
        }

        private static IEnumerable<string?> CharacterNames(CharacterTable row) =>
        [
            row.LogName,
            row.TradeName,
            $"{row.Name} {row.TradeName}",
            $"{row.TradeName} {row.Name}"
        ];

        // Resolves a frame name (TradeName such as "Pyroath", "Lucia Pyroath", LogName "Lucia: Pyroath") or an id.
        private CharacterTable ResolveCharacter(string[] tokens, bool ownedOnly)
        {
            string query = string.Join(' ', tokens);
            if (tokens.Length == 0)
                throw new CommandMessageCallbackException("Specify a frame name or character id.");
            HashSet<uint> owned = session.character.Characters.Select(character => character.Id).ToHashSet();
            IEnumerable<CharacterTable> rows = TableReaderV2.Parse<CharacterTable>()
                .Where(row => Character.IsOwnableCharacter((uint)row.Id) && (!ownedOnly || owned.Contains((uint)row.Id)));
            NameLookup.Result<CharacterTable> result = NameLookup.Resolve(rows, row => row.Id, CharacterNames, tokens);
            if (result.Match is null)
                throw new CommandMessageCallbackException(int.TryParse(query, out _)
                    ? $"Character {query} is not {(ownedOnly ? "on this account" : "a playable character")}."
                    : NameLookup.FormatSuggestions(ownedOnly ? "owned frame" : "frame", query, result.Suggestions));
            if (result.Suggestions.Count > 0)
                throw new CommandMessageCallbackException($"\"{query}\" matches several frames: "
                    + string.Join(", ", new[] { result.Match }.Concat(result.Suggestions).Select(candidate => $"{candidate.Row.LogName} ({candidate.Id})"))
                    + ". Type more of the name, e.g. character and frame together.");
            return result.Match.Row;
        }

        private void FindCharacters()
        {
            if (args.Length < 2)
                throw new CommandMessageCallbackException("Usage: /character find <name>");
            NameLookup.Result<CharacterTable> result = NameLookup.Resolve(
                TableReaderV2.Parse<CharacterTable>().Where(row => Character.IsOwnableCharacter((uint)row.Id)),
                row => row.Id, CharacterNames, args[1..]);
            List<NameLookup.Candidate<CharacterTable>> found = result.Match is null
                ? result.Suggestions.ToList()
                : [result.Match, .. result.Suggestions];
            HashSet<uint> owned = session.character.Characters.Select(character => character.Id).ToHashSet();
            throw new CommandMessageCallbackException(found.Count == 0
                ? $"No frame matches \"{string.Join(' ', args[1..])}\"."
                : "Frames: " + string.Join(", ", found.Take(10).Select(candidate =>
                    $"{candidate.Row.LogName} ({candidate.Id}{(owned.Contains((uint)candidate.Id) ? ", owned" : string.Empty)})")));
        }

        private void SetRank()
        {
            const string usage = "Usage: /character rank <frame name|id> <B|A|S|SS|SSS|SSS3|SSS6|SSS+|quality> [star 0-9]";
            string[] tokens = args[1..];
            if (tokens.Length < 2)
                throw new CommandMessageCallbackException(usage);

            // The frame name may span several words; the rank and an optional star are the trailing tokens.
            string rankText, starText = string.Empty;
            int nameLength;
            if (tokens.Length >= 3 && StarToken.IsMatch(tokens[^1]) && RankToken.IsMatch(tokens[^2]))
            {
                rankText = tokens[^2];
                starText = tokens[^1];
                nameLength = tokens.Length - 2;
            }
            else if (RankToken.IsMatch(tokens[^1]))
            {
                rankText = tokens[^1];
                nameLength = tokens.Length - 1;
            }
            else
                throw new CommandMessageCallbackException(usage);

            CharacterTable table = ResolveCharacter(tokens[..nameLength], ownedOnly: true);
            int characterId = table.Id;
            CharacterData? character = session.character.Characters.FirstOrDefault(candidate => candidate.Id == (uint)characterId);
            if (character is null)
                throw new CommandMessageCallbackException($"{table.LogName} (ID {characterId}) is not on this account. Use /character add {table.TradeName} first.");

            string selectedRank = rankText.ToUpperInvariant();
            int quality = selectedRank switch
            {
                "B" => 1,
                "A" => 2,
                "S" => 3,
                "SS" => 4,
                "SSS" or "SSS3" or "SSS6" => 5,
                "SSS+" => 6,
                _ => Miscs.ParseIntOr(rankText)
            };
            bool supportedQuality = TableReaderV2.Parse<CharacterQualityTable>()
                .Any(row => row.CharacterId == characterId && row.Quality == quality);
            if (!supportedQuality)
                throw new CommandMessageCallbackException($"Quality {quality} is not available for {table.LogName} (ID {characterId}).");

            int star = string.IsNullOrWhiteSpace(starText) ? selectedRank switch
            {
                "SSS" or "SSS3" => 3,
                "SSS6" => 6,
                "SSS+" => 0,
                _ => character.Star
            } : Miscs.ParseIntOr(starText);
            if (quality == 6)
                star = 0;
            if (star < 0 || star > 9)
                throw new CommandMessageCallbackException("Evolution stars must be between 0 and 9.");

            List<CharacterSkill> skillsBeforeSync = character.SkillList.ToList();
            character.Quality = quality;
            character.Star = star;
            session.character.UnlockQualityGatedSkills(character, session.player.GatherRewards, synchronizeToRank: true);
            session.character.SaveChecked();
            // Client XCharacter:UpdateSkillData merges by skill group and never clears a group missing from the list,
            // so a rank-down must first state each removed skill at Level 0 (locked). The follow-up push restores the
            // real list on the client; the Level 0 rows are never persisted.
            List<CharacterSkill> removedSkills = skillsBeforeSync
                .Where(before => character.SkillList.All(after => after.Id != before.Id))
                .Select(before => new CharacterSkill { Id = before.Id, Level = 0 })
                .ToList();
            if (removedSkills.Count > 0)
            {
                character.SkillList.AddRange(removedSkills);
                session.SendPush(new NotifyCharacterDataList { CharacterDataList = { character } });
                character.SkillList.RemoveAll(removedSkills.Contains);
            }
            session.SendPush(new NotifyCharacterDataList { CharacterDataList = { character } });
            string rank = quality switch { 1 => "B", 2 => "A", 3 => "S", 4 => "SS", 5 => "SSS", 6 => "SSS+", _ => $"quality {quality}" };
            string evolution = star > 0 ? $"{rank}{star}" : rank;
            throw new CommandMessageCallbackException($"{table.LogName} (ID {characterId}) set to {evolution}.");
        }
    }
}
