using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer.Game;
using AscNet.Table.V2.client.wheelchairmanual;
using AscNet.Table.V2.share.reward;
using AscNet.Table.V2.share.fuben;
using AscNet.Table.V2.share.wheelchairmanual;
using MessagePack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace AscNet.GameServer.Handlers;

[MessagePackObject(true)]
public sealed class WheelchairManualPurchaseRequest
{
    public int? ManualId { get; set; }
}

[MessagePackObject(true)]
public sealed class WheelchairManualPurchaseResponse
{
    public int Code { get; set; }
}

[MessagePackObject(true)]
public sealed class WheelchairManualGetPlanRewardResponse
{
    public int Code { get; set; }
    public List<RewardGoods> RewardList { get; set; } = [];
}

[MessagePackObject(true)]
public sealed class WheelchairManualGetManualRewardRequest
{
    public int? ManualId { get; set; }
}

[MessagePackObject(true)]
public sealed class WheelchairManualGetManualRewardResponse
{
    public int Code { get; set; }
    public List<RewardGoods> RewardList { get; set; } = [];
}

[MessagePackObject(true)]
public sealed class WheelchairManualClickBluePointRequest
{
    public int? Type { get; set; }
}

[MessagePackObject(true)]
public sealed class WheelchairManualClickBluePointResponse
{
    public int Code { get; set; }
}

[MessagePackObject(true)]
public sealed class WheelchairManualClickRedPointRequest
{
    public long? Id { get; set; }
}

[MessagePackObject(true)]
public sealed class WheelchairManualClickRedPointResponse
{
    public int Code { get; set; }
}

internal static class WheelchairManualModule
{
    private static readonly Lazy<int> ExperienceItem = new(() => int.Parse(
        TableReaderV2.Parse<WheelchairManualClientConfigTable>()
            .Single(row => row.Key == "WheelchairManualBpExp").Values[0],
        System.Globalization.CultureInfo.InvariantCulture));

    public static int ExperienceItemId => ExperienceItem.Value;

    private static WheelchairManualState State(Session session, int activityId) =>
        session.player.WheelchairManualStates.GetValueOrDefault(activityId) ?? new();

    private static string LevelReceipt(Session session, int activityId, int level) =>
        $"wheelchair-level:{session.player.PlayerData.Id}:{activityId}:{level}";

    private static List<WheelchairManualBattlePassLevelTable> Levels(WheelchairManualActivityTable activity) =>
        TableReaderV2.Parse<WheelchairManualBattlePassLevelTable>()
            .Where(row => row.Id >= activity.BpLevelConfig[0] && row.Id <= activity.BpLevelConfig[1])
            .OrderBy(row => row.Id).ToList();

    private static int LevelIndex(Session session, WheelchairManualActivityTable activity,
        IReadOnlyList<WheelchairManualBattlePassLevelTable> levels)
    {
        int index = 0;
        while (index + 1 < levels.Count && (Convert.ToInt32(levels[index].NeedExp) == 0
            || session.inventory.AppliedRewardClaims.Contains(
                LevelReceipt(session, activity.Id, Convert.ToInt32(levels[index + 1].Level)))))
            index++;
        return index;
    }

    // The client renders inventory EXP / the current level's NeedExp, not total earned EXP.
    // Each promotion debits that remainder and records its level in the SAME inventory document.
    // A player save failing after the inventory save therefore cannot consume EXP twice on relog.
    public static bool RefreshProgress(Session session)
    {
        if (Activity(out WheelchairManualActivityTable? activity) != 0 || activity is null)
            return false;
        List<WheelchairManualBattlePassLevelTable> levels = Levels(activity);
        int index = LevelIndex(session, activity, levels);
        long experience = session.inventory.Items.FirstOrDefault(item => item.Id == ExperienceItemId)?.Count ?? 0;
        List<RewardGrant> grants = [];
        while (index + 1 < levels.Count)
        {
            int needed = Convert.ToInt32(levels[index].NeedExp);
            if (needed <= 0 || experience < needed)
                break;
            experience -= needed;
            index++;
            grants.Add(new RewardGrant(LevelReceipt(session, activity.Id, Convert.ToInt32(levels[index].Level)),
                [], new Dictionary<int, int> { [ExperienceItemId] = needed }));
        }
        if (grants.Count == 0)
            return false;
        RewardHandler.ApplyRewardsOnceAndPersist(grants, session);
        return true;
    }
    public static bool IsActive(DateTimeOffset now) => Activity(out _, now) == 0;


