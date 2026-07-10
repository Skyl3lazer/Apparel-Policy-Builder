using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelAttributeFilter
{
    // The filter authoring window: attributes on the left, scope-grouped rules on the right.
    public class Dialog_ApparelAttributeFilter : Window
    {
        private const float TitleHeight = 34f;
        private const float ButtonRowHeight = 40f;
        private const float LeftPanelWidth = 320f;
        private const float PanelGap = 12f;
        private const float RowHeight = 24f;
        private const float HeaderHeight = 26f;
        private const float RuleRowHeight = 32f;
        private const float ToolbarHeight = 32f;

        // Session clipboard, shared across windows so a ruleset can be transferred between policies.
        private static Ruleset clipboard;

        private readonly ApparelPolicy policy;
        private Ruleset working;
        private readonly List<AttributeCategory> categories;
        private readonly HashSet<string> collapsedCategories = new HashSet<string>();
        private readonly HashSet<ApparelLayerDef> collapsedScopes = new HashSet<ApparelLayerDef>(); // holds null for Global

        private string searchText = "";
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private ThingDef evalStuff; // null = evaluate stuff-powered stats by the material multiplier
        private readonly Dictionary<AttributeRule, string> thresholdBuffers = new Dictionary<AttributeRule, string>();

        public override Vector2 InitialSize => new Vector2(940f, 590f);

        public Dialog_ApparelAttributeFilter(ApparelPolicy policy)
        {
            this.policy = policy;
            AttributeCache.EnsureBuilt();

            Ruleset stored = GameComponent_ApparelAttributeFilter.Instance?.GetRuleset(policy);
            working = stored != null ? stored.Clone() : new Ruleset();

            // Merge categories that share a display name (vanilla has several "Basics").
            categories = AttributeCache.NumericAttributes
                .GroupBy(CategoryLabelOf)
                .Select(g => new AttributeCategory
                {
                    label = g.Key,
                    order = g.Min(s => s.category?.displayOrder ?? int.MaxValue),
                    stats = g.OrderBy(s => (s.label ?? s.defName)).ToList()
                })
                .OrderBy(c => c.order).ThenBy(c => c.label)
                .ToList();

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
                Widgets.Label(titleRect, "AAF.Title".Translate());
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
                    "AAF.SearchHint".Translate());
                GUI.color = prev;
            }

            var listRect = new Rect(inner.x, searchRect.yMax + 6f, inner.width, inner.yMax - searchRect.yMax - 6f);

            bool searching = !searchText.NullOrEmpty();
            bool coversVisible = Matches("AAF.Covers".Translate());
            float viewHeight = (coversVisible ? RowHeight : 0f);
            foreach (AttributeCategory cat in categories)
            {
                int visible = cat.stats.Count(CoreMatches);
                if (visible == 0) continue;
                viewHeight += HeaderHeight;
                if (searching || !collapsedCategories.Contains(cat.label))
                    viewHeight += visible * RowHeight;
            }

            var viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(viewHeight, listRect.height));
            Widgets.BeginScrollView(listRect, ref leftScroll, viewRect);
            float y = 0f;

            if (coversVisible)
            {
                if (Widgets.ButtonText(new Rect(0f, y, viewRect.width, RowHeight), "AAF.Covers".Translate()))
                    AddCoversRule();
                y += RowHeight;
            }

            foreach (AttributeCategory cat in categories)
            {
                List<StatDef> visibleStats = cat.stats.Where(CoreMatches).ToList();
                if (visibleStats.Count == 0) continue;

                bool expanded = searching || !collapsedCategories.Contains(cat.label);

                var headerRect = new Rect(0f, y, viewRect.width, HeaderHeight);
                if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
                var iconRect = new Rect(headerRect.x + 2f, headerRect.y + (HeaderHeight - 16f) / 2f, 16f, 16f);
                GUI.DrawTexture(iconRect, expanded ? TexButton.Minus : TexButton.Plus);
                Color prevHeader = GUI.color;
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(new Rect(headerRect.x + 22f, headerRect.y, headerRect.width - 22f, headerRect.height),
                    cat.label);
                GUI.color = prevHeader;
                if (Widgets.ButtonInvisible(headerRect) && !searching)
                {
                    if (!collapsedCategories.Remove(cat.label)) collapsedCategories.Add(cat.label);
                }
                y += HeaderHeight;

                if (!expanded) continue;

                foreach (StatDef stat in visibleStats)
                {
                    var statRect = new Rect(0f, y, viewRect.width, RowHeight);
                    if (Mouse.IsOver(statRect)) Widgets.DrawHighlight(statRect);
                    if (!stat.description.NullOrEmpty()) TooltipHandler.TipRegion(statRect, stat.description);
                    Widgets.Label(new Rect(statRect.x + 14f, statRect.y, statRect.width - 14f, statRect.height),
                        stat.LabelCap);
                    if (Widgets.ButtonInvisible(statRect)) AddNumericRule(stat);
                    y += RowHeight;
                }
            }

            Widgets.EndScrollView();
        }

        private bool CoreMatches(StatDef stat) => Matches(stat.LabelCap);

        private bool Matches(string label)
            => searchText.NullOrEmpty()
               || (label != null && label.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);

        private static string CategoryLabelOf(StatDef stat)
            => stat.category != null && !stat.category.label.NullOrEmpty()
                ? stat.category.LabelCap.ToString()
                : "AAF.OtherCategory".Translate().ToString();

        // Numeric attributes grouped by display name (duplicate-named categories merged).
        private class AttributeCategory
        {
            public string label;
            public int order;
            public List<StatDef> stats;
        }

        // ---- Right: copy/paste toolbar ----

        private void DrawRightToolbar(Rect rect)
        {
            const float bw = 96f, gap = 6f, bh = 28f;
            float y = rect.y + (rect.height - bh) / 2f;

            var copyRect = new Rect(rect.x, y, bw, bh);
            TooltipHandler.TipRegionByKey(copyRect, "AAF.CopyTip");
            if (Widgets.ButtonText(copyRect, "AAF.Copy".Translate()))
                clipboard = working.Clone();

            bool canPaste = clipboard != null && !clipboard.IsEmpty;
            var pasteRect = new Rect(copyRect.xMax + gap, y, bw, bh);
            TooltipHandler.TipRegionByKey(pasteRect, "AAF.PasteTip");
            if (Widgets.ButtonText(pasteRect, "AAF.Paste".Translate(), active: canPaste) && canPaste)
            {
                working = clipboard.Clone();
                thresholdBuffers.Clear();
            }

            // Right side: material lens for stuff-powered stats.
            const float ddWidth = 150f;
            var ddRect = new Rect(rect.xMax - ddWidth, y, ddWidth, bh);
            string ddLabel = evalStuff != null ? evalStuff.LabelCap.ToString() : "AAF.Multiplier".Translate().ToString();
            TooltipHandler.TipRegionByKey(ddRect, "AAF.EvalAsTip");
            if (Widgets.ButtonText(ddRect, ddLabel))
                OpenMaterialMenu();

            var ddLabelRect = new Rect(pasteRect.xMax + gap, rect.y, ddRect.x - pasteRect.xMax - gap * 2f, rect.height);
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(ddLabelRect, "AAF.EvalAs".Translate());
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
                Widgets.Label(inner, "AAF.NoRules".Translate());
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
                DrawScopeGroup("AAF.Global".Translate(), null, viewRect.width, ref y, ref toDelete);
            foreach (ApparelLayerDef layer in scopeLayers)
                DrawScopeGroup(layer.LabelCap, layer, viewRect.width, ref y, ref toDelete);

            Widgets.EndScrollView();

            if (toDelete != null)
            {
                working.rules.Remove(toDelete);
                thresholdBuffers.Remove(toDelete);
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
                var rowRect = new Rect(0f, y, width, RuleRowHeight);
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

            // Polarity toggle
            if (Widgets.ButtonText(Slice(74f), ("AAF.Polarity." + rule.polarity).Translate()))
                rule.polarity = rule.polarity == RulePolarity.Forbid ? RulePolarity.Require : RulePolarity.Forbid;

            // Scope
            if (Widgets.ButtonText(Slice(92f), ScopeLabel(rule)))
                OpenScopeMenu(rule);

            // Delete (reserve at right)
            var deleteRect = new Rect(row.xMax - 24f, row.y, 24f, row.height);
            bool delete = Widgets.ButtonText(deleteRect, "×");

            if (rule.kind == RuleAttributeKind.Covers)
            {
                Widgets.Label(Slice(58f), "AAF.CoversLabel".Translate());
                float groupWidth = deleteRect.x - gap - x;
                if (Widgets.ButtonText(new Rect(x, row.y, Mathf.Max(groupWidth, 60f), row.height),
                        rule.coversGroup?.LabelCap ?? "AAF.Pick".Translate()))
                    OpenCoversMenu(rule);
            }
            else
            {
                // Attribute label takes the flexible middle space.
                float fixedRight = 104f + gap + 48f; // mode + gap + value
                float labelWidth = deleteRect.x - gap - fixedRight - gap - x;
                var labelRect = Slice(Mathf.Max(labelWidth, 60f));
                Widgets.Label(labelRect, rule.stat?.LabelCap ?? "?");
                if (rule.stat != null && !rule.stat.description.NullOrEmpty())
                    TooltipHandler.TipRegion(labelRect, rule.stat.description);

                if (Widgets.ButtonText(Slice(104f), ("AAF.Mode." + rule.numericMode).Translate()))
                    OpenModeMenu(rule);

                var valueRect = Slice(48f);
                if (rule.NeedsThreshold)
                {
                    if (!thresholdBuffers.TryGetValue(rule, out string buffer))
                        buffer = rule.threshold.ToString("0.###");
                    Widgets.TextFieldNumeric(valueRect, ref rule.threshold, ref buffer, -1e9f, 1e9f);
                    thresholdBuffers[rule] = buffer;
                }
            }

            return delete;
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

            TooltipHandler.TipRegionByKey(applyRect, "AAF.ApplyTip");
            if (Widgets.ButtonText(applyRect, "AAF.Apply".Translate()))
            {
                Commit();
                working.ApplyTo(policy, evalStuff);
                Close();
            }
            TooltipHandler.TipRegionByKey(saveRect, "AAF.SaveTip");
            if (Widgets.ButtonText(saveRect, "AAF.Save".Translate()))
            {
                Commit();
                Close();
            }
            if (Widgets.ButtonText(cancelRect, "AAF.Cancel".Translate()))
                Close();
        }

        private void Commit()
            => GameComponent_ApparelAttributeFilter.Instance?.Store(policy, working.Clone());

        // ---- Rule construction / menus ----

        private void AddNumericRule(StatDef stat)
            => working.rules.Add(new AttributeRule { kind = RuleAttributeKind.Numeric, stat = stat });

        private void AddCoversRule()
            => working.rules.Add(new AttributeRule
            {
                kind = RuleAttributeKind.Covers,
                coversGroup = AttributeCache.Covers.FirstOrDefault()
            });

        private string ScopeLabel(AttributeRule rule)
            => rule.layerScope != null ? rule.layerScope.LabelCap.ToString() : "AAF.Global".Translate().ToString();

        private void OpenScopeMenu(AttributeRule rule)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("AAF.Global".Translate(), () => rule.layerScope = null)
            };
            foreach (ApparelLayerDef layer in AttributeCache.Layers)
            {
                ApparelLayerDef captured = layer;
                options.Add(new FloatMenuOption(layer.LabelCap, () => rule.layerScope = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenModeMenu(AttributeRule rule)
        {
            var options = new List<FloatMenuOption>();
            foreach (NumericMode mode in Enum.GetValues(typeof(NumericMode)))
            {
                NumericMode captured = mode;
                options.Add(new FloatMenuOption(("AAF.Mode." + mode).Translate(),
                    () => rule.numericMode = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenCoversMenu(AttributeRule rule)
        {
            var options = new List<FloatMenuOption>();
            foreach (BodyPartGroupDef group in AttributeCache.Covers)
            {
                BodyPartGroupDef captured = group;
                options.Add(new FloatMenuOption(group.LabelCap, () => rule.coversGroup = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenMaterialMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("AAF.Multiplier".Translate(), () => evalStuff = null)
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
