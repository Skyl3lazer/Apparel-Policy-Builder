using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelPolicyBuilder
{
    public class Dialog_ApparelPolicyBuilder : Window
    {
        private const float TitleHeight = 34f;
        private const float ButtonRowHeight = 40f;
        private const float LeftPanelWidth = 320f;
        private const float PanelGap = 12f;
        private const float RowHeight = 24f;
        private const float HeaderHeight = 26f;
        private const float RuleRowHeight = 32f;
        private const float ToolbarHeight = 32f;

        // Static so a ruleset can be copied between policy windows.
        private static Ruleset clipboard;

        private readonly ApparelPolicy policy;
        private Ruleset working;
        private readonly List<OptionGroup> groups;
        private readonly HashSet<string> collapsedGroups = new HashSet<string>();
        private readonly HashSet<ApparelLayerDef> collapsedScopes = new HashSet<ApparelLayerDef>(); // holds null for Global

        private string searchText = "";
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private ThingDef evalStuff; // null = evaluate stuff-powered stats by the material multiplier
        private readonly Dictionary<AttributeRule, string> valueBuffers = new Dictionary<AttributeRule, string>();

        public override Vector2 InitialSize => new Vector2(940f, 590f);

        public Dialog_ApparelPolicyBuilder(ApparelPolicy policy)
        {
            this.policy = policy;
            AttributeCache.EnsureBuilt();

            Ruleset stored = GameComponent_ApparelPolicyBuilder.Instance?.GetRuleset(policy);
            working = stored != null ? stored.Clone() : new Ruleset();

            groups = AttributeCache.Options
                .GroupBy(GroupKey)
                .Select(g => new OptionGroup
                {
                    isFacet = g.Key == null,
                    label = GroupLabel(g.Key),
                    order = g.Key == null ? int.MaxValue : g.Min(o => o.category?.displayOrder ?? int.MaxValue),
                    options = g.OrderBy(o => o.order).ThenBy(OptionLabel).ToList()
                })
                .OrderBy(gr => gr.isFacet).ThenBy(gr => gr.label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (OptionGroup g in groups.Skip(1))
                collapsedGroups.Add(g.label);

            doCloseX = true;
            draggable = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width, TitleHeight);
            using (new TextBlock(GameFont.Medium))
                Widgets.Label(titleRect, "APB.Title".Translate());
            Text.Font = GameFont.Small;

            float contentTop = titleRect.yMax + 4f;
            float contentBottom = inRect.yMax - ButtonRowHeight;
            var leftRect = new Rect(inRect.x, contentTop, LeftPanelWidth, contentBottom - contentTop);

            float rightX = leftRect.xMax + PanelGap;
            float rightWidth = inRect.width - LeftPanelWidth - PanelGap;
            var toolbarRect = new Rect(rightX, contentTop, rightWidth, ToolbarHeight);
            var rightRect = new Rect(rightX, toolbarRect.yMax + 4f, rightWidth, contentBottom - toolbarRect.yMax - 4f);

            DrawLeftPanel(leftRect);
            DrawRightToolbar(toolbarRect);
            DrawRightPanel(rightRect);
            DrawButtonRow(new Rect(inRect.x, contentBottom, inRect.width, ButtonRowHeight));
        }

        // ---- Left: attribute picker ----

        private void DrawLeftPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(6f);

            var searchRect = new Rect(inner.x, inner.y, inner.width, 26f);
            searchText = Widgets.TextField(searchRect, searchText);
            if (searchText.NullOrEmpty())
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Widgets.Label(new Rect(searchRect.x + 4f, searchRect.y, searchRect.width - 8f, searchRect.height),
                    "APB.SearchHint".Translate());
                GUI.color = prev;
            }

            var listRect = new Rect(inner.x, searchRect.yMax + 6f, inner.width, inner.yMax - searchRect.yMax - 6f);
            bool searching = !searchText.NullOrEmpty();

            float viewHeight = 0f;
            foreach (OptionGroup g in groups)
            {
                int visible = g.options.Count(OptionMatches);
                if (visible == 0) continue;
                viewHeight += HeaderHeight;
                if (searching || !collapsedGroups.Contains(g.label))
                    viewHeight += visible * RowHeight;
            }

            var viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(viewHeight, listRect.height));
            Widgets.BeginScrollView(listRect, ref leftScroll, viewRect);
            float y = 0f;

            foreach (OptionGroup g in groups)
            {
                List<AttributeOption> visible = g.options.Where(OptionMatches).ToList();
                if (visible.Count == 0) continue;

                bool expanded = searching || !collapsedGroups.Contains(g.label);

                var headerRect = new Rect(0f, y, viewRect.width, HeaderHeight);
                if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
                var iconRect = new Rect(headerRect.x + 2f, headerRect.y + (HeaderHeight - 16f) / 2f, 16f, 16f);
                GUI.DrawTexture(iconRect, expanded ? TexButton.Minus : TexButton.Plus);
                Color prevHeader = GUI.color;
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(new Rect(headerRect.x + 22f, headerRect.y, headerRect.width - 22f, headerRect.height), g.label);
                GUI.color = prevHeader;
                if (Widgets.ButtonInvisible(headerRect) && !searching)
                {
                    if (!collapsedGroups.Remove(g.label)) collapsedGroups.Add(g.label);
                }
                y += HeaderHeight;

                if (!expanded) continue;

                foreach (AttributeOption opt in visible)
                {
                    var optRect = new Rect(0f, y, viewRect.width, RowHeight);
                    if (Mouse.IsOver(optRect)) Widgets.DrawHighlight(optRect);
                    Widgets.Label(new Rect(optRect.x + 14f, optRect.y, optRect.width - 14f, optRect.height), OptionLabel(opt));
                    if (Widgets.ButtonInvisible(optRect)) AddRule(opt);
                    y += RowHeight;
                }
            }

            Widgets.EndScrollView();
        }

        private bool OptionMatches(AttributeOption opt) => Matches(OptionLabel(opt));

        private bool Matches(string label)
            => searchText.NullOrEmpty()
               || (label != null && label.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);

        private static string GroupKey(AttributeOption o)
        {
            if (o.kind != RuleAttributeKind.Numeric && o.kind != RuleAttributeKind.Categorical)
                return null; // facets group
            return o.category == null || o.category.label.NullOrEmpty()
                ? "__other__" : o.category.LabelCap.ToString();
        }

        private static string GroupLabel(string key)
        {
            if (key == null) return "APB.FacetsGroup".Translate();
            if (key == "__other__") return "APB.OtherCategory".Translate();
            return key;
        }

        private static string OptionLabel(AttributeOption o)
        {
            switch (o.kind)
            {
                case RuleAttributeKind.Quality: return "APB.Facet.Quality".Translate();
                case RuleAttributeKind.HitPoints: return "APB.Facet.HitPoints".Translate();
                case RuleAttributeKind.Material: return "APB.Facet.Material".Translate();
                case RuleAttributeKind.SpecialFilter: return CleanSpecialFilterLabel(o.specialFilter);
                default: return o.label;
            }
        }

        private static string CleanSpecialFilterLabel(SpecialThingFilterDef sf)
        {
            string label = sf?.LabelCap;
            if (label.NullOrEmpty()) return label;
            return label.StartsWith("allow ", StringComparison.OrdinalIgnoreCase)
                ? label.Substring(6).CapitalizeFirst() : label;
        }

        private class OptionGroup
        {
            public string label;
            public int order;
            public bool isFacet;
            public List<AttributeOption> options;
        }

        // ---- Right: copy/paste + material lens ----

        private void DrawRightToolbar(Rect rect)
        {
            const float bw = 96f, gap = 6f, bh = 28f;
            float y = rect.y + (rect.height - bh) / 2f;

            var copyRect = new Rect(rect.x, y, bw, bh);
            TooltipHandler.TipRegionByKey(copyRect, "APB.CopyTip");
            if (Widgets.ButtonText(copyRect, "APB.Copy".Translate()))
                clipboard = working.Clone();

            bool canPaste = clipboard != null && !clipboard.IsEmpty;
            var pasteRect = new Rect(copyRect.xMax + gap, y, bw, bh);
            TooltipHandler.TipRegionByKey(pasteRect, "APB.PasteTip");
            if (Widgets.ButtonText(pasteRect, "APB.Paste".Translate(), active: canPaste) && canPaste)
            {
                working = clipboard.Clone();
                valueBuffers.Clear();
            }

            const float ddWidth = 150f;
            var ddRect = new Rect(rect.xMax - ddWidth, y, ddWidth, bh);
            string ddLabel = evalStuff != null ? evalStuff.LabelCap.ToString() : "APB.Multiplier".Translate().ToString();
            TooltipHandler.TipRegionByKey(ddRect, "APB.EvalAsTip");
            if (Widgets.ButtonText(ddRect, ddLabel))
                OpenMaterialLensMenu();

            var ddLabelRect = new Rect(pasteRect.xMax + gap, rect.y, ddRect.x - pasteRect.xMax - gap * 2f, rect.height);
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(ddLabelRect, "APB.EvalAs".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prev;
        }

        // ---- Right: rule list grouped by scope ----

        private void DrawRightPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(6f);

            if (working.rules.Count == 0)
            {
                Color prev = GUI.color;
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inner, "APB.NoRules".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = prev;
                return;
            }

            List<ApparelLayerDef> scopeLayers = working.rules
                .Select(r => r.layerScope).Distinct()
                .Where(l => l != null)
                .OrderBy(l => l.drawOrder).ToList();
            bool hasGlobal = working.rules.Any(r => r.layerScope == null);

            float viewHeight = 0f;
            if (hasGlobal)
            {
                viewHeight += HeaderHeight;
                if (!collapsedScopes.Contains(null))
                    viewHeight += working.rules.Count(r => r.layerScope == null) * RuleRowHeight;
            }
            foreach (ApparelLayerDef layer in scopeLayers)
            {
                viewHeight += HeaderHeight;
                if (!collapsedScopes.Contains(layer))
                    viewHeight += working.rules.Count(r => r.layerScope == layer) * RuleRowHeight;
            }

            var viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(viewHeight, inner.height));
            Widgets.BeginScrollView(inner, ref rightScroll, viewRect);
            float y = 0f;
            AttributeRule toDelete = null;

            if (hasGlobal)
                DrawScopeGroup("APB.Global".Translate(), null, viewRect.width, ref y, ref toDelete);
            foreach (ApparelLayerDef layer in scopeLayers)
                DrawScopeGroup(layer.LabelCap, layer, viewRect.width, ref y, ref toDelete);

            Widgets.EndScrollView();

            if (toDelete != null)
            {
                working.rules.Remove(toDelete);
                valueBuffers.Remove(toDelete);
            }
        }

        private void DrawScopeGroup(string label, ApparelLayerDef layer, float width, ref float y, ref AttributeRule toDelete)
        {
            bool collapsed = collapsedScopes.Contains(layer);
            int count = working.rules.Count(r => r.layerScope == layer);

            var headerRect = new Rect(0f, y, width, HeaderHeight);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            var iconRect = new Rect(headerRect.x + 2f, headerRect.y + (HeaderHeight - 16f) / 2f, 16f, 16f);
            GUI.DrawTexture(iconRect, collapsed ? TexButton.Plus : TexButton.Minus);
            Widgets.Label(new Rect(headerRect.x + 22f, headerRect.y, headerRect.width - 22f, headerRect.height),
                $"{label} ({count})");
            if (Widgets.ButtonInvisible(headerRect))
            {
                if (!collapsedScopes.Remove(layer)) collapsedScopes.Add(layer);
            }
            y += HeaderHeight;

            if (collapsed) return;

            foreach (AttributeRule rule in working.rules)
            {
                if (rule.layerScope != layer) continue;
                var rowRect = new Rect(12f, y, width - 12f, RuleRowHeight);
                if (DrawRuleRow(rowRect.ContractedBy(1f), rule)) toDelete = rule;
                y += RuleRowHeight;
            }
        }

        private bool DrawRuleRow(Rect row, AttributeRule rule)
        {
            const float gap = 4f;
            float x = row.x;

            Rect Slice(float w)
            {
                var r = new Rect(x, row.y, w, row.height);
                x += w + gap;
                return r;
            }

            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);

            if (rule.kind == RuleAttributeKind.Quality || rule.kind == RuleAttributeKind.HitPoints)
            {
                if (Widgets.ButtonText(Slice(74f), ("APB.Bound." + rule.rangeBound).Translate().CapitalizeFirst()))
                    rule.rangeBound = rule.rangeBound == RangeBound.AtLeast ? RangeBound.AtMost : RangeBound.AtLeast;
            }
            else
            {
                bool allow = rule.polarity == RulePolarity.Require;
                string label = rule.kind == RuleAttributeKind.SpecialFilter
                    ? (allow ? "APB.Special.Allow" : "APB.Special.Disallow").Translate()
                    : ("APB.Polarity." + rule.polarity).Translate();
                if (ColoredToggle(Slice(74f), label, allow))
                    rule.polarity = allow ? RulePolarity.Forbid : RulePolarity.Require;
            }

            if (rule.IsPerDef)
            {
                if (Widgets.ButtonText(Slice(92f), ScopeLabel(rule)))
                    OpenScopeMenu(rule);
            }
            else
            {
                var scopeRect = Slice(92f);
                TooltipHandler.TipRegionByKey(scopeRect, "APB.FacetScopeTip");
                DrawFaded(scopeRect, "APB.Global".Translate(), TextAnchor.MiddleCenter);
            }

            var deleteRect = new Rect(row.xMax - 24f, row.y, 24f, row.height);
            bool delete = Widgets.ButtonImage(deleteRect, TexButton.Delete);

            switch (rule.kind)
            {
                case RuleAttributeKind.Numeric:
                    DrawNumericContent(row, rule, deleteRect, x, gap, Slice);
                    break;
                case RuleAttributeKind.Categorical:
                    DrawSingleValueRow(row, deleteRect, x, gap, OptionFor(rule)?.label ?? rule.attrKey,
                        CategoricalValueLabel(rule), () => OpenCategoricalMenu(rule));
                    break;
                case RuleAttributeKind.Material:
                    DrawSingleValueRow(row, deleteRect, x, gap, "APB.Facet.Material".Translate(),
                        rule.materialStuff?.LabelCap ?? "APB.Pick".Translate(), () => OpenMaterialMenu(rule));
                    break;
                case RuleAttributeKind.Quality:
                    DrawSingleValueRow(row, deleteRect, x, gap, "APB.Facet.Quality".Translate(),
                        rule.qualityValue.GetLabel().CapitalizeFirst(), () => OpenQualityMenu(rule));
                    break;
                case RuleAttributeKind.HitPoints:
                    DrawHitPointsContent(row, rule, deleteRect, x);
                    break;
                case RuleAttributeKind.SpecialFilter:
                    RowLabel(new Rect(x, row.y, Mathf.Max(deleteRect.x - gap - x, 40f), row.height),
                        CleanSpecialFilterLabel(rule.specialFilter));
                    break;
            }

            return delete;
        }

        private void DrawNumericContent(Rect row, AttributeRule rule, Rect deleteRect, float x, float gap, Func<float, Rect> Slice)
        {
            float fixedRight = 104f + gap + 48f;
            float labelWidth = deleteRect.x - gap - fixedRight - gap - x;
            var labelRect = Slice(Mathf.Max(labelWidth, 60f));
            RowLabel(labelRect, rule.stat?.LabelCap ?? "?");
            if (rule.stat != null && !rule.stat.description.NullOrEmpty())
                TooltipHandler.TipRegion(labelRect, rule.stat.description);

            if (Widgets.ButtonText(Slice(104f), ("APB.Mode." + rule.numericMode).Translate().CapitalizeFirst()))
                OpenModeMenu(rule);

            var valueRect = Slice(48f);
            if (rule.NeedsThreshold)
            {
                if (!valueBuffers.TryGetValue(rule, out string buffer)) buffer = rule.threshold.ToString("0.###");
                Widgets.TextFieldNumeric(valueRect, ref rule.threshold, ref buffer, -1e9f, 1e9f);
                valueBuffers[rule] = buffer;
            }
        }

        private void DrawHitPointsContent(Rect row, AttributeRule rule, Rect deleteRect, float x)
        {
            const float fieldW = 46f, pctW = 14f, gap = 4f;
            var labelRect = new Rect(x, row.y, deleteRect.x - gap - x - fieldW - pctW - gap, row.height);
            RowLabel(labelRect, "APB.Facet.HitPoints".Translate());

            var fieldRect = new Rect(deleteRect.x - gap - pctW - fieldW, row.y, fieldW, row.height);
            float pct = Mathf.Round(rule.threshold * 100f);
            if (!valueBuffers.TryGetValue(rule, out string buffer)) buffer = pct.ToString("0");
            Widgets.TextFieldNumeric(fieldRect, ref pct, ref buffer, 0f, 100f);
            rule.threshold = pct / 100f;
            valueBuffers[rule] = buffer;
            RowLabel(new Rect(fieldRect.xMax + 2f, row.y, pctW, row.height), "%");
        }

        // label on the left, a single picker button filling the space before delete
        private void DrawSingleValueRow(Rect row, Rect deleteRect, float x, float gap, string label, string valueLabel, Action onClick)
        {
            const float ctrlW = 150f;
            var ctrlRect = new Rect(deleteRect.x - gap - ctrlW, row.y, ctrlW, row.height);
            var labelRect = new Rect(x, row.y, Mathf.Max(ctrlRect.x - gap - x, 40f), row.height);
            RowLabel(labelRect, label);
            if (Widgets.ButtonText(ctrlRect, valueLabel))
                onClick();
        }

        private static void DrawFaded(Rect rect, string text, TextAnchor anchor)
        {
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prev;
        }

        // Vertically centres a row label so it lines up with the buttons beside it.
        private static void RowLabel(Rect rect, string text)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static bool ColoredToggle(Rect rect, string label, bool positive)
        {
            Color prev = GUI.color;
            GUI.color = positive ? new Color(0.5f, 0.78f, 0.5f) : new Color(0.85f, 0.5f, 0.5f);
            bool clicked = Widgets.ButtonText(rect, label);
            GUI.color = prev;
            return clicked;
        }

        // ---- Bottom buttons ----

        private void DrawButtonRow(Rect rect)
        {
            const float bw = 130f, bh = 34f, gap = 12f;
            float totalWidth = bw * 3f + gap * 2f;
            float startX = rect.x + (rect.width - totalWidth) / 2f;
            float y = rect.y + (rect.height - bh) / 2f;

            var applyRect = new Rect(startX, y, bw, bh);
            var saveRect = new Rect(applyRect.xMax + gap, y, bw, bh);
            var cancelRect = new Rect(saveRect.xMax + gap, y, bw, bh);

            TooltipHandler.TipRegionByKey(applyRect, "APB.ApplyTip");
            if (Widgets.ButtonText(applyRect, "APB.Apply".Translate()))
            {
                Commit();
                working.ApplyTo(policy, evalStuff);
                Close();
            }
            TooltipHandler.TipRegionByKey(saveRect, "APB.SaveTip");
            if (Widgets.ButtonText(saveRect, "APB.Save".Translate()))
            {
                Commit();
                Close();
            }
            if (Widgets.ButtonText(cancelRect, "APB.Cancel".Translate()))
                Close();
        }

        private void Commit()
            => GameComponent_ApparelPolicyBuilder.Instance?.Store(policy, working.Clone());

        // ---- Rule construction / menus ----

        private void AddRule(AttributeOption opt)
        {
            var rule = new AttributeRule { kind = opt.kind };
            switch (opt.kind)
            {
                case RuleAttributeKind.Numeric: rule.stat = opt.stat; break;
                case RuleAttributeKind.Categorical:
                    rule.attrKey = opt.key;
                    rule.categoricalValue = opt.values.FirstOrDefault()?.token;
                    break;
                case RuleAttributeKind.HitPoints: rule.threshold = 0.5f; break;
                case RuleAttributeKind.Material: rule.materialStuff = AttributeCache.MaterialAttributes.FirstOrDefault(); break;
                case RuleAttributeKind.SpecialFilter: rule.specialFilter = opt.specialFilter; break;
            }
            working.rules.Add(rule);
            collapsedScopes.Remove(null);
        }

        private static AttributeOption OptionFor(AttributeRule rule) => AttributeCache.OptionFor(rule.attrKey);

        private static string CategoricalValueLabel(AttributeRule rule)
        {
            AttributeOption opt = OptionFor(rule);
            CategoricalValue v = opt?.values.FirstOrDefault(cv => cv.token == rule.categoricalValue);
            return v?.label ?? rule.categoricalValue ?? "APB.Pick".Translate();
        }

        private string ScopeLabel(AttributeRule rule)
            => rule.layerScope != null ? rule.layerScope.LabelCap.ToString() : "APB.Global".Translate().ToString();

        private void OpenScopeMenu(AttributeRule rule)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("APB.Global".Translate(), () => { rule.layerScope = null; collapsedScopes.Remove(null); })
            };
            foreach (ApparelLayerDef layer in AttributeCache.Layers)
            {
                ApparelLayerDef captured = layer;
                options.Add(new FloatMenuOption(layer.LabelCap,
                    () => { rule.layerScope = captured; collapsedScopes.Remove(captured); }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenModeMenu(AttributeRule rule)
        {
            var options = new List<FloatMenuOption>();
            foreach (NumericMode mode in Enum.GetValues(typeof(NumericMode)))
            {
                NumericMode captured = mode;
                options.Add(new FloatMenuOption(("APB.Mode." + mode).Translate(), () => rule.numericMode = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenCategoricalMenu(AttributeRule rule)
        {
            AttributeOption opt = OptionFor(rule);
            if (opt?.values == null) return;
            var options = new List<FloatMenuOption>();
            foreach (CategoricalValue cv in opt.values)
            {
                CategoricalValue captured = cv;
                options.Add(new FloatMenuOption(captured.label, () => rule.categoricalValue = captured.token));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenQualityMenu(AttributeRule rule)
        {
            var options = new List<FloatMenuOption>();
            foreach (QualityCategory q in Enum.GetValues(typeof(QualityCategory)))
            {
                QualityCategory captured = q;
                options.Add(new FloatMenuOption(q.GetLabel().CapitalizeFirst(), () => rule.qualityValue = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenMaterialMenu(AttributeRule rule)
        {
            var options = new List<FloatMenuOption>();
            foreach (ThingDef material in AttributeCache.MaterialAttributes)
            {
                ThingDef captured = material;
                options.Add(new FloatMenuOption(material.LabelCap, () => rule.materialStuff = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenMaterialLensMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("APB.Multiplier".Translate(), () => evalStuff = null)
            };
            foreach (ThingDef material in AttributeCache.StuffMaterials)
            {
                ThingDef captured = material;
                options.Add(new FloatMenuOption(material.LabelCap, () => evalStuff = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
