using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ApparelPolicyBuilder
{
    public readonly struct LensValues
    {
        public readonly float lowest;
        public readonly float typical;
        public readonly float highest;

        public LensValues(float lowest, float typical, float highest)
        {
            this.lowest = lowest;
            this.typical = typical;
            this.highest = highest;
        }
    }

    public class ApparelAttributeInfo
    {
        public readonly ThingDef def;
        public readonly HashSet<ApparelLayerDef> Layers;
        private readonly Dictionary<StatDef, float> statValues;
        private readonly Dictionary<StatDef, LensValues> lensValues; // null for apparel not made from stuff
        private readonly Dictionary<string, HashSet<string>> categoricalTokens;

        public ApparelAttributeInfo(ThingDef def, HashSet<ApparelLayerDef> layers,
            Dictionary<StatDef, float> statValues, Dictionary<string, HashSet<string>> categoricalTokens,
            Dictionary<StatDef, LensValues> lensValues = null)
        {
            this.def = def;
            this.Layers = layers;
            this.statValues = statValues;
            this.categoricalTokens = categoricalTokens;
            this.lensValues = lensValues;
        }

        public float GetStatValue(StatDef stat)
            => statValues.TryGetValue(stat, out float v) ? v : 0f;

        public float GetStatValue(StatDef stat, MaterialLens lens)
        {
            switch (lens.mode)
            {
                case MaterialLensMode.Material:
                    // A chosen material reads stuff-powered stats at that material instead of by the multiplier.
                    if (lens.IsNamedMaterial && AttributeCache.IsStuffPowered(stat) && def.MadeFromStuff
                        && lens.stuff.stuffProps != null && lens.stuff.stuffProps.CanMake(def))
                        return def.GetStatValueAbstract(stat, lens.stuff);
                    return GetStatValue(stat);

                case MaterialLensMode.Lowest:
                case MaterialLensMode.Typical:
                case MaterialLensMode.Highest:
                    if (lensValues == null || !lensValues.TryGetValue(stat, out LensValues lv))
                        return GetStatValue(stat);
                    if (lens.mode == MaterialLensMode.Lowest) return lv.lowest;
                    return lens.mode == MaterialLensMode.Typical ? lv.typical : lv.highest;

                default:
                    return GetStatValue(stat);
            }
        }

        private static readonly HashSet<string> emptyTokens = new HashSet<string>();

        public bool HasCategorical(string attrKey, string token)
            => categoricalTokens.TryGetValue(attrKey, out HashSet<string> set) && set.Contains(token);

        public HashSet<string> TokensFor(string attrKey)
            => categoricalTokens.TryGetValue(attrKey, out HashSet<string> set) ? set : emptyTokens;
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
        // A discovered set of things (apparel or weapons) scanned identically; weapons carry no layers.
        private class Universe
        {
            public readonly List<ApparelAttributeInfo> infos = new List<ApparelAttributeInfo>();
            public readonly HashSet<StatDef> numericSet = new HashSet<StatDef>();
            public readonly HashSet<ApparelLayerDef> layerSet = new HashSet<ApparelLayerDef>();
            public readonly Dictionary<string, AttributeOption> catOptions = new Dictionary<string, AttributeOption>();
            public readonly Dictionary<string, Dictionary<string, string>> catValueLabels = new Dictionary<string, Dictionary<string, string>>();
            public readonly HashSet<ThingDef> stuffSet = new HashSet<ThingDef>();
            public bool qualityActive, hpActive;
        }

        public static List<ApparelAttributeInfo> Apparel { get; private set; }
        public static List<ApparelAttributeInfo> Weapons { get; private set; }
        public static List<AttributeOption> Options { get; private set; }       // apparel palette
        public static List<AttributeOption> WeaponOptions { get; private set; } // weapon palette
        public static List<ApparelLayerDef> Layers { get; private set; }
        public static List<ThingDef> StuffMaterials { get; private set; }
        public static List<ThingDef> MaterialAttributes { get; private set; }
        public static bool MaterialFilterActive { get; private set; }
        public static bool QualityFacetActive { get; private set; }
        public static bool HitPointsFacetActive { get; private set; }

        // True only when Auto Arm has folded weapons into the policy tree, so any weapon was scanned.
        public static bool WeaponsActive => Weapons != null && Weapons.Count > 0;

        // Stuff-powered stats (armor/insulation) mapped to their StuffEffectMultiplier stat.
        private static Dictionary<StatDef, StatDef> stuffPoweredMultipliers;
        // A StatPart_Stuff's power and multiplier stats restate the real apparel stat, so they are excluded.
        private static HashSet<StatDef> stuffMetaStats;
        // Stats whose worker throws at def level (needs a spawned thing); learned once, then skipped.
        private static HashSet<StatDef> unevaluableStats;
        private static Dictionary<ThingDef, SpecialThingFilterDef> materialFilters;
        private static HashSet<string> stuffCategoryNames;
        private static Dictionary<string, AttributeOption> optionsByKey;
        private static Dictionary<string, AttributeOption> weaponOptionsByKey;

        public static bool IsStuffPowered(StatDef stat)
            => stuffPoweredMultipliers != null && stuffPoweredMultipliers.ContainsKey(stat);

        public static bool IsStuffCategory(string token)
            => token != null && stuffCategoryNames != null && stuffCategoryNames.Contains(token);

        public static AttributeOption OptionFor(string key) => OptionFor(key, false);

        public static AttributeOption OptionFor(string key, bool weapon)
        {
            Dictionary<string, AttributeOption> map = weapon ? weaponOptionsByKey : optionsByKey;
            return key != null && map != null && map.TryGetValue(key, out AttributeOption o) ? o : null;
        }

        public static SpecialThingFilterDef MaterialFilterFor(ThingDef stuff)
            => materialFilters != null && materialFilters.TryGetValue(stuff, out SpecialThingFilterDef sf) ? sf : null;

        public static void EnsureBuilt()
        {
            if (Apparel == null) Build();
        }

        public static void Build()
        {
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

            var apparelU = new Universe();
            var weaponU = new Universe();

            // Exactly the defs the policy screen shows, since its filter allows the Apparel category;
            // Auto Arm folds weapons in under it, so split them out rather than scanning by def.IsApparel.
            var apparelFilter = new ThingFilter();
            apparelFilter.SetAllow(ThingCategoryDefOf.Apparel, true);
            foreach (ThingDef def in apparelFilter.AllowedThingDefs)
            {
                Universe u;
                if (def.apparel != null) u = apparelU;
                else if (def.IsWeapon) u = weaponU;
                else continue;
                try { ScanDef(def, u); }
                catch (Exception e)
                {
                    Log.Warning($"[Apparel Policy Builder] Skipped caching {def.defName}: {e.Message}");
                }
            }

            Apparel = apparelU.infos;
            Weapons = weaponU.infos;
            Layers = apparelU.layerSet.OrderBy(l => l.drawOrder).ToList();
            QualityFacetActive = apparelU.qualityActive;
            HitPointsFacetActive = apparelU.hpActive;

            var allStuffs = new HashSet<ThingDef>(apparelU.stuffSet);
            allStuffs.UnionWith(weaponU.stuffSet);
            StuffMaterials = allStuffs.OrderBy(s => s.label ?? s.defName).ToList();
            BuildMaterialFilterMap(allStuffs);

            FinalizeCatValues(apparelU);
            FinalizeCatValues(weaponU);

            bool apparelMaterial = MaterialFilterActive && apparelU.stuffSet.Any(materialFilters.ContainsKey);
            bool weaponMaterial = MaterialFilterActive && weaponU.stuffSet.Any(materialFilters.ContainsKey);

            var weaponCats = ThingCategoryDefOf.Weapons != null
                ? new HashSet<ThingCategoryDef>(ThingCategoryDefOf.Weapons.ThisAndChildCategoryDefs)
                : new HashSet<ThingCategoryDef>();

            Options = BuildOptions(apparelU.numericSet, apparelU.catOptions.Values,
                DiscoverSpecialFilters(apparelU.infos, weapon: false, weaponCats), apparelU.qualityActive, apparelU.hpActive, apparelMaterial);
            WeaponOptions = weaponU.infos.Count > 0
                ? BuildOptions(weaponU.numericSet, weaponU.catOptions.Values,
                    DiscoverSpecialFilters(weaponU.infos, weapon: true, weaponCats), weaponU.qualityActive, weaponU.hpActive, weaponMaterial)
                : new List<AttributeOption>();

            optionsByKey = BuildByKey(Options);
            weaponOptionsByKey = BuildByKey(WeaponOptions);
        }

        private static void ScanDef(ThingDef def, Universe u)
        {
            var layers = def.apparel?.layers != null
                ? new HashSet<ApparelLayerDef>(def.apparel.layers)
                : new HashSet<ApparelLayerDef>();
            Dictionary<StatDef, float> statValues = ComputeStatValues(def);
            var catTokens = DiscoverCategorical(def, u.catOptions, u.catValueLabels);
            Dictionary<StatDef, LensValues> lensValues = ComputeLensValues(def, statValues);

            u.infos.Add(new ApparelAttributeInfo(def, layers, statValues, catTokens, lensValues));
            u.layerSet.UnionWith(layers);
            // Offer a stat only when some loaded thing actually carries a non-default value for it.
            foreach (KeyValuePair<StatDef, float> kv in statValues)
                if (kv.Value != kv.Key.defaultBaseValue) u.numericSet.Add(kv.Key);
            if (!u.qualityActive && def.FollowQualityThingFilter()) u.qualityActive = true;
            if (!u.hpActive && def.useHitPoints) u.hpActive = true;
            if (def.MadeFromStuff) u.stuffSet.UnionWith(GenStuff.AllowedStuffsFor(def));
        }

        private static void FinalizeCatValues(Universe u)
        {
            foreach (KeyValuePair<string, AttributeOption> kv in u.catOptions)
                kv.Value.values = u.catValueLabels[kv.Key]
                    .Select(p => new CategoricalValue { token = p.Key, label = p.Value })
                    .OrderBy(v => v.label).ToList();
        }

        private static Dictionary<string, AttributeOption> BuildByKey(List<AttributeOption> opts)
        {
            var map = new Dictionary<string, AttributeOption>();
            foreach (AttributeOption o in opts) map[o.key] = o;
            return map;
        }

        private static List<AttributeOption> BuildOptions(HashSet<StatDef> numericSet,
            IEnumerable<AttributeOption> categorical, List<SpecialThingFilterDef> specialFilters,
            bool qualityActive, bool hpActive, bool materialActive)
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
            if (qualityActive)
                options.Add(new AttributeOption { key = "facet:quality", label = "APB.Facet.Quality".Translate(), order = 0, kind = RuleAttributeKind.Quality });
            if (hpActive)
                options.Add(new AttributeOption { key = "facet:hitpoints", label = "APB.Facet.HitPoints".Translate(), order = 1, kind = RuleAttributeKind.HitPoints });
            if (materialActive)
                options.Add(new AttributeOption { key = "facet:material", label = "APB.Facet.Material".Translate(), order = 2, kind = RuleAttributeKind.Material });
            int i = 10;
            foreach (SpecialThingFilterDef sf in specialFilters)
                options.Add(new AttributeOption
                {
                    key = "sf:" + sf.defName,
                    label = CleanSpecialFilterLabel(sf),
                    order = i++,
                    kind = RuleAttributeKind.SpecialFilter,
                    specialFilter = sf
                });
            return options;
        }

        public static string CleanSpecialFilterLabel(SpecialThingFilterDef sf)
        {
            string label = sf?.LabelCap;
            if (label.NullOrEmpty()) return label;
            return label.StartsWith("allow ", StringComparison.OrdinalIgnoreCase)
                ? label.Substring(6).CapitalizeFirst() : label;
        }

        // Routing by attachment (weapon-subtree -> weapon palette, else apparel) rather than by what the
        // worker matches keeps parallel same-labelled defs (AllowBurnableApparel vs AllowBurnableWeapons)
        // from both landing in one palette.
        private static List<SpecialThingFilterDef> DiscoverSpecialFilters(List<ApparelAttributeInfo> universe,
            bool weapon, HashSet<ThingCategoryDef> weaponCats)
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
                bool attachedToWeapon = sf.parentCategory != null && weaponCats.Contains(sf.parentCategory);
                if (attachedToWeapon != weapon) continue;
                bool matches = false;
                try
                {
                    foreach (ApparelAttributeInfo info in universe)
                        if (sf.Worker.CanEverMatch(info.def)) { matches = true; break; }
                }
                catch (Exception) { continue; }
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
                bool allNumeric = true;
                for (int i = 0; i < values.Count; i++)
                {
                    if (!LooksNumeric(values[i].token))
                    {
                        allNumeric = false;
                        break;
                    }
                }
                if (allNumeric) continue;

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

        private const float TrimFraction = 0.10f;

        // Unlike the multiplier gauge, these land on the same scale as non-stuffable apparel, so a threshold spans both.
        private static Dictionary<StatDef, LensValues> ComputeLensValues(ThingDef def, Dictionary<StatDef, float> statValues)
        {
            if (!def.MadeFromStuff) return null;
            List<ThingDef> allowed = GenStuff.AllowedStuffsFor(def).ToList();
            if (allowed.Count == 0) return null;

            List<List<int>> groups = BuildCategoryGroups(def, allowed);
            var offsets = new Dictionary<StatDef, float>();
            if (def.equippedStatOffsets != null)
                foreach (StatModifier sm in def.equippedStatOffsets)
                    if (sm?.stat != null) Add(offsets, sm.stat, sm.value);

            StatRequest req = StatRequest.For(def, GenStuff.DefaultStuffFor(def));
            var result = new Dictionary<StatDef, LensValues>();
            var values = new List<float>(allowed.Count);
            var catValues = new List<float>(groups.Count);
            var scratch = new List<float>(allowed.Count);

            foreach (StatDef stat in statValues.Keys)
            {
                if (unevaluableStats.Contains(stat)) continue;
                // Wearer offsets do not vary by material, so a stat with no material-varying term has nothing to collapse.
                try { if (!stat.Worker.ShouldShowFor(req)) continue; }
                catch { continue; }

                values.Clear();
                try
                {
                    foreach (ThingDef stuff in allowed) values.Add(def.GetStatValueAbstract(stat, stuff));
                }
                catch
                {
                    unevaluableStats.Add(stat);
                    continue;
                }

                float lowest = values[0], highest = values[0];
                for (int i = 1; i < values.Count; i++)
                {
                    if (values[i] < lowest) lowest = values[i];
                    if (values[i] > highest) highest = values[i];
                }

                catValues.Clear();
                foreach (List<int> group in groups)
                {
                    scratch.Clear();
                    foreach (int i in group) scratch.Add(values[i]);
                    scratch.Sort();
                    catValues.Add(TrimmedMean(scratch, TrimFraction));
                }

                offsets.TryGetValue(stat, out float offset);
                result[stat] = new LensValues(lowest + offset, Median(catValues) + offset, highest + offset);
            }

            return result.Count > 0 ? result : null;
        }

        // Typical weights categories, not materials, so a category holding forty leathers cannot outvote one holding three metals.
        private static List<List<int>> BuildCategoryGroups(ThingDef def, List<ThingDef> allowed)
        {
            var groups = new List<List<int>>();
            if (def.stuffCategories != null)
                foreach (StuffCategoryDef cat in def.stuffCategories)
                {
                    var group = new List<int>();
                    for (int i = 0; i < allowed.Count; i++)
                        if (allowed[i].stuffProps?.categories != null && allowed[i].stuffProps.categories.Contains(cat))
                            group.Add(i);
                    if (group.Count > 0) groups.Add(group);
                }

            if (groups.Count == 0)
            {
                var all = new List<int>(allowed.Count);
                for (int i = 0; i < allowed.Count; i++) all.Add(i);
                groups.Add(all);
            }
            return groups;
        }

        // Dims exotic materials without deleting them; a trim that would empty the list degrades to the plain mean.
        private static float TrimmedMean(List<float> sorted, float fraction)
        {
            int drop = (int)(sorted.Count * fraction);
            int lo = drop, hi = sorted.Count - drop;
            if (hi <= lo) { lo = 0; hi = sorted.Count; }
            float sum = 0f;
            for (int i = lo; i < hi; i++) sum += sorted[i];
            return sum / (hi - lo);
        }

        private static float Median(List<float> values)
        {
            values.Sort();
            int n = values.Count;
            return (n & 1) == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) * 0.5f;
        }
    }
}
