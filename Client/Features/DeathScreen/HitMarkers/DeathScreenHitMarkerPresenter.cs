using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using EFT;
using EFT.HealthSystem;
using EFT.UI;
using EFT.UI.SessionEnd;
using EFT.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using TraumaCore.Features.DeathScreen.DamageTracking;

namespace TraumaCore.Features.DeathScreen.HitMarkers
{
    internal static class DeathScreenHitMarkerPresenter
    {
        private sealed class StaticHitCallout
        {
            internal EBodyPart BodyPart;
            internal Transform Anchor;
            internal Vector3 LocalEntry;
            internal Vector3 LocalDirection;
            internal float TraveledDepth;
            internal bool PassedThrough;
            internal int Sequence;
            internal bool IsFragmentBranch;
            internal bool HasFragmentBranches;
            internal DeathScreenDamageTracker.HitAnatomyRecord Anatomy;
            internal StaticHitCallout Parent;
            internal RectTransform EntryMark;
            internal RectTransform StartArrow;
            internal RectTransform ApproachLine;
            internal RectTransform DepthLine;
            internal RectTransform EndPoint;
            internal RectTransform ExitMark;
            internal RectTransform CustomHitMark;
            internal RectTransform CustomHitLine;
            internal RectTransform Label;
            internal TextMeshProUGUI Text;
        }

        private const float BulletHoleSizePixels = 7f;
        private const float LabelWidthPixels = 300f;
        private const float MinimumLabelWidthPixels = 190f;
        private const float LabelWidthFraction = 0.44f;
        private const float LabelHeightPixels = 52f;
        private const float LabelSpacingPixels = 5f;
        private const float LabelModelClearancePixels = 10f;
        private const float LabelLaneCenterPull = 0.50f;
        private const float FontSizePoints = 12f;

        internal static void Show(
            SessionResultExitStatus screen,
            Profile activeProfile,
            ESideType side,
            ExitStatus exitStatus)
        {
            if (exitStatus != ExitStatus.Killed &&
                exitStatus != ExitStatus.MissingInAction)
                return;

            DamageHistory damageHistory = activeProfile?.EftStats?.DamageHistory;
            if (damageHistory == null)
                return;

            PlayerModelView modelView = screen._playerModelView;

            if (modelView == null)
                return;

            MoveLevelIntoCharacterName(screen, activeProfile, side);

            screen.StartCoroutine(
                CreateMarkersWhenModelReady(
                    screen,
                    screen.gameObject,
                    modelView,
                    screen._bodyPartLabel,
                    activeProfile,
                    damageHistory));
        }

        internal static void ShowStaticCorpsePreview(
            MonoBehaviour host,
            GameObject activeRoot,
            Camera previewCamera,
            RawImage previewImage,
            Transform anchorRoot,
            Profile profile,
            Func<bool> shouldShowTrajectories, Func<bool> shouldHideBackHits = null)
        {
            if (host == null || activeRoot == null || previewCamera == null ||
                previewImage == null || anchorRoot == null || profile == null)
                return;
            host.StartCoroutine(CreateStaticCorpseMarkers(
                activeRoot, previewCamera, previewImage, anchorRoot, profile,
                shouldShowTrajectories, shouldHideBackHits));
        }

        private static IEnumerator CreateStaticCorpseMarkers(
            GameObject activeRoot,
            Camera previewCamera,
            RawImage previewImage,
            Transform anchorRoot,
            Profile profile,
            Func<bool> shouldShowTrajectories, Func<bool> shouldHideBackHits)
        {
            yield return null;
            Dictionary<EBodyPart, Transform> anchors = new();
            foreach (EBodyPart bodyPart in BodyPartAnchorResolver.BodyParts)
            {
                Transform anchor = BodyPartAnchorResolver.Find(anchorRoot, bodyPart);
                if (anchor != null)
                    anchors.Add(bodyPart, anchor);
            }

            GameObject container = new GameObject(
                "TraumaCoreHitMarkers", typeof(RectTransform));
            container.transform.SetParent(previewImage.rectTransform, false);
            RectTransform markerRect = (RectTransform)container.transform;
            markerRect.anchorMin = Vector2.zero;
            markerRect.anchorMax = Vector2.one;
            markerRect.offsetMin = Vector2.zero;
            markerRect.offsetMax = Vector2.zero;
            ModelPreview preview = new ModelPreview
            {
                Camera = previewCamera,
                Image = previewImage,
                Container = markerRect
            };
            List<StaticHitCallout> callouts = CreateStaticHitCallouts(
                profile, anchors, markerRect, anchorRoot, includeLabels: true);
            while (activeRoot != null && activeRoot.activeInHierarchy)
            {
                bool shouldShow = shouldShowTrajectories?.Invoke() ?? true;
                container.SetActive(shouldShow);
                if (shouldShow)
                    UpdateStaticHitCallouts(preview, callouts,
                        shouldHideBackHits?.Invoke() ?? false);
                yield return null;
            }
        }

