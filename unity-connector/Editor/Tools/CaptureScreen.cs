using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Capture the current Unity screen as a base64-encoded PNG.", Group = "editor")]
    public static class CaptureScreen
    {
        public class Parameters
        {
            [ToolParameter("Target view to capture: 'game' (default) or 'scene'")]
            public string View { get; set; }

            [ToolParameter("Capture width in pixels (defaults to camera resolution)")]
            public int Width { get; set; }

            [ToolParameter("Capture height in pixels (defaults to camera resolution)")]
            public int Height { get; set; }

            [ToolParameter("Name of a specific camera to use (defaults to Camera.main)")]
            public string CameraName { get; set; }
        }

        public static object HandleCommand(JObject raw)
        {
            var p = new ToolParams(raw);
            string view = p.Get("view", "game").ToLowerInvariant();
            int requestedWidth = p.GetInt("width") ?? 0;
            int requestedHeight = p.GetInt("height") ?? 0;
            string cameraName = p.Get("camera_name");

            Camera camera;
            if (view == "scene")
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    return new ErrorResponse("No active Scene View found.");
                camera = sceneView.camera;
            }
            else
            {
                camera = FindCamera(cameraName);
                if (camera == null)
                {
                    string detail = string.IsNullOrEmpty(cameraName)
                        ? "No camera available (Camera.main is null and no cameras exist)."
                        : $"Camera '{cameraName}' not found.";
                    return new ErrorResponse(detail);
                }
            }

            int width = requestedWidth > 0 ? requestedWidth : camera.pixelWidth;
            int height = requestedHeight > 0 ? requestedHeight : camera.pixelHeight;

            if (width <= 0 || height <= 0)
                return new ErrorResponse($"Invalid capture dimensions: {width}x{height}.");

            RenderTexture rt = null;
            Texture2D tex = null;
            RenderTexture prevTarget = camera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] pngBytes = tex.EncodeToPNG();
                string base64 = Convert.ToBase64String(pngBytes);

                return new SuccessResponse("Screen captured.", new
                {
                    base64,
                    width,
                    height,
                    format = "png"
                });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Capture failed: {ex.Message}");
            }
            finally
            {
                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }

        private static Camera FindCamera(string cameraName)
        {
            if (!string.IsNullOrEmpty(cameraName))
            {
                var go = GameObject.Find(cameraName);
                if (go != null)
                {
                    var cam = go.GetComponent<Camera>();
                    if (cam != null) return cam;
                }

                foreach (var cam in Camera.allCameras)
                {
                    if (cam.name == cameraName)
                        return cam;
                }

                return null;
            }

            if (Camera.main != null)
                return Camera.main;

            return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
        }
    }
}
