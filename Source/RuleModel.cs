using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelAttributeFilter
{
    public enum RulePolarity : byte { Forbid, Require }

    public enum RuleAttributeKind : byte { Numeric, Covers, Material }

    public enum NumericMode : byte { Positive, Negative, None, GreaterThan, LessThan, EqualTo }

    public class AttributeRule : IExposable
    {
        public RulePolarity polarity = RulePolarity.Forbid;
        public ApparelLayerDef layerScope; // null = Global
        public RuleAttributeKind kind = RuleAttributeKind.Numeric;
        public StatDef stat;
        public NumericMode numericMode = NumericMode.Negative;
        public float threshold;
        public BodyPartGroupDef coversGroup;
        public ThingDef materialStuff;

        public AttributeRule() { }

        public bool IsValid
        {
            get
            {
                switch (kind)
                {
                    case RuleAttributeKind.Covers: return coversGroup != null;
                    case RuleAttributeKind.Material: return materialStuff != null;
                    default: return stat != null;
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
            if (kind == RuleAttributeKind.Covers)
                return coversGroup != null && info.Covers.Contains(coversGroup);

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
            Scribe_Defs.Look(ref coversGroup, "coversGroup");
            Scribe_Defs.Look(ref materialStuff, "materialStuff");
        }
    }

    public class Ruleset : IExposable
    {
        public List<AttributeRule> rules = new List<AttributeRule>();

        public bool IsEmpty => rules.Count == 0;

        public Ruleset Clone()
        {
            var copy = new Ruleset();
            foreach (var r in rules) copy.rules.Add(r.Clone());
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
                    if (rule.kind == RuleAttributeKind.Material) continue; // handled in ApplyMaterialPass
                    if (rule.IsValid && rule.Disqualifies(info, evalStuff)) { allow = false; break; }
                }
                filter.SetAllow(info.def, allow);
            }

            ApplyMaterialPass(filter);
        }

        // Only toggles Material Filter's special filters, never apparel defs.
        private void ApplyMaterialPass(ThingFilter filter)
        {
            if (!AttributeCache.MaterialFilterActive) return;

            var forbidden = new HashSet<ThingDef>();
            var required = new HashSet<ThingDef>();
            bool hasRequire = false;
            foreach (AttributeRule rule in rules)
            {
                if (rule.kind != RuleAttributeKind.Material || !rule.IsValid) continue;
                if (rule.polarity == RulePolarity.Require) { required.Add(rule.materialStuff); hasRequire = true; }
                else forbidden.Add(rule.materialStuff);
            }

            foreach (ThingDef material in AttributeCache.MaterialAttributes)
            {
                SpecialThingFilterDef sf = AttributeCache.MaterialFilterFor(material);
                if (sf == null) continue;
                bool allow = !forbidden.Contains(material) && (!hasRequire || required.Contains(material));
                filter.SetAllow(sf, allow);
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref rules, "rules", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && rules == null)
                rules = new List<AttributeRule>();
        }
    }
}
