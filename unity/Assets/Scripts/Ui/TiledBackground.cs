using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Repeats a RawImage's texture at a fixed on-screen size, whatever the rect ends
    /// up being. UGUI has no tiling mode for RawImage, so the repeat count lives in
    /// uvRect and has to be recomputed whenever the rect resizes (window resize, aspect
    /// change). Anchored bottom-left so the pattern stays put as the rect grows upward.
    /// </summary>
    public sealed class TiledBackground : MonoBehaviour
    {
        public float TileWidth = 512f;
        public float TileHeight = 512f;

        private RawImage _image;
        private RectTransform _rect;
        private Vector2 _lastSize = new Vector2(-1, -1);

        private void Awake()
        {
            _image = GetComponent<RawImage>();
            _rect = (RectTransform)transform;
        }

        private void Update()
        {
            var size = _rect.rect.size;
            if (size == _lastSize || TileWidth <= 0f || TileHeight <= 0f)
            {
                return;
            }
            _lastSize = size;
            _image.uvRect = new Rect(0f, 0f, size.x / TileWidth, size.y / TileHeight);
        }
    }
}
