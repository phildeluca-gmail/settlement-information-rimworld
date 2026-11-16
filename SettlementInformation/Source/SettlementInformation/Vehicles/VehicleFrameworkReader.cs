using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace SettlementInformation.Vehicles
{
    // Everything that touches Vehicle Framework lives here and nowhere else.
    // Vehicles.dll never enters lib\ and is not a modDependency; no VF type
    // is named at compile time.
    //
    // Verified by reflection on VF 1.5 Vehicles.dll, 2026-11-15:
    //   Vehicles.VehicleCaravan : RimWorld.Planet.Caravan
    //       field vehiclePather : VehicleCaravan_PathFollower
    //       property VehiclesListForReading : List<VehiclePawn>
    //   Vehicles.VehicleCaravan_PathFollower
    //       properties Moving, Paused, Destination (int tile)
    //   Vehicles.AerialVehicleInFlight : DynamicDrawnWorldObject : WorldObject
    //       property IsPlayerControlled; field vehicle : VehiclePawn;
    //       field flightPath : FlightPath (property Empty, property Last : FlightNode)
    //       FlightNode is a struct with field tile (int)
    //   Vehicles.VehiclePawn property AllPawnsAboard : List<Pawn>
    // Not verified in a running game.
    public static class VehicleFrameworkReader
    {
        public static bool Available { get; private set; }
        public static string Status { get; private set; } = "Vehicle Framework not resolved yet.";

        private static bool resolved;
        private static Type caravanType;
        private static Type aerialType;
        private static FieldInfo vehiclePatherField;
        private static PropertyInfo patherMoving;
        private static PropertyInfo patherPaused;
        private static PropertyInfo patherMovingNow;
        private static PropertyInfo patherDestination;
        private static PropertyInfo vehiclesList;
        private static PropertyInfo aerialPlayerControlled;
        private static FieldInfo aerialVehicle;
        private static FieldInfo aerialFlightPath;
        private static PropertyInfo pathEmpty;
        private static PropertyInfo pathLast;
        private static FieldInfo nodeTile;
        private static PropertyInfo pawnsAboard;
        private static Type vehiclePawnType;
        private static readonly HashSet<Pawn> countScratch = new HashSet<Pawn>();
        private static bool failureLogged;

        public static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;

            vehiclePawnType = AccessTools.TypeByName("Vehicles.VehiclePawn");
            caravanType = AccessTools.TypeByName("Vehicles.VehicleCaravan");
            aerialType = AccessTools.TypeByName("Vehicles.AerialVehicleInFlight");
            if (caravanType == null && aerialType == null)
            {
                Status = "Vehicle Framework not found, only ordinary caravans listed.";
                return;
            }

            string missing = "";
            if (caravanType != null)
            {
                vehiclePatherField = AccessTools.Field(caravanType, "vehiclePather");
                vehiclesList = AccessTools.Property(caravanType, "VehiclesListForReading");
                if (vehiclePatherField != null)
                {
                    Type pf = vehiclePatherField.FieldType;
                    patherMoving = AccessTools.Property(pf, "Moving");
                    patherPaused = AccessTools.Property(pf, "Paused");
                    patherMovingNow = AccessTools.Property(pf, "MovingNow");
                    patherDestination = AccessTools.Property(pf, "Destination");
                }
                if (vehiclePatherField == null || patherMoving == null || patherDestination == null)
                {
                    missing += " VehicleCaravan.vehiclePather/Moving/Destination";
                    vehiclePatherField = null;
                }
            }
            if (aerialType != null)
            {
                aerialPlayerControlled = AccessTools.Property(aerialType, "IsPlayerControlled");
                aerialVehicle = AccessTools.Field(aerialType, "vehicle");
                aerialFlightPath = AccessTools.Field(aerialType, "flightPath");
                if (aerialFlightPath != null)
                {
                    pathEmpty = AccessTools.Property(aerialFlightPath.FieldType, "Empty");
                    pathLast = AccessTools.Property(aerialFlightPath.FieldType, "Last");
                    if (pathLast != null)
                    {
                        nodeTile = AccessTools.Field(pathLast.PropertyType, "tile");
                    }
                }
                if (aerialVehicle != null)
                {
                    pawnsAboard = AccessTools.Property(aerialVehicle.FieldType, "AllPawnsAboard");
                }
                if (pawnsAboard == null && vehiclePawnType != null)
                {
                    pawnsAboard = AccessTools.Property(vehiclePawnType, "AllPawnsAboard");
                }
                if (aerialPlayerControlled == null || aerialVehicle == null || aerialFlightPath == null
                    || pathEmpty == null || pathLast == null || nodeTile == null || pawnsAboard == null)
                {
                    missing += " AerialVehicleInFlight members";
                    aerialType = null;
                }
            }

            Available = true;
            Status = "Vehicle Framework found." + (missing.Length > 0
                ? " Did not resolve:" + missing + " (version mismatch?), those rows are reduced or left out."
                : " Vehicle caravans and aerial vehicles resolved.");
            if (missing.Length > 0)
            {
                Log.Warning("[SettlementInformation] " + Status);
            }
        }

        public static bool IsVehicleCaravan(Caravan c)
        {
            return caravanType != null && caravanType.IsInstanceOfType(c);
        }

        // true when the caravan is a VF caravan and its own pather was read
        public static bool TryReadCaravanPath(Caravan c, out bool moving, out bool movingNow, out bool paused, out int destination)
        {
            moving = false;
            movingNow = false;
            paused = false;
            destination = -1;
            if (vehiclePatherField == null || !IsVehicleCaravan(c))
            {
                return false;
            }
            try
            {
                object pather = vehiclePatherField.GetValue(c);
                if (pather == null)
                {
                    return false;
                }
                moving = (bool)patherMoving.GetValue(pather, null);
                movingNow = patherMovingNow != null && (bool)patherMovingNow.GetValue(pather, null);
                paused = patherPaused != null && (bool)patherPaused.GetValue(pather, null);
                destination = (int)patherDestination.GetValue(pather, null);
                return true;
            }
            catch (Exception e)
            {
                Fail(e);
                return false;
            }
        }

        private static bool IsVehicle(object o)
        {
            return vehiclePawnType != null && vehiclePawnType.IsInstanceOfType(o);
        }

        private static void AddAboard(object vehicle)
        {
            if (pawnsAboard == null || vehicle == null)
            {
                return;
            }
            IEnumerable aboard = pawnsAboard.GetValue(vehicle, null) as IEnumerable;
            if (aboard == null)
            {
                return;
            }
            foreach (object o in aboard)
            {
                Pawn p = o as Pawn;
                if (p != null && !IsVehicle(p))
                {
                    countScratch.Add(p);
                }
            }
        }

        // Real pawns in a caravan: never a vehicle. Pawns listed directly plus
        // those aboard each vehicle, each counted once.
        public static int PawnCount(Caravan c)
        {
            countScratch.Clear();
            List<Pawn> direct = c.PawnsListForReading;
            try
            {
                for (int i = 0; i < direct.Count; i++)
                {
                    Pawn p = direct[i];
                    if (IsVehicle(p))
                    {
                        AddAboard(p);
                    }
                    else
                    {
                        countScratch.Add(p);
                    }
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
            return countScratch.Count;
        }

        public static int VehicleCount(Caravan c)
        {
            if (vehiclesList == null || !IsVehicleCaravan(c))
            {
                return 0;
            }
            try
            {
                ICollection list = vehiclesList.GetValue(c, null) as ICollection;
                return list == null ? 0 : list.Count;
            }
            catch (Exception e)
            {
                Fail(e);
                return 0;
            }
        }

        // Adds the player's aerial vehicles currently in flight to the list.
        public static void CollectAerial(List<WorldObject> into)
        {
            if (aerialType == null)
            {
                return;
            }
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject o = all[i];
                if (!aerialType.IsInstanceOfType(o))
                {
                    continue;
                }
                try
                {
                    if ((bool)aerialPlayerControlled.GetValue(o, null))
                    {
                        into.Add(o);
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        public static int AerialPawnCount(WorldObject o)
        {
            try
            {
                countScratch.Clear();
                AddAboard(aerialVehicle.GetValue(o));
                return countScratch.Count;
            }
            catch (Exception e)
            {
                Fail(e);
                return 0;
            }
        }

        // -1 = no destination (empty flight path)
        public static int AerialDestination(WorldObject o)
        {
            try
            {
                object path = aerialFlightPath.GetValue(o);
                if (path == null || (bool)pathEmpty.GetValue(path, null))
                {
                    return -1;
                }
                object last = pathLast.GetValue(path, null);
                return last == null ? -1 : (int)nodeTile.GetValue(last);
            }
            catch (Exception e)
            {
                Fail(e);
                return -1;
            }
        }

        // Fills pawns (real pawns, deduplicated, aboard ones included) and
        // vehicles for the travel log. Never counts a vehicle as a pawn.
        public static void CollectMembers(Caravan c, List<Pawn> pawns, List<Pawn> vehicles)
        {
            pawns.Clear();
            vehicles.Clear();
            try
            {
                countScratch.Clear();
                List<Pawn> direct = c.PawnsListForReading;
                for (int i = 0; i < direct.Count; i++)
                {
                    Pawn p = direct[i];
                    if (IsVehicle(p))
                    {
                        vehicles.Add(p);
                        AddAboard(p);
                    }
                    else
                    {
                        countScratch.Add(p);
                    }
                }
                foreach (Pawn p in countScratch)
                {
                    pawns.Add(p);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        // Same for an aerial vehicle in flight: the vehicle plus those aboard.
        public static void CollectAerialMembers(WorldObject o, List<Pawn> pawns, List<Pawn> vehicles)
        {
            pawns.Clear();
            vehicles.Clear();
            try
            {
                countScratch.Clear();
                object v = aerialVehicle.GetValue(o);
                Pawn vp = v as Pawn;
                if (vp != null)
                {
                    vehicles.Add(vp);
                }
                AddAboard(v);
                foreach (Pawn p in countScratch)
                {
                    pawns.Add(p);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void Fail(Exception e)
        {
            if (failureLogged)
            {
                return;
            }
            failureLogged = true;
            Log.Warning("[SettlementInformation] Vehicle Framework read threw once, further failures are silent: " + e.Message);
        }
    }
}
