using Verse;

namespace SettlementInformation.Core
{
    // Entry point. No settings, so no SettingsCategory override (an override
    // would list an empty page under Mod options). If one is ever added it
    // must return "Ketjak's Settlement Information" - CLAUDE.md section 0.
    public class SettlementInformationMod : Mod
    {
        public SettlementInformationMod(ModContentPack content) : base(content)
        {
            // wait until every mod's assemblies are surely loaded before
            // looking for Rim War's types
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                RimWar.RimWarReader.Resolve();
                // the one startup line; nothing else in the mod logs routinely
                Log.Message("[SettlementInformation] Loaded. " + RimWar.RimWarReader.Status);
            });
        }
    }
}
