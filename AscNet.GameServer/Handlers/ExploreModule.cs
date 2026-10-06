using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.Table.V2.share.fuben.explore;

namespace AscNet.GameServer.Handlers;

internal static class ExploreModule
{
    private const int ChapterMissing = 20046001, ChapterLocked = 20046003, ChapterRewardClaimed = 20046004;
    private const int NodeMissing = 20046005, NodeLocked = 20046006, NodeFinished = 20046007, EnduranceShort = 20046008;

    private static IReadOnlyList<ExploreChapterTable> Chapters => TableReaderV2.Parse<ExploreChapterTable>();
    private static IReadOnlyList<ExploreNodeTable> Nodes => TableReaderV2.Parse<ExploreNodeTable>();
    private static IReadOnlyList<ExploreBuffItemTable> Buffs => TableReaderV2.Parse<ExploreBuffItemTable>();

    internal static NotifyExploreData BuildLoginData(Player player)
    {
        player.Explore ??= [];
        foreach (ExploreChapterTable chapter in Chapters.OrderBy(x => x.Id))
            if (player.Explore.All(x => x.Id != chapter.Id)) player.Explore.Add(new() { Id = chapter.Id });
        return new NotifyExploreData { ChapterDatas = player.Explore.OrderBy(x => x.Id).Select(ToDto).ToList() };
    }

    private static ExploreChapterData ToDto(ExploreChapterState state) => new()
    {
        Id = state.Id, RewardStatus = state.RewardStatus,
        FinishNodes = state.FinishNodes, UnlockEvents = state.UnlockEvents,
        EnduranceInfos = state.EnduranceInfos.Select(x => new ExploreEnduranceInfo { Id = x.Id, Use = x.Use }).ToList()
    };

    [RequestPacketHandler("ExploreFinishNodeRequest")]
    public static void ExploreFinishNodeRequestHandler(Session session, Packet.Request packet) =>
        FinishNode(session, packet.Deserialize<ExploreFinishNodeRequest>(), packet.Id);

    [RequestPacketHandler("ExploreGetRewardRequest")]
    public static void ExploreGetRewardRequestHandler(Session session, Packet.Request packet) =>
        GetReward(session, packet.Deserialize<ExploreGetRewardRequest>(), packet.Id);

    internal static void FinishNode(Session session, ExploreFinishNodeRequest request, int packetId)
    {
        ExploreFinishNodeResponse response = new();
        ExploreNodeTable? node = Nodes.FirstOrDefault(x => x.Id == request.Id);
        if (node is null || node.Type != 2) { response.Code = NodeMissing; session.SendResponse(response, packetId); return; }
        ExploreChapterState state = State(session.player, node.ChapterId);
        ExploreChapterTable chapter = Chapters.First(x => x.Id == node.ChapterId);
        if (chapter.PreId.GetValueOrDefault() > 0 && !session.player.Explore.Any(x => x.Id == chapter.PreId.GetValueOrDefault() && AllFinished(x))) response.Code = ChapterLocked;
        else if (state.FinishNodes.Contains(node.Id)) response.Code = NodeFinished;
        else if (node.PreOpenId.Any(x => x > 0 && !state.FinishNodes.Contains(x))) response.Code = NodeLocked;
        else if (node.PreShowId.Any(x => x > 0 && !state.FinishNodes.Contains(x))) response.Code = NodeLocked;
        else { state.FinishNodes.Add(node.Id); session.player.SaveChecked(); }
        session.SendResponse(response, packetId);
    }

    internal static void GetReward(Session session, ExploreGetRewardRequest request, int packetId)
    {
        ExploreGetRewardResponse response = new();
        ExploreChapterTable? chapter = Chapters.FirstOrDefault(x => x.Id == request.Id);
        if (chapter is null) { response.Code = ChapterMissing; session.SendResponse(response, packetId); return; }
        ExploreChapterState state = State(session.player, chapter.Id);
        if (chapter.PreId.GetValueOrDefault() > 0 && !session.player.Explore.Any(x => x.Id == chapter.PreId.GetValueOrDefault() && AllFinished(x))) response.Code = ChapterLocked;
        else if (state.RewardStatus != 0) response.Code = ChapterRewardClaimed;
        else if (!Nodes.Where(x => x.ChapterId == chapter.Id).All(x => state.FinishNodes.Contains(x.Id))) response.Code = 20046002;
        else
        {
            RewardApplicationResult grant = RewardHandler.ApplyRewardsOnceAndPersist(
                [new RewardGrant($"explore:chapter:{chapter.Id}", RewardHandler.GetRewardGoods(chapter.RewardId))], session);
            state.RewardStatus = 1; session.player.SaveChecked(); grant.SendPushes(session); response.RewardGoodsList = grant.RewardGoods;
        }
        session.SendResponse(response, packetId);
    }

