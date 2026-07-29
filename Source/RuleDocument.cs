using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ApparelPolicyBuilder
{
    // Def references held as defName strings so a document survives a referenced mod being toggled off then on.
    public class PortableRule : RuleScalars, IExposable
    {
        public string layerScope;
        public string stat;
        public string materialStuff;
        public string specialFilter;

        public PortableRule() { }

        public static PortableRule From(AttributeRule r)
        {
            var pr = new PortableRule
            {
                layerScope = r.layerScope?.defName,
                stat = r.stat?.defName,
                materialStuff = r.materialStuff?.defName,
                specialFilter = r.specialFilter?.defName
            };
            pr.CopyScalarsFrom(r);
            return pr;
        }

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

            var r = new AttributeRule { layerScope = layer };
            r.CopyScalarsFrom(this);

            switch (kind)
            {
                case RuleAttributeKind.Numeric:
                    r.stat = DefDatabase<StatDef>.GetNamedSilentFail(stat);
                    if (r.stat == null) return false;
                    break;
                case RuleAttributeKind.Categorical:
                {
                    if (attrKey.NullOrEmpty() || categoricalValue == null) return false;
                    // A weapon categorical rule is dormant, not gone, when Auto Arm is absent - keep it inert.
                    bool universeActive = !weaponScope || AttributeCache.WeaponsActive;
                    if (universeActive && AttributeCache.Options != null && AttributeCache.OptionFor(attrKey, weaponScope) == null) return false;
                    break;
                }
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
            ExposeScalars();
            Scribe_Values.Look(ref layerScope, "layerScope");
            Scribe_Values.Look(ref stat, "stat");
            Scribe_Values.Look(ref materialStuff, "materialStuff");
            Scribe_Values.Look(ref specialFilter, "specialFilter");
        }
    }

    public enum ExprNodeKind : byte { Condition, Group, Not }

    // The only def reference in the tree is a Condition's stat, held by defName so a document survives a referenced mod being toggled off then on.
    public class PortableExpr : IExposable
    {
        public ExprNodeKind nodeKind;
        public bool any;
        public RuleAttributeKind kind = RuleAttributeKind.Numeric;
        public NumericMode numericMode = NumericMode.Positive;
        public float threshold;
        public string attrKey;
        public string categoricalValue;
        public string stat;
        public List<PortableExpr> children = new List<PortableExpr>();

        public static PortableExpr From(Expression e)
        {
            switch (e)
            {
                case ConditionExpr ce:
                    Condition c = ce.condition;
                    return new PortableExpr
                    {
                        nodeKind = ExprNodeKind.Condition,
                        kind = c.kind, numericMode = c.numericMode, threshold = c.threshold,
                        attrKey = c.attrKey, categoricalValue = c.categoricalValue, stat = c.stat?.defName
                    };
                case NotExpr ne:
                    var not = new PortableExpr { nodeKind = ExprNodeKind.Not };
                    if (ne.child != null) not.children.Add(From(ne.child));
                    return not;
                case GroupExpr ge:
                    var g = new PortableExpr { nodeKind = ExprNodeKind.Group, any = ge.any };
                    foreach (Expression child in ge.children)
                        if (child != null) g.children.Add(From(child));
                    return g;
                default:
                    return null;
            }
        }

        // Fails when any def the tree needs is absent, so the caller drops the whole Expression Rule.
        public bool TryResolve(out Expression expr, bool weapon)
        {
            expr = null;
            switch (nodeKind)
            {
                case ExprNodeKind.Condition:
                    var cond = new Condition
                    {
                        kind = kind, numericMode = numericMode, threshold = threshold,
                        attrKey = attrKey, categoricalValue = categoricalValue
                    };
                    if (kind == RuleAttributeKind.Categorical)
                    {
                        if (attrKey.NullOrEmpty() || categoricalValue == null) return false;
                        // A weapon leaf is dormant, not gone, when Auto Arm is absent - keep it inert.
                        bool universeActive = !weapon || AttributeCache.WeaponsActive;
                        if (universeActive && AttributeCache.Options != null && AttributeCache.OptionFor(attrKey, weapon) == null) return false;
                    }
                    else
                    {
                        cond.stat = DefDatabase<StatDef>.GetNamedSilentFail(stat);
                        if (cond.stat == null) return false;
                    }
                    expr = new ConditionExpr { condition = cond };
                    return true;
                case ExprNodeKind.Not:
                    if (children.Count == 0 || !children[0].TryResolve(out Expression childExpr, weapon)) return false;
                    expr = new NotExpr { child = childExpr };
                    return true;
                default:
                    if (children.Count == 0) return false;
                    var g = new GroupExpr { any = any };
                    foreach (PortableExpr pc in children)
                    {
                        if (!pc.TryResolve(out Expression ce, weapon)) return false;
                        g.children.Add(ce);
                    }
                    expr = g;
                    return true;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref nodeKind, "nodeKind", ExprNodeKind.Condition);
            Scribe_Values.Look(ref any, "any", false);
            Scribe_Values.Look(ref kind, "kind", RuleAttributeKind.Numeric);
            Scribe_Values.Look(ref numericMode, "numericMode", NumericMode.Positive);
            Scribe_Values.Look(ref threshold, "threshold", 0f);
            Scribe_Values.Look(ref attrKey, "attrKey");
            Scribe_Values.Look(ref categoricalValue, "categoricalValue");
            Scribe_Values.Look(ref stat, "stat");
            Scribe_Collections.Look(ref children, "children", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && children == null)
                children = new List<PortableExpr>();
        }
    }

    public class PortableExpressionRule : IExposable
    {
        public string layerScope;
        public bool exceptUtility;
        public bool utilityOnly;
        public bool weaponScope;
        public PortableExpr root;

        public static PortableExpressionRule From(ExpressionRule e) => new PortableExpressionRule
        {
            layerScope = e.layerScope?.defName,
            exceptUtility = e.exceptUtility,
            utilityOnly = e.utilityOnly,
            weaponScope = e.weaponScope,
            root = e.root != null ? PortableExpr.From(e.root) : null
        };

        public bool TryResolve(out ExpressionRule rule)
        {
            rule = null;
            ApparelLayerDef layer = null;
            if (!layerScope.NullOrEmpty())
            {
                layer = DefDatabase<ApparelLayerDef>.GetNamedSilentFail(layerScope);
                if (layer == null) return false;
            }
            if (root == null || !root.TryResolve(out Expression expr, weaponScope)) return false;

            rule = new ExpressionRule { layerScope = layer, exceptUtility = exceptUtility, utilityOnly = utilityOnly, weaponScope = weaponScope, root = expr };
            return rule.IsValid;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref layerScope, "layerScope");
            Scribe_Values.Look(ref exceptUtility, "exceptUtility", false);
            Scribe_Values.Look(ref utilityOnly, "utilityOnly", false);
            Scribe_Values.Look(ref weaponScope, "weaponScope", false);
            Scribe_Deep.Look(ref root, "root");
        }
    }

    public class RuleDocument : IExposable
    {
        public string name;
        public string evalStuff;
        public List<PortableRule> rules = new List<PortableRule>();
        public List<PortableExpressionRule> expressionRules = new List<PortableExpressionRule>();

        public RuleDocument() { }

        public static RuleDocument From(string name, Ruleset rs)
        {
            var doc = new RuleDocument { name = name, evalStuff = rs.evalStuff?.defName };
            foreach (AttributeRule r in rs.rules) doc.rules.Add(PortableRule.From(r));
            foreach (ExpressionRule e in rs.expressionRules) doc.expressionRules.Add(PortableExpressionRule.From(e));
            return doc;
        }

        public Ruleset ToRuleset(out int skipped)
        {
            skipped = 0;
            // A missing lens material falls back to the multiplier; unlike a rule, it costs the document nothing.
            var rs = new Ruleset
            {
                evalStuff = evalStuff.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(evalStuff)
            };
            foreach (PortableRule pr in rules)
            {
                if (pr.TryResolve(out AttributeRule r)) rs.rules.Add(r);
                else skipped++;
            }
            foreach (PortableExpressionRule pe in expressionRules)
            {
                if (pe.TryResolve(out ExpressionRule e)) rs.expressionRules.Add(e);
                else skipped++;
            }
            return rs;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref evalStuff, "evalStuff");
            Scribe_Collections.Look(ref rules, "rules", LookMode.Deep);
            Scribe_Collections.Look(ref expressionRules, "expressionRules", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (rules == null) rules = new List<PortableRule>();
                if (expressionRules == null) expressionRules = new List<PortableExpressionRule>();
            }
        }
    }
}
