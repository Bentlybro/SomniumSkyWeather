using UnityEngine;

namespace SomniumSpace.Worlds.Bently.Weather
{
    /// <summary>
    /// Rain &amp; snow that follow the viewer. Emission ramps from the active weather blend
    /// (SkyWeather.Current.rain / .snow). Everything is built at runtime — no texture or
    /// prefab assets required, so it ships cleanly in a Somnium world bundle.
    ///
    /// In Somnium the player camera spawns at runtime; set <see cref="followTarget"/> from the
    /// Somnium integration layer (LocalPlayer body). Falls back to Camera.main, then this object.
    /// </summary>
    [AddComponentMenu("Bently/Weather Particles")]
    public class WeatherParticles : MonoBehaviour
    {
        public enum Coverage { FollowViewer, FixedArea }

        [Header("Coverage")]
        [Tooltip("FollowViewer: precipitation follows the player so it covers the whole world.  FixedArea: stays at this object's position for localized weather in one region.")]
        public Coverage coverage = Coverage.FollowViewer;
        [Tooltip("On: intensity follows the global weather (rains only when it's raining). Off: use the Local Rain/Snow values for an always-on zone.")]
        public bool useGlobalWeather = true;
        [Range(0f, 1f)] public float localRain = 0f;
        [Range(0f, 1f)] public float localSnow = 0f;

        [Tooltip("FollowViewer only. What to follow; if empty uses Camera.main.")]
        public Transform followTarget;
        [Tooltip("How high above the area centre particles spawn.")]
        public float spawnHeight = 16f;
        [Tooltip("Horizontal half-size of the emission area around the viewer. Make this large so precipitation fills the whole view, not a small box.")]
        public float areaRadius = 28f;

        [Header("Max emission (at intensity 1)")]
        public float maxRainRate = 7000f;
        public float maxSnowRate = 1400f;

        [Header("Collision")]
        [Tooltip("Stop rain/snow at solid surfaces (scene colliders) instead of falling through the deck/objects. Uses accurate World collision — if it costs too much on low-end VR, turn it off or lower the Max emission rates.")]
        public bool collideWithWorld = true;

        ParticleSystem _rain, _snow;
        ParticleSystem.EmissionModule _rainEm, _snowEm;
        ParticleSystem.VelocityOverLifetimeModule _rainVel, _snowVel;
        Transform _rig;
        static Material _rainMat, _snowMat;

        void OnEnable()
        {
            if (_rain == null) Build();
        }

        void LateUpdate()
        {
            // --- position the emission area ---
            if (coverage == Coverage.FixedArea)
            {
                // localized weather: stay at this object's position
                _rig.position = transform.position + Vector3.up * spawnHeight;
            }
            else
            {
                // global weather: follow the viewer so it covers the whole world (XZ follow, world-simulated)
                Transform tgt = followTarget;
                if (tgt == null && Camera.main != null) tgt = Camera.main.transform;
                if (tgt == null) tgt = transform;
                _rig.position = new Vector3(tgt.position.x, tgt.position.y + spawnHeight, tgt.position.z);
            }

            // --- intensity ---
            float rain, snow;
            if (useGlobalWeather)
            {
                rain = 0f; snow = 0f;
                var sw = SkyWeather.Instance;
                if (sw != null && sw.Current != null) { rain = sw.Current.rain; snow = sw.Current.snow; }
            }
            else
            {
                rain = localRain; snow = localSnow;   // always-on local zone
            }

            _rainEm.rateOverTime = maxRainRate * Mathf.Clamp01(rain);
            _snowEm.rateOverTime = maxSnowRate * Mathf.Clamp01(snow);

            // wind blows precipitation sideways: gentle drift when calm, driving hard in a storm/blizzard
            var sw2 = SkyWeather.Instance;
            float windAmt = (sw2 != null && sw2.Current != null) ? sw2.Current.wind : 0f;
            float rad = (sw2 != null ? sw2.windHeading : 90f) * Mathf.Deg2Rad;
            float wx = Mathf.Sin(rad), wz = Mathf.Cos(rad);
            _snowVel.x = new ParticleSystem.MinMaxCurve(wx * windAmt * 7f);
            _snowVel.z = new ParticleSystem.MinMaxCurve(wz * windAmt * 7f);
            _rainVel.x = new ParticleSystem.MinMaxCurve(wx * windAmt * 12f);
            _rainVel.z = new ParticleSystem.MinMaxCurve(wz * windAmt * 12f);
        }

