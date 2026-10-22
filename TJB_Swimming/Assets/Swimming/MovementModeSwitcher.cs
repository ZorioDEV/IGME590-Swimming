using System;
using UnityEngine;
using UnityEngine.Events;

namespace SwimmingSystem
{
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerWaterDetector), typeof(SwimController))]
    public class MovementModeSwitcher : MonoBehaviour
    {
        [Header("Controllers")]
        [Tooltip("Your existing ground / first-person controller script. It is turned off while swimming.")]
        [SerializeField] Behaviour groundController;
        [Tooltip("Optional extra components to turn off while swimming (footsteps, head bob, a separate gravity script...).")]
        [SerializeField] Behaviour[] alsoDisableWhileSwimming = new Behaviour[0];
        [SerializeField] SwimController swimController;
        [SerializeField] PlayerWaterDetector waterDetector;

        [Header("Depth Thresholds (fraction of the body under water)")]
        [Tooltip("Start swimming when at least this much of the player's capsule is under water.")]
        [SerializeField, Range(0.05f, 1f)] float startSwimmingAt = 0.6f;
        [Tooltip("Go back to walking when this much or less is under water. Keeping it below Start Swimming At " +
                 "stops the player flickering between modes when standing right at the threshold.")]
        [SerializeField, Range(0f, 0.95f)] float stopSwimmingAt = 0.4f;

        [Header("Events (optional)")]
        [Tooltip("Called right after swimming starts.")]
        [SerializeField] UnityEvent onStartedSwimming = new UnityEvent();
        [Tooltip("Called right after the ground controller takes back over. Handy for clearing your controller's " +
                 "leftover input, e.g. StarterAssetsInputs.JumpInput(false).")]
        [SerializeField] UnityEvent onStoppedSwimming = new UnityEvent();

        public bool IsSwimming { get; private set; }

        public event Action<bool> SwimmingChanged;

        bool[] extrasWereEnabled = new bool[0];

        void Reset()
        {
            waterDetector = GetComponent<PlayerWaterDetector>();
            swimController = GetComponent<SwimController>();
        }

        void Awake()
        {
            if (waterDetector == null) waterDetector = GetComponent<PlayerWaterDetector>();
            if (swimController == null) swimController = GetComponent<SwimController>();
        }

        void Start()
        {
            IsSwimming = false;
            if (swimController != null)
            {
                swimController.enabled = false;
            }
        }

        void Update()
        {
            if (waterDetector == null) return;
            if (swimController == null) return;

            float submerged = waterDetector.SubmergedFraction;

            if (!IsSwimming && submerged >= startSwimmingAt)
            {
                SetSwimming(true);
            }
            else if (IsSwimming && ShouldStopSwimming(submerged))
            {
                SetSwimming(false);
            }
        }

        bool ShouldStopSwimming(float submerged)
        {
            if (submerged > stopSwimmingAt) return false;
            if (waterDetector.IsHeadUnderwater) return false;
            if (swimController.IsClimbing) return false;
            return true;
        }

        public void SetSwimming(bool swimming)
        {
            if (swimming == IsSwimming) return;
            if (swimController == null) return;

            IsSwimming = swimming;

            if (swimming)
            {
                TurnOffGroundSide();
                swimController.enabled = true;
            }
            else
            {
                swimController.enabled = false;
                TurnOnGroundSide();
            }

            if (SwimmingChanged != null) SwimmingChanged.Invoke(swimming);

            if (swimming)
            {
                onStartedSwimming.Invoke();
            }
            else
            {
                onStoppedSwimming.Invoke();
            }
        }

        void TurnOffGroundSide()
        {
            if (groundController != null)
            {
                groundController.enabled = false;
            }

            extrasWereEnabled = new bool[alsoDisableWhileSwimming.Length];
            for (int i = 0; i < alsoDisableWhileSwimming.Length; i++)
            {
                Behaviour extra = alsoDisableWhileSwimming[i];
                if (extra == null) continue;

                extrasWereEnabled[i] = extra.enabled;
                extra.enabled = false;
            }
        }

        void TurnOnGroundSide()
        {
            if (groundController != null)
            {
                groundController.enabled = true;
            }

            for (int i = 0; i < alsoDisableWhileSwimming.Length; i++)
            {
                Behaviour extra = alsoDisableWhileSwimming[i];
                if (extra == null) continue;
                if (i >= extrasWereEnabled.Length) continue;

                if (extrasWereEnabled[i])
                {
                    extra.enabled = true;
                }
            }
        }

        void OnValidate()
        {
            float highestAllowed = Mathf.Max(0f, startSwimmingAt - 0.05f);
            stopSwimmingAt = Mathf.Clamp(stopSwimmingAt, 0f, highestAllowed);
        }
    }
}
