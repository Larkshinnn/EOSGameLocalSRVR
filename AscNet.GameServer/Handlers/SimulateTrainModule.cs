using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.GameServer.Game;
using AscNet.Common.Util;
using AscNet.Table.V2.share.fuben.simulatetrain;
using MessagePack;

namespace AscNet.GameServer.Handlers;

[MessagePackObject(true)]
public sealed class SimulateTrainFightResultData
{
    public int AtkLevel { get; set; }
    public int HpLevel { get; set; }
    public int Difficulty { get; set; }
    public long FightTime { get; set; }
}

[MessagePackObject(true)]
public sealed class SimulateTrainNpcGroupData
{
    public List<SimulateTrainNpcData> NpcList { get; set; } = [];
}

[MessagePackObject(true)]
public sealed class SimulateTrainNpcData
{
    public int NpcId { get; set; }
    public List<int> BufferIds { get; set; } = [];
    public int Level { get; set; }
    public List<object> MagicInfos { get; set; } = [];
    public Dictionary<int, object> AttrTable { get; set; } = [];
}

internal static class SimulateTrainModule
{
    private const int FightFramesPerSecond = 20;
    private const int InvalidPreFightData = 1; // Retail code unobserved; any non-zero rejects the request.

    private sealed record Data(
        IReadOnlyDictionary<uint, SimulateTrainMonsterTable> MonstersByStage,
        IReadOnlyDictionary<int, SimulateTrainAtkTable> AttackLevels,
        IReadOnlyDictionary<int, SimulateTrainHpTable> HealthLevels,
        IReadOnlyDictionary<(int BossId, int Period), int> PeriodBuffs);

    private static readonly Lazy<Data> Runtime = new(Load);

    public static bool IsStage(uint stageId) => Runtime.Value.MonstersByStage.ContainsKey(stageId);

    public static bool TryApplyPreFight(
        PreFightRequest.PreFightRequestPreFightData request,
        PreFightResponse.PreFightResponseFightData fightData,
        DateTimeOffset now,
        out int code)
    {
        code = 0;
        if (!Runtime.Value.MonstersByStage.TryGetValue(request.StageId, out SimulateTrainMonsterTable? monster))
            return false;

        PreFightRequest.PreFightRequestPreFightData.SimulateTrainInfoData? info = request.SimulateTrainInfo;
        int difficultyIndex = (info?.Difficulty ?? 0) - 1;
        if (info is null
            || info.BossId != monster.Id
            || difficultyIndex < 0
            || difficultyIndex >= monster.NpcId.Count
            || difficultyIndex >= monster.NpcLevel.Count
            || difficultyIndex >= monster.StageBuffId.Count
            || !Runtime.Value.AttackLevels.TryGetValue(info.AtkLevel, out SimulateTrainAtkTable? attack)
            || !Runtime.Value.HealthLevels.TryGetValue(info.HpLevel, out SimulateTrainHpTable? health))
        {
            code = InvalidPreFightData;
            return true;
        }

        bool hasPeriodBuff = Runtime.Value.PeriodBuffs.TryGetValue(
            (info.BossId, info.Period),
            out int periodBuffId);
        if (info.Period < 1 || (info.Period > 1 && !hasPeriodBuff))
        {
            code = InvalidPreFightData;
            return true;
        }

        bool isImpasseDifficulty = monster.ImpasseTimeId > 0
            && difficultyIndex == monster.NpcId.Count - 1;
        if (!IsTimeOpen(monster.TimeId, now)
            || (isImpasseDifficulty && !IsTimeOpen(monster.ImpasseTimeId, now)))
        {
            code = FashionStoryModule.StageLocked;
            return true;
        }

        List<int> bufferIds = [monster.StageBuffId[difficultyIndex]];
        if (hasPeriodBuff)
            bufferIds.Add(periodBuffId);
        bufferIds.Add(attack.AtkBuffId);
        bufferIds.Add(health.HpBuffId);
        bufferIds = bufferIds.Where(id => id > 0).Distinct().ToList();

        fightData.FightCheckType = 1;
        fightData.SegmentFightCheckSecond = 60;
        fightData.MonsterLevel = null;
        fightData.EventIds = [];
        fightData.FightEventsWithLevel = new List<dynamic>();
        fightData.NormalEventIds = [2];
        fightData.NpcGroupList = new List<SimulateTrainNpcGroupData>
        {
            new()
            {
                NpcList =
                [
                    new()
                    {
                        NpcId = monster.NpcId[difficultyIndex],
                        BufferIds = bufferIds,
                        Level = monster.NpcLevel[difficultyIndex],
                    }
                ]
            }
        };
        fightData.Records = new Dictionary<string, dynamic>();
        fightData.StageParams = new Dictionary<string, dynamic>();
        fightData.Restartable = true;
        return true;
    }

