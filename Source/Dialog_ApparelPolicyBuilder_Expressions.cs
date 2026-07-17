using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelPolicyBuilder
{
    public partial class Dialog_ApparelPolicyBuilder
    {
        // Set while the user is picking an attribute from the left panel to drop into a chosen Expression node.
        private Action<Expression> pendingInsert;
        // A structural edit from an inline button, deferred until after the tree draw to avoid mutating mid-iteration.
        private Action pendingTreeOp;
        private readonly Dictionary<Condition, string> condBuffers = new Dictionary<Condition, string>();

        private static bool IsPerDefOption(AttributeOption opt)
            => opt.kind == RuleAttributeKind.Numeric || opt.kind == RuleAttributeKind.Categorical;

        private void OnAttributeClicked(AttributeOption opt)
        {
            if (pendingInsert != null)
            {
                if (!IsPerDefOption(opt)) return;
                pendingInsert(new ConditionExpr { condition = ConditionFromOption(opt) });
                pendingInsert = null;
                return;
            }
            AddRule(opt);
        }

        private static Condition ConditionFromOption(AttributeOption opt)
        {
            var c = new Condition { kind = opt.kind };
            if (opt.kind == RuleAttributeKind.Numeric) c.stat = opt.stat;
            else { c.attrKey = opt.key; c.categoricalValue = opt.values.FirstOrDefault()?.token; }
            return c;
        }

        // ---- Layout measurement ----

        private float ExpressionsSectionHeight()
        {
            float h = HeaderHeight;
            foreach (ExpressionRule er in working.expressionRules)
                h += RuleRowHeight + MeasureExpr(er.root) * RuleRowHeight + 6f;
            return h;
        }

        private static int MeasureExpr(Expression e)
        {
            switch (e)
            {
                case null: return 0;
                case ConditionExpr _: return 1;
                case NotExpr n: return 1 + MeasureExpr(n.child);
                case GroupExpr g:
                    int sum = 1;
                    if (g.children.Count == 0) sum += 1;
                    foreach (Expression c in g.children) sum += MeasureExpr(c);
                    return sum;
                default: return 1;
            }
        }

        // ---- Section + cards ----

        private void DrawExpressionsSection(float width, ref float y)
        {
            var header = new Rect(0f, y, width, HeaderHeight);
            Color prev = GUI.color;
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(new Rect(header.x + 4f, header.y, width - 120f, header.height), "APB.Expressions".Translate());
            GUI.color = prev;
            var newRect = new Rect(width - 112f, header.y + 1f, 110f, HeaderHeight - 3f);
            TooltipHandler.TipRegionByKey(newRect, "APB.NewExpressionTip");
            if (Widgets.ButtonText(newRect, "APB.NewExpression".Translate()))
                working.expressionRules.Add(new ExpressionRule { root = new GroupExpr { any = true } });
            y += HeaderHeight;

            foreach (ExpressionRule er in working.expressionRules)
            {
                DrawExpressionCard(er, width, ref y);
                y += 6f;
            }
        }

        private void DrawExpressionCard(ExpressionRule er, float width, ref float y)
        {
            var headerRow = new Rect(0f, y, width, RuleRowHeight);
            Widgets.DrawLightHighlight(headerRow);
            var scopeBtn = new Rect(headerRow.x + 4f, headerRow.y + 2f, 170f, RuleRowHeight - 4f);
            TooltipHandler.TipRegionByKey(scopeBtn, "APB.ExprScopeTip");
            if (Widgets.ButtonText(scopeBtn, ExprScopeLabel(er)))
                OpenExprScopeMenu(er);
            var delRect = new Rect(headerRow.xMax - 26f, headerRow.y + (RuleRowHeight - 24f) / 2f, 24f, 24f);
            if (Widgets.ButtonImage(delRect, TexButton.Delete))
                pendingTreeOp = () => working.expressionRules.Remove(er);
            y += RuleRowHeight;

            DrawExprNode(er.root, null, 1, width, ref y);
        }

        // ---- Recursive node drawing. deleteSelf == null means this node is the (undeletable) root. ----

        private void DrawExprNode(Expression node, Action deleteSelf, int depth, float width, ref float y)
        {
            var band = new Rect(0f, y, width, RuleRowHeight);
            if (Mouse.IsOver(band)) Widgets.DrawHighlight(band);
            float indent = 12f + depth * 16f;
            var row = new Rect(indent, y, width - indent - 2f, RuleRowHeight);

            switch (node)
            {
                case GroupExpr g:
                    DrawGroupRow(g, row, deleteSelf);
                    y += RuleRowHeight;
                    if (g.children.Count == 0)
                    {
                        DrawFaded(new Rect(indent + 18f, y, width - indent - 22f, RuleRowHeight), "APB.EmptyGroup".Translate(), TextAnchor.MiddleLeft);
                        y += RuleRowHeight;
                    }
                    else
                        foreach (Expression child in g.children)
                        {
                            Expression captured = child;
                            DrawExprNode(child, () => g.children.Remove(captured), depth + 1, width, ref y);
                        }
                    break;

                case NotExpr n:
                    DrawNotRow(n, row, deleteSelf);
                    y += RuleRowHeight;
                    if (n.child != null)
                    {
                        NotExpr capturedNot = n;
                        DrawExprNode(n.child, () => capturedNot.child = null, depth + 1, width, ref y);
                    }
                    break;

                case ConditionExpr ce:
                    DrawConditionRow(ce.condition, row, deleteSelf);
                    y += RuleRowHeight;
                    break;
            }
        }

        private void DrawGroupRow(GroupExpr g, Rect row, Action deleteSelf)
        {
            var opRect = new Rect(row.x, row.y + 2f, 74f, row.height - 4f);
            if (Widgets.ButtonText(opRect, (g.any ? "APB.AnyOf" : "APB.AllOf").Translate()))
                g.any = !g.any;
            var addRect = new Rect(opRect.xMax + 4f, row.y + 2f, 28f, row.height - 4f);
            if (Widgets.ButtonText(addRect, "+"))
                OpenAddMenu(expr => g.children.Add(expr));
            DrawNodeDelete(row, deleteSelf);
        }

        private void DrawNotRow(NotExpr n, Rect row, Action deleteSelf)
        {
            RowLabel(new Rect(row.x, row.y, 40f, row.height), "APB.Not".Translate());
            if (n.child == null)
            {
                var addRect = new Rect(row.x + 44f, row.y + 2f, 28f, row.height - 4f);
                if (Widgets.ButtonText(addRect, "+"))
                    OpenAddMenu(expr => n.child = expr);
            }
            DrawNodeDelete(row, deleteSelf);
        }

        private void DrawConditionRow(Condition c, Rect row, Action deleteSelf)
        {
            const float gap = 4f, valueCol = 170f, fieldW = 48f;
            float rightEdge = row.xMax;
            if (deleteSelf != null)
            {
                var delRect = new Rect(row.xMax - 24f, row.y + (row.height - 24f) / 2f, 24f, 24f);
                if (Widgets.ButtonImage(delRect, TexButton.Delete)) pendingTreeOp = deleteSelf;
                rightEdge = delRect.x - gap;
            }

            float valueX = rightEdge - valueCol;
            var nameRect = new Rect(row.x, row.y, Mathf.Max(valueX - gap - row.x, 40f), row.height);

            if (c.kind == RuleAttributeKind.Categorical)
            {
                RowLabel(nameRect, AttributeCache.OptionFor(c.attrKey)?.label ?? c.attrKey);
                if (Widgets.ButtonText(new Rect(valueX, row.y, valueCol, row.height), CondCategoricalValueLabel(c)))
                    OpenCondCategoricalMenu(c);
            }
            else
            {
                RowLabel(nameRect, c.stat?.LabelCap ?? "?");
                if (c.stat != null && !c.stat.description.NullOrEmpty())
                    TooltipHandler.TipRegion(nameRect, c.stat.description);
                float modeW = valueCol - gap - fieldW;
                if (Widgets.ButtonText(new Rect(valueX, row.y, modeW, row.height),
                        ("APB.Mode." + c.numericMode).Translate().CapitalizeFirst()))
                    OpenCondModeMenu(c);
                if (c.NeedsThreshold)
                    DrawCondThreshold(new Rect(valueX + modeW + gap, row.y, fieldW, row.height), c);
            }
        }

        private void DrawNodeDelete(Rect row, Action deleteSelf)
        {
            if (deleteSelf == null) return;
            var delRect = new Rect(row.xMax - 24f, row.y + (row.height - 24f) / 2f, 24f, 24f);
            if (Widgets.ButtonImage(delRect, TexButton.Delete)) pendingTreeOp = deleteSelf;
        }

        private void DrawCondThreshold(Rect rect, Condition c)
        {
            if (!condBuffers.TryGetValue(c, out string buffer)) buffer = c.threshold.ToString("0.###");
            Widgets.TextFieldNumeric(rect, ref c.threshold, ref buffer, -1e9f, 1e9f);
            condBuffers[c] = buffer;
        }

        // ---- Menus ----

        private void OpenAddMenu(Action<Expression> insert)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("APB.AddCondition".Translate(), () => pendingInsert = insert),
                new FloatMenuOption("APB.AddAllGroup".Translate(), () => insert(new GroupExpr { any = false })),
                new FloatMenuOption("APB.AddAnyGroup".Translate(), () => insert(new GroupExpr { any = true })),
                new FloatMenuOption("APB.AddNot".Translate(), () => insert(new NotExpr()))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenCondModeMenu(Condition c)
        {
            var options = new List<FloatMenuOption>();
            foreach (NumericMode mode in Enum.GetValues(typeof(NumericMode)))
            {
                NumericMode captured = mode;
                options.Add(new FloatMenuOption(("APB.Mode." + mode).Translate(), () => c.numericMode = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenCondCategoricalMenu(Condition c)
        {
            AttributeOption opt = AttributeCache.OptionFor(c.attrKey);
            if (opt?.values == null) return;
            var options = new List<FloatMenuOption>();
            foreach (CategoricalValue cv in opt.values)
            {
                CategoricalValue captured = cv;
                options.Add(new FloatMenuOption(captured.label, () => c.categoricalValue = captured.token));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string CondCategoricalValueLabel(Condition c)
        {
            AttributeOption opt = AttributeCache.OptionFor(c.attrKey);
            CategoricalValue v = opt?.values.FirstOrDefault(cv => cv.token == c.categoricalValue);
            return v?.label ?? c.categoricalValue ?? "APB.Pick".Translate();
        }

        private static string ExprScopeLabel(ExpressionRule er)
            => er.utilityOnly ? "APB.UtilityOnly".Translate().ToString()
               : er.exceptUtility ? "APB.GlobalExceptUtility".Translate().ToString()
               : er.layerScope != null ? er.layerScope.LabelCap.ToString()
               : "APB.Global".Translate().ToString();

        private void OpenExprScopeMenu(ExpressionRule er)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("APB.Global".Translate(), () => { er.layerScope = null; er.exceptUtility = false; er.utilityOnly = false; }),
                new FloatMenuOption("APB.GlobalExceptUtility".Translate(), () => { er.layerScope = null; er.exceptUtility = true; er.utilityOnly = false; }),
                new FloatMenuOption("APB.UtilityOnly".Translate(), () => { er.layerScope = null; er.exceptUtility = false; er.utilityOnly = true; })
            };
            foreach (ApparelLayerDef layer in AttributeCache.Layers)
            {
                ApparelLayerDef captured = layer;
                options.Add(new FloatMenuOption(captured.LabelCap,
                    () => { er.layerScope = captured; er.exceptUtility = false; er.utilityOnly = false; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
