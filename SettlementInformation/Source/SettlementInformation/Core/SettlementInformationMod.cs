using UnityEngine;
using Verse;

namespace SettlementInformation.Core
{
    // Entry point and settings page. SettingsCategory must match the
    // About.xml name - CLAUDE.md section 0.
    public class SettlementInformationMod : Mod
    {
        public static SettlementInformationSettings Settings;

        public SettlementInformationMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SettlementInformationSettings>();
            // wait until every mod's assemblies are surely loaded before
            // looking for Rim War's types
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                RimWar.RimWarReader.Resolve();
                Vehicles.VehicleFrameworkReader.Resolve();
                // the one startup line; nothing else in the mod logs routinely
                Log.Message("[SettlementInformation] Loaded. " + RimWar.RimWarReader.Status + " " + Vehicles.VehicleFrameworkReader.Status);
            });
        }

        public override string SettingsCategory()
        {
            return "Ketjak's Settlement Information";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);
            bool value = Settings.ShowCaravanList;
            list.CheckboxLabeled("Show caravan list", ref value);
            list.End();
            if (value != Settings.ShowCaravanList)
            {
                Settings.ShowCaravanList = value;
                Log.Message("[SettlementInformation] Setting changed: Show caravan list = " + value);
            }
        }
    }
}
