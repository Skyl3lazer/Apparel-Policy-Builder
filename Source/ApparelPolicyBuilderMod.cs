using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ApparelPolicyBuilder
{
    public class ApparelPolicyBuilderSettings : ModSettings
    {
        public List<RuleDocument> documents = new List<RuleDocument>();
        public Dictionary<string, bool> layerUtilityOverrides = new Dictionary<string, bool>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref documents, "documents", LookMode.Deep);
            Scribe_Collections.Look(ref layerUtilityOverrides, "layerUtilityOverrides", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (documents == null) documents = new List<RuleDocument>();
                if (layerUtilityOverrides == null) layerUtilityOverrides = new Dictionary<string, bool>();
            }
        }
    }

    public class ApparelPolicyBuilderMod : Mod
    {
        private static ApparelPolicyBuilderMod instance;
        private static readonly Dictionary<string, bool> noOverrides = new Dictionary<string, bool>();
        private readonly ApparelPolicyBuilderSettings settings;
        private Vector2 settingsScroll;

        public ApparelPolicyBuilderMod(ModContentPack content) : base(content)
        {
            instance = this;
            settings = GetSettings<ApparelPolicyBuilderSettings>();
        }

        public override string SettingsCategory() => "Apparel Policy Builder";

        public override void WriteSettings()
        {
            base.WriteSettings();
            AttributeRule.InvalidateUtilityLayers();
        }

        // User overrides of which layers count as utility, layered over the shipped UtilityLayerExtension defaults.
        public static Dictionary<string, bool> LayerUtilityOverrides
            => instance?.settings.layerUtilityOverrides ?? noOverrides;

        public static List<RuleDocument> Documents
            => instance?.settings.documents ?? new List<RuleDocument>();

        public static RuleDocument FindDocument(string name)
            => instance?.settings.documents.FirstOrDefault(d => Matches(d, name));

        public static void SaveDocument(string name, Ruleset rs)
        {
            if (instance == null || name.NullOrEmpty()) return;
            List<RuleDocument> docs = instance.settings.documents;
            docs.RemoveAll(d => Matches(d, name));
            docs.Add(RuleDocument.From(name, rs));
            docs.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            instance.WriteSettings();
        }

        public static void DeleteDocument(string name)
        {
            if (instance == null) return;
            instance.settings.documents.RemoveAll(d => Matches(d, name));
            instance.WriteSettings();
        }

        private static bool Matches(RuleDocument d, string name)
            => string.Equals(d.name, name, StringComparison.OrdinalIgnoreCase);

        // ---- Utility-layer settings ----

        public override void DoSettingsWindowContents(Rect inRect)
        {
            AttributeCache.EnsureBuilt();
            Dictionary<string, bool> overrides = settings.layerUtilityOverrides;

            var noteRect = new Rect(inRect.x, inRect.y, inRect.width, 44f);
            Widgets.Label(noteRect, "APB.Settings.UtilityNote".Translate());

            const float resetW = 190f, resetH = 30f;
            var resetRect = new Rect(inRect.xMax - resetW, noteRect.yMax + 2f, resetW, resetH);
            if (Widgets.ButtonText(resetRect, "APB.Settings.ResetAll".Translate(), active: overrides.Count > 0))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "APB.Settings.ResetConfirm".Translate(),
                    () => { overrides.Clear(); WriteSettings(); }, destructive: true));

            var listRect = new Rect(inRect.x, resetRect.yMax + 8f, inRect.width, inRect.yMax - resetRect.yMax - 8f);
            DrawLayerList(listRect, overrides);
        }

        private void DrawLayerList(Rect rect, Dictionary<string, bool> overrides)
        {
            const float headerH = 26f, rowH = 24f;
            List<LayerGroup> groups = LayerGroups();

            float viewH = 0f;
            foreach (LayerGroup g in groups) viewH += headerH + g.layers.Count * rowH;
            var viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(viewH, rect.height));

            Widgets.BeginScrollView(rect, ref settingsScroll, viewRect);
            float y = 0f;
            foreach (LayerGroup g in groups)
            {
                var headerRect = new Rect(0f, y, viewRect.width, headerH);
                Color prev = GUI.color;
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(headerRect, g.label);
                GUI.color = prev;
                y += headerH;

                foreach (ApparelLayerDef layer in g.layers)
                {
                    var rowRect = new Rect(12f, y, viewRect.width - 12f, rowH);
                    bool shippedDefault = layer.HasModExtension<UtilityLayerExtension>();
                    bool current = overrides.TryGetValue(layer.defName, out bool forced) ? forced : shippedDefault;
                    bool overridden = current != shippedDefault;

                    string label = layer.LabelCap;
                    if (overridden) label += " *";
                    bool next = current;
                    Widgets.CheckboxLabeled(rowRect, label, ref next);
                    if (next != current)
                    {
                        if (next == shippedDefault) overrides.Remove(layer.defName);
                        else overrides[layer.defName] = next;
                        AttributeRule.InvalidateUtilityLayers();
                    }

                    string tip = layer.defName + "\n" + g.label;
                    if (overridden)
                        tip += "\n" + (shippedDefault ? "APB.Settings.DefaultUtility" : "APB.Settings.DefaultNormal").Translate();
                    TooltipHandler.TipRegion(rowRect, tip);

                    y += rowH;
                }
            }
            Widgets.EndScrollView();
        }

        private static List<LayerGroup> LayerGroups()
            => AttributeCache.Layers
                .GroupBy(SourceOf)
                .Select(gr => new LayerGroup
                {
                    order = gr.Key.order,
                    label = gr.Key.label,
                    layers = gr.OrderBy(l => l.LabelCap.ToString(), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(l => l.defName, StringComparer.OrdinalIgnoreCase).ToList()
                })
                .OrderBy(g => g.order).ThenBy(g => g.label, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static (int order, string label) SourceOf(ApparelLayerDef layer)
        {
            ModContentPack mcp = layer.modContentPack;
            if (mcp == null) return (2, layer.defName);
            if (mcp.IsCoreMod) return (0, "APB.Settings.Vanilla".Translate());
            if (mcp.IsOfficialMod) return (1, "APB.Settings.DLC".Translate());
            return (2, mcp.Name);
        }

        private sealed class LayerGroup
        {
            public int order;
            public string label;
            public List<ApparelLayerDef> layers;
        }
    }
}
