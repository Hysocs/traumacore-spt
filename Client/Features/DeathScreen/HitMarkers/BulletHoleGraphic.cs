using UnityEngine;
using UnityEngine.UI;

namespace TraumaCore.Features.DeathScreen.HitMarkers
{
    internal sealed class BulletHoleGraphic : MaskableGraphic
    {
        private const int CircleSegments = 20;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            Rect rect = rectTransform.rect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            Vector2 center = rect.center;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = center;
            vertexHelper.AddVert(vertex);

            for (int index = 0; index <= CircleSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / CircleSegments;
                vertex.position = center + new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * radius;
                vertexHelper.AddVert(vertex);
            }

            for (int index = 1; index <= CircleSegments; index++)
                vertexHelper.AddTriangle(0, index, index + 1);
        }
    }

    internal sealed class LastHitXGraphic : MaskableGraphic
    {
        private const float StrokeWidthPixels = 1.5f;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect bounds = rectTransform.rect;
            AddStroke(vertexHelper,
                new Vector2(bounds.xMin, bounds.yMin),
                new Vector2(bounds.xMax, bounds.yMax));
            AddStroke(vertexHelper,
                new Vector2(bounds.xMin, bounds.yMax),
                new Vector2(bounds.xMax, bounds.yMin));
        }

        private void AddStroke(VertexHelper vertexHelper, Vector2 start, Vector2 end)
        {
            Vector2 direction = end - start;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized *
                (StrokeWidthPixels * 0.5f);
            int firstVertex = vertexHelper.currentVertCount;
            vertexHelper.AddVert(start - normal, color, Vector2.zero);
            vertexHelper.AddVert(start + normal, color, Vector2.zero);
            vertexHelper.AddVert(end + normal, color, Vector2.zero);
            vertexHelper.AddVert(end - normal, color, Vector2.zero);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
        }
    }
}
