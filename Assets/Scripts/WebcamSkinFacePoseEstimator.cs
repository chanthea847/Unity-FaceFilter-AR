using UnityEngine;

/// <summary>
/// Cheap Editor-only face pose hint from webcam colour: centroid, bounding box scale, ellipse roll,
/// and pseudo yaw/pitch from centroid offset. Not ML-quality; complements AR Face on devices.
/// </summary>
public sealed class WebcamSkinFacePoseEstimator
{
    Texture2D _cpuTex;

    public struct Sample
    {
        public bool Valid;
        /// <summary>0–1 horizontally (left→right in downscaled image before mirror remap).</summary>
        public float CenterXN;
        public float CenterYN;
        /// <summary>BBox horizontal midpoint → better horizontal “between eyes” than centroid.</summary>
        public float BboxCenterXN;
        /// <summary>BBox vertical midpoint (image space).</summary>
        public float BboxCenterYN;
        /// <summary>BBox height divided by tracking texture height.</summary>
        public float BboxNormH;
        /// <summary>BBox width divided by tracking texture width.</summary>
        public float BboxNormW;
        public float RollDeg;
        public float YawDeg;
        public float PitchDeg;
        public int SkinPixelCount;
    }

    public void Release()
    {
        if (_cpuTex != null)
        {
            Object.Destroy(_cpuTex);
            _cpuTex = null;
        }
    }

    static bool IsSkinRgb(Color32 c)
    {
        int r = c.r, g = c.g, b = c.b;
        return r > 95 && g > 40 && b > 20 && r > g && r > b && Mathf.Abs(r - g) > 15;
    }

    /// <summary>
    /// Downscales webcam to CPU, thresholds skin-tone, derives pose hints.
    /// </summary>
    public bool TryEstimate(
        WebCamTexture web,
        int trackWidth,
        bool horizontalMirrorSelfieStyle,
        int minSkinPixels,
        float roiMarginX,
        float roiMarginY,
        float maxYawDegrees,
        float maxPitchDegrees,
        float rollGain,
        out Sample sample)
    {
        sample = default;
        if (web == null || !web.isPlaying || web.width <= 16)
            return false;

        int tw = Mathf.Max(32, trackWidth);
        int th = Mathf.Max(24, (int)(tw * (float)web.height / web.width));

        RenderTexture tempRt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(web, tempRt);
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = tempRt;

        if (_cpuTex == null || _cpuTex.width != tw || _cpuTex.height != th)
        {
            if (_cpuTex != null)
                Object.Destroy(_cpuTex);
            _cpuTex = new Texture2D(tw, th, TextureFormat.RGBA32, false, false);
        }

        _cpuTex.ReadPixels(new Rect(0, 0, tw, th), 0, 0, false);
        _cpuTex.Apply(false, false);
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(tempRt);

        Color32[] pixels = _cpuTex.GetPixels32();
        float xMinR = tw * Mathf.Clamp01(roiMarginX);
        float xMaxR = tw * Mathf.Clamp(1f - roiMarginX, xMinR + 4f, tw);
        float yMinR = th * Mathf.Clamp01(roiMarginY);
        float yMaxR = th * Mathf.Clamp(1f - roiMarginY, yMinR + 4f, th);

        long sx = 0, sy = 0;
        int count = 0;
        int xmin = int.MaxValue, xmax = int.MinValue;
        int ymin = int.MaxValue, ymax = int.MinValue;

        int idx = 0;
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++, idx++)
            {
                if (x < xMinR || x > xMaxR || y < yMinR || y > yMaxR)
                    continue;
                Color32 px = pixels[idx];
                if (!IsSkinRgb(px))
                    continue;

                sx += x;
                sy += y;
                count++;
                if (x < xmin) xmin = x;
                if (x > xmax) xmax = x;
                if (y < ymin) ymin = y;
                if (y > ymax) ymax = y;
            }
        }

        if (count < minSkinPixels)
            return false;

        float mx = sx / (float)count;
        float my = sy / (float)count;

        double cxx = 0, cyy = 0, cxy = 0;
        idx = 0;
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++, idx++)
            {
                if (x < xMinR || x > xMaxR || y < yMinR || y > yMaxR)
                    continue;
                Color32 px = pixels[idx];
                if (!IsSkinRgb(px))
                    continue;
                float dx = x - mx;
                float dy = y - my;
                cxx += dx * dx;
                cyy += dy * dy;
                cxy += dx * dy;
            }
        }

        float rollDeg = Mathf.Atan2((float)(2 * cxy), Mathf.Max((float)(cxx - cyy), 1e-6f)) * 0.5f * Mathf.Rad2Deg * rollGain;
        rollDeg = Mathf.Clamp(rollDeg, -60f, 60f);

        float cxNorm = Mathf.Clamp01(mx / Mathf.Max(tw - 1, 1));
        float cyNorm = Mathf.Clamp01(my / Mathf.Max(th - 1, 1));
        float cxYaw = horizontalMirrorSelfieStyle ? (1f - cxNorm) : cxNorm;

        float yawDeg = Mathf.Clamp((cxYaw - 0.5f) * 2f * maxYawDegrees, -maxYawDegrees, maxYawDegrees);
        float pitchDeg = Mathf.Clamp((0.5f - cyNorm) * 2f * maxPitchDegrees, -maxPitchDegrees, maxPitchDegrees);

        float bw = xmax >= xmin ? (xmax - xmin + 1f) / tw : 0.1f;
        float bh = ymax >= ymin ? (ymax - ymin + 1f) / th : 0.1f;
        float bxMid = Mathf.Clamp01(((xmin + xmax + 1f) * 0.5f) / tw);
        float byMid = Mathf.Clamp01(((ymin + ymax + 1f) * 0.5f) / th);

        sample = new Sample
        {
            Valid = true,
            CenterXN = cxNorm,
            CenterYN = cyNorm,
            BboxCenterXN = bxMid,
            BboxCenterYN = byMid,
            BboxNormH = bh,
            BboxNormW = bw,
            RollDeg = rollDeg,
            YawDeg = yawDeg,
            PitchDeg = pitchDeg,
            SkinPixelCount = count
        };
        return true;
    }
}
