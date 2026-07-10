using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ApparelAttributeFilter
{
    // One apparel def's filterable attributes, computed once at load.
    public class ApparelAttributeInfo
    {
        public readonly ThingDef def;
        public readonly HashSet<ApparelLayerDef> Layers;
        public readonly HashSet<BodyPartGroupDef> Covers;
        private readonly Dictionary<StatDef, float> statValues;

        public ApparelAttributeInfo(ThingDef def, HashSet<ApparelLayerDef> layers,
            HashSet<BodyPartGroupDef> covers, Dictionary<StatDef, float> statValues)
        {
            this.def = def;
            this.Layers = layers;
            this.Covers = covers;
            this.statValues = statValues;
        }

        public float GetStatValue(StatDef stat)
            => statValues.TryGetValue(stat, out float v) ? v : 0f;
    }

    // Load-time cache of everything the filter UI and engine need.
    public static class AttributeCache
    {
        public static List<ApparelAttributeInfo> Apparel { get; private set; }
        public static List<StatDef> NumericAttributes { get; private set; }
        public static List<ApparelLayerDef> Layers { get; private set; }
        public static List<BodyPartGroupDef> Covers { get; private set; }

        // Stuff-powered stats (armor, insulation) mapped to the apparel multiplier stat that gates
        // them (StatPart_Stuff.multiplierStat). These live in no stat list; the material supplies them.
        private static Dictionary<StatDef, StatDef> stuffPoweredMultipliers;

        public static void EnsureBuilt()
        {
            if (Apparel == null) Build();
        }

        public static void Build()
        {
            var apparel = new List<ApparelAttributeInfo>();
            var numericSet = new HashSet<StatDef>();
            var layerSet = new HashSet<ApparelLayerDef>();
            var coverSet = new HashSet<BodyPartGroupDef>();

            stuffPoweredMultipliers = new Dictionary<StatDef, StatDef>();
            foreach (StatDef s in DefDatabase<StatDef>.AllDefsListForReading)
            {
                StatPart_Stuff part = s.parts?.OfType<StatPart_Stuff>().FirstOrDefault();
                if (part?.multiplierStat != null) stuffPoweredMultipliers[s] = part.multiplierStat;
            }

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.IsApparel) continue;
                try
                {
                    var layers = def.apparel.layers != null
                        ? new HashSet<ApparelLayerDef>(def.apparel.layers)
                        : new HashSet<ApparelLayerDef>();
                    var covers = def.apparel.bodyPartGroups != null
                        ? new HashSet<BodyPartGroupDef>(def.apparel.bodyPartGroups)
                        : new HashSet<BodyPartGroupDef>();
                    Dictionary<StatDef, float> statValues = ComputeStatValues(def);

                    apparel.Add(new ApparelAttributeInfo(def, layers, covers, statValues));
                    layerSet.UnionWith(layers);
                    coverSet.UnionWith(covers);
                    numericSet.UnionWith(statValues.Keys);
                }
                catch (Exception e)
                {
                    Log.Warning($"[Apparel Attribute Filter] Skipped caching {def.defName}: {e.Message}");
                }
            }

            Apparel = apparel;
            NumericAttributes = numericSet
                .OrderBy(s => s.category?.displayOrder ?? int.MaxValue)
                .ThenBy(s => s.label ?? s.defName)
                .ToList();
            Layers = layerSet.OrderBy(l => l.drawOrder).ToList();
            Covers = coverSet.OrderBy(b => b.listOrder).ToList();
        }

        // sum of statBases + equippedStatOffsets, evaluated at default stuff
        private static Dictionary<StatDef, float> ComputeStatValues(ThingDef def)
        {
            var result = new Dictionary<StatDef, float>();

            // Equipped offsets are flat (stuff-independent).
            if (def.equippedStatOffsets != null)
                foreach (StatModifier sm in def.equippedStatOffsets)
                    if (sm?.stat != null && !sm.stat.alwaysHide)
                        Add(result, sm.stat, sm.value);

            // Base stats plus any stat the default stuff modifies, at that stuff's value.
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            var baseStats = new HashSet<StatDef>();
            if (def.statBases != null)
                foreach (StatModifier sm in def.statBases)
                    if (sm?.stat != null) baseStats.Add(sm.stat);
            if (stuff?.stuffProps != null)
            {
                CollectStats(stuff.stuffProps.statOffsets, baseStats);
                CollectStats(stuff.stuffProps.statFactors, baseStats);
            }

            foreach (StatDef stat in baseStats)
            {
                if (stat.alwaysHide) continue;
                Add(result, stat, def.GetStatValueAbstract(stat, stuff));
            }

            // Look at material multiplier for stuffables
            if (stuff != null && stuffPoweredMultipliers != null)
                foreach (KeyValuePair<StatDef, StatDef> pair in stuffPoweredMultipliers)
                {
                    StatDef stat = pair.Key;
                    if (stat.alwaysHide || result.ContainsKey(stat)) continue;
                    float multiplier = def.GetStatValueAbstract(pair.Value);
                    if (multiplier != 0f) result[stat] = multiplier;
                }

            return result;
        }

        private static void CollectStats(List<StatModifier> mods, HashSet<StatDef> into)
        {
            if (mods == null) return;
            foreach (StatModifier sm in mods)
                if (sm?.stat != null) into.Add(sm.stat);
        }

        private static void Add(Dictionary<StatDef, float> dict, StatDef stat, float value)
            => dict[stat] = dict.TryGetValue(stat, out float existing) ? existing + value : value;
    }
}
