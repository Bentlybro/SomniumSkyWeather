using UnityEngine;

namespace SomniumSpace.Worlds.Bently.Weather
{
    /// <summary>
    /// A named bundle of weather settings. The controller blends the live state
    /// toward the active preset, so authoring weather is just editing one of these.
    /// Plain serializable class (no ScriptableObject wiring) -> easy to use.
    /// </summary>
    [System.Serializable]
    public class WeatherPreset
    {
        public string name = "Clear";

        [Header("Clouds")]
        [Range(0f, 1f)] public float cloudCoverage = 0.4f;
        [Range(0f, 4f)] public float cloudDensity = 1.4f;
        [Range(0f, 1f)] public float cloudOpacity = 1f;
        [ColorUsage(false, true)] public Color cloudColor = new Color(1f, 0.98f, 0.95f, 1f);
        [ColorUsage(false, true)] public Color cloudShadow = new Color(0.42f, 0.48f, 0.60f, 1f);

        [Header("Atmosphere")]
        [Tooltip("Multiplies overall sky exposure for this weather (1 = unchanged, <1 = gloomier).")]
        [Range(0.2f, 1.5f)] public float skyExposure = 1f;

        [Header("Fog")]
        [Range(0f, 1f)] public float fog = 0f;
        public Color fogColor = new Color(0.62f, 0.66f, 0.72f, 1f);

        [Header("Precipitation (0..1 intensity)")]
        [Range(0f, 1f)] public float rain = 0f;
        [Range(0f, 1f)] public float snow = 0f;

        [Header("Wind")]
        [Range(0f, 1f)] public float wind = 0.2f;

        [Header("Storm")]
        [Tooltip("Lightning frequency (0 = none).")]
        [Range(0f, 1f)] public float lightning = 0f;

        public WeatherPreset() { }
        public WeatherPreset(string n) { name = n; }

        /// <summary>Linear interpolate every field from a toward b by t, into this.</summary>
        public void LerpFrom(WeatherPreset a, WeatherPreset b, float t)
        {
            cloudCoverage = Mathf.Lerp(a.cloudCoverage, b.cloudCoverage, t);
            cloudDensity  = Mathf.Lerp(a.cloudDensity,  b.cloudDensity,  t);
            cloudOpacity  = Mathf.Lerp(a.cloudOpacity,  b.cloudOpacity,  t);
            cloudColor    = Color.Lerp(a.cloudColor,    b.cloudColor,    t);
            cloudShadow   = Color.Lerp(a.cloudShadow,   b.cloudShadow,   t);
            skyExposure   = Mathf.Lerp(a.skyExposure,   b.skyExposure,   t);
            fog           = Mathf.Lerp(a.fog,           b.fog,           t);
            fogColor      = Color.Lerp(a.fogColor,      b.fogColor,      t);
            rain          = Mathf.Lerp(a.rain,          b.rain,          t);
            snow          = Mathf.Lerp(a.snow,          b.snow,          t);
            wind          = Mathf.Lerp(a.wind,          b.wind,          t);
            lightning     = Mathf.Lerp(a.lightning,     b.lightning,     t);
        }

        public void CopyFrom(WeatherPreset o)
        {
            name = o.name;
            cloudCoverage = o.cloudCoverage; cloudDensity = o.cloudDensity; cloudOpacity = o.cloudOpacity;
            cloudColor = o.cloudColor; cloudShadow = o.cloudShadow; skyExposure = o.skyExposure;
            fog = o.fog; fogColor = o.fogColor; rain = o.rain; snow = o.snow; wind = o.wind; lightning = o.lightning;
        }
    }
}
