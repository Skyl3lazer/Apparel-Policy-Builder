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
        }
    }

    // Not DoWindowContents: Mono shares one body across all Dialog_ManagePolicies<T>, so only the first mod to patch one wins.
    [HarmonyPatch(typeof(Dialog_ManageApparelPolicies), "DoContentsRect")]
    public static class Patch_ApparelPolicyDialog_Button
    {
        private const float ButtonWidth = 168f;
        private const float ButtonHeight = 28f;
        private const float CloseXClearance = 24f;
        private const float TitleRowY = 2f;

        public static void Postfix(Rect rect, ApparelPolicy ___policyInt)
        {
            if (___policyInt == null) return;

            // rect shares the window's right edge, and the dialog draws in a zero-origin group.
            var buttonRect = new Rect(rect.xMax - ButtonWidth - CloseXClearance, TitleRowY, ButtonWidth, ButtonHeight);
            TooltipHandler.TipRegionByKey(buttonRect, "APB.OpenFilterTip");
            if (!Widgets.ButtonText(buttonRect, "APB.OpenFilter".Translate())) return;

            var existing = Find.WindowStack.WindowOfType<Dialog_ApparelPolicyBuilder>();
            if (existing != null) existing.Close();
            else Find.WindowStack.Add(new Dialog_ApparelPolicyBuilder(___policyInt));
        }
    }

    // Outfit ids are reused by MakeNewOutfit, so a deleted policy's rules would bleed into the next new one.
    [HarmonyPatch(typeof(OutfitDatabase), nameof(OutfitDatabase.TryDelete))]
    public static class Patch_OutfitDatabase_TryDelete
    {
        public static void Postfix(ApparelPolicy apparelPolicy, AcceptanceReport __result)
        {
            if (__result.Accepted) GameComponent_ApparelPolicyBuilder.Instance?.Remove(apparelPolicy);
        }
    }

    // Vanilla's copy-policy duplicates only the filter; carry the source's rules onto the new policy too.
    [HarmonyPatch(typeof(ApparelPolicy), nameof(ApparelPolicy.CopyFrom))]
    public static class Patch_ApparelPolicy_CopyFrom
    {
        public static void Postfix(ApparelPolicy __instance, Policy other)
        {
            // Colony-owned source only. Seeding a new colony copies the other way, before there is a game to read.
            if (!(other is ApparelPolicy source) || !RulesetStore.IsColonyOwned(source)) return;
            Ruleset rs = RulesetStore.Get(source);
            if (rs != null && !rs.IsEmpty) RulesetStore.Set(__instance, rs.Clone());
        }
    }
}
