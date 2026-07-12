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

        public IEnumerable<string> TokensFor(string attrKey)
            => categoricalTokens.TryGetValue(attrKey, out HashSet<string> set) ? set : Enumerable.Empty<string>();
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
        // A StatPart_Stuff's power and multiplier stats restate the real apparel stat, so they are excluded.
        private static HashSet<StatDef> stuffMetaStats;
        // Stats whose worker throws at def level (needs a spawned thing); learned once, then skipped.
        private static HashSet<StatDef> unevaluableStats;
        private static Dictionary<ThingDef, SpecialThingFilterDef> materialFilters;
        private static HashSet<string> stuffCategoryNames;
        private static Dictionary<string, AttributeOption> optionsByKey;

        public static bool IsStuffPowered(StatDef stat)
            => stuffPoweredMultipliers != null && stuffPoweredMultipliers.ContainsKey(stat);

        public static bool IsStuffCategory(string token)
            => token != null && stuffCategoryNames != null && stuffCategoryNames.Contains(token);

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
            stuffMetaStats = new HashSet<StatDef>();
            unevaluableStats = new HashSet<StatDef>();
            foreach (StatDef s in DefDatabase<StatDef>.AllDefsListForReading)
            {
                StatPart_Stuff part = s.parts?.OfType<StatPart_Stuff>().FirstOrDefault();
                if (part == null) continue;
                if (part.multiplierStat != null)
                {
                    stuffPoweredMultipliers[s] = part.multiplierStat;
                    stuffMetaStats.Add(part.multiplierStat);
                }
                if (part.stuffPowerStat != null) stuffMetaStats.Add(part.stuffPowerStat);
            }

            stuffCategoryNames = new HashSet<string>(
                DefDatabase<StuffCategoryDef>.AllDefsListForReading.Select(c => c.defName));

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
                    // Offer a stat only when some loaded apparel actually carries a non-default value for it.
                    foreach (KeyValuePair<StatDef, float> kv in statValues)
                        if (kv.Value != kv.Key.defaultBaseValue) numericSet.Add(kv.Key);
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
                // Numeric-valued card entries (weapon stats like Miss Radius, percentages) aren't
                // meaningful as a value picker and have no StatDef to filter on, so skip them.
                if (values.All(v => LooksNumeric(v.token))) continue;

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
                    catValueLabels[key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                Dictionary<string, string> seen = catValueLabels[key];
                if (!tokens.TryGetValue(key, out HashSet<string> apparelSet))
                {
                    apparelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    tokens[key] = apparelSet;
                }
                foreach (CategoricalValue v in values)
                {
                    seen[v.token] = v.label.CapitalizeFirst();
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
            {
                List<StuffCategoryDef> madeFrom = (req.Def as ThingDef)?.stuffCategories;
                foreach (Dialog_InfoCard.Hyperlink h in links)
                {
                    if (h.def == null) continue;
                    // A material collapses to the stuff category this piece is made from; one required as a discrete ingredient stays itself.
                    bool collapsed = false;
                    if (madeFrom != null && h.def is ThingDef td && td.stuffProps?.categories != null)
                        foreach (StuffCategoryDef cat in td.stuffProps.categories)
                            if (madeFrom.Contains(cat)) { result.Add(new CategoricalValue { token = cat.defName, label = cat.LabelCap }); collapsed = true; }
                    if (!collapsed)
                        result.Add(new CategoricalValue { token = h.def.defName, label = h.def.LabelCap });
                }
            }

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

        private static bool LooksNumeric(string s)
        {
            s = s?.TrimStart();
            if (s.NullOrEmpty()) return false;
            char c = s[0];
            if ((c == '+' || c == '-') && s.Length > 1) c = s[1];
            return char.IsDigit(c);
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

            // Scan every stat, not just statBases, so computed stats like market value appear too.
            foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
            {
                if (stat.alwaysHide || stuffPoweredMultipliers.ContainsKey(stat)
                    || stuffMetaStats.Contains(stat) || unevaluableStats.Contains(stat))
                    continue;
                try
                {
                    if (stat.Worker.ShouldShowFor(req)) Add(result, stat, def.GetStatValueAbstract(stat, stuff));
                }
                catch
                {
                    unevaluableStats.Add(stat);
                }
            }

            // Stuff-powered stats are skipped above; gauge stuffables by the multiplier (counts even when the material zeroes the type), read non-stuffables' intrinsic value.
            if (stuffPoweredMultipliers != null)
                foreach (KeyValuePair<StatDef, StatDef> pair in stuffPoweredMultipliers)
                {
                    StatDef stat = pair.Key;
                    if (stat.alwaysHide || result.ContainsKey(stat)) continue;
                    float value = stuff != null ? def.GetStatValueAbstract(pair.Value) : def.GetStatValueAbstract(stat, null);
                    if (value != 0f) result[stat] = value;
                }

            return result;
        }


        private static void Add(Dictionary<StatDef, float> dict, StatDef stat, float value)
            => dict[stat] = dict.TryGetValue(stat, out float existing) ? existing + value : value;
    }
}
