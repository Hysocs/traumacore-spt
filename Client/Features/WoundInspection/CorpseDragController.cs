using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.CameraControl;
using EFT.Interactive;
using EFT.InventoryLogic;
using TraumaCore.Features.DeathScreen.HitMarkers;
using UnityEngine;

namespace TraumaCore.Features.WoundInspection
{
    internal sealed class CorpseDragController : MonoBehaviour
    {
        private const float GrabSpring = 600f;
        private const float GrabDamping = 54f;
        private const float MaximumGrabAcceleration = 900f;
        private const float MaximumSeparationDuration = 5f;
        private const float HeldDistanceMultiplier = 0.85f;
        private const float HeldDistanceBlendDuration = 0.75f;
        private const float EmptyHandsRetryDelay = 0.25f;
        private const float EmptyHandsRequestTimeout = 1.5f;

        private sealed class BodyState
        {
            internal Rigidbody Body;
            internal RigidbodyConstraints Constraints;
        }

        private static CorpseDragController _active;
        private readonly List<BodyState> _bodyStates = new();
        private readonly RagdollJointStability _jointStability = new();
        private GamePlayerOwner _owner;
        private Corpse _corpse;
        private CorpseRagdoll _ragdoll;
        private Camera _camera;
        private Rigidbody _grabbedBody;
        private Vector3 _localGrabPoint;
        private float _grabDistance;
        private float _initialGrabDistance;
        private float _targetGrabDistance;
        private float _grabDistanceBlendElapsed;
        private float _cameraHeightAtCapture;
        private float _grabHeightAtCapture;
        private float _maximumTargetSeparation;
        private float _separationDuration;
        private bool _originalPutToSleep;
        private bool _isStopping;
        private CorpseWeaponLink.DetachedWeapon _detachedWeapon;
        private Player _localPlayer;
        private Item _previousHandsItem;
        private bool _shouldRestoreHands;
        private bool _shouldEnforceEmptyHands;
        private bool _isEmptyHandsRequestPending;
        private float _nextEmptyHandsRequestTime;
        private float _emptyHandsRequestExpires;

        internal static bool IsDragging(Corpse corpse) =>
            _active != null && _active._corpse == corpse;

        internal static bool HasActiveDrag => _active != null;

        internal static void Begin(GamePlayerOwner owner, Corpse corpse)
        {
            if (!Plugin.EnableCorpseDragging.Value || owner == null ||
                corpse?.Ragdoll == null)
                return;
            if (_active != null)
                _active.StopDragging();

            Player localPlayer = GamePlayerOwner.MyPlayer;
            if (localPlayer == null)
                return;
            CorpseDragController controller =
                localPlayer.gameObject.AddComponent<CorpseDragController>();
            if (!controller.Capture(owner, corpse))
            {
                Destroy(controller);
                return;
            }
            _active = controller;
            owner.ClearInteractionState();
        }

        internal static void StopActiveDrag()
        {
            if (_active != null)
                _active.StopDragging();
        }

        private bool Capture(GamePlayerOwner owner, Corpse corpse)
        {
            CorpseRagdollSettlement.Cancel(corpse);
            _owner = owner;
            _corpse = corpse;
            _localPlayer = GamePlayerOwner.MyPlayer;
            _ragdoll = corpse.Ragdoll;
            _camera = CameraManager.Instance?.Camera ?? Camera.main;
            if (_camera == null || _ragdoll._owner == null ||
                _ragdoll._rigidbodySpawners == null ||
                _ragdoll._rigidbodySpawners.Length == 0)
                return false;

            Dictionary<RigidbodySpawner, (Vector3 Position, Quaternion Rotation)> poses =
                _ragdoll._rigidbodySpawners
                    .Where(spawner => spawner != null)
                    .ToDictionary(spawner => spawner,
                        spawner => (spawner.transform.position,
                            spawner.transform.rotation));
            _detachedWeapon = CorpseWeaponLink.Detach(_ragdoll);
            ActivateRagdoll();
            _jointStability.Capture(_ragdoll._jointSpawners);
            foreach (KeyValuePair<RigidbodySpawner,
                (Vector3 Position, Quaternion Rotation)> pose in poses)
            {
                Rigidbody body = pose.Key.Rigidbody;
                if (body == null)
                    continue;
                pose.Key.transform.SetPositionAndRotation(
                    pose.Value.Position, pose.Value.Rotation);
                body.position = pose.Value.Position;
                body.rotation = pose.Value.Rotation;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                _bodyStates.Add(new BodyState
                {
                    Body = body,
                    Constraints = body.constraints
                });
                body.constraints = RigidbodyConstraints.None;
                body.isKinematic = false;
                body.WakeUp();
            }
            Physics.SyncTransforms();
            if (!FindChestBody(out _grabbedBody, out Vector3 worldPoint))
                return false;

            _localGrabPoint = _grabbedBody.transform.InverseTransformPoint(worldPoint);
            float interactionRange = EFTHardSettings.Instance.LOOT_RAYCAST_DISTANCE +
                EFTHardSettings.Instance.BEHIND_CAST;
            Vector3 cameraToGrab = worldPoint - _camera.transform.position;
            _initialGrabDistance = Vector3.ProjectOnPlane(
                cameraToGrab, Vector3.up).magnitude;
            _targetGrabDistance = interactionRange * 0.5f *
                HeldDistanceMultiplier;
            _grabDistance = _initialGrabDistance;
            _grabDistanceBlendElapsed = 0f;
            _maximumTargetSeparation = interactionRange;
            _cameraHeightAtCapture = _camera.transform.position.y;
            _grabHeightAtCapture = worldPoint.y;
            UnequipLocalPlayerHands();
            TraumaLog.Info(
                $"[BodyDrag] Grabbed '{_grabbedBody.name}' at " +
                $"{_initialGrabDistance:F2}m, blending to " +
                $"{_targetGrabDistance:F2}m " +
                $"inside EFT interaction range {interactionRange:F2}m");
            return true;
        }