    public static NotifyWheelchairManualActivity BuildPayload(Session session, DateTimeOffset now)
    {
        if (Activity(out WheelchairManualActivityTable? activity, now) != 0 || activity is null)
            return new();
        WheelchairManualState state = State(session, activity.Id);
        List<int> claimedPlans = session.player.WheelchairManualClaimedPlanIds.GetValueOrDefault(activity.Id) ?? [];
        List<WheelchairManualBattlePassLevelTable> levels = Levels(activity);
        NotifyWheelchairManualActivity payload = new()
        {
            ActivityId = activity.Id,
            PlanId = activity.PlanIds.FirstOrDefault(id => !claimedPlans.Contains(id), activity.PlanIds.Last()),
            GetRewardPlanIds = claimedPlans.ToList(),
            BpLevel = Convert.ToInt32(levels[LevelIndex(session, activity, levels)].Level),
            IsSeniorManualUnlock = state.IsSeniorManualUnlock,
            GetRewardManualRewardIds = state.ClaimedRewardIds.ToList(),
            FinishStageIds = activity.TeachCommonStageIds.Prepend(activity.TeachConnectivityStageId)
                .Where(id => session.stage is not null
                    && session.stage.Stages.TryGetValue(id, out StageDatum? data) && data.Passed).ToList(),
            BluePointSet = EligibleBluePointTypes(session)
                .Where(id => !state.AcknowledgedBluePoints.Contains(id)).ToList(),
            RedPointSet = EligibleRedPointIds(session, activity, now)
                .Where(id => !state.AcknowledgedRedPoints.Contains(id)).Distinct().ToList(),
            CurrentGuildBossEndTime = AccountModule.GetCurrentGuildBossEndTime(now)
        };
        WheelchairManualGuideManager.PopulatePayload(session, now, payload);
        return payload;
    }