        private static List<StaticHitCallout> CreateStaticHitCallouts(
            Profile profile,
            IReadOnlyDictionary<EBodyPart, Transform> anchors,
            RectTransform container,
            Transform modelRoot,
            bool includeLabels)
        {
            List<StaticHitCallout> callouts = new();
            Transform[] meshBones = MeshSkeleton.ResolveBones(modelRoot);
            foreach (EBodyPart bodyPart in BodyPartAnchorResolver.BodyParts)
            {
                if (!anchors.TryGetValue(bodyPart, out Transform anchor) ||
                    !DeathScreenDamageTracker.TryGetRecordedDamage(
                        profile, bodyPart, out var recordedDamage) ||
                    recordedDamage.DirectHits <= 0)
                    continue;

                for (int impactIndex = 0;
                    impactIndex < recordedDamage.Impacts.Count;
                    impactIndex++)
                {
                    var impact = recordedDamage.Impacts[impactIndex];
                    Transform impactAnchor = BodyPartAnchorResolver.ResolveRecordedAnchor(
                        meshBones, impact.AnchorName, anchor);
                    if (!impact.HasWoundTrajectory)
                        continue;
                    WoundPathSegment primarySegment =
                        impact.Trajectory.Segments[0];
                    Color color = ResolveImpactColor(impact);
                    RectTransform entryMark = CreateTrajectoryGraphic<LastHitXGraphic>(
                        container, $"Entry_{bodyPart}_{impactIndex}", 12f, Color.white);
                    RectTransform startArrow = CreateTrajectoryGraphic<TrajectoryArrowGraphic>(
                        container, $"Direction_{bodyPart}_{impactIndex}", 12f, color);
                    RectTransform approachLine = CreateTrajectoryLine(
                        container, $"Approach_{bodyPart}_{impactIndex}", color, 1.5f);
                    RectTransform depthLine = CreateTrajectoryLine(
                        container, $"Depth_{bodyPart}_{impactIndex}", color, 3f);
                    RectTransform endPoint = CreateTrajectoryGraphic<BulletHoleGraphic>(
                        container, $"End_{bodyPart}_{impactIndex}", 8f, color);
                    RectTransform exitMark = CreateTrajectoryGraphic<LastHitXGraphic>(
                        container, $"Exit_{bodyPart}_{impactIndex}", 12f, color);
                    RectTransform customHitMark =
                        CreateTrajectoryGraphic<LastHitXGraphic>(container,
                            $"CustomHit_{bodyPart}_{impactIndex}", 18f,
                            ResolveCustomHitColor(impact.Anatomy));
                    RectTransform customHitLine = CreateTrajectoryLine(container,
                        $"CustomHitLine_{bodyPart}_{impactIndex}",
                        ResolveCustomHitColor(impact.Anatomy), 5f);
                    RectTransform label;
                    TextMeshProUGUI text;
                    if (includeLabels)
                    {
                        label = CreateTrajectoryLabel(
                            container, bodyPart, impact.TraveledDepth,
                            impact.PassedThrough, impact.Anatomy, color, out text);
                    }
                    else
                    {
                        label = CreateHiddenTrajectoryGraphic(container);
                        text = null;
                    }
                    StaticHitCallout primary = new StaticHitCallout
                    {
                        BodyPart = bodyPart,
                        Anchor = impactAnchor,
                        LocalEntry = primarySegment.StartPoint,
                        LocalDirection = primarySegment.Direction,
                        TraveledDepth = primarySegment.TraveledDepth,
                        PassedThrough = primarySegment.Endpoint ==
                            WoundPathEndpoint.Exit,
                        Sequence = impact.Sequence,
                        HasFragmentBranches = impact.Trajectory.HasBranches,
                        Anatomy = impact.Anatomy,
                        EntryMark = entryMark,
                        StartArrow = startArrow,
                        ApproachLine = approachLine,
                        DepthLine = depthLine,
                        EndPoint = endPoint,
                        ExitMark = exitMark,
                        CustomHitMark = customHitMark,
                        CustomHitLine = customHitLine,
                        Label = label,
                        Text = text
                    };
                    callouts.Add(primary);
                    for (int segmentIndex = 1;
                        segmentIndex < impact.Trajectory.Segments.Length;
                        segmentIndex++)
                    {
                        WoundPathSegment fragment =
                            impact.Trajectory.Segments[segmentIndex];
                        RectTransform fragmentApproach = CreateTrajectoryLine(
                            container,
                            $"Fragment_{bodyPart}_{impactIndex}_{segmentIndex}",
                            color, 2f);
                        RectTransform fragmentEnd =
                            CreateTrajectoryGraphic<BulletHoleGraphic>(container,
                                $"FragmentEnd_{bodyPart}_{impactIndex}_{segmentIndex}",
                                8f, color);
                        RectTransform fragmentExit =
                            CreateTrajectoryGraphic<LastHitXGraphic>(container,
                                $"FragmentExit_{bodyPart}_{impactIndex}_{segmentIndex}",
                                12f, color);
                        callouts.Add(new StaticHitCallout
                        {
                            BodyPart = bodyPart,
                            Anchor = impactAnchor,
                            LocalEntry = fragment.StartPoint,
                            LocalDirection = fragment.Direction,
                            TraveledDepth = fragment.TraveledDepth,
                            PassedThrough = fragment.Endpoint ==
                                WoundPathEndpoint.Exit,
                            Sequence = impact.Sequence,
                            IsFragmentBranch = true,
                            Parent = primary,
                            EntryMark = CreateHiddenTrajectoryGraphic(container),
                            StartArrow = CreateHiddenTrajectoryGraphic(container),
                            ApproachLine = fragmentApproach,
                            DepthLine = CreateHiddenTrajectoryGraphic(container),
                            EndPoint = fragmentEnd,
                            ExitMark = fragmentExit,
                            CustomHitMark = CreateHiddenTrajectoryGraphic(container),
                            CustomHitLine = CreateHiddenTrajectoryGraphic(container),
                            Label = CreateHiddenTrajectoryGraphic(container),
                            Text = null
                        });
                    }
                }
            }
            TraumaLog.Info(
                $"[WoundInspection] Created {callouts.Count} hit callout(s)");
            return callouts;
        }

