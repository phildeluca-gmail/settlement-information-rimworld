using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using SettlementInformation.Vehicles;
using UnityEngine;
using Verse;

namespace SettlementInformation.Draw
{
    // Travel log, ordered 2026-11-15: every player caravan, vehicle caravan
    // and aerial vehicle in flight is tracked by diffing the set of such world
    // objects about once a second; each appearance, departure, arrival and
    // disappearance is one Player.log line. No Harmony patch, and independent
    // of the "Show caravan list" setting.
    public class TravelLogComponent : GameComponent
    {
        private class Tracked
        {
            public WorldObject Obj;
            public int Id;
            public string Kind;
            public string Name;
            public List<Pawn> Pawns = new List<Pawn>();
            public List<Pawn> Vehicles = new List<Pawn>();
            public int Tile;
            public int Dest;
            public bool Moving;
            public int Seen;
        }

        private const float RefreshSeconds = 1f;
        private const int TickInterval = 30;

        private readonly Dictionary<int, Tracked> tracked = new Dictionary<int, Tracked>();
        private readonly List<WorldObject> scratch = new List<WorldObject>();
        private readonly List<int> gone = new List<int>();
        private readonly StringBuilder sb = new StringBuilder();
        private float lastScan = -100f;
        private int scanNumber;
        private bool started;

        public TravelLogComponent(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (Time.realtimeSinceStartup - lastScan > RefreshSeconds)
            {
                Scan();
            }
        }

        // also scans at high game speed, where one real second is many ticks
        public override void GameComponentTick()
        {
            if (Find.TickManager != null && Find.TickManager.TicksGame % TickInterval == 0)
            {
                Scan();
            }
        }

        private void Scan()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null || Find.WorldObjects == null)
            {
                return;
            }
            lastScan = Time.realtimeSinceStartup;
            scanNumber++;

