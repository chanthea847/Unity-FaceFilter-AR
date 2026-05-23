using UnityEngine;

/// <summary>
/// Captures the default (or chosen) laptop webcam and copies it into a <see cref="RenderTexture"/>
/// each frame so existing materials and the editor backdrop quad keep working.
/// </summary>
[DefaultExecutionOrder(-290)]
[DisallowMultipleComponent]
public sealed class WebcamToRenderTexture : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Same RenderTexture used by face material and EditorVideoBackdrop.")]
    RenderTexture targetTexture;

    [SerializeField]
    [Tooltip("Exact name from WebCamTexture.devices; leave empty for the first camera.")]
    string deviceName = "";

    [SerializeField]
    int requestWidth = 1280;

    [SerializeField]
    int requestHeight = 720;

    [SerializeField]
    int requestFps = 30;

    WebCamTexture _cam;

    public RenderTexture TargetTexture => targetTexture;

    /// <summary>For Editor webcam pose estimation (not GPU Blit).</summary>
    public WebCamTexture ActiveWebCam => _cam;

    void OnEnable()
    {
        if (targetTexture == null)
        {
            Debug.LogWarning($"{nameof(WebcamToRenderTexture)}: Assign Target Texture.", this);
            return;
        }

        var devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError($"{nameof(WebcamToRenderTexture)}: No webcam devices found.", this);
            return;
        }

        string name = string.IsNullOrEmpty(deviceName) ? devices[0].name : deviceName;
        if (!string.IsNullOrEmpty(deviceName))
        {
            bool found = false;
            foreach (var d in devices)
            {
                if (d.name == deviceName)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Debug.LogWarning($"{nameof(WebcamToRenderTexture)}: Device '{deviceName}' not found; using '{devices[0].name}'.", this);
                name = devices[0].name;
            }
        }

        _cam = new WebCamTexture(name, requestWidth, requestHeight, requestFps);
        _cam.Play();
    }

    void OnDisable()
    {
        StopCamera();
    }

    void OnDestroy()
    {
        StopCamera();
    }

    void StopCamera()
    {
        if (_cam == null)
            return;
        _cam.Stop();
        Destroy(_cam);
        _cam = null;
    }

    void Update()
    {
        if (_cam == null || targetTexture == null || !_cam.isPlaying)
            return;
        // WebCamTexture reports small dimensions until the device has started.
        if (_cam.width <= 16)
            return;

        Graphics.Blit(_cam, targetTexture);
    }
}