        private static void UpdateStaticHitCallouts(
            ModelPreview preview,
            IReadOnlyList<StaticHitCallout> callouts, bool hideBackHits = true)
        {
            Rect bounds = preview.Container.rect;
            for (int index = 0; index < callouts.Count; index++)
            {
                StaticHitCallout callout = callouts[index];
                Vector3 entryWorld = callout.Anchor.TransformPoint(callout.LocalEntry);
                Vector3 directionWorld = callout.Anchor.TransformDirection(
                    callout.LocalDirection).normalized;
                const float approachDistance = 0.28f;
                Vector3 approachWorld = callout.Parent != null
                    ? ResolveCalloutEnd(callout.Parent)
                    : entryWorld - directionWorld * approachDistance;
                Vector3 depthWorld = entryWorld + directionWorld * callout.TraveledDepth;
                bool hasCustomHit = !callout.IsFragmentBranch &&
                    callout.Anatomy.HasCustomHit;
                Vector3 customHitWorld = callout.Anchor.TransformPoint(
                    callout.Anatomy.LimbBone
                        ? callout.Anatomy.LocalBoneIntersection
                        : callout.Anatomy.LocalIntersection);
                bool isFragment = callout.IsFragmentBranch;
                Vector3 displayedEndWorld = !isFragment && callout.PassedThrough
                    ? depthWorld + directionWorld * approachDistance
                    : depthWorld;
                Vector3 approachViewport = preview.Camera.WorldToViewportPoint(approachWorld);
                Vector3 entryViewport = preview.Camera.WorldToViewportPoint(entryWorld);
                Vector3 exitViewport = preview.Camera.WorldToViewportPoint(depthWorld);
                Vector3 depthViewport = preview.Camera.WorldToViewportPoint(displayedEndWorld);
                Vector3 customHitViewport = preview.Camera.WorldToViewportPoint(
                    customHitWorld);
                bool isVisible = IsInsidePreview(preview.Image, entryViewport);
                if (hideBackHits)
                {
                    StaticHitCallout surfaceHit = callout;
                    while (surfaceHit.Parent != null) surfaceHit = surfaceHit.Parent;
                    Vector3 normal = surfaceHit.Anchor.TransformDirection(
                        surfaceHit.Anatomy.LocalSurfaceNormal);
                    Vector3 surface = surfaceHit.Anchor.TransformPoint(surfaceHit.LocalEntry);
                    Vector3 toCamera = preview.Camera.orthographic
                        ? -preview.Camera.transform.forward
                        : preview.Camera.transform.position - surface;
                    if (normal.sqrMagnitude > 0.0001f)
                        isVisible &= Vector3.Dot(normal, toCamera) > 0f;
                }
                bool hasApproach = IsInsidePreview(preview.Image,
                    approachViewport);
                bool hasDepthPoint = IsInsidePreview(preview.Image,
                    depthViewport);
                bool hasExitPoint = IsInsidePreview(preview.Image,
                    exitViewport);
                bool hasCustomPoint = IsInsidePreview(preview.Image,
                    customHitViewport);
                bool hasDepth = callout.TraveledDepth > 0f;
                callout.EntryMark.gameObject.SetActive(isVisible && !isFragment);
                callout.StartArrow.gameObject.SetActive(
                    isVisible && hasApproach && !isFragment);
                callout.ApproachLine.gameObject.SetActive(isVisible &&
                    hasApproach && (!isFragment || hasDepthPoint));
                callout.DepthLine.gameObject.SetActive(
                    isVisible && hasDepth && hasDepthPoint && !isFragment);
                callout.EndPoint.gameObject.SetActive(
                    isVisible && hasDepth && hasDepthPoint && !callout.PassedThrough &&
                    !callout.HasFragmentBranches);
                callout.ExitMark.gameObject.SetActive(
                    isVisible && hasDepth && hasExitPoint && callout.PassedThrough);
                callout.CustomHitMark.gameObject.SetActive(
                    isVisible && hasCustomHit && hasCustomPoint);
                callout.CustomHitLine.gameObject.SetActive(
                    isVisible && hasCustomHit && hasCustomPoint);
                callout.Label.gameObject.SetActive(
                    isVisible && !isFragment && callout.Text != null);
                if (!isVisible)
                    continue;

                Vector2 entryPoint = ConvertViewportToLocal(preview.Image, entryViewport);
                Vector2 approachPoint = hasApproach
                    ? ConvertViewportToLocal(preview.Image, approachViewport) : entryPoint;
                Vector2 exitPoint = hasExitPoint
                    ? ConvertViewportToLocal(preview.Image, exitViewport) : entryPoint;
                Vector2 depthPoint = hasDepthPoint
                    ? ConvertViewportToLocal(preview.Image, depthViewport) : entryPoint;
                Vector2 customHitPoint = hasCustomPoint
                    ? ConvertViewportToLocal(preview.Image, customHitViewport) : entryPoint;
                float side = entryPoint.x < bounds.center.x ? -1f : 1f;
                Vector2 labelPoint = depthPoint + new Vector2(
                    side * 20f, 16f + ((index % 3) - 1) * 14f);
                labelPoint.x = Mathf.Clamp(labelPoint.x,
                    bounds.xMin + 78f, bounds.xMax - 78f);
                labelPoint.y = Mathf.Clamp(labelPoint.y,
                    bounds.yMin + 18f, bounds.yMax - 18f);

                callout.StartArrow.anchoredPosition = approachPoint;
                callout.EntryMark.anchoredPosition = entryPoint;
                callout.EndPoint.anchoredPosition = depthPoint;
                callout.ExitMark.anchoredPosition = exitPoint;
                UpdateCalloutLine(
                    callout.ApproachLine,
                    approachPoint,
                    isFragment ? depthPoint : entryPoint);
                UpdateCalloutLine(callout.DepthLine, entryPoint, depthPoint);
                UpdateCalloutLine(callout.CustomHitLine, entryPoint,
                    customHitPoint);
                callout.CustomHitMark.anchoredPosition = customHitPoint;
                Vector2 approachDirection = entryPoint - approachPoint;
                callout.StartArrow.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(approachDirection.y, approachDirection.x) * Mathf.Rad2Deg);
                if (!isFragment && callout.Text != null)
                {
                    callout.Label.pivot = side < 0f
                        ? new Vector2(1f, 0.5f)
                        : new Vector2(0f, 0.5f);
                    callout.Text.alignment = side < 0f
                        ? TextAlignmentOptions.MidlineRight
                        : TextAlignmentOptions.MidlineLeft;
                    callout.Label.anchoredPosition = labelPoint;
                }
            }
        }

        private static Color ResolveCustomHitColor(
            DeathScreenDamageTracker.HitAnatomyRecord anatomy)
        {
            if (anatomy.Heart) return Color.green;
            if (anatomy.Brain) return Color.magenta;
            if (anatomy.CervicalSpine) return new Color(0.05f, 1f, 0.75f, 1f);
            if (anatomy.ThoracicSpine) return new Color(1f, 0.78f, 0.05f, 1f);
            if (anatomy.Skull) return new Color(0.55f, 0.82f, 1f, 1f);
            if (anatomy.Ribcage) return new Color(0.95f, 0.82f, 0.55f, 1f);
            return new Color(0.1f, 0.55f, 1f, 1f);
        }

        private static bool IsInsidePreview(RawImage image, Vector3 viewport)
        {
            if (image == null || viewport.z <= 0f)
                return false;
            Rect uv = image.uvRect;
            float x = (viewport.x - uv.x) / uv.width;
            float y = (viewport.y - uv.y) / uv.height;
            return x >= 0f && x <= 1f && y >= 0f && y <= 1f;
        }

