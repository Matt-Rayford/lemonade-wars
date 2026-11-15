using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class DiceRoller
    {
        private const int DieLayer = 31; // unnamed project layer reserved for the die rig
        /// <summary>
        /// Orthographic and tipped a touch off vertical: with no perspective, a settled die
        /// shows the same face and the same two slivers of side wherever it stops.
        /// </summary>
        private const float CameraPitchDegrees = 78f;
        private const float CameraYawDegrees = -35f;
        private const float CameraDistance = 12f;
        private const float CameraOrthographicHalfHeight = 3.9f;
        private const int MaxRenderWidthPx = 1920;
        private const float ShadowSize = 1.6f;
        private const float DieCornerRadius = 0.13f;
        private const int DieFaceSegments = 14;
        private const float DieSizeUnits = 1f;
        private static readonly Vector4 KeyLightDirection = new Vector4(-0.45f, 0.8f, -0.4f, 0f);
        private static readonly Vector4 FillLightDirection = new Vector4(0.7f, 0.1f, -0.3f, 0f);

        private Camera CreateDieCamera()
        {
            var cameraGo = new GameObject("DieCamera", typeof(Camera));
            cameraGo.transform.SetParent(_rig, false);
            var cameraRotation = Quaternion.Euler(CameraPitchDegrees, CameraYawDegrees, 0f);
            cameraGo.transform.localRotation = cameraRotation;
            cameraGo.transform.localPosition = -(cameraRotation * Vector3.forward) * CameraDistance;
            var dieCamera = cameraGo.GetComponent<Camera>();
            dieCamera.orthographic = true;
            dieCamera.orthographicSize = CameraOrthographicHalfHeight;
            dieCamera.nearClipPlane = 0.5f;
            dieCamera.farClipPlane = 60f;
            dieCamera.clearFlags = CameraClearFlags.SolidColor;
            dieCamera.backgroundColor = new Color(0, 0, 0, 0);
            dieCamera.cullingMask = 1 << DieLayer;
            // URP's HDR colour buffer has no alpha channel: it would blit an opaque
            // picture over the table. Plain RGBA keeps the clear colour's zero alpha.
            dieCamera.allowHDR = false;
            dieCamera.allowMSAA = true;
            dieCamera.enabled = false;
            return dieCamera;
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

        /// <summary>
        /// The picture matches the screen (capped for cost), and the arena is whatever floor
        /// that picture shows, so it is rebuilt whenever the picture is.
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
            _simulator.SetArena(FloorCorners());
        }

        /// <summary>Where the screen's corners land on the table, in rig-local xz.</summary>
        private Vector2[] FloorCorners()
        {
            var viewportCorners = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            };
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
