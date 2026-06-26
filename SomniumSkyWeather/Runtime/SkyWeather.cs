using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.Bently.Weather
{
    /// <summary>
    /// Bently.Weather — a single, self-contained sky &amp; weather driver for Somnium worlds.
    /// Drives a procedural skybox (Bently/SkyDome), a sun/moon directional light, ambient
    /// lighting, fog, and weather blending. Pure UnityEngine (no SomniumBridge dependency) so
    /// it compiles anywhere, is validator-clean, and renders through RenderSettings.skybox —
    /// the path proven to work inside Somnium's URP with no render feature.
    ///
    /// Add ONE of these to your world. Optional Somnium networking/player-follow is added by a
    /// separate component that talks to this one.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Bently/Sky & Weather")]
    [DisallowMultipleComponent]
    public class SkyWeather : MonoBehaviour
    {
        public static SkyWeather Instance { get; private set; }

        // ---------------- Time ----------------
        [Header("Time of Day")]
        [Range(0f, 24f)] public float timeOfDay = 13.0f;
        [Tooltip("Advance time automatically (always in Play; in Editor only if 'Animate In Editor').")]
        public bool autoAdvance = true;
        [Tooltip("Real seconds for one full 24h cycle.")]
        public float dayLengthSeconds = 600f;
        [Tooltip("Compass direction of the sun's arc, degrees.")]
        [Range(0f, 360f)] public float sunAzimuth = 160f;
        [Tooltip("Run the cycle in the Editor without entering Play mode.")]
        public bool animateInEditor = false;

        // ---------------- References ----------------
        [Header("References (auto-created if empty)")]
        public Light sunLight;
        [Tooltip("Optional. If empty, a runtime instance of Bently/SkyDome is created and used.")]
        public Material skyboxMaterialOverride;
        [Tooltip("Baked 3D cloud noise (Tools > Bently Sky & Weather > Bake Textures).")]
        public Texture3D cloudNoise;
        [Tooltip("Baked star + Milky Way cubemap.")]
        public Cubemap starSky;
        [Tooltip("Baked moon albedo texture.")]
        public Texture2D moonTexture;

        // ---------------- Lighting ----------------
        [Header("Lighting")]
        public Gradient sunColor;
        public AnimationCurve sunIntensity = AnimationCurve.Linear(0, 0, 1, 1.3f);
        public Color moonColor = new Color(0.55f, 0.62f, 0.85f, 1f);
        [Range(0f, 1f)] public float moonIntensity = 0.12f;
        [Range(0f, 2f)] public float ambientStrength = 1f;
        [Range(0f, 3f)] public float baseExposure = 1.15f;

        // ---------------- Wind ----------------
        [Header("Wind")]
        [Range(0f, 360f)] public float windHeading = 90f;
        [Range(0f, 0.2f)] public float cloudSpeed = 0.03f;
        [Tooltip("Allow storm lightning flashes (driven by each weather's Lightning value).")]
        public bool lightningEnabled = true;

        // ---------------- Cloud look (global; weather drives coverage/density/colour) ----------------
        [Header("Cloud Look")]
        [Range(0f, 1f)] public float cloudType = 0.8f;            // flat stratus .. tall cumulus
        [Range(0.05f, 4f)] public float cloudScale = 2.0f;
        [Range(0f, 1f)] public float cloudDetail = 0.45f;
        [Range(0.1f, 6f)] public float cloudBottom = 1.3f;
        [Range(0.1f, 6f)] public float cloudThickness = 2.4f;
        [Range(0.1f, 6f)] public float cloudAbsorption = 1.3f;
        [Range(0f, 0.2f)] public float cloudFade = 0.045f;
        [Range(20f, 400f)] public float curvatureRadius = 90f;
        [Tooltip("Cloud raymarch steps. Lower = faster — use ~24 for VR.")]
        [Range(12, 64)] public int cloudSteps = 40;

        // ---------------- Sun / Moon / Stars look ----------------
        [Header("Sun / Moon / Stars Look")]
        [Range(0.0002f, 0.02f)] public float sunSize = 0.0009f;
        [Range(0.0005f, 0.03f)] public float moonSize = 0.0016f;
        [Range(0f, 4f)] public float starIntensity = 1.4f;

        // ---------------- Aurora (night only, random nights, fades in/out) ----------------
        [Header("Aurora")]
        public bool auroraEnabled = true;
        [Tooltip("Fraction of nights that get an aurora.")]
        [Range(0f, 1f)] public float auroraChance = 0.6f;
        [Range(0f, 2f)] public float auroraIntensity = 1f;
        [Tooltip("How much of the sky the aurora fills: low = a band over one region, high = most of the dome.")]
        [Range(0.05f, 1f)] public float auroraCoverage = 0.8f;
        [ColorUsage(false, true)] public Color auroraColor1 = new Color(0.70f, 0.2f, 1.0f);    // bottom (violet)
        [ColorUsage(false, true)] public Color auroraColor2 = new Color(0.15f, 1.0f, 0.5f);    // mid (green)
        [ColorUsage(false, true)] public Color auroraColor3 = new Color(1.0f, 0.25f, 0.45f);   // top (crimson)
        public int auroraSeed = 777;

        // ---------------- Weather ----------------
        [Header("Weather")]
        public List<WeatherPreset> weatherTypes = new List<WeatherPreset>();
        public int startWeatherIndex = 0;
        [Tooltip("Seconds to crossfade between weather types.")]
        public float weatherBlendSeconds = 12f;
        public bool autoWeather = false;
        public Vector2 autoWeatherInterval = new Vector2(90f, 240f);

        [Header("Multiplayer Sync")]
        [Tooltip("Derive time (and auto-weather) from a shared UTC clock so every player sees the same sky — no networking required. New joiners are instantly correct.")]
        public bool synchronized = true;
        [Tooltip("Seconds each weather lasts in the shared deterministic schedule (when Auto Weather is on).")]
        public float weatherPeriodSeconds = 240f;
        public int weatherSeed = 12345;

        // ---------------- Runtime state ----------------
        public float SolarAltitude { get; private set; }   // sun .y, -1..1
        public float DayAmount { get; private set; }        // 0 night .. 1 day
        public bool IsNight { get; private set; }
        public Vector3 SunDirection { get; private set; }   // direction TO sun
        public Vector3 MoonDirection { get; private set; }
        public WeatherPreset Current { get { return _current; } }
        public int CurrentWeatherIndex { get; private set; }

        Material _skyMat;
        bool _ownsSkyMat;
        readonly WeatherPreset _current = new WeatherPreset();
        readonly WeatherPreset _from = new WeatherPreset();
        WeatherPreset _target;
        float _blend = 1f;          // 1 = settled
        bool _blending;
        float _autoTimer;
        Quaternion _sunRot, _moonRot;
        long _lastSyncBucket = long.MinValue;
        static readonly System.DateTime _epoch = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        float _flash, _nextFlash;
        float _auroraValue, _lastTimeOfDay = -1f;
        int _localDays;

        // ---------------- Lifecycle ----------------
        void OnEnable()
        {
            Instance = this;
            EnsureDefaults();
            EnsureSetup();
            // Settle on the start weather immediately.
            CurrentWeatherIndex = Mathf.Clamp(startWeatherIndex, 0, Mathf.Max(0, weatherTypes.Count - 1));
            if (weatherTypes.Count > 0) _current.CopyFrom(weatherTypes[CurrentWeatherIndex]);
            _target = null; _blending = false; _blend = 1f;
            _autoTimer = Random.Range(autoWeatherInterval.x, autoWeatherInterval.y);
        }

        void OnDisable()
        {
            if (_ownsSkyMat && _skyMat != null)
            {
                if (RenderSettings.skybox == _skyMat) RenderSettings.skybox = null;
                if (Application.isPlaying) Destroy(_skyMat); else DestroyImmediate(_skyMat);
                _skyMat = null;
            }
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : (1f / 60f);

            if (synchronized && Application.isPlaying)
            {
                ApplySharedClock();
            }
            else
            {
                bool advance = autoAdvance && (Application.isPlaying || animateInEditor);
                if (advance && dayLengthSeconds > 0.01f)
                {
                    timeOfDay += (24f / dayLengthSeconds) * dt;
                    if (timeOfDay >= 24f) timeOfDay -= 24f;
                }

                if (autoWeather && Application.isPlaying && weatherTypes.Count > 1)
                {
                    _autoTimer -= dt;
                    if (_autoTimer <= 0f)
                    {
                        SetWeather(Random.Range(0, weatherTypes.Count));
                        _autoTimer = Random.Range(autoWeatherInterval.x, autoWeatherInterval.y);
                    }
                }
            }

            // track noon crossings so each whole night shares one "day" id (for per-night aurora)
            if (_lastTimeOfDay >= 0f && _lastTimeOfDay < 12f && timeOfDay >= 12f) _localDays++;
            _lastTimeOfDay = timeOfDay;

            EnsureSetup();
            ComputeDayState();
            UpdateWeatherBlend(dt);
            ApplySky();
            ApplyLighting();
            ApplyAmbient();
            ApplyFog();
            ApplyLightning(dt);
            UpdateAurora(dt);
        }

        // ---------------- Public API ----------------
        /// <summary>Crossfade to a weather preset by index.</summary>
        public void SetWeather(int index, float blendSeconds = -1f)
        {
            if (weatherTypes.Count == 0) return;
            index = Mathf.Clamp(index, 0, weatherTypes.Count - 1);
            CurrentWeatherIndex = index;
            _from.CopyFrom(_current);
            _target = weatherTypes[index];
            _blend = 0f;
            _blending = true;
            if (blendSeconds >= 0f) weatherBlendSeconds = blendSeconds;
        }

        /// <summary>Crossfade to a weather preset by name (case-insensitive).</summary>
        public void SetWeather(string weatherName, float blendSeconds = -1f)
        {
            for (int i = 0; i < weatherTypes.Count; i++)
                if (string.Equals(weatherTypes[i].name, weatherName, System.StringComparison.OrdinalIgnoreCase))
                { SetWeather(i, blendSeconds); return; }
        }

        public void SetTimeOfDay(float hours) { timeOfDay = Mathf.Repeat(hours, 24f); }

        // ---------------- Internals ----------------
        void ComputeDayState()
        {
            float sunAngle = (timeOfDay / 24f) * 360f - 90f;   // -90 midnight, 0 sunrise, 90 noon, 180 sunset
            _sunRot = Quaternion.Euler(sunAngle, sunAzimuth, 0f);
            _moonRot = Quaternion.Euler(sunAngle + 180f, sunAzimuth, 0f);

            SunDirection = (_sunRot * Vector3.forward) * -1f;     // direction TO the sun
            MoonDirection = (_moonRot * Vector3.forward) * -1f;
            SolarAltitude = SunDirection.y;
            DayAmount = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.16f, SolarAltitude));
            IsNight = SolarAltitude < 0.02f;
        }

        // ---- Shared deterministic clock: identical sky on every client, no networking ----
        double SharedClockSeconds() { return (System.DateTime.UtcNow - _epoch).TotalSeconds; }

        void ApplySharedClock()
        {
            double secs = SharedClockSeconds();
            float cyc = Mathf.Max(1f, dayLengthSeconds);
            timeOfDay = (float)(((secs / cyc) * 24.0) % 24.0);

            if (autoWeather && weatherTypes.Count > 0 && weatherPeriodSeconds > 1f)
            {
                long bucket = (long)System.Math.Floor(secs / weatherPeriodSeconds);
                if (bucket != _lastSyncBucket)
                {
                    _lastSyncBucket = bucket;
                    SetWeather(DeterministicWeatherIndex(bucket));
                }
            }
        }

        int DeterministicWeatherIndex(long bucket)
        {
            unchecked
            {
                ulong h = (ulong)(bucket + weatherSeed) * 2654435761UL;
                h ^= h >> 13; h *= 2246822519UL; h ^= h >> 16;
                return (int)(h % (ulong)Mathf.Max(1, weatherTypes.Count));
            }
        }

        void UpdateWeatherBlend(float dt)
        {
            if (_blending && _target != null)
            {
                _blend += (weatherBlendSeconds > 0.01f) ? dt / weatherBlendSeconds : 1f;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_blend));
                _current.LerpFrom(_from, _target, t);
                if (_blend >= 1f) { _current.CopyFrom(_target); _blending = false; }
            }
        }

        void ApplySky()
        {
            Shader.SetGlobalVector("_SunDir", SunDirection);
            Shader.SetGlobalVector("_MoonDir", MoonDirection);

            float windRad = windHeading * Mathf.Deg2Rad;
            Vector2 windVec = new Vector2(Mathf.Sin(windRad), Mathf.Cos(windRad)) * Mathf.Lerp(0.2f, 1.4f, _current.wind);
            Shader.SetGlobalVector("_WindDir", new Vector4(windVec.x, windVec.y, 0f, 0f));

            if (_skyMat == null) return;
            _skyMat.SetFloat("_CloudCoverage", _current.cloudCoverage);
            _skyMat.SetFloat("_CloudDensity", _current.cloudDensity);
            _skyMat.SetFloat("_CloudOpacity", _current.cloudOpacity);
            _skyMat.SetColor("_CloudColor", _current.cloudColor);
            _skyMat.SetColor("_CloudShadowColor", _current.cloudShadow);
            _skyMat.SetFloat("_CloudSpeed", cloudSpeed * (0.5f + _current.wind));
            _skyMat.SetFloat("_Exposure", baseExposure * _current.skyExposure);

            // global look params (live-tunable from the inspector)
            _skyMat.SetFloat("_CloudType", cloudType);
            _skyMat.SetFloat("_CloudScale", cloudScale);
            _skyMat.SetFloat("_CloudDetail", cloudDetail);
            _skyMat.SetFloat("_CloudHeight", cloudBottom);
            _skyMat.SetFloat("_CloudThickness", cloudThickness);
            _skyMat.SetFloat("_CloudAbsorption", cloudAbsorption);
            _skyMat.SetFloat("_CloudFade", cloudFade);
            _skyMat.SetFloat("_CloudPlanetRadius", curvatureRadius);
            _skyMat.SetFloat("_SunSize", sunSize);
            _skyMat.SetFloat("_MoonSize", moonSize);
            _skyMat.SetFloat("_StarIntensity", starIntensity);
            _skyMat.SetFloat("_CloudSteps", cloudSteps);
        }

        void ApplyLighting()
        {
            if (sunLight == null) return;
            // Single directional light = sun by day, moon by night.
            sunLight.transform.rotation = IsNight ? _moonRot : _sunRot;
            float dayT = Mathf.Clamp01((SolarAltitude + 0.2f) / 0.6f);
            if (IsNight)
            {
                sunLight.color = moonColor;
                sunLight.intensity = moonIntensity;
            }
            else
            {
                sunLight.color = (sunColor != null) ? sunColor.Evaluate(dayT) : Color.white;
                sunLight.intensity = Mathf.Max(0f, sunIntensity.Evaluate(dayT));
            }
            sunLight.shadowStrength = Mathf.Lerp(0.35f, 0.85f, DayAmount);
        }

        void ApplyAmbient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            Color zen = _skyMat != null
                ? Color.Lerp(_skyMat.GetColor("_NightZenith"), _skyMat.GetColor("_DayZenith"), DayAmount)
                : Color.Lerp(new Color(0.02f, 0.03f, 0.06f), new Color(0.3f, 0.5f, 0.8f), DayAmount);
            Color hor = _skyMat != null
                ? Color.Lerp(_skyMat.GetColor("_NightHorizon"), _skyMat.GetColor("_DayHorizon"), DayAmount)
                : Color.Lerp(new Color(0.04f, 0.05f, 0.09f), new Color(0.6f, 0.75f, 0.9f), DayAmount);
            Color grd = _skyMat != null ? _skyMat.GetColor("_GroundColor") : new Color(0.17f, 0.16f, 0.15f);

            RenderSettings.ambientSkyColor = zen * ambientStrength;
            RenderSettings.ambientEquatorColor = hor * ambientStrength;
            RenderSettings.ambientGroundColor = grd * ambientStrength * (0.4f + 0.4f * DayAmount);
        }

        void ApplyFog()
        {
            float f = _current.fog;

            // Sky fog: blend the sky itself toward the fog colour (visible even with no geometry).
            if (_skyMat != null)
            {
                _skyMat.SetFloat("_SkyFog", f);
                _skyMat.SetColor("_SkyFogColor", _current.fogColor);
            }

            // Scene fog: standard RenderSettings fog for world geometry.
            bool on = f > 0.002f;
            RenderSettings.fog = on;
            if (on)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = _current.fogColor;
                RenderSettings.fogDensity = Mathf.Lerp(0f, 0.06f, f);
            }
        }

        void ApplyLightning(float dt)
        {
            float freq = (_current != null) ? _current.lightning : 0f;
            if (lightningEnabled && freq > 0.02f && Application.isPlaying)
            {
                _nextFlash -= dt;
                if (_nextFlash <= 0f)
                {
                    _flash = 1f;                                            // strike
                    _nextFlash = Random.Range(2.5f, 11f) / Mathf.Max(0.1f, freq);
                }
            }
            _flash = Mathf.Max(0f, _flash - dt * 4f);                       // quick decay
            float fl = _flash * freq;
            Shader.SetGlobalFloat("_CloudFlash", fl);                       // clouds light up from within
            if (fl > 0.01f)
                RenderSettings.ambientLight += new Color(0.55f, 0.60f, 0.80f) * fl;  // scene flash
        }

        void UpdateAurora(float dt)
        {
            if (_skyMat == null) return;
            float target = 0f;
            if (auroraEnabled && auroraChance > 0f)
            {
                long dayIndex = (synchronized && Application.isPlaying)
                    ? (long)(SharedClockSeconds() / Mathf.Max(1f, dayLengthSeconds) + 0.5)
                    : _localDays;
                float h = Hash01(dayIndex + auroraSeed);
                float strength = (h < auroraChance) ? Mathf.Lerp(0.55f, 1f, Hash01(dayIndex * 7 + auroraSeed + 3)) : 0f;
                target = strength * (1f - DayAmount);                    // night only
            }
            _auroraValue = Mathf.MoveTowards(_auroraValue, target, dt * 0.12f);   // slow fade in/out
            Shader.SetGlobalFloat("_AuroraIntensity", _auroraValue * auroraIntensity);
            _skyMat.SetColor("_AuroraColor1", auroraColor1);
            _skyMat.SetColor("_AuroraColor2", auroraColor2);
            _skyMat.SetColor("_AuroraColor3", auroraColor3);
            _skyMat.SetFloat("_AuroraCoverage", auroraCoverage);
        }

        static float Hash01(long n)
        {
            unchecked
            {
                ulong x = (ulong)n * 2654435761UL + 12345UL;
                x ^= x >> 13; x *= 2246822519UL; x ^= x >> 16;
                return (x & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        void EnsureSetup()
        {
            // Skybox material
            if (_skyMat == null)
            {
                if (skyboxMaterialOverride != null)
                {
                    // Instance the shipped material so we drive params at runtime without touching the
                    // shared asset. Referencing a Material ASSET (instead of Shader.Find) is ALSO what
                    // keeps the shader from being stripped out of a Somnium world AssetBundle — a shader
                    // referenced only via Shader.Find returns null in a build, giving a black sky.
                    _skyMat = new Material(skyboxMaterialOverride) { name = "SkyDome (runtime)", hideFlags = HideFlags.DontSave };
                    _ownsSkyMat = true;
                }
                else
                {
                    Shader sh = Shader.Find("Bently/SkyDome");
                    if (sh != null)
                    {
                        _skyMat = new Material(sh) { name = "SkyDome (runtime)", hideFlags = HideFlags.DontSave };
                        _ownsSkyMat = true;
                    }
                }
            }
            if (_skyMat != null && RenderSettings.skybox != _skyMat)
                RenderSettings.skybox = _skyMat;

            if (_skyMat != null)
            {
                if (cloudNoise != null && _skyMat.GetTexture("_CloudNoiseTex") != cloudNoise) _skyMat.SetTexture("_CloudNoiseTex", cloudNoise);
                if (starSky != null && _skyMat.GetTexture("_StarSkyTex") != starSky) _skyMat.SetTexture("_StarSkyTex", starSky);
                if (moonTexture != null && _skyMat.GetTexture("_MoonTex") != moonTexture) _skyMat.SetTexture("_MoonTex", moonTexture);
            }

            // Sun light
            if (sunLight == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sunLight = l; break; }
                if (sunLight == null)
                {
                    var go = new GameObject("Directional Light (Sky)");
                    sunLight = go.AddComponent<Light>();
                    sunLight.type = LightType.Directional;
                    sunLight.shadows = LightShadows.Soft;
                }
            }
        }

        void EnsureDefaults()
        {
            if (sunColor == null || sunColor.colorKeys.Length == 0)
            {
                sunColor = new Gradient();
                sunColor.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.95f, 0.45f, 0.22f), 0.0f),
                        new GradientColorKey(new Color(1.0f, 0.72f, 0.5f), 0.18f),
                        new GradientColorKey(new Color(1.0f, 0.96f, 0.9f), 0.45f),
                        new GradientColorKey(new Color(1.0f, 0.99f, 0.97f), 1.0f),
                    },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            }
            if (sunIntensity == null || sunIntensity.length == 0)
            {
                sunIntensity = new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(0.12f, 0.15f),
                    new Keyframe(0.45f, 1.0f), new Keyframe(1f, 1.35f));
            }
            if (weatherTypes == null || weatherTypes.Count == 0)
                weatherTypes = BuildDefaultWeather();
        }

        public static List<WeatherPreset> BuildDefaultWeather()
        {
            var list = new List<WeatherPreset>();

            var clear = new WeatherPreset("Clear")
            { cloudCoverage = 0.2f, cloudDensity = 5.0f, cloudOpacity = 0.85f, skyExposure = 1.05f, wind = 0.15f };

            var fair = new WeatherPreset("Partly Cloudy")
            { cloudCoverage = 0.45f, cloudDensity = 6.0f, cloudOpacity = 1f, wind = 0.3f };

            var overcast = new WeatherPreset("Overcast")
            {
                cloudCoverage = 0.85f, cloudDensity = 7.5f, cloudOpacity = 1f, skyExposure = 0.8f,
                cloudColor = new Color(0.78f, 0.80f, 0.84f), cloudShadow = new Color(0.45f, 0.48f, 0.55f),
                fog = 0.15f, wind = 0.45f
            };

            var lightRain = new WeatherPreset("Light Rain")
            {
                cloudCoverage = 0.72f, cloudDensity = 6.5f, cloudOpacity = 1f, skyExposure = 0.9f,
                cloudColor = new Color(0.70f, 0.73f, 0.78f), cloudShadow = new Color(0.42f, 0.45f, 0.52f),
                fog = 0.12f, fogColor = new Color(0.60f, 0.63f, 0.68f), rain = 0.35f, wind = 0.25f
            };

            var rain = new WeatherPreset("Rain")
            {
                cloudCoverage = 0.92f, cloudDensity = 8.5f, cloudOpacity = 1f, skyExposure = 0.65f,
                cloudColor = new Color(0.62f, 0.65f, 0.70f), cloudShadow = new Color(0.34f, 0.37f, 0.43f),
                fog = 0.35f, fogColor = new Color(0.52f, 0.55f, 0.60f), rain = 0.8f, wind = 0.6f
            };

            var storm = new WeatherPreset("Storm")
            {
                cloudCoverage = 0.97f, cloudDensity = 9.0f, cloudOpacity = 1f, skyExposure = 0.5f,
                cloudColor = new Color(0.48f, 0.50f, 0.55f), cloudShadow = new Color(0.22f, 0.24f, 0.30f),
                fog = 0.45f, fogColor = new Color(0.42f, 0.45f, 0.50f), rain = 1f, wind = 1f, lightning = 1f
            };

            var lightSnow = new WeatherPreset("Light Snow")
            {
                cloudCoverage = 0.7f, cloudDensity = 6.5f, cloudOpacity = 1f, skyExposure = 0.92f,
                cloudColor = new Color(0.86f, 0.88f, 0.93f), cloudShadow = new Color(0.58f, 0.61f, 0.69f),
                fog = 0.2f, fogColor = new Color(0.82f, 0.85f, 0.90f), snow = 0.35f, wind = 0.18f
            };

            var blizzard = new WeatherPreset("Blizzard")
            {
                cloudCoverage = 0.98f, cloudDensity = 8.5f, cloudOpacity = 1f, skyExposure = 0.68f,
                cloudColor = new Color(0.70f, 0.73f, 0.80f), cloudShadow = new Color(0.40f, 0.43f, 0.50f),
                fog = 0.75f, fogColor = new Color(0.80f, 0.83f, 0.88f), snow = 1f, wind = 1f
            };

            var fog = new WeatherPreset("Fog")
            {
                cloudCoverage = 0.6f, cloudDensity = 5.0f, cloudOpacity = 0.8f, skyExposure = 0.8f,
                fog = 0.85f, fogColor = new Color(0.72f, 0.75f, 0.80f), wind = 0.1f
            };

            list.Add(clear); list.Add(fair); list.Add(overcast);
            list.Add(lightRain); list.Add(rain); list.Add(storm);
            list.Add(lightSnow); list.Add(blizzard); list.Add(fog);
            return list;
        }

        void Reset()
        {
            weatherTypes = BuildDefaultWeather();
            EnsureDefaults();
        }
    }
}