        private static Vector3 ResolveCalloutEnd(StaticHitCallout callout)
        {
            Vector3 entry = callout.Anchor.TransformPoint(callout.LocalEntry);
            Vector3 direction = callout.Anchor.TransformDirection(
                callout.LocalDirection).normalized;
            return entry + direction * callout.TraveledDepth;
        }

        private static RectTransform CreateTrajectoryLine(
            RectTransform container, string name, Color color, float width)
        {
            GameObject lineObject = new GameObject(
                name, typeof(RectTransform), typeof(Image));
            lineObject.transform.SetParent(container, false);
            RectTransform line = lineObject.GetComponent<RectTransform>();
            line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
            line.pivot = new Vector2(0f, 0.5f);
            line.sizeDelta = new Vector2(1f, width);
            Image image = lineObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return line;
        }

        private static RectTransform CreateHiddenTrajectoryGraphic(
            RectTransform container)
        {
            GameObject graphicObject = new GameObject(
                "HiddenTrajectoryGraphic", typeof(RectTransform));
            graphicObject.transform.SetParent(container, false);
            graphicObject.SetActive(false);
            return graphicObject.GetComponent<RectTransform>();
        }

        private static RectTransform CreateTrajectoryGraphic<T>(
            RectTransform container, string name, float size, Color color)
            where T : MaskableGraphic
        {
            GameObject graphicObject = new GameObject(
                name, typeof(RectTransform), typeof(T));
            graphicObject.transform.SetParent(container, false);
            RectTransform rect = graphicObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * size;
            T graphic = graphicObject.GetComponent<T>();
            graphic.color = color;
            graphic.raycastTarget = false;
            return rect;
        }

        private static RectTransform CreateTrajectoryLabel(
            RectTransform container, EBodyPart bodyPart, float depth,
            bool passedThrough, DeathScreenDamageTracker.HitAnatomyRecord anatomy,
            Color color, out TextMeshProUGUI text)
        {
            GameObject labelObject = new GameObject(
                $"TrajectoryText_{bodyPart}", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(container, false);
            RectTransform label = labelObject.GetComponent<RectTransform>();
            label.anchorMin = label.anchorMax = new Vector2(0.5f, 0.5f);
            label.sizeDelta = new Vector2(155f, 26f);
            text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = BuildWoundDescription(bodyPart, depth, passedThrough, anatomy);
            text.fontSize = 12f;
            text.fontStyle = FontStyles.Bold;
            text.enableWordWrapping = false;
            label.sizeDelta = new Vector2(text.preferredWidth, 26f);
            text.color = color;
            text.raycastTarget = false;
            Outline outline = labelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1f, -1f);
            return label;
        }

        private static string BuildWoundDescription(
            EBodyPart bodyPart, float depth, bool passedThrough,
            DeathScreenDamageTracker.HitAnatomyRecord anatomy)
        {
            string bodyPartName = bodyPart switch
            {
                EBodyPart.LeftArm => "Left arm",
                EBodyPart.RightArm => "Right arm",
                EBodyPart.LeftLeg => "Left leg",
                EBodyPart.RightLeg => "Right leg",
                _ => bodyPart.ToString()
            };
            List<string> woundDetails = new();
            if (anatomy.Heart) woundDetails.Add("Heart");
            if (anatomy.Brain) woundDetails.Add("Brain");
            if (anatomy.CervicalSpine) woundDetails.Add("Cervical spine");
            if (anatomy.ThoracicSpine) woundDetails.Add("Thoracic spine");
            if (anatomy.Ribcage) woundDetails.Add("Ribcage");
            if (anatomy.Skull) woundDetails.Add("Skull");
            if (anatomy.LimbBone) woundDetails.Add("Bone");
            if (passedThrough) woundDetails.Add("Exit wound");

            string details = woundDetails.Count > 0
                ? $" ({string.Join(", ", woundDetails)})"
                : string.Empty;
            string depthDescription = depth > 0f
                ? $"{depth * 100f:F0}cm deep"
                : "Depth unknown";
            return $"{bodyPartName}{details} {depthDescription}";
        }

