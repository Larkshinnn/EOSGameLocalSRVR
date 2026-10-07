using AscNet.Common.MsgPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AscNet.Common.Util;
using AscNet.Table.V2.share.fuben.arena;

namespace AscNet.GameServer.Handlers;

internal static class ArenaStageDataset
{
    private const string ConfigPath = "./Configs/arena_stage_data.json";
    private const int BaselineFluctuationLevel = 385;
    private static readonly Lazy<Dataset> Data = new(Load);
    private static readonly Lazy<HashSet<(int AreaId, uint StageId, int MarkId, string Archetype)>> ConfiguredStages = new(() =>
        TableReaderV2.Parse<AreaStageTable>()
            .Where(area => area.IsAbandoned == 0)
            .SelectMany(area => area.StageId
                .Where(stageId => stageId > 0)
                .Select(stageId => (area.Id, checked((uint)stageId), area.MarkId, area.Desc)))
            .ToHashSet());

    public static bool Supports(int areaId, uint stageId, int markId, string archetype) =>
        Resolve(areaId, stageId, markId, archetype) is not null;

    public static IReadOnlySet<int> GeneratedAreaIds => Data.Value.Stages
        .Where(stage => stage.Generator is not null)
        .Select(stage => stage.AreaId)
        .ToHashSet();

    public static bool IsGeneratedArea(int areaId) => GeneratedAreaIds.Contains(areaId);

    public static int? PassTimeLimit(int areaId, uint stageId, int markId, string archetype) =>
        Resolve(areaId, stageId, markId, archetype)?.PassTimeLimit;

    public static bool TryHydrate(int areaId, uint stageId, int markId, string archetype, PreFightResponse.PreFightResponseFightData fightData)
        => TryHydrate(areaId, stageId, markId, archetype, 0, 0, BaselineFluctuationLevel, fightData);

    public static bool TryHydrate(
        int areaId,
        uint stageId,
        int markId,
        string archetype,
        int activityNo,
        long playerId,
        int fluctuationLevel,
        PreFightResponse.PreFightResponseFightData fightData)
    {
        Stage? stage = Resolve(areaId, stageId, markId, archetype);
        if (stage is null)
            return false;

        // Materialize the graph every time. Response consumers may mutate dynamic dictionaries/lists.
        Generator? generator = stage.Generator;
        List<List<JObject>> waves = generator is null
            ? stage.NpcGroupRefs.Select(groupRef => Data.Value.GroupDefinitions[groupRef].NpcRefs
                .Select(npcRef => Data.Value.NpcDefinitions[npcRef]).ToList()).ToList()
            : GenerateWaves(generator, activityNo, playerId, areaId, stageId, ResolveNpcIds(generator));
        fightData.NpcGroupList = waves.Select(npcRefs => new Dictionary<string, object>
        {
            ["NpcList"] = npcRefs.Select(npc => CloneNpcForFluctuation(npc, fluctuationLevel)).ToList()
        }).ToList();
        fightData.PassTimeLimit = stage.PassTimeLimit;
        fightData.ReviseId = stage.ReviseId;
        fightData.Restartable = stage.Restartable;
        fightData.FightCheckType = stage.FightCheckType;
        fightData.SegmentFightCheckSecond = stage.SegmentFightCheckSecond;
        fightData.Records = CloneObject(stage.Records);
        fightData.StageParams = CloneObject(stage.StageParams);
        return true;
    }

    private static object? CloneNpcForFluctuation(JObject source, int fluctuationLevel)
    {
        Dictionary<string, object?> npc = source.Properties()
            .ToDictionary(property => property.Name, property => CloneObject(property.Value));
        int baseLevel = source.Value<int?>("Level") ?? 0;
        if (baseLevel > 0)
            npc["Level"] = checked((int)Math.Clamp((long)Math.Round(baseLevel * Math.Max(0, fluctuationLevel) / (double)BaselineFluctuationLevel,
                MidpointRounding.AwayFromZero), 1, int.MaxValue));
        return npc;
    }

