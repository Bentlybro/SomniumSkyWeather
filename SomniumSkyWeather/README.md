# SomniumSkyWeather

A complete, drop-in **sky & weather system built specifically for [Somnium Space](https://somniumspace.com) worlds** — gorgeous, dynamic skies that *just work* inside Somnium's render pipeline, with no setup required.

Procedural day/night sky, sun + phase-lit moon + procedural stars & Milky Way, **volumetric raymarched clouds**, nine weather presets with smooth crossfades, **raymarched aurora**, rain/snow that follow the player (or stay in localized zones), lightning, fog, wind, and **multiplayer-synced time & weather with zero networking**.

Everything is procedural and original — no copyrighted assets — so it ships clean under MIT.

---

## Why it works in Somnium

Somnium worlds ship as an **AssetBundle + one scripting assembly** and **cannot add a URP `ScriptableRendererFeature`** to the client. Many Unity sky/weather assets render their volumetric clouds and fog *only* through such a feature, so in Somnium they bind to the wrong runtime camera and flicker or vanish.

SomniumSkyWeather renders the **entire sky — including raymarched volumetric clouds and aurora — through a procedural skybox material on `RenderSettings.skybox`.** That's the one path proven to render reliably in Somnium's URP: no render feature, no depth texture, no camera-binding bugs. Everything else uses stock `RenderSettings`, `Light`, and `ParticleSystem`. No blocked APIs → it passes the Somnium scripting validator.

> **Important (Somnium):** install this under **`Assets/`**, _not_ via the Package Manager — Somnium doesn't bundle the assets inside a UPM package (`Packages/…`), so a Package-Manager install ships with **no sky shader/material/textures → black sky** (see Install). Also keep the shipped **`SkyDome.mat`** assigned to the prefab's *Skybox Material Override*: it's what pulls the shader into the bundle (a shader referenced only by `Shader.Find` gets stripped).

---

## Features

- **Day/night cycle** — procedural atmosphere, animated sun, golden-hour sunsets.
- **Sun, moon & stars** — analytic sun disc + glow, a **textured, phase-lit moon**, procedural point-stars and a baked Milky Way cubemap.
- **Volumetric clouds** — true raymarch (curved planet-shell model so they curve to the horizon) through a baked 3D Worley/FBM noise, lit with Beer–Lambert + Henyey–Greenstein phase + powder, lit by the moon at night.
- **9 weather presets** — `Clear`, `Partly Cloudy`, `Overcast`, `Light Rain`, `Rain`, `Storm`, `Light Snow`, `Blizzard`, `Fog`. Two rain intensities and two snow intensities (calm → raging), all crossfading smoothly.
- **Aurora** — raymarched 3D curtains (not a flat texture): ribbon contours + vertical rays, a multi-colour gradient, weaving through the clouds. Appears on **random nights**, fades in and out, sits near the player. Synced so all players see the same aurora nights.
- **Rain & snow** — GPU particles that **follow the player so precipitation covers the whole world**, or switch a `WeatherParticles` to `FixedArea` for a localized always-on zone (rain in one region only).
- **Lightning** — ambient + cloud flashes during storms.
- **Fog** — in-shader sky fog (the sky itself fogs, not just scene geometry) + standard scene fog.
- **Wind** — drives cloud motion and slants precipitation.
- **Multiplayer sync** — time, weather, and aurora are deterministic functions of a shared UTC clock, so every client agrees with **no network messages**; late joiners are instantly correct.
- **Editor control panel** — a friendly custom inspector with a time-of-day scrubber and one-click weather buttons; previews live in edit mode.

---

## Requirements

- **Unity 6000.x** (developed on 6000.3) with **URP** in **Linear** color space.
- **Somnium Space SDK** — needed by `SomniumWeatherIntegration.cs` (it references `SomniumBridge`). The rest of the system is plain URP and works in any project; delete that one script to use the sky standalone.

## Install

> ⚠️ **For Somnium, install it under `Assets/` — not via the Package Manager.** Somnium ships your `Assets/` + scripting assemblies but **not the assets inside a UPM package** (`Packages/…`), so a Package-Manager install gives a **black sky** (no shader/material/textures in the bundle).

**Recommended (Somnium) — put it in `Assets/`:** copy the **`SomniumSkyWeather`** folder into your project's **`Assets/`**, **or** `git clone https://github.com/Bentlybro/SomniumSkyWeather.git` and move its inner **`SomniumSkyWeather`** folder into **`Assets/`** (make sure it lands *inside* `Assets/`).

**Non-Somnium URP only — Package Manager (git URL):** *Window ▸ Package Manager ▸ ➕ ▸ Add package from git URL…* → `https://github.com/Bentlybro/SomniumSkyWeather.git?path=/SomniumSkyWeather` (append `#v1.0.0` to pin). ⚠️ Not for Somnium — see above.

Then:

1. Drag **`Bently Sky & Weather.prefab`** into your world scene (one per world).
2. In the Somnium World Uploader, set the **Scripting Assembly** to **`Bently.Weather`**.
3. Upload. The sky animates, weather cycles, and (with `Synchronized` on) every player sees the same thing.

The scene's Directional Light is auto-adopted as the sun; if there isn't one, it's created. Textures are pre-baked and included — you don't need to bake anything. (To re-bake your own: **Tools ▸ Bently Sky & Weather ▸ Bake Textures**.)

There's a ready demo in **`SomniumSkyWeather/Demo/SomniumSkyWeather Demo.unity`** — open it and press Play.

---

## Scripting API

```csharp
var sky = SkyWeather.Instance;
sky.SetWeather("Storm");      // by name
sky.SetWeather(5, 6f);        // by index, 6-second crossfade
sky.SetTimeOfDay(18.5f);      // 0..24
```

Weather presets are plain serialized data on the `SkyWeather` component — edit them or add your own right in the inspector, no ScriptableObjects to wire up.

## Components

- **SkyWeather** — the one controller: time of day, sun/moon/stars, lighting, ambient, fog, weather state machine, aurora, and the synced clock.
- **WeatherParticles** (child) — runtime rain & snow. `Coverage = FollowViewer` (whole world) or `FixedArea` (localized zone); `useGlobalWeather` off makes an always-on local rain/snow zone.
- **SomniumWeatherIntegration** — optional Somnium glue: registers the skybox/sun with Somnium's environment system and makes precipitation follow the local player's head. Safely no-ops outside a Somnium session.

## Performance (VR)

Cloud raymarching runs in the skybox fragment shader. Lower **`Cloud Steps`** on `SkyWeather` (or `_CloudSteps` on the material) for lower-end headsets; particle emission is capped in `WeatherParticles`. No per-frame render passes, no compute buffers, no depth texture.

## Limitations / roadmap

- Clouds live in the sky dome — you can't fly *through* them and scene geometry won't occlude them. A future v2 could add scene-integrated fly-through clouds, but only after confirming Somnium's client exposes a depth texture and the render callback.

## License

MIT — see [LICENSE](../LICENSE). All textures (cloud noise, star cubemap, moon) are generated procedurally by the included baker; no third-party assets are redistributed.
