﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RaycastIndexBufferFeature : ScriptableRendererFeature
{
    public class RaycastIndexPass : ScriptableRenderPass
    {
        private readonly int renderTargetId;
        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>();
        private readonly string profilerTag;

        private RaycastIndexBufferFeature feature;
        private int objectTargetID;
        private int placementTargetID;

        private RenderTexture objectRenderTexture;
        private RenderTexture placementRenderTexture;

        private RenderTargetIdentifier cameraColorTarget;
        private RenderStateBlock renderStateBlock;

        private FilteringSettings objectFilteringSettings;
        private FilteringSettings placementFilteringSettings;

        public RaycastIndexPass(RaycastIndexBufferFeature feature, string profilerTag, int objectTargetID, int placementTargetID,
            LayerMask objectLayerMask, LayerMask placementLayerMask)
        {
            this.feature = feature;
            this.profilerTag = profilerTag;
            this.objectTargetID = objectTargetID;
            this.placementTargetID = placementTargetID;

            objectFilteringSettings = new FilteringSettings(null, objectLayerMask);
            placementFilteringSettings = new FilteringSettings(null, placementLayerMask);

            shaderTagIds.Add(new ShaderTagId("SRPDefaultUnlit"));
            shaderTagIds.Add(new ShaderTagId("UniversalForward"));
            shaderTagIds.Add(new ShaderTagId("UniversalForwardOnly"));
            shaderTagIds.Add(new ShaderTagId("LightweightForward"));

            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

            objectRenderTexture = new RenderTexture(RTHandles.maxWidth, RTHandles.maxHeight, 1, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            objectRenderTexture.name = "Raycast Index Buffer";

            placementRenderTexture = new RenderTexture(objectRenderTexture);
        }

        public void Setup(RenderTargetIdentifier cameraColorTarget)
        {
            this.cameraColorTarget = cameraColorTarget;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            RenderTextureDescriptor blitTargetDescriptor = cameraTextureDescriptor;
            blitTargetDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            cmd.GetTemporaryRT(renderTargetId, blitTargetDescriptor);

            if (objectRenderTexture != null)
            {
                objectRenderTexture.Release();
                placementRenderTexture.Release();
            }

            objectRenderTexture = new RenderTexture(cameraTextureDescriptor.width, cameraTextureDescriptor.height, 1, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            objectRenderTexture.name = "Raycast Index Buffer";

            placementRenderTexture = new RenderTexture(objectRenderTexture);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = SortingCriteria.BackToFront;

            renderingData.postProcessingEnabled = false;
            
            DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortingCriteria);

            CommandBuffer cmd = CommandBufferPool.Get();

            RenderTexture.active = objectRenderTexture;

            RenderIntoRaycastBuffer(in cmd, in objectRenderTexture, in context, in renderingData, ref drawingSettings, ref objectFilteringSettings);

            Shader.SetGlobalTexture(objectTargetID, objectRenderTexture);

            if (feature.showColliders)
            {
                cmd.Blit(objectRenderTexture, cameraColorTarget);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }

            RenderIntoRaycastBuffer(in cmd, in placementRenderTexture, in context, in renderingData, ref drawingSettings, ref placementFilteringSettings);

            Shader.SetGlobalTexture(placementTargetID, placementRenderTexture);

            if (feature.showColliders)
            {
                cmd.Blit(objectRenderTexture, cameraColorTarget);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }

            renderingData.postProcessingEnabled = true;

            CommandBufferPool.Release(cmd);
        }

        private void RenderIntoRaycastBuffer(in CommandBuffer cmd, in RenderTexture target, in ScriptableRenderContext context,
            in RenderingData renderingData, ref DrawingSettings drawingSettings, ref FilteringSettings filteringSettings)
        {
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(true, true, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.Submit();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.SetRenderTarget(cameraColorTarget);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    private const string PassTag = "RenderRaycastIndexBuffer";

    [SerializeField] private LayerMask objectLayerMask;
    [SerializeField] private LayerMask placementLayerMask;
    [SerializeField] private bool showColliders = false;

    private RaycastIndexPass pass;

    public override void Create()
    {
        pass = new RaycastIndexPass(this, PassTag,
            Shader.PropertyToID(Intersections.OBJECT_RAYCAST_INDEX_BUFFER),
            Shader.PropertyToID(Intersections.PLACEMENT_RAYCAST_INDEX_BUFFER),
            objectLayerMask,
            placementLayerMask);

        pass.renderPassEvent = RenderPassEvent.AfterRendering;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(pass);
    }
}
