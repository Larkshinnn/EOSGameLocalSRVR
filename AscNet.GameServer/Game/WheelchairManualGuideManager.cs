using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer.Handlers;
using AscNet.Table.V2.share.wheelchairmanual;
using AscNet.Table.V2.client.functional;
using AscNet.Table.V2.share.functional;
using AscNet.Table.V2.share.reward;
using AscNet.Table.V2.share.fuben.bosssingle;
using TaskTable = AscNet.Table.V2.share.task.TaskTable;

namespace AscNet.GameServer.Game;

public static class WheelchairManualGuideManager
{
    private static readonly Lazy<IReadOnlyList<WheelchairManualGuideActivityTable>> Activities = new(() =>
        TableReaderV2.Parse<WheelchairManualGuideActivityTable>().OrderBy(row => row.Id).ToArray());
    private static readonly Lazy<IReadOnlyDictionary<int, WheelchairManualGuideActivityPeriodTable>> Periods = new(() =>
        TableReaderV2.Parse<WheelchairManualGuideActivityPeriodTable>().ToDictionary(row => row.Id));
    private static readonly Lazy<IReadOnlyList<WheelchairManualGuideWeekActivityTable>> Weeks = new(() =>
        TableReaderV2.Parse<WheelchairManualGuideWeekActivityTable>().OrderBy(row => row.Id).ToArray());
    private static readonly Lazy<IReadOnlyDictionary<int, WheelchairManualGuideWeekRewardTable[]>> WeekRewards = new(() =>
        TableReaderV2.Parse<WheelchairManualGuideWeekRewardTable>().GroupBy(row => row.MainId)
            .ToDictionary(group => group.Key, group => group.ToArray()));
    private static readonly Lazy<IReadOnlyDictionary<int, TaskTable[]>> SourceTasks = new(() =>
        TableReaderV2.Parse<TaskTable>().Where(row => row.SourceGroupId is > 0)
            .GroupBy(row => row.SourceGroupId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray()));
    private static readonly Lazy<IReadOnlyDictionary<int, SkipFunctionalTable>> Skips = new(() =>
        TableReaderV2.Parse<SkipFunctionalTable>().ToDictionary(row => row.SkipId));
    private static readonly Lazy<IReadOnlyDictionary<int, FunctionalOpenTable>> Functions = new(() =>
        TableReaderV2.Parse<FunctionalOpenTable>().ToDictionary(row => row.Id));
    private static readonly Lazy<IReadOnlyDictionary<int, BossSingleGradeTable>> BossGrades = new(() =>
        TableReaderV2.Parse<BossSingleGradeTable>().ToDictionary(row => row.LevelType));

    public static int? GetUniqueWeeklyEventCause(int chapterType)
    {
        int[] causes = Weeks.Value.Where(row => row.ChapterType == chapterType)
            .SelectMany(row => WeekRewards.Value.GetValueOrDefault(row.Id) ?? [])
            .SelectMany(row => row.EventCause).Where(cause => cause > 0).Distinct().Take(2).ToArray();
        return causes.Length == 1 ? causes[0] : null;
    }

    // Mutation only: the reward transaction persists this with its other player changes.
    // Replace the list, never mutate existing receipts, so the caller can restore its reference.
    public static void RecordReward(Session session, string claimKey, int eventCause,
        IReadOnlyList<RewardGoodsTable> goods, DateTimeOffset now)
    {
        if (session.player.WheelchairManualGuideRewardReceipts.Any(receipt => receipt.ClaimKey == claimKey))
            return;
        if (!Activities.Value.Any(row => row.EventCause.Contains(eventCause))
            && !WeekRewards.Value.Values.SelectMany(rows => rows).Any(row => row.EventCause.Contains(eventCause)))
            return;
        session.player.WheelchairManualGuideRewardReceipts =
        [
            .. session.player.WheelchairManualGuideRewardReceipts,
            new()
            {
                ClaimKey = claimKey,
                EventCause = eventCause,
                GrantedAt = now.ToUnixTimeSeconds(),
                Goods = goods.Where(row => row.Count > 0).Select(row => new WheelchairManualGuideRewardCount
                {
                    TemplateId = row.TemplateId,
                    Count = row.Count
                }).ToList()
            }
        ];
    }

    // This is the client's total-time gate, including its explicit defaultOpen=true.
    // Missing schedule metadata is not evidence that an activity is closed or permanent.
    private static bool IsGuideOpen(int timeId, DateTimeOffset now) =>
        !ActivityScheduleService.TryGet(timeId, out ActivityScheduleEntry schedule) || schedule.IsOpen(now);

    public static IEnumerable<long> GetEligibleRedPointIds(Session session, DateTimeOffset now) =>
        Activities.Value.Where(row => IsGuideOpen(row.TimeId, now)
                && PayModule.AreConditionsSatisfied(session, new[] { row.ConditionId }.Where(id => id > 0))
                && CanSkip(session, row.SkipId)).Select(row => row.Id)
            .Concat(Weeks.Value.Where(row => CanSkip(session, row.SkipId)).Select(row => row.Id))
            .Select(id => (7L << 32) + id);

    private static bool CanSkip(Session session, int skipId)
    {
        if (!Skips.Value.TryGetValue(skipId, out var skip))
            return false;
        return skip.FunctionalId is not > 0
            || Functions.Value.TryGetValue(skip.FunctionalId.Value, out var function)
                && PayModule.AreConditionsSatisfied(session, function.Condition.Where(id => id > 0));
    }

