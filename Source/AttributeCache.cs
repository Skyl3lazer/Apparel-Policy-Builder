using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ApparelAttributeFilter
{
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

        // A chosen material reads stuff-powered stats at that material instead of by the multiplier.
        public float GetStatValue(StatDef stat, ThingDef material)
        {
            if (material != null && AttributeCache.IsStuffPowered(stat) && def.MadeFromStuff
                && material.stuffProps != null && material.stuffProps.CanMake(def))
                return def.GetStatValueAbstract(stat, material);
            return GetStatValue(stat);
        }
    }

    public static class AttributeCache
    {
        public static List<ApparelAttributeInfo> Apparel { get; private set; }
        public static List<StatDef> NumericAttributes { get; private set; }
        public static List<ApparelLayerDef> Layers { get; private set; }
        public static List<BodyPartGroupDef> Covers { get; private set; }
        public static List<ThingDef> StuffMaterials { get; private set; }
        public static bool MaterialFilterActive { get; private set; }
        public static List<ThingDef> MaterialAttributes { get; private set; }

        // Stuff-powered stats (armor/insulation) mapped to their StuffEffectMultiplier stat.
        private static Dictionary<StatDef, StatDef> stuffPoweredMultipliers;
        private static Dictionary<ThingDef, SpecialThingFilterDef> materialFilters;

        public static bool IsStuffPowered(StatDef stat)
            => stuffPoweredMultipliers != null && stuffPoweredMultipliers.ContainsKey(stat);

        public static SpecialThingFilterDef MaterialFilterFor(ThingDef stuff)
            => materialFilters != null && materialFilters.TryGetValue(stuff, out SpecialThingFilterDef sf) ? sf : null;

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

            var stuffSet = new HashSet<ThingDef>();
            foreach (ApparelAttributeInfo info in apparel)
                if (info.def.MadeFromStuff)
                    stuffSet.UnionWith(GenStuff.AllowedStuffsFor(info.def));
            StuffMaterials = stuffSet.OrderBy(s => s.label ?? s.defName).ToList();

            BuildMaterialFilterMap(stuffSet);
        }

        // Detected by the presence of its generated special filters rather than by packageId.
        private static void BuildMaterialFilterMap(HashSet<ThingDef> apparelStuffs)
        {
            const string prefix = "MaterialFilter_allow";
            materialFilters = new Dictionary<ThingDef, SpecialThingFilterDef>();
            foreach (SpecialThingFilterDef sf in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
            {
                if (sf.defName == null || !sf.defName.StartsWith(prefix)) continue;
                ThingDef stuff = DefDatabase<ThingDef>.GetNamedSilentFail(sf.defName.Substring(prefix.Length));
                if (stuff != null) materialFilters[stuff] = sf;
            }
            MaterialFilterActive = materialFilters.Count > 0;
            MaterialAttributes = materialFilters.Keys
                .Where(apparelStuffs.Contains)
                .OrderBy(s => s.label ?? s.defName)
                .ToList();
        }

        private static Dictionary<StatDef, float> ComputeStatValues(ThingDef def)
        {
            var result = new Dictionary<StatDef, float>();

            // Wearer offsets: ShouldShowFor rejects them for a non-pawn, so add them directly.
            if (def.equippedStatOffsets != null)
                foreach (StatModifier sm in def.equippedStatOffsets)
                    if (sm?.stat != null && !sm.stat.alwaysHide)
                        Add(result, sm.stat, sm.value);

            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            StatRequest req = StatRequest.For(def, stuff);

            // ShouldShowFor keeps only stats that apply to this apparel, dropping material-global
            // construction stats like DoorOpenSpeed that the stuff also carries.
            var candidates = new HashSet<StatDef>();
            if (def.statBases != null)
                foreach (StatModifier sm in def.statBases)
                    if (sm?.stat != null) candidates.Add(sm.stat);
            if (stuff?.stuffProps != null)
            {
                CollectStats(stuff.stuffProps.statOffsets, candidates);
                CollectStats(stuff.stuffProps.statFactors, candidates);
            }
            foreach (StatDef stat in candidates)
            {
                if (stat.alwaysHide || !stat.Worker.ShouldShowFor(req)) continue;
                Add(result, stat, def.GetStatValueAbstract(stat, stuff));
            }

            // Gauge armor/insulation by the multiplier so a piece counts even when its default
            // material zeroes a given type.
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
