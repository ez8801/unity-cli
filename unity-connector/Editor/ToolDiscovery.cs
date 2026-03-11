using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace UnityCliConnector
{
    /// <summary>
    /// Finds [UnityCliTool] handlers on demand via reflection.
    /// No caching, no registration — every call scans live.
    /// </summary>
    public static class ToolDiscovery
    {
        public static MethodInfo FindHandler(string command)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }

                foreach (var type in types)
                {
                    if (type.IsClass == false) continue;
                    var attr = type.GetCustomAttribute<UnityCliToolAttribute>();
                    if (attr == null) continue;

                    var name = attr.Name ?? StringCaseUtility.ToSnakeCase(type.Name);
                    if (name != command) continue;

                    var method = type.GetMethod("HandleCommand",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(JObject) }, null);

                    if (method != null) return method;
                }
            }

            return null;
        }

        public static List<object> GetToolSchemas()
        {
            var tools = new List<object>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }

                foreach (var type in types)
                {
                    if (type.IsClass == false) continue;
                    var attr = type.GetCustomAttribute<UnityCliToolAttribute>();
                    if (attr == null) continue;

                    var name = attr.Name ?? StringCaseUtility.ToSnakeCase(type.Name);
                    var paramsType = type.GetNestedType("Parameters");

                    tools.Add(new
                    {
                        name,
                        description = attr.Description ?? "",
                        group = attr.Group ?? "",
                        parameters = GetParameterSchema(paramsType),
                    });
                }
            }

            return tools;
        }

        public static object GetToolHelp(string command)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }

                foreach (var type in types)
                {
                    if (type.IsClass == false) continue;
                    var attr = type.GetCustomAttribute<UnityCliToolAttribute>();
                    if (attr == null) continue;

                    var name = attr.Name ?? StringCaseUtility.ToSnakeCase(type.Name);
                    if (name != command) continue;

                    return new
                    {
                        name,
                        description = attr.Description ?? "",
                        group = attr.Group ?? "",
                        parameters = GetParameterSchema(type.GetNestedType("Parameters")),
                    };
                }
            }

            return null;
        }

        public static List<object> GetParameterSchema(Type paramsType)
        {
            if (paramsType == null) return new List<object>();

            return paramsType.GetProperties()
                .Select(p =>
                {
                    var attr = p.GetCustomAttribute<ToolParameterAttribute>();
                    return new
                    {
                        name = StringCaseUtility.ToSnakeCase(p.Name),
                        type = p.PropertyType.Name,
                        description = attr?.Description ?? "",
                        required = attr?.Required ?? false,
                    };
                })
                .Cast<object>()
                .ToList();
        }
    }
}