    private static int Activity(out WheelchairManualActivityTable? activity, DateTimeOffset? clock = null)
    {
        activity = TableReaderV2.Parse<WheelchairManualActivityTable>().SingleOrDefault();
        if (activity is null)
            return 20236004;
        // EN GetIsOpen uses the current ActivityId and optional CountDown, not TimeId.
        // The current authoritative manual has no countdown; an absent shared schedule is not closure.
        if (!ActivityScheduleService.TryGet(activity.TimeId, out ActivityScheduleEntry schedule))
            return 0;
        long now = (clock ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        if (schedule.StartTime != 0 && now < schedule.StartTime)
            return 20236001;
        return schedule.EndTime != 0 && now >= schedule.EndTime ? 20236002 : 0;
    }

    private static T? Request<T>(Packet.Request packet) where T : class
    {
        try { return packet.Deserialize<T>(); }
        catch (MessagePackSerializationException) { return null; }
    }

    private static void SaveState(Session session, int activityId, Action<WheelchairManualState> change)
    {
        WheelchairManualState? old = session.player.WheelchairManualStates.GetValueOrDefault(activityId);
        WheelchairManualState staged = old is null ? new() : BsonSerializer.Deserialize<WheelchairManualState>(old.ToBson());
        change(staged);
        session.player.WheelchairManualStates[activityId] = staged;
        try { session.player.SaveChecked(); }
        catch
        {
            if (old is null)
                session.player.WheelchairManualStates.Remove(activityId);
            else
                session.player.WheelchairManualStates[activityId] = old;
            throw;
        }
    }

    [RequestPacketHandler("WheelchairManualPurchaseRequest")]
    public static void WheelchairManualPurchaseRequestHandler(Session session, Packet.Request packet)
    {
        WheelchairManualPurchaseResponse response = new();
        response.Code = Activity(out WheelchairManualActivityTable? activity);
        if (response.Code == 0)
        {
            WheelchairManualPurchaseRequest? request = Request<WheelchairManualPurchaseRequest>(packet);
            if (request?.ManualId != activity!.SeniorBattlePassManualId)
                response.Code = 20236013;
            else if (State(session, activity.Id).IsSeniorManualUnlock)
                response.Code = 20236011;
            else
            {
                WheelchairManualBattlePassManualTable? manual = TableReaderV2.Parse<WheelchairManualBattlePassManualTable>()
                    .SingleOrDefault(row => row.Id == request.ManualId);
                if (manual is null)
                    response.Code = 20236006;
                else
                {
                    int itemId = Convert.ToInt32(manual.ConsumeItemId);
                    int cost = Convert.ToInt32(manual.ConsumeItemCount);
                    string receipt = $"wheelchair-purchase:{session.player.PlayerData.Id}:{activity.Id}:{manual.Id}";
                    if (!Inventory.IsValidClientItemId(itemId) || cost <= 0)
                        response.Code = 20236014;
                    else if (!session.inventory.AppliedRewardClaims.Contains(receipt)
                        && (session.inventory.Items.FirstOrDefault(item => item.Id == itemId)?.Count ?? 0) < cost)
                        response.Code = 20012004;
                    else
                    {
                        RewardApplicationResult application = RewardHandler.ApplyRewardsOnceAndPersist(
                            [new RewardGrant(receipt, [], new Dictionary<int, int> { [itemId] = cost })], session);
                        SaveState(session, activity.Id, state => state.IsSeniorManualUnlock = true);
                        application.SendPushes(session);
                        session.SendPush(BuildPayload(session, DateTimeOffset.UtcNow));
                    }
                }
            }
        }
        session.SendResponse(response, packet.Id);
    }

    [RequestPacketHandler("WheelchairManualGetPlanRewardRequest")]
    public static void WheelchairManualGetPlanRewardRequestHandler(Session session, Packet.Request packet)
    {
        WheelchairManualGetPlanRewardResponse response = new();
        response.Code = Activity(out WheelchairManualActivityTable? activity);
        if (response.Code != 0)
        {
            session.SendResponse(response, packet.Id);
            return;
        }
        List<int>? claimedPlans = session.player.WheelchairManualClaimedPlanIds.GetValueOrDefault(activity!.Id);
        int planId = activity.PlanIds.FirstOrDefault(id => claimedPlans?.Contains(id) != true, activity.PlanIds.Last());
        WheelchairManualBattlePassPlanTable? plan = TableReaderV2.Parse<WheelchairManualBattlePassPlanTable>()
            .SingleOrDefault(row => row.Id == planId);
        if (claimedPlans?.Contains(planId) == true)
            response.Code = 20236009;
        else if (plan is null)
            response.Code = 20236005;
        // Client GetTaskProgressByTaskList counts Finish, not Achieved.
        else if (plan.TaskIds.Count == 0 || !plan.TaskIds.All(session.player.MissionProgress.ClaimedTaskIds.Contains))
            response.Code = 20236010;
        else
        {
            List<RewardGoodsTable> goods = RewardHandler.GetRewardGoods(plan.RewardId);
            if (goods.Count == 0)
                response.Code = 20236007;
            else
            {
                RewardApplicationResult application = RewardHandler.ApplyRewardsOnceAndPersist(
                    [new RewardGrant($"wheelchair-plan:{session.player.PlayerData.Id}:{activity.Id}:{plan.Id}", goods)], session);
                session.player.WheelchairManualClaimedPlanIds[activity.Id] = [.. claimedPlans ?? [], plan.Id];
                try { session.player.SaveChecked(); }
                catch
                {
                    if (claimedPlans is null)
                        session.player.WheelchairManualClaimedPlanIds.Remove(activity.Id);
                    else
                        session.player.WheelchairManualClaimedPlanIds[activity.Id] = claimedPlans;
                    throw;
                }
                application.SendPushes(session);
                session.SendPush(BuildPayload(session, DateTimeOffset.UtcNow));
                response.RewardList = application.RewardGoods;
            }
        }
        session.SendResponse(response, packet.Id);
    }

    [RequestPacketHandler("WheelchairManualGetManualRewardRequest")]
    public static void WheelchairManualGetManualRewardRequestHandler(Session session, Packet.Request packet)
    {
        WheelchairManualGetManualRewardResponse response = new();
        response.Code = Activity(out WheelchairManualActivityTable? activity);
        if (response.Code != 0)
        {
            session.SendResponse(response, packet.Id);
            return;
        }
        WheelchairManualGetManualRewardRequest? request = Request<WheelchairManualGetManualRewardRequest>(packet);
        WheelchairManualState state = State(session, activity!.Id);
        if (request?.ManualId is not int manualId || (manualId != 0
            && manualId != activity.CommonBattlePassManualId && manualId != activity.SeniorBattlePassManualId))
            response.Code = 20236006;
        else if (manualId == activity.SeniorBattlePassManualId && !state.IsSeniorManualUnlock)
            response.Code = 20236012;
        else
        {
            RefreshProgress(session);
            int level = BuildPayload(session, DateTimeOffset.UtcNow).BpLevel;
            // Even a single grid click sends its MANUAL id; the client does not send a reward id.
            // ManualId=0 selects both unlocked tiers, otherwise claim every eligible reward in that tier.
            List<WheelchairManualBattlePassManualTable> manuals = TableReaderV2.Parse<WheelchairManualBattlePassManualTable>()
                .Where(row => (row.Id == activity.CommonBattlePassManualId
                        || (state.IsSeniorManualUnlock && row.Id == activity.SeniorBattlePassManualId))
                    && (manualId == 0 || row.Id == manualId)).OrderBy(row => row.Id).ToList();
            List<WheelchairManualBattlePassRewardTable> rewards = manuals.SelectMany(manual =>
                TableReaderV2.Parse<WheelchairManualBattlePassRewardTable>().Where(row =>
                    row.Id >= manual.BpRewardConfig[0] && row.Id <= manual.BpRewardConfig[1]
                    && row.Level <= level && !state.ClaimedRewardIds.Contains(row.Id)).OrderBy(row => row.Id)).ToList();
            if (manuals.Count == 0)
                response.Code = 20236006;
            else if (rewards.Count == 0)
                response.Code = 20236015;
            else
            {
                List<RewardGrant> grants = rewards.Select(reward => new RewardGrant(
                    $"wheelchair-reward:{session.player.PlayerData.Id}:{activity.Id}:{reward.Id}",
                    RewardHandler.GetRewardGoods(reward.RewardId))).ToList();
                if (grants.Any(grant => grant.Goods.Count == 0))
                    response.Code = 20236007;
                else
                {
                    RewardApplicationResult application = RewardHandler.ApplyRewardsOnceAndPersist(grants, session);
                    SaveState(session, activity.Id, updated => updated.ClaimedRewardIds.AddRange(rewards.Select(row => row.Id)));
                    application.SendPushes(session);
                    session.SendPush(BuildPayload(session, DateTimeOffset.UtcNow));
                    response.RewardList = application.RewardGoods;
                }
            }
        }
        session.SendResponse(response, packet.Id);
    }

    [RequestPacketHandler("WheelchairManualClickBluePointRequest")]
    public static void WheelchairManualClickBluePointRequestHandler(Session session, Packet.Request packet)
    {
        WheelchairManualClickBluePointResponse response = new();
        response.Code = Activity(out WheelchairManualActivityTable? activity);
        if (response.Code == 0)
        {
            WheelchairManualClickBluePointRequest? request = Request<WheelchairManualClickBluePointRequest>(packet);
            if (request?.Type is not int type || !EligibleBluePointTypes(session).Contains(type))
                response.Code = 20236018;
            else
            {
                if (!State(session, activity!.Id).AcknowledgedBluePoints.Contains(type))
                    SaveState(session, activity.Id, state => state.AcknowledgedBluePoints.Add(type));
                session.SendPush(BuildPayload(session, DateTimeOffset.UtcNow));
            }
        }
        session.SendResponse(response, packet.Id);
    }

    [RequestPacketHandler("WheelchairManualClickRedPointRequest")]
    public static void WheelchairManualClickRedPointRequestHandler(Session session, Packet.Request packet)
    {
        WheelchairManualClickRedPointResponse response = new();
        response.Code = Activity(out WheelchairManualActivityTable? activity);
        if (response.Code == 0)
        {
            WheelchairManualClickRedPointRequest? request = Request<WheelchairManualClickRedPointRequest>(packet);
            if (request?.Id is not long id || !EligibleRedPointIds(session, activity!, DateTimeOffset.UtcNow).Contains(id))
                response.Code = 20236018;
            else
            {
                if (!State(session, activity!.Id).AcknowledgedRedPoints.Contains(id))
                    SaveState(session, activity.Id, state => state.AcknowledgedRedPoints.Add(id));
                session.SendPush(BuildPayload(session, DateTimeOffset.UtcNow));
            }
        }
        session.SendResponse(response, packet.Id);
    }

    private static IEnumerable<int> EligibleBluePointTypes(Session session) =>
        TableReaderV2.Parse<WheelchairManualTabsTable>()
            .Where(tab => tab.Condition is not > 0
                || PayModule.AreConditionsSatisfied(session, [tab.Condition.Value]))
            .Select(tab => tab.Type);

    private static bool IsTeachingStageUnlocked(Session session, int stageId)
    {
        StageTable? stage = TableReaderV2.Parse<StageTable>().SingleOrDefault(row => row.StageId == stageId);
        return stage is not null && session.player.PlayerData.Level >= Convert.ToInt32(stage.RequireLevel)
            && stage.PreStageId.Where(id => id > 0).All(id =>
                session.stage is not null && session.stage.Stages.TryGetValue(id, out StageDatum? data) && data.Passed);
    }

    private static IEnumerable<long> EligibleRedPointIds(Session session,
        WheelchairManualActivityTable activity, DateTimeOffset now)
    {
        // XEnumConst.WheelchairManual.TabType; client packs the type into the upper 32 bits.
        foreach (int id in activity.ShowPackageIds.Where(id => PayModule.IsPurchaseUnlocked(session, id)))
            yield return ((long)5 << 32) | (uint)id;
        foreach (int id in activity.TeachCommonStageIds.Prepend(activity.TeachConnectivityStageId)
                     .Where(id => IsTeachingStageUnlocked(session, id)))
            yield return ((long)6 << 32) | (uint)id;
        foreach (long id in WheelchairManualGuideManager.GetEligibleRedPointIds(session, now))
            yield return id;
    }
}
