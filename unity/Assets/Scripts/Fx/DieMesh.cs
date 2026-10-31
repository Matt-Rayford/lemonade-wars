using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The die's geometry and skin: a 24-vertex cube whose faces map onto a code-drawn
    /// pip atlas, plus the soft contact-shadow quad that sits under it. Opposite faces
    /// sum to 7, like a real die.
    /// </summary>
    public static class DieMesh
    {
        public static readonly Vector3[] FaceNormals =
        {
            Vector3.forward, Vector3.back,
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
        };
        public static readonly int[] FaceValues = { 1, 6, 2, 5, 3, 4 };

        public static Vector3 NormalOf(int value) => FaceNormals[System.Array.IndexOf(FaceValues, value)];

        /// <summary>The value on top of a die in the given rotation (the face closest to straight up).</summary>
        public static int ValueUp(Quaternion rotation) => FaceValues[UpFaceIndex(rotation)];

        public static int UpFaceIndex(Quaternion rotation)
        {
            int best = 0;
            float bestDot = float.MinValue;
            for (int face = 0; face < FaceNormals.Length; face++)
            {
                float dot = Vector3.Dot(rotation * FaceNormals[face], Vector3.up);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = face;
                }
            }
            return best;
        }

        /// <summary>True when some face lies flat enough to read as "the roll".</summary>
        public static bool IsFlat(Quaternion rotation) =>
            Vector3.Dot(rotation * FaceNormals[UpFaceIndex(rotation)], Vector3.up) > 0.97f;

        /// <summary>
        /// The cube with its edges and corners rounded off: each face is a grid whose
        /// points are pushed onto a rounded box, with the true rounded-box normal, so
        /// the bevels shade smoothly. Grid points crowd toward the edges, where the
        /// curvature is. UVs stay the flat face's, so the pips keep their place.
        /// </summary>
        public static Mesh BuildRoundedCube(float cornerRadius, int segmentsPerFace)
        {
            int pointsPerSide = segmentsPerFace + 1;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            float coreHalf = 0.5f - cornerRadius;

            for (int face = 0; face < 6; face++)
            {
                var n = FaceNormals[face];
                var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, n);
                var w = Vector3.Cross(n, u);
                int tile = FaceValues[face] - 1;
                float x0 = (tile % 3) / 3f;
                float y0 = (tile / 3) / 2f;
                int baseIndex = vertices.Count;

                for (int i = 0; i < pointsPerSide; i++)
                {
                    float x = EdgeWeightedCoordinate(i, segmentsPerFace);
                    for (int j = 0; j < pointsPerSide; j++)
                    {
                        float y = EdgeWeightedCoordinate(j, segmentsPerFace);
                        var flat = n * 0.5f + u * x + w * y;
                        var core = new Vector3(
                            Mathf.Clamp(flat.x, -coreHalf, coreHalf),
                            Mathf.Clamp(flat.y, -coreHalf, coreHalf),
                            Mathf.Clamp(flat.z, -coreHalf, coreHalf));
                        var normal = (flat - core).normalized;
                        vertices.Add(core + normal * cornerRadius);
                        normals.Add(normal);
                        uv.Add(new Vector2(x0 + (x + 0.5f) / 3f, y0 + (y + 0.5f) / 2f));
                    }
                }

                for (int i = 0; i < segmentsPerFace; i++)
                {
                    for (int j = 0; j < segmentsPerFace; j++)
                    {
                        int a = baseIndex + i * pointsPerSide + j;
                        int b = a + 1;
                        int c = a + pointsPerSide + 1;
                        int d = a + pointsPerSide;
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(a); triangles.Add(d); triangles.Add(c);
                    }
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Grid coordinate in [-0.5, 0.5], denser near the ends where the bevel curves.</summary>
        private static float EdgeWeightedCoordinate(int index, int segments)
        {
            float t = index / (float)segments * 2f - 1f;
            return 0.5f * Mathf.Sign(t) * Mathf.Pow(Mathf.Abs(t), 0.6f);
        }

        /// <summary>A flat square on the floor plane, facing up, the cube's top face wound the same way.</summary>
        public static Mesh BuildFloorQuad(float size)
        {
            var u = Vector3.right * (size * 0.5f);
            var w = Vector3.back * (size * 0.5f);
            var mesh = new Mesh
            {
                vertices = new[] { -u - w, -u + w, u + w, u - w },
                uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
                colors32 = new Color32[] { Color.white, Color.white, Color.white, Color.white },
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Black disc with a soft rim; alpha carries the shape so vertex alpha can fade it.</summary>
        public static Texture2D BuildShadowTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float dy = (y + 0.5f) / size - 0.5f;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float alpha = 1f - Mathf.SmoothStep(0.55f, 1f, radius);
                    pixels[y * size + x] = new Color(0f, 0f, 0f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Draw the six faces into one atlas: plain lemonade-yellow tiles (the lit,
        /// rounded mesh draws its own edges) with anti-aliased dark pips in the
        /// standard layouts.
        /// </summary>
        public static Texture2D BuildPipAtlas()
        {
            const int tileSize = 128;
            const float pipRadius = 13f;
            var faceColor = new Color(0.98f, 0.85f, 0.22f);
            var pipColor = new Color(0.14f, 0.11f, 0.05f);

            const float lo = 0.26f, mid = 0.5f, hi = 0.74f;
            var pips = new Vector2[7][];
            pips[1] = new[] { new Vector2(mid, mid) };
            pips[2] = new[] { new Vector2(lo, hi), new Vector2(hi, lo) };
            pips[3] = new[] { new Vector2(lo, hi), new Vector2(mid, mid), new Vector2(hi, lo) };
            pips[4] = new[]
            {
                new Vector2(lo, lo), new Vector2(lo, hi),
                new Vector2(hi, lo), new Vector2(hi, hi),
            };
            pips[5] = new[]
            {
                new Vector2(lo, lo), new Vector2(lo, hi), new Vector2(mid, mid),
                new Vector2(hi, lo), new Vector2(hi, hi),
            };
            pips[6] = new[]
            {
                new Vector2(lo, lo), new Vector2(lo, mid), new Vector2(lo, hi),
                new Vector2(hi, lo), new Vector2(hi, mid), new Vector2(hi, hi),
            };

            var texture = new Texture2D(3 * tileSize, 2 * tileSize, TextureFormat.RGBA32, false);
            var pixels = new Color[texture.width * texture.height];
            for (int value = 1; value <= 6; value++)
            {
                int tileX = ((value - 1) % 3) * tileSize;
                int tileY = ((value - 1) / 3) * tileSize;
                for (int y = 0; y < tileSize; y++)
                {
                    for (int x = 0; x < tileSize; x++)
                    {
                        var color = faceColor;

                        float minDistance = float.MaxValue;
                        foreach (var pip in pips[value])
                        {
                            float dx = x - pip.x * tileSize;
                            float dy = y - pip.y * tileSize;
                            minDistance = Mathf.Min(minDistance, Mathf.Sqrt(dx * dx + dy * dy));
                        }
                        float pipBlend = Mathf.Clamp01((pipRadius - minDistance) / 1.5f + 0.5f);
                        color = Color.Lerp(color, pipColor, pipBlend);

                        pixels[(tileY + y) * texture.width + tileX + x] = color;
                    }
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
