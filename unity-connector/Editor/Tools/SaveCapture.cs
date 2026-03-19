using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Save base64-encoded image data to a file on disk.", Group = "editor")]
    public static class SaveCapture
    {
        public class Parameters
        {
            [ToolParameter("Base64-encoded image data (e.g. from capture_screen)", Required = true)]
            public string Base64 { get; set; }

            [ToolParameter("Output file path (absolute or relative to project root)", Required = true)]
            public string FilePath { get; set; }

            [ToolParameter("Output format: 'png' (default) or 'jpg'")]
            public string Format { get; set; }

            [ToolParameter("JPG quality 1-100 (default 75, ignored for PNG)")]
            public int Quality { get; set; }
        }

        public static object HandleCommand(JObject raw)
        {
            var p = new ToolParams(raw);

            var base64Result = p.GetRequired("base64");
            if (!base64Result.IsSuccess)
                return new ErrorResponse(base64Result.ErrorMessage);

            var filePathResult = p.GetRequired("file_path");
            if (!filePathResult.IsSuccess)
                return new ErrorResponse(filePathResult.ErrorMessage);

            string base64 = base64Result.Value;
            string filePath = filePathResult.Value;
            string format = p.Get("format", "png").ToLowerInvariant();
            int quality = p.GetInt("quality") ?? 75;
            quality = Mathf.Clamp(quality, 1, 100);

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return new ErrorResponse("Invalid base64 data.");
            }

            string absPath = Path.IsPathRooted(filePath)
                ? filePath
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", filePath));

            string dir = Path.GetDirectoryName(absPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            try
            {
                byte[] outputBytes;
                if (format == "jpg" || format == "jpeg")
                {
                    var tex = new Texture2D(2, 2);
                    try
                    {
                        tex.LoadImage(imageBytes);
                        outputBytes = tex.EncodeToJPG(quality);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
                else
                {
                    outputBytes = imageBytes;
                }

                File.WriteAllBytes(absPath, outputBytes);

                return new SuccessResponse("File saved.", new
                {
                    path = absPath,
                    size = outputBytes.Length
                });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to write file: {ex.Message}");
            }
        }
    }
}
