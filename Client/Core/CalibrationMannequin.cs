using System.Collections.Generic;
using System;
using BepInEx.Configuration;
using Comfort.Common;
using Diz.Resources;
using EFT;
using EFT.Visual;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace TraumaCore
{
    public sealed partial class Plugin
    {
        private enum CalibrationPoseMode { Recorded, TPose }

        private ConfigEntry<CalibrationPoseMode> _calibrationPoseMode;
        private GameObject _calibrationMannequin;
        private readonly Dictionary<Transform, Transform> _calibrationBones =
            new Dictionary<Transform, Transform>();
        private readonly Dictionary<Transform, RecordedTransformPose>
            _calibrationRecordedPoses =
                new Dictionary<Transform, RecordedTransformPose>();
        private Transform _calibrationHead;
        private Transform _calibrationChest;
        private Transform _calibrationPelvis;
        private Player _calibrationSource;
        private IReadOnlyDictionary<Transform, Transform> _calibrationLimbBones;
        private Animator _calibrationAnimator;

        private void UpdateCalibrationMannequin()
        {
            bool requested = OrganSystem.ShowCalibrationMannequin != null &&
                OrganSystem.ShowCalibrationMannequin.Value;
            if (!requested || _world == null || _localPlayer == null)
            {
                RemoveCalibrationMannequin();
                return;
            }
            if (_calibrationMannequin != null) return;
            Player source = FindCalibrationSource();
            if (source != null) CreateCalibrationMannequin(source);
        }

        private Player FindCalibrationSource()
        {
            Player closest = null;
            float closestDistanceSquared = float.MaxValue;
            IEnumerable<Player> players = _world.AllPlayersEverExisted;
            if (players == null) return null;
            foreach (Player player in players)
            {
                if (player == null || player == _localPlayer ||
                    player.PlayerBody == null || player.HealthController == null)
                    continue;
                float distanceSquared = (player.Transform.position -
                    _localPlayer.Transform.position).sqrMagnitude;
                if (distanceSquared >= closestDistanceSquared) continue;
                closest = player;
                closestDistanceSquared = distanceSquared;
            }
            return closest;
        }

        private bool CreateCalibrationMannequin(Player source)
        {
            Transform sourceRoot = source.Transform?.Original;
            if (sourceRoot == null || source.PlayerBody == null) return false;
            RemoveCalibrationMannequin();
            _calibrationSource = source;
            _calibrationMannequin = new GameObject(
                "TraumaCore Calibration Mannequin");
            List<CalibrationRendererSource> rendererSources =
                CaptureCalibrationRenderers(source, sourceRoot);
            if (rendererSources.Count == 0)
            {
                TraumaLog.Warning("[Calibration] No usable skinned renderers found");
                RemoveCalibrationMannequin();
                return false;
            }

            BuildCalibrationSkeleton();
            int createdRenderers = BuildCalibrationRenderers(rendererSources);
            _calibrationLimbBones = MeshSkeleton.CaptureLimbMapping(
                source, _calibrationMannequin.transform);
            ResolveCalibrationAnchors(source);
            CreateCalibrationAnimator(source);
            ApplySelectedCalibrationPose();
            PositionCalibrationMannequin();
            TraumaLog.Info($"[Calibration] Built shared skeleton: " +
                $"renderers={createdRenderers}, bones={_calibrationBones.Count}, " +
                $"head={DescribeTransform(_calibrationHead)}, " +
                $"chest={DescribeTransform(_calibrationChest)}, " +
                $"pelvis={DescribeTransform(_calibrationPelvis)}");
            if (createdRenderers == 0 || _calibrationHead == null ||
                _calibrationChest == null || _calibrationPelvis == null)
            {
                TraumaLog.Warning("[Calibration] Shared mannequin is incomplete; " +
                    "enable F12 Logging and inspect [Calibration] entries");
            }
            return createdRenderers > 0;
        }

        private List<CalibrationRendererSource> CaptureCalibrationRenderers(
            Player source, Transform sourceRoot)
        {
            List<CalibrationRendererSource> sources =
                new List<CalibrationRendererSource>();
            foreach (KeyValuePair<EBodyModelPart, LoddedSkin> bodySkin in
                source.PlayerBody.BodySkins)
            {
                if (bodySkin.Value == null) continue;
                SkinnedMeshRenderer preferredRenderer =
                    FindCalibrationRenderer(bodySkin.Value);
                foreach (Renderer renderer in bodySkin.Value.GetRenderers())
                {
                    if (!(renderer is SkinnedMeshRenderer skinned) ||
                        skinned.sharedMesh == null ||
                        skinned != preferredRenderer) continue;
                    Matrix4x4 rendererFromRoot = sourceRoot.worldToLocalMatrix *
                        skinned.transform.localToWorldMatrix;
                    Transform[] bones = skinned.bones;
                    Matrix4x4[] bindPoses = skinned.sharedMesh.bindposes;
                    int count = Mathf.Min(bones.Length, bindPoses.Length);
                    sources.Add(new CalibrationRendererSource(bodySkin.Key,
                        skinned, rendererFromRoot));
                    TraumaLog.Info($"[Calibration] Renderer part={bodySkin.Key} " +
                        $"name={skinned.name} mesh={skinned.sharedMesh.name} " +
                        $"vertices={skinned.sharedMesh.vertexCount} bones={bones.Length} " +
                        $"bindposes={bindPoses.Length} rootBone=" +
                        $"{DescribeTransform(skinned.rootBone)}");
                    if (bones.Length != bindPoses.Length)
                        TraumaLog.Warning($"[Calibration] Bone/bindpose mismatch on " +
                            $"{skinned.name}: {bones.Length}/{bindPoses.Length}");
                    for (int index = 0; index < count; index++)
                    {
                        Transform bone = bones[index];
                        if (bone == null) continue;
                        if (!_calibrationRecordedPoses.ContainsKey(bone))
                            CaptureCalibrationHierarchy(bone, sourceRoot);
                    }
                }
            }
            return sources;
        }

        private void CaptureCalibrationHierarchy(Transform bone,
            Transform sourceRoot)
        {
            Transform current = bone;
            while (current != null && current != sourceRoot)
            {
                if (!_calibrationRecordedPoses.ContainsKey(current))
                    _calibrationRecordedPoses.Add(current,
                        new RecordedTransformPose(current.localPosition,
                            current.localRotation, current.localScale));
                current = current.parent;
            }
        }

        private static SkinnedMeshRenderer FindCalibrationRenderer(
            LoddedSkin skin)
        {
            SkinnedMeshRenderer first = null;
            foreach (Renderer renderer in skin.GetRenderers())
            {
                if (!(renderer is SkinnedMeshRenderer skinned) ||
                    skinned.sharedMesh == null) continue;
                if (first == null) first = skinned;
                if (skinned.enabled) return skinned;
            }
            return first;
        }

        private void BuildCalibrationSkeleton()
        {
            foreach (KeyValuePair<Transform, RecordedTransformPose> pose in
                _calibrationRecordedPoses)
            {
                GameObject boneObject = new GameObject(pose.Key.name);
                _calibrationBones.Add(pose.Key, boneObject.transform);
            }
            foreach (KeyValuePair<Transform, Transform> clone in
                _calibrationBones)
            {
                Transform sourceParent = clone.Key.parent;
                Transform cloneParent = _calibrationMannequin.transform;
                if (sourceParent != null && _calibrationBones.TryGetValue(
                    sourceParent, out Transform mappedParent))
                    cloneParent = mappedParent;
                clone.Value.SetParent(cloneParent, false);
            }
            ApplyRecordedCalibrationPose();
        }

        private void ApplyRecordedCalibrationPose()
        {
            foreach (KeyValuePair<Transform, Transform> clone in
                _calibrationBones)
            {
                RecordedTransformPose pose =
                    _calibrationRecordedPoses[clone.Key];
                clone.Value.localPosition = pose.LocalPosition;
                clone.Value.localRotation = pose.LocalRotation;
                clone.Value.localScale = pose.LocalScale;
            }
        }

        private void ApplySelectedCalibrationPose()
        {
            ApplyRecordedCalibrationPose();
            if (_calibrationPoseMode != null &&
                _calibrationPoseMode.Value == CalibrationPoseMode.TPose)
                ApplyCalibrationTPoseClip();
        }

        private void BindCalibrationPoseButtons()
        {
            _calibrationPoseMode = Config.Bind("Debug",
                "CalibrationPoseMode", CalibrationPoseMode.TPose,
                new ConfigDescription(
                    "Switch the existing calibration mannequin between its " +
                    "captured source pose and EFT's T-pose clip.", null,
                    new ConfigurationManagerAttributes
                    {
                        Category = "09 - Debugging",
                        DispName = "Calibration Mannequin Pose",
                        Order = 88,
                        HideDefaultButton = true,
                        HideSettingName = true,
                        CustomDrawer = ignored => DrawCalibrationPoseButtons()
                    }));
        }

        private void DrawCalibrationPoseButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Recorded Pose", GUILayout.ExpandWidth(true)))
                SetCalibrationPoseMode(CalibrationPoseMode.Recorded);
            if (GUILayout.Button("T-Pose", GUILayout.ExpandWidth(true)))
                SetCalibrationPoseMode(CalibrationPoseMode.TPose);
            GUILayout.EndHorizontal();
        }

        private void SetCalibrationPoseMode(CalibrationPoseMode mode)
        {
            if (_calibrationPoseMode == null) return;
            _calibrationPoseMode.Value = mode;
            if (_calibrationMannequin != null) ApplySelectedCalibrationPose();
            TraumaLog.Info("[Calibration] Pose mode changed to " + mode);
        }

        private int BuildCalibrationRenderers(
            List<CalibrationRendererSource> sources)
        {
            int created = 0;
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                CalibrationRendererSource source = sources[sourceIndex];
                Transform[] sourceBones = source.Renderer.bones;
                Transform[] cloneBones = new Transform[sourceBones.Length];
                int mappedCount = 0;
                for (int boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    if (sourceBones[boneIndex] != null &&
                        _calibrationBones.TryGetValue(sourceBones[boneIndex],
                            out cloneBones[boneIndex])) mappedCount++;
                }
                GameObject rendererObject = new GameObject(
                    source.ModelPart + " - " + source.Renderer.name);
                rendererObject.transform.SetParent(_calibrationMannequin.transform,
                    false);
                ApplyCalibrationMatrix(rendererObject.transform,
                    source.RendererFromRoot);
                SkinnedMeshRenderer renderer =
                    rendererObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source.Renderer.sharedMesh;
                renderer.sharedMaterials = source.Renderer.sharedMaterials;
                renderer.bones = cloneBones;
                if (source.Renderer.rootBone != null &&
                    _calibrationBones.TryGetValue(source.Renderer.rootBone,
                        out Transform cloneRootBone))
                    renderer.rootBone = cloneRootBone;
                renderer.localBounds = source.Renderer.localBounds;
                renderer.updateWhenOffscreen = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                created++;
                TraumaLog.Info($"[Calibration] Skinned renderer=" +
                    $"{source.Renderer.name} part={source.ModelPart} " +
                    $"mapped={mappedCount}/{cloneBones.Length} root=" +
                    $"{DescribeTransform(renderer.rootBone)} bounds=" +
                    $"{renderer.localBounds.center}/{renderer.localBounds.size}");
            }
            return created;
        }

        private void ApplyCalibrationTPoseClip()
        {
            AnimationClip clip = FindCalibrationTPoseClip();
            if (clip == null)
            {
                TraumaLog.Warning("[Calibration] T-pose clip not found in loaded EFT bundles");
                return;
            }
            if (_calibrationAnimator == null ||
                _calibrationAnimator.avatar == null)
            {
                TraumaLog.Warning("[Calibration] Source humanoid avatar was not resolved");
                return;
            }
            Dictionary<Transform, Quaternion> rotationsBefore =
                new Dictionary<Transform, Quaternion>();
            foreach (Transform bone in _calibrationBones.Values)
                rotationsBefore[bone] = bone.localRotation;

            _calibrationAnimator.enabled = true;
            PlayableGraph graph = PlayableGraph.Create(
                "TraumaCore Mannequin T-Pose");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                graph, "T-Pose", _calibrationAnimator);
            AnimationClipPlayable playable = AnimationClipPlayable.Create(
                graph, clip);
            playable.SetTime(clip.length * 0.5d);
            output.SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0f);
            graph.Destroy();
            _calibrationAnimator.enabled = false;

            float maximumRotationDelta = 0f;
            foreach (KeyValuePair<Transform, Quaternion> before in rotationsBefore)
                maximumRotationDelta = Mathf.Max(maximumRotationDelta,
                    Quaternion.Angle(before.Value, before.Key.localRotation));
            TraumaLog.Info($"[Calibration] Sampled clip={clip.name} " +
                $"length={clip.length:F3}s maxBoneRotationDelta=" +
                $"{maximumRotationDelta:F2}deg");
        }

        private void CreateCalibrationAnimator(Player source)
        {
            Animator sourceAnimator = source?.PlayerBones?.PlayableAnimator != null
                ? source.PlayerBones.PlayableAnimator.outputAnimator : null;
            if (sourceAnimator == null || sourceAnimator.avatar == null)
            {
                TraumaLog.Warning("[Calibration] Source output animator/avatar missing");
                return;
            }
            _calibrationAnimator =
                _calibrationMannequin.AddComponent<Animator>();
            _calibrationAnimator.avatar = sourceAnimator.avatar;
            _calibrationAnimator.applyRootMotion = false;
            _calibrationAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            TraumaLog.Info($"[Calibration] Copied humanoid avatar=" +
                $"{sourceAnimator.avatar.name} human={sourceAnimator.avatar.isHuman}");
        }

        private static AnimationClip FindCalibrationTPoseClip()
        {
            IEasyAssets assets = Singleton<IEasyAssets>.Instance;
            if (assets == null) return null;
            string[] bundlePaths =
            {
                "assets/content/characters/controllers/player_anim_controller.bundle",
                "assets/content/characters/animations/character_animations.bundle"
            };
            string[] clipNames = { "T_Pose", "Tpose_idle", "Idle_Tpose" };
            for (int nameIndex = 0; nameIndex < clipNames.Length; nameIndex++)
            {
                for (int bundleIndex = 0; bundleIndex < bundlePaths.Length;
                    bundleIndex++)
                {
                    string path = bundlePaths[bundleIndex];
                    if (!assets.IsAssetLoaded(path)) continue;
                    foreach (UnityEngine.Object asset in
                        assets.System.GetNode(path).Data.Assets)
                    {
                        if (asset is AnimationClip clip && string.Equals(
                            clip.name, clipNames[nameIndex],
                            StringComparison.OrdinalIgnoreCase)) return clip;
                    }
                }
            }
            return null;
        }

        private void ResolveCalibrationAnchors(Player source)
        {
            _calibrationHead = FindCalibrationBone(
                OrganSystem.GetHeadAnchor(source), "head");
            _calibrationChest = FindCalibrationBone(
                OrganSystem.GetChestAnchor(source), "ribcage", "spine2", "chest");
            _calibrationPelvis = FindCalibrationBone(
                OrganSystem.GetPelvisAnchor(source), "pelvis", "hips");
        }

        private Transform FindCalibrationBone(Transform source,
            params string[] fallbackNames)
        {
            if (source != null && _calibrationBones.TryGetValue(source,
                out Transform exact)) return exact;
            foreach (KeyValuePair<Transform, Transform> bone in _calibrationBones)
            {
                string candidate = bone.Key.name.ToLowerInvariant();
                for (int index = 0; index < fallbackNames.Length; index++)
                    if (candidate.Contains(fallbackNames[index].ToLowerInvariant()))
                        return bone.Value;
            }
            TraumaLog.Warning("[Calibration] Anchor unresolved: " +
                DescribeTransform(source));
            return null;
        }

        private void PositionCalibrationMannequin()
        {
            Vector3 forward = _localPlayer.Transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            _calibrationMannequin.transform.SetPositionAndRotation(
                _localPlayer.Transform.position + forward * 2.5f,
                Quaternion.LookRotation(-forward, Vector3.up));
        }

        private void AddCalibrationMannequinOverlay()
        {
            if (_calibrationMannequin == null || _calibrationSource == null) return;
            AnatomyRig rig = new AnatomyRig(_calibrationHead, _calibrationChest,
                _calibrationPelvis, _calibrationMannequin.transform.forward);
            _anatomyLines.Clear();
            AnatomyOverlayGeometry.Build(rig, _calibrationSource,
                OrganSystem.GetTargetRules(_calibrationSource), _anatomyLines, _calibrationLimbBones);
            AnatomyOverlayRenderer.AppendGeometry(_camera, _canvasRect, _anatomyLines, _lines);
        }

        private static void ApplyCalibrationMatrix(Transform target,
            Matrix4x4 matrix)
        {
            Vector3 right = matrix.GetColumn(0);
            Vector3 up = matrix.GetColumn(1);
            Vector3 forward = matrix.GetColumn(2);
            float sign = Vector3.Dot(Vector3.Cross(right, up), forward) < 0f
                ? -1f : 1f;
            target.localPosition = matrix.GetColumn(3);
            target.localRotation = Quaternion.LookRotation(forward, up);
            target.localScale = new Vector3(right.magnitude * sign,
                up.magnitude, forward.magnitude);
        }

        private void RemoveCalibrationMannequin()
        {
            if (_calibrationMannequin != null) Destroy(_calibrationMannequin);
            _calibrationMannequin = null;
            _calibrationSource = null;
            _calibrationBones.Clear();
            _calibrationLimbBones = null;
            _calibrationRecordedPoses.Clear();
            _calibrationHead = null;
            _calibrationChest = null;
            _calibrationPelvis = null;
            _calibrationAnimator = null;
        }

        private static string DescribeTransform(Transform transform) =>
            transform == null ? "null" : transform.name;

        private readonly struct CalibrationRendererSource
        {
            internal readonly EBodyModelPart ModelPart;
            internal readonly SkinnedMeshRenderer Renderer;
            internal readonly Matrix4x4 RendererFromRoot;

            internal CalibrationRendererSource(EBodyModelPart modelPart,
                SkinnedMeshRenderer renderer, Matrix4x4 rendererFromRoot)
            {
                ModelPart = modelPart;
                Renderer = renderer;
                RendererFromRoot = rendererFromRoot;
            }
        }

        private readonly struct RecordedTransformPose
        {
            internal readonly Vector3 LocalPosition;
            internal readonly Quaternion LocalRotation;
            internal readonly Vector3 LocalScale;

            internal RecordedTransformPose(Vector3 localPosition,
                Quaternion localRotation, Vector3 localScale)
            {
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
            }
        }
    }
}
