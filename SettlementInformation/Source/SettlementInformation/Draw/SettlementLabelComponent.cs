using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SettlementInformation.Draw
{
    // Draws the label. RimWorld adds a GameComponent subclass to every game
    // (new or loaded) by itself, and UIRoot.UIRootOnGUI calls
    // GameComponentOnGUI on each whenever a game exists - so no Harmony patch.
    // It runs BEFORE WorldInterfaceOnGUI, so the vanilla icons and any
    // window draw on top of the label, which is what we want.
    // That call also happens on the map view, hence the WorldRenderedNow check.
    public class SettlementLabelComponent : GameComponent
    {
        private class Entry
        {
            public WorldObject Settlement;
            public string Text;
            public Vector2 Size;
        }

        // real seconds, not ticks - the world map is usable while paused
        private const float RefreshSeconds = 1f;
        private const float PadX = 4f;
        private const float PadY = 2f;

        private readonly List<Entry> entries = new List<Entry>();
        private float lastRefresh = -100f;
        private readonly StringBuilder sb = new StringBuilder();

        // RimWorld constructs components with (Game)
        public SettlementLabelComponent(Game game)
        {
        }

        public override void GameComponentOnGUI()
        {
            if (Event.current.type != EventType.Repaint || !WorldRendererUtility.WorldRenderedNow)
            {
                return;
            }
            if (Find.World == null || Find.WorldCamera == null)
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
                Text.Anchor = TextAnchor.UpperCenter;
                Text.WordWrap = false;

                if (Time.realtimeSinceStartup - lastRefresh > RefreshSeconds)
                {
                    Refresh();
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    WorldObject s = e.Settlement;
                    if (s == null || s.Destroyed || s.HiddenBehindTerrainNow())
                    {
                        continue;
                    }

                    Vector2 pos = s.ScreenPos();
                    // 14px puts the box under the settlement icon, clear of vanilla's own label
                    var box = new Rect(pos.x - e.Size.x / 2f - PadX, pos.y + 14f, e.Size.x + PadX * 2f, e.Size.y + PadY * 2f);
                    if (box.xMax < 0f || box.x > UI.screenWidth || box.yMax < 0f || box.y > UI.screenHeight)
                    {
                        continue;
                    }

                    Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.5f));
                    var textRect = new Rect(box.x, box.y + PadY, box.width, e.Size.y);
                    GUI.color = Color.black;
                    Widgets.Label(new Rect(textRect.x + 1f, textRect.y + 1f, textRect.width, textRect.height), e.Text);
                    GUI.color = Color.white;
                    Widgets.Label(textRect, e.Text);
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

        // rebuilds every string once a second; must run inside OnGUI because
        // Text.CalcSize needs the GUI context (and the Tiny font set by the caller)
        private void Refresh()
        {
            lastRefresh = Time.realtimeSinceStartup;
            entries.Clear();

            List<Settlement> all = Find.WorldObjects.Settlements;
            int now = Find.TickManager.TicksGame;

            for (int i = 0; i < all.Count; i++)
            {
                Settlement s = all[i];
                string text = Build(s, now);
                entries.Add(new Entry { Settlement = s, Text = text, Size = Text.CalcSize(text) });
            }

            // Player-held maps that are not settlements (a destroyed settlement, site, camp):
            // vanilla gives them no label from us, so draw a short one. Pop is cached here,
            // once a second, never per frame.
            List<MapParent> parents = Find.WorldObjects.MapParents;
            for (int i = 0; i < parents.Count; i++)
            {
                MapParent mp = parents[i];
                if (mp is Settlement || !mp.HasMap || mp.Map == null)
                {
                    continue;
                }
                int pop = PlayerPopulation(mp.Map);
                if (pop <= 0)
                {
                    continue;
                }
                string text = mp.LabelCap + "\nPop: " + pop;
                entries.Add(new Entry { Settlement = mp, Text = text, Size = Text.CalcSize(text) });
            }
        }

        private string Build(Settlement s, int now)
        {
            sb.Length = 0;
            sb.Append(s.LabelCap);

            Faction f = s.Faction;
            if (f != null)
            {
                sb.Append('\n').Append(f.Name);
            }

            if (RimWar.RimWarReader.Available)
            {
                int? strength = RimWar.RimWarReader.ReadStrength(s);
                sb.Append("\nStrength: ").Append(strength.HasValue ? strength.Value.ToString() : "-");
            }

            sb.Append('\n').Append(RestockLine(s, f, now));
            if (f != null && f.IsPlayer && s.HasMap && s.Map != null)
            {
                sb.Append("\nPop: ").Append(PlayerPopulation(s.Map));
            }
            return sb.ToString();
        }

        // Free player colonists spawned on the map. Prisoners, slaves and animals are
        // not counted (colonists only, the default asked for). Pawns in a downed or
        // incapacitated state still count; pawns off the map (caravans) do not.
        private static int PlayerPopulation(Map map)
        {
            return map.mapPawns.FreeColonistsSpawnedCount;
        }

        // Never touches StockListForReading - that regenerates stock and marks
        // the settlement visited. Same branch order as vanilla's
        // Dialog_SellableItems, with the player case first.
        private static string RestockLine(Settlement s, Faction f, int now)
        {
            if (f != null && f.IsPlayer)
            {
                return "N/A";
            }

            Settlement_TraderTracker t = s.trader;
            if (t == null)
            {
                return "-";
            }
            if (!t.EverVisited)
            {
                return "not visited yet";
            }
            if (t.NextRestockTick != -1)
            {
                // can dip below zero for a moment before the stock is dropped
                float days = Mathf.Max(0f, (t.NextRestockTick - now).TicksToDays());
                return "restocks in " + days.ToString("0.0") + " days";
            }
            if (t.RestockedSinceLastVisit)
            {
                return "stocked";
            }
            return "-";
        }
    }
}
