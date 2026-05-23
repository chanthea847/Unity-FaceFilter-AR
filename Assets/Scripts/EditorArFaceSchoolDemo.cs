using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Windows Editor: webcam backdrop + stand-in mesh + props. Optional skin-blob tracking from
/// <see cref="WebCamTexture"/> moves the demo root (approx centroid, bbox-scale distance, ellipse roll + pseudo yaw/pitch).
/// Device AR face meshes outclass this; use there with <see cref="ARFaceManager"/> enabled instead.
/// </summary>
[DefaultExecutionOrder(-32000)]
public sealed class EditorArFaceSchoolDemo : MonoBehaviour
{
    const string PropsAttachChildName = "Props_Attach_Point";

    enum GeneratedFaceFilter
    {
        None,
        Glasses,
        Crown,
        Hat,
        PartyHat,
        Mask,
        Heart,
        Headphones,
        Star,
        CyberpunkVisor,
        LiquidMercuryMask
    }

    struct FilterChoice
    {
        public GameObject Prefab;
        public GeneratedFaceFilter Generated;
        public bool IsGenerated;

        public static FilterChoice FromPrefab(GameObject prefab)
        {
            return new FilterChoice { Prefab = prefab };
        }

        public static FilterChoice FromGenerated(GeneratedFaceFilter generated)
        {
            return new FilterChoice { Generated = generated, IsGenerated = true };
        }
    }

    static readonly GeneratedFaceFilter[] BuiltInGeneratedFilters =
    {
        GeneratedFaceFilter.Glasses,
        GeneratedFaceFilter.Hat,
        GeneratedFaceFilter.PartyHat,
        GeneratedFaceFilter.Crown,
        GeneratedFaceFilter.Mask,
        GeneratedFaceFilter.Heart,
        GeneratedFaceFilter.Headphones,
        GeneratedFaceFilter.Star,
        GeneratedFaceFilter.CyberpunkVisor,
        GeneratedFaceFilter.LiquidMercuryMask
    };

    [SerializeField]
    Transform demoRoot;

    [SerializeField]
    GameObject arDefaultFacePrefab;

    [SerializeField]
    [Tooltip("Legacy single prop; used only when Filter Prefabs is empty.")]
    GameObject propsCrazyEyesPrefab;

    [SerializeField]
    GameObject[] filterPrefabs;

    [SerializeField]
    [Tooltip("Runtime-generated filters appended after Filter Prefabs. Used for quick editor filters without imported models.")]
    GeneratedFaceFilter[] generatedFilters =
    {
        GeneratedFaceFilter.Glasses,
        GeneratedFaceFilter.Hat,
        GeneratedFaceFilter.PartyHat,
        GeneratedFaceFilter.Crown,
        GeneratedFaceFilter.Mask,
        GeneratedFaceFilter.Heart,
        GeneratedFaceFilter.Headphones,
        GeneratedFaceFilter.Star,
        GeneratedFaceFilter.CyberpunkVisor,
        GeneratedFaceFilter.LiquidMercuryMask
    };

    [SerializeField]
    [Tooltip("When false, a scene with Filter Prefabs uses only those prefabs. Generated Filters become the fallback set.")]
    bool appendGeneratedFiltersAfterPrefabs;

    [SerializeField]
    [Tooltip("Creates a fullscreen overlay with a Next Filter button while in Editor Play mode.")]
    bool autoCreateFilterSwitchUi = true;

    [SerializeField]
    string nextFilterButtonLabel = "Next Filter";

    [SerializeField]
    Camera mainCameraOverride;

    [SerializeField]
    EditorVideoBackdrop videoBackdropSource;

    [Header("Stand-in placement (camera-forward baseline)")]
    [SerializeField]
    float editorPlaneAlongCameraForward = -1f;

    [SerializeField]
    float forwardBiasInFrontOfBackdrop = 0.08f;

    [SerializeField]
    Vector3 fallbackWorldPosition = new Vector3(0f, 0f, 8f);

    [SerializeField]
    Quaternion fallbackWorldRotation = Quaternion.identity;

    [Header("Face mesh scale (initial fit)")]
    [SerializeField]
    bool autoFitFaceMeshToViewport = true;

    [SerializeField]
    [Range(0.15f, 0.95f)]
    float viewportHeightFractionForStandInFace = 0.42f;

    [SerializeField]
    float faceStandInScaleMultiplier = 1f;

    [SerializeField]
    Vector3 manualFaceLocalScale = Vector3.one;

    [Header("Props")]
    [SerializeField]
    bool propsAttachToMainCamera;

    [SerializeField]
    Vector3 propsHudLocalPosition = new Vector3(0f, 0.06f, 0.42f);

    [SerializeField]
    Vector3 propsHudLocalEuler = new Vector3(0f, 180f, 0f);

    [SerializeField]
    float propsHudUniformScale = 1.4f;

    [Header("Glasses / eye alignment (webcam → plane)")]
    [SerializeField]
    [Tooltip("Skin centroid sits around cheeks; shift aim upward toward eye-line (multiplied by face bbox height in 0–1 image space).")]
    float eyeLineShiftUpBBoxFraction = 0.165f;