        void Build()
        {
            EnsureMaterials();

            _rig = new GameObject("PrecipRig").transform;
            _rig.SetParent(transform, false);

            _rain = MakeSystem("Rain", _rainMat, ParticleSystemRenderMode.Stretch);
            ConfigureRain(_rain);
            _rainEm = _rain.emission;
            _rainVel = _rain.velocityOverLifetime;

            _snow = MakeSystem("Snow", _snowMat, ParticleSystemRenderMode.Billboard);
            ConfigureSnow(_snow);
            _snowEm = _snow.emission;
            _snowVel = _snow.velocityOverLifetime;
        }

        ParticleSystem MakeSystem(string name, Material mat, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_rig, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 12000;
            main.playOnAwake = true;

            var em = ps.emission;
            em.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(areaRadius * 2f, 0.5f, areaRadius * 2f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.material = mat;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (mode == ParticleSystemRenderMode.Stretch)
            {
                renderer.velocityScale = 0.0f;
                renderer.lengthScale = 7.0f;     // long thin streaks
            }
            ps.Play();
            return ps;
        }

        void ConfigureRain(ParticleSystem ps)
        {
            var main = ps.main;
            main.startLifetime = 0.6f;
            main.startSpeed = 34f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.startColor = new Color(0.74f, 0.80f, 0.92f, 0.6f);
            main.gravityModifier = 1.3f;
            main.startRotation = 0f;

            // fire straight down
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(-26f);

            if (collideWithWorld) ConfigureCollision(ps, 1f);   // vanish (splash) the instant it hits a surface
        }

        void ConfigureSnow(ParticleSystem ps)
        {
            var main = ps.main;
            main.startLifetime = 6f;
            main.startSpeed = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.gravityModifier = 0.08f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(-1.4f);

            // gentle drift
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.4f;

            if (collideWithWorld) ConfigureCollision(ps, 0.5f);   // settle on the surface, then fade (no pass-through)
        }

        // Stop precipitation at solid surfaces (the scene's colliders) instead of dropping through the world.
        // World collision = accurate 3D against scene colliders; the particle loses 'lifetimeLoss' of its
        // remaining life on contact (1 = vanish like a splash, less = rest briefly then fade). Needs World
        // simulation space (already set) so drops collide in world space as they fall past geometry.
        static void ConfigureCollision(ParticleSystem ps, float lifetimeLoss)
        {
            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.High;   // exact collision against scene colliders
            col.dampen = 1f;                  // kill velocity on contact (no slide)
            col.bounce = 0f;                  // no bounce
            col.lifetimeLoss = lifetimeLoss;
            col.radiusScale = 1f;
            col.collidesWith = ~0;            // every layer
            col.sendCollisionMessages = false;
        }

        static void EnsureMaterials()
        {
            if (_rainMat != null && _snowMat != null) return;
            Texture2D dot = BuildSoftDot();

            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");

            _snowMat = new Material(s) { name = "SnowParticle", hideFlags = HideFlags.DontSave };
            TrySetTransparent(_snowMat, dot);

            _rainMat = new Material(s) { name = "RainParticle", hideFlags = HideFlags.DontSave };
            TrySetTransparent(_rainMat, dot);
        }

        static void TrySetTransparent(Material m, Texture tex)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

            // Setting _Surface alone doesn't reconfigure blend state on URP shaders — do it explicitly,
            // otherwise the quad renders opaque (hard squares) and the soft-dot alpha is ignored.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // Alpha
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static Texture2D BuildSoftDot()
        {
            const int N = 32;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N - 0.5f;
                float dy = (y + 0.5f) / N - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float a = Mathf.Clamp01(1f - d);
                a = a * a;
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
