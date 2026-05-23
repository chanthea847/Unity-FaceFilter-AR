using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Editor helper: full-screen quad parented to the main camera showing the same <see cref="RenderTexture"/>
/// used for the face material (filled by <see cref="WebcamToRenderTexture"/> or an optional disabled VideoPlayer).
/// </summary>
[DefaultExecutionOrder(-280)]
[DisallowMultipleComponent]
public sealed class EditorVideoBackdrop : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Leave null to use Camera.main.")]
    Camera targetCamera;

    [SerializeField]
    [Tooltip("Distance from the camera along its forward axis.")]
    float backdropDistance = 8f;

    /// <summary>Same distance passed to FitQuad — use when aligning Editor face-filter props with the webcam plane.</summary>
    public float PlaneDistanceAlongCameraForward => backdropDistance;

    GameObject backdropRoot;

    void Awake()
    {
        if (!Application.isEditor)
            return;

        RenderTexture rt = null;
        var webcam = GetComponent<WebcamToRenderTexture>();
        if (webcam != null)
            rt = webcam.TargetTexture;

        if (rt == null)
        {
            var vp = GetComponent<VideoPlayer>();
            if (vp != null)
                rt = vp.targetTexture;
        }

        if (rt == null)
        {
            Debug.LogWarning($"{nameof(EditorVideoBackdrop)}: Add WebcamToRenderTexture or assign a VideoPlayer Target Texture.", this);
            return;
        }

        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning($"{nameof(EditorVideoBackdrop)}: No camera found (assign Target Camera or tag Main Camera).", this);
            return;
        }

        var quadProto = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var mesh = quadProto.GetComponent<MeshFilter>().sharedMesh;
        Destroy(quadProto);

        backdropRoot = new GameObject("Editor Video Backdrop (auto)");
        backdropRoot.transform.SetParent(cam.transform, false);

        var mf = backdropRoot.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = backdropRoot.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("ARFaceFilter/VideoBackdropBackground"));
        if (mat.shader == null || !mat.shader.isSupported)
            mat = new Material(Shader.Find("ARFaceFilter/UnlitVideoTexture"));
        if (mat.shader == null || !mat.shader.isSupported)
            mat = new Material(Shader.Find("Unlit/Texture"));
        mat.mainTexture = rt;
        mr.sharedMaterial = mat;

        FitQuadToCamera(cam, backdropRoot.transform, backdropDistance);
    }

    static void FitQuadToCamera(Camera cam, Transform quad, float distance)
    {
        distance = Mathf.Clamp(distance, cam.nearClipPlane + 0.05f, cam.farClipPlane - 0.05f);
        float height = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
        float width = height * cam.aspect;
        quad.localPosition = new Vector3(0f, 0f, distance);
        quad.localRotation = Quaternion.identity;
        quad.localScale = new Vector3(width, height, 1f);
    }

    void OnDestroy()
    {
        if (backdropRoot != null)
            Destroy(backdropRoot);
    }
}
