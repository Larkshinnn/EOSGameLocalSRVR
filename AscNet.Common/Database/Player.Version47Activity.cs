using MongoDB.Bson.Serialization.Attributes;

namespace AscNet.Common.Database;

public partial class Player
{
    /// <summary>
    /// Durable set of ActivityBriefStory ids the commandant has already watched.
    /// Unique; ordered. Populated only by FinishBriefStoryRequest, replayed at login
    /// via NotifyBriefStoryData.FinishedIds.
    /// </summary>
    [BsonElement("brief_story_finished_ids")]
    public List<int> BriefStoryFinishedIds { get; set; } = new();

    /// <summary>Records a finished brief story id durably; idempotent for ids already present.</summary>
    public bool AddFinishedBriefStoryId(int storyId)
    {
        if (storyId <= 0 || BriefStoryFinishedIds.Contains(storyId))
            return false;

        BriefStoryFinishedIds.Add(storyId);
        BriefStoryFinishedIds.Sort();
        Save();
        return true;
    }
}
