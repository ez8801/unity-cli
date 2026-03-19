using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Simulate a touch/pointer event at screen coordinates via EventSystem.", Group = "editor")]
    public static class SimulateTouch
    {
        public class Parameters
        {
            [ToolParameter("Screen X coordinate", Required = true)]
            public float X { get; set; }

            [ToolParameter("Screen Y coordinate", Required = true)]
            public float Y { get; set; }

            [ToolParameter("Event action: 'down', 'up', 'click' (default), or 'move'")]
            public string Action { get; set; }
        }

        public static object HandleCommand(JObject raw)
        {
            var p = new ToolParams(raw);

            float? x = p.GetFloat("x");
            float? y = p.GetFloat("y");
            if (x == null || y == null)
                return new ErrorResponse("'x' and 'y' parameters are required.");

            string action = p.Get("action", "click").ToLowerInvariant();

            if (!EditorApplication.isPlaying)
                return new ErrorResponse("simulate_touch requires Play mode.");

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return new ErrorResponse("No EventSystem found in the current scene.");

            var position = new Vector2(x.Value, y.Value);
            var pointerData = new PointerEventData(eventSystem)
            {
                position = position,
                pressPosition = position
            };

            var raycastResults = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, raycastResults);

            if (raycastResults.Count == 0)
                return new ErrorResponse($"No UI element found at ({x.Value}, {y.Value}).");

            var target = raycastResults[0].gameObject;
            pointerData.pointerCurrentRaycast = raycastResults[0];
            pointerData.pointerPressRaycast = raycastResults[0];

            switch (action)
            {
                case "down":
                    pointerData.pointerPress = ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);
                    break;

                case "up":
                    ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);
                    break;

                case "click":
                    pointerData.pointerPress = ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerClickHandler);
                    break;

                case "move":
                    ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerMoveHandler);
                    break;

                default:
                    return new ErrorResponse($"Unknown action '{action}'. Use 'down', 'up', 'click', or 'move'.");
            }

            return new SuccessResponse($"Simulated '{action}' at ({x.Value}, {y.Value}).", new
            {
                x = x.Value,
                y = y.Value,
                action,
                hitObject = target.name,
                hitCount = raycastResults.Count
            });
        }
    }
}
