using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using SettlementInformation.Vehicles;
using UnityEngine;
using Verse;

namespace SettlementInformation.Draw
{
    // Compact list of the player's caravans (including Vehicle Framework
    // caravans and aerial vehicles in flight), vertically centred on the
    // left edge. Drawn from GameComponentOnGUI, which UIRoot_Play calls on
    // both the map view and the world view - no Harmony patch.
    // Click a row: CameraJumper.TryJumpAndSelect on the world object.
    // Strings are rebuilt once a second of real time; the per-frame path
    // allocates nothing.
    public class CaravanListComponent : GameComponent
    {
        private class Row
        {
            public WorldObject Obj;
            public string Text;
        }

        private const float RefreshSeconds = 1f;
        private const float RowHeight = 18f;
        private const float Width = 270f;
        private const float Pad = 4f;
        private const float LeftMargin = 4f;
        // Keeps the list under the resource readout (top-left) and above the
        // Unsupported roofs button (bottom-left, about 73 px up from the
        // bottom) when many rows are listed. The readout's height is not read.
        private const float MinTop = 220f;
        private const float ReadoutGap = 8f;
        private const float BottomReserve = 120f;
        // after a caravan stops moving it reads "arrived" for this long
        private const float ArrivedShowSeconds = 120f;

        private readonly List<Row> rows = new List<Row>();
        private readonly List<WorldObject> scratch = new List<WorldObject>();
        private readonly Dictionary<int, bool> wasMoving = new Dictionary<int, bool>();
        private readonly Dictionary<int, float> arrivedAt = new Dictionary<int, float>();
        private readonly StringBuilder sb = new StringBuilder();
        // MapInterface.resourceReadout (private) and ResourceReadout.lastDrawnHeight
        // (private), verified by reflection 2026-11-15
        private static readonly FieldInfo ReadoutField = AccessTools.Field(typeof(MapInterface), "resourceReadout");
        private static readonly FieldInfo ReadoutHeightField = AccessTools.Field(typeof(ResourceReadout), "lastDrawnHeight");
        private float readoutBottom = MinTop;
        private float lastRefresh = -100f;
        private bool shownLogged;

        public CaravanListComponent(Game game)
        {
        }

        public override void GameComponentOnGUI()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null || Find.TickManager == null)
            {
                return;
            }
            if (!SettlementInformation.Core.SettlementInformationMod.Settings.ShowCaravanList)
            {
                return;
            }
            EventType type = Event.current.type;
            if (type != EventType.Repaint && type != EventType.MouseDown)
            {
                return;
            }

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                if (Time.realtimeSinceStartup - lastRefresh > RefreshSeconds)
                {
                    Refresh();
                }
                if (rows.Count == 0)
                {
                    return;
                }

                if (!shownLogged)
                {
                    shownLogged = true;
                    Log.Message("[SettlementInformation] Caravan list shown for the first time, " + rows.Count + " row(s).");
                }

                float height = rows.Count * RowHeight + Pad * 2f;
                float top = (UI.screenHeight - height) / 2f;
                top = Mathf.Max(top, readoutBottom);
                top = Mathf.Min(top, UI.screenHeight - BottomReserve - height);
                var box = new Rect(LeftMargin, top, Width, height);

