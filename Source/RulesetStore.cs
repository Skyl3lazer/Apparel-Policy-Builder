using RimWorld;
using Verse;

namespace ApparelPolicyBuilder
{
    // Colony policies are keyed by id in the save, foreign ones by label in mod settings.
    public static class RulesetStore
    {
        public static bool IsColonyOwned(ApparelPolicy policy)
            => policy != null && (Current.Game?.outfitDatabase?.AllOutfits.Contains(policy) ?? false);

        public static Ruleset Get(ApparelPolicy policy)
        {
            if (policy == null) return null;
            return IsColonyOwned(policy)
                ? GameComponent_ApparelPolicyBuilder.Instance?.GetRuleset(policy)
                : ApparelPolicyBuilderMod.GetForeignRuleset(policy.label);
        }

        public static void Set(ApparelPolicy policy, Ruleset ruleset)
        {
            if (policy == null) return;
            if (IsColonyOwned(policy)) GameComponent_ApparelPolicyBuilder.Instance?.Store(policy, ruleset);
            else ApparelPolicyBuilderMod.SaveForeignRuleset(policy.label, ruleset);
        }
    }
}
