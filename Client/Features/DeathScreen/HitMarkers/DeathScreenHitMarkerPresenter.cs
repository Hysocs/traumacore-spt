using System.Collections;
using System.Collections.Generic;
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
                    modelView,
                    screen._bodyPartLabel,
                    activeProfile,
                    damageHistory));
        }

        private static IEnumerator CreateMarkersWhenModelReady(
            SessionResultExitStatus screen,
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

            if (bodyPartMarkers.Count == 0)
            {
                TraumaLog.Warning(
                    "[DeathScreenHitMarkers] No body-part markers were created");
                yield break;
            }

            TraumaLog.Info(
                $"[DeathScreenHitMarkers] Created {bodyPartMarkers.Count} body-part labels");

            if (originalLabel != null)
                originalLabel.gameObject.SetActive(false);

            while (screen != null &&
                   modelPreview.Image != null &&
                   modelPreview.Camera != null &&
                   modelPreview.Container != null &&
                   screen.gameObject.activeInHierarchy)
            {
                UpdateMarkerPositions(modelPreview, bodyPartMarkers);
                yield return null;
            }
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
                damageHistory.BodyParts.TryGetValue(
                    bodyPart,
                    out List<DamageStats> bodyPartDamageHistory);
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
            for (int i = 0; i < marker.RecordedImpacts.Count; i++)
                marker.Impacts.Add(CreateBulletImpactVisual(
                    container,
                    marker,
                    marker.RecordedImpacts[i],
                    i));

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
            Color color = ResolveImpactColor(marker.Color, impact.DamageType, index);
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

        private static Vector2 ConvertViewportToLocal(
            RawImage image,
            Vector3 viewport)
        {
            Rect rect = image.rectTransform.rect;
            Rect uv = image.uvRect;

            float x =
                (viewport.x - uv.x) / uv.width;

            float y =
                (viewport.y - uv.y) / uv.height;

            return new Vector2(
                Mathf.Lerp(
                    rect.xMin,
                    rect.xMax,
                    x),
                Mathf.Lerp(
                    rect.yMin,
                    rect.yMax,
                    y));
        }

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
            Color bodyPartColor,
            EDamageType damageType,
            int index)
        {
            Color baseColor = Color.Lerp(ResolveDamageColor(damageType), bodyPartColor, 0.4f);
            Color.RGBToHSV(baseColor, out float hue, out float saturation, out float value);
            hue = Mathf.Repeat(hue + index * 0.11f, 1f);
            return Color.HSVToRGB(hue, Mathf.Max(0.7f, saturation), Mathf.Max(0.9f, value));
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
