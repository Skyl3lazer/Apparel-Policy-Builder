using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelPolicyBuilder
{
    public enum ExprKind : byte { And, Or, Not }

    internal static class ConditionEval
    {
        public static bool NumericMatches(NumericMode mode, float v, float threshold)
        {
            switch (mode)
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
    }

    // A per-def predicate with no polarity and no scope: the leaf of an Expression.
    public class Condition : IExposable
    {
        public RuleAttributeKind kind = RuleAttributeKind.Numeric; // Numeric or Categorical only
        public NumericMode numericMode = NumericMode.Positive;
        public float threshold;
        public string attrKey;
        public string categoricalValue;
        public StatDef stat;

        public bool IsValid => kind == RuleAttributeKind.Categorical
            ? !attrKey.NullOrEmpty() && categoricalValue != null
            : stat != null;

        public bool NeedsThreshold =>
            kind == RuleAttributeKind.Numeric &&
            (numericMode == NumericMode.GreaterThan
             || numericMode == NumericMode.LessThan
             || numericMode == NumericMode.EqualTo);

        public bool Matches(ApparelAttributeInfo info, ThingDef evalStuff)
        {
            if (kind == RuleAttributeKind.Categorical)
                return info.HasCategorical(attrKey, categoricalValue);
            if (stat == null) return false;
            return ConditionEval.NumericMatches(numericMode, info.GetStatValue(stat, evalStuff), threshold);
        }

        public Condition Clone() => (Condition)MemberwiseClone();

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind", RuleAttributeKind.Numeric);
            Scribe_Values.Look(ref numericMode, "numericMode", NumericMode.Positive);
            Scribe_Values.Look(ref threshold, "threshold", 0f);
            Scribe_Values.Look(ref attrKey, "attrKey");
            Scribe_Values.Look(ref categoricalValue, "categoricalValue");
            Scribe_Defs.Look(ref stat, "stat");
        }
    }

    public abstract class Expression : IExposable
    {
        public abstract bool Evaluate(ApparelAttributeInfo info, ThingDef evalStuff);
        public abstract bool IsValid { get; }
        public abstract Expression Clone();
        public abstract void ExposeData();
    }

    public class ConditionExpr : Expression
    {
        public Condition condition = new Condition();

        public override bool Evaluate(ApparelAttributeInfo info, ThingDef evalStuff)
            => condition != null && condition.Matches(info, evalStuff);

        public override bool IsValid => condition != null && condition.IsValid;

        public override Expression Clone() => new ConditionExpr { condition = condition?.Clone() };

        public override void ExposeData() => Scribe_Deep.Look(ref condition, "condition");
    }

    public class NotExpr : Expression
    {
        public Expression child;

        public override bool Evaluate(ApparelAttributeInfo info, ThingDef evalStuff)
            => child != null && !child.Evaluate(info, evalStuff);

        public override bool IsValid => child != null && child.IsValid;

        public override Expression Clone() => new NotExpr { child = child?.Clone() };

        public override void ExposeData() => Scribe_Deep.Look(ref child, "child");
    }

    // A container combining its children with AND (all of) or OR (any of); the flag makes toggling a one-line flip.
    public class GroupExpr : Expression
    {
        public bool any; // false = AND (all of), true = OR (any of)
        public List<Expression> children = new List<Expression>();

        public override bool Evaluate(ApparelAttributeInfo info, ThingDef evalStuff)
        {
            if (any)
            {
                foreach (Expression c in children)
                    if (c != null && c.Evaluate(info, evalStuff)) return true;
                return false;
            }
            foreach (Expression c in children)
                if (c == null || !c.Evaluate(info, evalStuff)) return false;
            return true;
        }

        public override bool IsValid => children.Count > 0 && children.All(c => c != null && c.IsValid);

        public override Expression Clone()
        {
            var copy = new GroupExpr { any = any };
            foreach (Expression c in children) copy.children.Add(c?.Clone());
            return copy;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref any, "any", false);
            Scribe_Collections.Look(ref children, "children", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && children == null)
                children = new List<Expression>();
        }
    }

    // A clause pairing one scope with one Expression; disqualifies an in-scope piece whose Expression is false.
    public class ExpressionRule : IExposable
    {
        public ApparelLayerDef layerScope; // null = Global
        public bool exceptUtility;
        public bool utilityOnly;
        public Expression root;

        public bool IsValid => root != null && root.IsValid;

        public bool InScope(ApparelAttributeInfo info)
            => AttributeRule.IsInScope(layerScope, exceptUtility, utilityOnly, info);

        public bool Disqualifies(ApparelAttributeInfo info, ThingDef evalStuff)
            => InScope(info) && !root.Evaluate(info, evalStuff);

        public ExpressionRule Clone() => new ExpressionRule
        {
            layerScope = layerScope,
            exceptUtility = exceptUtility,
            utilityOnly = utilityOnly,
            root = root?.Clone()
        };

        public void ExposeData()
        {
            Scribe_Defs.Look(ref layerScope, "layerScope");
            Scribe_Values.Look(ref exceptUtility, "exceptUtility", false);
            Scribe_Values.Look(ref utilityOnly, "utilityOnly", false);
            Scribe_Deep.Look(ref root, "root");
        }
    }
}
