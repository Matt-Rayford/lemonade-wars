using System.Collections.Generic;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The sale-roll moment: a real 3D D6 (procedural cube + code-drawn pip atlas) seen
    /// by its own perspective camera, composited over the whole table. Every roll is a
    /// physics throw recorded ahead of time (<see cref="DieThrowSimulator"/>): the die
    /// flies in from a screen edge, bounces off the edges, and settles wherever it
    /// stops — with its faces relabelled so the number on top is the one the engine
    /// already rolled. Rolls queue and play back-to-back, speeding up if they pile up.
    /// Click anywhere to skip ahead.
    /// </summary>
    public sealed class DiceRoller
    {
        private struct Roll
        {
            public string Title;
            public int Value;
            /// <summary>Modifier flip: no fresh throw — the die hops over to the new face.</summary>
            public bool Flip;
            /// <summary>Fires once the number is showing; the roll's log line waits on it.</summary>
            public System.Action OnLanded;
        }

        private enum Phase
        {
            Idle,
            Rolling,
            Hold,
            /// <summary>Settled and waiting on a response window — visible but NOT busy.</summary>
            Parked,
            FadeOut,
        }

        private const int DieLayer = 31; // unnamed project layer reserved for the die rig
        /// <summary>Straight down: a settled die reads as its number; perspective still shows sides away from centre.</summary>
        /// <summary>
        /// Orthographic, tipped a touch off vertical and swung to the lower right:
        /// no perspective, so a settled die shows the same face and the same two
        /// slivers of side wherever on the table it stops.
        /// </summary>
        private const float CameraPitchDegrees = 78f;
        private const float CameraYawDegrees = -35f;
        private const float CameraDistance = 12f;
        private const float CameraOrthographicHalfHeight = 3.9f;
        private const int MaxRenderWidthPx = 1920;
        private const float HoldSeconds = 0.9f;
        private const float FadeSeconds = 0.3f;
        private const float ShadowSize = 1.6f;
        private const float DieCornerRadius = 0.13f;
        private const int DieFaceSegments = 14;
        private static readonly Vector4 KeyLightDirection = new Vector4(-0.45f, 0.8f, -0.4f, 0f);
        private static readonly Vector4 FillLightDirection = new Vector4(0.7f, 0.1f, -0.3f, 0f);

        public System.Action OnFinished;

        /// <summary>
        /// True while an animation plays or rolls are queued; modals wait on this.
        /// A PARKED die is deliberately not busy — the response panel must be able to
        /// open beside it.
        /// </summary>
        public bool IsBusy => (_phase != Phase.Idle && _phase != Phase.Parked) || _queue.Count > 0;

        /// <summary>Set while a roll is still open for responses: the die stays put.</summary>
        public bool Linger;

        /// <summary>
        /// Set while the roll's earnings are still landing on the table: the die and
        /// its result line stay up, since the money on screen is this number's doing.
        /// </summary>
        public bool KeepForEarnings;

        /// <summary>The table message while a roll plays ("KIM ROLLS FOR SALES..."), null otherwise.</summary>
        public string Headline { get; private set; }
        /// <summary>Bumps with every headline change, so the revision-gated banner knows to re-render.</summary>
        public int HeadlineRevision { get; private set; }

        private readonly Queue<Roll> _queue = new Queue<Roll>();
        private readonly RectTransform _view;
        private readonly RawImage _viewImage;
        private readonly CanvasGroup _group;
        private readonly Transform _rig;
        private readonly Camera _camera;
        private readonly Transform _die;
        private readonly Transform _shadow;
        private readonly Mesh _shadowMesh;
        private readonly Color32[] _shadowColors = new Color32[4];
        private readonly DieThrowSimulator _simulator = new DieThrowSimulator();

        private RenderTexture _renderTexture;
        private Phase _phase = Phase.Idle;
        private float _phaseElapsed;
        private float _speed = 1f;
        private Roll _current;
        private DieThrowRecording _recording;
        private string _landedHeadline;
        /// <summary>Applied after the recorded rotation so the engine's number is the face on top.</summary>
        private Quaternion _labelRotation = Quaternion.identity;

        public DiceRoller(RectTransform canvasRoot)
        {
            // Lives far below the origin on its own layer; only the die camera sees it.
            _rig = new GameObject("DieRig").transform;
            _rig.position = new Vector3(0, -500f, 0);

            var cameraGo = new GameObject("DieCamera", typeof(Camera));
            cameraGo.transform.SetParent(_rig, false);
            var cameraRotation = Quaternion.Euler(CameraPitchDegrees, CameraYawDegrees, 0f);
            cameraGo.transform.localRotation = cameraRotation;
            cameraGo.transform.localPosition = -(cameraRotation * Vector3.forward) * CameraDistance;
            _camera = cameraGo.GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = CameraOrthographicHalfHeight;
            _camera.nearClipPlane = 0.5f;
            _camera.farClipPlane = 60f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0, 0, 0, 0);
            _camera.cullingMask = 1 << DieLayer;
            // URP's HDR colour buffer has no alpha channel: it would blit an opaque
            // picture over the table. Plain RGBA keeps the clear colour's zero alpha.
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.enabled = false;
            if (Camera.main != null)
            {
                Camera.main.cullingMask &= ~(1 << DieLayer);
            }

            var dieShader = LoadShader("shaders/DieSatin");
            var shadowShader = LoadShader("shaders/DieUnlit");
            _die = CreateRigMesh("Die", DieMesh.BuildRoundedCube(DieCornerRadius, DieFaceSegments),
                DieMesh.BuildPipAtlas(), dieShader, 2000);
            var dieMaterial = _die.GetComponent<MeshRenderer>().sharedMaterial;
            dieMaterial.SetVector("_KeyDir", KeyLightDirection);
            dieMaterial.SetVector("_FillDir", FillLightDirection);
            _shadowMesh = DieMesh.BuildFloorQuad(ShadowSize);
            _shadow = CreateRigMesh("DieShadow", _shadowMesh, DieMesh.BuildShadowTexture(), shadowShader, 3000);

            // The camera's picture over the whole table; transparent wherever there is no die.
            var viewGo = new GameObject("DieView", typeof(RectTransform), typeof(RawImage));
            viewGo.transform.SetParent(canvasRoot, false);
            _view = (RectTransform)viewGo.transform;
            UiKit.Anchor(_view, Vector2.zero, Vector2.one);
            _viewImage = viewGo.GetComponent<RawImage>();
            _viewImage.raycastTarget = false;
            _group = viewGo.AddComponent<CanvasGroup>();
            UiKit.AddClick(viewGo, Skip);
            viewGo.SetActive(false);
        }

        private static Shader LoadShader(string resourcePath)
        {
            var shader = Resources.Load<Shader>(resourcePath);
            return shader != null ? shader : Shader.Find("Sprites/Default");
        }

        private Transform CreateRigMesh(string name, Mesh mesh, Texture2D texture, Shader shader, int renderQueue)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = DieLayer;
            go.transform.SetParent(_rig, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(shader)
            {
                mainTexture = texture,
                renderQueue = renderQueue,
            };
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            return go.transform;
        }

        // ------------------------------------------------------------ public

        /// <summary>
        /// A fresh die. The title names WHAT the roll is for — a Night Shifts die must never
        /// read like the turn sale, or Out of Stock gets spent on the wrong roll.
        /// </summary>
        public void Enqueue(string rollerName, int value, bool isYou, RollPurpose purpose,
            string standName = null, System.Action onLanded = null)
        {
            string title = RollTitle(rollerName, isYou, purpose);
            if (purpose == RollPurpose.TradeWinds && standName != null)
            {
                // Stand-scoped roll: say WHICH stand, or it reads like a table-wide sale.
                title = $"{title.TrimEnd('.')} — {standName.ToUpperInvariant()} ONLY";
            }
            EnqueueRoll(title, value, false, onLanded);
        }

        private static string RollTitle(string rollerName, bool isYou, RollPurpose purpose)
        {
            string name = rollerName.ToUpperInvariant();
            switch (purpose)
            {
                case RollPurpose.NightShifts:
                    return isYou
                        ? "YOU ROLL FOR NIGHT SHIFTS..."
                        : $"{name} ROLLS FOR NIGHT SHIFTS...";
                case RollPurpose.SpoiledRotten:
                    return isYou
                        ? "YOUR SPOILED ROTTEN ROLL..."
                        : $"{name}'S SPOILED ROTTEN ROLL...";
                case RollPurpose.ExtraSale:
                    // Liquid Energy: everyone sells, so whose hand threw it matters least.
                    return "AN EXTRA SALE ROLL...";
                case RollPurpose.TradeWinds:
                    return isYou
                        ? "YOUR TRADE WINDS ROLL..."
                        : $"{name}'S TRADE WINDS ROLL...";
                default:
                    return isYou ? "YOU ROLL FOR SALES..." : $"{name} ROLLS FOR SALES...";
            }
        }

        /// <summary>Out of Stock forced a reroll: show the die landing on its new value.</summary>
        public void EnqueueReroll(string rollerName, int value, bool isYou, System.Action onLanded = null)
        {
            EnqueueRoll(
                isYou
                    ? "YOU PLAY OUT OF STOCK — REROLLING..."
                    : $"{rollerName.ToUpperInvariant()} PLAYS OUT OF STOCK — REROLLING...",
                value, false, onLanded);
        }

        /// <summary>
        /// A card modified the roll (Take Two, Sugared Up, Downsell): the settled die
        /// hops up and lands on the new face instead of being thrown from scratch.
        /// The owner's name says WHOSE card just changed the number.
        /// </summary>
        public void EnqueueModifier(string ownerName, string sourceName, int newValue, bool isYou,
            System.Action onLanded = null)
        {
            string owner = isYou ? "YOUR" : $"{ownerName.ToUpperInvariant()}'S";
            EnqueueRoll($"{owner} {sourceName.ToUpperInvariant()} CHANGES THE ROLL...", newValue, true, onLanded);
        }

        private void EnqueueRoll(string title, int value, bool flip, System.Action onLanded)
        {
            _queue.Enqueue(new Roll
            {
                Title = title,
                Value = Mathf.Clamp(value, 1, 6),
                Flip = flip,
                OnLanded = onLanded,
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
            _recording = null;
            _view.gameObject.SetActive(false);
            _camera.enabled = false;
            SetHeadline(null);
        }

        /// <summary>Advance the animation; call every frame while in game.</summary>
        public void Tick()
        {
            if (_phase == Phase.Idle)
            {
                return;
            }
            _phaseElapsed += Time.deltaTime * _speed;

            switch (_phase)
            {
                case Phase.Rolling:
                    PlaceDie(_phaseElapsed);
                    if (_phaseElapsed >= _recording.DurationSeconds)
                    {
                        Land();
                    }
                    break;
                case Phase.Hold:
                    if (_phaseElapsed >= HoldSeconds)
                    {
                        // A roll still open for responses, or still paying out, parks
                        // on screen instead of fading: the number is what it all rests on.
                        if (Stays && _queue.Count == 0)
                        {
                            SetPhase(Phase.Parked);
                            // The full-screen view must stop catching clicks: a parked
                            // die sits under a live response strip whose Pass button
                            // it would otherwise swallow.
                            _viewImage.raycastTarget = false;
                            SetHeadline(ParkedHeadline);
                            // Parking ends the BUSY period, so it must wake the
                            // renderer exactly like a fade-out does — renders are
                            // revision-gated and would otherwise never re-run.
                            OnFinished?.Invoke();
                        }
                        else
                        {
                            BeginFadeOut();
                        }
                    }
                    break;
                case Phase.Parked:
                    SetHeadline(ParkedHeadline);
                    if (!Stays || _queue.Count > 0)
                    {
                        BeginFadeOut();
                    }
                    break;
                case Phase.FadeOut:
                    _group.alpha = 1f - Mathf.Clamp01(_phaseElapsed / FadeSeconds);
                    if (_phaseElapsed >= FadeSeconds)
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
        }

        // --------------------------------------------------------- animation

        private bool Stays => Linger || KeepForEarnings;

        /// <summary>The result stays in words while the payout plays; a response window needs the strip for itself.</summary>
        private string ParkedHeadline => KeepForEarnings && !Linger ? _landedHeadline : null;

        private void BeginNext()
        {
            _current = _queue.Dequeue();
            // Rolls stacking up (bot round in full swing): play faster so we keep pace.
            _speed = _queue.Count >= 3 ? 2.4f : _queue.Count >= 1 ? 1.6f : 1f;
            EnsureRenderTarget();
            SetHeadline(_current.Title);
            _group.alpha = 1f;
            _view.gameObject.SetActive(true);
            _viewImage.raycastTarget = true;
            _camera.enabled = true;

            if (_current.Flip && _recording != null)
            {
                RecordHop();
            }
            else
            {
                _recording = _simulator.RecordThrow();
                _labelRotation = LabelShowing(_current.Value, _recording.FinalRotation);
            }
            PlaceDie(0f);
            SetPhase(Phase.Rolling);
        }

        /// <summary>
        /// A hop keeps the labels the die already wears, so the simulator retries until
        /// physics lands the wanted face up. Should it give up, relabel after all — a
        /// one-frame jump of the pips beats showing the wrong number.
        /// </summary>
        private void RecordHop()
        {
            int wanted = _current.Value;
            var label = _labelRotation;
            _recording = _simulator.RecordHop(_recording.FinalPosition, _recording.FinalRotation,
                rotation => DieMesh.ValueUp(rotation * label) == wanted);
            if (DieMesh.ValueUp(_recording.FinalRotation * _labelRotation) != wanted)
            {
                _labelRotation = LabelShowing(wanted, _recording.FinalRotation);
            }
        }

        /// <summary>
        /// The local rotation that turns the face physics left on top into the face for
        /// <paramref name="value"/>; a random quarter-turn about it varies the pip spin.
        /// </summary>
        private static Quaternion LabelShowing(int value, Quaternion restingRotation)
        {
            var upFace = DieMesh.FaceNormals[DieMesh.UpFaceIndex(restingRotation)];
            var wantedFace = DieMesh.NormalOf(value);
            return Quaternion.FromToRotation(wantedFace, upFace) *
                   Quaternion.AngleAxis(90f * Random.Range(0, 4), wantedFace);
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseElapsed = 0f;
        }

        private void SetHeadline(string headline)
        {
            if (Headline == headline)
            {
                return;
            }
            Headline = headline;
            HeadlineRevision++;
        }

        private void BeginFadeOut()
        {
            _viewImage.raycastTarget = false;
            SetHeadline(null);
            SetPhase(Phase.FadeOut);
        }

        private void PlaceDie(float seconds)
        {
            _recording.Sample(seconds, out var position, out var rotation);
            _die.localPosition = position;
            _die.localRotation = rotation * _labelRotation;

            // The contact shadow spreads and thins as the die lifts off the floor.
            float height = Mathf.Max(0f, position.y - 0.5f);
            _shadow.localPosition = new Vector3(position.x, 0.01f, position.z);
            _shadow.localScale = Vector3.one * (1f + 0.15f * height);
            byte alpha = (byte)(255f * 0.6f / (1f + 0.5f * height));
            for (int i = 0; i < 4; i++)
            {
                _shadowColors[i] = new Color32(255, 255, 255, alpha);
            }
            _shadowMesh.colors32 = _shadowColors;
        }

        private void Land()
        {
            PlaceDie(_recording.DurationSeconds);
            _landedHeadline = $"{_current.Title.TrimEnd('.')}: {_current.Value}!";
            SetHeadline(_landedHeadline);
            _current.OnLanded?.Invoke();
            SetPhase(Phase.Hold);
        }

        /// <summary>Click-to-skip: land immediately, or end the hold early.</summary>
        private void Skip()
        {
            switch (_phase)
            {
                case Phase.Rolling:
                    Land();
                    break;
                case Phase.Hold:
                    BeginFadeOut();
                    break;
            }
        }

        // ---------------------------------------------------------- rendering

        /// <summary>
        /// The picture matches the screen (capped for cost); the arena the die may
        /// roll in is whatever floor that picture shows, so it follows along.
        /// </summary>
        private void EnsureRenderTarget()
        {
            int width = Mathf.Min(Screen.width, MaxRenderWidthPx);
            int height = Mathf.Max(1, Mathf.RoundToInt(width * (Screen.height / (float)Screen.width)));
            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height)
            {
                return;
            }
            if (_renderTexture != null)
            {
                _renderTexture.Release();
            }
            _renderTexture = new RenderTexture(width, height, 16) { antiAliasing = 4 };
            _camera.targetTexture = _renderTexture;
            _camera.aspect = width / (float)height;
            _viewImage.texture = _renderTexture;
            _simulator.SetArena(VisibleFloorCorners());
        }

        /// <summary>Where the screen's four corners land on the table, in rig-local xz.</summary>
        private Vector2[] VisibleFloorCorners()
        {
            var viewportCorners = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            var corners = new Vector2[4];
            float floorY = _rig.position.y;
            for (int i = 0; i < 4; i++)
            {
                var ray = _camera.ViewportPointToRay(viewportCorners[i]);
                float distance = (floorY - ray.origin.y) / ray.direction.y;
                var hit = _rig.InverseTransformPoint(ray.GetPoint(distance));
                corners[i] = new Vector2(hit.x, hit.z);
            }
            return corners;
        }
    }
}
