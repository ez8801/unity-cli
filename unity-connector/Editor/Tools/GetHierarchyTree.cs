using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Return the Unity hierarchy as a Markdown tree using HierarchyMdTree.Serialize.", Group = "editor")]
    public static class GetHierarchyTree
    {
        public class Parameters
        {
            [ToolParameter("Prefab asset path (e.g. Assets/Bundles/UI/Lobby.prefab). Omit for current open scene(s).")]
            public string Path { get; set; }

            [ToolParameter("GameObject path within the current scene (e.g. Canvas/Panel). Takes precedence over path.")]
            public string ObjectPath { get; set; }
        }

        public static object HandleCommand(JObject raw)
        {
            var p = new ToolParams(raw);
            string assetPath = p.Get("path");
            string objectPath = p.Get("object_path");

            // Resolve Serialize method via reflection (HierarchyMdTree is in Assembly-CSharp)
            MethodInfo serializeMethod = FindSerializeMethod();
            if (serializeMethod == null)
                return new ErrorResponse("HierarchyMdTree.Serialize not found. Ensure the project is compiled.");

            try
            {
                string tree = BuildTree(serializeMethod, assetPath, objectPath);
                return new SuccessResponse("Hierarchy tree built.", new { tree });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to build hierarchy tree: {ex.Message}");
            }
        }

        private static string BuildTree(MethodInfo serializeMethod, string assetPath, string objectPath)
        {
            // Priority 1: specific GameObject in current scene
            if (!string.IsNullOrEmpty(objectPath))
            {
                var go = GameObject.Find(objectPath);
                if (go == null)
                    throw new Exception($"GameObject not found in scene: '{objectPath}'");

                return Serialize(serializeMethod, go.transform);
            }

            // Priority 2: prefab asset
            if (!string.IsNullOrEmpty(assetPath))
            {
                if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Not a prefab path: '{assetPath}'");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                    throw new Exception($"Prefab not found: '{assetPath}'");

                return Serialize(serializeMethod, prefab.transform);
            }

            // Priority 3: all root objects in open scenes
            return SerializeOpenScenes(serializeMethod);
        }

        private static string SerializeOpenScenes(MethodInfo serializeMethod)
        {
            var sb = new StringBuilder();
            int sceneCount = SceneManager.sceneCount;

            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                if (sceneCount > 1)
                    sb.AppendLine($"# Scene: {scene.path}").AppendLine();

                GameObject[] roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    sb.AppendLine(Serialize(serializeMethod, root.transform));
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static string Serialize(MethodInfo serializeMethod, Transform transform)
        {
            return (string)serializeMethod.Invoke(null, new object[] { transform });
        }

        private static MethodInfo FindSerializeMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = asm.GetTypes().FirstOrDefault(
                        t => t.Name == "HierarchyMdTree" && t.Namespace == "Game.Util");

                    if (type == null) continue;

                    MethodInfo method = type.GetMethod("Serialize",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Transform) },
                        null);

                    if (method != null) return method;
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                }
            }

            return null;
        }
    }
}
