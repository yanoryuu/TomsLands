using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 宴（Utage）の描画カメラ（SpriteCamera / UICamera）を、いま画面に出ているシーン側 Base カメラの
/// カメラスタックへ Overlay として積み直す。ScenarioSystem プレハブのルートに ScenarioPlayer が自動で付ける。
///
/// 背景: URP 17（RenderGraph）では「ClearFlags=Nothing（Background=Uninitialized）の Base カメラ」は
/// Color.yellow でクリアされる（Renderer2DRendergraph.GetImportResourceSummary）。
/// 宴の SpriteCamera は Base + Nothing のまま depth 0 で各シーンの Main Camera（depth -1）の後に描画されるため、
/// 画面全体が黄色で塗り潰されていた（Overlay Canvas の UI だけが上に残る）。
/// Base カメラは仕様上必ず背景をクリアするので、上に重ねたいカメラは Overlay にしてスタックに入れるしかない。
/// </summary>
public sealed class UtageCameraStacker : MonoBehaviour
{
    /// <summary>スタックに積む宴カメラ（depth 昇順 = 描画順）。</summary>
    private readonly List<Camera> overlays = new();
    private Camera currentBase;
    private Camera[] cameraBuffer = new Camera[8];

    private void Awake()
    {
        // ClearFlags=Nothing の宴カメラ（SpriteCamera と、そのスタックにいた UICamera）を集める。
        // ClearCamera（SolidColor 黒）は「シーンに Base カメラが無いときの土台」としてそのまま Base で残す。
        foreach (var cam in GetComponentsInChildren<Camera>(true))
        {
            if (cam.clearFlags != CameraClearFlags.Nothing) continue;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) continue;

            if (data.renderType == CameraRenderType.Base)
            {
                foreach (var stacked in data.cameraStack)
                    if (stacked != null && !overlays.Contains(stacked)) overlays.Add(stacked);
                data.cameraStack.Clear();
                data.renderType = CameraRenderType.Overlay;
            }
            if (!overlays.Contains(cam)) overlays.Add(cam);
        }
        overlays.Sort((a, b) => a.depth.CompareTo(b.depth));
    }

    private void LateUpdate()
    {
        var target = FindScreenBaseCamera();
        if (target != currentBase)
        {
            Detach(currentBase);
            currentBase = target;
        }
        if (currentBase != null) EnsureOnTop(currentBase);
    }

    private void OnDestroy()
    {
        Detach(currentBase);
        currentBase = null;
    }

    /// <summary>画面（targetTexture なし）に描く Base カメラのうち、最後に描画されるもの（depth 最大）。</summary>
    private Camera FindScreenBaseCamera()
    {
        int count = Camera.allCamerasCount;
        if (cameraBuffer.Length < count) cameraBuffer = new Camera[count * 2];
        count = Camera.GetAllCameras(cameraBuffer);

        Camera best = null;
        for (int i = 0; i < count; i++)
        {
            var cam = cameraBuffer[i];
            if (cam == null || cam.targetTexture != null || overlays.Contains(cam)) continue;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base) continue;
            if (best == null || cam.depth >= best.depth) best = cam;
        }
        System.Array.Clear(cameraBuffer, 0, count);
        return best;
    }

    /// <summary>宴カメラがスタックの末尾に、この順で並んでいることを保証する（ゲーム側 Overlay より上に出す）。</summary>
    private void EnsureOnTop(Camera baseCamera)
    {
        var stack = baseCamera.GetUniversalAdditionalCameraData().cameraStack;
        int offset = stack.Count - overlays.Count;
        bool ok = offset >= 0;
        for (int i = 0; ok && i < overlays.Count; i++)
            ok = stack[offset + i] == overlays[i];
        if (ok) return;

        foreach (var cam in overlays) stack.Remove(cam);
        stack.AddRange(overlays);
    }

    private void Detach(Camera baseCamera)
    {
        if (baseCamera == null) return;
        var data = baseCamera.GetUniversalAdditionalCameraData();
        if (data == null || data.renderType != CameraRenderType.Base) return;
        foreach (var cam in overlays) data.cameraStack.Remove(cam);
    }
}
