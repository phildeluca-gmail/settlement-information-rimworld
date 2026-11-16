using Verse;

namespace SettlementInformation.Core
{
    // Saved mod settings. One switch: the caravan list on the left edge.
    public class SettlementInformationSettings : ModSettings
    {
        public bool ShowCaravanList = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ShowCaravanList, "showCaravanList", true);
        }
    }
}