        private static void UpdateCalloutLine(
            RectTransform line, Vector2 start, Vector2 end)
        {
            Vector2 direction = end - start;
            line.anchoredPosition = start;
            line.sizeDelta = new Vector2(direction.magnitude, 1.5f);
            line.localRotation = Quaternion.Euler(
                0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        private static IEnumerator CreateMarkersWhenModelReady(
            MonoBehaviour coroutineHost,
            GameObject activeRoot,
            PlayerModelView modelView,
            TextMeshProUGUI originalLabel,
            Profile activeProfile,
            DamageHistory damageHistory)
        {
            float timeoutSeconds = 10f;

            while (!modelView.LoadingComplete && timeoutSeconds > 0f)
            {
                timeoutSeconds -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (!modelView.LoadingComplete)
            {
                TraumaLog.Warning(
                    "[DeathScreenHitMarkers] Player model did not finish loading within 10 seconds");
                yield break;
            }

            yield return null;

            Dictionary<EBodyPart, Transform> anchorsByBodyPart =
                BodyPartAnchorResolver.Resolve(modelView);

            BodyPartAnchorResolver.LogResolution(modelView, anchorsByBodyPart);

            ModelPreview modelPreview =
                FindModelPreview(modelView, anchorsByBodyPart);

            if (modelPreview == null)
            {
                TraumaLog.Warning(
                    "[DeathScreenHitMarkers] Could not match the model camera to a RawImage preview");
                yield break;
            }

            List<BodyPartMarker> bodyPartMarkers =
                CreateBodyPartMarkers(
                    activeProfile,
                    damageHistory,
                    anchorsByBodyPart,
                    modelPreview);

            RectTransform trajectoryRoot = new GameObject("WoundTrajectories", typeof(RectTransform))
                .GetComponent<RectTransform>();
            trajectoryRoot.SetParent(modelPreview.Container, false);
            trajectoryRoot.anchorMin = Vector2.zero;
            trajectoryRoot.anchorMax = Vector2.one;
            trajectoryRoot.offsetMin = trajectoryRoot.offsetMax = Vector2.zero;
            List<StaticHitCallout> trajectoryCallouts = CreateStaticHitCallouts(
                activeProfile, anchorsByBodyPart, trajectoryRoot, modelView.transform,
                includeLabels: false);

            if (bodyPartMarkers.Count == 0)
            {
                TraumaLog.Warning(
                    "[DeathScreenHitMarkers] No body-part markers were created");
                yield break;
            }

            TraumaLog.Info(
                $"[DeathScreenHitMarkers] Created {bodyPartMarkers.Count} body-part " +
                $"labels and {trajectoryCallouts.Count} trajectory callouts");

            if (originalLabel != null)
                originalLabel.gameObject.SetActive(false);

            DeathScreenAnatomyGraphic anatomy = DeathScreenAnatomyGraphic.Create(
                modelPreview.Container, modelView.transform, modelPreview.Camera, modelPreview.Image);

            anatomy.UpdateWounds = () =>
            {
                if (activeRoot == null || !activeRoot.activeInHierarchy) return;
                UpdateMarkerPositions(modelPreview, bodyPartMarkers);
                trajectoryRoot.gameObject.SetActive(Plugin.ShouldShowInspectionTrajectories.Value);
                if (Plugin.ShouldShowInspectionTrajectories.Value)
                    UpdateStaticHitCallouts(modelPreview, trajectoryCallouts,
                        Plugin.ShouldHideInspectionBackHits.Value);
            };
        }
        private static ModelPreview FindModelPreview(
            PlayerModelView modelView,
            IReadOnlyDictionary<EBodyPart, Transform> anchorsByBodyPart)
        {
            if (anchorsByBodyPart.Count == 0)
                return null;

            Transform reference =
                anchorsByBodyPart.TryGetValue(EBodyPart.Chest, out Transform chestAnchor)
                    ? chestAnchor
                    : anchorsByBodyPart.First().Value;

            RawImage[] images = modelView.transform.parent
                .GetComponentsInChildren<RawImage>(true);

            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null ||
                    camera.targetTexture == null ||
                    !camera.gameObject.scene.IsValid())
                    continue;

                Vector3 viewport =
                    camera.WorldToViewportPoint(reference.position);

                if (viewport.z <= 0f ||
                    viewport.x < 0f || viewport.x > 1f ||
                    viewport.y < 0f || viewport.y > 1f)
                    continue;

                RawImage image = images.FirstOrDefault(x =>
                    x.texture == camera.targetTexture ||
                    x.mainTexture == camera.targetTexture);

                if (image == null)
                    continue;

                TraumaLog.Info(
                    $"[DeathScreenHitMarkers] Matched camera '{camera.name}' to preview " +
                    $"'{image.name}' ({image.rectTransform.rect.width:F0}x" +
                    $"{image.rectTransform.rect.height:F0})");

                Transform previousMarkerContainer =
                    image.rectTransform.Find("TraumaCoreHitMarkers");

                if (previousMarkerContainer != null)
                    UnityEngine.Object.Destroy(previousMarkerContainer.gameObject);

                GameObject container =
                    new GameObject(
                        "TraumaCoreHitMarkers",
                        typeof(RectTransform));

                container.transform.SetParent(
                    image.rectTransform,
                    false);

                container.transform.SetAsLastSibling();

                RectTransform markerContainer =
                    container.GetComponent<RectTransform>();

                markerContainer.anchorMin = Vector2.zero;
                markerContainer.anchorMax = Vector2.one;
                markerContainer.offsetMin = Vector2.zero;
                markerContainer.offsetMax = Vector2.zero;

                EnableModelRotation(markerContainer, modelView);

                return new ModelPreview
                {
                    Camera = camera,
                    Image = image,
                    Container = markerContainer
                };
            }

            return null;
        }

        private static void EnableModelRotation(
            RectTransform inputArea,
            PlayerModelView modelView)
        {
            if (inputArea == null || modelView?.ModelPlayerPoser == null)
                return;

            Image inputSurface = inputArea.gameObject.AddComponent<Image>();
            inputSurface.color = Color.clear;
            inputSurface.raycastTarget = true;

            DragTrigger dragTrigger = inputArea.gameObject.AddComponent<DragTrigger>();
            XCoordRotation rotation = inputArea.gameObject.AddComponent<XCoordRotation>();
            rotation.Init(modelView.ModelPlayerPoser.transform);
            dragTrigger.onDrag += pointerData =>
            {
                if (pointerData.button == PointerEventData.InputButton.Left)
                    rotation.Rotate(pointerData.delta.x);
            };
            TraumaLog.Info(
                "[DeathScreenHitMarkers] Enabled left-drag model rotation");
        }

        private static List<BodyPartMarker> CreateBodyPartMarkers(
            Profile activeProfile,
            DamageHistory damageHistory,
            IReadOnlyDictionary<EBodyPart, Transform> anchorsByBodyPart,
            ModelPreview modelPreview)
        {
            List<BodyPartMarker> markers = new List<BodyPartMarker>();

            foreach (EBodyPart bodyPart in BodyPartAnchorResolver.BodyParts)
            {
                List<DamageStats> bodyPartDamageHistory = null;
                damageHistory?.BodyParts?.TryGetValue(
                    bodyPart,
                    out bodyPartDamageHistory);
                bool hasRecordedDamage =
                    DeathScreenDamageTracker.TryGetRecordedDamage(
                        activeProfile,
                        bodyPart,
                        out _);

                int historyEntries = bodyPartDamageHistory?.Count ?? 0;
                bool hasAnchor = anchorsByBodyPart.TryGetValue(
                    bodyPart,
                    out Transform bodyPartAnchor);
                TraumaLog.Info(
                    $"[DeathScreenHitMarkers] {bodyPart}: historyEntries={historyEntries}, " +
                    $"recorded={hasRecordedDamage}, " +
                    $"anchor={(hasAnchor ? bodyPartAnchor.name : "missing")}");

                bool shouldAlwaysShowLabel =
                    bodyPart == EBodyPart.Head ||
                    bodyPart == EBodyPart.LeftLeg ||
                    bodyPart == EBodyPart.RightLeg;
                bool shouldCreateLabel = historyEntries > 0 ||
                    hasRecordedDamage ||
                    shouldAlwaysShowLabel;
                if (!shouldCreateLabel || !hasAnchor)
                {
                    TraumaLog.Warning(
                        $"[DeathScreenHitMarkers] Skipped {bodyPart}: " +
                        $"{(!hasAnchor ? "anchor missing" : "no damage history or recording")}");
                    continue;
                }

                EvaluateDamageSummary(
                    activeProfile,
                    bodyPart,
                    bodyPartDamageHistory ?? new List<DamageStats>(),
                    out int directHits,
                    out float directDamage,
                    out float bleedDamage,
                    out float bleedDurationSeconds,
                    out float bleedDamagePerSecond,
                    out EDamageType directType,
                    out bool hasHeavyBleed);

                BodyPartMarker marker = new()
                {
                    Bone = bodyPartAnchor,
                    BodyPart = bodyPart,
                    MaximumHealth = FindMaximumBodyPartHealth(activeProfile, bodyPart),
                    DirectHits = directHits,
                    DirectDamage = directDamage,
                    BleedDamage = bleedDamage,
                    BleedDurationSeconds = bleedDurationSeconds,
                    BleedDamagePerSecond = bleedDamagePerSecond,
                    DirectType = directType,
                    HasHeavyBleed = hasHeavyBleed,
                    LatestImpactSequence =
                        DeathScreenDamageTracker.FindLatestImpactSequence(activeProfile),
                    Color = ResolveBodyPartColor(bodyPart),
                    LeftSide = IsLabelOnLeft(bodyPart)
                };

                if (DeathScreenDamageTracker.TryGetRecordedDamage(
                    activeProfile,
                    bodyPart,
                    out DeathScreenDamageTracker.BodyPartDamageRecord recordedDamage))
                {
                    foreach (DeathScreenDamageTracker.BulletImpactRecord impact in recordedDamage.Impacts)
                        if (impact.HasWoundTrajectory)
                            marker.RecordedImpacts.Add(impact);
                }

                CreateMarkerVisuals(modelPreview.Container, marker);
                markers.Add(marker);
            }

            return markers;
        }

        private static void MoveLevelIntoCharacterName(
            SessionResultExitStatus screen,
            Profile profile,
            ESideType side)
        {
            if (screen._levelPanel != null)
                screen._levelPanel.gameObject.SetActive(false);

            if (side != ESideType.Pmc ||
                profile?.Info == null ||
                screen._namePanel == null ||
                screen._namePanel._name == null)
                return;

            screen._namePanel._name.text =
                $"{profile.GetCorrectedNickname()} (Lv. {profile.Info.Level})";
        }

        private static void EvaluateDamageSummary(
            Profile profile,
            EBodyPart bodyPart,
            List<DamageStats> bodyPartDamageHistory,
            out int directHits,
            out float directDamage,
            out float bleedDamage,
            out float bleedDurationSeconds,
            out float bleedDamagePerSecond,
            out EDamageType directType,
            out bool hasHeavyBleed)
        {
            if (DeathScreenDamageTracker.TryGetRecordedDamage(
                profile,
                bodyPart,
                out DeathScreenDamageTracker.BodyPartDamageRecord recordedDamage))
            {
                directHits = recordedDamage.DirectHits;
                directDamage = recordedDamage.DirectDamage;
                bleedDamage = recordedDamage.BleedDamage;
                bleedDurationSeconds = recordedDamage.BleedDurationSeconds;
                bleedDamagePerSecond = recordedDamage.AverageBleedDamagePerSecond;
                directType = recordedDamage.LastDirectType;
                hasHeavyBleed = recordedDamage.HasHeavyBleed;
                return;
            }

            List<DamageStats> directDamageHistory = bodyPartDamageHistory
                .Where(x => !DeathScreenDamageTracker.IsBleeding(x.Type))
                .ToList();
            List<DamageStats> bleedDamageHistory = bodyPartDamageHistory
                .Where(x => DeathScreenDamageTracker.IsBleeding(x.Type))
                .ToList();

            directHits = directDamageHistory
                .Where(x => x.Type.IsWeaponInduced())
                .Sum(x => Mathf.Max(1, Mathf.RoundToInt(x.ImpactsCount)));
            directDamage = directDamageHistory.Sum(x => x.Amount);
            bleedDamage = bleedDamageHistory.Sum(x => x.Amount);
            bleedDurationSeconds = 0f;
            bleedDamagePerSecond = 0f;
            directType = directDamageHistory.Count > 0
                ? directDamageHistory.OrderByDescending(x => x.Amount).First().Type
                : EDamageType.Undefined;
            hasHeavyBleed = bleedDamageHistory.Any(
                x => x.Type == EDamageType.HeavyBleeding);
        }

        private static float FindMaximumBodyPartHealth(Profile profile, EBodyPart bodyPart)
        {
            if (profile?.Health?.BodyParts != null &&
                profile.Health.BodyParts.TryGetValue(bodyPart, out Profile.HealthInfo.BodyPartInfo health))
                return health.Health.Maximum;

            return 0f;
        }

        private static void CreateMarkerVisuals(
            RectTransform container,
            BodyPartMarker marker)
        {
            GameObject labelObject =
                new GameObject(
                    $"Label_{marker.BodyPart}",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));

            labelObject.transform.SetParent(container, false);

            marker.Label =
                labelObject.GetComponent<RectTransform>();

            marker.Label.anchorMin =
                marker.Label.anchorMax =
                new Vector2(0.5f, 0.5f);

            marker.Label.sizeDelta =
                new Vector2(LabelWidthPixels, LabelHeightPixels);

            marker.Text =
                labelObject.GetComponent<TextMeshProUGUI>();

            marker.Text.fontSize = FontSizePoints;
            marker.Text.enableAutoSizing = true;
            marker.Text.fontSizeMin = 10f;
            marker.Text.fontSizeMax = FontSizePoints;
            marker.Text.fontStyle = FontStyles.Bold;
            marker.Text.color = Color.white;
            marker.Text.enableWordWrapping = false;
            marker.Text.raycastTarget = false;
            marker.Text.overflowMode = TextOverflowModes.Ellipsis;
            marker.Text.lineSpacing = -8f;

            Outline outline = labelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            string bodyColor = ColorUtility.ToHtmlStringRGB(marker.Color);
            string directColor = ColorUtility.ToHtmlStringRGB(ResolveDamageColor(marker.DirectType));
            string bleedColor = ColorUtility.ToHtmlStringRGB(
                ResolveDamageColor(marker.HasHeavyBleed
                    ? EDamageType.HeavyBleeding
                    : EDamageType.LightBleeding));
            string health = marker.MaximumHealth > 0f
                ? $" / {marker.MaximumHealth:F0} HP"
                : string.Empty;
            string bleedRate = marker.BleedDurationSeconds > 0f
                ? $" over {marker.BleedDurationSeconds:F1}s ({marker.BleedDamagePerSecond:F1}/s)"
                : string.Empty;

            marker.Text.text =
                $"<color=#{bodyColor}>{marker.BodyPart}</color>{health}\n" +
                $"<color=#{directColor}>{marker.DirectHits} direct hit{(marker.DirectHits == 1 ? string.Empty : "s")}: {marker.DirectDamage:F0} damage</color>\n" +
                $"<color=#{bleedColor}>Bleed drain: {marker.BleedDamage:F0}{bleedRate}</color>";
        }

