using AscNet.Common.Util;
using AscNet.Table.V2.share.equip;
using AscNet.Table.V2.share.item;
using AscNet.Table.V2.share.reward;

namespace AscNet.GameServer.Handlers.Drops
{
    // User-authorized private-server uniform equipment-overclock policy, not retail drop weights.
    // The caller independently samples this distinct material pool once per box.
    internal static class EquipmentOverclockDropPolicy
    {
        private static readonly Lazy<Dictionary<int, (IReadOnlyList<RewardGoodsTable> Pool, int Count)>> Pools = new(() =>
        {
            Dictionary<int, (IReadOnlyList<RewardGoodsTable> Pool, int Count)> pools = new();
            foreach (int sourceId in TableReaderV2.Parse<EquipmentOverclockDropPolicyTable>().Select(row => row.Id).Distinct())
            {
                if (TryBuildPool(sourceId, out IReadOnlyList<RewardGoodsTable> pool, out int count))
                    pools.Add(sourceId, (pool, count));
            }
            return pools;
        });

        internal static bool TryResolve(int sourceId, out IReadOnlyList<RewardGoodsTable> pool, out int countPerBox)
        {
            if (Pools.Value.TryGetValue(sourceId, out var resolved))
            {
                pool = resolved.Pool;
                countPerBox = resolved.Count;
                return true;
            }
            pool = Array.Empty<RewardGoodsTable>();
            countPerBox = 0;
            return false;
        }

        private static bool TryBuildPool(int sourceId, out IReadOnlyList<RewardGoodsTable> pool, out int countPerBox)
        {
            pool = Array.Empty<RewardGoodsTable>();
            countPerBox = 0;
            EquipmentOverclockDropPolicyTable? policy = null;
            foreach (EquipmentOverclockDropPolicyTable row in TableReaderV2.Parse<EquipmentOverclockDropPolicyTable>())
            {
                if (row.Id != sourceId)
                    continue;
                if (policy is not null || row.Id <= 0 || row.Quality <= 0 || row.Count <= 0)
                    return false;
                policy = row;
            }
            if (policy is null)
                return false;

            HashSet<int> materialIds = new();
            foreach (EquipBreakThroughTable row in TableReaderV2.Parse<EquipBreakThroughTable>())
            {
                if (row.ItemId is null || row.ItemCount is null || row.ItemId.Count != row.ItemCount.Count)
                    return false;
                for (int index = 0; index < row.ItemId.Count; index++)
                {
                    int itemId = row.ItemId[index];
                    int itemCount = row.ItemCount[index];
                    if (itemId == 0 && itemCount == 0)
                        continue;
                    if (itemId <= 0 || itemCount <= 0)
                        return false;
                    materialIds.Add(itemId);
                }
            }

            Dictionary<int, ItemTable> materials = new();
            foreach (ItemTable row in TableReaderV2.Parse<ItemTable>())
            {
                if (materialIds.Contains(row.Id) && (!materials.TryAdd(row.Id, row) || row.Quality <= 0))
                    return false;
            }
            if (materials.Count != materialIds.Count)
                return false;

            List<RewardGoodsTable> candidates = materials.Values
                .Where(row => row.Quality == policy.Quality)
                .OrderBy(row => row.Id)
                .Select(row => new RewardGoodsTable { TemplateId = row.Id, Count = policy.Count })
                .ToList();
            if (candidates.Count == 0)
                return false;

            pool = candidates;
            countPerBox = policy.Count;
            return true;
        }
    }
}
