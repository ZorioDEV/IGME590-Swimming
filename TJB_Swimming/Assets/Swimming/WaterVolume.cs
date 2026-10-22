using UnityEngine;

namespace SwimmingSystem
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class WaterVolume : MonoBehaviour
    {
        [Tooltip("Optional. The water surface is this transform's world Y (e.g. your visible water plane). " +
                 "Leave empty to use the top of this trigger collider.")]
        [SerializeField] Transform surfaceOverride;

        Collider volumeCollider;

        public Collider Collider
        {
            get
            {
                if (volumeCollider == null)
                {
                    volumeCollider = GetComponent<Collider>();
                }
                return volumeCollider;
            }
        }

        public float SurfaceHeight
        {
            get
            {
                if (surfaceOverride != null)
                {
                    return surfaceOverride.position.y;
                }
                else
                {
                    return Collider.bounds.max.y;
                }
            }
        }

        public bool IsConvex
        {
            get
            {
                MeshCollider meshCollider = Collider as MeshCollider;
                if (meshCollider == null)
                {
                    return true;
                }
                else
                {
                    return meshCollider.convex;
                }
            }
        }

        public bool ContainsPoint(Vector3 point)
        {
            Collider col = Collider;
            if (col == null) return false;
            if (point.y > SurfaceHeight) return false;
            if (!col.bounds.Contains(point)) return false;

            Vector3 closestPoint = col.ClosestPoint(point);
            float distance = Vector3.Distance(closestPoint, point);
            return distance < 0.01f;
        }

        void Reset()
        {
            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0)
            {
                gameObject.layer = waterLayer;
            }
            MakeValidTrigger();
        }

        void Awake()
        {
            MakeValidTrigger();
        }

        void MakeValidTrigger()
        {
            Collider col = Collider;
            if (col == null) return;

            if (!IsConvex)
            {
                MeshCollider meshCollider = (MeshCollider)col;
                meshCollider.convex = true;
            }

            if (!col.isTrigger)
            {
                col.isTrigger = true;
            }
        }

        void OnDrawGizmos()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;
            if (!col.enabled) return;

            Bounds b = col.bounds;

            float surface;
            if (surfaceOverride != null)
            {
                surface = surfaceOverride.position.y;
            }
            else
            {
                surface = b.max.y;
            }

            bool valid = col.isTrigger;
            MeshCollider meshCollider = col as MeshCollider;
            if (meshCollider != null && !meshCollider.convex)
            {
                valid = false;
            }

            if (valid)
            {
                Gizmos.color = new Color(0.1f, 0.6f, 1f, 0.25f);
            }
            else
            {
                Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.5f);
            }

            Vector3 center = new Vector3(b.center.x, surface, b.center.z);
            Vector3 size = new Vector3(b.size.x, 0.02f, b.size.z);
            Gizmos.DrawCube(center, size);
        }

        void OnDrawGizmosSelected()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = new Color(0.1f, 0.6f, 1f, 0.8f);
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
    }
}
