using UnityEngine;
using SomniumSpace.Bridge;

namespace SomniumSpace.Worlds.Bently.Weather
{
    /// <summary>
    /// Optional Somnium glue for <see cref="SkyWeather"/>. Add it next to a SkyWeather.
    /// - Registers our skybox + sun with Somnium's environment system (cooperative, so the
    ///   host environment is aware of them) via ISomniumEnvironment.
    /// - Makes rain/snow follow the local player's head once Somnium spawns it.
    ///
    /// Time and weather are kept in sync across players by SkyWeather's shared deterministic
    /// clock (no networking needed), so this component does NOT need to send anything.
    /// It runs only in Play mode and safely no-ops outside a Somnium session.
    /// </summary>
    [AddComponentMenu("Bently/Somnium Weather Integration")]
    [RequireComponent(typeof(SkyWeather))]
    [DisallowMultipleComponent]
    public class SomniumWeatherIntegration : MonoBehaviour
    {
        [Tooltip("Tell Somnium's environment system about our skybox + sun (cooperative).")]
        public bool registerEnvironment = true;
        [Tooltip("Make rain/snow follow the local player's head once it spawns.")]
        public bool followLocalPlayer = true;

        SkyWeather _sky;
        WeatherParticles _particles;
        bool _envDone;
        float _retry;

        void Awake()
        {
            _sky = GetComponent<SkyWeather>();
            _particles = GetComponentInChildren<WeatherParticles>(true);
        }

        void Update()
        {
            // Throttle: Somnium objects appear a moment after join, so retry once a second.
            _retry -= Time.unscaledDeltaTime;
            if (_retry > 0f) return;
            _retry = 1f;

            if (registerEnvironment && !_envDone) TryRegisterEnvironment();
            if (followLocalPlayer) TryFollowLocalPlayer();
        }

        void TryRegisterEnvironment()
        {
            try
            {
                var env = SomniumBridge.Environment;
                if (env == null) return;
                if (RenderSettings.skybox != null) env.SetSkybox(RenderSettings.skybox);
                if (_sky != null && _sky.sunLight != null) env.SetSunSource(_sky.sunLight);
                env.UpdateEnvironment();
                _envDone = true;
            }
            catch { /* not in a Somnium session / bridge not ready */ }
        }

        void TryFollowLocalPlayer()
        {
            if (_particles == null)
            {
                _particles = GetComponentInChildren<WeatherParticles>(true);
                if (_particles == null) return;
            }
            if (_particles.followTarget != null) return;

            try
            {
                var pc = SomniumBridge.PlayersContainer;
                if (pc == null) return;
                var lp = pc.LocalPlayer;
                if (lp == null) return;
                var refs = lp.References;
                if (refs == null || refs.Body == null) return;
                Transform head = refs.Body.Head != null ? refs.Body.Head : refs.Body.Root;
                if (head != null) _particles.followTarget = head;
            }
            catch { /* bridge not ready */ }
        }
    }
}
