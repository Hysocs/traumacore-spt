using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TraumaCore
{
    internal readonly struct AnatomyScreenLine
    {
        internal readonly Vector2 Start, End;
        internal readonly Color Color;
        internal readonly float Thickness;

        internal AnatomyScreenLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Start = start;
            End = end;
            Color = color;
            Thickness = thickness;
        }
    }

    internal static class AnatomyOverlayRenderer
    {
        internal static Vector2 ConvertPreviewPoint(Rect bounds, Rect uv, Vector3 viewport) =>
            new(bounds.xMin + (viewport.x - uv.x) / uv.width * bounds.width,
                bounds.yMin + (viewport.y - uv.y) / uv.height * bounds.height);

        internal static void AppendGeometry(Camera camera, RectTransform canvas,
            IList<AnatomyOverlayLine> geometry, List<AnatomyScreenLine> lines,
            RawImage preview = null)
        {
            if (camera == null || canvas == null || Screen.width <= 0 || Screen.height <= 0)
                return;
            foreach (AnatomyOverlayLine line in geometry)
            {
                if (line.WorldRadius < 0f)
                {
                    float size = -line.WorldRadius;
                    AddLine(line.Start - camera.transform.right * size,
                        line.Start + camera.transform.right * size, line.Color, line.ScreenThickness);
                    AddLine(line.Start - camera.transform.up * size,
                        line.Start + camera.transform.up * size, line.Color, line.ScreenThickness);
                    continue;
                }
                float thickness = line.ScreenThickness;
                if (line.WorldRadius > 0f)
                {
                    Vector3 midpoint = (line.Start + line.End) * 0.5f;
                    if (!TryProject(midpoint, out Vector2 center) ||
                        !TryProject(midpoint + camera.transform.right * line.WorldRadius,
                            out Vector2 radius)) continue;
                    thickness = Mathf.Clamp(Vector2.Distance(center, radius) * 2f, 0.75f, 80f);
                }
                AddLine(line.Start, line.End, line.Color, thickness);
            }

            bool TryProject(Vector3 world, out Vector2 local)
            {
                local = default;
                if (preview != null)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(world);
                    Rect uv = preview.uvRect;
                    if (viewport.z <= 0f || Mathf.Abs(uv.width) < 0.000001f || Mathf.Abs(uv.height) < 0.000001f)
                        return false;
                    local = ConvertPreviewPoint(canvas.rect, uv, viewport);
                    return true;
                }
                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f) return false;
                if (camera.targetTexture == null && camera.rect.width > 0f && camera.rect.height > 0f)
                {
                    screen.x /= camera.rect.width;
                    screen.y /= camera.rect.height;
                }
                Rect rect = canvas.rect;
                local = new Vector2(rect.xMin + screen.x * rect.width / Screen.width,
                    rect.yMin + screen.y * rect.height / Screen.height);
                return true;
            }

            void AddLine(Vector3 start, Vector3 end, Color color, float thickness)
            {
                if (TryProject(start, out Vector2 projectedStart) &&
                    TryProject(end, out Vector2 projectedEnd))
                    lines.Add(new AnatomyScreenLine(projectedStart, projectedEnd, color, thickness));
            }
        }

        internal static void PopulateMesh(VertexHelper vertices, IList<AnatomyScreenLine> lines)
        {
            vertices.Clear();
            if (lines == null) return;
            foreach (AnatomyScreenLine line in lines)
            {
                Vector2 delta = line.End - line.Start;
                if (delta.sqrMagnitude < 0.01f) continue;
                Vector2 normal = new Vector2(-delta.y, delta.x).normalized * line.Thickness * 0.5f;
                int vertex = vertices.currentVertCount;
                vertices.AddVert(line.Start - normal, line.Color, Vector2.zero);
                vertices.AddVert(line.Start + normal, line.Color, Vector2.zero);
                vertices.AddVert(line.End + normal, line.Color, Vector2.zero);
                vertices.AddVert(line.End - normal, line.Color, Vector2.zero);
                vertices.AddTriangle(vertex, vertex + 1, vertex + 2);
                vertices.AddTriangle(vertex, vertex + 2, vertex + 3);
            }
        }
    }
}
