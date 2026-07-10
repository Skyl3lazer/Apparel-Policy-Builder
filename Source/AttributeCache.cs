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

        // With a chosen material, stuff-powered stats use the real final value at that material
        // instead of the material-effect multiplier; everything else keeps its cached value.
        public float GetStatValue(StatDef stat, ThingDef material)
        {
            if (material != null && AttributeCache.IsStuffPowered(stat) && def.MadeFromStuff
                && material.stuffProps != null && material.stuffProps.CanMake(def))
                return def.GetStatValueAbstract(stat, material);
            return GetStatValue(stat);
        }
    }

    // Load-time cache of everything the filter UI and engine need.
    public static class AttributeCache
    {
        public static List<ApparelAttributeInfo> Apparel { get; private set; }
        public static List<StatDef> NumericAttributes { get; private set; }
        public static List<ApparelLayerDef> Layers { get; private set; }
        public static List<BodyPartGroupDef> Covers { get; private set; }
        public static List<ThingDef> StuffMaterials { get; private set; }

        // Stuff-powered stats (armor, insulation) mapped to the apparel multiplier stat that gates
        // them (StatPart_Stuff.multiplierStat). These live in no stat list; the material supplies them.
        private static Dictionary<StatDef, StatDef> stuffPoweredMultipliers;

        public static bool IsStuffPowered(StatDef stat)
            => stuffPoweredMultipliers != null && stuffPoweredMultipliers.ContainsKey(stat);

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

            // Materials any apparel can be stuffed with, for the "evaluate as" dropdown.
            var stuffSet = new HashSet<ThingDef>();
            foreach (ApparelAttributeInfo info in apparel)
                if (info.def.MadeFromStuff)
                    stuffSet.UnionWith(GenStuff.AllowedStuffsFor(info.def));
            StuffMaterials = stuffSet.OrderBy(s => s.label ?? s.defName).ToList();
        }

        private static Dictionary<StatDef, float> ComputeStatValues(ThingDef def)
        {
            var result = new Dictionary<StatDef, float>();

            // Equipped offsets apply to the wearer, not the item, so they aren't on the item's stat
            // card (ShouldShowFor would reject them for a non-pawn). Add them directly.
            if (def.equippedStatOffsets != null)
                foreach (StatModifier sm in def.equippedStatOffsets)
                    if (sm?.stat != null && !sm.stat.alwaysHide)
                        Add(result, sm.stat, sm.value);

            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            StatRequest req = StatRequest.For(def, stuff);

            // Item stats: the apparel's own base stats plus stats its material affects, but only those
            // that actually apply to this apparel (the check the info card uses). This drops
            // material-global stats a material only carries for construction, e.g. DoorOpenSpeed.
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

            // Armor/insulation come from the material; gauge them by the material-effect multiplier so
            // a piece still counts when its default material zeroes a given type.
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
