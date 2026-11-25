using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityModManagerNet;

namespace ZG2
{
    /// <summary>
    /// Mod settings (persisted by UMM). In multiplayer the host's values are sent to all clients;
    /// clients do not see the settings.
    /// </summary>
    public class ZG2Settings : UnityModManager.ModSettings
    {
        // ── Zg2 check ──
        public bool CheckOnDeparture = true;
        public bool CheckOnArrival = true;
        public int RequiredBoards = 2;
        public float MaxCouplerDistance = 0.5f;
        public float MaxBoardAngle = 35f;
        /// <summary>Locomotive at the train end: 0 = always OK, 1 = red tail lights must be lit (or Zg2 boards on the loco).</summary>
        public int LocoEndMode = 0;

        // ── Penalty ──
        public float PenaltyPerCheckpoint = 5f;
        public float MaxPenalty = 50f;

        // ── Station detection ──
        public float StationEnterMargin = 30f;
        public float StationExitMargin = 1000f;

        /// <summary>Settings file version for migrations.</summary>
        public int SettingsVersion = 0;
        public const int CurrentSettingsVersion = 2;

        // ── Job types ──
        public string JobTypes = "Transport;EmptyHaul";
        /// <summary>Detected job types of other mods ("number:abbreviation;…"), filled automatically.</summary>
        public string KnownJobTypes = "";

        // ── Boards ──
        public bool SpawnBoardsAtStations = true;
        public int BoardsPerStation = 3;
        public bool RandomStationWear = true;
        public bool BoardAging = true;
        public float WearFullKm = 500f;
        public float WornTextureAt = 0.6f;
        public bool UseCustomTextures = true;
        public bool ShowMapMarkers = true;

        // ── Debug ──
        public bool DebugLogging = false;

        [NonSerialized] private bool zonesDirty;
        [NonSerialized] private bool syncDirty;
        [NonSerialized] private float nextSync;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Clamp();
            Save(this, modEntry);
        }

        public void Clamp()
        {
            RequiredBoards = Mathf.Clamp(RequiredBoards, 1, 4);
            MaxCouplerDistance = Mathf.Clamp(MaxCouplerDistance, 0.1f, 3f);
            MaxBoardAngle = Mathf.Clamp(MaxBoardAngle, 5f, 90f);
            LocoEndMode = Mathf.Clamp(LocoEndMode, 0, 1);
            PenaltyPerCheckpoint = Mathf.Clamp(PenaltyPerCheckpoint, 0f, 100f);
            MaxPenalty = Mathf.Clamp(MaxPenalty, 0f, 100f);
            StationEnterMargin = Mathf.Clamp(StationEnterMargin, 0f, 150f);
            StationExitMargin = Mathf.Clamp(StationExitMargin, Mathf.Max(200f, StationEnterMargin + 50f), 3000f);
            BoardsPerStation = Mathf.Clamp(BoardsPerStation, 1, 10);
            WearFullKm = Mathf.Clamp(WearFullKm, 10f, 10000f);
            WornTextureAt = Mathf.Clamp01(WornTextureAt);
            if (JobTypes == null) JobTypes = string.Empty;
        }

        /// <summary>Upgrades older settings files (called after loading).</summary>
        public void Migrate()
        {
            if (SettingsVersion < 2)
            {
                // 1.1.0 test builds used 80 m – too short for stations with several track groups.
                if (StationExitMargin < 200f) StationExitMargin = 1000f;
                SettingsVersion = CurrentSettingsVersion;
            }
            Clamp();
        }

        /// <summary>Called by UMM when the settings window is closed / saved.</summary>
        public void OnSaved()
        {
            if (zonesDirty)
            {
                zonesDirty = false;
                if (Main.WorldLoaded && ZG2MPUtil.IsSinglePlayerOrHost) ZG2StationZones.Build();
            }
            SendSync();
        }

