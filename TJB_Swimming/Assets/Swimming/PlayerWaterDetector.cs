using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwimmingSystem
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class PlayerWaterDetector : MonoBehaviour
    {
        [Tooltip("The player's body collider. Leave empty to use the CharacterController (or first Collider) on this GameObject.")]
        [SerializeField] Collider playerCollider;

        [Tooltip("A transform at the player's eyes: the camera, or the camera root if a Cinemachine camera follows it. " +
                 "Leave empty to use the Main Camera.")]
        [SerializeField] Transform eyeLevel;

        [Header("Live State (read-only, watch these while testing)")]
        [SerializeField] List<WaterVolume> waterVolumes = new List<WaterVolume>();
        [SerializeField, Range(0f, 1f)] float submergedFraction;
        [SerializeField] bool inWater;
        [SerializeField] bool headUnderwater;

        const float OverlapMargin = 0.25f;

        public event Action WaterEntered;
        public event Action WaterExited;
        public event Action HeadSubmerged;
        public event Action HeadSurfaced;

        public float SubmergedFraction { get { return submergedFraction; } }
        public bool IsInWater { get { return inWater; } }
        public bool IsHeadUnderwater { get { return headUnderwater; } }

        public WaterVolume ActiveVolume { get; private set; }

        public float SurfaceHeight
        {
            get
            {
                if (ActiveVolume != null)
                {
                    return ActiveVolume.SurfaceHeight;
                }
                else
                {
                    return float.NegativeInfinity;
                }
            }
        }

        public IReadOnlyList<WaterVolume> WaterVolumes { get { return waterVolumes; } }
        public Collider PlayerCollider { get { return playerCollider; } }

        public Vector3 EyePosition
        {
            get
            {
                if (eyeLevel != null)
                {
                    return eyeLevel.position;
                }

                Bounds body = playerCollider.bounds;
                float eyeY = body.min.y + body.size.y * 0.9f;
                return new Vector3(body.center.x, eyeY, body.center.z);
            }
        }

        void Awake()
        {
            if (playerCollider == null) playerCollider = GetComponent<CharacterController>();
            if (playerCollider == null) playerCollider = GetComponent<Collider>();
            if (playerCollider == null)
            {
                enabled = false;
                return;
            }

            if (eyeLevel == null && Camera.main != null)
            {
                eyeLevel = Camera.main.transform;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            WaterVolume volume = other.GetComponent<WaterVolume>();
            if (volume != null && !waterVolumes.Contains(volume))
            {
                waterVolumes.Add(volume);
            }
        }

        void OnTriggerExit(Collider other)
        {
            WaterVolume volume = other.GetComponent<WaterVolume>();
            if (volume != null)
            {
                waterVolumes.Remove(volume);
            }
        }

        void Update()
        {
            RemoveStaleVolumes();
            ActiveVolume = FindHighestVolume();
            submergedFraction = CalculateSubmergedFraction();

            bool nowInWater = submergedFraction > 0f;
            bool nowHeadUnderwater = nowInWater && IsPointUnderwater(EyePosition);

            if (nowInWater && !inWater)
            {
                inWater = true;
                if (WaterEntered != null) WaterEntered.Invoke();
            }

            if (nowHeadUnderwater && !headUnderwater)
            {
                headUnderwater = true;
                if (HeadSubmerged != null) HeadSubmerged.Invoke();
            }
            else if (!nowHeadUnderwater && headUnderwater)
            {
                headUnderwater = false;
                if (HeadSurfaced != null) HeadSurfaced.Invoke();
            }

            if (!nowInWater && inWater)
            {
                inWater = false;
                if (WaterExited != null) WaterExited.Invoke();
            }
        }

        float CalculateSubmergedFraction()
        {
            if (ActiveVolume == null) return 0f;

            Bounds body = playerCollider.bounds;
            if (body.size.y <= 0f) return 0f;

            float waterDepthOnBody = ActiveVolume.SurfaceHeight - body.min.y;
            return Mathf.Clamp01(waterDepthOnBody / body.size.y);
        }

        public bool IsPointUnderwater(Vector3 point)
        {
            for (int i = 0; i < waterVolumes.Count; i++)
            {
                if (waterVolumes[i].ContainsPoint(point))
                {
                    return true;
                }
            }
            return false;
        }

        void RemoveStaleVolumes()
        {
            Bounds playerBounds = playerCollider.bounds;
            playerBounds.Expand(OverlapMargin);

            for (int i = waterVolumes.Count - 1; i >= 0; i--)
            {
                if (!IsVolumeStillValid(waterVolumes[i], playerBounds))
                {
                    waterVolumes.RemoveAt(i);
                }
            }
        }

        bool IsVolumeStillValid(WaterVolume volume, Bounds playerBounds)
        {
            if (volume == null) return false;
            if (!volume.isActiveAndEnabled) return false;
            if (volume.Collider == null) return false;
            if (!volume.Collider.enabled) return false;

            if (playerCollider.enabled && !playerBounds.Intersects(volume.Collider.bounds))
            {
                return false;
            }
            return true;
        }

        WaterVolume FindHighestVolume()
        {
            WaterVolume highest = null;
            float highestSurface = float.NegativeInfinity;

            for (int i = 0; i < waterVolumes.Count; i++)
            {
                float surface = waterVolumes[i].SurfaceHeight;
                if (surface > highestSurface)
                {
                    highestSurface = surface;
                    highest = waterVolumes[i];
                }
            }
            return highest;
        }

        void OnDrawGizmosSelected()
        {
            if (eyeLevel == null) return;

            if (headUnderwater)
            {
                Gizmos.color = Color.cyan;
            }
            else
            {
                Gizmos.color = Color.yellow;
            }
            Gizmos.DrawWireSphere(eyeLevel.position, 0.1f);
        }
    }
}
