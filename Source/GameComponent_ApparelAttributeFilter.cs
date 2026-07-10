using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ApparelAttributeFilter
{
    public class GameComponent_ApparelAttributeFilter : GameComponent
    {
        private Dictionary<int, Ruleset> rulesets = new Dictionary<int, Ruleset>(); // keyed by ApparelPolicy.id

        public GameComponent_ApparelAttributeFilter(Game game) { }

        public Ruleset GetRuleset(ApparelPolicy policy)
            => policy != null && rulesets.TryGetValue(policy.id, out Ruleset r) ? r : null;

        public void Store(ApparelPolicy policy, Ruleset ruleset)
        {
            if (policy == null) return;
            if (ruleset == null || ruleset.IsEmpty) rulesets.Remove(policy.id);
            else rulesets[policy.id] = ruleset;
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) Prune();
            // Key bumped so pre-v2 rulesets (different rule schema) are dropped rather than misread.
            Scribe_Collections.Look(ref rulesets, "rulesetsV2", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && rulesets == null)
                rulesets = new Dictionary<int, Ruleset>();
        }

        private void Prune()
        {
            OutfitDatabase db = Current.Game?.outfitDatabase;
            if (db == null) return;
            var live = new HashSet<int>(db.AllOutfits.Select(p => p.id));
            List<int> dead = rulesets.Keys.Where(id => !live.Contains(id)).ToList();
            foreach (int id in dead) rulesets.Remove(id);
        }

        public static GameComponent_ApparelAttributeFilter Instance
            => Current.Game?.GetComponent<GameComponent_ApparelAttributeFilter>();
    }
}
