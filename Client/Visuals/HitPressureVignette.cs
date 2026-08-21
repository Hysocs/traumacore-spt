using UnityEngine;
using UnityEngine.UI;

namespace TraumaCore.Visuals
{
    internal sealed class HitPressureVignette : MaskableGraphic
    {
        private const float IntensityPerHit = 0.05f;
        private const float MaximumIntensity = 0.50f;
        private const float FadeDurationSeconds = 1.50f;

        private static HitPressureVignette _instance;

        private float _fadeStartTimeSeconds;
        private float _fadeStartIntensity;
        private float _currentIntensity;

        internal static float ApplyHitStack()
        {
            EnsureOverlayCreated();
            float nowSeconds = Time.unscaledTime;
            float currentIntensity = _instance.EvaluateCurrentIntensity(nowSeconds);
            _instance._fadeStartIntensity = Mathf.Min(
                MaximumIntensity,
                currentIntensity + IntensityPerHit);
            _instance._fadeStartTimeSeconds = nowSeconds;
            _instance.ApplyRenderedIntensity(_instance._fadeStartIntensity);
            return _instance._fadeStartIntensity;
        }

        internal static void RemoveOverlay()
        {
            if (_instance == null)
                return;

            Destroy(_instance.transform.parent.gameObject);
            _instance = null;
        }

        private static void EnsureOverlayCreated()
        {
            if (_instance != null)
                return;

            GameObject canvasObject = new GameObject(
                "TraumaCore Hit Pressure Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            DontDestroyOnLoad(canvasObject);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31990;

            GameObject vignetteObject = new GameObject(
                "TraumaCore Hit Pressure Vignette",
                typeof(RectTransform),
                typeof(HitPressureVignette));
            RectTransform vignetteRect = vignetteObject.GetComponent<RectTransform>();
            vignetteRect.SetParent(canvasObject.transform, false);
            vignetteRect.anchorMin = Vector2.zero;
            vignetteRect.anchorMax = Vector2.one;
            vignetteRect.offsetMin = Vector2.zero;
            vignetteRect.offsetMax = Vector2.zero;

            _instance = vignetteObject.GetComponent<HitPressureVignette>();
            _instance.raycastTarget = false;
            _instance.ApplyRenderedIntensity(0f);
        }

        private void Update()
        {
            ApplyRenderedIntensity(EvaluateCurrentIntensity(Time.unscaledTime));
        }

        private float EvaluateCurrentIntensity(float nowSeconds)
        {
            float fadeProgress = Mathf.Clamp01(
                (nowSeconds - _fadeStartTimeSeconds) / FadeDurationSeconds);
            return Mathf.Lerp(_fadeStartIntensity, 0f, fadeProgress);
        }

        private void ApplyRenderedIntensity(float intensity)
        {
            if (Mathf.Approximately(_currentIntensity, intensity))
                return;

            _currentIntensity = intensity;
            gameObject.SetActive(intensity > 0.001f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_currentIntensity <= 0f)
                return;

            Rect bounds = rectTransform.rect;
            float horizontalInset = bounds.width * 0.27f;
            float verticalInset = bounds.height * 0.23f;
            Rect clearCenter = new Rect(
                bounds.xMin + horizontalInset,
                bounds.yMin + verticalInset,
                bounds.width - horizontalInset * 2f,
                bounds.height - verticalInset * 2f);

            float edgeOpacity = Mathf.Min(0.95f, _currentIntensity * 3.40f);
            Color edgeColor = new Color(0.015f, 0f, 0f, edgeOpacity);
            Color centerColor = new Color(0.10f, 0f, 0.005f, 0f);

            AddVertex(vertexHelper, new Vector2(bounds.xMin, bounds.yMax), edgeColor);
            AddVertex(vertexHelper, new Vector2(bounds.xMax, bounds.yMax), edgeColor);
            AddVertex(vertexHelper, new Vector2(bounds.xMax, bounds.yMin), edgeColor);
            AddVertex(vertexHelper, new Vector2(bounds.xMin, bounds.yMin), edgeColor);
            AddVertex(vertexHelper, new Vector2(clearCenter.xMin, clearCenter.yMax), centerColor);
            AddVertex(vertexHelper, new Vector2(clearCenter.xMax, clearCenter.yMax), centerColor);
            AddVertex(vertexHelper, new Vector2(clearCenter.xMax, clearCenter.yMin), centerColor);
            AddVertex(vertexHelper, new Vector2(clearCenter.xMin, clearCenter.yMin), centerColor);

            AddQuad(vertexHelper, 0, 1, 5, 4);
            AddQuad(vertexHelper, 1, 2, 6, 5);
            AddQuad(vertexHelper, 2, 3, 7, 6);
            AddQuad(vertexHelper, 3, 0, 4, 7);
        }

        private static void AddVertex(
            VertexHelper vertexHelper,
            Vector2 position,
            Color color)
        {
            vertexHelper.AddVert(position, color, Vector2.zero);
        }

        private static void AddQuad(
            VertexHelper vertexHelper,
            int first,
            int second,
            int third,
            int fourth)
        {
            vertexHelper.AddTriangle(first, second, third);
            vertexHelper.AddTriangle(first, third, fourth);
        }
    }
}