        #region Multiplayer sync

        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        private static FieldInfo[] SyncFields =>
            typeof(ZG2Settings).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsNotSerialized && f.DeclaringType == typeof(ZG2Settings) &&
                            (f.FieldType == typeof(bool) || f.FieldType == typeof(int) ||
                             f.FieldType == typeof(float) || f.FieldType == typeof(string)))
                .ToArray();

        public string ToSyncString()
        {
            var sb = new StringBuilder();
            foreach (var f in SyncFields)
            {
                object v = f.GetValue(this);
                string s = v is float fl ? fl.ToString("R", IC) : Convert.ToString(v, IC);
                sb.Append(f.Name).Append('=').Append(Uri.EscapeDataString(s ?? string.Empty)).Append('&');
            }
            return sb.ToString();
        }

        public void ApplySyncString(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            bool markers = ShowMapMarkers;
            var fields = SyncFields.ToDictionary(f => f.Name);
            foreach (var pair in data.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!fields.TryGetValue(pair.Substring(0, eq), out var f)) continue;
                string s = Uri.UnescapeDataString(pair.Substring(eq + 1));
                try
                {
                    if (f.FieldType == typeof(bool)) f.SetValue(this, bool.Parse(s));
                    else if (f.FieldType == typeof(int)) f.SetValue(this, int.Parse(s, IC));
                    else if (f.FieldType == typeof(float)) f.SetValue(this, float.Parse(s, IC));
                    else f.SetValue(this, s);
                }
                catch { /* ignore single bad value */ }
            }
            if (markers != ShowMapMarkers) ZG2MapMarkerManager.OnSettingChanged(ShowMapMarkers);
        }

        private void SendSync()
        {
            syncDirty = false;
            if (ZG2MPUtil.IsMultiplayerActive && ZG2MPUtil.IsSinglePlayerOrHost)
                ZG2MPUtil.BroadcastSettings(ToSyncString());
        }

        #endregion

        #region GUI

        private static GUIStyle tipStyle, headerStyle, toggleStyle, labelStyle;
        private const float LabelWidth = 330f;

        public void OnGUI()
        {
            if (ZG2MPUtil.IsClient)
            {
                GUILayout.Label(ZG2Loc.T("<i>Die Einstellungen gibt der Host vor.</i>", "<i>Settings are controlled by the host.</i>"));
                return;
            }

            EnsureStyles();
            ZG2TooltipOverlay.CaptureArea();
            bool de = ZG2Loc.German;
            GUI.changed = false;

            // ── Zg2 check ──
            Header(de ? "Zg2-Prüfung" : "Zg2 check");
            CheckOnDeparture = Toggle(CheckOnDeparture, de ? "Prüfung bei Bahnhofsausfahrt" : "Check on station departure", TipDeparture(de));
            CheckOnArrival = Toggle(CheckOnArrival, de ? "Prüfung bei Bahnhofseinfahrt" : "Check on station arrival", TipArrival(de));
            RequiredBoards = IntSlider(de ? "Erforderliche Zg2-Tafeln" : "Required Zg2 boards", RequiredBoards, 1, 4, TipRequired(de));
            MaxCouplerDistance = FloatSlider(de ? "Max. Abstand zur letzten Kupplung" : "Max. distance to last coupler",
                MaxCouplerDistance, 0.1f, 3f, 0.05f, "0.00 m", TipDistance(de));
            MaxBoardAngle = FloatSlider(de ? "Max. Winkelabweichung" : "Max. angle deviation",
                MaxBoardAngle, 5f, 90f, 1f, "0°", TipAngle(de));

            GUILayout.BeginHorizontal(GUILayout.MinHeight(LineHeight));
            SliderLabel(de ? "Lok am Zugschluss" : "Locomotive at train end", TipLocoEnd(de));
            if (GUILayout.Toggle(LocoEndMode == 0, de ? " immer gültig" : " always valid", toggleStyle, GUILayout.ExpandWidth(false), GUILayout.MinHeight(LineHeight))) LocoEndMode = 0;
            GUILayout.Space(12);
            if (GUILayout.Toggle(LocoEndMode == 1, de ? " rote Schlusslichter an" : " red tail lights on", toggleStyle, GUILayout.ExpandWidth(false), GUILayout.MinHeight(LineHeight))) LocoEndMode = 1;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ── Penalty ──
            Header(de ? "Strafe" : "Penalty");
            PenaltyPerCheckpoint = FloatSlider(de ? "Abzug pro fehlerhafter Ausfahrt" : "Deduction per failed departure",
                PenaltyPerCheckpoint, 0f, 25f, 0.5f, "0.#'%'", TipPenalty(de));
            MaxPenalty = FloatSlider(de ? "Maximaler Abzug" : "Maximum deduction", MaxPenalty, 0f, 100f, 5f, "0'%'", TipMaxPenalty(de));

            // ── Station detection ──
            Header(de ? "Bahnhofserkennung" : "Station detection");
            float enter = FloatSlider(de ? "Einfahrt ab Abstand zum Bahnhofsgleis" : "Arrival within distance of yard tracks",
                StationEnterMargin, 0f, 150f, 5f, "0 m", TipEnter(de));
            float exit = FloatSlider(de ? "Ausfahrt ab Entfernung zum Bahnhof" : "Departure beyond distance from station",
                StationExitMargin, 200f, 3000f, 50f, "0 m", TipExit(de));
            if (enter != StationEnterMargin || exit != StationExitMargin)
            {
                StationEnterMargin = enter;
                StationExitMargin = Mathf.Max(exit, enter + 50f);
                zonesDirty = true;
            }

            // ── Job types ──
            Header(de ? "Zg2-pflichtige Jobtypen" : "Job types requiring Zg2");
            DrawJobTypes(de);

            // ── Boards ──
            Header(de ? "Zugschlusstafeln" : "Train end boards");
            SpawnBoardsAtStations = Toggle(SpawnBoardsAtStations, de ? "Gebrauchte Tafeln an Bahnhöfen auslegen" : "Place used boards at stations", TipSpawn(de));
            if (SpawnBoardsAtStations)
                BoardsPerStation = IntSlider(de ? "Tafeln pro Bahnhof" : "Boards per station", BoardsPerStation, 1, 10, TipPerStation(de));
            RandomStationWear = Toggle(RandomStationWear, de ? "Zufällige Gebrauchsspuren (Bahnhofstafeln)" : "Random wear (station boards)", TipRandomWear(de));
            BoardAging = Toggle(BoardAging, de ? "Gekaufte Tafeln nutzen sich ab" : "Bought boards wear out", TipAging(de));
            if (BoardAging)
            {
                WearFullKm = FloatSlider(de ? "Voll abgenutzt nach" : "Fully worn after", WearFullKm, 50f, 5000f, 50f, "0 km", TipWearKm(de));
                WornTextureAt = FloatSlider(de ? "Abgenutzte Textur ab" : "Worn texture from", WornTextureAt, 0.1f, 1f, 0.05f, "0%", TipWornTexture(de));
            }
            UseCustomTextures = Toggle(UseCustomTextures,
                (de ? "Eigene Texturen verwenden" : "Use custom textures") + $" ({ZG2CustomTextures.UsedCount}/{ZG2CustomTextures.NewCount})",
                TipCustomTextures(de));
            bool markers = Toggle(ShowMapMarkers, de ? "Kartenmarker an Wagen mit Zg2" : "Map markers on cars with Zg2", TipMarkers(de));
            if (markers != ShowMapMarkers)
            {
                ShowMapMarkers = markers;
                ZG2MapMarkerManager.OnSettingChanged(ShowMapMarkers);
            }

            // ── Debug ──
            Header("Debug");
            DebugLogging = Toggle(DebugLogging, de ? "Ausführliches Log" : "Verbose log", TipDebug(de));

            if (GUI.changed) syncDirty = true;
            if (syncDirty && Time.unscaledTime >= nextSync)
            {
                nextSync = Time.unscaledTime + 0.5f;
                SendSync();
            }

            ZG2TooltipOverlay.DrawTooltip();
        }

        private void DrawJobTypes(bool de)
        {
            // Vanilla job types
            foreach (var v in ZG2JobTypes.Vanilla)
            {
                bool on = ZG2JobTypes.Contains(v.Key);
                bool now = Toggle(on, $"{v.Name} ({v.Abbrev})", TipVanillaType(de, v));
                if (now != on) ZG2JobTypes.Set(v.Key, now);
            }

            // Separator
            GUILayout.Space(4);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUILayout.Box(GUIContent.none, GUILayout.Height(1), GUILayout.Width(420));
            GUI.color = old;

            GUILayout.BeginHorizontal();
            GUILayout.Label(ZG2JobTypeNames.Get(ZG2JobTypeNames.ModHeader), GUILayout.ExpandWidth(false));
            Tip(TipModTypes(de));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Detected mod job types (automatic, persistent) – each with on/off
            ZG2JobTypes.DetectFromMods(); // once per session
            foreach (var t in ZG2JobTypes.KnownModTypes)
            {
                bool on = ZG2JobTypes.IsModTypeEnabled(t.Number, t.Abbrev);
                bool now = Toggle(on, t.Label, TipModType(de, t));
                if (now != on) ZG2JobTypes.SetModType(t.Number, t.Abbrev, now);
            }
        }

        #endregion

        #region GUI helpers (tooltip pattern as in LongerLoadingDelay)

        private static void EnsureStyles()
        {
            if (tipStyle != null) return;
            tipStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            tipStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);
            // Overflow instead of clipping: the skin's line height cuts off descenders (g, q, y).
            toggleStyle = new GUIStyle(GUI.skin.toggle) { clipping = TextClipping.Overflow };
            labelStyle = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Overflow };
            headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, richText = true, clipping = TextClipping.Overflow };
        }

        private static void Header(string text)
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>" + text + "</b>", headerStyle);
        }

        private static void Tip(string tip)
        {
            GUILayout.Label(new GUIContent(" ?", tip), tipStyle, GUILayout.ExpandWidth(false));
        }

        private static float LineHeight => Mathf.Ceil(GUI.skin.label.lineHeight) + 6f;

        private static bool Toggle(bool value, string label, string tip)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(LineHeight));
            bool v = GUILayout.Toggle(value, " " + label, toggleStyle, GUILayout.ExpandWidth(false), GUILayout.MinHeight(LineHeight));
            Tip(tip);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return v;
        }

        /// <summary>Label with its "?" directly behind it, in a group of fixed width (sliders line up).</summary>
        private static void SliderLabel(string label, string tip)
        {
            GUILayout.BeginHorizontal(GUILayout.Width(LabelWidth));
            GUILayout.Label(label, labelStyle, GUILayout.ExpandWidth(false), GUILayout.MinHeight(LineHeight));
            Tip(tip);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static int IntSlider(string label, int value, int min, int max, string tip)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(LineHeight));
            SliderLabel(label, tip);
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220)));
            GUILayout.Label(" " + v, labelStyle, GUILayout.Width(70));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return v;
        }

        private static float FloatSlider(string label, float value, float min, float max, float step, string format, string tip)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(LineHeight));
            SliderLabel(label, tip);
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220));
            if (step > 0f) v = Mathf.Round(v / step) * step;
            v = Mathf.Clamp(v, min, max);
            GUILayout.Label(" " + v.ToString(format), labelStyle, GUILayout.Width(70));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return v;
        }

        #endregion

        #region Tooltip texts

        private string TipDeparture(bool de) => de
            ? "<b>Ausfahrt-Check</b>\nSobald ein Zug mit Wagen eines Zg2-pflichtigen Jobs einen Bahnhof <b>vollständig</b> verlassen hat (kein Wagen mehr in der Ausfahrt-Zone), wird der Zugschluss einmal geprüft.\n• Fehlerhaft → jeder Job im Zug erhält einen Fehler-Checkpoint\n• Kurze Rangierfahrten aus dem Bahnhof heraus lösen nichts aus"
            : "<b>Departure check</b>\nAs soon as a train carrying cars of a Zg2 job has <b>completely</b> left a station (no car left in the departure zone), the train end is checked once.\n• Failed → every job in the train gets a failed checkpoint\n• Short shunting moves out of the station trigger nothing";

        private string TipArrival(bool de) => de
            ? "<b>Einfahrt-Check</b>\nSobald der erste Wagen eines solchen Zuges die Einfahrt-Zone eines Bahnhofs erreicht, wird der Zugschluss geprüft – auch am Zielbahnhof und bei Durchfahrten.\n• Entscheidend ist der Zustand <b>in diesem Moment</b>, nicht ob die Tafeln irgendwann einmal hingen\n• Wird nur ins Log (Player.log) geschrieben, <b>ohne Abzug</b> – eine Fahrt ohne Zugschluss wird nur einmal bei der Ausfahrt bestraft"
            : "<b>Arrival check</b>\nWhen the first car of such a train reaches a station's arrival zone, the train end is checked – also at the destination and when passing through.\n• Only the state <b>at that moment</b> counts, not whether boards were mounted at some point\n• Only written to the log (Player.log), <b>no deduction</b> – a run without train end is penalised once, at departure";

        private string TipRequired(bool de) => de
            ? $"<b>Erforderliche Tafeln</b>\nGenau so viele korrekt montierte Zg2 müssen am letzten Wagen hängen.\n• Vorbild DB: 2 (links und rechts)\n• Aktuell: <b>{RequiredBoards}</b> – {RequiredBoards + 1} korrekte Tafeln zählen ebenfalls als Fehler"
            : $"<b>Required boards</b>\nExactly this many correctly mounted Zg2 must be on the last car.\n• Prototype (DB): 2 (left and right)\n• Current: <b>{RequiredBoards}</b> – {RequiredBoards + 1} correct boards also count as failure";

        private string TipDistance(bool de) => de
            ? $"<b>Abstand zur letzten Kupplung</b>\nGemessen entlang der Wagenachse zwischen Tafel und freier Kupplung am Zugschluss.\n• Aktuell: max. <b>{MaxCouplerDistance:0.00} m</b> davor oder dahinter\n• Tafeln seitlich oder auf dem Wagen weiter vorn zählen nicht\n• Tipp: mit „Ausführliches Log“ steht der gemessene Wert im Log"
            : $"<b>Distance to last coupler</b>\nMeasured along the car axis between board and the free coupler at the train end.\n• Current: max. <b>{MaxCouplerDistance:0.00} m</b> in front or behind\n• Boards further along the car do not count\n• Tip: with \"Verbose log\" the measured value is logged";

        private string TipAngle(bool de) => de
            ? $"<b>Richtung</b>\nDie Vorderseite der Tafel muss nach hinten (vom Zug weg) zeigen.\n• Erlaubte Abweichung: <b>{MaxBoardAngle:0}°</b>\n• Seitlich montierte Tafeln (≈90°) oder nach vorn zeigende (≈180°) sind falsch"
            : $"<b>Direction</b>\nThe board must face backwards (away from the train).\n• Allowed deviation: <b>{MaxBoardAngle:0}°</b>\n• Side-mounted (≈90°) or forward-facing (≈180°) boards are wrong";

        private string TipLocoEnd(bool de) => de
            ? "<b>Lok als letztes Fahrzeug</b>\nZ. B. wenn der Zug zurückgeschoben wird und die Lok in Fahrtrichtung hinten ist (auch Tender).\n• <b>immer gültig</b>: keine Prüfung, die Lok gilt als Zugschluss\n• <b>rote Schlusslichter an</b>: an der Lok müssen auf dieser Seite die roten Lichter eingeschaltet sein – alternativ zählen korrekt montierte Zg2-Tafeln an der Lok\n• Erkennt die Mod an einer Lok keine roten Lichter, gilt der Zugschluss als fehlerhaft (die Lampenstruktur steht dann im Log)"
            : "<b>Locomotive as last vehicle</b>\nE.g. when pushing the train back and the loco is at the rear in direction of travel (tenders too).\n• <b>always valid</b>: no check, the loco counts as train end\n• <b>red tail lights on</b>: the loco's red lights on that side must be on – correctly mounted Zg2 boards on the loco count as well\n• If no red lights can be detected on a loco, the train end counts as failed (the lamp structure is written to the log)";

        private string TipPenalty(bool de)
        {
            string ex(int n) => $"{n} → -{ZG2JobLog.ComputePenaltyPercent(n, PenaltyPerCheckpoint, MaxPenalty):0.#}%";
            return de
                ? $"<b>Abzug pro Fehler</b>\nJeder Job sammelt eigene Fehler bei der <b>Ausfahrt</b> (Einfahrten werden nur ins Log geschrieben). Die Auszahlung wird beim Entwerten stillschweigend reduziert (keine Einblendung) und im Auftragsbericht als eigene Zeile auf der Vergütungsseite ausgewiesen.\n• {ex(1)}   • {ex(3)}   • {ex(10)}"
                : $"<b>Deduction per failure</b>\nEach job collects its own failed <b>departures</b> (arrivals are only written to the log). The payout is reduced silently at validation (no popup) and shown as an extra line on the payment page of the job report.\n• {ex(1)}   • {ex(3)}   • {ex(10)}";
        }

        private string TipMaxPenalty(bool de) => de
            ? $"<b>Obergrenze</b>\nMehr als <b>{MaxPenalty:0}%</b> wird nie abgezogen.\n• Erreicht nach {(PenaltyPerCheckpoint > 0 ? Mathf.CeilToInt(MaxPenalty / PenaltyPerCheckpoint).ToString() : "∞")} Fehlern"
            : $"<b>Cap</b>\nNever more than <b>{MaxPenalty:0}%</b> is deducted.\n• Reached after {(PenaltyPerCheckpoint > 0 ? Mathf.CeilToInt(MaxPenalty / PenaltyPerCheckpoint).ToString() : "∞")} failures";

        private string TipEnter(bool de) => de
            ? $"<b>Einfahrt-Zone</b>\nEin Zug gilt als eingefahren, sobald ein Wagen näher als <b>{StationEnterMargin:0} m</b> an einem Gleis des Bahnhofs ist.\n• Wird beim Schließen der Einstellungen neu berechnet"
            : $"<b>Arrival zone</b>\nA train has arrived as soon as a car is closer than <b>{StationEnterMargin:0} m</b> to a yard track of the station.\n• Recalculated when the settings are closed";

        private string TipExit(bool de) => de
            ? $"<b>Ausfahrt-Abstand</b>\nEin Zug gilt erst als ausgefahren, wenn <b>kein</b> Wagen mehr näher als <b>{StationExitMargin:0} m</b> an einem Gleis des Bahnhofs ist.\n• Dadurch zählen alle Gleisgruppen eines Bahnhofs zusammen – Durchfahren mehrerer Gruppen löst nichts aus\n• Eine Einfahrt wird nur nach einer gewerteten Ausfahrt aus einem <b>anderen</b> Bahnhof gewertet; Rückkehr zum Abfahrtsbahnhof wird nur geloggt"
            : $"<b>Departure distance</b>\nA train has only departed when <b>no</b> car is closer than <b>{StationExitMargin:0} m</b> to a track of the station.\n• All track groups of a station count as one – driving through several groups triggers nothing\n• An arrival is only scored after a scored departure from a <b>different</b> station; returning to the departure station is only logged";

        private static string TipVanillaType(bool de, ZG2JobTypes.VanillaType v) => de
            ? $"<b>{v.Name} ({v.Abbrev})</b>\nAufträge mit „{v.Abbrev}“ in der Auftragsnummer (z. B. GF-{v.Abbrev}-12) werden auf Zg2 geprüft und bei Fehlern gekürzt.\n• Standard: Güterverkehr und Logistik an, Rangieren aus (Rangierjobs verlassen den Bahnhof normalerweise nicht)"
            : $"<b>{v.Name} ({v.Abbrev})</b>\nJobs with \"{v.Abbrev}\" in the job ID (e.g. GF-{v.Abbrev}-12) are checked for Zg2 and reduced on failures.\n• Default: Freight and Logistical Haul on, shunting off (shunting jobs usually stay in the station)";

        private static string TipModTypes(bool de) => de
            ? "<b>Jobtypen anderer Mods</b>\nWird einmal beim Spielstart automatisch gefüllt: ZG2 schaut in allen geladenen Mods nach, welche Jobtypen sie mitbringen, und zeigt sie mit dem Namen der Mod, z. B. <b>Circle Hauls (CH)</b>.\n• Das Kürzel in Klammern stammt aus der Auftragsnummer und wird ergänzt, sobald ein Auftrag dieses Typs auftaucht\n• Neu erkannte Typen sind zunächst <b>aus</b>\n• Die Liste bleibt gespeichert, auch wenn eine Mod mal nicht aktiv ist"
            : "<b>Job types of other mods</b>\nFilled automatically once at game start: ZG2 looks into all loaded mods for the job types they add and shows them with the mod's name, e.g. <b>Circle Hauls (CH)</b>.\n• The abbreviation in brackets comes from the job ID and is added as soon as a job of this type appears\n• Newly detected types are <b>off</b> at first\n• The list is kept, even if a mod is not active";

        private static string TipModType(bool de, ZG2JobTypes.ModType t)
        {
            string title = t != null ? t.Label : ZG2Loc.T("Älterer Eintrag", "Older entry");
            string abbrev = t?.Abbrev;
            bool missing = t != null && !t.Installed && !string.IsNullOrEmpty(t.Name);
            return de
                ? $"<b>{title}</b>\nAufträge dieses Jobtyps werden auf Zg2 geprüft, wenn angehakt."
                  + (string.IsNullOrEmpty(abbrev) ? "\n• Kürzel noch unbekannt – wird beim ersten Auftrag dieses Typs ergänzt" : $"\n• Auftragsnummern wie XX-{abbrev}-12")
                  + (missing ? "\n• Die Mod ist gerade nicht aktiv" : "")
                : $"<b>{title}</b>\nJobs of this type are checked for Zg2 when ticked."
                  + (string.IsNullOrEmpty(abbrev) ? "\n• Abbreviation not known yet – added with the first job of this type" : $"\n• Job IDs like XX-{abbrev}-12")
                  + (missing ? "\n• The mod is currently not active" : "");
        }


        private static string TipSpawn(bool de) => de
            ? "<b>Bahnhofstafeln</b>\nAn jedem Bahnhof liegen gebrauchte (abgenutzte) Tafeln am Auftragsdrucker.\n• Neue Tafeln gibt es nur im Shop\n• Abgenutzte Tafeln werden nie im Shop angeboten"
            : "<b>Station boards</b>\nUsed (worn) boards are placed at each station's job printer.\n• New boards are only sold in shops\n• Worn boards are never offered in shops";

        private string TipPerStation(bool de) => de
            ? $"<b>Anzahl</b>\n<b>{BoardsPerStation}</b> Tafeln pro Bahnhof bei jedem Laden des Spielstands (klein/groß zufällig)."
            : $"<b>Count</b>\n<b>{BoardsPerStation}</b> boards per station on every load (small/large random).";

        private static string TipRandomWear(bool de) => de
            ? "<b>Zufällige Gebrauchsspuren</b>\nJede Bahnhofstafel bekommt einen eigenen Verschmutzungsgrad und Schmutzfarbton (dunkler, matter, gelblich).\n• Wird mit der Tafel gespeichert, auch montiert"
            : "<b>Random wear</b>\nEvery station board gets its own degree of dirt and dirt colour (darker, duller, yellowish).\n• Saved with the board, also when mounted";

        private string TipAging(bool de) => de
            ? "<b>Abnutzung</b>\nNeu gekaufte Tafeln verschmutzen mit jedem Kilometer, den ihr Wagen fährt, und bekommen ab dem eingestellten Grad die abgenutzte Textur."
            : "<b>Aging</b>\nNew boards get dirtier with every kilometre their car travels and switch to the worn texture at the set degree.";

        private string TipWearKm(bool de) => de
            ? $"<b>Lebensdauer</b>\nNach <b>{WearFullKm:0} km</b> montierter Fahrt ist eine Tafel voll abgenutzt.\n• Abgenutzte Textur ab {WearFullKm * WornTextureAt:0} km"
            : $"<b>Lifetime</b>\nAfter <b>{WearFullKm:0} km</b> of mounted travel a board is fully worn.\n• Worn texture from {WearFullKm * WornTextureAt:0} km";

        private string TipWornTexture(bool de) => de
            ? $"<b>Texturwechsel</b>\nAb <b>{WornTextureAt * 100f:0}%</b> Abnutzung wechselt eine neue Tafel auf die abgenutzte Textur (≈ {WearFullKm * WornTextureAt:0} km)."
            : $"<b>Texture switch</b>\nFrom <b>{WornTextureAt * 100f:0}%</b> wear a new board switches to the worn texture (≈ {WearFullKm * WornTextureAt:0} km).";

        private static string TipCustomTextures(bool de) => de
            ? "<b>Eigene Texturen</b>\nZusätzliche Texturen aus <b>Mods\\ZG2\\Textures\\</b> und aus jedem Texturpaket-Ordner <b>Mods\\…\\ZG2Textures\\</b>.\n• Zahlen: gebrauchte / neue Sets\n• Gebrauchte Sets: Bahnhofstafeln und abgenutzte gekaufte Tafeln würfeln zufällig zwischen Original und eigenen Sets\n• Dateien: <b>name_d</b> (Farbe, Pflicht), optional name_n / name_s / name_e – PNG, JPG oder TGA\n• Sets, die mit „new“ beginnen, gelten für neue Tafeln\n• Neue Dateien werden beim nächsten Spielstart erkannt"
            : "<b>Custom textures</b>\nAdditional textures from <b>Mods\\ZG2\\Textures\\</b> and from every texture pack folder <b>Mods\\…\\ZG2Textures\\</b>.\n• Numbers: used / new sets\n• Used sets: station boards and worn bought boards pick randomly between original and custom sets\n• Files: <b>name_d</b> (colour, required), optional name_n / name_s / name_e – PNG, JPG or TGA\n• Sets starting with \"new\" are for new boards\n• New files are detected on the next game start";

        private static string TipMarkers(bool de) => de
            ? "<b>Kartenmarker</b>\nRoter Punkt auf der Karte an jedem Wagen mit montierter Zg2."
            : "<b>Map markers</b>\nRed dot on the map at every car with a mounted Zg2.";

        private static string TipDebug(bool de) => de
            ? "<b>Ausführliches Log</b>\nSchreibt jeden Checkpoint und die gemessenen Abstände/Winkel jeder Tafel ins Player.log. Fehler-Checkpoints werden immer geloggt."
            : "<b>Verbose log</b>\nWrites every checkpoint and the measured distance/angle of each board to Player.log. Failed checkpoints are always logged.";

        #endregion
    }
}