    private static List<List<JObject>> GenerateWaves(
        Generator generator, int activityNo, long playerId, int areaId, uint stageId,
        (List<JObject> Repeatable, List<JObject> UniqueOnce) npcTemplates)
    {
        List<JObject> bossTemplates = generator.BossNpcIds
            .Select(id => npcTemplates.UniqueOnce.Single(npc => npc.Value<int>("NpcId") == id)).ToList();
        List<JObject> waveNpcs = npcTemplates.UniqueOnce
            .Where(npc => !generator.BossNpcIds.Contains(npc.Value<int>("NpcId"))).ToList();
        if (generator.IncludeEveryNpcOnce)
            waveNpcs.AddRange(npcTemplates.Repeatable);

        uint state = Seed(activityNo, playerId, areaId, stageId);
        while (waveNpcs.Count + bossTemplates.Count < generator.WaveCount)
            waveNpcs.Add(npcTemplates.Repeatable[(int)(Next(ref state) % (uint)npcTemplates.Repeatable.Count)]);

        Shuffle(waveNpcs, ref state);
        Shuffle(bossTemplates, ref state);
        JObject?[] generatedWaves = new JObject?[generator.WaveCount];
        List<int> bossPositions = PlaceBosses(generator.WaveCount, bossTemplates.Count, generator.MinimumBossWaveGap, ref state);
        for (int index = 0; index < bossTemplates.Count; index++)
            generatedWaves[bossPositions[index]] = bossTemplates[index];

        int normalIndex = 0;
        for (int index = 0; index < generatedWaves.Length; index++)
        {
            if (generatedWaves[index] is null)
                generatedWaves[index] = waveNpcs[normalIndex++];
        }

        return generatedWaves.Select(npc => new List<JObject> { npc! }).ToList();
    }