    [SerializeField]
    [Tooltip("Horizontal shift × bbox width (+ = right in unmirrored webcam image).")]
    float eyeLineShiftRightBBoxFraction = 0f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("Blend centroid X toward bbox midpoint so glasses span both eyes symmetrically.")]
    float interpupillaryHorizontalBlend = 0.42f;

    [SerializeField]
    [Tooltip("Slides the rig slightly toward the camera along the anchor→camera ray (sit forward on skull).")]
    float eyeTowardCameraMeters = 0.028f;

    [Header("Editor webcam pose (skin blob)")]
    [SerializeField]
    bool useWebcamFacePoseTracking = true;

    [SerializeField]
    WebcamToRenderTexture webcamForPoseTracking;

    [SerializeField]
    int trackingProcessWidth = 240;

    [SerializeField]
    [Tooltip("Run GPU→CPU skin pass every N frames.")]
    int processEveryNFrames = 3;

    [SerializeField]
    float smoothResponseHz = 10f;

    [SerializeField]
    float lateralPlaneGain = 1f;

    [SerializeField]
    float verticalPlaneGain = 1f;

    [SerializeField]
    float roiMarginX = 0.08f;

    [SerializeField]
    float roiMarginY = 0.06f;

    [SerializeField]
    int minSkinPixels = 350;

    [SerializeField]
    bool horizontalMirrorSelfieStyle = true;

    [SerializeField]
    float yawMaxDegreesFromCenter = 40f;

    [SerializeField]
    float pitchMaxDegreesFromCenter = 32f;

    [SerializeField]
    [Range(0f, 2f)]
    float rollEllipseGain = 0.45f;

    [Header("Head rotation — smoothing & clamps")]
    [SerializeField]
    [Range(0f, 1.5f)]
    [Tooltip("<1 dampens centroid-based pitch/yaw so filters do not snap.")]
    float headPitchYawScale = 0.5f;

    [SerializeField]
    [Range(0f, 1.5f)]
    float headRollScale = 0.3f;

    [SerializeField]
    float appliedHeadYawClampDegrees = 20f;

    [SerializeField]
    float appliedHeadPitchClampDegrees = 16f;

    [SerializeField]
    float appliedHeadRollClampDegrees = 18f;

    [SerializeField]
    [Tooltip("Lower = smoother head tilt, higher latency.")]
    float headEulerSmoothHz = 5f;

    [SerializeField]
    [Tooltip("Extra low-pass after Euler (reduces jitter from skin blob ellipse).")]
    float headRotationSlerpHz = 10f;

    [SerializeField]
    float depthBBoxGain = 0.45f;

    [SerializeField]
    float maxDepthBBoxShift = 0.42f;

    [SerializeField]
    float planeDistanceSmoothHz = 12f;

    [SerializeField]
    float smoothScaleMultiplierHz = 8f;

    [SerializeField]
    [Range(0.01f, 0.35f)]
    float normHeightRefAdaptiveRate = 0.055f;

    [SerializeField]
    int lostFramesBeforeCenterOnly = 28;

    float _resolvedPlaneAlongCameraForward = 8f;
    Transform _spawnedFace;
    Transform _propsAttachAnchor;
    Camera _setupCamera;
    GameObject _activeFilterInstance;
    int _filterIndex;
    GameObject _filterSwitchUiRoot;

    float _baselineFaceUniformScale = 1f;
    bool _setupComplete;

    readonly WebcamSkinFacePoseEstimator _poseEstimator = new WebcamSkinFacePoseEstimator();

    WebcamSkinFacePoseEstimator.Sample _lastSample;

    Vector3 _lateralSmooth;
    Vector3 _lateralVelSm;
    Vector3 _eulerSmooth;
    Vector3 _eulerVelSm;
    float _planeSmooth;
    float _planeVelSm;
    float _scaleMulSmooth = 1f;
    float _scaleMulVelSm;
    float _normBBoxHeightReference = -1f;
    int _lostPoseFrames;

    void Awake()
    {
        if (!Application.isEditor)
            return;

        EnsureEditorEventSystem();

        foreach (ARFaceManager m in FindObjectsByType<ARFaceManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            m.enabled = false;
        foreach (ARCameraBackground b in FindObjectsByType<ARCameraBackground>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            b.enabled = false;
        foreach (ARSession s in FindObjectsByType<ARSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            s.enabled = false;

        if (demoRoot == null)
            demoRoot = new GameObject("Editor Face Demo Root").transform;

        _lastSample = default;
    }

    void OnDestroy()
    {
        _poseEstimator.Release();
        if (_filterSwitchUiRoot != null)
            Destroy(_filterSwitchUiRoot);
    }

    void Start()
    {
        if (!Application.isEditor)
            return;
        StartCoroutine(EditorDemoSetup());
    }

    void LateUpdate()
    {
        if (!Application.isEditor || !_setupComplete || demoRoot == null)
            return;

        Camera cam = ActiveCamera();
        if (cam == null)
            return;

        WebcamToRenderTexture binder = webcamForPoseTracking != null ? webcamForPoseTracking : ResolveWebCamSource();

        bool canTrack =
            useWebcamFacePoseTracking &&
            !propsAttachToMainCamera &&
            _spawnedFace != null &&
            binder != null &&
            binder.ActiveWebCam != null &&
            binder.ActiveWebCam.isPlaying &&
            binder.ActiveWebCam.width > 16;

        if (!canTrack)
        {
            AlignBillboard(cam);
            return;
        }

        WebCamTexture wc = binder.ActiveWebCam;
        bool doHeavy = Time.frameCount % Mathf.Max(processEveryNFrames, 1) == 0;

        if (doHeavy)
        {
            WebcamSkinFacePoseEstimator.Sample measured;
            bool freshOk =
                _poseEstimator.TryEstimate(
                    wc,
                    trackingProcessWidth,
                    horizontalMirrorSelfieStyle,
                    minSkinPixels,
                    roiMarginX,
                    roiMarginY,
                    yawMaxDegreesFromCenter,
                    pitchMaxDegreesFromCenter,
                    rollEllipseGain,
                    out measured) && measured.Valid;
            if (freshOk)
            {
                _lastSample = measured;
                _lostPoseFrames = 0;
            }
            else if (_lastSample.Valid)
            {
                _lostPoseFrames++;
            }
        }

        if (_lostPoseFrames > lostFramesBeforeCenterOnly)
        {
            _normBBoxHeightReference = -1f;
            _lastSample = default;
            _lostPoseFrames = 0;
            AlignBillboard(cam);
            return;
        }

        if (!_lastSample.Valid)
        {
            AlignBillboard(cam);
            return;
        }

        WebcamSkinFacePoseEstimator.Sample src = _lastSample;

        float capH = Mathf.Max(src.BboxNormH, 0.034f);
        float capW = Mathf.Max(src.BboxNormW, 0.034f);

        // Horizontal: centroid often drifts sideways; blend toward bbox midpoint for both eyes.
        float cxBlend = Mathf.Lerp(src.CenterXN, src.BboxCenterXN, interpupillaryHorizontalBlend);
        cxBlend = Mathf.Clamp01(cxBlend + eyeLineShiftRightBBoxFraction * capW);

        // Vertical: eyes sit above skin centroid → shift aim upward in normalized image space.
        float cyEye = Mathf.Clamp01(src.CenterYN - eyeLineShiftUpBBoxFraction * capH);

        float nearPlane = cam.nearClipPlane + 0.06f;
        float farPlane = Mathf.Max(cam.farClipPlane - 0.1f, nearPlane + 0.25f);

        if (_normBBoxHeightReference < 0f)
            _normBBoxHeightReference = src.BboxNormH;
        _normBBoxHeightReference = Mathf.Lerp(_normBBoxHeightReference, Mathf.Clamp01(src.BboxNormH), normHeightRefAdaptiveRate);

        float depthShift = Mathf.Clamp(
            (_normBBoxHeightReference - src.BboxNormH) * depthBBoxGain,
            -maxDepthBBoxShift, maxDepthBBoxShift);

        float planeTarget = Mathf.Clamp(_resolvedPlaneAlongCameraForward + depthShift, nearPlane, farPlane);

        float cxM = horizontalMirrorSelfieStyle ? (1f - cxBlend) : cxBlend;
        float nx = (cxM - 0.5f) * 2f * lateralPlaneGain;
        float ny = -(cyEye - 0.5f) * 2f * verticalPlaneGain;

        float halfVH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * planeTarget;
        float halfHW = halfVH * cam.aspect;

        Vector3 lateralTarget =
            cam.transform.right * (nx * halfHW) +
            cam.transform.up * (ny * halfVH);

        Vector3 eulerRaw = new Vector3(
            src.PitchDeg * headPitchYawScale,
            src.YawDeg * headPitchYawScale,
            src.RollDeg * headRollScale);
        eulerRaw.x = Mathf.Clamp(eulerRaw.x, -appliedHeadPitchClampDegrees, appliedHeadPitchClampDegrees);
        eulerRaw.y = Mathf.Clamp(eulerRaw.y, -appliedHeadYawClampDegrees, appliedHeadYawClampDegrees);
        eulerRaw.z = Mathf.Clamp(eulerRaw.z, -appliedHeadRollClampDegrees, appliedHeadRollClampDegrees);

        float refH = Mathf.Max(_normBBoxHeightReference, 0.05f);
        float scaleTarget = Mathf.Clamp(refH / Mathf.Max(src.BboxNormH, 0.038f), 0.52f, 2.58f);

        float latSmoothSec = Mathf.Max(0.02f, 1f / Mathf.Max(smoothResponseHz, 2f));
        float eulerSmoothSec = Mathf.Max(0.04f, 1f / Mathf.Max(headEulerSmoothHz, 0.5f));

        _lateralSmooth = Vector3.SmoothDamp(_lateralSmooth, lateralTarget, ref _lateralVelSm, latSmoothSec);
        _eulerSmooth = Vector3.SmoothDamp(_eulerSmooth, eulerRaw, ref _eulerVelSm, eulerSmoothSec);

        _planeSmooth = Mathf.SmoothDamp(_planeSmooth, planeTarget, ref _planeVelSm,
            Mathf.Max(0.02f, 1f / Mathf.Max(planeDistanceSmoothHz, 4f)));

        _scaleMulSmooth = Mathf.SmoothDamp(_scaleMulSmooth, scaleTarget, ref _scaleMulVelSm,
            Mathf.Max(0.02f, 1f / Mathf.Max(smoothScaleMultiplierHz, 2f)));

        Vector3 anchor =
            cam.transform.position +
            cam.transform.forward * _planeSmooth +
            _lateralSmooth;

        if (eyeTowardCameraMeters > 1e-5f)
        {
            Vector3 toCam = cam.transform.position - anchor;
            float m = toCam.magnitude;
            if (m > 1e-4f)
                anchor += (toCam / m) * eyeTowardCameraMeters;
        }

        Quaternion billboard = Quaternion.LookRotation(cam.transform.position - anchor, cam.transform.up);
        Quaternion oriented = billboard * Quaternion.Euler(_eulerSmooth.x, _eulerSmooth.y, _eulerSmooth.z);

        float rotT = 1f - Mathf.Exp(-Mathf.Max(0.01f, headRotationSlerpHz) * Mathf.Min(Time.deltaTime, 0.05f));
        demoRoot.SetPositionAndRotation(anchor, Quaternion.Slerp(demoRoot.rotation, oriented, Mathf.Clamp01(rotT)));

        demoRoot.localScale = Vector3.one;

        _spawnedFace.localScale = Vector3.one * (_baselineFaceUniformScale * _scaleMulSmooth);
    }

    void AlignBillboard(Camera cam)
    {
        PlaneAlignDemoRoot(cam, _resolvedPlaneAlongCameraForward);
        _planeSmooth = _resolvedPlaneAlongCameraForward;

        _lateralSmooth = Vector3.SmoothDamp(_lateralSmooth, Vector3.zero, ref _lateralVelSm, 0.1f);
        _eulerSmooth = Vector3.SmoothDamp(_eulerSmooth, Vector3.zero, ref _eulerVelSm, 0.15f);
        _scaleMulSmooth = Mathf.SmoothDamp(_scaleMulSmooth, 1f, ref _scaleMulVelSm, 0.12f);

        if (_spawnedFace != null)
            _spawnedFace.localScale = Vector3.one * (_baselineFaceUniformScale * _scaleMulSmooth);
    }

    Camera ActiveCamera() => mainCameraOverride != null ? mainCameraOverride : Camera.main;

    IEnumerator EditorDemoSetup()
    {
        Transform root = demoRoot;
        if (root == null)
            yield break;

        yield return null;

        Camera cam = ActiveCamera();

        EditorVideoBackdrop backdrop = videoBackdropSource;
        if (backdrop == null)
        {
            EditorVideoBackdrop[] backs = FindObjectsByType<EditorVideoBackdrop>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (backs != null && backs.Length > 0)
                backdrop = backs[0];
        }

        float planeDist = ResolvePlaneAlongCamera(cam, backdrop);
        _resolvedPlaneAlongCameraForward = planeDist;
        _planeSmooth = planeDist;

        if (cam != null)
            PlaneAlignDemoRoot(cam, planeDist);
        else
            root.SetPositionAndRotation(fallbackWorldPosition, fallbackWorldRotation);

        if (arDefaultFacePrefab == null)
        {
            Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Assign AR Default Face prefab.", this);
            yield break;
        }

        FilterChoice[] effectiveFilters = EffectiveFilterChoices();
        if (effectiveFilters.Length == 0)
        {
            Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Add entries to Filter Prefabs, Generated Filters, or assign Props Crazy Eyes prefab as fallback.", this);
            yield break;
        }

        for (var i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        yield return null;

        GameObject face = Instantiate(arDefaultFacePrefab, root);
        face.name = "AR Default Face";
        face.transform.localPosition = Vector3.zero;
        face.transform.localRotation = Quaternion.identity;
        face.transform.localScale = Vector3.one;

        bool useAutoFit = cam != null && autoFitFaceMeshToViewport;
        if (!useAutoFit)
            face.transform.localScale = manualFaceLocalScale;

        DisableArFoundationBehaviours(face);

        if (useAutoFit)
        {
            PlaneAlignDemoRoot(cam, planeDist);
            ApplyFaceStandInViewportScale(cam, face, planeDist);
        }

        _spawnedFace = face.transform;
        _baselineFaceUniformScale = _spawnedFace.localScale.x;
        _setupCamera = cam;

        Transform propsAttach = face.transform.Find(PropsAttachChildName);
        if (propsAttach == null)
            propsAttach = face.transform;
        _propsAttachAnchor = propsAttach;

        _filterIndex = 0;
        SpawnActiveFilter(effectiveFilters);

        if (autoCreateFilterSwitchUi && effectiveFilters.Length > 1)
            BuildEditorFilterSwitcherUi();

        _setupComplete = true;
    }

    /// <summary>Editor/UI: advances to next filter prefab.</summary>
    public void CycleToNextFilter()
    {
        if (!Application.isEditor || !_setupComplete)
            return;

        FilterChoice[] list = EffectiveFilterChoices();
        if (list.Length <= 1)
            return;

        _filterIndex = (_filterIndex + 1) % list.Length;

        SpawnActiveFilter(list);
    }

    FilterChoice[] EffectiveFilterChoices()
    {
        var choices = new List<FilterChoice>();

        if (filterPrefabs != null && filterPrefabs.Length > 0)
        {
            foreach (GameObject go in filterPrefabs)
            {
                if (go == null)
                    continue;
                choices.Add(FilterChoice.FromPrefab(go));
            }
        }

        if (choices.Count == 0 && propsCrazyEyesPrefab != null)
            choices.Add(FilterChoice.FromPrefab(propsCrazyEyesPrefab));

        AddGeneratedChoices(choices, BuiltInGeneratedFilters);
        AddGeneratedChoices(choices, generatedFilters);

        return choices.Count > 0 ? choices.ToArray() : Array.Empty<FilterChoice>();
    }

    static void AddGeneratedChoices(List<FilterChoice> choices, GeneratedFaceFilter[] filters)
    {
        if (filters == null)
            return;

        foreach (GeneratedFaceFilter filter in filters)
        {
            if (filter == GeneratedFaceFilter.None || HasGeneratedChoice(choices, filter))
                continue;
            choices.Add(FilterChoice.FromGenerated(filter));
        }
    }

    static bool HasGeneratedChoice(List<FilterChoice> choices, GeneratedFaceFilter filter)
    {
        foreach (FilterChoice choice in choices)
            if (choice.IsGenerated && choice.Generated == filter)
                return true;
        return false;
    }

    void SpawnActiveFilter(FilterChoice[] list)
    {
        if (_activeFilterInstance != null)
        {
            Destroy(_activeFilterInstance);
            _activeFilterInstance = null;
        }

        if (_propsAttachAnchor == null || list == null || list.Length == 0)
            return;

        FilterChoice choice = list[Mathf.Clamp(_filterIndex, 0, list.Length - 1)];
        if (!choice.IsGenerated && choice.Prefab == null)
            return;

        GameObject spawned = choice.IsGenerated
            ? CreateGeneratedFilter(choice.Generated)
            : Instantiate(choice.Prefab, _propsAttachAnchor);
        if (choice.IsGenerated)
            spawned.transform.SetParent(_propsAttachAnchor, false);

        spawned.name = choice.IsGenerated ? choice.Generated + " Filter" : choice.Prefab.name + " (clone)";
        _activeFilterInstance = spawned;

        ConvertPropsToBuiltInPipeline(spawned);
        ForcePropsVisible(spawned);

        foreach (Animator animator in spawned.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;

        if (propsAttachToMainCamera && _setupCamera != null)
        {
            spawned.transform.SetParent(_setupCamera.transform, false);
            spawned.transform.localPosition = propsHudLocalPosition;
            spawned.transform.localRotation = Quaternion.Euler(propsHudLocalEuler);
            spawned.transform.localScale = Vector3.one * propsHudUniformScale;
        }
        else
        {
            spawned.transform.localPosition = Vector3.zero;
            spawned.transform.localRotation = Quaternion.identity;
            spawned.transform.localScale = Vector3.one;
        }
    }

    static GameObject CreateGeneratedFilter(GeneratedFaceFilter filter)
    {
        GameObject root = new GameObject("Generated " + filter);

        Material black = CreateFilterMaterial("Filter Black", new Color(0.02f, 0.02f, 0.025f, 1f));
        Material gold = CreateFilterMaterial("Filter Gold", new Color(1f, 0.72f, 0.16f, 1f));
        Material blue = CreateFilterMaterial("Filter Blue", new Color(0.1f, 0.44f, 0.88f, 1f));
        Material darkBlue = CreateFilterMaterial("Filter Dark Blue", new Color(0.02f, 0.08f, 0.18f, 1f));
        Material maskBlue = CreateFilterMaterial("Filter Mask Blue", new Color(0.15f, 0.78f, 0.95f, 1f));
        Material jewel = CreateFilterMaterial("Filter Jewel", new Color(0.95f, 0.08f, 0.22f, 1f));
        Material red = CreateFilterMaterial("Filter Red", new Color(1f, 0.12f, 0.24f, 1f));
        Material purple = CreateFilterMaterial("Filter Purple", new Color(0.52f, 0.18f, 0.86f, 1f));
        Material white = CreateFilterMaterial("Filter White", new Color(0.95f, 0.95f, 0.98f, 1f));
        Material neonCyan = CreateFilterMaterial("Filter Neon Cyan", new Color(0f, 0.95f, 1f, 1f));
        Material neonMagenta = CreateFilterMaterial("Filter Neon Magenta", new Color(1f, 0.05f, 0.85f, 1f));
        Material hologramGlass = CreateTransparentFilterMaterial("Filter Hologram Glass", new Color(0.08f, 0.9f, 1f, 0.42f));
        Material mercury = CreateMetallicFilterMaterial("Filter Liquid Mercury", new Color(0.78f, 0.82f, 0.86f, 1f), 1f, 0.08f);
        Material darkChrome = CreateMetallicFilterMaterial("Filter Dark Chrome", new Color(0.22f, 0.24f, 0.28f, 1f), 1f, 0.04f);

        switch (filter)
        {
            case GeneratedFaceFilter.Glasses:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Left Lens", new Vector3(-0.035f, 0.018f, -0.018f), Vector3.zero, new Vector3(0.045f, 0.032f, 0.006f), black);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Right Lens", new Vector3(0.035f, 0.018f, -0.018f), Vector3.zero, new Vector3(0.045f, 0.032f, 0.006f), black);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Bridge", new Vector3(0f, 0.018f, -0.018f), Vector3.zero, new Vector3(0.028f, 0.008f, 0.007f), black);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Left Arm", new Vector3(-0.074f, 0.018f, -0.004f), new Vector3(0f, 22f, 0f), new Vector3(0.05f, 0.006f, 0.006f), black);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Right Arm", new Vector3(0.074f, 0.018f, -0.004f), new Vector3(0f, -22f, 0f), new Vector3(0.05f, 0.006f, 0.006f), black);
                break;

            case GeneratedFaceFilter.Crown:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Crown Band", new Vector3(0f, 0.104f, -0.012f), Vector3.zero, new Vector3(0.155f, 0.024f, 0.035f), gold);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Left Point", new Vector3(-0.052f, 0.142f, -0.012f), Vector3.zero, new Vector3(0.022f, 0.064f, 0.025f), gold);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Center Point", new Vector3(0f, 0.154f, -0.012f), Vector3.zero, new Vector3(0.024f, 0.09f, 0.025f), gold);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Right Point", new Vector3(0.052f, 0.142f, -0.012f), Vector3.zero, new Vector3(0.022f, 0.064f, 0.025f), gold);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Left Jewel", new Vector3(-0.052f, 0.18f, -0.012f), Vector3.zero, Vector3.one * 0.018f, jewel);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Center Jewel", new Vector3(0f, 0.205f, -0.012f), Vector3.zero, Vector3.one * 0.021f, jewel);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Right Jewel", new Vector3(0.052f, 0.18f, -0.012f), Vector3.zero, Vector3.one * 0.018f, jewel);
                break;

            case GeneratedFaceFilter.Hat:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Hat Brim", new Vector3(0f, 0.103f, -0.01f), Vector3.zero, new Vector3(0.12f, 0.012f, 0.075f), darkBlue);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Hat Top", new Vector3(0f, 0.152f, -0.01f), Vector3.zero, new Vector3(0.071f, 0.052f, 0.071f), blue);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Hat Band", new Vector3(0f, 0.124f, -0.01f), Vector3.zero, new Vector3(0.074f, 0.008f, 0.074f), black);
                break;

            case GeneratedFaceFilter.PartyHat:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Party Hat Cone", new Vector3(0f, 0.145f, -0.012f), Vector3.zero, new Vector3(0.055f, 0.105f, 0.055f), red);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Party Hat Top", new Vector3(0f, 0.215f, -0.012f), Vector3.zero, Vector3.one * 0.017f, gold);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Party Hat Band", new Vector3(0f, 0.095f, -0.012f), Vector3.zero, new Vector3(0.105f, 0.012f, 0.035f), blue);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Party Hat Stripe", new Vector3(0f, 0.145f, -0.057f), new Vector3(0f, 0f, 25f), new Vector3(0.02f, 0.095f, 0.008f), gold);
                break;

            case GeneratedFaceFilter.Mask:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Mask Front", new Vector3(0f, -0.025f, -0.028f), Vector3.zero, new Vector3(0.14f, 0.055f, 0.012f), maskBlue);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Mask Top Fold", new Vector3(0f, 0.002f, -0.036f), Vector3.zero, new Vector3(0.12f, 0.006f, 0.01f), blue);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Mask Bottom Fold", new Vector3(0f, -0.052f, -0.036f), Vector3.zero, new Vector3(0.12f, 0.006f, 0.01f), blue);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Left Strap", new Vector3(-0.093f, -0.025f, -0.02f), new Vector3(0f, 0f, 18f), new Vector3(0.055f, 0.006f, 0.006f), black);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Right Strap", new Vector3(0.093f, -0.025f, -0.02f), new Vector3(0f, 0f, -18f), new Vector3(0.055f, 0.006f, 0.006f), black);
                break;

            case GeneratedFaceFilter.Heart:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Left Heart Top A", new Vector3(-0.052f, -0.02f, -0.03f), Vector3.zero, Vector3.one * 0.024f, red);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Left Heart Top B", new Vector3(-0.028f, -0.02f, -0.03f), Vector3.zero, Vector3.one * 0.024f, red);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Left Heart Point", new Vector3(-0.04f, -0.044f, -0.03f), new Vector3(0f, 0f, 45f), new Vector3(0.032f, 0.032f, 0.012f), red);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Right Heart Top A", new Vector3(0.028f, -0.02f, -0.03f), Vector3.zero, Vector3.one * 0.024f, red);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Right Heart Top B", new Vector3(0.052f, -0.02f, -0.03f), Vector3.zero, Vector3.one * 0.024f, red);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Right Heart Point", new Vector3(0.04f, -0.044f, -0.03f), new Vector3(0f, 0f, 45f), new Vector3(0.032f, 0.032f, 0.012f), red);
                break;

            case GeneratedFaceFilter.Headphones:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Headphone Band", new Vector3(0f, 0.105f, -0.012f), Vector3.zero, new Vector3(0.17f, 0.014f, 0.014f), purple);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Left Headphone Stem", new Vector3(-0.087f, 0.055f, -0.012f), Vector3.zero, new Vector3(0.014f, 0.09f, 0.014f), purple);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Right Headphone Stem", new Vector3(0.087f, 0.055f, -0.012f), Vector3.zero, new Vector3(0.014f, 0.09f, 0.014f), purple);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Left Ear Cup", new Vector3(-0.092f, 0.012f, -0.012f), Vector3.zero, new Vector3(0.034f, 0.052f, 0.03f), black);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Right Ear Cup", new Vector3(0.092f, 0.012f, -0.012f), Vector3.zero, new Vector3(0.034f, 0.052f, 0.03f), black);
                break;

            case GeneratedFaceFilter.Star:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Star Center", new Vector3(0f, 0.08f, -0.024f), new Vector3(0f, 0f, 45f), new Vector3(0.045f, 0.045f, 0.012f), gold);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Star Vertical", new Vector3(0f, 0.08f, -0.024f), Vector3.zero, new Vector3(0.022f, 0.09f, 0.012f), gold);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Star Horizontal", new Vector3(0f, 0.08f, -0.024f), Vector3.zero, new Vector3(0.09f, 0.022f, 0.012f), gold);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Star Spark Left", new Vector3(-0.07f, 0.045f, -0.024f), Vector3.zero, Vector3.one * 0.014f, white);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Star Spark Right", new Vector3(0.07f, 0.115f, -0.024f), Vector3.zero, Vector3.one * 0.014f, white);
                break;

            case GeneratedFaceFilter.CyberpunkVisor:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Holographic Visor Glass", new Vector3(0f, 0.014f, -0.032f), Vector3.zero, new Vector3(0.145f, 0.045f, 0.006f), hologramGlass);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Visor Top Neon", new Vector3(0f, 0.042f, -0.038f), Vector3.zero, new Vector3(0.158f, 0.006f, 0.009f), neonCyan);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Visor Bottom Neon", new Vector3(0f, -0.014f, -0.038f), Vector3.zero, new Vector3(0.138f, 0.006f, 0.009f), neonMagenta);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Left Temple Neon", new Vector3(-0.083f, 0.014f, -0.025f), new Vector3(0f, 18f, 0f), new Vector3(0.052f, 0.008f, 0.008f), neonCyan);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Right Temple Neon", new Vector3(0.083f, 0.014f, -0.025f), new Vector3(0f, -18f, 0f), new Vector3(0.052f, 0.008f, 0.008f), neonMagenta);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Circuit Line Left", new Vector3(-0.038f, 0.018f, -0.041f), new Vector3(0f, 0f, 32f), new Vector3(0.045f, 0.004f, 0.005f), neonMagenta);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Circuit Line Right", new Vector3(0.038f, 0.01f, -0.041f), new Vector3(0f, 0f, -32f), new Vector3(0.045f, 0.004f, 0.005f), neonCyan);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Circuit Node Left", new Vector3(-0.061f, 0.031f, -0.043f), Vector3.zero, Vector3.one * 0.009f, neonCyan);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Circuit Node Right", new Vector3(0.061f, -0.006f, -0.043f), Vector3.zero, Vector3.one * 0.009f, neonMagenta);
                break;

            case GeneratedFaceFilter.LiquidMercuryMask:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Mercury Brow Left", new Vector3(-0.042f, 0.034f, -0.03f), Vector3.zero, new Vector3(0.055f, 0.024f, 0.018f), mercury);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Mercury Brow Right", new Vector3(0.042f, 0.034f, -0.03f), Vector3.zero, new Vector3(0.055f, 0.024f, 0.018f), mercury);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Molten Nose Bridge", new Vector3(0f, 0.004f, -0.036f), Vector3.zero, new Vector3(0.03f, 0.05f, 0.016f), darkChrome);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Liquid Cheek Left", new Vector3(-0.058f, -0.024f, -0.032f), Vector3.zero, new Vector3(0.04f, 0.052f, 0.018f), mercury);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Liquid Cheek Right", new Vector3(0.058f, -0.024f, -0.032f), Vector3.zero, new Vector3(0.04f, 0.052f, 0.018f), mercury);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Fluid Mouth Sweep", new Vector3(0f, -0.058f, -0.038f), new Vector3(0f, 0f, -6f), new Vector3(0.12f, 0.014f, 0.012f), darkChrome);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Drip Left Long", new Vector3(-0.075f, -0.07f, -0.034f), Vector3.zero, new Vector3(0.014f, 0.045f, 0.012f), mercury);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Drip Left Drop", new Vector3(-0.075f, -0.101f, -0.034f), Vector3.zero, Vector3.one * 0.017f, mercury);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Drip Center", new Vector3(0f, -0.085f, -0.04f), Vector3.zero, new Vector3(0.013f, 0.038f, 0.012f), darkChrome);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Drip Right Short", new Vector3(0.072f, -0.064f, -0.034f), Vector3.zero, new Vector3(0.013f, 0.028f, 0.012f), mercury);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Chrome Splash Left", new Vector3(-0.098f, 0.012f, -0.03f), Vector3.zero, Vector3.one * 0.018f, darkChrome);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Chrome Splash Right", new Vector3(0.098f, 0.014f, -0.03f), Vector3.zero, Vector3.one * 0.018f, darkChrome);
                break;
        }

        return root;
    }

    static Material CreateFilterMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader) { name = name, color = color };
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        material.renderQueue = 2800;
        return material;
    }

    static Material CreateMetallicFilterMaterial(string name, Color color, float metallic, float smoothness)
    {
        Material material = CreateFilterMaterial(name, color);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", smoothness);
        return material;
    }

    static Material CreateTransparentFilterMaterial(string name, Color color)
    {
        Material material = CreateFilterMaterial(name, color);
        if (material.HasProperty("_Mode"))
            material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = 3000;
        return material;
    }

    static void AddPrimitive(Transform parent, PrimitiveType primitive, string name, Vector3 localPosition, Vector3 localEuler, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(primitive);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = localScale;

        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
    }

    static void EnsureEditorEventSystem()
    {
        EventSystem[] existing = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (existing != null && existing.Length > 0)
            return;

        GameObject esGo = new GameObject("Editor EventSystem");
        esGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<StandaloneInputModule>();
    }

    void BuildEditorFilterSwitcherUi()
    {
        if (!Application.isEditor || _filterSwitchUiRoot != null)
            return;

        GameObject canvasGo = new GameObject("Editor Face Filter Switcher UI");
        canvasGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        _filterSwitchUiRoot = canvasGo;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject btnGo = new GameObject("Next Filter Button", typeof(RectTransform));
        btnGo.transform.SetParent(canvasGo.transform, false);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(240f, 60f);
        rt.anchoredPosition = new Vector2(-28f, 28f);

        Image img = btnGo.AddComponent<Image>();
        img.color = new Color(0.22f, 0.53f, 0.96f, 0.93f);

        Button btn = btnGo.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.42f, 0.72f, 1f, 1f);
        cb.pressedColor = new Color(0.15f, 0.4f, 0.82f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(CycleToNextFilter);

        GameObject textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(btnGo.transform, false);

        RectTransform trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        Text label = textGo.AddComponent<Text>();

        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null)
        {
            try
            {
                f = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Helvetica" }, 22);
            }
            catch (Exception)
            {
                /* font optional */
            }
        }

        label.font = f;
        label.text = nextFilterButtonLabel;
        label.alignment = TextAnchor.MiddleCenter;
        label.resizeTextForBestFit = false;
        label.fontSize = 22;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    float ResolvePlaneAlongCamera(Camera cam, EditorVideoBackdrop backdrop)
    {
        float baseBackdrop = backdrop != null ? backdrop.PlaneDistanceAlongCameraForward : 8f;
        float plane = editorPlaneAlongCameraForward >= 0f
            ? editorPlaneAlongCameraForward
            : Mathf.Max(
                cam != null ? cam.nearClipPlane + 0.05f : 0.15f,
                baseBackdrop - forwardBiasInFrontOfBackdrop);
        float maxZ = cam != null ? Mathf.Max(cam.farClipPlane - 0.1f, cam.nearClipPlane + 0.2f) : plane;
        return Mathf.Clamp(plane, cam != null ? cam.nearClipPlane + 0.05f : 0.15f, maxZ);
    }

    void PlaneAlignDemoRoot(Camera cam, float planeAlongCameraForward)
    {
        if (cam == null || demoRoot == null)
            return;

        Vector3 pos = cam.transform.position + cam.transform.forward * planeAlongCameraForward;
        Quaternion rot = Quaternion.LookRotation(cam.transform.position - pos, cam.transform.up);
        demoRoot.SetPositionAndRotation(pos, rot);
        demoRoot.localScale = Vector3.one;
    }

    void ApplyFaceStandInViewportScale(Camera cam, GameObject face, float planeDistance)
    {
        var mf = face.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
            return;

        float planeDist = Mathf.Max(planeDistance, cam.nearClipPlane + 0.02f);
        float viewportHeight = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * planeDist;
        float targetWorldHeight = viewportHeight * viewportHeightFractionForStandInFace;
        Bounds lb = mf.sharedMesh.bounds;
        float meshHeight = Mathf.Max(lb.size.y, 1e-5f);
        float uniform = targetWorldHeight / meshHeight * faceStandInScaleMultiplier;
        face.transform.localScale = Vector3.one * uniform;
    }

    WebcamToRenderTexture ResolveWebCamSource()
    {
        WebcamToRenderTexture[] all = FindObjectsByType<WebcamToRenderTexture>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return all != null && all.Length > 0 ? all[0] : null;
    }

    static void ForcePropsVisible(GameObject propsRoot)
    {
        foreach (Transform t in propsRoot.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = 0;
            t.gameObject.hideFlags = HideFlags.None;
        }

        foreach (Renderer renderer in propsRoot.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    static void ConvertPropsToBuiltInPipeline(GameObject propsRoot)
    {
        Shader unlitTex = Shader.Find("Unlit/Texture");
        Shader unlitColor = Shader.Find("Unlit/Color");
        Shader standard = Shader.Find("Standard");
        const int drawOnTopQueue = 2800;

        foreach (Renderer renderer in propsRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                continue;

            Material[] mats = renderer.materials;
            for (var i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null)
                    continue;

                Texture albedo = null;
                if (m.HasProperty("_BaseMap"))
                    albedo = m.GetTexture("_BaseMap");
                if (albedo == null && m.HasProperty("_MainTex"))
                    albedo = m.GetTexture("_MainTex");

                Color c = Color.white;
                if (m.HasProperty("_BaseColor"))
                    c = m.GetColor("_BaseColor");
                else if (m.HasProperty("_Color"))
                    c = m.GetColor("_Color");

                if (albedo != null && unlitTex != null)
                {
                    m.shader = unlitTex;
                    m.mainTexture = albedo;
                    m.color = c;
                    m.renderQueue = drawOnTopQueue;
                }
                else if (unlitColor != null)
                {
                    m.shader = unlitColor;
                    m.color = c.a < 0.01f ? new Color(1f, 0.35f, 0.85f) : c;
                    m.renderQueue = drawOnTopQueue;
                }
                else if (standard != null)
                {
                    m.shader = standard;
                    m.SetColor("_Color", c);
                    if (albedo != null)
                        m.SetTexture("_MainTex", albedo);
                    m.SetFloat("_Glossiness", 0.4f);
                    m.SetFloat("_Metallic", 0f);
                    m.renderQueue = drawOnTopQueue;
                }
            }

            renderer.materials = mats;
        }
    }

    static void DisableArFoundationBehaviours(GameObject root)
    {
        foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null)
                continue;
            string ns = mb.GetType().Namespace;
            if (ns != null && ns.StartsWith("UnityEngine.XR.ARFoundation"))
                mb.enabled = false;
        }
    }
}