            scratch.Clear();
            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                if (caravans[i].IsPlayerControlled)
                {
                    scratch.Add(caravans[i]);
                }
            }
            VehicleFrameworkReader.CollectAerial(scratch);

            bool first = !started;
            started = true;
            for (int i = 0; i < scratch.Count; i++)
            {
                WorldObject o = scratch[i];
                Tracked t;
                bool isNew = !tracked.TryGetValue(o.ID, out t);
                if (isNew)
                {
                    t = new Tracked { Obj = o, Id = o.ID, Tile = o.Tile, Dest = -1 };
                    tracked[o.ID] = t;
                }
                t.Seen = scanNumber;
                bool wasMoving = t.Moving;
                int oldDest = t.Dest;
                Read(t);

                if (isNew)
                {
                    if (first)
                    {
                        Log.Message("[SettlementInformation] Travel log: " + t.Kind + " '" + t.Name + "' (id " + t.Id + ") already on the world at tile " + t.Tile + " (" + CaravanListComponent.PlaceName(t.Tile) + ") when tracking began; " + Members(t) + ".");
                    }
                    else
                    {
                        Log.Message("[SettlementInformation] Travel log: " + t.Kind + " '" + t.Name + "' (id " + t.Id + ") appeared on the world map at tile " + t.Tile + " (" + CaravanListComponent.PlaceName(t.Tile) + "), " + Members(t) + "; " + (t.Moving ? "heading to " + Place(t.Dest) : "no destination yet") + ".");
                    }
                }
                else if (t.Moving && (!wasMoving || t.Dest != oldDest))
                {
                    Log.Message("[SettlementInformation] Travel log: " + t.Kind + " '" + t.Name + "' (id " + t.Id + ") " + (wasMoving ? "changed destination" : "departed") + " from " + Place(t.Tile) + " to " + Place(t.Dest) + ", " + Members(t) + ".");
                }
                else if (wasMoving && !t.Moving && t.Obj is Caravan)
                {
                    Log.Message("[SettlementInformation] Travel log: " + t.Kind + " '" + t.Name + "' (id " + t.Id + ") stopped moving, arrived at tile " + t.Tile + " (" + CaravanListComponent.PlaceName(t.Tile) + "), " + Members(t) + ".");
                }
            }

            gone.Clear();
            foreach (KeyValuePair<int, Tracked> kv in tracked)
            {
                if (kv.Value.Seen != scanNumber)
                {
                    gone.Add(kv.Key);
                }
            }
            for (int i = 0; i < gone.Count; i++)
            {
                Tracked t = tracked[gone[i]];
                tracked.Remove(gone[i]);
                LogGone(t);
            }
        }

        // refreshes name, members, tile and movement of a tracked object
        private void Read(Tracked t)
        {
            WorldObject o = t.Obj;
            t.Name = o.LabelCap;
            t.Tile = o.Tile;
            Caravan c = o as Caravan;
            if (c != null)
            {
                VehicleFrameworkReader.CollectMembers(c, t.Pawns, t.Vehicles);
                if (VehicleFrameworkReader.IsVehicleCaravan(c))
                {
                    t.Kind = "vehicle caravan";
                    bool moving, movingNow, paused;
                    int dest;
                    if (!VehicleFrameworkReader.TryReadCaravanPath(c, out moving, out movingNow, out paused, out dest))
                    {
                        moving = false;
                        dest = -1;
                    }
                    t.Moving = moving;
                    t.Dest = moving ? dest : -1;
                }
                else
                {
                    t.Kind = "caravan";
                    Caravan_PathFollower p = c.pather;
                    bool moving = p != null && p.Moving;
                    t.Moving = moving;
                    t.Dest = moving ? p.Destination : -1;
                }
            }
            else
            {
                t.Kind = "aerial vehicle";
                VehicleFrameworkReader.CollectAerialMembers(o, t.Pawns, t.Vehicles);
                t.Dest = VehicleFrameworkReader.AerialDestination(o);
                t.Moving = t.Dest >= 0;
            }
        }

        private void LogGone(Tracked t)
        {
            string head = "[SettlementInformation] Travel log: " + t.Kind + " '" + t.Name + "' (id " + t.Id + "), " + Members(t) + ", ";
            string where = "last seen at tile " + t.Tile + " (" + CaravanListComponent.PlaceName(t.Tile) + ")" + (t.Moving ? ", was heading to " + Place(t.Dest) : "");

            // 1. members now spawned on a map: it entered / landed on that map
            Map map = null;
            for (int i = 0; i < t.Vehicles.Count && map == null; i++)
            {
                map = SpawnedMap(t.Vehicles[i]);
            }
            for (int i = 0; i < t.Pawns.Count && map == null; i++)
            {
                map = SpawnedMap(t.Pawns[i]);
            }
            if (map != null)
            {
                string mapName = map.Parent != null ? map.Parent.LabelCap.ToString() : "unnamed map";
                Log.Message(head + (t.Obj is Caravan ? "entered map '" : "arrived and landed on map '") + mapName + "' (map tile " + map.Tile + "); " + where + ".");
                return;
            }

            // 2. members now in another caravan: merged or became a caravan
            List<Pawn> all = new List<Pawn>(t.Vehicles);
            all.AddRange(t.Pawns);
            for (int i = 0; i < all.Count; i++)
            {
                Caravan other = SafeCaravan(all[i]);
                if (other != null && other != t.Obj)
                {
                    Log.Message(head + "members are now in caravan '" + other.LabelCap + "' (id " + other.ID + ", tile " + other.Tile + "); " + where + ".");
                    return;
                }
            }

            // 3. everyone dead or destroyed
            bool allDead = all.Count > 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].Dead && !all[i].Destroyed)
                {
                    allDead = false;
                    break;
                }
            }
            if (allDead)
            {
                Log.Message(head + "gone: every member is dead or destroyed; " + where + ".");
                return;
            }

            Log.Message(head + "gone: destroyed or removed, reason not known; " + where + ".");
        }

        private static Map SpawnedMap(Pawn p)
        {
            return p != null && p.Spawned ? p.Map : null;
        }

        private static Caravan SafeCaravan(Pawn p)
        {
            try
            {
                return p.GetCaravan();
            }
            catch
            {
                return null;
            }
        }

        private static string Place(int tile)
        {
            return CaravanListComponent.PlaceName(tile) + " (tile " + tile + ")";
        }

        private string Members(Tracked t)
        {
            sb.Length = 0;
            sb.Append("pawns [");
            for (int i = 0; i < t.Pawns.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(t.Pawns[i].LabelShort);
            }
            sb.Append("]");
            if (t.Vehicles.Count > 0)
            {
                sb.Append(", vehicles [");
                for (int i = 0; i < t.Vehicles.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(t.Vehicles[i].LabelShort);
                }
                sb.Append("]");
            }
            return sb.ToString();
        }
    }
}
