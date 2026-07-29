using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelPolicyBuilder
{
    public enum RulePolarity : byte { Forbid, Require }

    public enum RuleAttributeKind : byte { Numeric, Categorical, Quality, HitPoints, Material, SpecialFilter }

    public enum NumericMode : byte { Positive, Negative, None, GreaterThan, LessThan, EqualTo }

    public enum RangeBound : byte { AtLeast, AtMost }

    // Multiplier must stay 0 so an absent Scribe node and a default(MaterialLens) both mean the legacy mode.
    public enum MaterialLensMode : byte { Multiplier, Lowest, Typical, Highest, Material }

    public readonly struct MaterialLens
    {
        public readonly MaterialLensMode mode;
        public readonly ThingDef stuff; // only meaningful when mode is Material

        public MaterialLens(MaterialLensMode mode, ThingDef stuff = null)
        {
            this.mode = mode;
            this.stuff = mode == MaterialLensMode.Material ? stuff : null;
        }

        public static MaterialLens Multiplier => new MaterialLens(MaterialLensMode.Multiplier);

        // A Material lens whose stuff went missing degrades to the multiplier rather than to no filtering at all.
        public bool IsNamedMaterial => mode == MaterialLensMode.Material && stuff != null;
    }

    public abstract class RuleScalars
    {
        public RulePolarity polarity = RulePolarity.Forbid;
        public bool exceptUtility;
        public bool utilityOnly;
        public bool weaponScope; // keeps weapon rules disjoint from apparel; per-def kinds only
        public RuleAttributeKind kind = RuleAttributeKind.Numeric;
        public NumericMode numericMode = NumericMode.Negative;
        public float threshold; // also the HitPoints fraction for that facet
        public string attrKey;
        public string categoricalValue;
        public RangeBound rangeBound = RangeBound.AtLeast;
        public QualityCategory qualityValue = QualityCategory.Normal;

        public void CopyScalarsFrom(RuleScalars src)
        {
            polarity = src.polarity;
            exceptUtility = src.exceptUtility;
            utilityOnly = src.utilityOnly;
            weaponScope = src.weaponScope;
            kind = src.kind;
            numericMode = src.numericMode;
            threshold = src.threshold;
            attrKey = src.attrKey;
            categoricalValue = src.categoricalValue;
            rangeBound = src.rangeBound;
            qualityValue = src.qualityValue;
        }

        protected void ExposeScalars()
        {
            Scribe_Values.Look(ref polarity, "polarity", RulePolarity.Forbid);
            Scribe_Values.Look(ref exceptUtility, "exceptUtility", false);
            Scribe_Values.Look(ref utilityOnly, "utilityOnly", false);
            Scribe_Values.Look(ref weaponScope, "weaponScope", false);
            Scribe_Values.Look(ref kind, "kind", RuleAttributeKind.Numeric);
            Scribe_Values.Look(ref numericMode, "numericMode", NumericMode.Negative);
            Scribe_Values.Look(ref threshold, "threshold", 0f);
            Scribe_Values.Look(ref attrKey, "attrKey");
            Scribe_Values.Look(ref categoricalValue, "categoricalValue");
            Scribe_Values.Look(ref rangeBound, "rangeBound", RangeBound.AtLeast);
            Scribe_Values.Look(ref qualityValue, "qualityValue", QualityCategory.Normal);
        }
    }

    public class AttributeRule : RuleScalars, IExposable
    {
        public ApparelLayerDef layerScope; // null = Global; per-def kinds only
        public StatDef stat;
        public ThingDef materialStuff;
        public SpecialThingFilterDef specialFilter; // Require = allow, Forbid = disallow

        public AttributeRule() { }

        // Per-def rules toggle apparel defs; facet rules write policy-wide filter settings.
        public bool IsPerDef => kind == RuleAttributeKind.Numeric || kind == RuleAttributeKind.Categorical;

        public bool IsValid
        {
            get
            {
                switch (kind)
                {
                    case RuleAttributeKind.Numeric: return stat != null;
                    case RuleAttributeKind.Categorical: return !attrKey.NullOrEmpty() && categoricalValue != null;
                    case RuleAttributeKind.Material: return materialStuff != null;
                    case RuleAttributeKind.SpecialFilter: return specialFilter != null;
                    default: return true;
                }
            }
        }

        public bool NeedsThreshold =>
            kind == RuleAttributeKind.Numeric &&
            (numericMode == NumericMode.GreaterThan
             || numericMode == NumericMode.LessThan
             || numericMode == NumericMode.EqualTo);

        public bool ConditionMatches(ApparelAttributeInfo info, MaterialLens lens)
        {
            if (kind == RuleAttributeKind.Categorical)
                return info.HasCategorical(attrKey, categoricalValue);

            if (stat == null) return false;
            return ConditionEval.NumericMatches(numericMode, info.GetStatValue(stat, lens), threshold);
        }

        private static HashSet<ApparelLayerDef> utilityLayers;

        internal static void InvalidateUtilityLayers() => utilityLayers = null;

        private static HashSet<ApparelLayerDef> UtilityLayers
        {
            get
            {
                if (utilityLayers != null) return utilityLayers;
                utilityLayers = new HashSet<ApparelLayerDef>();
                Dictionary<string, bool> overrides = ApparelPolicyBuilderMod.LayerUtilityOverrides;
                foreach (ApparelLayerDef layer in DefDatabase<ApparelLayerDef>.AllDefsListForReading)
                {
                    bool util = layer.HasModExtension<UtilityLayerExtension>();
                    if (overrides.TryGetValue(layer.defName, out bool forced)) util = forced;
                    if (util) utilityLayers.Add(layer);
                }
                return utilityLayers;
            }
        }

        public bool InScope(ApparelAttributeInfo info)
            => IsInScope(layerScope, exceptUtility, utilityOnly, info);

        internal static bool IsInScope(ApparelLayerDef layerScope, bool exceptUtility, bool utilityOnly, ApparelAttributeInfo info)
        {
            if (utilityOnly) return UtilityLayers.Overlaps(info.Layers);
            if (exceptUtility) return !UtilityLayers.Overlaps(info.Layers);
            return layerScope == null || info.Layers.Contains(layerScope);
        }

        public bool Disqualifies(ApparelAttributeInfo info, MaterialLens lens)
        {
            if (!InScope(info)) return false;
            bool matches = ConditionMatches(info, lens);
            return polarity == RulePolarity.Forbid ? matches : !matches;
        }

        public AttributeRule Clone() => (AttributeRule)MemberwiseClone();

        public void ExposeData()
        {
            ExposeScalars();
            Scribe_Defs.Look(ref layerScope, "layerScope");
            Scribe_Defs.Look(ref stat, "stat");
            Scribe_Defs.Look(ref materialStuff, "materialStuff");
            Scribe_Defs.Look(ref specialFilter, "specialFilter");
        }
    }

    public class Ruleset : IExposable
    {
        public List<AttributeRule> rules = new List<AttributeRule>();
        public List<ExpressionRule> expressionRules = new List<ExpressionRule>();
        public MaterialLensMode evalMode = MaterialLensMode.Typical;
        public ThingDef evalStuff; // only meaningful when evalMode is Material

        public MaterialLens Lens => new MaterialLens(evalMode, evalStuff);

        public bool IsEmpty => rules.Count == 0 && expressionRules.Count == 0;

        public Ruleset Clone()
        {
            var copy = new Ruleset { evalMode = evalMode, evalStuff = evalStuff };
            foreach (AttributeRule r in rules) copy.rules.Add(r.Clone());
            foreach (ExpressionRule e in expressionRules) copy.expressionRules.Add(e.Clone());
            return copy;
        }

        public void ApplyTo(ApparelPolicy policy)
        {
            AttributeCache.EnsureBuilt();
            ThingFilter filter = policy.filter;

            ApplyPerDefPass(filter, AttributeCache.Apparel, weapon: false);
            // Leave weapons untouched until the user authors a weapon rule, so foreign weapon config survives.
            bool weaponIntent = rules.Any(r => r.IsPerDef && r.weaponScope)
                || expressionRules.Any(e => e.weaponScope);
            if (AttributeCache.WeaponsActive && weaponIntent)
                ApplyPerDefPass(filter, AttributeCache.Weapons, weapon: true);

            ApplyMaterialPass(filter);
            ApplyRangePasses(filter);
            ApplySpecialFilterPass(filter);
        }

        private void ApplyPerDefPass(ThingFilter filter, List<ApparelAttributeInfo> universe, bool weapon)
        {
            MaterialLens lens = Lens;
            foreach (ApparelAttributeInfo info in universe)
            {
                bool allow = true;
                for (int i = 0; i < rules.Count; i++)
                {
                    AttributeRule rule = rules[i];
                    if (!rule.IsPerDef || rule.weaponScope != weapon || IsStuffCategoryForbid(rule)) continue;
                    if (rule.IsValid && rule.Disqualifies(info, lens)) { allow = false; break; }
                }
                if (allow)
                    for (int i = 0; i < expressionRules.Count; i++)
                    {
                        ExpressionRule er = expressionRules[i];
                        if (er.weaponScope != weapon) continue;
                        if (er.IsValid && er.Disqualifies(info, lens)) { allow = false; break; }
                    }
                filter.SetAllow(info.def, allow);
            }

            ApplyIngredientCategoryPass(filter, universe, weapon);
        }

        // A stuff category is one of several materials a piece can be made from, so forbidding it disqualifies a piece only when every one of its stuff categories is forbidden.
        private void ApplyIngredientCategoryPass(ThingFilter filter, List<ApparelAttributeInfo> universe, bool weapon)
        {
            Dictionary<string, List<AttributeRule>> forbidsByAttr = null;
            foreach (AttributeRule rule in rules)
                if (IsStuffCategoryForbid(rule) && rule.weaponScope == weapon)
                {
                    forbidsByAttr ??= new Dictionary<string, List<AttributeRule>>();
                    if (!forbidsByAttr.TryGetValue(rule.attrKey, out List<AttributeRule> list))
                        forbidsByAttr[rule.attrKey] = list = new List<AttributeRule>();
                    list.Add(rule);
                }
            if (forbidsByAttr == null) return;

            foreach (ApparelAttributeInfo info in universe)
                foreach (KeyValuePair<string, List<AttributeRule>> kv in forbidsByAttr)
                    if (AllStuffCategoriesForbidden(info, kv.Key, kv.Value))
                    {
                        filter.SetAllow(info.def, false);
                        break;
                    }
        }

        private static bool AllStuffCategoriesForbidden(ApparelAttributeInfo info, string attrKey, List<AttributeRule> forbids)
        {
            bool anyCat = false;
            foreach (string token in info.TokensFor(attrKey))
            {
                if (!AttributeCache.IsStuffCategory(token)) continue;
                anyCat = true;
                bool covered = false;
                foreach (AttributeRule rule in forbids)
                    if (rule.categoricalValue == token && rule.InScope(info)) { covered = true; break; }
                if (!covered) return false;
            }
            return anyCat;
        }

        private static bool IsStuffCategoryForbid(AttributeRule r)
            => r.kind == RuleAttributeKind.Categorical && r.polarity == RulePolarity.Forbid
               && AttributeCache.IsStuffCategory(r.categoricalValue);

        // Each special-filter rule allows or disallows its filter on the policy.
        private void ApplySpecialFilterPass(ThingFilter filter)
        {
            foreach (AttributeRule rule in rules)
                if (rule.kind == RuleAttributeKind.SpecialFilter && rule.specialFilter != null)
                    filter.SetAllow(rule.specialFilter, rule.polarity == RulePolarity.Require);
        }

        // Only toggles Material Filter's special filters, never apparel defs.
        private void ApplyMaterialPass(ThingFilter filter)
        {
            if (!AttributeCache.MaterialFilterActive) return;

            var forbidden = new HashSet<ThingDef>();
            var required = new HashSet<ThingDef>();
            bool any = false, hasRequire = false;
            foreach (AttributeRule rule in rules)
            {
                if (rule.kind != RuleAttributeKind.Material || !rule.IsValid) continue;
                any = true;
                if (rule.polarity == RulePolarity.Require) { required.Add(rule.materialStuff); hasRequire = true; }
                else forbidden.Add(rule.materialStuff);
            }
            if (!any) return;

            foreach (ThingDef material in AttributeCache.MaterialAttributes)
            {
                SpecialThingFilterDef sf = AttributeCache.MaterialFilterFor(material);
                if (sf == null) continue;
                bool allow = !forbidden.Contains(material) && (!hasRequire || required.Contains(material));
                filter.SetAllow(sf, allow);
            }
        }

        // Quality/HitPoints rules drive the vanilla range sliders; a facet is only managed when used.
        private void ApplyRangePasses(ThingFilter filter)
        {
            bool anyQuality = false;
            var qMin = QualityCategory.Awful;
            var qMax = QualityCategory.Legendary;
            bool anyHp = false;
            float hpMin = 0f, hpMax = 1f;

            foreach (AttributeRule rule in rules)
            {
                if (rule.kind == RuleAttributeKind.Quality)
                {
                    anyQuality = true;
                    if (rule.rangeBound == RangeBound.AtLeast)
                    {
                        if (rule.qualityValue > qMin) qMin = rule.qualityValue;
                    }
                    else if (rule.qualityValue < qMax) qMax = rule.qualityValue;
                }
                else if (rule.kind == RuleAttributeKind.HitPoints)
                {
                    anyHp = true;
                    float f = Mathf.Clamp01(rule.threshold);
                    if (rule.rangeBound == RangeBound.AtLeast) hpMin = Mathf.Max(hpMin, f);
                    else hpMax = Mathf.Min(hpMax, f);
                }
            }

            if (anyQuality) filter.AllowedQualityLevels = new QualityRange(qMin, qMax);
            if (anyHp) filter.AllowedHitPointsPercents = new FloatRange(hpMin, hpMax);
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref rules, "rules", LookMode.Deep);
            Scribe_Collections.Look(ref expressionRules, "expressionRules", LookMode.Deep);

            // Disagreeing with the field initializer on purpose: an absent node means a ruleset predating the lens, which must stay legacy.
            Scribe_Values.Look(ref evalMode, "evalMode", MaterialLensMode.Multiplier);

            // By defName, not Scribe_Defs: losing the lens material degrades to the multiplier rather than erroring.
            string lens = evalStuff?.defName;
            Scribe_Values.Look(ref lens, "evalStuff");

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                evalStuff = lens.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(lens);
                if (evalMode == MaterialLensMode.Material && evalStuff == null) evalMode = MaterialLensMode.Multiplier;

                if (rules == null) rules = new List<AttributeRule>();
                else rules.RemoveAll(r => r == null || !r.IsValid); // a def a rule points at can vanish when its mod is removed

                if (expressionRules == null) expressionRules = new List<ExpressionRule>();
                else expressionRules.RemoveAll(e => e == null || !e.IsValid);
            }
        }
    }
}
