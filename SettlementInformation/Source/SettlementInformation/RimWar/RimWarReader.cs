using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace SettlementInformation.RimWar
{
    // Everything that touches Rim War lives here and nowhere else. Its DLL
    // never enters lib\ and is not a modDependency. Same pattern as
    // RimWarOdds/Compare/RimWarReader.cs.
    //
    // Verified by RimWar Odds against RimWar.dll 2026-08-30:
    //   RimWar.Planet.RimWarSettlementComp : WorldObjectComp
    //       int EffectivePoints (property)
    // We read EffectivePoints, same choice as rwo-architecture.md section 3.
    // For a settlement it is a spending budget, not a garrison.
    public static class RimWarReader
    {
        public static bool Available { get; private set; }
        public static string Status { get; private set; } = "Rim War not resolved yet.";

        private static bool resolved;
        private static bool failureLogged;
        private static Type compType;
        private static PropertyInfo effective;

        public static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;

            compType = AccessTools.TypeByName("RimWar.Planet.RimWarSettlementComp");
            if (compType == null)
            {
                Status = "Rim War not found, strength line left out.";
                return;
            }

            effective = AccessTools.Property(compType, "EffectivePoints");
            if (effective == null)
            {
                // half-resolved is worse than none
                Status = "Rim War found but RimWarSettlementComp.EffectivePoints did not resolve (version mismatch?), strength line left out.";
                Log.Warning("[SettlementInformation] " + Status);
                return;
            }

            Available = true;
            Status = "Rim War found, RimWarSettlementComp.EffectivePoints resolved.";
        }

        // null = this settlement has no rating (caller prints a dash)
        public static int? ReadStrength(WorldObject obj)
        {
            if (!Available || obj == null)
            {
                return null;
            }

            try
            {
                var comp = obj.GetComponent(compType);
                if (comp == null)
                {
                    return null;
                }
                return (int)effective.GetValue(comp, null);
            }
            catch (Exception e)
            {
                // one line ever, not one per settlement per refresh
                if (!failureLogged)
                {
                    failureLogged = true;
                    Log.Warning("[SettlementInformation] Reading Rim War strength threw, showing dashes from now on: " + e.Message);
                }
                return null;
            }
        }
    }
}
