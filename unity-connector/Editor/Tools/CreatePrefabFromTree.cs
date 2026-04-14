using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Create a prefab from a HierarchyMdTree string. Returns the saved asset path.", Group = "editor")]
    public static class CreatePrefabFromTree
    {
        public class Parameters
        {
            [ToolParameter("The Hierarchy MD Tree string that defines the GameObject structure.")]
            public string Tree { get; set; }

            [ToolParameter("Asset path to save the prefab (e.g. Assets/Bundles/UI/MyPrefab.prefab).")]
            public string Path { get; set; }

            [ToolParameter("If true, overwrite existing prefab at the path. Defaults to false.", Required = false)]
            public bool Overwrite { get; set; }
        }

        public static object HandleCommand(JObject raw)
        {
            var p = new ToolParams(raw);

            var treeResult = p.GetRequired("tree");
            if (!treeResult.IsSuccess)
                return new ErrorResponse(treeResult.ErrorMessage);

            var pathResult = p.GetRequired("path");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string tree = treeResult.Value;
            string assetPath = pathResult.Value;
            bool overwrite = p.GetBool("overwrite", false);

            // Validate path
            if (!assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Assets\\"))
                return new ErrorResponse($"Path must start with 'Assets/': '{assetPath}'");

            if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                assetPath += ".prefab";

            // Check existing
            if (!overwrite && File.Exists(assetPath))
                return new ErrorResponse($"Prefab already exists at '{assetPath}'. Set overwrite=true to replace.");

            // Resolve HierarchyMdTree.Deserialize via reflection
            MethodInfo deserializeMethod = FindDeserializeMethod();
            if (deserializeMethod == null)
                return new ErrorResponse("HierarchyMdTree.Deserialize not found. Ensure the project is compiled.");

            GameObject root = null;
            try
            {
                // Deserialize MD tree to GameObject hierarchy
                root = (GameObject)deserializeMethod.Invoke(null, new object[] { tree });
                if (root == null)
                    return new ErrorResponse("HierarchyMdTree.Deserialize returned null. Check the tree format.");

                // Ensure parent directory exists
                string directory = System.IO.Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    AssetDatabase.Refresh();
                }

                // Save as prefab
                bool success;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath, out success);

                if (!success || prefab == null)
                    return new ErrorResponse($"PrefabUtility.SaveAsPrefabAsset failed for '{assetPath}'.");

                // Get hierarchy info for response
                MethodInfo serializeMethod = FindSerializeMethod();
                string savedTree = null;
                if (serializeMethod != null)
                    savedTree = (string)serializeMethod.Invoke(null, new object[] { prefab.transform });

                return new SuccessResponse($"Prefab created at '{assetPath}'.", new
                {
                    path = assetPath,
                    root_name = prefab.name,
                    child_count = CountChildren(prefab.transform),
                    tree = savedTree
                });
            }
            catch (TargetInvocationException ex)
            {
                return new ErrorResponse($"Deserialization failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to create prefab: {ex.Message}");
            }
            finally
            {
                // Clean up the temporary scene object
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static int CountChildren(Transform t)
        {
            int count = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                count++;
                count += CountChildren(t.GetChild(i));
            }
            return count;
        }

        private static MethodInfo FindDeserializeMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = asm.GetTypes().FirstOrDefault(
                        t => t.Name == "HierarchyMdTree" && t.Namespace == "Game.Util");

                    if (type == null) continue;

                    MethodInfo method = type.GetMethod("Deserialize",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string) },
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
