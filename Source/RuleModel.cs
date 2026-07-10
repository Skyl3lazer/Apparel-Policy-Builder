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

    public class AttributeRule : IExposable
    {
        public RulePolarity polarity = RulePolarity.Forbid;
        public ApparelLayerDef layerScope; // null = Global; per-def kinds only
        public RuleAttributeKind kind = RuleAttributeKind.Numeric;

        public StatDef stat;
        public NumericMode numericMode = NumericMode.Negative;
        public float threshold; // also the HitPoints fraction for that facet

        public string attrKey;
        public string categoricalValue;

        public RangeBound rangeBound = RangeBound.AtLeast;
        public QualityCategory qualityValue = QualityCategory.Normal;

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

        public bool ConditionMatches(ApparelAttributeInfo info, ThingDef evalStuff)
        {
            if (kind == RuleAttributeKind.Categorical)
                return info.HasCategorical(attrKey, categoricalValue);

            if (stat == null) return false;
            float v = info.GetStatValue(stat, evalStuff);
            switch (numericMode)
            {
                case NumericMode.Positive: return v > 0f;
                case NumericMode.Negative: return v < 0f;
                case NumericMode.None: return v == 0f;
                case NumericMode.GreaterThan: return v > threshold;
                case NumericMode.LessThan: return v < threshold;
                case NumericMode.EqualTo: return Mathf.Approximately(v, threshold);
                default: return false;
            }
        }

        public bool InScope(ApparelAttributeInfo info)
            => layerScope == null || info.Layers.Contains(layerScope);

        public bool Disqualifies(ApparelAttributeInfo info, ThingDef evalStuff)
        {
            if (!InScope(info)) return false;
            bool matches = ConditionMatches(info, evalStuff);
            return polarity == RulePolarity.Forbid ? matches : !matches;
        }

        public AttributeRule Clone() => (AttributeRule)MemberwiseClone();

        public void ExposeData()
        {
            Scribe_Values.Look(ref polarity, "polarity", RulePolarity.Forbid);
            Scribe_Defs.Look(ref layerScope, "layerScope");
            Scribe_Values.Look(ref kind, "kind", RuleAttributeKind.Numeric);
            Scribe_Defs.Look(ref stat, "stat");
            Scribe_Values.Look(ref numericMode, "numericMode", NumericMode.Negative);
            Scribe_Values.Look(ref threshold, "threshold", 0f);
            Scribe_Values.Look(ref attrKey, "attrKey");
            Scribe_Values.Look(ref categoricalValue, "categoricalValue");
            Scribe_Values.Look(ref rangeBound, "rangeBound", RangeBound.AtLeast);
            Scribe_Values.Look(ref qualityValue, "qualityValue", QualityCategory.Normal);
            Scribe_Defs.Look(ref materialStuff, "materialStuff");
            Scribe_Defs.Look(ref specialFilter, "specialFilter");
        }
    }

    public class Ruleset : IExposable
    {
        public List<AttributeRule> rules = new List<AttributeRule>();

        public bool IsEmpty => rules.Count == 0;

        public Ruleset Clone()
        {
            var copy = new Ruleset();
            foreach (AttributeRule r in rules) copy.rules.Add(r.Clone());
            return copy;
        }

        public void ApplyTo(ApparelPolicy policy, ThingDef evalStuff)
        {
            AttributeCache.EnsureBuilt();
            ThingFilter filter = policy.filter;

            foreach (ApparelAttributeInfo info in AttributeCache.Apparel)
            {
                bool allow = true;
                for (int i = 0; i < rules.Count; i++)
                {
                    AttributeRule rule = rules[i];
                    if (!rule.IsPerDef) continue;
                    if (rule.IsValid && rule.Disqualifies(info, evalStuff)) { allow = false; break; }
                }
                filter.SetAllow(info.def, allow);
            }

            ApplyMaterialPass(filter);
            ApplyRangePasses(filter);
            ApplySpecialFilterPass(filter);
        }

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
            if (Scribe.mode == LoadSaveMode.LoadingVars && rules == null)
                rules = new List<AttributeRule>();
        }
    }
}
