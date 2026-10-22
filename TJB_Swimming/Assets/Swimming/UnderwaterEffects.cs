using UnityEngine;

namespace SwimmingSystem
{
    public class UnderwaterEffects : MonoBehaviour
    {
        [SerializeField] PlayerWaterDetector waterDetector;

        [Header("Fog (Built-in and URP)")]
        [SerializeField] bool useFog = true;
        [SerializeField] Color fogColor = new Color(0.06f, 0.3f, 0.38f);
        [SerializeField, Min(0f)] float fogDensity = 0.07f;

        [Header("Objects")]
        [Tooltip("Switched on only while the head is under water: a tinted full-screen UI Image, a URP/HDRP " +
                 "post-processing Volume, bubble particles, an underwater ambience AudioSource...")]
        [SerializeField] GameObject[] enableWhileUnderwater = new GameObject[0];

        [Header("Muffled Audio")]
        [SerializeField] bool muffleAudio = true;
        [Tooltip("Leave empty to use the Main Camera's AudioListener.")]
        [SerializeField] AudioListener listener;
        [SerializeField, Range(100f, 22000f)] float muffledCutoff = 900f;

        [Header("Sounds (optional)")]
        [Tooltip("Leave empty and one is created automatically if any clip is set.")]
        [SerializeField] AudioSource sfxSource;
        [SerializeField] AudioClip enterWaterClip;
        [SerializeField] AudioClip exitWaterClip;
        [SerializeField] AudioClip headUnderClip;
        [SerializeField] AudioClip headUpClip;
        [Tooltip("Entry speed (m/s) at which the enter splash plays at full volume.")]
        [SerializeField, Min(0.1f)] float loudSplashSpeed = 8f;

        CharacterController body;
        AudioLowPassFilter lowPass;

        bool effectsOn;
        bool fogApplied;
        bool muffleApplied;

        bool savedFog;
        FogMode savedFogMode;
        Color savedFogColor;
        float savedFogDensity;

        bool savedLowPassEnabled;
        float savedCutoff;

        void Awake()
        {
            if (waterDetector == null)
            {
                waterDetector = GetComponentInParent<PlayerWaterDetector>();
            }
            if (waterDetector != null)
            {
                body = waterDetector.GetComponent<CharacterController>();
            }
            if (listener == null && Camera.main != null)
            {
                listener = Camera.main.GetComponent<AudioListener>();
            }

            bool hasClips = enterWaterClip != null || exitWaterClip != null || headUnderClip != null || headUpClip != null;
            if (sfxSource == null && hasClips)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
                sfxSource.playOnAwake = false;
                sfxSource.spatialBlend = 0f;
            }
        }

        void OnEnable()
        {
            if (waterDetector == null) return;

            waterDetector.WaterEntered += OnWaterEntered;
            waterDetector.WaterExited += OnWaterExited;
            waterDetector.HeadSubmerged += OnHeadSubmerged;
            waterDetector.HeadSurfaced += OnHeadSurfaced;
        }

        void OnDisable()
        {
            if (waterDetector != null)
            {
                waterDetector.WaterEntered -= OnWaterEntered;
                waterDetector.WaterExited -= OnWaterExited;
                waterDetector.HeadSubmerged -= OnHeadSubmerged;
                waterDetector.HeadSurfaced -= OnHeadSurfaced;
            }

            if (effectsOn)
            {
                TurnEffectsOff();
            }
        }

        void LateUpdate()
        {
            bool underwater = false;
            if (waterDetector != null && waterDetector.isActiveAndEnabled)
            {
                underwater = waterDetector.IsHeadUnderwater;
            }

            if (underwater && !effectsOn)
            {
                TurnEffectsOn();
            }
            else if (!underwater && effectsOn)
            {
                TurnEffectsOff();
            }
        }

        void TurnEffectsOn()
        {
            effectsOn = true;

            if (useFog)
            {
                savedFog = RenderSettings.fog;
                savedFogMode = RenderSettings.fogMode;
                savedFogColor = RenderSettings.fogColor;
                savedFogDensity = RenderSettings.fogDensity;

                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                fogApplied = true;
            }

            SetObjectsActive(true);

            if (muffleAudio && listener != null)
            {
                if (lowPass == null)
                {
                    lowPass = listener.GetComponent<AudioLowPassFilter>();
                }
                if (lowPass == null)
                {
                    lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>();
                    lowPass.enabled = false;
                }

                savedLowPassEnabled = lowPass.enabled;
                savedCutoff = lowPass.cutoffFrequency;

                lowPass.cutoffFrequency = muffledCutoff;
                lowPass.enabled = true;
                muffleApplied = true;
            }
        }

        void TurnEffectsOff()
        {
            effectsOn = false;

            if (fogApplied)
            {
                RenderSettings.fog = savedFog;
                RenderSettings.fogMode = savedFogMode;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogDensity = savedFogDensity;
                fogApplied = false;
            }

            SetObjectsActive(false);

            if (muffleApplied)
            {
                if (lowPass != null)
                {
                    lowPass.cutoffFrequency = savedCutoff;
                    lowPass.enabled = savedLowPassEnabled;
                }
                muffleApplied = false;
            }
        }

        void SetObjectsActive(bool active)
        {
            for (int i = 0; i < enableWhileUnderwater.Length; i++)
            {
                if (enableWhileUnderwater[i] != null)
                {
                    enableWhileUnderwater[i].SetActive(active);
                }
            }
        }

        void OnWaterEntered()
        {
            float speed = 0f;
            if (body != null)
            {
                speed = body.velocity.magnitude;
            }

            float loudness = Mathf.InverseLerp(1f, loudSplashSpeed, speed);
            float volume = Mathf.Lerp(0.35f, 1f, loudness);
            Play(enterWaterClip, volume);
        }

        void OnWaterExited()
        {
            Play(exitWaterClip, 0.6f);
        }

        void OnHeadSubmerged()
        {
            Play(headUnderClip, 0.8f);
        }

        void OnHeadSurfaced()
        {
            Play(headUpClip, 0.8f);
        }

        void Play(AudioClip clip, float volume)
        {
            if (clip == null) return;
            if (sfxSource == null) return;

            sfxSource.PlayOneShot(clip, volume);
        }
    }
}