        private static BulletImpactVisual CreateBulletImpactVisual(
            RectTransform container,
            BodyPartMarker marker,
            DeathScreenDamageTracker.BulletImpactRecord impact,
            int index)
        {
            Color color = ResolveImpactColor(impact);
            GameObject dotObject = new GameObject(
                $"Hit_{marker.BodyPart}_{index + 1}",
                typeof(RectTransform));
            dotObject.transform.SetParent(container, false);

            RectTransform dot = dotObject.GetComponent<RectTransform>();
            dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
            bool isLastHit = impact.Sequence == marker.LatestImpactSequence;
            dot.sizeDelta = Vector2.one *
                (isLastHit ? BulletHoleSizePixels * 1.15f : BulletHoleSizePixels);

            BulletHoleGraphic circle = dotObject.AddComponent<BulletHoleGraphic>();
            circle.color = color;
            circle.raycastTarget = false;

            GameObject coreObject = new GameObject(
                "BulletHoleCore",
                typeof(RectTransform));
            coreObject.transform.SetParent(dot, false);
            RectTransform core = coreObject.GetComponent<RectTransform>();
            core.anchorMin = core.anchorMax = new Vector2(0.5f, 0.5f);
            core.sizeDelta = Vector2.one * (BulletHoleSizePixels * 0.45f);
            BulletHoleGraphic coreCircle = coreObject.AddComponent<BulletHoleGraphic>();
            coreCircle.color = new Color(0.025f, 0.025f, 0.025f, 1f);
            coreCircle.raycastTarget = false;

            if (isLastHit)
            {
                GameObject xObject = new GameObject(
                    "LastHitX",
                    typeof(RectTransform),
                    typeof(LastHitXGraphic));
                xObject.transform.SetParent(dot, false);
                RectTransform xRect = xObject.GetComponent<RectTransform>();
                xRect.anchorMin = Vector2.zero;
                xRect.anchorMax = Vector2.one;
                xRect.offsetMin = Vector2.one * 1.5f;
                xRect.offsetMax = Vector2.one * -1.5f;
                LastHitXGraphic xGraphic = xObject.GetComponent<LastHitXGraphic>();
                xGraphic.color = Color.white;
                xGraphic.raycastTarget = false;
            }

            return new BulletImpactVisual
            {
                LocalPoint = impact.LocalPoint,
                Dot = dot
            };
        }

