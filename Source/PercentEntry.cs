using RimWorld;
using Verse;

namespace ApparelPolicyBuilder
{
    // A threshold is authored in the units the stat's own info card shows, so 5% is typed as 5 rather than 0.05.
    public static class PercentEntry
    {
        public static bool Applies(StatDef stat)
        {
            switch (stat?.toStringStyle)
            {
                case ToStringStyle.PercentZero:
                case ToStringStyle.PercentOne:
                case ToStringStyle.PercentTwo:
                    return true;
                default:
                    return false;
            }
        }

        // Carries the style's own precision, so seeding the box cannot round a saved 72.5% armor threshold to 73%.
        public static string Display(float threshold, StatDef stat)
            => (threshold * 100f).ToString(Format(stat));

        private static string Format(StatDef stat)
        {
            switch (stat?.toStringStyle)
            {
                case ToStringStyle.PercentOne: return "0.#";
                case ToStringStyle.PercentTwo: return "0.##";
                default: return "0";
            }
        }
    }
}
