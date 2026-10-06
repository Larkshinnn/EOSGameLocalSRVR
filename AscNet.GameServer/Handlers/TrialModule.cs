using AscNet.Common.Database;
using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.GameServer.Game;
using AscNet.Table.V2.share.fuben.trial;
using MessagePack;

namespace AscNet.GameServer.Handlers;

[MessagePackObject(true)] public sealed class TrialPassRewardRequest { public int TrialId { get; set; } }
[MessagePackObject(true)] public sealed class TrialPassRewardResponse { public int Code { get; set; } public List<RewardGoods> RewardGoodsList { get; set; } = new(); }
[MessagePackObject(true)] public sealed class TrialTypeRewardRequest { public int Type { get; set; } }
[MessagePackObject(true)] public sealed class TrialTypeRewardResponse { public int Code { get; set; } public List<RewardGoods> RewardGoodsList { get; set; } = new(); }

internal static class TrialModule
{
    private const int Invalid = 1;
    internal static NotifyTrialData BuildLoginData(Player player) => new()
    {
        FinishTrial = player.Trial.FinishedTrials.ToList(),
        RewardRecord = player.Trial.ClaimedTrials.ToList(),
        TypeRewardRecord = player.Trial.ClaimedTypes.Cast<dynamic>().ToList()
    };

    internal static bool RecordStageClear(Player player, int stageId)
    {
        TrialChallengeTable? row = TableReaderV2.Parse<TrialChallengeTable>().FirstOrDefault(trial => trial.StageId == stageId);
        if (row is null || player.Trial.FinishedTrials.Contains(row.Id)) return false;
        player.Trial.FinishedTrials.Add(row.Id);
        return true;
    }

    [RequestPacketHandler("TrialPassRewardRequest")]
    public static void PassReward(Session session, Packet.Request packet)
    {
        TrialPassRewardRequest request = packet.Deserialize<TrialPassRewardRequest>();
        TrialChallengeTable? row = TableReaderV2.Parse<TrialChallengeTable>().FirstOrDefault(trial => trial.Id == request.TrialId);
        if (row is null || !session.player.Trial.FinishedTrials.Contains(request.TrialId) || session.player.Trial.ClaimedTrials.Contains(request.TrialId))
        {
            session.SendResponse(new TrialPassRewardResponse { Code = Invalid }, packet.Id);
            return;
        }
        RewardApplicationResult result = RewardHandler.ApplyRewardsOnceAndPersist(
            [new RewardGrant($"trial-pass:{session.player.PlayerData.Id}:{request.TrialId}", RewardHandler.GetRewardGoods(row.RewardId))], session);
        session.player.Trial.ClaimedTrials.Add(request.TrialId);
        session.player.SaveChecked();
        result.SendPushes(session);
        session.SendResponse(new TrialPassRewardResponse { RewardGoodsList = result.RewardGoods }, packet.Id);
    }

    [RequestPacketHandler("TrialTypeRewardRequest")]
    public static void TypeReward(Session session, Packet.Request packet)
    {
        TrialTypeRewardRequest request = packet.Deserialize<TrialTypeRewardRequest>();
        TrialTypeRewardTable? row = TableReaderV2.Parse<TrialTypeRewardTable>().FirstOrDefault(reward => reward.Type == request.Type);
        int[] required = TableReaderV2.Parse<TrialChallengeTable>().Where(trial => trial.Type == request.Type).Select(trial => trial.Id).ToArray();
        if (row is null || required.Length == 0 || required.Any(id => !session.player.Trial.FinishedTrials.Contains(id)) || session.player.Trial.ClaimedTypes.Contains(request.Type))
        {
            session.SendResponse(new TrialTypeRewardResponse { Code = Invalid }, packet.Id);
            return;
        }
        RewardApplicationResult result = RewardHandler.ApplyRewardsOnceAndPersist(
            [new RewardGrant($"trial-type:{session.player.PlayerData.Id}:{request.Type}", RewardHandler.GetRewardGoods(row.RewardId))], session);
        session.player.Trial.ClaimedTypes.Add(request.Type);
        session.player.SaveChecked();
        result.SendPushes(session);
        session.SendResponse(new TrialTypeRewardResponse { RewardGoodsList = result.RewardGoods }, packet.Id);
    }
}
