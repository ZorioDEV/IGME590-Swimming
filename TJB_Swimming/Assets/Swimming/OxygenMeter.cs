using UnityEngine;
using UnityEngine.Events;

namespace SwimmingSystem
{
    public class OxygenMeter : MonoBehaviour
    {
        [SerializeField] PlayerWaterDetector waterDetector;

        [Tooltip("Seconds the player can hold their breath.")]
        [SerializeField, Min(0.1f)] float maxOxygen = 20f;
        [Tooltip("Seconds of breath regained per second with the head above water.")]
        [SerializeField, Min(0f)] float refillRate = 8f;
        [Tooltip("Seconds between drowning ticks once oxygen runs out.")]
        [SerializeField, Min(0.1f)] float drowningTickInterval = 1f;

        [Header("Display (optional)")]
        [Tooltip("Your oxygen bar's root object. It is shown only while under water or refilling.")]
        [SerializeField] GameObject meterRoot;
        [Tooltip("Draws a simple bar at the bottom of the screen, so the meter works before you build any UI.")]
        [SerializeField] bool drawSimpleBar = true;

        [Header("Events")]
        [Tooltip("Sends 0-1 whenever oxygen changes. Hook to Slider.value or Image.fillAmount.")]
        [SerializeField] UnityEvent<float> onOxygenChanged = new UnityEvent<float>();
        [Tooltip("Called once when oxygen reaches zero.")]
        [SerializeField] UnityEvent onOutOfOxygen = new UnityEvent();
        [Tooltip("Called every Drowning Tick Interval while out of oxygen. Hook your damage here.")]
        [SerializeField] UnityEvent onDrowningTick = new UnityEvent();

        public float Oxygen { get; private set; }
        public float Normalized { get { return Oxygen / maxOxygen; } }
        public bool IsOutOfOxygen { get { return Oxygen <= 0f; } }

        float drowningTimer;
        bool showing;

        void Awake()
        {
            if (waterDetector == null)
            {
                waterDetector = GetComponentInParent<PlayerWaterDetector>();
            }
            Oxygen = maxOxygen;
        }

        void Start()
        {
            onOxygenChanged.Invoke(Normalized);
            SetShowing(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool underwater = waterDetector != null && waterDetector.IsHeadUnderwater;
            float oxygenBefore = Oxygen;

            if (underwater)
            {
                Oxygen = Mathf.Max(0f, Oxygen - dt);

                bool justRanOut = oxygenBefore > 0f && Oxygen <= 0f;
                if (justRanOut)
                {
                    drowningTimer = 0f;
                    onOutOfOxygen.Invoke();
                }

                if (Oxygen <= 0f)
                {
                    drowningTimer += dt;
                    if (drowningTimer >= drowningTickInterval)
                    {
                        drowningTimer -= drowningTickInterval;
                        onDrowningTick.Invoke();
                    }
                }
            }
            else
            {
                Oxygen = Mathf.Min(maxOxygen, Oxygen + refillRate * dt);
                drowningTimer = 0f;
            }

            if (!Mathf.Approximately(Oxygen, oxygenBefore))
            {
                onOxygenChanged.Invoke(Normalized);
            }

            SetShowing(underwater || Oxygen < maxOxygen);
        }

        public void Refill()
        {
            Oxygen = maxOxygen;
            drowningTimer = 0f;
            onOxygenChanged.Invoke(Normalized);
        }

        void SetShowing(bool show)
        {
            showing = show;
            if (meterRoot != null && meterRoot.activeSelf != show)
            {
                meterRoot.SetActive(show);
            }
        }

        void OnGUI()
        {
            if (!drawSimpleBar) return;
            if (!showing) return;

            float width = 240f;
            float height = 14f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - 60f;

            Rect background = new Rect(x, y, width, height);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(background, Texture2D.whiteTexture);

            if (Normalized > 0.25f)
            {
                GUI.color = new Color(0.35f, 0.8f, 1f);
            }
            else
            {
                GUI.color = new Color(1f, 0.35f, 0.3f);
            }

            Rect fill = new Rect(x + 2f, y + 2f, (width - 4f) * Normalized, height - 4f);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
