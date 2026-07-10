using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ApparelPolicyBuilder
{
    public class ApparelAttributeInfo
    {
        public readonly ThingDef def;
        public readonly HashSet<ApparelLayerDef> Layers;
        private readonly Dictionary<StatDef, float> statValues;
        private readonly Dictionary<string, HashSet<string>> categoricalTokens;

        public ApparelAttributeInfo(ThingDef def, HashSet<ApparelLayerDef> layers,
            Dictionary<StatDef, float> statValues, Dictionary<string, HashSet<string>> categoricalTokens)
        {
            this.def = def;
            this.Layers = layers;
            this.statValues = statValues;
            this.categoricalTokens = categoricalTokens;
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

        public bool HasCategorical(string attrKey, string token)
            => categoricalTokens.TryGetValue(attrKey, out HashSet<string> set) && set.Contains(token);
    }

    public class CategoricalValue
    {
        public string token;
        public string label;
    }

    public class AttributeOption
    {
        public string key;
        public string label;
        public StatCategoryDef category; // null for facets
        public int order;
        public RuleAttributeKind kind;
        public StatDef stat;                 // Numeric
        public List<CategoricalValue> values; // Categorical
        public SpecialThingFilterDef specialFilter; // SpecialFilter
    }

    public static class AttributeCache
    {
        public static List<ApparelAttributeInfo> Apparel { get; private set; }
        public static List<AttributeOption> Options { get; private set; }
        public static List<ApparelLayerDef> Layers { get; private set; }
        public static List<ThingDef> StuffMaterials { get; private set; }
        public static List<ThingDef> MaterialAttributes { get; private set; }
        public static bool MaterialFilterActive { get; private set; }
        public static bool QualityFacetActive { get; private set; }
        public static bool HitPointsFacetActive { get; private set; }

        // Stuff-powered stats (armor/insulation) mapped to their StuffEffectMultiplier stat.
        private static Dictionary<StatDef, StatDef> stuffPoweredMultipliers;
        private static Dictionary<ThingDef, SpecialThingFilterDef> materialFilters;
        private static Dictionary<string, AttributeOption> optionsByKey;

        public static bool IsStuffPowered(StatDef stat)
            => stuffPoweredMultipliers != null && stuffPoweredMultipliers.ContainsKey(stat);

        public static AttributeOption OptionFor(string key)
            => key != null && optionsByKey != null && optionsByKey.TryGetValue(key, out AttributeOption o) ? o : null;

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
            var catOptions = new Dictionary<string, AttributeOption>();
            var catValueLabels = new Dictionary<string, Dictionary<string, string>>();
            bool qualityActive = false, hpActive = false;

            stuffPoweredMultipliers = new Dictionary<StatDef, StatDef>();
            foreach (StatDef s in DefDatabase<StatDef>.AllDefsListForReading)
            {
                StatPart_Stuff part = s.parts?.OfType<StatPart_Stuff>().FirstOrDefault();
                if (part?.multiplierStat != null) stuffPoweredMultipliers[s] = part.multiplierStat;
            }

            // Exactly the defs the apparel policy screen shows: its parent filter allows the
            // Apparel category. Scanning by def.IsApparel is broader and leaks non-apparel gear.
            var apparelFilter = new ThingFilter();
            apparelFilter.SetAllow(ThingCategoryDefOf.Apparel, true);
            foreach (ThingDef def in apparelFilter.AllowedThingDefs)
            {
                if (def.apparel == null) continue;
                try
                {
                    var layers = def.apparel.layers != null
                        ? new HashSet<ApparelLayerDef>(def.apparel.layers)
                        : new HashSet<ApparelLayerDef>();
                    Dictionary<StatDef, float> statValues = ComputeStatValues(def);
                    var catTokens = DiscoverCategorical(def, catOptions, catValueLabels);

                    apparel.Add(new ApparelAttributeInfo(def, layers, statValues, catTokens));
                    layerSet.UnionWith(layers);
                    numericSet.UnionWith(statValues.Keys);
                    if (!qualityActive && def.FollowQualityThingFilter()) qualityActive = true;
                    if (!hpActive && def.useHitPoints) hpActive = true;
                }
                catch (Exception e)
                {
                    Log.Warning($"[Apparel Policy Builder] Skipped caching {def.defName}: {e.Message}");
                }
            }

            Apparel = apparel;
            Layers = layerSet.OrderBy(l => l.drawOrder).ToList();
            QualityFacetActive = qualityActive;
            HitPointsFacetActive = hpActive;

            var stuffSet = new HashSet<ThingDef>();
            foreach (ApparelAttributeInfo info in apparel)
                if (info.def.MadeFromStuff)
                    stuffSet.UnionWith(GenStuff.AllowedStuffsFor(info.def));
            StuffMaterials = stuffSet.OrderBy(s => s.label ?? s.defName).ToList();
            BuildMaterialFilterMap(stuffSet);

            foreach (KeyValuePair<string, AttributeOption> kv in catOptions)
                kv.Value.values = catValueLabels[kv.Key]
                    .Select(p => new CategoricalValue { token = p.Key, label = p.Value })
                    .OrderBy(v => v.label).ToList();

            Options = BuildOptions(numericSet, catOptions.Values, DiscoverSpecialFilters(apparel));
            optionsByKey = new Dictionary<string, AttributeOption>();
            foreach (AttributeOption o in Options) optionsByKey[o.key] = o;
        }

        private static List<AttributeOption> BuildOptions(HashSet<StatDef> numericSet,
            IEnumerable<AttributeOption> categorical, List<SpecialThingFilterDef> specialFilters)
        {
            var options = new List<AttributeOption>();
            foreach (StatDef s in numericSet)
                options.Add(new AttributeOption
                {
                    key = "stat:" + s.defName,
                    label = s.LabelCap,
                    category = s.category,
                    order = s.displayPriorityInCategory,
                    kind = RuleAttributeKind.Numeric,
                    stat = s
                });
            options.AddRange(categorical);
            if (QualityFacetActive)
                options.Add(new AttributeOption { key = "facet:quality", order = 0, kind = RuleAttributeKind.Quality });
            if (HitPointsFacetActive)
                options.Add(new AttributeOption { key = "facet:hitpoints", order = 1, kind = RuleAttributeKind.HitPoints });
            if (MaterialFilterActive && MaterialAttributes.Count > 0)
                options.Add(new AttributeOption { key = "facet:material", order = 2, kind = RuleAttributeKind.Material });
            int i = 10;
            foreach (SpecialThingFilterDef sf in specialFilters)
                options.Add(new AttributeOption
                {
                    key = "sf:" + sf.defName,
                    label = sf.LabelCap,
                    order = i++,
                    kind = RuleAttributeKind.SpecialFilter,
                    specialFilter = sf
                });
            return options;
        }

        // The special filters the apparel policy tree draws: those structurally attached to the
        // Apparel category (its own, its descendants', and its ancestors') and able to match apparel,
        // minus the one the dialog hides and Material Filter's per-material filters.
        private static List<SpecialThingFilterDef> DiscoverSpecialFilters(List<ApparelAttributeInfo> apparel)
        {
            var result = new List<SpecialThingFilterDef>();
            ThingCategoryDef apparelCat = ThingCategoryDefOf.Apparel;
            if (apparelCat == null) return result;

            var candidates = new HashSet<SpecialThingFilterDef>();
            foreach (SpecialThingFilterDef sf in apparelCat.DescendantSpecialThingFilterDefs) candidates.Add(sf);
            foreach (SpecialThingFilterDef sf in apparelCat.ParentsSpecialThingFilterDefs) candidates.Add(sf);

            foreach (SpecialThingFilterDef sf in candidates)
            {
                if (sf == null || !sf.configurable || sf == SpecialThingFilterDefOf.AllowNonDeadmansApparel) continue;
                if (sf.defName != null && sf.defName.StartsWith("MaterialFilter_allow")) continue;
                bool matches = false;
                foreach (ApparelAttributeInfo info in apparel)
                    if (sf.Worker.CanEverMatch(info.def)) { matches = true; break; }
                if (matches) result.Add(sf);
            }
            result.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // Reads an apparel's info-card categorical entries, registering each as an option and
        // returning this apparel's value tokens per attribute key.
        private static Dictionary<string, HashSet<string>> DiscoverCategorical(ThingDef def,
            Dictionary<string, AttributeOption> catOptions, Dictionary<string, Dictionary<string, string>> catValueLabels)
        {
            var tokens = new Dictionary<string, HashSet<string>>();
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            StatRequest req = StatRequest.For(def, stuff);

            IEnumerable<StatDrawEntry> entries;
            try { entries = def.SpecialDisplayStats(req); }
            catch { return tokens; }
            if (entries == null) return tokens;

            foreach (StatDrawEntry entry in entries)
            {
                if (entry == null || entry.stat != null || entry.category == null) continue;
                string label = entry.LabelCap;
                if (label.NullOrEmpty()) continue;
                List<CategoricalValue> values = ExtractCategoricalValues(entry, req);
                if (values.Count == 0) continue;

                string key = "cat:" + entry.category.defName + ":" + label;
                if (!catOptions.TryGetValue(key, out AttributeOption opt))
                {
                    opt = new AttributeOption
                    {
                        key = key,
                        label = label,
                        category = entry.category,
                        order = entry.DisplayPriorityWithinCategory,
                        kind = RuleAttributeKind.Categorical
                    };
                    catOptions[key] = opt;
                    catValueLabels[key] = new Dictionary<string, string>();
                }

                Dictionary<string, string> seen = catValueLabels[key];
                if (!tokens.TryGetValue(key, out HashSet<string> apparelSet))
                {
                    apparelSet = new HashSet<string>();
                    tokens[key] = apparelSet;
                }
                foreach (CategoricalValue v in values)
                {
                    seen[v.token] = v.label;
                    apparelSet.Add(v.token);
                }
            }
            return tokens;
        }

        private static List<CategoricalValue> ExtractCategoricalValues(StatDrawEntry entry, StatRequest req)
        {
            var result = new List<CategoricalValue>();
            IEnumerable<Dialog_InfoCard.Hyperlink> links = null;
            try { links = entry.GetHyperlinks(req); } catch { }
            if (links != null)
                foreach (Dialog_InfoCard.Hyperlink h in links)
                    if (h.def != null) result.Add(new CategoricalValue { token = h.def.defName, label = h.def.LabelCap });

            if (result.Count == 0)
            {
                string vs = entry.ValueString;
                if (!vs.NullOrEmpty())
                    foreach (string part in vs.Split(','))
                    {
                        string t = part.Trim();
                        if (t.Length > 0) result.Add(new CategoricalValue { token = t, label = t });
                    }
            }
            return result;
        }

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
