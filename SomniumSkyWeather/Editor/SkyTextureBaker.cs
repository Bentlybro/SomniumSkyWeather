using UnityEngine;
using UnityEditor;
using System.IO;

namespace SomniumSpace.Worlds.Bently.Weather.EditorTools
{
    /// <summary>
    /// Generates the asset's textures procedurally so everything is 100% owned and
    /// safe to redistribute (MIT) — no copyrighted source textures.
    ///  - CloudNoise3D : tileable Worley FBM (R = base billows, GBA = erosion detail)
    ///  - StarSky      : cubemap star field + Milky Way band
    /// Run from the menu: Tools ▸ Bently Sky & Weather ▸ Bake Textures.
    /// </summary>
    public static class SkyTextureBaker
    {
        const string Folder = "Assets/#User/Bently.Weather/Textures";
        const int CloudSize = 64;     // 3D texture resolution (per axis)
        const int StarFace = 512;     // cubemap face resolution (higher = sharper, smaller stars)
        const int MoonSize = 512;     // moon texture resolution

        [MenuItem("Tools/Bently Sky & Weather/Bake Textures")]
        public static void BakeAll()
        {
            EnsureFolder();
            var cloud = BakeCloudNoise3D();
            var stars = BakeStarCubemap();
            var moon = BakeMoon();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Bently Sky] Baked textures into {Folder}\n - {AssetDatabase.GetAssetPath(cloud)}\n - {AssetDatabase.GetAssetPath(stars)}\n - {AssetDatabase.GetAssetPath(moon)}");
            EditorUtility.DisplayDialog("Bently Sky & Weather", "Textures baked into\n" + Folder, "Nice");
        }

