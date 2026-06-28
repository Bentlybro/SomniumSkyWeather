// Bently.Weather - SkyDome
// A single procedural skybox shader: day/night atmosphere, sun disc + glow,
// moon with phase, procedural star field, and raymarched 2.5D sky-dome clouds.
// Everything is driven by global shader properties set from C# (SkyModule / CloudModule).
// Renders entirely via RenderSettings.skybox -> works inside Somnium's URP with no render feature.
Shader "Bently/SkyDome"
{
    Properties
    {
        [Header(Atmosphere)]
        _DayZenith   ("Day Zenith",   Color) = (0.16, 0.40, 0.82, 1)
        _DayHorizon  ("Day Horizon",  Color) = (0.62, 0.80, 0.96, 1)
        _NightZenith ("Night Zenith", Color) = (0.008, 0.018, 0.05, 1)
        _NightHorizon("Night Horizon",Color) = (0.03, 0.05, 0.10, 1)
        _SunsetColor ("Sunset Glow",  Color) = (1.0, 0.45, 0.16, 1)
        _GroundColor ("Ground",       Color) = (0.17, 0.16, 0.15, 1)
        _Exposure    ("Exposure", Range(0,3)) = 1.15
        _SkyExponent ("Sky Gradient Exponent", Range(0.2,2)) = 0.5
        _SkyFog      ("Sky Fog Amount", Range(0,1)) = 0
        _SkyFogColor ("Sky Fog Color", Color) = (0.72, 0.75, 0.80, 1)

        [Header(Sun)]
        _SunColor    ("Sun Color", Color) = (1.0, 0.96, 0.86, 1)
        _SunSize     ("Sun Size", Range(0.0002, 0.02)) = 0.0009
        _SunGlow     ("Sun Glow Strength", Range(0,3)) = 0.8
        _SunGlowPow  ("Sun Glow Falloff", Range(8, 2048)) = 320

        [Header(Moon)]
        _MoonColor   ("Moon Color", Color) = (0.85, 0.88, 1.0, 1)
        _MoonSize    ("Moon Size", Range(0.0005, 0.03)) = 0.0016
        _MoonGlow    ("Moon Glow", Range(0,2)) = 0.4
        _MoonPhase   ("Moon Phase (-1..1)", Range(-1,1)) = 0.35

        [Header(Stars)]
        _StarIntensity ("Star Intensity", Range(0,4)) = 1.4
        _StarDensity   ("Star Density", Range(2,40)) = 14
        _GalaxyColor   ("Galaxy Tint", Color) = (0.35, 0.4, 0.6, 1)

        [Header(Clouds)]
        _CloudColor      ("Cloud Lit Color", Color) = (1.0, 0.98, 0.95, 1)
        _CloudShadowColor("Cloud Shadow Color", Color) = (0.42, 0.48, 0.60, 1)
        _CloudCoverage   ("Coverage", Range(0,1)) = 0.45
        _CloudDensity    ("Density", Range(0,10)) = 6.0
        _CloudType       ("Type (flat..puffy)", Range(0,1)) = 0.8
        _CloudScale      ("Base Scale", Range(0.05, 4)) = 2.0
        _CloudDetail     ("Detail Erosion", Range(0,1)) = 0.45
        _CloudHeight     ("Layer Bottom", Range(0.1, 6)) = 1.3
        _CloudThickness  ("Layer Thickness", Range(0.1, 6)) = 2.4
        _CloudAbsorption ("Light Absorption", Range(0.1, 6)) = 1.3
        _CloudSpeed      ("Cloud Speed", Range(0, 0.2)) = 0.02
        _CloudPlanetRadius ("Curvature Radius", Range(20, 400)) = 90
        _CloudFade       ("Distance Fade", Range(0, 0.2)) = 0.045
        _CloudSteps      ("Cloud Steps (lower = faster, for VR)", Range(12, 64)) = 40

        [Header(Aurora)]
        [HDR] _AuroraColor1 ("Aurora Color (bottom)", Color) = (0.7, 0.2, 1.0, 1)
        [HDR] _AuroraColor2 ("Aurora Color (mid)", Color) = (0.15, 1.0, 0.5, 1)
        [HDR] _AuroraColor3 ("Aurora Color (top)", Color) = (1.0, 0.25, 0.45, 1)
        _AuroraCoverage ("Aurora Sky Coverage", Range(0.05, 1)) = 0.4

        [Header(Textures)]
        [NoScaleOffset] _CloudNoiseTex ("Cloud Noise (3D)", 3D) = "" {}
        [NoScaleOffset] _StarSkyTex ("Star Sky (Cube)", Cube) = "black" {}
        [NoScaleOffset] _MoonTex ("Moon", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define CLOUD_STEPS 48
            #define LIGHT_STEPS 6
            #define PI 3.14159265

            // --- art-directed params ---
            half4 _DayZenith, _DayHorizon, _NightZenith, _NightHorizon, _SunsetColor, _GroundColor;
            half  _Exposure, _SkyExponent, _SkyFog;
            half4 _SkyFogColor;
            half4 _SunColor; half _SunSize, _SunGlow, _SunGlowPow;
            half4 _MoonColor; half _MoonSize, _MoonGlow, _MoonPhase;
            half  _StarIntensity, _StarDensity; half4 _GalaxyColor;
            half4 _CloudColor, _CloudShadowColor;
            half  _CloudCoverage, _CloudDensity, _CloudScale, _CloudHeight, _CloudThickness, _CloudAbsorption, _CloudSpeed, _CloudType, _CloudDetail, _CloudPlanetRadius, _CloudFade, _CloudSteps;
            half  _AuroraIntensity, _AuroraCoverage;   // set by the controller (intensity gated to night)
            half4 _AuroraColor1, _AuroraColor2, _AuroraColor3;

            // --- globals set from C# (Shader.SetGlobalVector) ---
            float3 _SunDir;   // direction TO the sun (normalized)
            float3 _MoonDir;  // direction TO the moon
            float4 _WindDir;  // xy = horizontal wind (x,z), scaled by speed
            float  _CloudOpacity; // master 0..1 fade for clouds (weather)
            float  _CloudFlash;   // lightning flash 0..1 (set by the controller)

            sampler3D _CloudNoiseTex;  // baked tileable Worley FBM (R = base billows, GBA = detail)
            samplerCUBE _StarSkyTex;   // baked star field + Milky Way band
            sampler2D _MoonTex;        // baked moon albedo (maria + craters)

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // ---------- noise ----------
            float hash21 (float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float3 hash33 (float3 p)
            {
                p = float3(dot(p, float3(127.1,311.7, 74.7)),
                           dot(p, float3(269.5,183.3,246.1)),
                           dot(p, float3(113.5,271.9,124.6)));
                return frac(sin(p) * 43758.5453);
            }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1,0));
                float c = hash21(i + float2(0,1));
                float d = hash21(i + float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
            }
            float fbm (float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 5; i++) { v += a * vnoise(p); p *= 2.02; a *= 0.5; }
                return v;
            }

            // ---------- volumetric clouds ----------
            float remap (float v, float a, float b, float c, float d)
            {
                return c + (v - a) * (d - c) / max(b - a, 1e-5);
            }

            // vertical cloud shape: rounded bottom, tapered top. type 0 = flat stratus .. 1 = tall cumulus
            float heightGradient (float h, float type)
            {
                float bottom = saturate(remap(h, 0.0, 0.10, 0.0, 1.0));
                float top    = saturate(remap(h, lerp(0.25, 0.55, type), lerp(0.35, 1.0, type), 1.0, 0.0));
                return bottom * top;
            }

            // density of the cloud volume at a world point. cheap=1 skips detail erosion (used by light march).
            float cloudDensity (float3 p, int cheap)
            {
                float3 pc = float3(0.0, -_CloudPlanetRadius, 0.0);
                float h = saturate((length(p - pc) - (_CloudPlanetRadius + _CloudHeight)) / max(_CloudThickness, 0.001));
                float3 w = float3(_WindDir.x, 0.0, _WindDir.y);
                float3 sp = p + w * (_Time.y * _CloudSpeed * 3.0) + w * (h * 0.6);    // wind + downwind shear of tops
                float4 n = tex3D(_CloudNoiseTex, sp * (_CloudScale * 0.04));
                // coverage threshold on the base billow noise -> where clouds exist
                float density = saturate(remap(n.r, 1.0 - _CloudCoverage, 1.0, 0.0, 1.0));
                // overcast deck: at high coverage, fill the gaps so the whole dome is covered (incl.
                // straight up) instead of leaving a clear hole where the zenith path is thinnest.
                density = max(density, saturate((_CloudCoverage - 0.72) / 0.26) * 0.7);
                // cumulus vertical shaping
                density *= heightGradient(h, _CloudType);
                // erode the edges with higher-frequency detail (soft, no core speckle)
                if (cheap == 0 && density > 0.0)
                {
                    float4 nd = tex3D(_CloudNoiseTex, sp * (_CloudScale * 0.16) + 21.7);
                    float hi = nd.g * 0.5 + nd.b * 0.35 + nd.a * 0.15;
                    density = saturate(density - hi * _CloudDetail * 0.35 * (1.0 - density));
                }
                return density * _CloudDensity;
            }

            float HGphase (float c, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * c, 1e-3), 1.5));
            }

            // optical depth toward the sun (self-shadowing), widening cone of taps
            float lightMarch (float3 p, float3 ldir)
            {
                float tau = 0.0;
                float stepd = _CloudThickness * 0.10;
                float dist = 0.0;
                [unroll]
                for (int i = 0; i < LIGHT_STEPS; i++)
                {
                    dist += stepd;
                    tau += cloudDensity(p + ldir * dist, 1) * stepd;
                    stepd *= 1.7;
                }
                return tau;
            }

            float3 marchClouds (float3 rd, float dayAmt, float jitter, float3 skyColor, out float alpha)
            {
                alpha = 0.0;
                float3 col = 0.0;
                if (rd.y < 0.0 || _CloudOpacity < 0.001) return col;

                // Curved cloud shell: viewer on a planet of radius R, clouds in a spherical
                // shell above. Bounds the horizon path length (no grey wall) and keeps cloud
                // sizes consistent across the dome. oc = origin - center = (0, R, 0).
                float R = _CloudPlanetRadius;
                float radN = R + _CloudHeight;
                float radF = R + _CloudHeight + _CloudThickness;
                float b = rd.y * R;
                float discF = b * b - (R * R - radF * radF);
                if (discF < 0.0) return col;
                float discN = b * b - (R * R - radN * radN);
                float tStart = -b + sqrt(max(discN, 0.0));   // far root of inner shell (cloud base)
                float tEnd   = -b + sqrt(discF);             // far root of outer shell (cloud top)
                tStart = max(tStart, 0.0);
                if (tEnd <= tStart) return col;

                float stepLen = (tEnd - tStart) / _CloudSteps;
                float t = tStart + stepLen * jitter;

                // light from the sun by day, the moon by night; night clouds are dim, moonlit silhouettes
                float3 Ldir = (dayAmt > 0.04) ? _SunDir : _MoonDir;
                float lightLevel = lerp(0.13, 1.0, dayAmt);
                float ndl = dot(rd, Ldir);
                float phase  = max(HGphase(ndl, 0.72), HGphase(ndl, -0.2) * 0.6);
                float silver = pow(saturate(ndl), 6.0);
                float3 sunCol = lerp(_CloudShadowColor.rgb, _CloudColor.rgb, dayAmt);

                // Localized aurora skyglow: only clouds sitting directly under the curtain get kissed
                // green. Sample the aurora's plan-view footprint (ribbon x region-envelope x near-player)
                // once for this cloud column so the glow tracks where the aurora actually hangs overhead.
                float auroraGlow = 0.0;
                if (_AuroraIntensity > 0.001)
                {
                    float3 pc = rd * ((tStart + tEnd) * 0.5);
                    float2 aUV = pc.xz * 0.04;
                    float aWind = _Time.y * 0.015;
                    float aRibbon = exp(-abs(fbm(aUV * 0.3 + float2(aWind, aWind * 0.6)) - 0.5) * 28.0);
                    float aEnv = smoothstep(1.0 - _AuroraCoverage, 1.0, fbm(aUV * 0.12 + float2(aWind * 0.5, aWind * 0.3)));
                    auroraGlow = aRibbon * aEnv * saturate(1.0 - length(pc.xz) * 0.006);
                }

                float transmittance = 1.0;
                int steps = (int)_CloudSteps;
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    if (transmittance < 0.01) break;
                    float3 p = rd * t;
                    float d = cloudDensity(p, 0);
                    if (d > 0.001)
                    {
                        float tau = lightMarch(p, Ldir);
                        float lightT = exp(-pow(_CloudAbsorption * tau, 1.05));        // bent Beer toward the light
                        float ms = (1.0 - exp(-tau * 0.7)) * 0.45;                     // multi-scatter / powder fill
                        float energy = lightT * (phase + 0.55) * (1.0 + silver * 1.6) + ms * 0.55 + 0.05 * dayAmt + 0.03;
                        float3 lit = (sunCol * energy + _CloudShadowColor.rgb * 0.22 * (1.0 - lightT) * dayAmt) * lightLevel;
                        lit += _CloudColor.rgb * _CloudFlash * (0.6 + 0.8 * (1.0 - lightT));  // lightning lights the cloud from within
                        lit += _AuroraColor2.rgb * (_AuroraIntensity * auroraGlow * 0.40);    // glow ONLY where the curtain hangs overhead
                        lit = lerp(skyColor, lit, exp(-t * _CloudFade));               // atmospheric distance fade

                        float stepT = exp(-d * stepLen * _CloudAbsorption);
                        col += transmittance * lit * (1.0 - stepT);                    // energy-conserving front-to-back
                        transmittance *= stepT;
                    }
                    t += stepLen;
                }
                float horizonFade = smoothstep(0.0, 0.05, rd.y);                       // hide the thin sliver at the horizon
                alpha = (1.0 - transmittance) * _CloudOpacity * horizonFade;
                return col * _CloudOpacity * horizonFade;
            }

            // ---------- stars ----------
            float3 starField (float3 dir)
            {
                float3 col = 0.0;
                float3 p = dir * _StarDensity;
                float3 ip = floor(p);
                float3 fp = frac(p);
                // check a small neighborhood for a star point in each cell
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    float3 cell = float3(x,y,z);
                    float3 rnd = hash33(ip + cell);
                    if (rnd.z > 0.86)
                    {
                        float3 sp = cell + rnd - 0.5;
                        float dist = length(fp - 0.5 - (cell + (rnd - 0.5)));
                        float star = smoothstep(0.16, 0.0, dist);
                        float tw = 0.6 + 0.4 * sin(_Time.y * (2.0 + rnd.x * 6.0) + rnd.y * 30.0);
                        col += star * tw * (0.6 + 0.4 * rnd.x);
                    }
                }
                return col;
            }

            // Raymarched aurora: emissive curtains in a high-altitude band ABOVE the clouds.
            // Sampling real world positions (not just the view angle) gives true 3D curtains with
            // depth + parallax; because it's added to the sky before the clouds composite, clouds
            // correctly sit in front of it. Gated to night by the controller (_AuroraIntensity).
            float3 auroraColor (float3 rd)
            {
                if (_AuroraIntensity < 0.001 || rd.y < 0.04) return float3(0, 0, 0);

                float3 acc = float3(0, 0, 0);
                float aBottom = _CloudHeight + _CloudThickness + 3.0;   // well above the cloud layer
                float aTop = aBottom + 7.0;
                float t0 = aBottom / rd.y;
                float t1 = aTop / rd.y;
                float dt = (t1 - t0) / 36.0;
                float t = t0;
                float wind = _Time.y * 0.015;

                [loop]
                for (int i = 0; i < 36; i++)
                {
                    float3 p = rd * t;
                    float h = saturate((p.y - aBottom) / (aTop - aBottom));    // 0 bottom .. 1 top of the band
                    float2 uv = p.xz * 0.04;
                    float tt = _Time.y;
                    // thin snaking ribbon footprint (where the curtain hangs, in plan view)
                    float ribbon = exp(-abs(fbm(uv * 0.3 + float2(wind, wind * 0.6)) - 0.5) * 28.0);
                    // vertical rays: thin high-frequency striations across the ribbon = the "rayed" curtain look
                    float rays = fbm(float2(uv.x * 11.0 + tt * 0.06, uv.y * 0.55));
                    rays = pow(saturate(rays * 1.8 - 0.45), 3.0);
                    // height band: soft at the bottom, fading out toward the top
                    float vfall = saturate(h * 5.0) * saturate((1.0 - h) * 2.2);
                    // large-scale envelope: aurora only fills certain regions of the sky, not the
                    // whole dome — slowly drifts so the active region moves over time
                    float env = smoothstep(1.0 - _AuroraCoverage, 1.0, fbm(uv * 0.12 + float2(wind * 0.5, wind * 0.3)));
                    // keep the aurora near/overhead the player — fade it out toward the far horizon
                    float distFade = saturate(1.0 - length(p.xz) * 0.006);
                    // real aurora palette: violet bottom -> green body -> crimson top
                    float3 col = lerp(_AuroraColor1.rgb, _AuroraColor2.rgb, smoothstep(0.0, 0.2, h));
                    col = lerp(col, _AuroraColor3.rgb, smoothstep(0.5, 1.0, h));
                    acc += col * ribbon * (0.3 + 1.2 * rays) * vfall * env * distFade;
                    t += dt;
                }
                return acc * (13.0 / 36.0) * _AuroraIntensity;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 rd = normalize(i.dir);
                float elev = rd.y;

                // sun dir fallback so the material previews without C#
                float3 sunDir = (dot(_SunDir,_SunDir) < 0.01) ? normalize(float3(0.3,0.35,0.88)) : normalize(_SunDir);
                float3 moonDir = (dot(_MoonDir,_MoonDir) < 0.01) ? -sunDir : normalize(_MoonDir);

                float sunElev = sunDir.y;
                float day = smoothstep(-0.18, 0.14, sunElev);     // 0 night .. 1 day
                float upper = saturate(elev);

                // base atmosphere gradient
                float3 dayCol   = lerp(_DayHorizon.rgb,   _DayZenith.rgb,   pow(upper, _SkyExponent));
                float3 nightCol = lerp(_NightHorizon.rgb, _NightZenith.rgb, pow(upper, _SkyExponent + 0.1));
                float3 sky = lerp(nightCol, dayCol, day);

                // sunset / sunrise warm band near the horizon, strongest toward the sun azimuth
                float lowSun  = saturate(1.0 - abs(sunElev) / 0.35) * smoothstep(-0.25, 0.0, sunElev);
                float horizonBand = pow(saturate(1.0 - abs(elev)), 3.0);
                float2 rdAz = normalize(float2(rd.x, rd.z) + 1e-4);
                float2 snAz = normalize(float2(sunDir.x, sunDir.z) + 1e-4);
                float sunAz = pow(saturate(dot(rdAz, snAz)), 2.0);
                sky += _SunsetColor.rgb * lowSun * horizonBand * (0.35 + 0.65 * sunAz) * 1.6;

                // ground / below horizon
                sky = lerp(sky, _GroundColor.rgb * (0.3 + 0.7 * day), smoothstep(0.0, -0.05, elev));

                // stars (night, above horizon)
                float starMask = (1.0 - day) * smoothstep(-0.02, 0.08, elev);
                sky += texCUBE(_StarSkyTex, rd).rgb * _StarIntensity * starMask;

                // sun disc + glow
                float sd = dot(rd, sunDir);
                float sunDisc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.4, sd);
                float sunGlow = pow(saturate(sd), _SunGlowPow) * _SunGlow;
                sky += _SunColor.rgb * (sunDisc * 18.0 + sunGlow) * saturate(sunElev + 0.1);

                // moon: textured disc, phase-lit from the sun direction, + soft glow
                float md = dot(rd, moonDir);
                float moonGlow = pow(saturate(md), 600.0) * _MoonGlow;
                sky += _MoonColor.rgb * moonGlow * (1.0 - day * 0.85);
                float mRad = sqrt(2.0 * _MoonSize);                          // small-angle disc radius
                float3 mRight = normalize(cross(moonDir, float3(0.0, 1.0, 0.0)) + 1e-4);
                float3 mUp = cross(mRight, moonDir);
                float2 muv = float2(dot(rd, mRight), dot(rd, mUp)) / mRad;    // [-1,1] across the disc
                float mr2 = dot(muv, muv);
                if (mr2 < 1.0 && md > 0.0)
                {
                    float mz = sqrt(1.0 - mr2);
                    float3 mNrm = mRight * muv.x + mUp * muv.y - moonDir * mz; // outward normal (toward viewer at centre)
                    float lit = saturate(dot(mNrm, sunDir)) + 0.04;           // phase + faint earthshine
                    float3 albedo = tex2D(_MoonTex, muv * 0.5 + 0.5).rgb;
                    float3 moonCol = albedo * _MoonColor.rgb * lit * 2.2;
                    float edge = smoothstep(1.0, 0.9, mr2);
                    sky = lerp(sky, moonCol, edge * (1.0 - day * 0.9));
                }

                // aurora: compute once. Most of it sits behind the clouds (added to sky here)...
                float3 auroraCol = auroraColor(rd);
                sky += auroraCol;

                // clouds (composite over sky) — interleaved-gradient-noise dither (smooth, not salt-and-pepper)
                float ign = frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715))));
                float cloudA;
                float3 cloudCol = marchClouds(rd, day, ign, sky, cloudA);
                float3 col = sky * (1.0 - cloudA) + cloudCol;
                col += auroraCol * 0.4;   // ...and part of it weaves IN FRONT of the clouds

                col *= _Exposure;

                // fog: blend the sky toward the fog colour (visible against the sky, not just geometry).
                // Blankets the WHOLE dome (incl. straight up) when thick — only a touch stronger near the horizon.
                float fogH = saturate(1.0 - elev * 0.2);
                col = lerp(col, _SkyFogColor.rgb, saturate(_SkyFog * fogH * 1.5));

                // Kill 8-bit banding in the smooth dark sky/fog gradients. Dark night skies are where
                // quantization "rungs"/blobs show worst, so add a sub-LSB triangular-PDF dither (two
                // decorrelated IGN samples -> triangular noise, hides bands without looking grainy).
                float dth = frac(52.9829189 * frac(dot(i.pos.xy + 137.13, float2(0.06711056, 0.00583715))));
                col += (ign + dth - 1.0) * (1.6 / 255.0);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