        private static void UpdateMarkerPositions(
            ModelPreview modelPreview,
            List<BodyPartMarker> bodyPartMarkers)
        {
            Rect bounds = modelPreview.Container.rect;
            float responsiveLabelWidthPixels = Mathf.Clamp(
                bounds.width * LabelWidthFraction,
                MinimumLabelWidthPixels,
                LabelWidthPixels);
            bool labelWidthChanged = !Mathf.Approximately(
                modelPreview.LabelWidthPixels,
                responsiveLabelWidthPixels);
            modelPreview.LabelWidthPixels = responsiveLabelWidthPixels;
            modelPreview.VisibleLeftMarkers.Clear();
            modelPreview.VisibleRightMarkers.Clear();

            foreach (BodyPartMarker marker in bodyPartMarkers)
            {
                if (labelWidthChanged)
                    marker.Label.sizeDelta = new Vector2(
                        responsiveLabelWidthPixels,
                        LabelHeightPixels);

                Vector3 viewport =
                    modelPreview.Camera.WorldToViewportPoint(
                        marker.Bone.position);

                bool visible = viewport.z > 0f;

                if (!marker.HasLoggedProjection)
                {
                    marker.HasLoggedProjection = true;
                    TraumaLog.Info(
                        $"[DeathScreenHitMarkers] {marker.BodyPart} projection: " +
                        $"viewport=({viewport.x:F3}, {viewport.y:F3}, {viewport.z:F3}), " +
                        $"labelVisible={visible}, impacts={marker.Impacts.Count}, " +
                        $"lane={(marker.LeftSide ? "left" : "right")}");
                }

                if (marker.Visible != visible)
                    marker.Label.gameObject.SetActive(visible);
                marker.Visible = visible;

                if (!visible)
                {
                    foreach (BulletImpactVisual impact in marker.Impacts)
                        impact.Dot.gameObject.SetActive(false);
                    continue;
                }

                if (marker.LeftSide)
                    modelPreview.VisibleLeftMarkers.Add(marker);
                else
                    modelPreview.VisibleRightMarkers.Add(marker);

                foreach (BulletImpactVisual impact in marker.Impacts)
                {
                    Vector3 impactViewport = modelPreview.Camera.WorldToViewportPoint(
                        marker.Bone.TransformPoint(impact.LocalPoint));
                    bool impactVisible = impactViewport.z > 0f &&
                        impactViewport.x >= 0f && impactViewport.x <= 1f &&
                        impactViewport.y >= 0f && impactViewport.y <= 1f;
                    if (impact.Visible != impactVisible)
                    {
                        impact.Visible = impactVisible;
                        impact.Dot.gameObject.SetActive(impactVisible);
                    }
                    if (!impactVisible)
                        continue;

                    impact.Point = ConvertViewportToLocal(
                        modelPreview.Image,
                        impactViewport);
                    impact.Dot.anchoredPosition = impact.Point;
                }

            }

            UpdateLabelLane(
                modelPreview.VisibleLeftMarkers,
                bounds,
                true);

            UpdateLabelLane(
                modelPreview.VisibleRightMarkers,
                bounds,
                false);
        }

