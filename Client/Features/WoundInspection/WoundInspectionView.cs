using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using EFT;
using EFT.CameraControl;
using EFT.InputSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using TMPro;
using TraumaCore.Features.DeathScreen.HitMarkers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace TraumaCore.Features.WoundInspection
{
    internal sealed class WoundInspectionView : UIScreen
    {
        private const float ForgivingSelectionRadius = 95f;
        private const float RagdollActivationDuration = 0.5f;
        private const float GrabSpring = 180f;
        private const float GrabDamping = 18f;
        private const float MaximumGrabAcceleration = 260f;
        private const float GrabDepthPerScrollStep = 0.3f;
        private const float EmptyHandsRetryDelay = 0.25f;
        private const float EmptyHandsRequestTimeout = 1.5f;

        private sealed class BodyPose
        {
            internal RigidbodySpawner Spawner;
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        private sealed class BodyActivation
        {
            internal Rigidbody Body;
            internal float Drag;
            internal float AngularDrag;
            internal float MaximumDepenetrationVelocity;
            internal RigidbodyConstraints Constraints;
        }

        private static WoundInspectionView _instance;
        private readonly List<Renderer> _anchorRenderers = new();
        private readonly List<Material> _overlayMaterials = new();
        private readonly List<Mesh> _overlayMeshes = new();
        private readonly List<BodyActivation> _bodyActivations = new();
        private readonly RagdollJointStability _jointStability = new();
        private Camera _camera;
        private float _originalFieldOfView;
        private bool _hasCapturedFieldOfView;
        private CorpseRagdoll _ragdoll;
        private Corpse _corpse;
        private InspectionOrganGraphic _organGraphic;
        private Rigidbody _draggedBody;
        private Vector3 _draggedLocalPoint;
        private Vector2 _dragPointerPosition;
        private float _dragDepth;
        private Player _localPlayer;
        private Item _previousHandsItem;
        private bool _shouldRestoreHands;
        private bool _shouldEnforceEmptyHands;
        private bool _isEmptyHandsRequestPending;
        private float _nextEmptyHandsRequestTime;
        private float _emptyHandsRequestExpires;
        private bool _isRagdollReady;
        private bool _originalPutToSleep;
        private readonly Dictionary<Toggle, ConfigEntry<bool>> _settingsByToggle = new();
        private CorpseWeaponLink.DetachedWeapon _detachedWeapon;
        private bool _isInputNodeRegistered;
        private bool _isUiEventSystemEnabled;

        internal static void Open(GamePlayerOwner owner, Corpse corpse, Player corpsePlayer)
        {
            if (!Plugin.EnableWoundInspection.Value || owner == null ||
                corpse == null || corpsePlayer?.Profile == null)
                return;
            owner.ClearInteractionState();
            if (_instance != null)
                DestroyImmediate(_instance.gameObject);
            GameObject root = new GameObject(
                "TraumaCoreWoundInspection", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(WoundInspectionView));
            _instance = root.GetComponent<WoundInspectionView>();
            try
            {
                EftScreenManager.Instance._inputNode.Add(_instance);
                _instance._isInputNodeRegistered = true;
                UIEventSystem.Instance.Enable();
                _instance._isUiEventSystemEnabled = true;
                _instance.Build(GamePlayerOwner.MyPlayer, corpse, corpsePlayer);
            }
            catch (System.Exception exception)
            {
                TraumaLog.Error(
                    "[WoundInspection] Failed to open inspection: " + exception);
                if (_instance != null)
                    DestroyImmediate(_instance.gameObject);
            }
        }

        internal static bool IsInspecting(Corpse corpse)
        {
            return _instance != null && _instance._corpse == corpse;
        }

        private void Build(Player localPlayer, Corpse corpse, Player corpsePlayer)
        {
            ConfigureCanvas();
            TMP_Text textTemplate = Resources.FindObjectsOfTypeAll<TMP_Text>()
                .FirstOrDefault(text => text != null && text.font != null);
            CreateTitle(textTemplate);
            CreateCloseButton(textTemplate);
            CreateOverlayToggles(textTemplate);
            RawImage inputSurface = CreateInputSurface();

            _camera = CameraManager.Instance?.Camera ?? Camera.main;
            if (_camera == null)
            {
                TraumaLog.Warning("[WoundInspection] EFT world camera was unavailable");
                Close();
                return;
            }
            _originalFieldOfView = _camera.fieldOfView;
            _hasCapturedFieldOfView = true;
            _corpse = corpse;
            _ragdoll = corpse.Ragdoll;
            if (!CanActivateRagdoll())
            {
                TraumaLog.Warning("[WoundInspection] Corpse has no usable EFT ragdoll");
                Close();
                return;
            }

            CorpseRagdollSettlement.Cancel(corpse);
            _detachedWeapon = CorpseWeaponLink.Detach(_ragdoll);
            ActivateNativeRagdoll();
            UnequipLocalPlayerHands(localPlayer);
            CreateOrganOverlay(corpsePlayer);
            LogCorpseAnatomyPose(corpsePlayer);
            CreateAnchorOverlay();
            AddCorpseInteraction(inputSurface);
            AddZoom(inputSurface);
            DeathScreenHitMarkerPresenter.ShowStaticCorpsePreview(
                this, gameObject, _camera, inputSurface, corpse.transform,
                corpsePlayer.Profile,
                () => Plugin.ShouldShowInspectionTrajectories.Value,
                () => Plugin.ShouldHideInspectionBackHits.Value);
            TraumaLog.Info(
                $"[WoundInspection] Inspecting live EFT corpse ragdoll with " +
                $"{_ragdoll._rigidbodySpawners.Length} bodies");
        }

        private void Update()
        {
            RequestEmptyHands();
            UpdateOverlaySettings();
        }

        private bool CanActivateRagdoll()
        {
            return _ragdoll != null && _ragdoll._owner != null &&
                _ragdoll._rigidbodySpawners != null &&
                _ragdoll._rigidbodySpawners.Length > 0 &&
                _ragdoll._jointSpawners != null;
        }

        private void ActivateNativeRagdoll()
        {
            List<BodyPose> poses = CaptureBodyPoses();
            _originalPutToSleep = _ragdoll._putToSleep;
            bool hasMissingBodies = _ragdoll._rigidbodySpawners
                .Any(spawner => spawner == null || spawner.Rigidbody == null);
            if (_ragdoll._isPhysicsDone || hasMissingBodies)
            {
                _ragdoll._putToSleep = false;
                try
                {
                    _ragdoll.Start();
                }
                finally
                {
                    _ragdoll._putToSleep = _originalPutToSleep;
                }
                LockBodyPoses(poses);
                _jointStability.Capture(_ragdoll._jointSpawners);
                StartCoroutine(BlendRagdollActivation());
                TraumaLog.Info("[WoundInspection] Reactivated native EFT corpse ragdoll");
                return;
            }
            _ragdoll.WakeUp();
            LockBodyPoses(poses);
            _jointStability.Capture(_ragdoll._jointSpawners);
            StartCoroutine(BlendRagdollActivation());
            TraumaLog.Info("[WoundInspection] Kept active native EFT corpse ragdoll awake");
        }

        private List<BodyPose> CaptureBodyPoses()
        {
            return _ragdoll._rigidbodySpawners
                .Where(spawner => spawner != null)
                .Select(spawner => new BodyPose
                {
                    Spawner = spawner,
                    Position = spawner.transform.position,
                    Rotation = spawner.transform.rotation
                }).ToList();
        }

        private void LockBodyPoses(IEnumerable<BodyPose> poses)
        {
            foreach (BodyPose pose in poses)
            {
                if (pose.Spawner == null)
                    continue;
                pose.Spawner.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                Rigidbody body = pose.Spawner.Rigidbody;
                if (body == null)
                    continue;
                body.position = pose.Position;
                body.rotation = pose.Rotation;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                _bodyActivations.Add(new BodyActivation
                {
                    Body = body,
                    Drag = body.drag,
                    AngularDrag = body.angularDrag,
                    MaximumDepenetrationVelocity = body.maxDepenetrationVelocity,
                    Constraints = body.constraints
                });
                body.constraints = RigidbodyConstraints.None;
                body.isKinematic = true;
                body.Sleep();
            }
            Physics.SyncTransforms();
        }

        private IEnumerator BlendRagdollActivation()
        {
            yield return null;
            foreach (BodyActivation activation in _bodyActivations)
            {
                if (activation.Body == null)
                    continue;
                activation.Body.drag = 18f;
                activation.Body.angularDrag = 18f;
                activation.Body.maxDepenetrationVelocity = 0.05f;
                activation.Body.isKinematic = false;
                activation.Body.WakeUp();
            }
            _jointStability.RepairExcessiveSeparation();

            float elapsed = 0f;
            while (elapsed < RagdollActivationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float blend = Mathf.Clamp01(elapsed / RagdollActivationDuration);
                foreach (BodyActivation activation in _bodyActivations)
                {
                    if (activation.Body == null)
                        continue;
                    activation.Body.drag = Mathf.Lerp(18f, activation.Drag, blend);
                    activation.Body.angularDrag = Mathf.Lerp(
                        18f, activation.AngularDrag, blend);
                    activation.Body.maxDepenetrationVelocity = Mathf.Lerp(
                        0.05f, activation.MaximumDepenetrationVelocity, blend);
                }
                _jointStability.RepairExcessiveSeparation();
                yield return null;
            }
            CompleteRagdollActivation();
            _isRagdollReady = true;
        }

        private void CompleteRagdollActivation()
        {
            foreach (BodyActivation activation in _bodyActivations)
            {
                if (activation.Body == null)
                    continue;
                activation.Body.isKinematic = false;
                activation.Body.drag = activation.Drag;
                activation.Body.angularDrag = activation.AngularDrag;
                activation.Body.maxDepenetrationVelocity =
                    activation.MaximumDepenetrationVelocity;
                activation.Body.constraints = RigidbodyConstraints.None;
            }
        }

        private void ConfigureCanvas()
        {
            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Image background = gameObject.AddComponent<Image>();
            background.color = Color.clear;
            background.raycastTarget = true;
        }

        private RawImage CreateInputSurface()
        {
            GameObject surface = new GameObject(
                "CorpseInteractionSurface", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(RawImage));
            surface.transform.SetParent(transform, false);
            RectTransform rect = (RectTransform)surface.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling();
            RawImage image = surface.GetComponent<RawImage>();
            image.color = Color.clear;
            image.raycastTarget = true;
            return image;
        }

        private void AddCorpseInteraction(RawImage inputSurface)
        {
            EventTrigger trigger = inputSurface.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerDown,
                eventData => CaptureDraggedBody((PointerEventData)eventData));
            AddTrigger(trigger, EventTriggerType.Drag,
                eventData => DriveDraggedBody((PointerEventData)eventData));
            AddTrigger(trigger, EventTriggerType.PointerUp, _ => ReleaseDraggedBody());
            AddTrigger(trigger, EventTriggerType.PointerExit, _ => ReleaseDraggedBody());
        }

        private static void AddTrigger(
            EventTrigger trigger, EventTriggerType eventType,
            UnityEngine.Events.UnityAction<BaseEventData> callback)
        {
            EventTrigger.Entry entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }

        private void CaptureDraggedBody(PointerEventData eventData)
        {
            _draggedBody = null;
            if (!_isRagdollReady)
                return;
            Ray ray = _camera.ScreenPointToRay(eventData.position);
            RaycastHit[] hits = Physics.RaycastAll(ray, 1000f)
                .OrderBy(candidate => candidate.distance).ToArray();
            foreach (RaycastHit hit in hits)
            {
                if (!_ragdoll.FindRigidbodySpawner(hit.collider, out RigidbodySpawner spawner) ||
                    spawner?.Rigidbody == null)
                    continue;
                SelectDraggedBody(spawner, hit.point, false);
                _dragPointerPosition = eventData.position;
                return;
            }

            RigidbodySpawner nearbySpawner = FindNearbyBody(eventData.position, hits);
            if (nearbySpawner != null)
            {
                SelectDraggedBody(nearbySpawner, nearbySpawner.transform.position, true);
                _dragPointerPosition = eventData.position;
            }
        }

        private RigidbodySpawner FindNearbyBody(
            Vector2 pointerPosition, IReadOnlyList<RaycastHit> hits)
        {
            if (hits.Count > 0)
            {
                Vector3 nearestHit = hits[0].point;
                RigidbodySpawner worldNearest = _ragdoll._rigidbodySpawners
                    .Where(spawner => spawner?.Rigidbody != null)
                    .OrderBy(spawner => Vector3.Distance(
                        spawner.transform.position, nearestHit))
                    .FirstOrDefault();
                if (worldNearest != null && Vector3.Distance(
                    worldNearest.transform.position, nearestHit) <= 0.55f)
                    return worldNearest;
            }

            return _ragdoll._rigidbodySpawners
                .Where(spawner => spawner?.Rigidbody != null)
                .Select(spawner => new
                {
                    Spawner = spawner,
                    ScreenPosition = _camera.WorldToScreenPoint(spawner.transform.position)
                })
                .Where(candidate => candidate.ScreenPosition.z > 0f)
                .Select(candidate => new
                {
                    candidate.Spawner,
                    Distance = Vector2.Distance(pointerPosition, candidate.ScreenPosition)
                })
                .Where(candidate => candidate.Distance <= ForgivingSelectionRadius)
                .OrderBy(candidate => candidate.Distance)
                .Select(candidate => candidate.Spawner)
                .FirstOrDefault();
        }

        private void SelectDraggedBody(
            RigidbodySpawner spawner, Vector3 worldPoint, bool isForgivingSelection)
        {
            _draggedBody = spawner.Rigidbody;
            _draggedLocalPoint = _draggedBody.transform.InverseTransformPoint(worldPoint);
            _dragPointerPosition = _camera.WorldToScreenPoint(worldPoint);
            _dragDepth = Mathf.Max(0.3f,
                Vector3.Dot(worldPoint - _camera.transform.position,
                    _camera.transform.forward));
            _ragdoll.WakeUp();
            TraumaLog.Info(
                $"[WoundInspection] Grabbed native body '{spawner.name}'" +
                (isForgivingSelection ? " using forgiving selection" : string.Empty));
        }

        private void DriveDraggedBody(PointerEventData eventData)
        {
            if (_draggedBody == null)
                return;
            _dragPointerPosition = eventData.position;
        }

        private void FixedUpdate()
        {
            if (!_isRagdollReady)
                return;

            if (_draggedBody == null)
            {
                _jointStability.RepairExcessiveSeparation();
                return;
            }

            Vector3 worldPoint = _draggedBody.transform.TransformPoint(_draggedLocalPoint);
            Ray pointerRay = _camera.ScreenPointToRay(_dragPointerPosition);
            Vector3 target = pointerRay.GetPoint(_dragDepth);
            Vector3 displacement = target - worldPoint;
            Vector3 pointVelocity = _draggedBody.GetPointVelocity(worldPoint);
            Vector3 acceleration = displacement * GrabSpring -
                pointVelocity * GrabDamping;
            acceleration = Vector3.ClampMagnitude(
                acceleration, MaximumGrabAcceleration);
            _ragdoll.WakeUp();
            _draggedBody.AddForceAtPosition(
                acceleration, worldPoint, ForceMode.Acceleration);
            _jointStability.RepairExcessiveSeparation();
        }

        private void ReleaseDraggedBody()
        {
            _draggedBody = null;
        }

        private void AddZoom(RawImage inputSurface)
        {
            EventTrigger trigger = inputSurface.GetComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.Scroll, eventData =>
            {
                PointerEventData pointer = (PointerEventData)eventData;
                if (_draggedBody != null)
                {
                    _dragDepth = Mathf.Clamp(
                        _dragDepth + pointer.scrollDelta.y * GrabDepthPerScrollStep,
                        0.3f, 30f);
                    return;
                }
                _camera.fieldOfView = Mathf.Clamp(
                    _camera.fieldOfView - pointer.scrollDelta.y * 2.5f,
                    Mathf.Max(18f, _originalFieldOfView * 0.4f), _originalFieldOfView);
            });
        }

        private void UnequipLocalPlayerHands(Player localPlayer)
        {
            if (localPlayer == null)
                return;
            _localPlayer = localPlayer;

            if (!localPlayer.HandsIsEmpty)
            {
                localPlayer.TrySaveLastItemInHands();
                _previousHandsItem = localPlayer.LastEquippedWeaponOrKnifeItem;
                _shouldRestoreHands = _previousHandsItem != null;
            }
            _shouldEnforceEmptyHands = true;
            RequestEmptyHands();
        }

        private void RequestEmptyHands()
        {
            if (!_shouldEnforceEmptyHands || _localPlayer == null ||
                _localPlayer.HandsIsEmpty || Time.unscaledTime < _nextEmptyHandsRequestTime)
                return;
            if (_isEmptyHandsRequestPending &&
                Time.unscaledTime < _emptyHandsRequestExpires)
                return;

            _isEmptyHandsRequestPending = true;
            _nextEmptyHandsRequestTime = Time.unscaledTime + EmptyHandsRetryDelay;
            _emptyHandsRequestExpires =
                Time.unscaledTime + EmptyHandsRequestTimeout;
            _localPlayer.SetEmptyHands(result =>
            {
                _isEmptyHandsRequestPending = false;
                if (!string.IsNullOrEmpty(result.Error))
                    TraumaLog.Warning(
                        $"[WoundInspection] Empty-hands transition failed: {result.Error}");
                else
                    TraumaLog.Info(
                        "[WoundInspection] Completed native empty-hands transition");
            });
        }

        private void RestoreLocalPlayerHands()
        {
            _shouldEnforceEmptyHands = false;
            if (!_shouldRestoreHands || _localPlayer == null ||
                _previousHandsItem == null)
                return;
            _shouldRestoreHands = false;
            if (!_localPlayer.IsItemCanBeEquipped(_previousHandsItem))
            {
                TraumaLog.Warning(
                    "[WoundInspection] Previous hands item is no longer equippable");
                return;
            }
            _localPlayer.TryProceed(_previousHandsItem, result =>
            {
                if (!string.IsNullOrEmpty(result.Error))
                    TraumaLog.Warning(
                        $"[WoundInspection] Previous-item equip failed: {result.Error}");
                else
                    TraumaLog.Info(
                        "[WoundInspection] Restored previous item through EFT hands pipeline");
            });
        }

        private void CreateOverlayToggles(TMP_Text template)
        {
            CreateOverlayToggle(template, "TRAJECTORIES", -132f,
                Plugin.ShouldShowInspectionTrajectories);
            CreateOverlayToggle(template, "ANATOMY", -178f,
                Plugin.ShouldShowInspectionAnatomy);
            CreateOverlayToggle(template, "ANCHORS", -224f,
                Plugin.ShouldShowInspectionAnchors);
            CreateOverlayToggle(template, "HIDE BACK HITS", -270f,
                Plugin.ShouldHideInspectionBackHits);
        }

        private void UpdateOverlaySettings()
        {
            foreach (KeyValuePair<Toggle, ConfigEntry<bool>> setting in _settingsByToggle)
                setting.Key.SetIsOnWithoutNotify(setting.Value.Value);

            if (_organGraphic != null)
                _organGraphic.enabled = Plugin.ShouldShowInspectionAnatomy.Value;
            if (_anchorRenderers.Count > 0 &&
                _anchorRenderers[0].enabled != Plugin.ShouldShowInspectionAnchors.Value)
                ApplyOverlayVisibility("anchors", _anchorRenderers,
                    Plugin.ShouldShowInspectionAnchors.Value);
        }

        private void CreateOverlayToggle(
            TMP_Text template, string labelText, float y, ConfigEntry<bool> setting)
        {
            GameObject toggleObject = new GameObject(
                labelText + "Toggle", typeof(RectTransform), typeof(Toggle));
            toggleObject.transform.SetParent(transform, false);
            RectTransform rect = (RectTransform)toggleObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(28f, y);
            rect.sizeDelta = new Vector2(210f, 32f);
            GameObject boxObject = new GameObject("Box", typeof(RectTransform), typeof(Image));
            boxObject.transform.SetParent(rect, false);
            RectTransform box = (RectTransform)boxObject.transform;
            box.anchorMin = box.anchorMax = new Vector2(0f, 0.5f);
            box.sizeDelta = Vector2.one * 22f;
            box.anchoredPosition = new Vector2(11f, 0f);
            Image boxImage = boxObject.GetComponent<Image>();
            boxImage.color = new Color(0.1f, 0.12f, 0.12f, 0.95f);
            GameObject checkObject = new GameObject("Check", typeof(RectTransform), typeof(Image));
            checkObject.transform.SetParent(box, false);
            RectTransform check = (RectTransform)checkObject.transform;
            check.anchorMin = Vector2.zero;
            check.anchorMax = Vector2.one;
            check.offsetMin = Vector2.one * 4f;
            check.offsetMax = Vector2.one * -4f;
            Image checkImage = checkObject.GetComponent<Image>();
            checkImage.color = new Color(0.2f, 0.85f, 1f, 1f);
            TMP_Text label = CreateLabel(template, rect, labelText);
            if (label != null)
            {
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(34f, 0f);
                label.rectTransform.offsetMax = Vector2.zero;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.fontSize = 14f;
            }
            Toggle toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkImage;
            toggle.SetIsOnWithoutNotify(setting.Value);
            toggle.onValueChanged.AddListener(value => setting.Value = value);
            _settingsByToggle.Add(toggle, setting);
        }

        private void CreateAnchorOverlay()
        {
            Mesh mesh = CreateOctahedronMesh();
            _overlayMeshes.Add(mesh);
            Material material = CreateOverlayMaterial(new Color(0.1f, 0.9f, 1f, 0.95f));
            foreach (RigidbodySpawner spawner in _ragdoll._rigidbodySpawners)
            {
                if (spawner == null)
                    continue;
                GameObject marker = new GameObject(
                    spawner.name + "_InspectionAnchor", typeof(MeshFilter), typeof(MeshRenderer));
                marker.transform.SetParent(spawner.transform, false);
                marker.transform.localScale = Vector3.one * 0.04f;
                marker.GetComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.enabled = Plugin.ShouldShowInspectionAnchors.Value;
                _anchorRenderers.Add(renderer);
            }
        }

        private void CreateOrganOverlay(Player corpsePlayer)
        {
            GameObject drawing = new GameObject(
                "InspectionF12Anatomy", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(InspectionOrganGraphic));
            drawing.transform.SetParent(transform, false);
            RectTransform rect = (RectTransform)drawing.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _organGraphic = drawing.GetComponent<InspectionOrganGraphic>();
            AnatomyRig rig = AnatomyRig.FromMesh(_corpse.transform);
            _organGraphic.Configure(_camera, corpsePlayer, rig, _corpse.transform);
            _organGraphic.raycastTarget = false;
            _organGraphic.enabled = Plugin.ShouldShowInspectionAnatomy.Value;
        }

        private void LogCorpseAnatomyPose(Player corpsePlayer)
        {
            if (OrganSystem.DebugLogging == null ||
                !OrganSystem.DebugLogging.Value)
                return;

            PlayerBones bones = _corpse.GetComponentInChildren<PlayerBones>();
            TraumaLog.Info($"[CorpseAnatomy] root name={_corpse.transform.name} " +
                $"pos={_corpse.transform.position:F3} rot={_corpse.transform.eulerAngles:F1} " +
                $"forward={_corpse.transform.forward:F3} up={_corpse.transform.up:F3}");
            LogCorpseBone("head", bones?.Head);
            LogCorpseBone("ribcage", bones?.Ribcage);
            LogCorpseBone("pelvis", bones?.Pelvis);

            AnatomyRig rig = AnatomyRig.FromMesh(_corpse.transform);
            TraumaLog.Info($"[CorpseAnatomy] mesh leftCollar={GetTransformPath(rig.LeftCollarbone)} " +
                $"rightCollar={GetTransformPath(rig.RightCollarbone)}");
            if (rig.LeftCollarbone != null && rig.RightCollarbone != null)
                TraumaLog.Info($"[CorpseAnatomy] mesh leftPos={rig.LeftCollarbone.position:F3} " +
                    $"rightPos={rig.RightCollarbone.position:F3}");
            if (AnatomyPoseSystem.TryResolveTorsoRotation(rig,
                    out Quaternion torsoRotation))
                TraumaLog.Info($"[CorpseAnatomy] resolved torso " +
                    $"rot={torsoRotation.eulerAngles:F1} forward=" +
                    $"{(torsoRotation * Vector3.forward):F3} up=" +
                    $"{(torsoRotation * Vector3.up):F3}");

            foreach (RigidbodySpawner spawner in _ragdoll._rigidbodySpawners)
            {
                if (spawner == null)
                    continue;
                Transform transform = spawner.transform;
                TraumaLog.Info($"[CorpseAnatomy] ragdoll name={transform.name} " +
                    $"path={GetTransformPath(transform)} pos={transform.position:F3} " +
                    $"rot={transform.eulerAngles:F1} forward={transform.forward:F3} " +
                    $"up={transform.up:F3} hasBody={spawner.Rigidbody != null}");
            }
        }

        private static void LogCorpseBone(string label, BifacialTransform bone)
        {
            if (bone?.Original == null)
            {
                TraumaLog.Info($"[CorpseAnatomy] bone={label} missing");
                return;
            }

            Transform original = bone.Original;
            TraumaLog.Info($"[CorpseAnatomy] bone={label} imitation={bone.UseImitation} " +
                $"livePos={bone.position:F3} liveRot={bone.rotation.eulerAngles:F1} " +
                $"liveForward={bone.forward:F3} original={GetTransformPath(original)} " +
                $"originalPos={original.position:F3} originalRot={original.eulerAngles:F1} " +
                $"originalForward={original.forward:F3}");
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return "<missing>";
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null;
                parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private Material CreateOverlayMaterial(Color color)
        {
            Material material = new Material(
                Shader.Find("Hidden/Internal-Colored") ??
                Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
            material.color = color;
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.Always);
            material.renderQueue = 5000;
            _overlayMaterials.Add(material);
            return material;
        }

        private static void ApplyOverlayVisibility(
            string overlayName, IReadOnlyCollection<Renderer> renderers, bool isVisible)
        {
            foreach (Renderer renderer in renderers)
                if (renderer != null)
                {
                    renderer.enabled = isVisible;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            TraumaLog.Info(
                $"[WoundInspection] {(isVisible ? "Showing" : "Hiding")} " +
                $"{renderers.Count} {overlayName} renderer(s)");
        }

        private static Mesh CreateOctahedronMesh()
        {
            Mesh mesh = new Mesh { name = "InspectionAnchorMarker" };
            mesh.vertices = new[]
            {
                Vector3.up, Vector3.down, Vector3.left,
                Vector3.right, Vector3.forward, Vector3.back
            };
            mesh.triangles = new[]
            {
                0, 4, 3, 0, 2, 4, 0, 5, 2, 0, 3, 5,
                1, 3, 4, 1, 4, 2, 1, 2, 5, 1, 5, 3
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CreateTitle(TMP_Text template)
        {
            TMP_Text title = CreateLabel(template, transform, "WOUND INSPECTION");
            if (title == null)
                return;
            RectTransform rect = title.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -34f);
            rect.sizeDelta = new Vector2(760f, 52f);
            title.fontSize = 30f;
        }

        private void CreateCloseButton(TMP_Text template)
        {
            GameObject buttonObject = new GameObject(
                "CloseWoundInspection", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(transform, false);
            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 28f);
            rect.sizeDelta = new Vector2(220f, 50f);
            buttonObject.GetComponent<Image>().color =
                new Color(0.12f, 0.14f, 0.14f, 0.98f);
            buttonObject.GetComponent<Button>().onClick.AddListener(Close);
            TMP_Text label = CreateLabel(template, buttonObject.transform, "CLOSE");
            if (label == null)
                return;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.fontSize = 18f;
            label.color = Color.white;
        }

        private static TMP_Text CreateLabel(TMP_Text template, Transform parent, string value)
        {
            if (template == null)
                return null;
            GameObject labelObject = new GameObject(
                value + "Text", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = template.font;
            label.fontSharedMaterial = template.fontSharedMaterial;
            label.text = value;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private sealed class InspectionOrganGraphic : Graphic
        {
            private readonly List<AnatomyScreenLine> _lines = new(2048);
            private readonly List<AnatomyOverlayLine> _anatomyLines = new(2048);
            private Camera _worldCamera;
            private Player _corpsePlayer;
            private AnatomyRig _corpseRig;
            private IReadOnlyDictionary<Transform, Transform> _bonesBySource;

            internal void Configure(Camera worldCamera, Player corpsePlayer,
                AnatomyRig corpseRig, Transform corpseRoot)
            {
                _worldCamera = worldCamera;
                _corpsePlayer = corpsePlayer;
                _corpseRig = corpseRig;
                _bonesBySource = MeshSkeleton.CaptureLimbMapping(corpsePlayer, corpseRoot);
            }

            private void LateUpdate()
            {
                if (!enabled) return;
                _lines.Clear();
                _anatomyLines.Clear();
                if (_worldCamera != null && _corpsePlayer != null)
                {
                    AnatomyOverlayGeometry.Build(_corpseRig, _corpsePlayer,
                        OrganSystem.GetTargetRules(_corpsePlayer), _anatomyLines, _bonesBySource);
                    AnatomyOverlayRenderer.AppendGeometry(_worldCamera, rectTransform,
                        _anatomyLines, _lines);
                }
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vertices)
            {
                AnatomyOverlayRenderer.PopulateMesh(vertices, _lines);
            }
        }

        private void ScheduleRagdollSettlement()
        {
            CorpseRagdollSettlement.Schedule(_corpse, _ragdoll);
            TraumaLog.Info("[WoundInspection] Scheduled EFT ragdoll settling cycle");
        }

        private void RestoreNativeBodyConstraints()
        {
            foreach (BodyActivation activation in _bodyActivations)
                if (activation.Body != null)
                    activation.Body.constraints = activation.Constraints;
        }

        public override ETranslateResult TranslateCommand(ECommand command)
        {
            if (command == ECommand.Escape)
                Close();
            return InputNode.GetDefaultBlockResult(command);
        }

        public override void Close()
        {
            if (this != null)
                Destroy(gameObject);
        }

        public override void OnDestroy()
        {
            StopAllCoroutines();
            if (_isInputNodeRegistered)
            {
                EftScreenManager.Instance?._inputNode?.Remove(this);
                _isInputNodeRegistered = false;
            }
            if (_isUiEventSystemEnabled)
            {
                UIEventSystem.Instance.Disable();
                _isUiEventSystemEnabled = false;
            }
            ReleaseDraggedBody();
            _jointStability.RepairExcessiveSeparation();
            _jointStability.Restore();
            CompleteRagdollActivation();
            RestoreNativeBodyConstraints();
            _detachedWeapon?.RestoreCollisions();
            ScheduleRagdollSettlement();
            if (_camera != null && _hasCapturedFieldOfView)
                _camera.fieldOfView = _originalFieldOfView;
            RestoreLocalPlayerHands();
            foreach (Renderer renderer in _anchorRenderers)
                if (renderer != null)
                    Destroy(renderer.gameObject);
            foreach (Mesh mesh in _overlayMeshes)
                if (mesh != null)
                    Destroy(mesh);
            foreach (Material material in _overlayMaterials)
                if (material != null)
                    Destroy(material);
            if (_instance == this)
                _instance = null;
            TraumaLog.Info("[WoundInspection] Closed live corpse inspection");
            base.OnDestroy();
        }
    }
}
