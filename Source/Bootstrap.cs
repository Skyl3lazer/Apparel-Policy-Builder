using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelAttributeFilter
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var harmony = new Harmony("Skyl3lazer.ApparelAttributeFilter");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            AttributeCache.EnsureBuilt();
        }
    }

    [HarmonyPatch(typeof(Dialog_ManagePolicies<ApparelPolicy>), "DoWindowContents")]
    public static class Patch_ApparelPolicyDialog_Button
    {
        private const float ButtonWidth = 168f;
        private const float ButtonHeight = 28f;

        public static void Postfix(Rect inRect, ApparelPolicy ___policyInt)
        {
            if (___policyInt == null) return;

            var buttonRect = new Rect(inRect.xMax - ButtonWidth, inRect.y + 2f, ButtonWidth, ButtonHeight);
            TooltipHandler.TipRegionByKey(buttonRect, "AAF.OpenFilterTip");
            if (!Widgets.ButtonText(buttonRect, "AAF.OpenFilter".Translate())) return;

            var existing = Find.WindowStack.WindowOfType<Dialog_ApparelAttributeFilter>();
            if (existing != null) existing.Close();
            else Find.WindowStack.Add(new Dialog_ApparelAttributeFilter(___policyInt));
        }
    }
}
