using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ApparelPolicyBuilder
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var harmony = new Harmony("Skyl3lazer.ApparelPolicyBuilder");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            AttributeCache.EnsureBuilt();
        }
    }

    [HarmonyPatch(typeof(Dialog_ManagePolicies<ApparelPolicy>), "DoWindowContents")]
    public static class Patch_ApparelPolicyDialog_Button
    {
        private const float ButtonWidth = 168f;
        private const float ButtonHeight = 28f;

        // DoWindowContents is shared across every Dialog_ManagePolicies<T> under Mono, so gate on the apparel dialog.
        public static void Postfix(Rect inRect, object __instance, object ___policyInt)
        {
            if (!(__instance is Dialog_ManageApparelPolicies) || !(___policyInt is ApparelPolicy policy)) return;

            var buttonRect = new Rect(inRect.xMax - ButtonWidth, inRect.y + 2f, ButtonWidth, ButtonHeight);
            TooltipHandler.TipRegionByKey(buttonRect, "APB.OpenFilterTip");
            if (!Widgets.ButtonText(buttonRect, "APB.OpenFilter".Translate())) return;

            var existing = Find.WindowStack.WindowOfType<Dialog_ApparelPolicyBuilder>();
            if (existing != null) existing.Close();
            else Find.WindowStack.Add(new Dialog_ApparelPolicyBuilder(policy));
        }
    }

    // Vanilla's copy-policy duplicates only the filter; carry the source's rules onto the new policy too.
    [HarmonyPatch(typeof(ApparelPolicy), nameof(ApparelPolicy.CopyFrom))]
    public static class Patch_ApparelPolicy_CopyFrom
    {
        public static void Postfix(ApparelPolicy __instance, Policy other)
        {
            if (!(other is ApparelPolicy source)) return;
            GameComponent_ApparelPolicyBuilder gc = GameComponent_ApparelPolicyBuilder.Instance;
            if (gc == null) return;
            Ruleset rs = gc.GetRuleset(source);
            if (rs != null && !rs.IsEmpty) gc.Store(__instance, rs.Clone());
        }
    }
}