        // ----------------------------------------------------------------- cloud 3D
        public static Texture3D BakeCloudNoise3D()
        {
            int N = CloudSize;
            var cols = new Color[N * N * N];
            for (int z = 0; z < N; z++)
            {
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        Vector3 p = new Vector3((x + 0.5f) / N, (y + 0.5f) / N, (z + 0.5f) / N);
                        float baseShape = WorleyFbm(p, 4, 0);        // big billows
                        float d1 = 1f - WorleyTileable(p, 8, 31);    // detail octaves (erosion)
                        float d2 = 1f - WorleyTileable(p, 16, 47);
                        float d3 = 1f - WorleyTileable(p, 24, 61);
                        cols[x + y * N + z * N * N] = new Color(baseShape, Clamp01(d1), Clamp01(d2), Clamp01(d3));
                    }
                }
                if ((z & 7) == 0) EditorUtility.DisplayProgressBar("Baking cloud noise", "slice " + z + "/" + N, z / (float)N);
            }
            EditorUtility.ClearProgressBar();

            var tex = new Texture3D(N, N, N, TextureFormat.RGBA32, true) { name = "CloudNoise3D", wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels(cols);
            tex.Apply(true);
            return SaveAsset(tex, "CloudNoise3D.asset");
        }

        // ----------------------------------------------------------------- star cubemap
        public static Cubemap BakeStarCubemap()
        {
            int S = StarFace;
            var cube = new Cubemap(S, TextureFormat.RGBA32, true) { name = "StarSky", wrapMode = TextureWrapMode.Clamp };
            Vector3 galacticNormal = new Vector3(0.35f, 0.30f, 0.88f).normalized;

            for (int face = 0; face < 6; face++)
            {
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                {
                    for (int x = 0; x < S; x++)
                    {
                        float u = (x + 0.5f) / S * 2f - 1f;
                        float v = (y + 0.5f) / S * 2f - 1f;
                        Vector3 dir = FaceDir(face, u, v).normalized;

                        // Stars: sparse, slight colour temperature + brightness variation.
                        float s = StarValue(dir, 720f, 0);
                        float s2 = StarValue(dir, 1500f, 99) * 0.6f;     // faint dense layer
                        float star = Mathf.Max(s, s2);
                        Color starCol = Color.Lerp(new Color(0.7f, 0.8f, 1f), new Color(1f, 0.85f, 0.65f), Hash1Dir(dir, 5));

                        // Milky Way band: bright noisy band around the galactic plane.
                        float band = 1f - Mathf.SmoothStep(0f, 0.32f, Mathf.Abs(Vector3.Dot(dir, galacticNormal)));
                        float milkN = 0.5f + 0.5f * FbmDir(dir, 3, 0);
                        float milk = band * band * milkN * 0.18f;
                        Color milkCol = new Color(0.45f, 0.5f, 0.7f) * milk;

                        Color c = starCol * star + milkCol;
                        px[y * S + x] = new Color(c.r, c.g, c.b, 1f);
                    }
                }
                cube.SetPixels(px, (CubemapFace)face);
                EditorUtility.DisplayProgressBar("Baking star sky", "face " + (face + 1) + "/6", face / 6f);
            }
            EditorUtility.ClearProgressBar();
            cube.Apply(true);
            return SaveAsset(cube, "StarSky.cubemap");
        }

        // ----------------------------------------------------------------- moon
        // A round moon disc: grey albedo with darker maria (seas) and crater speckle.
        // alpha = inside the disc. The shader phase-lights it from the sun direction.
        public static Texture2D BakeMoon()
        {
            int S = MoonSize;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f;
                    float v = (y + 0.5f) / S * 2f - 1f;
                    float r2 = u * u + v * v;
                    if (r2 > 1f) { px[y * S + x] = new Color(0f, 0f, 0f, 0f); continue; }
                    float z = Mathf.Sqrt(1f - r2);
                    Vector3 nrm = new Vector3(u, v, z);

                    float maria  = FbmDir(nrm * 1.6f, 4, 10);   // -1..1 large dark seas
                    float fine   = FbmDir(nrm * 7f, 3, 20);
                    float crater = FbmDir(nrm * 17f, 2, 33);

                    float albedo = 0.66f + 0.10f * fine;
                    albedo -= Mathf.Clamp01(maria * 0.6f + 0.1f) * 0.30f;   // darken maria
                    albedo += crater * 0.05f;                                // crater speckle
                    albedo = Mathf.Clamp01(albedo);
                    albedo *= Mathf.Lerp(0.82f, 1f, z);                      // slight limb darkening

                    px[y * S + x] = new Color(albedo, albedo * 0.985f, albedo * 0.95f, 1f);
                }
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true)
            { name = "Moon", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply(true);
            return SaveAsset(tex, "Moon.asset");
        }

        // ----------------------------------------------------------------- noise helpers
        static float Hash1(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(seed * 2654435761u);
                h ^= h >> 15; h *= 0x2c1b3c6dU; h ^= h >> 12; h *= 0x297a2d39U; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
        static Vector3 Hash3(int x, int y, int z, int seed)
        {
            return new Vector3(Hash1(x, y, z, seed), Hash1(x, y, z, seed + 101), Hash1(x, y, z, seed + 202));
        }

        // Tileable Worley F1 distance, p in [0,1)
        static float WorleyTileable(Vector3 p, int freq, int seed)
        {
            Vector3 pf = p * freq;
            int xi = Mathf.FloorToInt(pf.x), yi = Mathf.FloorToInt(pf.y), zi = Mathf.FloorToInt(pf.z);
            Vector3 f = new Vector3(pf.x - xi, pf.y - yi, pf.z - zi);
            float minD = 10f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int wx = Mod(xi + dx, freq), wy = Mod(yi + dy, freq), wz = Mod(zi + dz, freq);
                        Vector3 feat = new Vector3(dx, dy, dz) + Hash3(wx, wy, wz, seed);
                        float d = (feat - f).sqrMagnitude;
                        if (d < minD) minD = d;
                    }
            return Mathf.Sqrt(minD);
        }

        static float WorleyFbm(Vector3 p, int baseFreq, int seed)
        {
            float w = 0.625f * (1f - Clamp01(WorleyTileable(p, baseFreq, seed)))
                    + 0.250f * (1f - Clamp01(WorleyTileable(p, baseFreq * 2, seed + 7)))
                    + 0.125f * (1f - Clamp01(WorleyTileable(p, baseFreq * 4, seed + 13)));
            return Clamp01(w);
        }

        // direction-domain star value: a small compact POINT inside some cells (not a whole cell)
        static float StarValue(Vector3 dir, float scale, int seed)
        {
            Vector3 sp = dir * scale;
            int xi = Mathf.FloorToInt(sp.x), yi = Mathf.FloorToInt(sp.y), zi = Mathf.FloorToInt(sp.z);
            if (Hash1(xi, yi, zi, seed) < 0.91f) return 0f;                 // only ~9% of cells host a star
            Vector3 fp = new Vector3(sp.x - xi, sp.y - yi, sp.z - zi);
            Vector3 feat = new Vector3(Hash1(xi, yi, zi, seed + 1), Hash1(xi, yi, zi, seed + 2), Hash1(xi, yi, zi, seed + 3));
            float dist = (fp - feat).magnitude;
            float star = Mathf.Clamp01(1f - dist / 0.09f);                  // tight point
            star = star * star * star;
            return star * (0.35f + 0.65f * Hash1(xi, yi, zi, seed + 7));    // brightness variation
        }

        static float Hash1Dir(Vector3 dir, int seed)
        {
            Vector3 sp = dir * 311f;
            return Hash1(Mathf.FloorToInt(sp.x), Mathf.FloorToInt(sp.y), Mathf.FloorToInt(sp.z), seed);
        }

        static float FbmDir(Vector3 dir, int oct, int seed)
        {
            float v = 0f, a = 0.5f; float fr = 3f;
            for (int i = 0; i < oct; i++)
            {
                Vector3 sp = dir * fr;
                int xi = Mathf.FloorToInt(sp.x), yi = Mathf.FloorToInt(sp.y), zi = Mathf.FloorToInt(sp.z);
                Vector3 f = new Vector3(sp.x - xi, sp.y - yi, sp.z - zi);
                f = new Vector3(Smooth(f.x), Smooth(f.y), Smooth(f.z));
                float c = Trilerp(xi, yi, zi, f, seed + i);
                v += a * c; a *= 0.5f; fr *= 2.03f;
            }
            return v * 2f - 1f;
        }

        static float Trilerp(int xi, int yi, int zi, Vector3 f, int seed)
        {
            float c000 = Hash1(xi, yi, zi, seed), c100 = Hash1(xi + 1, yi, zi, seed);
            float c010 = Hash1(xi, yi + 1, zi, seed), c110 = Hash1(xi + 1, yi + 1, zi, seed);
            float c001 = Hash1(xi, yi, zi + 1, seed), c101 = Hash1(xi + 1, yi, zi + 1, seed);
            float c011 = Hash1(xi, yi + 1, zi + 1, seed), c111 = Hash1(xi + 1, yi + 1, zi + 1, seed);
            float x00 = Mathf.Lerp(c000, c100, f.x), x10 = Mathf.Lerp(c010, c110, f.x);
            float x01 = Mathf.Lerp(c001, c101, f.x), x11 = Mathf.Lerp(c011, c111, f.x);
            float y0 = Mathf.Lerp(x00, x10, f.y), y1 = Mathf.Lerp(x01, x11, f.y);
            return Mathf.Lerp(y0, y1, f.z);
        }

        static Vector3 FaceDir(int face, float u, float v)
        {
            switch (face)
            {
                case 0: return new Vector3(1f, -v, -u);   // +X
                case 1: return new Vector3(-1f, -v, u);   // -X
                case 2: return new Vector3(u, 1f, v);     // +Y
                case 3: return new Vector3(u, -1f, -v);   // -Y
                case 4: return new Vector3(u, -v, 1f);    // +Z
                default: return new Vector3(-u, -v, -1f);  // -Z
            }
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        static float Smooth(float t) { return t * t * (3f - 2f * t); }

        // ----------------------------------------------------------------- io
        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                string parent = "Assets/#User/Bently.Weather";
                if (!AssetDatabase.IsValidFolder(parent)) Directory.CreateDirectory(parent);
                AssetDatabase.CreateFolder(parent, "Textures");
            }
        }

        static T SaveAsset<T>(T obj, string fileName) where T : Object
        {
            string path = Folder + "/" + fileName;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { EditorUtility.CopySerialized(obj, existing); return existing; }
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }
    }
}