    private static void Shuffle<T>(List<T> values, ref uint state)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int other = (int)(Next(ref state) % (uint)(index + 1));
            (values[index], values[other]) = (values[other], values[index]);
        }
    }

    private static List<int> PlaceBosses(int waveCount, int bossCount, int minimumGap, ref uint state)
    {
        if (bossCount == 0) return [];
        if (waveCount < (bossCount - 1) * minimumGap + 1)
            throw new InvalidDataException("Could not place Warzone boss appearances with the configured wave gap");

        int[] slack = new int[bossCount + 1];
        int remaining = waveCount - ((bossCount - 1) * minimumGap + 1);
        for (int index = 0; index < remaining; index++)
            slack[(int)(Next(ref state) % (uint)slack.Length)]++;

        List<int> selected = [];
        int wave = slack[0];
        for (int index = 0; index < bossCount; index++)
        {
            selected.Add(wave);
            wave += minimumGap + slack[index + 1];
        }
        Shuffle(selected, ref state);
        return selected;
    }

    private static (List<JObject> Repeatable, List<JObject> UniqueOnce) ResolveNpcIds(Generator generator)
    {
        Dictionary<int, JObject> npcById = generator.NpcDefinitions.ToDictionary(npc => npc.Value<int>("NpcId"));
        return (
            generator.RepeatableNpcIds.Select(id => npcById[id]).ToList(),
            generator.UniqueOnceNpcIds.Select(id => npcById[id]).ToList());
    }

    private static uint Seed(int activityNo, long playerId, int areaId, uint stageId)
    {
        uint hash = 2166136261;
        foreach (uint value in new[] { unchecked((uint)activityNo), unchecked((uint)playerId), (uint)((ulong)playerId >> 32), unchecked((uint)areaId), stageId })
        {
            hash = (hash ^ value) * 16777619;
        }
        return hash == 0 ? 0x6D2B79F5 : hash;
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static Stage? Resolve(int areaId, uint stageId, int markId, string archetype)
    {
        if (!ConfiguredStages.Value.Contains((areaId, stageId, markId, archetype)))
            return null;
        Stage? exact = Data.Value.Stages.SingleOrDefault(candidate =>
            candidate.StageId == stageId && candidate.AreaId == areaId);
        if (exact is not null)
            return exact.MarkId == markId && exact.Archetype == archetype ? exact : null;
        return Data.Value.Stages.SingleOrDefault(candidate =>
            candidate.MarkId == markId && candidate.Archetype == archetype && candidate.ReusableArchetype);
    }

    private static object? CloneObject(JToken token) => token switch
    {
        JObject value => value.Properties().ToDictionary(property => property.Name, property => CloneObject(property.Value)),
        JArray value => value.Select(CloneObject).ToList(),
        JValue value => value.Value,
        _ => throw new InvalidDataException($"Unsupported Arena dynamic value {token.Type}")
    };

    private static Dataset Load()
    {
        Dataset data = JsonConvert.DeserializeObject<Dataset>(File.ReadAllText(ConfigPath))
            ?? throw new InvalidDataException($"Could not deserialize {ConfigPath}");
        if (data.SchemaVersion != 2)
            throw new InvalidDataException($"Unsupported Arena stage schema {data.SchemaVersion}");
        if (data.Stages.Select(stage => (stage.AreaId, stage.StageId)).Distinct().Count() != data.Stages.Count)
            throw new InvalidDataException("Arena stage keys must be unique");
        if (data.Stages.Where(stage => stage.ReusableArchetype).GroupBy(stage => (stage.MarkId, stage.Archetype)).Any(group => group.Count() > 1))
            throw new InvalidDataException("Arena reusable MarkId/archetype profiles must be unique");
        if (data.Stages.Any(stage => string.IsNullOrWhiteSpace(stage.Archetype)))
            throw new InvalidDataException("Arena stages must identify a source archetype");
        if (data.Stages.Any(stage => !ConfiguredStages.Value.Contains(
                (stage.AreaId, stage.StageId, stage.MarkId, stage.Archetype))))
            throw new InvalidDataException("Arena dataset contains a stage profile absent from nonabandoned AreaStage configuration");
        foreach (Group group in data.GroupDefinitions)
            if (group.NpcRefs.Count == 0 || group.NpcRefs.Any(index => index < 0 || index >= data.NpcDefinitions.Count))
                throw new InvalidDataException("Arena group has an empty or invalid NPC reference list");
        foreach (Stage stage in data.Stages)
        {
            if (stage.Generator is null && (stage.NpcGroupRefs.Count == 0 || stage.NpcGroupRefs.Any(index => index < 0 || index >= data.GroupDefinitions.Count)))
                throw new InvalidDataException($"Arena stage {stage.StageId} has an empty or invalid group reference list");
            if (stage.Generator is not { } generator)
                continue;
            int[] configuredIds = generator.RepeatableNpcIds.Concat(generator.UniqueOnceNpcIds).ToArray();
            int matchingNpcDefinitions = generator.NpcDefinitions.Count(npc => configuredIds.Contains(npc.Value<int?>("NpcId") ?? 0));
            if (generator.Mode != "SingleEnemyPerWave" || generator.WaveCount <= 0
                || generator.RepeatableNpcIds.Count == 0
                || generator.IncludeEveryNpcOnce && generator.WaveCount < generator.RepeatableNpcIds.Count + generator.UniqueOnceNpcIds.Count
                || generator.WaveCount < generator.BossNpcIds.Count
                || generator.MinimumBossWaveGap < 1
                || generator.BossNpcIds.Except(generator.UniqueOnceNpcIds).Any()
                || generator.RepeatableNpcIds.Intersect(generator.UniqueOnceNpcIds).Any()
                || configuredIds.Distinct().Count() != configuredIds.Length
                || matchingNpcDefinitions != configuredIds.Length
                || generator.NpcDefinitions.Count != configuredIds.Length)
                throw new InvalidDataException($"Arena stage {stage.StageId} has an invalid single-enemy generator profile");
            _ = ResolveNpcIds(generator);
        }
        return data;
    }

    private sealed class Dataset
    {
        public int SchemaVersion { get; set; }
        public List<JObject> NpcDefinitions { get; set; } = new();
        public List<Group> GroupDefinitions { get; set; } = new();
        public List<Stage> Stages { get; set; } = new();
    }

    private sealed class Group
    {
        public List<int> NpcRefs { get; set; } = new();
    }

    private sealed class Generator
    {
        public string Mode { get; set; } = string.Empty;
        public int WaveCount { get; set; }
        public bool IncludeEveryNpcOnce { get; set; }
        public List<JObject> NpcDefinitions { get; set; } = new();
        public List<int> RepeatableNpcIds { get; set; } = new();
        public List<int> UniqueOnceNpcIds { get; set; } = new();
        public List<int> BossNpcIds { get; set; } = new();
        public int MinimumBossWaveGap { get; set; } = 3;
    }

    private sealed class Stage
    {
        public uint StageId { get; set; }
        public int AreaId { get; set; }
        public int MarkId { get; set; }
        public bool ReusableArchetype { get; set; }
        public List<int> NpcGroupRefs { get; set; } = new();
        public Generator? Generator { get; set; }
        public string Archetype { get; set; } = string.Empty;
        public int PassTimeLimit { get; set; }
        public int ReviseId { get; set; }
        public bool Restartable { get; set; }
        public int FightCheckType { get; set; }
        public int SegmentFightCheckSecond { get; set; }
        public JObject Records { get; set; } = new();
        public JObject StageParams { get; set; } = new();
    }
}
