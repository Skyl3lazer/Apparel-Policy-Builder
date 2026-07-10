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

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref documents, "documents", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && documents == null)
                documents = new List<RuleDocument>();
        }
    }

    public class ApparelPolicyBuilderMod : Mod
    {
        private static ApparelPolicyBuilderMod instance;
        private readonly ApparelPolicyBuilderSettings settings;

        public ApparelPolicyBuilderMod(ModContentPack content) : base(content)
        {
            instance = this;
            settings = GetSettings<ApparelPolicyBuilderSettings>();
        }

        public override string SettingsCategory() => "Apparel Policy Builder";

        public override void DoSettingsWindowContents(Rect inRect)
            => Widgets.Label(inRect, "APB.SettingsNote".Translate());

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
    }
}
