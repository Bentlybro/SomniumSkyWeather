using UnityEngine;
using UnityEditor;
using SomniumSpace.Worlds.Bently.Weather;

namespace SomniumSpace.Worlds.Bently.Weather.EditorTools
{
    /// <summary>
    /// A friendly control panel for <see cref="SkyWeather"/>: collapsible sections, a live
    /// time-of-day scrubber, and one-click weather buttons. Everything updates the scene live
    /// (the component runs in edit mode), so you can dial in the look without entering Play.
    /// </summary>
    [CustomEditor(typeof(SkyWeather))]
    public class SkyWeatherEditor : Editor
    {
        static bool _fTime = true, _fWeather = true, _fClouds, _fSky, _fAurora, _fWindStorm, _fSync, _fRefs;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var sky = (SkyWeather)target;

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("☀  Bently Sky & Weather", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(StateLine(sky), EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            Section("Time of Day", ref _fTime, () =>
            {
                var tod = serializedObject.FindProperty("timeOfDay");
                tod.floatValue = EditorGUILayout.Slider(TimeLabel(tod.floatValue), tod.floatValue, 0f, 24f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Dawn 6:00")) tod.floatValue = 6f;
                    if (GUILayout.Button("Noon 12:00")) tod.floatValue = 12f;
                    if (GUILayout.Button("Dusk 18:30")) tod.floatValue = 18.5f;
                    if (GUILayout.Button("Night 0:30")) tod.floatValue = 0.5f;
                }
                Prop("autoAdvance"); Prop("animateInEditor"); Prop("dayLengthSeconds"); Prop("sunAzimuth");
            });

            Section("Weather", ref _fWeather, () =>
            {
                DrawWeatherButtons(sky);
                EditorGUILayout.Space(2);
                Prop("weatherBlendSeconds"); Prop("autoWeather"); Prop("autoWeatherInterval");
                Prop("startWeatherIndex");
                Prop("weatherTypes", true);
            });

            Section("Cloud Look", ref _fClouds, () =>
            {
                Prop("cloudType"); Prop("cloudScale"); Prop("cloudDetail");
                Prop("cloudBottom"); Prop("cloudThickness"); Prop("cloudAbsorption");
                Prop("cloudFade"); Prop("curvatureRadius"); Prop("cloudSpeed");
                Prop("cloudSteps"); Prop("highCloudAmount"); Prop("highCloudScale");
            });

            Section("Sun / Moon / Stars", ref _fSky, () =>
            {
                Prop("sunColor"); Prop("sunIntensity");
                Prop("moonColor"); Prop("moonIntensity");
                Prop("ambientStrength"); Prop("baseExposure");
                Prop("sunSize"); Prop("moonSize"); Prop("starIntensity");
            });

            Section("Aurora", ref _fAurora, () =>
            {
                EditorGUILayout.HelpBox("Appears on random nights, fading in/out. With sync on, the same nights show aurora for every player.", MessageType.None);
                Prop("auroraEnabled"); Prop("auroraChance"); Prop("auroraIntensity"); Prop("auroraCoverage");
                Prop("auroraColor1"); Prop("auroraColor2"); Prop("auroraColor3"); Prop("auroraSeed");
            });

            Section("Wind & Storm", ref _fWindStorm, () =>
            {
                Prop("windHeading"); Prop("lightningEnabled");
            });

            Section("Multiplayer Sync", ref _fSync, () =>
            {
                EditorGUILayout.HelpBox("When on, time & auto-weather derive from a shared UTC clock so every player sees the same sky — no networking needed.", MessageType.None);
                Prop("synchronized"); Prop("weatherPeriodSeconds"); Prop("weatherSeed");
            });

            Section("References & Textures", ref _fRefs, () =>
            {
                Prop("sunLight"); Prop("skyboxMaterialOverride");
                Prop("cloudNoise"); Prop("starSky"); Prop("moonTexture");
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Bake / Re-bake Textures"))
                    SkyTextureBaker.BakeAll();
            });

            serializedObject.ApplyModifiedProperties();
            if (!Application.isPlaying && GUI.changed)
                EditorUtility.SetDirty(sky);
        }

        void DrawWeatherButtons(SkyWeather sky)
        {
            if (sky.weatherTypes == null || sky.weatherTypes.Count == 0) return;
            int current = sky.CurrentWeatherIndex;
            int perRow = 3;
            for (int i = 0; i < sky.weatherTypes.Count; i++)
            {
                if (i % perRow == 0) EditorGUILayout.BeginHorizontal();
                var wt = sky.weatherTypes[i];
                Color prev = GUI.backgroundColor;
                if (i == current) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
                if (GUILayout.Button(wt != null ? wt.name : "?", GUILayout.Height(24)))
                {
                    Undo.RecordObject(sky, "Change Weather");
                    sky.SetWeather(i, Application.isPlaying ? -1f : 0f);
                    EditorUtility.SetDirty(sky);
                }
                GUI.backgroundColor = prev;
                if (i % perRow == perRow - 1 || i == sky.weatherTypes.Count - 1) EditorGUILayout.EndHorizontal();
            }
        }

        // ---- helpers ----
        void Prop(string name, bool children = false)
        {
            var p = serializedObject.FindProperty(name);
            if (p != null) EditorGUILayout.PropertyField(p, children);
        }

        static GUIStyle _hdr;
        static void Section(string title, ref bool open, System.Action body)
        {
            if (_hdr == null) _hdr = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            EditorGUILayout.Space(2);
            // plain foldout (not a FoldoutHeaderGroup) so it can safely contain list/array foldouts
            open = EditorGUILayout.Foldout(open, title, true, _hdr);
            if (open)
            {
                EditorGUI.indentLevel++;
                body();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }
        }

        static string TimeLabel(float h)
        {
            int hh = Mathf.FloorToInt(h) % 24;
            int mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f);
            return string.Format("Time  {0:00}:{1:00}", hh, mm);
        }

        static string StateLine(SkyWeather sky)
        {
            string w = (sky.weatherTypes != null && sky.CurrentWeatherIndex < sky.weatherTypes.Count && sky.weatherTypes[sky.CurrentWeatherIndex] != null)
                ? sky.weatherTypes[sky.CurrentWeatherIndex].name : "-";
            return string.Format("{0}   •   {1}", TimeLabel(sky.timeOfDay), sky.IsNight ? "night · " + w : "day · " + w);
        }
    }
}
