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

        public override string Help => "Use /character add <id|all> or /character rank <id> <SSS3|SSS6|SSS+|quality> [star 0-9].";

        [Argument(0, @"^add$|^rank$", "The operation selected (add, rank)", ArgumentFlags.IgnoreCase)]
        string Op { get; set; } = string.Empty;

        [Argument(1, @"^[0-9]+$|^all$", "The target character, value is character id or 'all'")]
        string Target { get; set; } = string.Empty;

        [Argument(2, @"^[0-6]$|^(B|A|S|SS|SSS|SSS3|SSS6|SSS\+)$", "Target rank or character quality value", ArgumentFlags.Optional | ArgumentFlags.IgnoreCase)]
        string Rank { get; set; } = string.Empty;

        [Argument(3, @"^[0-9]$", "Target evolution stars (0-9)", ArgumentFlags.Optional)]
        string Star { get; set; } = string.Empty;

        public override void Execute()
        {
            int id = Miscs.ParseIntOr(Target);

            switch (Op.ToLowerInvariant())
            {
                case "add":
                    RewardApplicationResult result;
                    if (Target == "all")
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
                        result = RewardHandler.ApplyRewards([ new Reward() { Id = id, Type = RewardType.Character } ], session);
                    }
                    session.inventory.SaveChecked();
                    session.character.SaveChecked();
                    if (result.DormFurnitureChanged || result.GatherRewardIds.Count > 0 || result.HeadPortraitData.Heads.Count > 0)
                        session.player.SaveChecked();
                    result.SendPushes(session);
                    break;
                case "rank":
                    SetRank(id);
                    break;
                default:
                    throw new InvalidOperationException("Invalid operation!");
            }
        }

        private void SetRank(int characterId)
        {
            if (Target == "all" || string.IsNullOrWhiteSpace(Rank))
                throw new CommandMessageCallbackException("Usage: /character rank <characterId> <SSS3|SSS6|SSS+|quality> [star 0-9]");

            CharacterData? character = session.character.Characters.FirstOrDefault(candidate => candidate.Id == (uint)characterId);
            CharacterTable? table = TableReaderV2.Parse<CharacterTable>().FirstOrDefault(candidate => candidate.Id == characterId);
            if (character is null || table is null)
                throw new CommandMessageCallbackException($"Character {characterId} is missing from this account or character table. Use /character add {characterId} if needed.");

            string selectedRank = Rank.ToUpperInvariant();
            int quality = selectedRank switch
            {
                "B" => 1,
                "A" => 2,
                "S" => 3,
                "SS" => 4,
                "SSS" or "SSS3" or "SSS6" => 5,
                "SSS+" => 6,
                _ => Miscs.ParseIntOr(Rank)
            };
            bool supportedQuality = TableReaderV2.Parse<CharacterQualityTable>()
                .Any(row => row.CharacterId == characterId && row.Quality == quality);
            if (!supportedQuality)
                throw new CommandMessageCallbackException($"Quality {quality} is not available for character {characterId}.");

            int star = string.IsNullOrWhiteSpace(Star) ? selectedRank switch
            {
                "SSS" or "SSS3" => 3,
                "SSS6" => 6,
                "SSS+" => 0,
                _ => character.Star
            } : Miscs.ParseIntOr(Star);
            if (quality == 6)
                star = 0;
            if (star < 0 || star > 9)
                throw new CommandMessageCallbackException("Evolution stars must be between 0 and 9.");

            character.Quality = quality;
            character.Star = star;
            session.character.UnlockQualityGatedSkills(character, session.player.GatherRewards, synchronizeToRank: true);
            session.character.SaveChecked();
            session.SendPush(new NotifyCharacterDataList { CharacterDataList = { character } });
            string rank = quality switch { 1 => "B", 2 => "A", 3 => "S", 4 => "SS", 5 => "SSS", 6 => "SSS+", _ => $"quality {quality}" };
            string evolution = star > 0 ? $"{rank}{star}" : rank;
            throw new CommandMessageCallbackException($"{table.Name} (ID {characterId}) set to {evolution}.");
        }
    }
}
