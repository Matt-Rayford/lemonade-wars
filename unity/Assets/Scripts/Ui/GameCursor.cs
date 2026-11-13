using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The game's hand cursor, as a real system cursor (drawn by the OS, so it never trails the mouse):
    /// pointing at rest, pressed while the button is down, open over anything that can be picked up.
    /// The art is upright and large; each pose is turned and shrunk once at start-up into the small
    /// texture a system cursor needs. If the art is missing the normal cursor is left alone.
    /// </summary>
    public sealed class GameCursor
    {
        /// <summary>Square, and no bigger: some platforms refuse or software-draw larger system cursors.</summary>
        private const int CursorSizePx = 64;
        private const int CursorMarginPx = 1;
        /// <summary>Counter-clockwise, so the finger leans up-left like an ordinary pointer.</summary>
        private const float TiltDegrees = 30f;
        /// <summary>Samples per side averaged into each cursor pixel; the art shrinks about sixfold.</summary>
        private const int SamplesPerSide = 6;

        // Each pose's fingertip in its upright art, as fractions from the top-left: where the cursor points.
        private static readonly Vector2 RestingFingertip = new Vector2(0.48f, 0.017f);
        private static readonly Vector2 PressedFingertip = new Vector2(0.41f, 0.014f);
        private static readonly Vector2 OpenHandFingertips = new Vector2(0.39f, 0.095f);

        private readonly System.Func<bool> _isCarryingCard;
        private readonly Pose _resting;
        private readonly Pose _pressed;
        private readonly Pose _openHand;
        private readonly List<RaycastResult> _underPointer = new List<RaycastResult>();
        private PointerEventData _pointer;
        private Pose _shown;
        private bool _pressBeganOnDraggable;

        private sealed class Pose
        {
            public Texture2D Texture;
            /// <summary>From the texture's top-left, as Cursor.SetCursor wants it.</summary>
            public Vector2 Hotspot;
        }

        /// <param name="isCarryingCard">A card is stuck to the pointer without the button held (a picked-up shelf card).</param>
        public GameCursor(System.Func<bool> isCarryingCard)
        {
            _isCarryingCard = isCarryingCard;
            _resting = BuildPose("cursor-resting.png", RestingFingertip);
            _pressed = BuildPose("cursor-click.png", PressedFingertip);
            _openHand = BuildPose("cursor-drag.png", OpenHandFingertips);
        }

        /// <summary>Call every frame, on every screen.</summary>
        public void Tick()
        {
            if (_resting == null || _pressed == null || _openHand == null)
            {
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                _pressBeganOnDraggable = PointerInvitesDrag();
            }
            Pose wanted;
            if (Input.GetMouseButton(0))
            {
                // Something draggable keeps the open hand from press to release, click or drag alike.
                wanted = _pressBeganOnDraggable ? _openHand : _pressed;
            }
            else if (_isCarryingCard?.Invoke() == true || PointerInvitesDrag())
            {
                wanted = _openHand;
            }
            else
            {
                wanted = _resting;
            }
            if (wanted != _shown)
            {
                _shown = wanted;
                Cursor.SetCursor(wanted.Texture, wanted.Hotspot, CursorMode.Auto);
            }
        }

        /// <summary>Only the topmost thing under the pointer counts: a card behind an open modal cannot be picked up.</summary>
        private bool PointerInvitesDrag()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }
            if (_pointer == null)
            {
                _pointer = new PointerEventData(eventSystem);
            }
            _pointer.position = Input.mousePosition;
            _underPointer.Clear();
            eventSystem.RaycastAll(_pointer, _underPointer);
            if (_underPointer.Count == 0)
            {
                return false;
            }
            var cue = _underPointer[0].gameObject.GetComponentInParent<IDragCue>();
            return cue != null && cue.InvitesDrag;
        }

        private static Pose BuildPose(string fileName, Vector2 fingertipFromTopLeft)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "images", "cursor", fileName);
            if (!File.Exists(path))
            {
                return null;
            }
            var art = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            art.LoadImage(File.ReadAllBytes(path));
            var pose = TiltAndShrink(art, fingertipFromTopLeft);
            Object.Destroy(art);
            return pose;
        }

        /// <summary>
        /// Turns the upright art about its centre and scales it to fit the cursor square. Works in
        /// Unity's pixel order (rows bottom to top), where a counter-clockwise turn is the usual one.
        /// </summary>
        private static Pose TiltAndShrink(Texture2D art, Vector2 fingertipFromTopLeft)
        {
            int artWidth = art.width;
            int artHeight = art.height;
            var artPixels = art.GetPixels32();
            float radians = TiltDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            float turnedWidth = artWidth * cos + artHeight * sin;
            float turnedHeight = artWidth * sin + artHeight * cos;
            float scale = (CursorSizePx - 2 * CursorMarginPx) / Mathf.Max(turnedWidth, turnedHeight);
            float half = CursorSizePx / 2f;

            var cursorPixels = new Color32[CursorSizePx * CursorSizePx];
            for (int y = 0; y < CursorSizePx; y++)
            {
                for (int x = 0; x < CursorSizePx; x++)
                {
                    cursorPixels[y * CursorSizePx + x] =
                        AverageOfArtUnder(x, y, artPixels, artWidth, artHeight, cos, sin, scale, half);
                }
            }

            // System cursors need a readable RGBA32 texture without mipmaps.
            var texture = new Texture2D(CursorSizePx, CursorSizePx, TextureFormat.RGBA32, false);
            texture.SetPixels32(cursorPixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            float tipX = fingertipFromTopLeft.x * artWidth - artWidth / 2f;
            float tipY = (1f - fingertipFromTopLeft.y) * artHeight - artHeight / 2f;
            float tipInCursorX = half + scale * (tipX * cos - tipY * sin);
            float tipInCursorY = half + scale * (tipX * sin + tipY * cos);
            return new Pose
            {
                Texture = texture,
                Hotspot = new Vector2(tipInCursorX, CursorSizePx - tipInCursorY),
            };
        }

        /// <summary>
        /// One cursor pixel: the art it covers, averaged with colours weighted by their opacity so
        /// the dark outline does not pick up a fringe from transparent neighbours.
        /// </summary>
        private static Color32 AverageOfArtUnder(int x, int y, Color32[] artPixels, int artWidth, int artHeight,
            float cos, float sin, float scale, float half)
        {
            float red = 0f, green = 0f, blue = 0f, opacity = 0f;
            for (int j = 0; j < SamplesPerSide; j++)
            {
                for (int i = 0; i < SamplesPerSide; i++)
                {
                    float fromCentreX = (x + (i + 0.5f) / SamplesPerSide - half) / scale;
                    float fromCentreY = (y + (j + 0.5f) / SamplesPerSide - half) / scale;
                    // The turn undone: where this sample sits in the upright art.
                    int artX = Mathf.FloorToInt(artWidth / 2f + fromCentreX * cos + fromCentreY * sin);
                    int artY = Mathf.FloorToInt(artHeight / 2f - fromCentreX * sin + fromCentreY * cos);
                    if (artX < 0 || artX >= artWidth || artY < 0 || artY >= artHeight)
                    {
                        continue;
                    }
                    var sample = artPixels[artY * artWidth + artX];
                    red += sample.r * sample.a;
                    green += sample.g * sample.a;
                    blue += sample.b * sample.a;
                    opacity += sample.a;
                }
            }
            if (opacity <= 0f)
            {
                return new Color32(0, 0, 0, 0);
            }
            return new Color32(
                (byte)Mathf.RoundToInt(red / opacity),
                (byte)Mathf.RoundToInt(green / opacity),
                (byte)Mathf.RoundToInt(blue / opacity),
                (byte)Mathf.RoundToInt(opacity / (SamplesPerSide * SamplesPerSide)));
        }
    }
}
