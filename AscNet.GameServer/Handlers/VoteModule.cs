using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using AscNet.Table.V2.share.equip.equipguide;
using MessagePack;

namespace AscNet.GameServer.Handlers
{
    [MessagePackObject(true)]
    public sealed class AddVoteRequest
    {
        public int VoteId { get; set; }
    }

    [MessagePackObject(true)]
    public sealed class AddVoteResponse
    {
        public int Code { get; set; }
    }

    internal class VoteModule
    {
        private static readonly Lazy<int[]> VoteIds = new(() => TableReaderV2
            .Parse<EquipRecommendTable>()
            .Select(row => row.Id)
            .Where(id => id > 0)
            .Distinct()
            .Order()
            .ToArray());

        [RequestPacketHandler("GetVoteGroupListRequest")]
        public static void GetVoteGroupListRequestHandler(Session session, Packet.Request packet)
        {
            GetVoteGroupListResponse response = new();
            int[] voteIds = VoteIds.Value;

            if (voteIds.Length > 0)
            {
                response.VoteGroupList.Add(new()
                {
                    Id = voteIds[0],
                    TimeToClose = 0,
                    VoteDic = voteIds.ToDictionary(id => (dynamic)id, _ => (dynamic)0)
                });
            }

            session.SendResponse(response, packet.Id);
        }

        [RequestPacketHandler("AddVoteRequest")]
        public static void AddVoteRequestHandler(Session session, Packet.Request packet)
        {
            AddVoteRequest request = packet.Deserialize<AddVoteRequest>();
            int[] voteIds = VoteIds.Value;
            if (!voteIds.Contains(request.VoteId))
            {
                session.SendResponse(new AddVoteResponse { Code = 20043006 }, packet.Id);
                return;
            }

            int groupId = voteIds[0];
            if (session.player.VoteAlarmData.Any(value => value.Id == groupId))
            {
                session.SendResponse(new AddVoteResponse { Code = 20043005 }, packet.Id);
                return;
            }

            session.player.VoteAlarmData.Add(new VoteAlarmData
            {
                Id = groupId,
                SelectId = request.VoteId
            });
            session.player.Save();
            session.SendPush(new NotifyVoteData { VoteAlarmDic = session.player.VoteAlarmData });
            session.SendResponse(new AddVoteResponse(), packet.Id);
        }
    }
}
