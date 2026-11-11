using System.Collections.Generic;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The sale-roll moment: a real 3D D6 seen by its own camera, composited over the
    /// whole table. Every roll is a physics throw recorded ahead of time
    /// (<see cref="DieThrowSimulator"/>) and relabelled so the face on top is the number
    /// the engine already rolled. Rolls queue and play back-to-back, faster if they
    /// pile up; a click skips ahead.
    /// </summary>
    public sealed partial class DiceRoller
    {
        private struct Roll
        {
            public string Title;
            public int Value;
            /// <summary>A card changed the number: the settled die hops onto the new face instead of being thrown.</summary>
            public bool IsHop;
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

        public System.Action OnFinished;

        /// <summary>
        /// Modals wait on this. A PARKED die is deliberately not busy: the response panel
        /// must be able to open beside it.
        /// </summary>
        public bool IsBusy => (_phase != Phase.Idle && _phase != Phase.Parked) || _queue.Count > 0;

        /// <summary>Set while a roll is still open for responses: the die stays put.</summary>
        public bool Linger;

        /// <summary>Set while the roll's earnings are still landing: the die and its result line stay up.</summary>
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
        private float _playbackSpeed = 1f;
        private Roll _current;
        private DieThrowRecording _recording;
        private string _landedHeadline;
        /// <summary>Applied after the recorded rotation so the engine's number is the face on top.</summary>
        private Quaternion _labelRotation = Quaternion.identity;

        /// <param name="landingCeilingViewportY">
        /// How far up the screen (0 bottom, 1 top) the die may come to REST, so a settled
        /// die never covers the message strip. Throws still fly in across it.
        /// </param>
        public DiceRoller(RectTransform canvasRoot, float landingCeilingViewportY = 1f)
        {
            _landingCeilingViewportY = landingCeilingViewportY;
            // Far below the origin on its own layer; only the die camera sees it.
            _rig = new GameObject("DieRig").transform;
            _rig.position = new Vector3(0, -500f, 0);
            _camera = CreateDieCamera();
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

        /// <summary>
        /// The title names WHAT the roll is for: a Night Shifts die must never read like
        /// the turn sale, or Out of Stock gets spent on the wrong roll.
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

        public void EnqueueReroll(string rollerName, int value, bool isYou, System.Action onLanded = null)
        {
            EnqueueRoll(
                isYou
                    ? "YOU PLAY OUT OF STOCK — REROLLING..."
                    : $"{rollerName.ToUpperInvariant()} PLAYS OUT OF STOCK — REROLLING...",
                value, false, onLanded);
        }

        /// <summary>
        /// A card modified the roll (Take Two, Sugared Up, Downsell): the settled die hops
        /// onto the new face. The owner's name says WHOSE card changed the number.
        /// </summary>
        public void EnqueueModifier(string ownerName, string sourceName, int newValue, bool isYou,
            System.Action onLanded = null)
        {
            string owner = isYou ? "YOUR" : $"{ownerName.ToUpperInvariant()}'S";
            EnqueueRoll($"{owner} {sourceName.ToUpperInvariant()} CHANGES THE ROLL...", newValue, true, onLanded);
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

        private void EnqueueRoll(string title, int value, bool isHop, System.Action onLanded)
        {
            _queue.Enqueue(new Roll
            {
                Title = title,
                Value = Mathf.Clamp(value, 1, 6),
                IsHop = isHop,
                OnLanded = onLanded,
            });
            if (_phase == Phase.Idle)
            {
                BeginNext();
            }
        }
    }
}
