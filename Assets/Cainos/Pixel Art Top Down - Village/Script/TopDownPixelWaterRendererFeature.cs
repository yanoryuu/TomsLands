using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class TopDownPixelWaterRendererFeature : ScriptableRendererFeature
{
    [Space]
    [Tooltip("Layer mask of terrain objects that will interact with water")]
    public LayerMask terrainLayerMask;
    [Tooltip("Layer mask of the water")]
    public LayerMask waterLayerMask;


    [System.Serializable]
    public class Reference
    {
        public Material waterMaterial;
        public Shader blurShader;                                           //actually not used, the blur shader is fetched by name. just to keep a reference here so the blur shader will be included in the final build
    }
    [Tooltip("Reference to water material and blur shader"), Space]
    public Reference reference;


    [System.Serializable]
    public class RefractionSetting
    {
        public float scale = 1.0f;
        public float intensity = 3.0f;
    }
    [Tooltip("Refraction setting")]
    public RefractionSetting refractionSetting = new RefractionSetting();


    [System.Serializable]
    public class ColorSetting
    {
        public Color deepColor              = new Color(0.20f, 0.35f, 0.39f, 1.0f);
        public Color shallowColor           = new Color(0.33f, 0.53f, 0.49f, 1.0f);
        public Color foamColor              = new Color(0.67f, 0.89f, 0.89f, 0.38f);
        public Color highlightColor         = new Color(0.42f, 0.56f, 0.56f, 0.58f);
        public Color underWaterColor        = new Color(0.33f, 0.86f, 0.84f, 0.1f);
    }
    [Tooltip("Water color setting")]
    public ColorSetting colorSetting = new ColorSetting();

    [System.Serializable]
    public class WaterColorBlurSetting
    {
        [Tooltip("Reduces texture resolution before blurring for performance, also affects blur size")]
        public int downsample = 12;

        [Tooltip("Size of blur kernel, affects blur quality")]
        public int kernelSize = 10;

        [Tooltip("Controls blur spread and falloff")]
        public float sigma = 6.0f;

        [Tooltip("Number of blur iterations")]
        public int blurPass = 6;
    }
    [Tooltip("Blur settings for Shallow Color to Deep Color transition")]
    public WaterColorBlurSetting waterColorBlurSetting;


    [System.Serializable]
    public class UnderWaterMaskBlurSetting
    {
        [Tooltip("Reduces texture resolution before blurring for performance, also affects blur size")]
        public int downsample = 3;

        [Tooltip("Size of blur kernel, affects blur quality")]
        public int kernelSize = 8;

        [Tooltip("Controls blur spread and falloff")]
        public float sigma = 6.0f;

        [Tooltip("Number of blur iterations")]
        public int blurPass = 4;
    }
    [Tooltip("Blur settings for masking underwater")]
    public UnderWaterMaskBlurSetting underwaterBlurSetting = new UnderWaterMaskBlurSetting();

    private TopDownPixelWaterRenderPass waterPass;

    public override void Create()
    {
        if (reference.waterMaterial == null)
        {
            Debug.LogError("[Top Down Pixel Water Renderer Feature] Water Material not set.");
            return;
        }

        waterPass = new TopDownPixelWaterRenderPass(
            refractionSetting,
            colorSetting,
            waterColorBlurSetting,
            underwaterBlurSetting,
            terrainLayerMask,
            waterLayerMask,
            reference.waterMaterial,
            CoreUtils.CreateEngineMaterial("Hidden/Cainos/Pixel Art Top Down - Village/Render Pass - Gaussian Blur"));
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (waterPass == null)
            return;

        renderer.EnqueuePass(waterPass);
    }
}

// The custom render pass that uses RenderGraph
public class TopDownPixelWaterRenderPass : ScriptableRenderPass
{
    private TopDownPixelWaterRendererFeature.RefractionSetting refractionSetting;
    private TopDownPixelWaterRendererFeature.ColorSetting colorSetting;
    private TopDownPixelWaterRendererFeature.WaterColorBlurSetting waterColorBlurSetting;
    private TopDownPixelWaterRendererFeature.UnderWaterMaskBlurSetting underwaterBlurSetting;
    private LayerMask terrainLayerMask;
    private LayerMask waterLayerMask;
    private Material waterMaterial;
    private Material blurMaterial;


    private static readonly int kTerrainTexID = Shader.PropertyToID("_TerrainTex");
    private static readonly int kTerrainBlurredID = Shader.PropertyToID("_TerrainBlurredTex");
    private static readonly int kUnderWaterMaskID = Shader.PropertyToID("_UnderWaterMaskTex");
    private static readonly int kKernelSizeID = Shader.PropertyToID("_KernelSize");
    private static readonly int kSigmaID = Shader.PropertyToID("_Sigma");

