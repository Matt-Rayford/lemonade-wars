using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Shared HTML-modal backdrop: captures the rendered frame, blurs it by bouncing
    /// through shrinking render textures, darkens it, then reveals the overlay root.
    /// Construct FIRST inside an overlay so the backdrop sits behind its content.
    /// </summary>
    public sealed class ModalBackdrop
    {
        private static readonly Color Tint = new Color(0.42f, 0.42f, 0.48f, 1f);
        private static readonly Color Fallback = new Color(0.05f, 0.05f, 0.08f, 0.92f);

        private readonly RawImage _image;
        private readonly MonoBehaviour _host;
        private RenderTexture _blur;
        private int _token;

        public ModalBackdrop(RectTransform overlayRoot, MonoBehaviour host)
        {
            _host = host;

            var backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(RawImage));
            backdropGo.transform.SetParent(overlayRoot, false);
            UiKit.Anchor((RectTransform)backdropGo.transform, Vector2.zero, Vector2.one);
            _image = backdropGo.GetComponent<RawImage>();

            var dim = UiKit.CreatePanel(overlayRoot, "Dim", new Color(0, 0, 0, 0.35f));
            UiKit.Anchor(dim, Vector2.zero, Vector2.one);
        }

        /// <summary>Capture + blur the current frame, then activate the overlay root.</summary>
        public void Reveal(GameObject overlayRoot)
        {
            _host.StartCoroutine(Run(++_token, overlayRoot));
        }

        public void Hide()
        {
            _token++; // cancels any in-flight capture
            Release();
        }

        private IEnumerator Run(int token, GameObject overlayRoot)
        {
            yield return new WaitForEndOfFrame(); // let the current frame finish rendering
            if (token != _token)
            {
                yield break; // closed (or reopened) while waiting
            }

            // The capture/blur is decoration; the ACTIVATION below is load-bearing.
            // Owners mark themselves open the moment Reveal is called, so a capture
            // hiccup (editor Game-view resizes love to break CaptureScreenshot) must
            // degrade to a plain dark backdrop — never to an invisible-open modal
            // that soft-locks the whole table behind it.
            try
            {
                Release();
                var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                _blur = BlurDownsample(screenshot);
                Object.Destroy(screenshot);
                if (_blur != null)
                {
                    _image.texture = _blur;
                    _image.color = Tint;
                }
                else
                {
                    _image.texture = null;
                    _image.color = Fallback;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                _image.texture = null;
                _image.color = Fallback;
            }
            overlayRoot.SetActive(true);
        }

        /// <summary>Cheap strong blur: bounce down to 1/16 resolution and back up once.</summary>
        private static RenderTexture BlurDownsample(Texture2D source)
        {
            if (source == null || source.width < 32)
            {
                return null;
            }
            int w = source.width;
            int h = source.height;
            var quarter = RenderTexture.GetTemporary(w / 4, h / 4);
            var eighth = RenderTexture.GetTemporary(w / 8, h / 8);
            var sixteenth = RenderTexture.GetTemporary(w / 16, h / 16);
            Graphics.Blit(source, quarter);
            Graphics.Blit(quarter, eighth);
            Graphics.Blit(eighth, sixteenth);
            Graphics.Blit(sixteenth, eighth); // back up: softens the blockiness
            RenderTexture.ReleaseTemporary(quarter);
            RenderTexture.ReleaseTemporary(sixteenth);
            return eighth; // released via Hide()
        }

        private void Release()
        {
            if (_blur != null)
            {
                _image.texture = null;
                RenderTexture.ReleaseTemporary(_blur);
                _blur = null;
            }
        }
    }
}
