using System.Collections.Generic;
using System.Globalization;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Visual wear of a board: <see cref="Level"/> 0 = factory new … 1 = fully worn,
    /// <see cref="Hue"/> 0 … 1 = individual dirt colour (grey → yellow-brown).
    /// </summary>
    public struct ZG2Wear
    {
        public float Level;
        public float Hue;

        public static ZG2Wear NewBoard() => new ZG2Wear { Level = 0f, Hue = Random.value };

        /// <summary>Used station board: random degree of use.</summary>
        public static ZG2Wear UsedBoard() => new ZG2Wear { Level = Random.Range(0.45f, 1f), Hue = Random.value };

        public Color Tint
        {
            get
            {
                float bright = Mathf.Lerp(1f, 0.70f, Level);
                float dirt = Hue * Level;
                return new Color(bright, bright * Mathf.Lerp(1f, 0.94f, dirt), bright * Mathf.Lerp(1f, 0.82f, dirt), 1f);
            }
        }

        public float GlossFactor => Mathf.Lerp(1f, 0.5f, Level);

        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        public string Serialize() => Level.ToString("0.####", IC) + "," + Hue.ToString("0.###", IC);

        public static bool TryParse(string s, out ZG2Wear wear)
        {
            wear = default;
            if (string.IsNullOrEmpty(s)) return false;
            var p = s.Split(',');
            if (p.Length < 2) return false;
            if (!float.TryParse(p[0], NumberStyles.Float, IC, out float level) ||
                !float.TryParse(p[1], NumberStyles.Float, IC, out float hue)) return false;
            wear = new ZG2Wear { Level = Mathf.Clamp01(level), Hue = Mathf.Clamp01(hue) };
            return true;
        }
    }

    /// <summary>
    /// Applies, ages and persists board wear.
    ///   • Station boards are always used boards (worn texture + random wear level).
    ///   • Shop boards start new and age with the distance driven while mounted on a car;
    ///     from <see cref="ZG2Settings.WornTextureAt"/> they switch to the worn texture.
    ///   • Item wear is saved in the item's own save data (<see cref="ZG2Board"/>), wear of
    ///     mounted boards in the ZG2 save block (car ID + car-local position).
    /// </summary>
    public static class ZG2BoardWear
    {
        private static readonly int ColorID = Shader.PropertyToID("_Color");
        private static readonly int GlossID = Shader.PropertyToID("_Glossiness");
        private static MaterialPropertyBlock mpb;

        private class SavedMount
        {
            public string CarId;
            public Vector3 LocalPos;
            public ZG2Wear Wear;
            public string Tex;
        }

        /// <summary>ZG2 gadgets currently mounted (for aging and saving).</summary>
        private static readonly HashSet<GadgetBase> mounted = new HashSet<GadgetBase>();
        private static readonly List<GadgetBase> deadGadgets = new List<GadgetBase>();
        private static readonly List<SavedMount> pending = new List<SavedMount>();
        private static int pendingTries;
        private static int scanCountdown;

        #region Visuals

        /// <summary>Applies tint/gloss (and the worn texture if due) to all renderers below root.</summary>
        public static void Apply(GameObject root, ZG2Board board)
        {
            if (root == null || board == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            var wear = board.Wear;
            Material[] wornMats = DesiredMaterials(board);

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;

                if (wornMats != null && wornMats.Length > 0 && r.sharedMaterial != wornMats[0])
                    r.sharedMaterials = wornMats;

                float baseGloss = 0.5f;
                var mat = r.sharedMaterial;
                if (mat != null && mat.HasProperty(GlossID)) baseGloss = mat.GetFloat(GlossID);

                r.GetPropertyBlock(mpb);
                mpb.SetColor(ColorID, wear.Tint);
                mpb.SetFloat(GlossID, baseGloss * wear.GlossFactor);
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>
        /// Materials the board must show instead of its prefab materials, or null to keep them.
        /// Custom texture set wins; otherwise new boards switch to the original worn texture when aged.
        /// </summary>
        public static Material[] DesiredMaterials(ZG2Board board)
        {
            if (board == null) return null;
            Material[] mats = ZG2CustomTextures.GetMaterials(board.TextureSet, board.IsLarge);
            if (mats == null && !board.IsWornVariant && board.Wear.Level >= Main.Settings.WornTextureAt)
                mats = ZG2BoardFactory.GetWornMaterials(board.IsLarge);
            return mats != null && mats.Length > 0 ? mats : null;
        }

        private static ZG2Board BoardOf(GadgetBase gadget)
            => gadget != null && gadget.GadgetItem != null ? gadget.GadgetItem.GetComponent<ZG2Board>() : null;

        #endregion

        #region Mount tracking

        public static void OnBoardMounted(GadgetBase gadget)
        {
            var board = BoardOf(gadget);
            if (board == null) return;
            mounted.Add(gadget);
            Apply(gadget.gameObject, board);
        }

        public static void OnBoardRemoved(GadgetBase gadget, ZG2Board board)
        {
            mounted.Remove(gadget);
            if (board != null) Apply(board.gameObject, board); // the item shows the wear it got while mounted
        }

        /// <summary>
        /// Called once per tracker poll: ages mounted boards by the distance their car moved,
        /// re-applies saved wear after a load and picks up gadgets restored by the game.
        /// </summary>
        public static void Tick(float deltaSeconds)
        {
            if (--scanCountdown <= 0)
            {
                scanCountdown = 30;
                ScanMountedGadgets();
            }

            TryResolvePending();

            if (!Main.Settings.BoardAging || mounted.Count == 0) return;
            float fullMeters = Mathf.Max(1f, Main.Settings.WearFullKm) * 1000f;

            deadGadgets.Clear();
            foreach (var g in mounted)
            {
                if (g == null || g.TrainCar == null) { deadGadgets.Add(g); continue; }
                var board = BoardOf(g);
                if (board == null || board.Wear.Level >= 1f) continue;

                float meters = Mathf.Abs(g.TrainCar.GetForwardSpeed()) * deltaSeconds;
                if (meters <= 0.01f) continue;

                float before = board.Wear.Level;
                board.AddWear(meters / fullMeters);

                // Visual update in visible steps only
                if (Mathf.FloorToInt(before * 100f) != Mathf.FloorToInt(board.Wear.Level * 100f) ||
                    (before < Main.Settings.WornTextureAt && board.Wear.Level >= Main.Settings.WornTextureAt))
                    Apply(g.gameObject, board);
            }
            foreach (var g in deadGadgets) mounted.Remove(g);
        }

        private static void ScanMountedGadgets()
        {
            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner == null) return;
            foreach (var car in spawner.AllCars)
            {
                var list = GetCustomizers(car);
                if (list == null) continue;
                foreach (var obj in list)
                {
                    if (obj is GadgetBase g && ZG2Board.IsZG2Board(g) && mounted.Add(g))
                        Apply(g.gameObject, BoardOf(g));
                }
            }
        }

        private static System.Collections.IList GetCustomizers(TrainCar car)
        {
            if (car == null) return null;
            var c = car.GetComponent<TrainCarCustomization>();
            return c != null ? c.Customizers : null;
        }

        public static void Clear()
        {
            mounted.Clear();
            pending.Clear();
            pendingTries = 0;
            scanCountdown = 0;
        }

        #endregion

        #region Save / load

        public static JArray Serialize()
        {
            var arr = new JArray();
            foreach (var g in mounted)
            {
                var board = BoardOf(g);
                var car = g != null ? g.TrainCar : null;
                if (board == null || car == null || car.logicCar == null) continue;
                Write(arr, car.logicCar.ID, car.transform.InverseTransformPoint(g.transform.position), board.Wear, board.TextureSet);
            }
            foreach (var p in pending) // not yet matched after load → keep
                Write(arr, p.CarId, p.LocalPos, p.Wear, p.Tex);
            return arr;
        }

        private static void Write(JArray arr, string carId, Vector3 lp, ZG2Wear wear, string tex)
        {
            arr.Add(new JObject
            {
                ["car"] = carId,
                ["x"] = lp.x, ["y"] = lp.y, ["z"] = lp.z,
                ["wear"] = wear.Serialize(),
                ["tex"] = tex ?? string.Empty
            });
        }

        public static void Deserialize(JArray arr)
        {
            pending.Clear();
            pendingTries = 0;
            if (arr == null) return;
            foreach (var token in arr)
            {
                if (!(token is JObject o)) continue;
                string car = o.Value<string>("car");
                if (string.IsNullOrEmpty(car) || !ZG2Wear.TryParse(o.Value<string>("wear"), out var wear)) continue;
                pending.Add(new SavedMount
                {
                    CarId = car,
                    LocalPos = new Vector3(o.Value<float?>("x") ?? 0f, o.Value<float?>("y") ?? 0f, o.Value<float?>("z") ?? 0f),
                    Wear = wear,
                    Tex = o.Value<string>("tex") ?? string.Empty
                });
            }
        }

        private static void TryResolvePending()
        {
            if (pending.Count == 0) return;
            if (++pendingTries > 120)
            {
                Main.LogWarning($"Zg2: {pending.Count} mounted board(s) could not be matched for wear restore.");
                pending.Clear();
                return;
            }

            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner == null) return;

            var byId = new Dictionary<string, TrainCar>();
            foreach (var c in spawner.AllCars)
                if (c != null && c.logicCar != null) byId[c.logicCar.ID] = c;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (!byId.TryGetValue(p.CarId, out var car)) continue;
                var list = GetCustomizers(car);
                if (list == null) continue;

                foreach (var obj in list)
                {
                    if (!(obj is GadgetBase g) || !ZG2Board.IsZG2Board(g)) continue;
                    var lp = car.transform.InverseTransformPoint(g.transform.position);
                    if ((lp - p.LocalPos).sqrMagnitude > 0.01f) continue;

                    var board = BoardOf(g);
                    board.SetWearAndTexture(p.Wear, p.Tex);
                    mounted.Add(g);
                    Apply(g.gameObject, board);
                    pending.RemoveAt(i);
                    break;
                }
            }
        }

        #endregion
    }
}