    public TopDownPixelWaterRenderPass(
        TopDownPixelWaterRendererFeature.RefractionSetting refractionSetting,
        TopDownPixelWaterRendererFeature.ColorSetting colorSetting,
        TopDownPixelWaterRendererFeature.WaterColorBlurSetting colorBlurSetting,
        TopDownPixelWaterRendererFeature.UnderWaterMaskBlurSetting underWarerBlurSetting,
        LayerMask terrainMask,
        LayerMask waterLayerMask,
        Material waterMterial,
        Material blurMaterial)
    {
        this.refractionSetting = refractionSetting;
        this.colorSetting = colorSetting;
        this.waterColorBlurSetting = colorBlurSetting;
        this.underwaterBlurSetting = underWarerBlurSetting;

        this.terrainLayerMask = terrainMask;
        this.waterLayerMask = waterLayerMask;

        this.waterMaterial = waterMterial;
        this.blurMaterial = blurMaterial;

        renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    // Define a data class to hold information for each render-graph pass
    private class PassData
    {
        public Material waterMaterial;
        public RendererListHandle objectsToDraw;

        public TextureHandle terrainTexture;
        public TextureHandle terrainTextureBlur;
        public TextureHandle underWaterMaskTex;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // Fetch camera and resource data
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
        UniversalLightData lightData = frameData.Get<UniversalLightData>();

        var texDesc = cameraData.cameraTargetDescriptor;
        texDesc.depthBufferBits = 0;
        texDesc.colorFormat = RenderTextureFormat.ARGB32;
        var terrainTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, texDesc, "Terrain Tex", false, FilterMode.Point, TextureWrapMode.Clamp);

        texDesc.width = cameraData.cameraTargetDescriptor.width / waterColorBlurSetting.downsample;
        texDesc.height = cameraData.cameraTargetDescriptor.height / waterColorBlurSetting.downsample;
        TextureHandle terrainBlurTemp1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, texDesc, "Terrain Blur Temp1", false, FilterMode.Bilinear, TextureWrapMode.Clamp);
        TextureHandle terrainBlurTemp2 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, texDesc, "Terrain Blur Temp2", false, FilterMode.Bilinear, TextureWrapMode.Clamp);
        TextureHandle terrainBlurredTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, texDesc, "Terrain Blur Tex", false, FilterMode.Bilinear, TextureWrapMode.Clamp);

        texDesc.width = cameraData.cameraTargetDescriptor.width / underwaterBlurSetting.downsample;
        texDesc.height = cameraData.cameraTargetDescriptor.height / underwaterBlurSetting.downsample;
        TextureHandle underWaterBlurTemp1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, texDesc, "Under Water Blur Temp1", false, FilterMode.Bilinear, TextureWrapMode.Clamp);
        TextureHandle underWaterBlurTemp2 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, texDesc, "Under Water Blur Temp2", false, FilterMode.Bilinear, TextureWrapMode.Clamp);
        TextureHandle underWaterMaskTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, texDesc, "Under Water Mask Tex", false, FilterMode.Bilinear, TextureWrapMode.Clamp);

        //TERRAIN PASS: render objects on terrainLayerMask into a texture
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Terrain Render Pass", out var passData))
        {
            passData.terrainTexture = terrainTex;

            // Set up drawing settings and filter for the terrain layer
            var sortingSettings = new SortingSettings(cameraData.camera) { criteria = SortingCriteria.CommonTransparent };
            var drawSettings = RenderingUtils.CreateDrawingSettings(new ShaderTagId("Universal2D"), renderingData, cameraData, lightData, sortingSettings.criteria);
            drawSettings.overrideMaterial = null;
            var filterSettings = new FilteringSettings(RenderQueueRange.all, terrainLayerMask);

            var rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
            passData.objectsToDraw = renderGraph.CreateRendererList(rendererListParams);

            builder.UseRendererList(passData.objectsToDraw);
            builder.SetRenderAttachment(passData.terrainTexture, 0, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.ClearRenderTarget(true, true, Color.clear);
                context.cmd.DrawRendererList(data.objectsToDraw);
            });
        }

        //WATER COLOR BLUR PASS
        {
            blurMaterial.SetFloat("_Sigma", waterColorBlurSetting.sigma);
            blurMaterial.SetFloat("_KernelSize", waterColorBlurSetting.kernelSize);

            RenderGraphUtils.BlitMaterialParameters blitStart_T_1 = new(terrainTex, terrainBlurTemp1, blurMaterial, 0);
            RenderGraphUtils.BlitMaterialParameters blitStart_1_2 = new(terrainBlurTemp1, terrainBlurTemp2, blurMaterial, 1);
            RenderGraphUtils.BlitMaterialParameters blitStart_2_1 = new(terrainBlurTemp2, terrainBlurTemp1, blurMaterial, 0);

            renderGraph.AddBlitPass(blitStart_T_1, "Water Color Blur Pass");
            renderGraph.AddBlitPass(blitStart_1_2, "Water Color Blur Pass");
            for (int i = 1; i < waterColorBlurSetting.blurPass; i++)
            {
                renderGraph.AddBlitPass(blitStart_2_1, "Water Color Blur Pass");
                renderGraph.AddBlitPass(blitStart_1_2, "Water Color Blur Pass");
            }
            renderGraph.AddCopyPass(terrainBlurTemp2, terrainBlurredTex, passName:"Water Color Blur Pass - Final");
        }

        //UNDERWATER BLUR PASS
        {
            blurMaterial.SetFloat("_Sigma", underwaterBlurSetting.sigma);
            blurMaterial.SetFloat("_KernelSize", underwaterBlurSetting.kernelSize);

            RenderGraphUtils.BlitMaterialParameters blitStart_T_1 = new(terrainTex, underWaterBlurTemp1, blurMaterial, 0);
            RenderGraphUtils.BlitMaterialParameters blitStart_1_2 = new(underWaterBlurTemp1, underWaterBlurTemp2, blurMaterial, 1);
            RenderGraphUtils.BlitMaterialParameters blitStart_2_1 = new(underWaterBlurTemp2, underWaterBlurTemp1, blurMaterial, 0);

            renderGraph.AddBlitPass(blitStart_T_1, "Under Water Blur Pass");
            renderGraph.AddBlitPass(blitStart_1_2, "Under Water Blur Pass");
            for (int i = 1; i < underwaterBlurSetting.blurPass; i++)
            {
                renderGraph.AddBlitPass(blitStart_2_1, "Under Water Blur Pass");
                renderGraph.AddBlitPass(blitStart_1_2, "Under Water Blur Pass");
            }
            renderGraph.AddCopyPass(underWaterBlurTemp2, underWaterMaskTex, passName: "Under Water Blur Pass - Final");
        }

        //DRAW WATER PASS:
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Draw Water Pass", out var passData))
        {
            waterMaterial.SetFloat("_CameraOrthoSize", cameraData.camera.orthographicSize);
            waterMaterial.SetFloat("_CameraAspect", cameraData.camera.aspect);
            waterMaterial.SetFloat("_RefractionNoiseScale", refractionSetting.scale);
            waterMaterial.SetFloat("_RefractionIntensity", refractionSetting.intensity);
            waterMaterial.SetColor("_DeepColor", colorSetting.deepColor);
            waterMaterial.SetColor("_ShallowColor", colorSetting.shallowColor);
            waterMaterial.SetColor("_FoamColor", colorSetting.foamColor);
            waterMaterial.SetColor("_HighlightColor", colorSetting.highlightColor);
            waterMaterial.SetColor("_UnderWaterColor", colorSetting.underWaterColor);


            passData.waterMaterial = waterMaterial;
            passData.terrainTexture = terrainTex;
            passData.terrainTextureBlur = terrainBlurredTex;
            passData.underWaterMaskTex = underWaterMaskTex;

            var sortingSettings = new SortingSettings(cameraData.camera) { criteria = SortingCriteria.CommonTransparent };
            var drawSettings = RenderingUtils.CreateDrawingSettings(new ShaderTagId("Universal2D"), renderingData, cameraData, lightData, sortingSettings.criteria);
            drawSettings.overrideMaterial = waterMaterial;
            var filterSettings = new FilteringSettings(RenderQueueRange.all, waterLayerMask);

            var rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
            passData.objectsToDraw = renderGraph.CreateRendererList(rendererListParams);

            builder.UseTexture(passData.terrainTexture);
            builder.UseTexture(passData.terrainTextureBlur);
            builder.UseTexture(passData.underWaterMaskTex);
            builder.UseRendererList(passData.objectsToDraw);
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                passData.waterMaterial.SetTexture(kTerrainTexID, passData.terrainTexture);
                passData.waterMaterial.SetTexture(kTerrainBlurredID, passData.terrainTextureBlur);
                passData.waterMaterial.SetTexture(kUnderWaterMaskID, passData.underWaterMaskTex);

                context.cmd.DrawRendererList(data.objectsToDraw);
            });
        }
    }
}