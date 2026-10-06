using AscNet.Common.Database;
using AscNet.Common.Util;
using AscNet.Table.V2.share.dormitory.character;

namespace AscNet.GameServer.Handlers;

internal partial class DormModule
{
    private static bool EvaluateEvents(Session session, uint now)
    {
        int refresh = Config().GetValueOrDefault("DormEventRefreshTime");
        if (refresh <= 0)
            return false;

        bool changed = session.player.Dorm.Characters.Sum(character =>
            character.EventList.RemoveAll(evt => evt.EndTime > 0 && evt.EndTime <= now)) > 0;
        if (now < session.player.Dorm.EventNextRefreshTime)
            return changed;

        Dictionary<uint, List<DormCharacterEventTable>> events = TableReaderV2.Parse<DormCharacterEventTable>()
            .Where(row => row.CharacterId > 0 && row.EventId > 0 && row.Weight > 0)
            .GroupBy(row => (uint)row.CharacterId)
            .ToDictionary(group => group.Key, group => group.ToList());
        bool generated = false;
        foreach (PlayerDormCharacter character in session.player.Dorm.Characters)
        {

            if (character.DormitoryId < 0 || character.EventList.Count > 0
                || session.player.Dorm.WorkList.Any(work => work.CharacterId == character.CharacterId && work.WorkEndTime > now))
                continue;
            if (!events.TryGetValue(character.CharacterId, out List<DormCharacterEventTable>? candidates))
                continue;
            int total = candidates.Sum(row => row.Weight);
            int roll = Random.Shared.Next(total);
            DormCharacterEventTable selected = candidates.First(row => (roll -= row.Weight) < 0);
            character.EventList.Add(new PlayerDormEvent { EventId = selected.EventId, EndTime = checked(now + (uint)refresh) });
            generated = changed = true;
        }
        if (generated)
            session.player.Dorm.EventNextRefreshTime = checked(now + (uint)refresh);
        return changed;
    }

    private static List<DormCharacterEvent> EventResponses(Session session) => session.player.Dorm.Characters.Select(character => new DormCharacterEvent
    {
        CharacterId = character.CharacterId,
        EventList = character.EventList.Select(evt => new DormEvent { EventId = evt.EventId, EndTime = evt.EndTime }).ToList()
    }).ToList();
}
