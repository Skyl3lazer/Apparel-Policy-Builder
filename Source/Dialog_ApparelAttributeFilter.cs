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

        private readonly ApparelPolicy policy;
        private Ruleset working;
        private readonly List<KeyValuePair<StatCategoryDef, List<StatDef>>> categorizedStats;

        private string searchText = "";
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private readonly Dictionary<AttributeRule, string> thresholdBuffers = new Dictionary<AttributeRule, string>();

        public override Vector2 InitialSize => new Vector2(940f, 590f);

        public Dialog_ApparelAttributeFilter(ApparelPolicy policy)
        {
            this.policy = policy;
            AttributeCache.EnsureBuilt();

            Ruleset stored = GameComponent_ApparelAttributeFilter.Instance?.GetRuleset(policy);
            working = stored != null ? stored.Clone() : new Ruleset();

            categorizedStats = AttributeCache.NumericAttributes
                .GroupBy(s => s.category)
                .OrderBy(g => g.Key?.displayOrder ?? int.MaxValue)
                .Select(g => new KeyValuePair<StatCategoryDef, List<StatDef>>(g.Key, g.ToList()))
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
            var rightRect = new Rect(leftRect.xMax + PanelGap, contentTop,
                inRect.width - LeftPanelWidth - PanelGap, contentBottom - contentTop);

            DrawLeftPanel(leftRect);
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

            bool coversVisible = Matches("AAF.Covers".Translate());
            float viewHeight = (coversVisible ? RowHeight : 0f);
            foreach (var group in categorizedStats)
            {
                int visible = group.Value.Count(CoreMatches);
                if (visible == 0) continue;
                viewHeight += HeaderHeight + visible * RowHeight;
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

            foreach (var group in categorizedStats)
            {
                List<StatDef> visibleStats = group.Value.Where(CoreMatches).ToList();
                if (visibleStats.Count == 0) continue;

                var headerRect = new Rect(0f, y, viewRect.width, HeaderHeight);
                Color prevHeader = GUI.color;
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(headerRect, (group.Key?.LabelCap ?? "AAF.OtherCategory".Translate()).ToString());
                GUI.color = prevHeader;
                y += HeaderHeight;

                foreach (StatDef stat in visibleStats)
                {
                    var statRect = new Rect(0f, y, viewRect.width, RowHeight);
                    if (Mouse.IsOver(statRect)) Widgets.DrawHighlight(statRect);
                    if (!stat.description.NullOrEmpty()) TooltipHandler.TipRegion(statRect, stat.description);
                    Widgets.Label(statRect, stat.LabelCap);
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
            if (hasGlobal) viewHeight += HeaderHeight + working.rules.Count(r => r.layerScope == null) * RuleRowHeight;
            foreach (ApparelLayerDef layer in scopeLayers)
                viewHeight += HeaderHeight + working.rules.Count(r => r.layerScope == layer) * RuleRowHeight;

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
            var headerRect = new Rect(0f, y, width, HeaderHeight);
            Widgets.Label(headerRect, label);
            y += HeaderHeight;

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
                working.ApplyTo(policy);
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
    }
}