        private void UnequipLocalPlayerHands()
        {
            if (_localPlayer == null)
                return;

            if (!_localPlayer.HandsIsEmpty)
            {
                _localPlayer.TrySaveLastItemInHands();
                _previousHandsItem = _localPlayer.LastEquippedWeaponOrKnifeItem;
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
                        $"[BodyDrag] Empty-hands transition failed: {result.Error}");
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
                return;
            _localPlayer.TryProceed(_previousHandsItem, result =>
            {
                if (!string.IsNullOrEmpty(result.Error))
                    TraumaLog.Warning(
                        $"[BodyDrag] Previous-item equip failed: {result.Error}");
            });
        }

        private void ActivateRagdoll()
        {
            _originalPutToSleep = _ragdoll._putToSleep;
            bool hasMissingBody = _ragdoll._rigidbodySpawners.Any(
                spawner => spawner == null || spawner.Rigidbody == null);
            if (!_ragdoll._isPhysicsDone && !hasMissingBody)
            {
                _ragdoll.WakeUp();
                return;
            }

            _ragdoll._putToSleep = false;
            try
            {
                _ragdoll.Start();
            }
            finally
            {
                _ragdoll._putToSleep = _originalPutToSleep;
            }
        }

        private bool FindChestBody(
            out Rigidbody grabbedBody, out Vector3 worldPoint)
        {
            grabbedBody = null;
            Transform chestAnchor = BodyPartAnchorResolver.Find(
                _ragdoll._owner.transform, EBodyPart.Chest);
            if (chestAnchor == null)
            {
                worldPoint = default;
                return false;
            }

            RigidbodySpawner chestSpawner = _ragdoll._rigidbodySpawners
                .Where(spawner => spawner?.Rigidbody != null)
                .OrderBy(spawner => Vector3.Distance(
                    spawner.transform.position, chestAnchor.position))
                .FirstOrDefault();
            if (chestSpawner == null)
            {
                worldPoint = default;
                return false;
            }

            grabbedBody = chestSpawner.Rigidbody;
            worldPoint = grabbedBody.worldCenterOfMass;
            return true;
        }

        private void FixedUpdate()
        {
            if (_grabbedBody == null || _camera == null || _corpse == null)
            {
                StopDragging();
                return;
            }

            Vector3 worldPoint =
                _grabbedBody.transform.TransformPoint(_localGrabPoint);
            _grabDistanceBlendElapsed += Time.fixedDeltaTime;
            float distanceBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                _grabDistanceBlendElapsed / HeldDistanceBlendDuration));
            _grabDistance = Mathf.Lerp(_initialGrabDistance,
                _targetGrabDistance, distanceBlend);
            Vector3 horizontalForward = Quaternion.Euler(
                0f, _camera.transform.eulerAngles.y, 0f) * Vector3.forward;
            float cameraHeightChange =
                _camera.transform.position.y - _cameraHeightAtCapture;
            Vector3 target = _camera.transform.position +
                horizontalForward * _grabDistance;
            target.y = _grabHeightAtCapture + cameraHeightChange;
            if (Vector3.Distance(worldPoint, _camera.transform.position) >
                _maximumTargetSeparation)
            {
                _separationDuration += Time.fixedDeltaTime;
                if (_separationDuration >= MaximumSeparationDuration)
                {
                    TraumaLog.Info(
                        "[BodyDrag] Released corpse after remaining outside " +
                        "the drag range for five seconds");
                    StopDragging();
                    return;
                }
            }
            else
            {
                _separationDuration = 0f;
            }

            Vector3 acceleration = (target - worldPoint) * GrabSpring -
                _grabbedBody.GetPointVelocity(worldPoint) * GrabDamping;
            acceleration = Vector3.ClampMagnitude(
                acceleration, MaximumGrabAcceleration);
            foreach (BodyState state in _bodyStates)
                if (state.Body != null)
                    state.Body.WakeUp();
            _grabbedBody.AddForceAtPosition(
                acceleration, worldPoint, ForceMode.Acceleration);
            _jointStability.RepairExcessiveSeparation();
        }

        private void Update()
        {
            if (_localPlayer?.MovementContext != null)
                _localPlayer.MovementContext.EnableSprint(false);
            RequestEmptyHands();
        }

        private void StopDragging()
        {
            if (_isStopping)
                return;
            _isStopping = true;
            if (_active == this)
                _active = null;
            if (_owner != null)
                _owner.ClearInteractionState();
            Destroy(this);
        }

        private void OnDestroy()
        {
            _jointStability.RepairExcessiveSeparation();
            _jointStability.Restore();
            foreach (BodyState state in _bodyStates)
                if (state.Body != null)
                {
                    state.Body.constraints = state.Constraints;
                    state.Body.velocity = Vector3.ClampMagnitude(
                        state.Body.velocity, 2f);
                    state.Body.angularVelocity = Vector3.ClampMagnitude(
                        state.Body.angularVelocity, 4f);
                }
            if (_localPlayer != null)
                _localPlayer.UpdateSpeedLimitByHealth();
            RestoreLocalPlayerHands();
            _detachedWeapon?.RestoreCollisions();
            CorpseRagdollSettlement.Schedule(_corpse, _ragdoll);
            if (_active == this)
                _active = null;
            TraumaLog.Info("[BodyDrag] Released corpse to EFT settling cycle");
        }
    }
}