    public static void PopulatePayload(Session session, DateTimeOffset now, NotifyWheelchairManualActivity payload)
    {
        payload.OpenActivityIds = Activities.Value.Where(row => IsGuideOpen(row.TimeId, now))
            .Select(row => row.Id).ToList();
        payload.TimeLimitActivityInfos = BuildTimedProgress(session, now, payload.OpenActivityIds);
        payload.WeekActivityInfos = BuildWeeklyProgress(session, now);
        payload.CurrentGuildBossEndTime = AccountModule.GetCurrentGuildBossEndTime(now);
    }

    public static NotifyWheelchairManualActivityUpdate BuildUpdate(Session session, DateTimeOffset now) => new()
    {
        UpdateTimeLimitActivityInfos = BuildTimedProgress(session, now,
            Activities.Value.Where(row => IsGuideOpen(row.TimeId, now)).Select(row => row.Id).ToList()),
        UpdateWeekActivityInfos = BuildWeeklyProgress(session, now),
        CurrentGuildBossEndTime = AccountModule.GetCurrentGuildBossEndTime(now)
    };

    public static void SendUpdate(Session session) => session.SendPush(BuildUpdate(session, DateTimeOffset.UtcNow));

    private static List<object> BuildTimedProgress(Session session, DateTimeOffset now, List<int> openIds)
    {
        List<object> result = [];
        foreach (WheelchairManualGuideActivityTable activity in Activities.Value)
        {
            if (!openIds.Contains(activity.Id) || !Periods.Value.TryGetValue(activity.PeriodIds, out var period)
                || !IsGuideOpen(period.TimeId, now))
                continue;
            result.Add(new Dictionary<string, object>
            {
                ["ActivityId"] = activity.Id,
                ["PeriodInfos"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["PeriodId"] = period.Id,
                        ["GotRewards"] = ClaimedTaskRewards(session, now, activity.TaskSourceGroupId,
                            period.MainTemplateIds, activity.EventCause,
                            ActivityScheduleService.TryGet(period.TimeId, out var schedule) ? schedule.StartTime : 0)
                    }
                }
            });
        }
        return result;
    }

    private static List<object> BuildWeeklyProgress(Session session, DateTimeOffset now)
    {
        List<object> result = [];
        foreach (WheelchairManualGuideWeekActivityTable activity in Weeks.Value)
        {
            if (!WeekRewards.Value.TryGetValue(activity.Id, out var rewards))
                continue;
            int subId = rewards.Length == 1 ? rewards[0].SubId : activity.ChapterType switch
            {
                5 => BossGrades.Value.TryGetValue(
                    session.player.SimulatedBattlefield.BossLevelType > 0
                        ? session.player.SimulatedBattlefield.BossLevelType
                        : session.player.SimulatedBattlefield.BossOldLevelType, out var grade) ? grade.GradeType : 0,
                44 => session.player.Stronghold.LevelId,
                91 => session.player.Transfinite?.RegionId ?? 0,
                _ => 0
            };
            WheelchairManualGuideWeekRewardTable? reward = rewards.SingleOrDefault(row => row.SubId == subId);
            if (reward is null)
                continue;
            long beginTime = activity.ChapterType switch
            {
                44 => session.player.Stronghold.BeginTime,
                91 => session.player.Transfinite?.BeginTime ?? 0,
                null => AccountModule.GetCurrentGuildBossEndTime(now) - (long)TimeSpan.FromDays(7).TotalSeconds,
                _ => now.ToUnixTimeSeconds() + TaskModule.RemainingSecondsInWeeklyResetPeriod(now.ToUnixTimeSeconds())
                    - (long)TimeSpan.FromDays(7).TotalSeconds
            };
            result.Add(new Dictionary<string, object>
            {
                ["MainId"] = activity.Id,
                ["SubId"] = reward.SubId,
                ["GotRewards"] = ClaimedTaskRewards(session, now, reward.TaskSourceGroupId,
                    reward.MainTemplateId, reward.EventCause, beginTime)
            });
        }
        return result;
    }

    private static List<object> ClaimedTaskRewards(Session session, DateTimeOffset now,
        int? sourceGroupId, IReadOnlyList<int> templateIds, IReadOnlyList<int> eventCauses, long beginTime)
    {
        Dictionary<int, int> counts = [];
        if (sourceGroupId is > 0 && SourceTasks.Value.TryGetValue(sourceGroupId.Value, out var tasks))
        {
            MissionProgressState state = session.player.MissionProgress;
            long timestamp = now.ToUnixTimeSeconds();
            foreach (TaskTable task in tasks)
            {
                if (!state.ClaimedTaskIds.Contains(task.Id) && !session.stage.FinishedTasks.Contains(task.Id))
                    continue;
                if (task.Type == 2 && state.DailyResetDay != TaskModule.CurrentDailyResetPeriod(timestamp)
                    || task.Type == 3 && state.WeeklyResetWeek != TaskModule.CurrentWeeklyResetPeriod(timestamp))
                    continue;
                foreach (var goods in RewardHandler.GetRewardGoods(task.RewardId ?? 0))
                    if (templateIds.Contains(goods.TemplateId))
                        counts[goods.TemplateId] = checked(counts.GetValueOrDefault(goods.TemplateId) + goods.Count);
            }
        }
        foreach (WheelchairManualGuideRewardReceipt receipt in session.player.WheelchairManualGuideRewardReceipts)
            if (receipt.GrantedAt >= beginTime && receipt.GrantedAt <= now.ToUnixTimeSeconds()
                && eventCauses.Contains(receipt.EventCause))
                foreach (WheelchairManualGuideRewardCount goods in receipt.Goods)
                    if (templateIds.Contains(goods.TemplateId))
                        counts[goods.TemplateId] = checked(counts.GetValueOrDefault(goods.TemplateId) + goods.Count);
        return counts.OrderBy(entry => entry.Key).Select(entry => (object)new Dictionary<string, object>
        {
            ["TemplateId"] = entry.Key,
            ["Count"] = entry.Value
        }).ToList();
    }
}
