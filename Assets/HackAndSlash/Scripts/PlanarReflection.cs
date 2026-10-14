using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HackAndSlash
{
    /// <summary>
    /// Mirror camera for the wet roof. Renders the scene reflected about the floor plane into a half-resolution
    /// HDR texture that the VoidArena/WetReflection overlay samples in screen space.
    /// The mirror is built as a proper (non-mirrored) camera, so no culling inversion is needed; the overlay simply
    /// flips the image horizontally. An oblique near plane clips everything below the floor.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(500)]
    public class PlanarReflection : MonoBehaviour
    {
        public float planeHeight = 0f;
        public float clipOffset = 0.03f;
        [Range(0.25f, 1f)] public float resolutionScale = 0.5f;
        public LayerMask reflectLayers = ~0;

        static readonly int ReflectionTexId = Shader.PropertyToID("_ReflectionTex");
        Camera main, mirror;
        RenderTexture rt;

        void OnEnable()
        {
            main = GetComponent<Camera>();
            var go = new GameObject("PlanarReflectionCamera");
            go.hideFlags = HideFlags.DontSave;
            mirror = go.AddComponent<Camera>();
            mirror.enabled = true;
            mirror.depth = main.depth - 10f;
            mirror.clearFlags = CameraClearFlags.Skybox;
            mirror.cullingMask = reflectLayers & ~(1 << Layers.Floor) & ~(1 << Layers.Water) & ~(1 << 5);
            mirror.allowHDR = true;
            mirror.allowMSAA = false;
            var data = mirror.GetUniversalAdditionalCameraData();
            if (data)
            {
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
                data.antialiasing = AntialiasingMode.None;
            }
        }

        void OnDisable()
        {
            if (mirror) Destroy(mirror.gameObject);
            if (rt) { rt.Release(); Destroy(rt); }
            mirror = null;
            rt = null;
        }

        void LateUpdate()
        {
            if (!mirror || !main) return;
            int w = Mathf.Max(64, (int)(main.pixelWidth * resolutionScale));
            int h = Mathf.Max(64, (int)(main.pixelHeight * resolutionScale));
            if (!rt || rt.width != w || rt.height != h)
            {
                if (rt) { rt.Release(); Destroy(rt); }
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "PlanarReflection", useMipMap = true, autoGenerateMips = true, wrapMode = TextureWrapMode.Clamp };
                rt.Create();
                mirror.targetTexture = rt;
                Shader.SetGlobalTexture(ReflectionTexId, rt);
            }

            var t = main.transform;
            Vector3 pos = t.position; pos.y = 2f * planeHeight - pos.y;
            Vector3 fwd = t.forward; fwd.y = -fwd.y;
            Vector3 up = t.up; up.y = -up.y;
            mirror.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));
            mirror.fieldOfView = main.fieldOfView;
            mirror.aspect = main.aspect;
            mirror.nearClipPlane = main.nearClipPlane;
            mirror.farClipPlane = main.farClipPlane;
            mirror.ResetProjectionMatrix();

            // oblique near plane = the floor, so nothing below the roof leaks into the reflection
            Matrix4x4 m = mirror.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(new Vector3(0f, planeHeight + clipOffset, 0f));
            Vector3 cnormal = m.MultiplyVector(Vector3.up).normalized;
            mirror.projectionMatrix = mirror.CalculateObliqueMatrix(new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal)));
            Shader.SetGlobalTexture(ReflectionTexId, rt);
        }
    }
}
