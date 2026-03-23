using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Find references to an asset using AssetUsageDetector.", Group = "editor")]
    public static class FindAssetReferences
    {
        public class Parameters
        {
            [ToolParameter("Asset path (e.g. Assets/Prefabs/Player.prefab)")]
            public string Path { get; set; }

            [ToolParameter("Multiple asset paths")]
            public string[] Paths { get; set; }

            [ToolParameter("Asset GUID")]
            public string Guid { get; set; }

            [ToolParameter("Search in scenes (default true)")]
            public bool SearchInScenes { get; set; }

            [ToolParameter("Search in Assets folder (default true)")]
            public bool SearchInAssets { get; set; }

            [ToolParameter("Search in ProjectSettings (default true)")]
            public bool SearchInProjectSettings { get; set; }

            [ToolParameter("Search depth limit (default 4)")]
            public int Depth { get; set; }
        }

        public static object HandleCommand(JObject raw)
        {
            var p = new ToolParams(raw);

            string path = p.Get("path");
            JToken pathsToken = p.GetRaw("paths");
            string guid = p.Get("guid");

            bool searchInScenes = p.GetBool("search_in_scenes", true);
            bool searchInAssets = p.GetBool("search_in_assets", true);
            bool searchInProjectSettings = p.GetBool("search_in_project_settings", true);
            int depth = p.GetInt("depth") ?? 4;

            // Collect asset objects
            var assets = new List<Object>();
            var searchedPaths = new List<string>();

            if (!string.IsNullOrEmpty(guid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                    return new ErrorResponse($"No asset found for GUID: {guid}");

                var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (obj == null)
                    return new ErrorResponse($"Failed to load asset at: {assetPath}");

                assets.Add(obj);
                searchedPaths.Add(assetPath);
            }

            if (!string.IsNullOrEmpty(path))
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (obj == null)
                    return new ErrorResponse($"Asset not found at path: {path}");

                assets.Add(obj);
                searchedPaths.Add(path);
            }

            if (pathsToken != null && pathsToken.Type == JTokenType.Array)
            {
                foreach (string p2 in pathsToken.ToObject<string[]>())
                {
                    var obj = AssetDatabase.LoadAssetAtPath<Object>(p2);
                    if (obj == null)
                        return new ErrorResponse($"Asset not found at path: {p2}");

                    assets.Add(obj);
                    searchedPaths.Add(p2);
                }
            }

            if (assets.Count == 0)
                return new ErrorResponse("At least one of 'path', 'paths', or 'guid' is required.");

            // Find AssetUsageDetector via reflection
            Type detectorType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    detectorType = asm.GetTypes().FirstOrDefault(
                        t => t.Name == "AssetUsageDetector" && t.Namespace == "AssetUsageDetectorNamespace");
                    if (detectorType != null) break;
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                }
            }

            if (detectorType == null)
                return new ErrorResponse("AssetUsageDetector plugin not found. Install it from Assets/Plugins/.");

            try
            {
                return RunSearch(detectorType, assets.ToArray(), searchedPaths, searchInScenes, searchInAssets,
                    searchInProjectSettings, depth);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Search failed: {ex.Message}");
            }
        }

        private static object RunSearch(Type detectorType, Object[] assets, List<string> searchedPaths,
            bool searchInScenes, bool searchInAssets, bool searchInProjectSettings, int depth)
        {
            // Create Parameters
            Type paramsType = detectorType.GetNestedType("Parameters");
            if (paramsType == null)
                return new ErrorResponse("AssetUsageDetector.Parameters type not found.");

            object parameters = Activator.CreateInstance(paramsType);
            paramsType.GetField("objectsToSearch").SetValue(parameters, assets);
            paramsType.GetField("searchInAssetsFolder").SetValue(parameters, searchInAssets);
            paramsType.GetField("searchInProjectSettings").SetValue(parameters, searchInProjectSettings);
            paramsType.GetField("searchDepthLimit").SetValue(parameters, depth);
            paramsType.GetField("showDetailedProgressBar").SetValue(parameters, false);
            paramsType.GetField("noAssetDatabaseChanges").SetValue(parameters, true);

            // Set scene search mode via enum
            if (!searchInScenes)
            {
                Type sceneModeType = detectorType.Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "SceneSearchMode");
                if (sceneModeType != null)
                {
                    object noneValue = Enum.Parse(sceneModeType, "None");
                    paramsType.GetField("searchInScenes").SetValue(parameters, noneValue);
                }
            }

            // Create detector and run
            object detector = Activator.CreateInstance(detectorType);
            MethodInfo runMethod = detectorType.GetMethod("Run");
            object searchResult = runMethod.Invoke(detector, new[] { parameters });

            // Parse results
            return ParseResult(searchResult, searchedPaths);
        }

        private static object ParseResult(object searchResult, List<string> searchedPaths)
        {
            Type resultType = searchResult.GetType();

            bool success = (bool)resultType.GetProperty("SearchCompletedSuccessfully").GetValue(searchResult);
            if (!success)
                return new ErrorResponse("Search did not complete successfully.");

            int groupCount = (int)resultType.GetProperty("NumberOfGroups").GetValue(searchResult);
            var groups = new List<object>();
            int totalReferences = 0;

            PropertyInfo indexer = resultType.GetProperties()
                .FirstOrDefault(pi => pi.GetIndexParameters().Length == 1 &&
                                      pi.GetIndexParameters()[0].ParameterType == typeof(int));

            for (int g = 0; g < groupCount; g++)
            {
                object group = indexer.GetValue(searchResult, new object[] { g });
                Type groupType = group.GetType();

                string title = (string)groupType.GetProperty("Title").GetValue(group);
                object typeEnum = groupType.GetProperty("Type").GetValue(group);
                int refCount = (int)groupType.GetProperty("NumberOfReferences").GetValue(group);

                PropertyInfo groupIndexer = groupType.GetProperties()
                    .FirstOrDefault(pi => pi.GetIndexParameters().Length == 1 &&
                                          pi.GetIndexParameters()[0].ParameterType == typeof(int));

                var references = new List<object>();
                for (int r = 0; r < refCount; r++)
                {
                    object node = groupIndexer.GetValue(group, new object[] { r });
                    var nodeData = ParseNode(node, 0, 2);
                    if (nodeData != null)
                        references.Add(nodeData);
                }

                totalReferences += refCount;
                groups.Add(new
                {
                    title,
                    type = typeEnum.ToString(),
                    referenceCount = refCount,
                    references
                });
            }

            return new SuccessResponse($"Found {totalReferences} reference(s).", new
            {
                searchedAssets = searchedPaths,
                groups,
                totalReferences
            });
        }

        private static object ParseNode(object node, int currentDepth, int maxDepth)
        {
            if (node == null) return null;

            Type nodeType = node.GetType();
            string label = (string)nodeType.GetProperty("Label").GetValue(node);

            // Get UnityObject path if available
            Object unityObj = nodeType.GetProperty("UnityObject").GetValue(node) as Object;
            string assetPath = unityObj != null ? AssetDatabase.GetAssetPath(unityObj) : null;

            var result = new Dictionary<string, object>
            {
                { "label", label }
            };

            if (!string.IsNullOrEmpty(assetPath))
                result["assetPath"] = assetPath;

            if (currentDepth < maxDepth)
            {
                int linkCount = (int)nodeType.GetProperty("NumberOfOutgoingLinks").GetValue(node);
                PropertyInfo linkIndexer = nodeType.GetProperties()
                    .FirstOrDefault(pi => pi.GetIndexParameters().Length == 1 &&
                                          pi.GetIndexParameters()[0].ParameterType == typeof(int));

                if (linkCount > 0 && linkIndexer != null)
                {
                    var links = new List<object>();
                    for (int i = 0; i < Math.Min(linkCount, 50); i++)
                    {
                        object link = linkIndexer.GetValue(node, new object[] { i });
                        Type linkType = link.GetType();

                        object targetNode = linkType.GetField("targetNode", BindingFlags.Public | BindingFlags.Instance).GetValue(link);
                        object descriptions = linkType.GetField("descriptions", BindingFlags.Public | BindingFlags.Instance).GetValue(link);

                        string targetLabel = null;
                        if (targetNode != null)
                            targetLabel = (string)targetNode.GetType().GetProperty("Label").GetValue(targetNode);

                        var descList = new List<string>();
                        if (descriptions is IList list)
                        {
                            foreach (var d in list)
                                descList.Add(d?.ToString());
                        }

                        links.Add(new
                        {
                            target = targetLabel,
                            descriptions = descList
                        });
                    }

                    if (links.Count > 0)
                        result["links"] = links;
                }
            }

            return result;
        }
    }
}
