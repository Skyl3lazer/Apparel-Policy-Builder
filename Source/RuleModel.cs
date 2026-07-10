using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelAttributeFilter
{
    public enum RulePolarity : byte { Forbid, Require }

    public enum RuleAttributeKind : byte { Numeric, Covers }

    public enum NumericMode : byte { Positive, Negative, None, GreaterThan, LessThan, EqualTo }

    // A single rule: polarity + scope + attribute + condition. See ADR 0001.
    public class AttributeRule : IExposable
    {
        public RulePolarity polarity = RulePolarity.Forbid;
        public ApparelLayerDef layerScope;                  // null = Global
        public RuleAttributeKind kind = RuleAttributeKind.Numeric;
        public StatDef stat;                                // numeric attribute
        public NumericMode numericMode = NumericMode.Negative;
        public float threshold;                             // greater/less/equal only
        public BodyPartGroupDef coversGroup;                // Covers attribute

        public AttributeRule() { }

        public bool IsValid => kind == RuleAttributeKind.Covers ? coversGroup != null : stat != null;

        public bool NeedsThreshold =>
            kind == RuleAttributeKind.Numeric &&
            (numericMode == NumericMode.GreaterThan
             || numericMode == NumericMode.LessThan
             || numericMode == NumericMode.EqualTo);

        // Does the apparel satisfy this rule's condition, ignoring polarity and scope?
        public bool ConditionMatches(ApparelAttributeInfo info)
        {
            if (kind == RuleAttributeKind.Covers)
                return coversGroup != null && info.Covers.Contains(coversGroup);

            if (stat == null) return false;
            float v = info.GetStatValue(stat);
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

        // True if this rule disallows the apparel: Forbid removes matches, Require removes non-matches.
        public bool Disqualifies(ApparelAttributeInfo info)
        {
            if (!InScope(info)) return false;
            bool matches = ConditionMatches(info);
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
        }
    }

    // The set of rules for one apparel policy.
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

        // ADR 0001: reset to all-apparel-allowed, then remove every disqualified piece.
        public void ApplyTo(ApparelPolicy policy)
        {
            AttributeCache.EnsureBuilt();
            ThingFilter filter = policy.filter;
            foreach (ApparelAttributeInfo info in AttributeCache.Apparel)
            {
                bool allow = true;
                for (int i = 0; i < rules.Count; i++)
                {
                    AttributeRule rule = rules[i];
                    if (rule.IsValid && rule.Disqualifies(info)) { allow = false; break; }
                }
                filter.SetAllow(info.def, allow);
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
