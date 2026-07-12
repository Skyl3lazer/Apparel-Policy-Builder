using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ApparelPolicyBuilder
{
    // Def references held as defName strings so a document survives a referenced mod being toggled off then on.
    public class PortableRule : IExposable
    {
        public RulePolarity polarity = RulePolarity.Forbid;
        public string layerScope;
        public bool exceptUtility;
        public RuleAttributeKind kind = RuleAttributeKind.Numeric;
        public string stat;
        public NumericMode numericMode = NumericMode.Negative;
        public float threshold;
        public string attrKey;
        public string categoricalValue;
        public RangeBound rangeBound = RangeBound.AtLeast;
        public QualityCategory qualityValue = QualityCategory.Normal;
        public string materialStuff;
        public string specialFilter;

        public PortableRule() { }

        public static PortableRule From(AttributeRule r) => new PortableRule
        {
            polarity = r.polarity,
            layerScope = r.layerScope?.defName,
            exceptUtility = r.exceptUtility,
            kind = r.kind,
            stat = r.stat?.defName,
            numericMode = r.numericMode,
            threshold = r.threshold,
            attrKey = r.attrKey,
            categoricalValue = r.categoricalValue,
            rangeBound = r.rangeBound,
            qualityValue = r.qualityValue,
            materialStuff = r.materialStuff?.defName,
            specialFilter = r.specialFilter?.defName
        };

        // Fails when content the rule needs is absent from the current modlist, so the caller drops it.
        public bool TryResolve(out AttributeRule rule)
        {
            rule = null;
            ApparelLayerDef layer = null;
            if (!layerScope.NullOrEmpty())
            {
                layer = DefDatabase<ApparelLayerDef>.GetNamedSilentFail(layerScope);
                if (layer == null) return false;
            }

            var r = new AttributeRule
            {
                polarity = polarity,
                layerScope = layer,
                exceptUtility = exceptUtility,
                kind = kind,
                numericMode = numericMode,
                threshold = threshold,
                attrKey = attrKey,
                categoricalValue = categoricalValue,
                rangeBound = rangeBound,
                qualityValue = qualityValue
            };

            switch (kind)
            {
                case RuleAttributeKind.Numeric:
                    r.stat = DefDatabase<StatDef>.GetNamedSilentFail(stat);
                    if (r.stat == null) return false;
                    break;
                case RuleAttributeKind.Categorical:
                    if (attrKey.NullOrEmpty() || categoricalValue == null) return false;
                    if (AttributeCache.Options != null && AttributeCache.OptionFor(attrKey) == null) return false;
                    break;
                case RuleAttributeKind.Material:
                    r.materialStuff = DefDatabase<ThingDef>.GetNamedSilentFail(materialStuff);
                    if (r.materialStuff == null) return false;
                    break;
                case RuleAttributeKind.SpecialFilter:
                    r.specialFilter = DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(specialFilter);
                    if (r.specialFilter == null) return false;
                    break;
            }

            rule = r;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref polarity, "polarity", RulePolarity.Forbid);
            Scribe_Values.Look(ref layerScope, "layerScope");
            Scribe_Values.Look(ref exceptUtility, "exceptUtility", false);
            Scribe_Values.Look(ref kind, "kind", RuleAttributeKind.Numeric);
            Scribe_Values.Look(ref stat, "stat");
            Scribe_Values.Look(ref numericMode, "numericMode", NumericMode.Negative);
            Scribe_Values.Look(ref threshold, "threshold", 0f);
            Scribe_Values.Look(ref attrKey, "attrKey");
            Scribe_Values.Look(ref categoricalValue, "categoricalValue");
            Scribe_Values.Look(ref rangeBound, "rangeBound", RangeBound.AtLeast);
            Scribe_Values.Look(ref qualityValue, "qualityValue", QualityCategory.Normal);
            Scribe_Values.Look(ref materialStuff, "materialStuff");
            Scribe_Values.Look(ref specialFilter, "specialFilter");
        }
    }

    public class RuleDocument : IExposable
    {
        public string name;
        public List<PortableRule> rules = new List<PortableRule>();

        public RuleDocument() { }

        public static RuleDocument From(string name, Ruleset rs)
        {
            var doc = new RuleDocument { name = name };
            foreach (AttributeRule r in rs.rules) doc.rules.Add(PortableRule.From(r));
            return doc;
        }

        public Ruleset ToRuleset(out int skipped)
        {
            skipped = 0;
            var rs = new Ruleset();
            foreach (PortableRule pr in rules)
            {
                if (pr.TryResolve(out AttributeRule r)) rs.rules.Add(r);
                else skipped++;
            }
            return rs;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            Scribe_Collections.Look(ref rules, "rules", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && rules == null)
                rules = new List<PortableRule>();
        }
    }
}
