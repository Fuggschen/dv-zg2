using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Keeps a detached map-marker anchor GameObject tracking the position
    /// and forward direction of a <see cref="TrainCar"/>.
    /// Destroys itself when the target car is destroyed or goes null.
    /// </summary>
    public class ZG2MarkerAnchor : MonoBehaviour
    {
        private Transform target;

        public void SetTarget(TrainCar car)
        {
            target = car != null ? car.transform : null;
            if (target != null)
                SyncTransform();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                // Car was destroyed — clean up the anchor
                Destroy(gameObject);
                return;
            }

            SyncTransform();
        }

        private void SyncTransform()
        {
            transform.position = target.position;
            transform.rotation = target.rotation;
        }
    }
}