                if (type == EventType.Repaint)
                {
                    Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.5f));
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    Row r = rows[i];
                    var rowRect = new Rect(box.x + Pad, box.y + Pad + i * RowHeight, box.width - Pad * 2f, RowHeight);
                    if (type == EventType.Repaint)
                    {
                        if (Find.WorldSelector.IsSelected(r.Obj))
                        {
                            Widgets.DrawHighlight(rowRect);
                        }
                        else
                        {
                            Widgets.DrawHighlightIfMouseover(rowRect);
                        }
                        GUI.color = Color.white;
                        Widgets.Label(rowRect, r.Text);
                    }
                    else if (Event.current.button == 0 && Mouse.IsOver(rowRect))
                    {
                        Event.current.Use();
                        if (r.Obj.Destroyed)
                        {
                            Log.Message("[SettlementInformation] Caravan list click ignored: '" + r.Obj.LabelCap + "' no longer exists.");
                            lastRefresh = -100f;
                            return;
                        }
                        Log.Message("[SettlementInformation] Caravan list click: jumping to '" + r.Obj.LabelCap + "' (id " + r.Obj.ID + ", tile " + r.Obj.Tile + ").");
                        CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(r.Obj));
                        return;
                    }
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        // Bottom of the resource readout (map view only; it is not drawn on
        // the world view), plus a gap. Falls back to MinTop when unreadable.
        private static float ReadoutBottom()
        {
            if (WorldRendererUtility.WorldRenderedNow || ReadoutField == null || ReadoutHeightField == null || Find.MapUI == null)
            {
                return MinTop;
            }
            object readout = ReadoutField.GetValue(Find.MapUI);
            if (readout == null)
            {
                return MinTop;
            }
            // lastDrawnHeight is the full content height; the readout scrolls
            // and stops 207 px above the screen bottom
            float h = Mathf.Min((float)ReadoutHeightField.GetValue(readout), UI.screenHeight - 207f);
            return 7f + h + ReadoutGap;
        }

        // Must run inside OnGUI with the Tiny font set (Truncate measures text).
        private void Refresh()
        {
            lastRefresh = Time.realtimeSinceStartup;
            readoutBottom = ReadoutBottom();
            rows.Clear();
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

            for (int i = 0; i < scratch.Count; i++)
            {
                WorldObject o = scratch[i];
                string text = Build(o);
                rows.Add(new Row { Obj = o, Text = text.Truncate(Width - Pad * 2f - 4f) });
            }
        }

        private string Build(WorldObject o)
        {
            sb.Length = 0;
            sb.Append(o.LabelCap).Append(" (");

            bool moving;
            bool movingNow = false;
            bool paused = false;
            int dest = -1;
            string eta = null;
            string extra = null;
            Caravan c = o as Caravan;

            if (c != null)
            {
                sb.Append(VehicleFrameworkReader.PawnCount(c)).Append(" pawns");
                if (VehicleFrameworkReader.IsVehicleCaravan(c))
                {
                    sb.Append(", ").Append(VehicleFrameworkReader.VehicleCount(c)).Append(" veh");
                    if (!VehicleFrameworkReader.TryReadCaravanPath(c, out moving, out movingNow, out paused, out dest))
                    {
                        moving = false;
                    }
                }
                else
                {
                    Caravan_PathFollower p = c.pather;
                    moving = p != null && p.Moving;
                    paused = p != null && p.Paused;
                    dest = moving ? p.Destination : -1;
                    if (moving && !paused)
                    {
                        int ticks = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(c, true);
                        if (ticks > 0 && ticks < 10000000)
                        {
                            eta = ticks.ToStringTicksToPeriod();
                        }
                    }
                }
                // NightResting reads the vanilla pather, which a vehicle caravan
                // does not use; for those, resting is not claimed while the
                // vehicle pather is actually moving (MovingNow).
                if (c.NightResting && !(VehicleFrameworkReader.IsVehicleCaravan(c) && movingNow))
                {
                    extra = "resting";
                }
            }
            else
            {
                // aerial vehicle in flight
                sb.Append(VehicleFrameworkReader.AerialPawnCount(o)).Append(" pawns");
                dest = VehicleFrameworkReader.AerialDestination(o);
                moving = dest >= 0;
                extra = moving ? "flying" : "in flight";
            }
            sb.Append(") - ");

            int id = o.ID;
            bool before;
            wasMoving.TryGetValue(id, out before);
            if (before && !moving && c != null)
            {
                arrivedAt[id] = Time.realtimeSinceStartup;
                // the "stopped moving" log line moved to TravelLogComponent (2026-11-15)
            }
            wasMoving[id] = moving;

            if (moving)
            {
                sb.Append(paused ? "paused, to " : (extra == null ? "to " : extra + " to ")).Append(PlaceName(dest));
                if (eta != null)
                {
                    sb.Append(", ").Append(eta);
                }
            }
            else
            {
                float t;
                if (arrivedAt.TryGetValue(id, out t) && Time.realtimeSinceStartup - t < ArrivedShowSeconds)
                {
                    sb.Append("arrived at ").Append(PlaceName(o.Tile));
                }
                else
                {
                    sb.Append(extra ?? "idle");
                }
            }
            return sb.ToString();
        }

        internal static string PlaceName(int tile)
        {
            if (tile < 0)
            {
                return "?";
            }
            MapParent mp = Find.WorldObjects.MapParentAt(tile);
            return mp != null ? mp.LabelCap.ToString() : "tile " + tile;
        }
    }
}
