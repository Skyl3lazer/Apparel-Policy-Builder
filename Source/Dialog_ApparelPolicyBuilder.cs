using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelPolicyBuilder
{
    public partial class Dialog_ApparelPolicyBuilder : Window
    {
        private const float TitleHeight = 34f;
        private const float ButtonRowHeight = 40f;
        private const float LeftPanelWidth = 320f;
        private const float PanelGap = 12f;
        private const float RowHeight = 24f;
        private const float HeaderHeight = 26f;
        private const float RuleRowHeight = 32f;
        private const float ToolbarHeight = 32f;
        private const float PercentSuffixWidth = 14f;

        // Static so a ruleset can be copied between policy windows.
        private static Ruleset clipboard;

        private readonly ApparelPolicy policy;
        private Ruleset working;
        private readonly List<OptionGroup> apparelGroups;
        private readonly List<OptionGroup> weaponGroups;
        private bool showWeapons;
        private readonly HashSet<string> collapsedGroups = new HashSet<string>();
        private readonly HashSet<string> collapsedScopes = new HashSet<string>(); // keyed by scope: "global", "exceptutil", "layer:<defName>"

        private string searchText = "";
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private readonly Dictionary<AttributeRule, string> valueBuffers = new Dictionary<AttributeRule, string>();

        public override Vector2 InitialSize => new Vector2(940f, 590f);

        public Dialog_ApparelPolicyBuilder(ApparelPolicy policy)
        {
            this.policy = policy;
            AttributeCache.EnsureBuilt();

            Ruleset stored = RulesetStore.Get(policy);
            working = stored != null ? stored.Clone() : new Ruleset();

            apparelGroups = BuildGroups(AttributeCache.Options);
            weaponGroups = BuildGroups(AttributeCache.WeaponOptions);

            foreach (OptionGroup g in apparelGroups.Skip(1)) collapsedGroups.Add(g.label);
            foreach (OptionGroup g in weaponGroups.Skip(1)) collapsedGroups.Add(g.label);

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

        private static List<OptionGroup> BuildGroups(List<AttributeOption> options)
            => options
                .GroupBy(GroupKey)
                .Select(g => new OptionGroup
                {
                    isFacet = g.Key == null,
                    label = GroupLabel(g.Key),
                    options = g.OrderBy(o => o.order).ThenBy(OptionLabel).ToList()
                })
                .OrderBy(gr => gr.isFacet).ThenBy(gr => gr.label, StringComparer.OrdinalIgnoreCase)
                .ToList();

        // While picking a condition, the palette follows the target expression's universe; otherwise the toggle.
        private bool WeaponMode => pendingInsert != null ? pendingInsertWeapon : (showWeapons && AttributeCache.WeaponsActive);
        private List<OptionGroup> ActiveGroups => WeaponMode ? weaponGroups : apparelGroups;
        private bool ShowPaletteToggle => AttributeCache.WeaponsActive && pendingInsert == null;

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

            float listTop = searchRect.yMax + 6f;
            if (ShowPaletteToggle)
            {
                var toggleRect = new Rect(inner.x, listTop, inner.width, 26f);
                float half = (toggleRect.width - 4f) / 2f;
                if (Widgets.ButtonText(new Rect(toggleRect.x, toggleRect.y, half, toggleRect.height),
                        "APB.PaletteApparel".Translate(), active: showWeapons))
                    showWeapons = false;
                if (Widgets.ButtonText(new Rect(toggleRect.x + half + 4f, toggleRect.y, half, toggleRect.height),
                        "APB.PaletteWeapons".Translate(), active: !showWeapons))
                    showWeapons = true;
                listTop = toggleRect.yMax + 6f;
            }
            if (pendingInsert != null)
            {
                var banner = new Rect(inner.x, listTop, inner.width, 24f);
                Widgets.DrawHighlightSelected(banner);
                var cancelRect = new Rect(banner.xMax - 60f, banner.y + 1f, 60f, 22f);
                RowLabel(new Rect(banner.x + 4f, banner.y, banner.width - 68f, banner.height), "APB.PickForExpression".Translate());
                if (Widgets.ButtonText(cancelRect, "APB.CancelPick".Translate())) pendingInsert = null;
                listTop = banner.yMax + 4f;
            }
            var listRect = new Rect(inner.x, listTop, inner.width, inner.yMax - listTop);
            bool searching = !searchText.NullOrEmpty();

            List<OptionGroup> groups = ActiveGroups;
            var visiblePerGroup = new List<AttributeOption>[groups.Count];
            float viewHeight = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                OptionGroup g = groups[i];
                List<AttributeOption> visible;
                if (!searching)
                {
                    visible = g.options;
                }
                else
                {
                    visible = new List<AttributeOption>();
                    for (int j = 0; j < g.options.Count; j++)
                    {
                        AttributeOption opt = g.options[j];
                        if (OptionMatches(opt)) visible.Add(opt);
                    }
                }
                visiblePerGroup[i] = visible;
                if (visible.Count == 0) continue;
                viewHeight += HeaderHeight;
                if (searching || !collapsedGroups.Contains(g.label))
                    viewHeight += visible.Count * RowHeight;
            }

            var viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(viewHeight, listRect.height));
            Widgets.BeginScrollView(listRect, ref leftScroll, viewRect);
            float y = 0f;

            for (int i = 0; i < groups.Count; i++)
            {
                OptionGroup g = groups[i];
                List<AttributeOption> visible = visiblePerGroup[i];
                if (visible == null || visible.Count == 0) continue;

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

                bool insertMode = pendingInsert != null;
                for (int j = 0; j < visible.Count; j++)
                {
                    AttributeOption opt = visible[j];
                    var optRect = new Rect(0f, y, viewRect.width, RowHeight);
                    bool selectable = !insertMode || IsPerDefOption(opt);
                    if (Mouse.IsOver(optRect) && selectable) Widgets.DrawHighlight(optRect);
                    var labelRect = new Rect(optRect.x + 14f, optRect.y, optRect.width - 14f, optRect.height);
                    string optLabel = OptionLabel(opt);
                    if (selectable) Widgets.Label(labelRect, optLabel);
                    else DrawFaded(labelRect, optLabel, TextAnchor.UpperLeft);
                    if (selectable && Widgets.ButtonInvisible(optRect)) OnAttributeClicked(opt);
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

        private static string OptionLabel(AttributeOption o) => o.label;

        private static string CleanSpecialFilterLabel(SpecialThingFilterDef sf)
            => AttributeCache.CleanSpecialFilterLabel(sf);

        private class OptionGroup
        {
            public string label;
            public bool isFacet;
            public List<AttributeOption> options;
        }

        private sealed class ScopeGroup
        {
            public string key;
            public string label;
            public List<AttributeRule> rules = new List<AttributeRule>();
        }

        // ---- Right: copy/paste + material lens ----

        private void DrawRightToolbar(Rect rect)
        {
            const float bw = 96f, gap = 6f, bh = 28f;
            float y = rect.y + (rect.height - bh) / 2f;

            var docsRect = new Rect(rect.x, y, bh, bh);
            TooltipHandler.TipRegionByKey(docsRect, "APB.DocumentsTip");
            if (Widgets.ButtonImage(docsRect, TexButton.Save))
                Find.WindowStack.Add(new Dialog_ApparelPolicyDocuments(this));

            var copyRect = new Rect(docsRect.xMax + gap, y, bw, bh);
            TooltipHandler.TipRegionByKey(copyRect, "APB.CopyTip");
            if (Widgets.ButtonText(copyRect, "APB.Copy".Translate()))
                clipboard = working.Clone();

            bool canPaste = clipboard != null && !clipboard.IsEmpty;
            var pasteRect = new Rect(copyRect.xMax + gap, y, bw, bh);
            TooltipHandler.TipRegionByKey(pasteRect, "APB.PasteTip");
            if (Widgets.ButtonText(pasteRect, "APB.Paste".Translate(), active: canPaste) && canPaste)
                ReplaceWorkingConfirmed(clipboard.Clone(), "APB.DiscardConfirm".Translate());

            const float ddWidth = 150f;
            var ddRect = new Rect(rect.xMax - ddWidth, y, ddWidth, bh);
            string ddLabel = LensLabel(working.evalMode, working.evalStuff);
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

            bool advanced = ApparelPolicyBuilderMod.AdvancedExpressions;
            float contentWidth = inner.width - 16f;

            List<ScopeGroup> groups = ScopeGroups();
            bool hasVisibleExpr = false;
            for (int i = 0; i < working.expressionRules.Count; i++)
            {
                if (ExprVisible(working.expressionRules[i]))
                {
                    hasVisibleExpr = true;
                    break;
                }
            }
            bool hasExprSection = advanced || hasVisibleExpr;
            float expressionsGap = groups.Count > 0 && hasExprSection ? 10f : 0f;

            bool hasVisibleRule = false;
            for (int i = 0; i < working.rules.Count; i++)
            {
                if (RuleVisible(working.rules[i]))
                {
                    hasVisibleRule = true;
                    break;
                }
            }
            bool noVisibleContent = !hasVisibleRule && !hasVisibleExpr;
            string emptyText = noVisibleContent
                ? "APB.NoRules".Translate() + "\n" + (advanced ? "APB.NoRulesExpression" : "APB.NoRulesAdvancedHint").Translate()
                : null;
            float emptyHeight = emptyText != null ? Text.CalcHeight(emptyText, contentWidth) : 0f;

            float viewHeight = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                ScopeGroup g = groups[i];
                viewHeight += HeaderHeight;
                if (!collapsedScopes.Contains(g.key))
                    viewHeight += g.rules.Count * RuleRowHeight;
            }
            viewHeight += emptyHeight + expressionsGap + ExpressionsSectionHeight(advanced);

            var viewRect = new Rect(0f, 0f, contentWidth, Mathf.Max(viewHeight, inner.height));
            Widgets.BeginScrollView(inner, ref rightScroll, viewRect);
            float y = 0f;
            AttributeRule toDelete = null;
            pendingTreeOp = null;

            for (int i = 0; i < groups.Count; i++)
                DrawScopeGroup(groups[i], viewRect.width, ref y, ref toDelete);

            if (emptyText != null)
            {
                DrawFaded(new Rect(0f, y, viewRect.width, emptyHeight), emptyText, TextAnchor.UpperLeft);
                y += emptyHeight;
            }

            y += expressionsGap;
            DrawExpressionsSection(viewRect.width, ref y, advanced);

            Widgets.EndScrollView();

            if (toDelete != null)
            {
                working.rules.Remove(toDelete);
                valueBuffers.Remove(toDelete);
            }
            pendingTreeOp?.Invoke();
            pendingTreeOp = null;
        }

        // The Weapon group shows only while its universe is active, so a saved weapon rule stays hidden
        // but preserved when Auto Arm is absent rather than surfacing in an apparel scope.
        private static bool IsApparelGlobal(AttributeRule r)
            => r.layerScope == null && !r.exceptUtility && !r.utilityOnly && !r.weaponScope;

        private static bool RuleVisible(AttributeRule r) => AttributeCache.WeaponsActive || !r.weaponScope;

        private List<ScopeGroup> ScopeGroups()
        {
            var groups = new List<ScopeGroup>();
            ScopeGroup globalGroup = null;
            ScopeGroup exceptUtilGroup = null;
            ScopeGroup utilityOnlyGroup = null;
            ScopeGroup weaponGroup = null;
            Dictionary<ApparelLayerDef, ScopeGroup> layerGroups = null;

            bool weaponsActive = AttributeCache.WeaponsActive;
            for (int i = 0; i < working.rules.Count; i++)
            {
                AttributeRule r = working.rules[i];
                if (IsApparelGlobal(r))
                {
                    globalGroup ??= new ScopeGroup { key = "global", label = "APB.Global".Translate() };
                    globalGroup.rules.Add(r);
                }
                else if (r.exceptUtility)
                {
                    exceptUtilGroup ??= new ScopeGroup { key = "exceptutil", label = "APB.GlobalExceptUtility".Translate() };
                    exceptUtilGroup.rules.Add(r);
                }
                else if (r.utilityOnly)
                {
                    utilityOnlyGroup ??= new ScopeGroup { key = "utilityonly", label = "APB.UtilityOnly".Translate() };
                    utilityOnlyGroup.rules.Add(r);
                }
                else if (r.layerScope != null && !r.weaponScope)
                {
                    layerGroups ??= new Dictionary<ApparelLayerDef, ScopeGroup>();
                    if (!layerGroups.TryGetValue(r.layerScope, out ScopeGroup lg))
                    {
                        lg = new ScopeGroup { key = "layer:" + r.layerScope.defName, label = r.layerScope.LabelCap };
                        layerGroups[r.layerScope] = lg;
                    }
                    lg.rules.Add(r);
                }
                else if (weaponsActive && r.weaponScope)
                {
                    weaponGroup ??= new ScopeGroup { key = "weapon", label = "APB.WeaponScope".Translate() };
                    weaponGroup.rules.Add(r);
                }
            }

            if (globalGroup != null) groups.Add(globalGroup);
            if (exceptUtilGroup != null) groups.Add(exceptUtilGroup);
            if (utilityOnlyGroup != null) groups.Add(utilityOnlyGroup);
            if (layerGroups != null)
            {
                var sortedLayers = new List<ApparelLayerDef>(layerGroups.Keys);
                sortedLayers.Sort((a, b) => a.drawOrder.CompareTo(b.drawOrder));
                for (int i = 0; i < sortedLayers.Count; i++)
                    groups.Add(layerGroups[sortedLayers[i]]);
            }
            if (weaponGroup != null) groups.Add(weaponGroup);

            return groups;
        }

        private void DrawScopeGroup(ScopeGroup group, float width, ref float y, ref AttributeRule toDelete)
        {
            bool collapsed = collapsedScopes.Contains(group.key);
            int count = group.rules.Count;

            var headerRect = new Rect(0f, y, width, HeaderHeight);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            var iconRect = new Rect(headerRect.x + 2f, headerRect.y + (HeaderHeight - 16f) / 2f, 16f, 16f);
            GUI.DrawTexture(iconRect, collapsed ? TexButton.Plus : TexButton.Minus);
            Widgets.Label(new Rect(headerRect.x + 22f, headerRect.y, headerRect.width - 22f, headerRect.height),
                $"{group.label} ({count})");
            if (Widgets.ButtonInvisible(headerRect))
            {
                if (!collapsedScopes.Remove(group.key)) collapsedScopes.Add(group.key);
            }
            y += HeaderHeight;

            if (collapsed) return;

            for (int i = 0; i < group.rules.Count; i++)
            {
                AttributeRule rule = group.rules[i];
                var band = new Rect(0f, y, width, RuleRowHeight);
                if (Mouse.IsOver(band)) Widgets.DrawHighlight(band);
                var rowRect = new Rect(12f, y, width - 12f, RuleRowHeight);
                if (DrawRuleRow(rowRect.ContractedBy(1f), rule)) toDelete = rule;
                y += RuleRowHeight;
            }
        }

        private bool DrawRuleRow(Rect row, AttributeRule rule)
        {
            const float gap = 4f, iconW = 18f, valueCol = 170f, fieldW = 48f;
            float x = row.x;

            Rect Slice(float w)
            {
                var r = new Rect(x, row.y, w, row.height);
                x += w + gap;
                return r;
            }

            var iconSlot = Slice(iconW);
            if (rule.kind == RuleAttributeKind.Quality || rule.kind == RuleAttributeKind.HitPoints)
            {
                DrawGlyph(iconSlot, rule.rangeBound == RangeBound.AtLeast ? "≥" : "≤");
                bool toggle = Widgets.ButtonInvisible(iconSlot);
                if (Widgets.ButtonText(Slice(74f), ("APB.Bound." + rule.rangeBound).Translate().CapitalizeFirst()))
                    toggle = true;
                if (toggle)
                    rule.rangeBound = rule.rangeBound == RangeBound.AtLeast ? RangeBound.AtMost : RangeBound.AtLeast;
            }
            else
            {
                bool allow = rule.polarity == RulePolarity.Require;
                DrawStateIcon(iconSlot, allow ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex);
                bool toggle = Widgets.ButtonInvisible(iconSlot);
                string label = rule.kind == RuleAttributeKind.SpecialFilter
                    ? (allow ? "APB.Special.Allow" : "APB.Special.Disallow").Translate()
                    : ("APB.Polarity." + rule.polarity).Translate();
                if (Widgets.ButtonText(Slice(74f), label))
                    toggle = true;
                if (toggle)
                    rule.polarity = allow ? RulePolarity.Forbid : RulePolarity.Require;
            }

            if (rule.IsPerDef && !rule.weaponScope)
            {
                var scopeSlot = Slice(26f);
                TooltipHandler.TipRegion(scopeSlot, ScopeLabel(rule));
                if (Widgets.ButtonText(scopeSlot, "≡"))
                    OpenScopeMenu(rule);
            }
            else
            {
                // Weapon rules can't cross into apparel scopes; facets are always Global.
                var scopeSlot = Slice(26f);
                TooltipHandler.TipRegionByKey(scopeSlot, rule.weaponScope ? "APB.WeaponScopeTip" : "APB.FacetScopeTip");
                DrawFaded(scopeSlot, "≡", TextAnchor.MiddleCenter);
            }

            var deleteRect = new Rect(row.xMax - 24f, row.y, 24f, row.height);
            bool delete = Widgets.ButtonImage(deleteRect, TexButton.Delete);

            // A fixed value column so the value controls line up across rows.
            float valueX = deleteRect.x - gap - valueCol;
            var nameRect = new Rect(x, row.y, Mathf.Max(valueX - gap - x, 40f), row.height);
            var valueRect = new Rect(valueX, row.y, valueCol, row.height);

            switch (rule.kind)
            {
                case RuleAttributeKind.Numeric:
                    RowLabel(nameRect, rule.stat?.LabelCap ?? "?");
                    if (rule.stat != null && !rule.stat.description.NullOrEmpty())
                        TooltipHandler.TipRegion(nameRect, rule.stat.description);
                    // The "%" suffix comes out of the mode button so the entry box keeps room for a signed decimal.
                    float suffixW = PercentEntry.Applies(rule.stat) ? PercentSuffixWidth : 0f;
                    float modeW = valueCol - gap - fieldW - suffixW;
                    if (Widgets.ButtonText(new Rect(valueX, row.y, modeW, row.height),
                            ("APB.Mode." + rule.numericMode).Translate().CapitalizeFirst()))
                        OpenModeMenu(rule);
                    if (rule.NeedsThreshold)
                        DrawThresholdField(new Rect(valueX + modeW + gap, row.y, fieldW + suffixW, row.height), rule);
                    break;
                case RuleAttributeKind.Categorical:
                    RowLabel(nameRect, OptionFor(rule)?.label ?? rule.attrKey);
                    if (Widgets.ButtonText(valueRect, CategoricalValueLabel(rule)))
                        OpenCategoricalMenu(rule);
                    break;
                case RuleAttributeKind.Material:
                    RowLabel(nameRect, "APB.Facet.Material".Translate());
                    if (Widgets.ButtonText(valueRect, rule.materialStuff?.LabelCap ?? "APB.Pick".Translate()))
                        OpenMaterialMenu(rule);
                    break;
                case RuleAttributeKind.Quality:
                    RowLabel(nameRect, "APB.Facet.Quality".Translate());
                    if (Widgets.ButtonText(valueRect, rule.qualityValue.GetLabel().CapitalizeFirst()))
                        OpenQualityMenu(rule);
                    break;
                case RuleAttributeKind.HitPoints:
                    RowLabel(nameRect, "APB.Facet.HitPoints".Translate());
                    DrawPercentField(DrawPercentSuffix(valueRect), rule);
                    break;
                case RuleAttributeKind.SpecialFilter:
                    RowLabel(new Rect(x, row.y, Mathf.Max(deleteRect.x - gap - x, 40f), row.height),
                        CleanSpecialFilterLabel(rule.specialFilter));
                    break;
            }

            return delete;
        }

        private void DrawThresholdField(Rect rect, AttributeRule rule)
        {
            valueBuffers.TryGetValue(rule, out string buffer);
            DrawThreshold(rect, rule.stat, ref rule.threshold, ref buffer);
            valueBuffers[rule] = buffer;
        }

        private static void DrawThreshold(Rect rect, StatDef stat, ref float threshold, ref string buffer)
        {
            if (!PercentEntry.Applies(stat))
            {
                buffer ??= threshold.ToString("0.###");
                Widgets.TextFieldNumeric(rect, ref threshold, ref buffer, -1e9f, 1e9f);
                return;
            }

            Rect field = DrawPercentSuffix(rect);
            float pct = threshold * 100f;
            buffer ??= PercentEntry.Display(threshold, stat);
            Widgets.TextFieldNumeric(field, ref pct, ref buffer, -1e9f, 1e9f);
            threshold = pct / 100f;
        }

        // Vanilla's own hit-points filter is whole-percent clamped 0-1, so this one rounds where DrawThreshold must not.
        private void DrawPercentField(Rect rect, AttributeRule rule)
        {
            float pct = Mathf.Round(rule.threshold * 100f);
            if (!valueBuffers.TryGetValue(rule, out string buffer)) buffer = pct.ToString("0");
            Widgets.TextFieldNumeric(rect, ref pct, ref buffer, 0f, 100f);
            rule.threshold = pct / 100f;
            valueBuffers[rule] = buffer;
        }

        private static Rect DrawPercentSuffix(Rect slot)
        {
            RowLabel(new Rect(slot.xMax - PercentSuffixWidth + 2f, slot.y, PercentSuffixWidth, slot.height), "%");
            return new Rect(slot.x, slot.y, slot.width - PercentSuffixWidth, slot.height);
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
            Widgets.Label(rect, (text ?? "").Truncate(rect.width));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawStateIcon(Rect slot, Texture2D tex)
        {
            const float s = 18f;
            GUI.DrawTexture(new Rect(slot.x, slot.y + (slot.height - s) / 2f, s, s), tex);
        }

        private static void DrawGlyph(Rect slot, string glyph)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(slot, "<b>" + glyph + "</b>");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
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
                working.ApplyTo(policy);
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
            => RulesetStore.Set(policy, working.Clone());

        public bool WorkingIsEmpty => working.rules.Count == 0;
        public Ruleset WorkingSnapshot() => working.Clone();
        public string PolicyLabel => policy?.label ?? "";

        public void LoadFromDocument(RuleDocument doc)
        {
            if (doc == null) return;
            Ruleset rs = doc.ToRuleset(out int skipped);
            Action after = skipped > 0
                ? () => Messages.Message("APB.SkippedRules".Translate(skipped), MessageTypeDefOf.CautionInput, false)
                : (Action)null;
            ReplaceWorkingConfirmed(rs, "APB.DiscardLoadConfirm".Translate(doc.name), after);
        }

        private void ReplaceWorkingConfirmed(Ruleset next, TaggedString confirmText, Action after = null)
        {
            if (next == null) return;
            void Apply()
            {
                working = next;
                valueBuffers.Clear();
                after?.Invoke();
            }
            if (working.rules.Count > 0)
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmText, Apply));
            else Apply();
        }

        // ---- Rule construction / menus ----

        private void AddRule(AttributeOption opt)
        {
            // Only per-def rules carry the Weapon scope; facets and special filters stay Global.
            bool weapon = WeaponMode && IsPerDefOption(opt);
            var rule = new AttributeRule { kind = opt.kind, weaponScope = weapon };
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
            collapsedScopes.Remove(weapon ? "weapon" : "global");
        }

        private static AttributeOption OptionFor(AttributeRule rule) => AttributeCache.OptionFor(rule.attrKey, rule.weaponScope);

        private static string CategoricalValueLabel(AttributeRule rule)
        {
            AttributeOption opt = OptionFor(rule);
            CategoricalValue v = opt?.values.FirstOrDefault(cv => cv.token == rule.categoricalValue);
            return v?.label ?? rule.categoricalValue ?? "APB.Pick".Translate();
        }

        private string ScopeLabel(AttributeRule rule)
            => rule.utilityOnly ? "APB.UtilityOnly".Translate().ToString()
               : rule.exceptUtility ? "APB.GlobalExceptUtility".Translate().ToString()
               : rule.layerScope != null ? rule.layerScope.LabelCap.ToString()
               : "APB.Global".Translate().ToString();

        private void OpenScopeMenu(AttributeRule rule)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("APB.Global".Translate(), () => { rule.layerScope = null; rule.exceptUtility = false; rule.utilityOnly = false; collapsedScopes.Remove("global"); }),
                new FloatMenuOption("APB.GlobalExceptUtility".Translate(), () => { rule.layerScope = null; rule.exceptUtility = true; rule.utilityOnly = false; collapsedScopes.Remove("exceptutil"); }),
                new FloatMenuOption("APB.UtilityOnly".Translate(), () => { rule.layerScope = null; rule.exceptUtility = false; rule.utilityOnly = true; collapsedScopes.Remove("utilityonly"); })
            };
            foreach (ApparelLayerDef layer in AttributeCache.Layers)
            {
                ApparelLayerDef captured = layer;
                options.Add(new FloatMenuOption(captured.LabelCap,
                    () => { rule.layerScope = captured; rule.exceptUtility = false; rule.utilityOnly = false; collapsedScopes.Remove("layer:" + captured.defName); }));
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
                Mode(MaterialLensMode.Highest),
                Mode(MaterialLensMode.Typical),
                Mode(MaterialLensMode.Lowest),
                Mode(MaterialLensMode.Multiplier)
            };
            foreach (ThingDef material in AttributeCache.StuffMaterials)
            {
                ThingDef captured = material;
                options.Add(new FloatMenuOption(material.LabelCap, () =>
                {
                    working.evalMode = MaterialLensMode.Material;
                    working.evalStuff = captured;
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));

            FloatMenuOption Mode(MaterialLensMode m)
                => new FloatMenuOption(LensLabel(m, null), () =>
                {
                    working.evalMode = m;
                    working.evalStuff = null;
                });
        }

        private static string LensLabel(MaterialLensMode mode, ThingDef stuff)
        {
            switch (mode)
            {
                case MaterialLensMode.Lowest: return "APB.Lens.Lowest".Translate();
                case MaterialLensMode.Typical: return "APB.Lens.Typical".Translate();
                case MaterialLensMode.Highest: return "APB.Lens.Highest".Translate();
                case MaterialLensMode.Material when stuff != null: return stuff.LabelCap;
                default: return "APB.Multiplier".Translate();
            }
        }
    }
}