    public static SimulateTrainFightResultData? BuildFightResult(
        PreFightRequest.PreFightRequestPreFightData? preFight,
        FightSettleResult settle)
    {
        PreFightRequest.PreFightRequestPreFightData.SimulateTrainInfoData? info = preFight?.SimulateTrainInfo;
        if (preFight is null || info is null || !IsStage(settle.StageId))
            return null;

        long activeFrames = Math.Max(0, settle.SettleFrame - settle.StartFrame - settle.PauseFrame);
        return new SimulateTrainFightResultData
        {
            AtkLevel = info.AtkLevel,
            HpLevel = info.HpLevel,
            Difficulty = info.Difficulty,
            FightTime = activeFrames / FightFramesPerSecond,
        };
    }

    public static NotifyArchiveMonsterRecord? RecordArchiveKill(
        Player player,
        PreFightRequest.PreFightRequestPreFightData? preFight,
        FightSettleResult settle)
    {
        PreFightRequest.PreFightRequestPreFightData.SimulateTrainInfoData? info = preFight?.SimulateTrainInfo;
        if (!settle.IsWin
            || settle.IsForceExit
            || info is null
            || !Runtime.Value.MonstersByStage.TryGetValue(settle.StageId, out SimulateTrainMonsterTable? monster))
        {
            return null;
        }

        int difficultyIndex = info.Difficulty - 1;
        if (difficultyIndex < 0 || difficultyIndex >= monster.NpcId.Count)
            return null;

        int npcId = monster.NpcId[difficultyIndex];
        player.ArchiveMonsterKills ??= [];
        int killed = player.ArchiveMonsterKills.TryGetValue(npcId, out int previous)
            ? checked(previous + 1)
            : 1;
        player.ArchiveMonsterKills[npcId] = killed;
        return new NotifyArchiveMonsterRecord
        {
            Monsters =
            [
                new()
                {
                    Id = checked((uint)npcId),
                    Killed = checked((uint)killed),
                }
            ]
        };
    }

    private static bool IsTimeOpen(int timeId, DateTimeOffset now) =>
        timeId == 0 || ActivityScheduleService.IsOpen(timeId, now);

    private static Data Load()
    {
        Dictionary<uint, SimulateTrainMonsterTable> monsters = TableReaderV2.Parse<SimulateTrainMonsterTable>()
            .ToDictionary(monster => checked((uint)monster.StageId));
        Dictionary<int, SimulateTrainAtkTable> attackLevels = TableReaderV2.Parse<SimulateTrainAtkTable>()
            .ToDictionary(level => level.AtkLevel);
        Dictionary<int, SimulateTrainHpTable> healthLevels = TableReaderV2.Parse<SimulateTrainHpTable>()
            .ToDictionary(level => level.HpLevel);
        Dictionary<(int BossId, int Period), int> periodBuffs = TableReaderV2.Parse<SimulateTrainPeriodBuffTable>()
            .ToDictionary(row => (row.BossId, row.Period), row => row.BuffId);

        return new Data(monsters, attackLevels, healthLevels, periodBuffs);
    }
}
