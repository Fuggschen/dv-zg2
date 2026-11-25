using DV.Customization.Gadgets;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Sits on the visual of every ZG2 gadget (placement preview and mounted board) and makes sure
    /// it shows the texture of the board item it came from.
    ///
    /// The gadget is built from one shared template that carries the original prefab materials.
    /// The game instantiates it for the placement preview and again when the board is mounted, and
    /// may restore the cached original materials after highlighting. Every time the renderer is
    /// found showing an original material while the board wants another (custom texture or aged
    /// texture), the board's materials are applied again. Other materials (e.g. highlight) are left alone.
    /// </summary>
    public class ZG2GadgetVisual : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;
        private const float PreviewSearchRadius = 4f;

        /// <summary>Board size of the template (set by the factory, copied on Instantiate).</summary>
        public bool Large;

        private Renderer targetRenderer;
        private Material templateMaterial;
        private GadgetBase gadget;
        private float nextCheck;

        private void Awake()
        {
            targetRenderer = GetComponent<Renderer>();
            if (targetRenderer != null) templateMaterial = targetRenderer.sharedMaterial;
            gadget = GetComponentInParent<GadgetBase>();
        }

        private void OnEnable()
        {
            nextCheck = 0f;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + CheckInterval;
            if (targetRenderer == null || !gameObject.scene.IsValid()) return;

            var board = FindBoard();
            if (board == null) return;

            var desired = ZG2BoardWear.DesiredMaterials(board);
            if (desired == null) return;

            var current = targetRenderer.sharedMaterial;
            if (current == desired[0] || !IsOriginal(current, board.IsLarge)) return;

            ZG2BoardWear.Apply(gadget != null ? gadget.gameObject : gameObject, board);
        }

        private bool IsOriginal(Material m, bool large)
        {
            if (m == null || m == templateMaterial) return true;
            var worn = ZG2BoardFactory.GetWornMaterials(large);
            return worn != null && worn.Length > 0 && m == worn[0];
        }

        private ZG2Board FindBoard()
        {
            if (gadget == null) gadget = GetComponentInParent<GadgetBase>();
            if (gadget != null && gadget.GadgetItem != null)
            {
                var linked = gadget.GadgetItem.GetComponent<ZG2Board>();
                if (linked != null) return linked;
            }

            // Placement preview: not linked to an item yet → the held board is the nearest active one.
            ZG2Board best = null;
            float bestSqr = PreviewSearchRadius * PreviewSearchRadius;
            Vector3 pos = transform.position;
            for (int i = ZG2Board.Active.Count - 1; i >= 0; i--)
            {
                var b = ZG2Board.Active[i];
                if (b == null) { ZG2Board.Active.RemoveAt(i); continue; }
                if (!b.isActiveAndEnabled || b.IsLarge != Large) continue;
                float d = (b.transform.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = b; }
            }
            return best;
        }
    }
}