    internal static int ValidatePreFight(Session session, PreFightRequest.PreFightRequestPreFightData data)
    {
        ExploreNodeTable? node = Nodes.FirstOrDefault(x => x.Type == 1 && x.TypeValue == data.StageId.ToString());
        if (node is null) return 0;
        ExploreChapterState state = State(session.player, node.ChapterId);
        ExploreChapterTable? chapter = Chapters.FirstOrDefault(x => x.Id == node.ChapterId);
        if (chapter is null) return ChapterMissing;
        if (chapter.PreId.GetValueOrDefault() > 0 && !session.player.Explore.Any(x => x.Id == chapter.PreId.GetValueOrDefault() && AllFinished(x))) return ChapterLocked;
        if (state.FinishNodes.Contains(node.Id)) return NodeFinished;
        if (node.PreOpenId.Any(x => x > 0 && !state.FinishNodes.Contains(x)) || node.PreShowId.Any(x => x > 0 && !state.FinishNodes.Contains(x))) return NodeLocked;
        foreach (uint cardId in (data.CardIds ?? []).Distinct())
        {
            if (cardId == 0) continue;
            ExploreEnduranceState endurance = state.EnduranceInfos.FirstOrDefault(x => x.Id == (int)cardId) ?? new() { Id = (int)cardId };
            if (endurance.Use + (node.CostEndurance ?? 0) > chapter.Endurance) return EnduranceShort;
            if (session.character.Characters.All(x => x.Id != cardId)) return EnduranceShort;
        }
        return 0;
    }
    internal static bool TryGetStageRewardId(uint stageId, out int rewardId)
    {
        ExploreNodeTable? node = Nodes.FirstOrDefault(x => x.Type == 1 && x.TypeValue == stageId.ToString());
        rewardId = node?.RewardId ?? 0;
        return rewardId > 0;
    }


    internal static bool TrySettle(Session session, FightSettleResult result)
    {
        ExploreNodeTable? node = Nodes.FirstOrDefault(x => x.Type == 1 && x.TypeValue == result.StageId.ToString());
        if (node is null || session.fight?.PreFight.PreFightData.StageId != result.StageId) return false;
        if (!result.IsWin || result.IsForceExit) return true;
        ExploreChapterState state = State(session.player, node.ChapterId);
        if (!state.FinishNodes.Contains(node.Id)) state.FinishNodes.Add(node.Id);
        foreach (uint cardId in (session.fight.PreFight.PreFightData.CardIds ?? []).Distinct())
        {
            if (cardId == 0) continue;
            ExploreEnduranceState endurance = state.EnduranceInfos.FirstOrDefault(x => x.Id == (int)cardId) ?? new() { Id = (int)cardId };
            endurance.Use += node.CostEndurance ?? 0;
            if (!state.EnduranceInfos.Contains(endurance)) state.EnduranceInfos.Add(endurance);
        }
        List<int> events = (result.EventSet ?? []).Select(TryInt).Where(x => x > 0 && Buffs.Any(b => b.ChapterId == node.ChapterId && b.UnlockEvent == x)).Distinct().ToList();
        foreach (int id in events) if (!state.UnlockEvents.Contains(id)) state.UnlockEvents.Add(id);
        session.player.SaveChecked();
        if (events.Count > 0) session.SendPush(new NotifyExploreUnlockEvent { Id = node.ChapterId, UnlockEvents = state.UnlockEvents.ToList() });
        return true;
    }

    private static int TryInt(dynamic value) { try { return Convert.ToInt32(value); } catch { return 0; } }
    private static bool AllFinished(ExploreChapterState state) => Nodes.Where(x => x.ChapterId == state.Id).All(x => state.FinishNodes.Contains(x.Id));
    private static ExploreChapterState State(Player player, int id) => (player.Explore ??= []).FirstOrDefault(x => x.Id == id) ?? Add(player, id);
    private static ExploreChapterState Add(Player player, int id) { ExploreChapterState state = new() { Id = id }; player.Explore.Add(state); return state; }
}