        private static void UpdateLabelLane(
            List<BodyPartMarker> markers,
            Rect bounds,
            bool isLeftLane)
        {
            if (markers.Count == 0)
                return;

            markers.Sort(
                (a, b) => ResolveLabelLaneOrder(b.BodyPart).CompareTo(
                    ResolveLabelLaneOrder(a.BodyPart)));

            float minY =
                bounds.yMin + LabelHeightPixels * 0.5f;

            float maxY =
                bounds.yMax - LabelHeightPixels * 0.5f;

            float available =
                maxY - minY;

            float gap =
                markers.Count > 1
                    ? Mathf.Min(
                        LabelHeightPixels + LabelSpacingPixels,
                        available / (markers.Count - 1))
                    : 0f;

            for (int i = 0; i < markers.Count; i++)
            {
                float y =
                    Mathf.Clamp(
                        Mathf.Lerp(
                            bounds.yMax,
                            bounds.yMin,
                            ResolveLabelVerticalPosition(markers[i].BodyPart)),
                        minY,
                        maxY);

                if (i > 0)
                {
                    y = Mathf.Max(
                        y,
                        markers[i - 1].LabelYPixels + gap);
                }

                markers[i].LabelYPixels = y;
            }

            if (markers[^1].LabelYPixels > maxY)
            {
                markers[^1].LabelYPixels = maxY;

                for (int i = markers.Count - 2; i >= 0; i--)
                {
                    markers[i].LabelYPixels =
                        Mathf.Min(
                            markers[i].LabelYPixels,
                            markers[i + 1].LabelYPixels - gap);
                }
            }

            foreach (BodyPartMarker marker in markers)
            {
                float x;
                marker.LabelYPixels = Mathf.Clamp(
                    marker.LabelYPixels,
                    minY,
                    maxY);

                if (isLeftLane)
                {
                    marker.Label.pivot =
                        new Vector2(1f, 0.5f);

                    marker.Text.alignment =
                        TextAlignmentOptions.Right;

                    x = Mathf.Lerp(
                        bounds.xMin - LabelModelClearancePixels,
                        bounds.center.x,
                        LabelLaneCenterPull);
                }
                else
                {
                    marker.Label.pivot =
                        new Vector2(0f, 0.5f);

                    marker.Text.alignment =
                        TextAlignmentOptions.Left;

                    x = Mathf.Lerp(
                        bounds.xMax + LabelModelClearancePixels,
                        bounds.center.x,
                        LabelLaneCenterPull);
                }

                marker.Label.anchoredPosition =
                    new Vector2(
                        x,
                        marker.LabelYPixels);

                if (!marker.HasLoggedLabelPosition)
                {
                    marker.HasLoggedLabelPosition = true;
                    TraumaLog.Info(
                        $"[DeathScreenHitMarkers] {marker.BodyPart} label: " +
                        $"position=({x:F1}, {marker.LabelYPixels:F1}), " +
                        $"verticalBounds=({minY:F1}, {maxY:F1})");
                }

            }
        }

        private static Vector2 ConvertViewportToLocal(RawImage image, Vector3 viewport) =>
            AnatomyOverlayRenderer.ConvertPreviewPoint(image.rectTransform.rect, image.uvRect, viewport);
        private static Color ResolveBodyPartColor(EBodyPart bodyPart) =>
            bodyPart switch
            {
                EBodyPart.Head => new Color(1f, 0.25f, 0.65f),
                EBodyPart.Chest => new Color(0.2f, 0.85f, 1f),
                EBodyPart.Stomach => new Color(1f, 0.72f, 0.15f),
                EBodyPart.LeftArm => new Color(0.35f, 1f, 0.45f),
                EBodyPart.RightArm => new Color(0.65f, 1f, 0.25f),
                EBodyPart.LeftLeg => new Color(0.65f, 0.45f, 1f),
                EBodyPart.RightLeg => new Color(0.35f, 0.55f, 1f),
                _ => Color.white
            };

        private static bool IsLabelOnLeft(EBodyPart bodyPart) =>
            bodyPart == EBodyPart.Chest ||
            bodyPart == EBodyPart.RightArm ||
            bodyPart == EBodyPart.RightLeg;

        private static int ResolveLabelLaneOrder(EBodyPart bodyPart) =>
            bodyPart switch
            {
                EBodyPart.Head => 0,
                EBodyPart.LeftArm => 1,
                EBodyPart.RightArm => 1,
                EBodyPart.Chest => 2,
                EBodyPart.Stomach => 3,
                EBodyPart.LeftLeg => 4,
                EBodyPart.RightLeg => 4,
                _ => 5
            };

        private static float ResolveLabelVerticalPosition(EBodyPart bodyPart) =>
            bodyPart switch
            {
                EBodyPart.Head => 0.30f,
                EBodyPart.LeftArm => 0.40f,
                EBodyPart.RightArm => 0.40f,
                EBodyPart.Chest => 0.42f,
                EBodyPart.Stomach => 0.55f,
                EBodyPart.LeftLeg => 0.75f,
                EBodyPart.RightLeg => 0.75f,
                _ => 0.50f
            };

        private static Color ResolveImpactColor(
            DeathScreenDamageTracker.BulletImpactRecord impact)
        {
            Color shallowColor = new Color(0.15f, 1f, 0.2f);
            Color fullPenetrationColor = new Color(1f, 0.12f, 0.08f);
            if (impact.PassedThrough)
                return fullPenetrationColor;

            const float shallowDepth = 0.1f;
            float fullPenetrationDepth = Mathf.Max(
                shallowDepth + 0.001f,
                impact.Penetration.ReferenceThickness);
            float penetrationProgress = Mathf.InverseLerp(
                shallowDepth,
                fullPenetrationDepth,
                impact.TraveledDepth);
            return Color.Lerp(
                shallowColor,
                fullPenetrationColor,
                penetrationProgress);
        }

        private static Color ResolveDamageColor(EDamageType type) =>
            type switch
            {
                EDamageType.Bullet =>
                    new Color(1f, 0.2f, 0.2f),

                EDamageType.HeavyBleeding =>
                    new Color(1f, 0.3f, 0.3f),

                EDamageType.LightBleeding =>
                    new Color(1f, 0.5f, 0.2f),

                EDamageType.Melee =>
                    new Color(1f, 1f, 0.2f),

                EDamageType.GrenadeFragment =>
                    new Color(1f, 0.2f, 1f),

                EDamageType.Explosion =>
                    new Color(1f, 0.5f, 0f),

                _ => Color.white
            };

    }
}
