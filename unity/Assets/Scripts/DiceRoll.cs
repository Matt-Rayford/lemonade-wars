using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The sale-roll moment: a real 3D D6 (procedural cube + code-drawn pip atlas)
    /// rendered by its own camera into a RenderTexture and shown over a dimmed table.
    /// The die tumbles convincingly, then eases onto the face the engine already rolled —
    /// the result is server/engine-decided, the physics is theatre. Rolls queue and play
    /// back-to-back, speeding up if they pile up. Click anywhere to skip ahead.
    /// </summary>
    public sealed class DiceRoller
    {
        private struct Roll
        {
            public string Title;
            public int Value;
        }

        private enum Phase
        {
            Idle,
            Tumble,
            Settle,
            Hold,
            FadeOut,
        }

        private const int DieLayer = 31; // unnamed project layer reserved for the die rig
        private const float DieScale = 1.7f;
        private const float TumbleSeconds = 0.7f;
        private const float SettleSeconds = 0.5f;
        private const float HoldSeconds = 0.9f;
        private const float FadeSeconds = 0.22f;

        // Opposite faces sum to 7, like a real die.
        private static readonly Vector3[] FaceNormals =
        {
            Vector3.forward, Vector3.back,
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
        };
        private static readonly int[] FaceValues = { 1, 6, 2, 5, 3, 4 };

        public System.Action OnFinished;

        /// <summary>True while an animation plays or rolls are queued; modals wait on this.</summary>
        public bool IsBusy => _phase != Phase.Idle || _queue.Count > 0;

        private readonly Queue<Roll> _queue = new Queue<Roll>();
        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly TMP_Text _title;
        private readonly TMP_Text _subtitle;
        private readonly Camera _camera;
        private readonly Transform _die;
        private readonly Mesh _mesh;
        private readonly Color32[] _faceColors = new Color32[24];

        private Phase _phase = Phase.Idle;
        private float _t;
        private float _speed = 1f;
        private Roll _current;
        private Vector3 _spinAxis;
        private float _spinFlip;
        private Quaternion _settleFrom;
        private Quaternion _settleTo;

        public DiceRoller(RectTransform canvasRoot)
        {
            // ------------------------------------------------------ 3D rig
            // Lives far below the origin on its own layer; only the die camera sees it.
            var rig = new GameObject("DieRig");
            rig.transform.position = new Vector3(0, -500f, 0);

            var cameraGo = new GameObject("DieCamera", typeof(Camera));
            cameraGo.transform.SetParent(rig.transform, false);
            // Long lens from further back: gentle perspective, so the settled die
            // reads as a tidy square with just a hint of its top/side edges.
            cameraGo.transform.localPosition = new Vector3(0, 0, -9f);
            _camera = cameraGo.GetComponent<Camera>();
            _camera.fieldOfView = 18f;
            _camera.nearClipPlane = 0.5f;
            _camera.farClipPlane = 20f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0, 0, 0, 0);
            _camera.cullingMask = 1 << DieLayer;
            var renderTexture = new RenderTexture(512, 512, 16) { antiAliasing = 4 };
            _camera.targetTexture = renderTexture;
            _camera.enabled = false;

            if (Camera.main != null)
            {
                Camera.main.cullingMask &= ~(1 << DieLayer);
            }

            var dieGo = new GameObject("Die", typeof(MeshFilter), typeof(MeshRenderer));
            dieGo.layer = DieLayer;
            dieGo.transform.SetParent(rig.transform, false);
            dieGo.transform.localScale = Vector3.one * DieScale;
            _die = dieGo.transform;
            _mesh = BuildCube();
            dieGo.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var shader = Resources.Load<Shader>("shaders/DieUnlit");
            var material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            material.mainTexture = BuildPipAtlas();
            var meshRenderer = dieGo.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            // ---------------------------------------------------- UI overlay
            _root = UiKit.CreatePanel(canvasRoot, "DiceOverlay", new Color(0, 0, 0, 0.45f));
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);
            _group = _root.gameObject.AddComponent<CanvasGroup>();

            _title = UiKit.CreateText(_root, "", 36, TextAnchor.MiddleCenter, Color.white);
            UiKit.Anchor((RectTransform)_title.transform,
                new Vector2(0.1f, 0.73f), new Vector2(0.9f, 0.81f));
            UiKit.AddTextShadow(_title);

            var viewGo = new GameObject("DieView", typeof(RectTransform), typeof(RawImage));
            viewGo.transform.SetParent(_root, false);
            var viewRect = (RectTransform)viewGo.transform;
            viewRect.anchorMin = viewRect.anchorMax = new Vector2(0.5f, 0.5f);
            viewRect.sizeDelta = new Vector2(320, 320);
            var view = viewGo.GetComponent<RawImage>();
            view.texture = renderTexture;
            view.raycastTarget = false;

            _subtitle = UiKit.CreateText(_root, "", 32, TextAnchor.MiddleCenter, UiKit.ButtonColor);
            UiKit.Anchor((RectTransform)_subtitle.transform,
                new Vector2(0.1f, 0.17f), new Vector2(0.9f, 0.26f));
            UiKit.AddTextShadow(_subtitle);

            UiKit.AddClick(_root.gameObject, Skip);
            _root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ public

        public void Enqueue(string rollerName, int value, bool isYou)
        {
            EnqueueRoll(
                isYou ? "YOU ROLL FOR SALES..." : $"{rollerName.ToUpperInvariant()} ROLLS FOR SALES...",
                value);
        }

        /// <summary>Out of Stock forced a reroll: show the die landing on its new value.</summary>
        public void EnqueueReroll(string rollerName, int value, bool isYou)
        {
            EnqueueRoll(
                isYou ? "YOU REROLL THE DIE..." : $"{rollerName.ToUpperInvariant()} REROLLS THE DIE...",
                value);
        }

        private void EnqueueRoll(string title, int value)
        {
            _queue.Enqueue(new Roll
            {
                Title = title,
                Value = Mathf.Clamp(value, 1, 6),
            });
            if (_phase == Phase.Idle)
            {
                BeginNext();
            }
        }

        /// <summary>Drop everything instantly (screen change, autopilot). No OnFinished.</summary>
        public void Clear()
        {
            _queue.Clear();
            _phase = Phase.Idle;
            _root.gameObject.SetActive(false);
            _camera.enabled = false;
        }

        /// <summary>Advance the animation; call every frame while in game.</summary>
        public void Tick()
        {
            if (_phase == Phase.Idle)
            {
                return;
            }
            _t += Time.deltaTime * _speed;

            switch (_phase)
            {
                case Phase.Tumble:
                    TickTumble();
                    break;
                case Phase.Settle:
                    TickSettle();
                    break;
                case Phase.Hold:
                    // A little landing punch, then rest.
                    float punch = Mathf.Clamp01(_t / 0.18f);
                    _die.localScale = Vector3.one * (DieScale * (1f + 0.12f * Mathf.Sin(punch * Mathf.PI)));
                    if (_t >= HoldSeconds)
                    {
                        SetPhase(Phase.FadeOut);
                    }
                    break;
                case Phase.FadeOut:
                    _group.alpha = 1f - Mathf.Clamp01(_t / FadeSeconds);
                    if (_t >= FadeSeconds)
                    {
                        if (_queue.Count > 0)
                        {
                            BeginNext();
                        }
                        else
                        {
                            Clear();
                            OnFinished?.Invoke();
                        }
                    }
                    break;
            }
            ShadeFaces();
        }

        // --------------------------------------------------------- animation

        private void BeginNext()
        {
            _current = _queue.Dequeue();
            // Rolls stacking up (bot round in full swing): play faster so we keep pace.
            _speed = _queue.Count >= 3 ? 2.4f : _queue.Count >= 1 ? 1.6f : 1f;
            _title.text = _current.Title;
            _subtitle.text = "";
            _group.alpha = 1f;
            _root.gameObject.SetActive(true);
            _camera.enabled = true;
            _die.rotation = Random.rotationUniform;
            _spinAxis = Random.onUnitSphere;
            _spinFlip = 0f;
            SetPhase(Phase.Tumble);
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _t = 0f;
        }

        private void TickTumble()
        {
            float progress = Mathf.Clamp01(_t / TumbleSeconds);
            // Pop in, spin hard, decelerate; swap the spin axis once mid-flight for chaos.
            float scaleIn = Mathf.Clamp01(_t / 0.22f);
            _die.localScale = Vector3.one * (DieScale * (0.6f + 0.4f * (1f - Mathf.Pow(1f - scaleIn, 3f))));
            if (progress > 0.5f && _spinFlip == 0f)
            {
                _spinFlip = 1f;
                _spinAxis = Random.onUnitSphere;
            }
            float degreesPerSecond = Mathf.Lerp(760f, 280f, progress);
            _die.rotation = Quaternion.AngleAxis(
                degreesPerSecond * Time.deltaTime * _speed, _spinAxis) * _die.rotation;

            if (_t >= TumbleSeconds)
            {
                _settleFrom = _die.rotation;
                _settleTo = ClosestShowing(_current.Value, _die.rotation);
                SetPhase(Phase.Settle);
            }
        }

        private void TickSettle()
        {
            float progress = Mathf.Clamp01(_t / SettleSeconds);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            _die.rotation = Quaternion.Slerp(_settleFrom, _settleTo, eased);
            if (_t >= SettleSeconds)
            {
                Land();
            }
        }

        private void Land()
        {
            _die.rotation = _settleTo;
            _subtitle.text = $"STANDS WITH A {_current.Value} SELL!";
            SetPhase(Phase.Hold);
        }

        /// <summary>Click-to-skip: land immediately, or end the hold early.</summary>
        private void Skip()
        {
            switch (_phase)
            {
                case Phase.Tumble:
                    _settleTo = ClosestShowing(_current.Value, _die.rotation);
                    Land();
                    break;
                case Phase.Settle:
                    Land();
                    break;
                case Phase.Hold:
                    SetPhase(Phase.FadeOut);
                    break;
            }
        }

        /// <summary>
        /// The rotation showing <paramref name="value"/> to the camera that is closest to
        /// the current rotation — there are four (one per in-plane spin); picking the
        /// nearest keeps the settle short and natural.
        /// </summary>
        private static Quaternion ClosestShowing(int value, Quaternion current)
        {
            // Settle slightly cocked toward the camera — a dead-flat face reads as a
            // 2D sprite; a whisper of top/side edge keeps it looking like a real die.
            var tilt = Quaternion.Euler(-9f, 11f, 0f);
            var normal = FaceNormals[System.Array.IndexOf(FaceValues, value)];
            var baseRotation = Quaternion.FromToRotation(normal, Vector3.back);
            Quaternion best = baseRotation;
            float bestAngle = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                var candidate = tilt * Quaternion.AngleAxis(90f * k, Vector3.back) * baseRotation;
                float angle = Quaternion.Angle(current, candidate);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// Fake diffuse lighting on the CPU: brightness per face from its world normal,
        /// written into vertex colors (the shader multiplies). Works under any pipeline.
        /// </summary>
        private void ShadeFaces()
        {
            var light = new Vector3(-0.35f, 0.6f, -0.72f).normalized;
            for (int face = 0; face < 6; face++)
            {
                var worldNormal = _die.rotation * FaceNormals[face];
                float brightness = 0.45f + 0.55f * Mathf.Max(0f, Vector3.Dot(worldNormal, light));
                byte level = (byte)(Mathf.Clamp01(brightness) * 255);
                var color = new Color32(level, level, level, 255);
                for (int v = 0; v < 4; v++)
                {
                    _faceColors[face * 4 + v] = color;
                }
            }
            _mesh.colors32 = _faceColors;
        }

        // ---------------------------------------------------- mesh + texture

        /// <summary>24-vertex cube, each face UV-mapped to its pip tile in the atlas.</summary>
        private static Mesh BuildCube()
        {
            var vertices = new Vector3[24];
            var uv = new Vector2[24];
            var triangles = new int[36];

            for (int face = 0; face < 6; face++)
            {
                var n = FaceNormals[face];
                var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, n);
                var w = Vector3.Cross(n, u);
                int b = face * 4;
                vertices[b + 0] = (n - u - w) * 0.5f;
                vertices[b + 1] = (n - u + w) * 0.5f;
                vertices[b + 2] = (n + u + w) * 0.5f;
                vertices[b + 3] = (n + u - w) * 0.5f;

                // Tile for this face's value: 3 columns x 2 rows.
                int tile = FaceValues[face] - 1;
                float x0 = (tile % 3) / 3f;
                float y0 = (tile / 3) / 2f;
                uv[b + 0] = new Vector2(x0, y0);
                uv[b + 1] = new Vector2(x0, y0 + 0.5f);
                uv[b + 2] = new Vector2(x0 + 1f / 3f, y0 + 0.5f);
                uv[b + 3] = new Vector2(x0 + 1f / 3f, y0);

                // Clockwise from outside — wound the other way you get the Cornell-box
                // effect: culling keeps the cube's INSIDE faces and it reads as a room.
                int t = face * 6;
                triangles[t + 0] = b;
                triangles[t + 1] = b + 2;
                triangles[t + 2] = b + 1;
                triangles[t + 3] = b;
                triangles[t + 4] = b + 3;
                triangles[t + 5] = b + 2;
            }

            var mesh = new Mesh
            {
                vertices = vertices,
                uv = uv,
                triangles = triangles,
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Draw the six faces into one atlas: lemonade-yellow tiles, darker edge border
        /// (reads as the cube edge), anti-aliased dark pips in the standard layouts.
        /// </summary>
        private static Texture2D BuildPipAtlas()
        {
            const int tileSize = 128;
            const float pipRadius = 13f;
            const int border = 6;
            var faceColor = new Color(0.98f, 0.85f, 0.22f);
            var borderColor = new Color(0.80f, 0.66f, 0.10f);
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
                        bool onBorder = x < border || y < border ||
                            x >= tileSize - border || y >= tileSize - border;
                        var color = onBorder ? borderColor : faceColor;

                        float minDistance = float.MaxValue;
                        foreach (var pip in pips[value])
                        {
                            float dx = x - pip.x * tileSize;
                            float dy = y - pip.y * tileSize;
                            minDistance = Mathf.Min(minDistance, Mathf.Sqrt(dx * dx + dy * dy));
                        }
                        // Anti-aliased pip edge.
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
