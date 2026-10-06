using AscNet.Common.MsgPack;
using AscNet.Common.Util;
using MessagePack;
using StageRow = AscNet.Table.V2.share.fuben.StageTable;

namespace AscNet.GameServer.Handlers
{
    internal class StageBookmarkModule
    {
        [RequestPacketHandler("AddStageBookmarkRequest")]
        public static void AddStageBookmarkRequestHandler(Session session, Packet.Request packet)
        {
            AddStageBookmarkRequest? request;
            try
            {
                request = packet.Deserialize<AddStageBookmarkRequest>();
            }
            catch (MessagePackSerializationException)
            {
                session.SendResponse(new AddStageBookmarkResponse { Code = 5 }, packet.Id); // ParamsError
                return;
            }

            // EN XMovieManager.IsShowBookmark permits these four stage types only.
            // Movie/action/selection IDs are client-owned story checkpoints, not reward claims.
            if (request is null || request.StageId <= 0 || string.IsNullOrEmpty(request.MovieId)
                || request.ActionId <= 0 || request.OptionInfos is null
                || request.OptionInfos.Any(option => option.Key <= 0 || option.Value <= 0)
                || !TableReaderV2.Parse<StageRow>().Any(stage => stage.StageId == request.StageId
                    && stage.Type is 1 or 25 or 57 or 87))
            {
                session.SendResponse(new AddStageBookmarkResponse { Code = 5 }, packet.Id); // ParamsError
                return;
            }

            int code = SaveBookmark(session, new StageBookmarkData
            {
                StageId = request.StageId,
                MovieId = request.MovieId,
                ActionId = request.ActionId,
                OptionInfos = request.OptionInfos
            });
            session.SendResponse(new AddStageBookmarkResponse { Code = code }, packet.Id);
        }

        [RequestPacketHandler("GetStageBookmarkRequest")]
        public static void GetStageBookmarkRequestHandler(Session session, Packet.Request packet)
        {
            session.SendResponse(new GetStageBookmarkResponse { StageBookmarkData = session.stage.StageBookmarkData }, packet.Id);
        }

        [RequestPacketHandler("DeleteStageBookmarkRequest")]
        public static void DeleteStageBookmarkRequestHandler(Session session, Packet.Request packet)
        {
            int code = session.stage.StageBookmarkData is null ? 0 : SaveBookmark(session, null);
            session.SendResponse(new DeleteStageBookmarkResponse { Code = code }, packet.Id);
        }

        private static int SaveBookmark(Session session, StageBookmarkData? bookmark)
        {
            StageBookmarkData? previous = session.stage.StageBookmarkData;
            session.stage.StageBookmarkData = bookmark;
            try
            {
                session.stage.SaveChecked();
                return 0;
            }
            catch (Exception)
            {
                session.stage.StageBookmarkData = previous;
                return 2; // ServerError: do not acknowledge an unpersisted checkpoint.
            }
        }
    }
}
