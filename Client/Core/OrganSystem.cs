using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace TraumaCore
{
    internal enum OrganAnchor { Chest, Head }
    internal enum OrganShape { Box, Ellipsoid }

    internal readonly struct TargetRules
    {
        internal readonly float DamageMultiplier;
        internal readonly bool BodyTraumaEnabled;
        internal readonly bool BrainEnabled;
        internal readonly bool HeartEnabled;
        internal readonly bool CervicalSpineEnabled;
        internal readonly bool ThoracicSpineEnabled;

        internal TargetRules(float damageMultiplier, bool bodyTraumaEnabled,
            bool brainEnabled, bool heartEnabled, bool cervicalSpineEnabled,
            bool thoracicSpineEnabled)
        {
            DamageMultiplier = damageMultiplier;
            BodyTraumaEnabled = bodyTraumaEnabled;
            BrainEnabled = brainEnabled;
            HeartEnabled = heartEnabled;
            CervicalSpineEnabled = cervicalSpineEnabled;
            ThoracicSpineEnabled = thoracicSpineEnabled;
        }
    }

    internal sealed class OrganDefinition
    {
        public readonly string Name;
        public readonly Color Color;
        public readonly OrganAnchor Anchor;
        public readonly OrganShape Shape;
        private readonly Vector3 _offset;
        private readonly Vector3 _halfExtents;
        private readonly Vector3 _rotationEuler;

        public Vector3 LocalOffset { get { return _offset; } }
        public Vector3 HalfExtents { get { return _halfExtents; } }
        public Vector3 LocalRotationEuler { get { return _rotationEuler; } }

        public OrganDefinition(string name, OrganAnchor anchor, OrganShape shape,
            Vector3 offset, Vector3 size, float scale, Color color)
        {
            Name = name; Anchor = anchor; Shape = shape; Color = color;
            _offset = offset;
            _halfExtents = size * scale * 0.5f;
            _rotationEuler = Vector3.zero;
        }

        public OrganDefinition(string name, OrganAnchor anchor, OrganShape shape,
            Vector3 offset, Vector3 size, Vector3 rotationEuler, Color color)
        {
            Name = name; Anchor = anchor; Shape = shape; Color = color;
            _offset = offset;
            _halfExtents = size * 0.5f;
            _rotationEuler = rotationEuler;
        }

        public Vector3 WorldCenter(Player player)
        {
            return AnatomyPoseSystem.TryGetOrgan(AnatomyRig.FromPlayer(player),
                this, out BoneVolumePose pose) ? pose.Center : Vector3.zero;
        }

        public Quaternion WorldRotation(Player player)
        {
            return AnatomyPoseSystem.TryGetOrgan(AnatomyRig.FromPlayer(player),
                this, out BoneVolumePose pose) ? pose.Rotation :
                Quaternion.identity;
        }

        public bool IntersectsShot(Player player, Vector3 hitPoint, Vector3 direction,
            out Vector3 intersection, out float travelDistance)
        {
            return IntersectsShot(player, hitPoint, direction, out intersection,
                out travelDistance, out _);
        }

        public bool IntersectsShot(Player player, Vector3 hitPoint, Vector3 direction,
            out Vector3 intersection, out float travelDistance,
            out float exitDistance)
        {
            intersection = hitPoint;
            travelDistance = 0f;
            exitDistance = 0f;
            if (!AnatomyPoseSystem.TryGetOrgan(AnatomyRig.FromPlayer(player),
                this, out BoneVolumePose pose)) return false;
            bool intersects = Shape == OrganShape.Ellipsoid
                ? AnatomyIntersections.TryIntersectEllipsoid(pose, hitPoint,
                    direction, false, out AnatomyHit hit)
                : AnatomyIntersections.TryIntersectBox(pose, hitPoint,
                    direction, out hit);
            if (!intersects) return false;
            intersection = hit.Point;
            travelDistance = hit.EntryDistance;
            exitDistance = hit.ExitDistance;
            return true;
        }

        public Transform GetAnchor(Player player)
        { return Anchor == OrganAnchor.Head ? OrganSystem.GetHeadAnchor(player) : OrganSystem.GetChestAnchor(player); }
    }

    internal static partial class OrganSystem
    {
        private static readonly Dictionary<int, LimbBoneCache> LimbCaches =
            new Dictionary<int, LimbBoneCache>();
        internal static Transform GetChestAnchor(Player player)
        {
            if (player == null || player.PlayerBones == null || player.PlayerBones.Ribcage == null) return null;
            return player.PlayerBones.Ribcage.Original;
        }

        internal static Transform GetPelvisAnchor(Player player)
        {
            if (player == null || player.PlayerBones == null || player.PlayerBones.Pelvis == null) return null;
            return player.PlayerBones.Pelvis.Original;
        }

        internal static Transform GetHeadAnchor(Player player)
        {
            if (player == null || player.PlayerBones == null || player.PlayerBones.Head == null) return null;
            return player.PlayerBones.Head.Original;
        }


        internal static bool TryFindSkullIntersection(Player player,
            WoundBallistics wound, Vector3 direction, out Vector3 intersection)
        {
            intersection = wound.EntryPoint;
            AnatomyRig rig = AnatomyRig.FromPlayer(player);
            bool found = false;
            float nearestDistance = float.MaxValue;
            if (AnatomyPoseSystem.TryGetSkull(rig, out BoneVolumePose skull1) &&
                AnatomyIntersections.TryIntersectEllipsoid(skull1,
                    wound.EntryPoint, direction, false, out AnatomyHit hit1))
            {
                found = true;
                nearestDistance = hit1.EntryDistance;
                intersection = hit1.Point;
            }
            if (AnatomyPoseSystem.TryGetSecondSkull(rig,
                    out BoneVolumePose skull2) &&
                AnatomyIntersections.TryIntersectEllipsoid(skull2,
                    wound.EntryPoint, direction, false, out AnatomyHit hit2) &&
                hit2.EntryDistance < nearestDistance)
            {
                found = true;
                intersection = hit2.Point;
            }
            return found;
        }

        internal static bool TryFindBrainIntersection(Player player,
            Vector3 origin, Vector3 direction, out Vector3 intersection,
            out float entryDistance, out float exitDistance)
        {
            intersection = origin;
            entryDistance = exitDistance = 0f;
            bool found = false;
            OrganDefinition[] brainLobes = { Brain, LowerBrain };
            for (int index = 0; index < brainLobes.Length; index++)
            {
                if (!brainLobes[index].IntersectsShot(player, origin, direction,
                    out Vector3 point, out float entry, out float exit) ||
                    found && entry >= entryDistance) continue;
                found = true;
                intersection = point;
                entryDistance = entry;
                exitDistance = exit;
            }
            return found;
        }

        internal static bool TryFindRibcageIntersection(Player player,
            WoundBallistics wound, Vector3 direction, out Vector3 intersection)
        {
            intersection = wound.EntryPoint;
            if (!AnatomyPoseSystem.TryGetRibcage(
                    AnatomyRig.FromPlayer(player), out RibcageShape ribcage) ||
                !AnatomyIntersections.TryIntersectRibcage(ribcage,
                    wound.EntryPoint, direction, out AnatomyHit hit))
                return false;
            intersection = hit.Point;
            return true;
        }

        internal static bool TryGetBodyPartBounds(Player player,
            EBodyPart bodyPart, out Bounds bounds)
        {
            bounds = default;
            BodyPartCollider[] colliders = player?.PlayerBones?.BodyPartColliders;
            bool hasBounds = false;
            if (colliders == null) return false;
            for (int i = 0; i < colliders.Length; i++)
            {
                BodyPartCollider bodyCollider = colliders[i];
                if (bodyCollider == null ||
                    bodyCollider.BodyPartType != bodyPart ||
                    bodyCollider.Collider == null) continue;
                if (!hasBounds)
                {
                    bounds = bodyCollider.Collider.bounds;
                    hasBounds = true;
                }
                else bounds.Encapsulate(bodyCollider.Collider.bounds);
            }
            return hasBounds;
        }

        internal static bool TryGetUpperSpineSegment(Player player,
            out Vector3 brainBase, out Vector3 chestTop)
        {
            brainBase = chestTop = Vector3.zero;
            if (!AnatomyPoseSystem.TryGetUpperSpine(
                AnatomyRig.FromPlayer(player), out BoneSegmentPose segment))
                return false;
            brainBase = segment.Start;
            chestTop = segment.End;
            return true;
        }

        internal static bool IntersectsUpperSpine(Player player, Vector3 hitPoint,
            Vector3 direction, out Vector3 intersection)
        {
            intersection = hitPoint;
            if (!AnatomyPoseSystem.TryGetUpperSpine(
                    AnatomyRig.FromPlayer(player), out BoneSegmentPose segment) ||
                !AnatomyIntersections.TryIntersectCylinder(segment, hitPoint,
                    direction, out AnatomyHit hit)) return false;
            intersection = hit.Point;
            return true;
        }

        internal static bool TryGetThoracicSpineSegment(Player player,
            out Vector3 chestTop, out Vector3 stomachTop)
        {
            chestTop = stomachTop = Vector3.zero;
            if (!AnatomyPoseSystem.TryGetThoracicSpine(
                AnatomyRig.FromPlayer(player), out BoneSegmentPose segment))
                return false;
            chestTop = segment.Start;
            stomachTop = segment.End;
            return true;
        }

        internal static bool IntersectsThoracicSpine(Player player, Vector3 hitPoint,
            Vector3 direction, out Vector3 intersection)
        {
            intersection = hitPoint;
            if (!AnatomyPoseSystem.TryGetThoracicSpine(
                    AnatomyRig.FromPlayer(player), out BoneSegmentPose segment) ||
                !AnatomyIntersections.TryIntersectCylinder(segment, hitPoint,
                    direction, out AnatomyHit hit)) return false;
            intersection = hit.Point;
            return true;
        }

        internal static bool TryGetBoneSegments(Player player, EBodyPart bodyPart,
            out Transform firstStart, out Transform firstEnd,
            out Transform secondStart, out Transform secondEnd,
            IReadOnlyDictionary<Transform, Transform> bonesBySource = null)
        {
            firstStart = firstEnd = secondStart = secondEnd = null;
            if (player == null || player.PlayerBones == null) return false;
            int playerId = player.GetInstanceID();
            LimbBoneCache cache;
            if (!LimbCaches.TryGetValue(playerId, out cache))
            {
                cache = new LimbBoneCache();
                LimbCaches.Add(playerId, cache);
            }
            cache.Resolve(player);

            switch (bodyPart)
            {
                case EBodyPart.LeftArm:
                    firstStart = cache.LeftUpperArm; firstEnd = cache.LeftElbow;
                    secondStart = cache.LeftElbow; secondEnd = cache.LeftHand;
                    break;
                case EBodyPart.RightArm:
                    firstStart = cache.RightUpperArm; firstEnd = cache.RightElbow;
                    secondStart = cache.RightElbow; secondEnd = cache.RightHand;
                    break;
                case EBodyPart.LeftLeg:
                    firstStart = cache.LeftHip; firstEnd = cache.LeftKnee;
                    secondStart = cache.LeftCalf != null ? cache.LeftCalf : cache.LeftKnee;
                    secondEnd = cache.LeftFoot;
                    break;
                case EBodyPart.RightLeg:
                    firstStart = cache.RightHip; firstEnd = cache.RightKnee;
                    secondStart = cache.RightCalf != null ? cache.RightCalf : cache.RightKnee;
                    secondEnd = cache.RightFoot;
                    break;
            }
            if (bonesBySource != null)
            {
                firstStart = ResolveMappedBone(firstStart, bonesBySource);
                firstEnd = ResolveMappedBone(firstEnd, bonesBySource);
                secondStart = ResolveMappedBone(secondStart, bonesBySource);
                secondEnd = ResolveMappedBone(secondEnd, bonesBySource);
            }
            return firstStart != null && firstEnd != null || secondStart != null && secondEnd != null;
        }

        private static Transform ResolveMappedBone(Transform source,
            IReadOnlyDictionary<Transform, Transform> bonesBySource) =>
            source != null && bonesBySource.TryGetValue(source, out Transform mapped)
                ? mapped : null;

        internal static bool TryFindDepthReferenceCenter(Player player,
            EBodyPart bodyPart, Vector3 hitPoint, out Vector3 center,
            out string referenceName)
        {
            center = hitPoint;
            referenceName = "collider bounds";
            if (bodyPart == EBodyPart.Head && Brain != null)
            {
                center = Brain.WorldCenter(player);
                referenceName = "brain center";
                return center != Vector3.zero;
            }
            if (bodyPart == EBodyPart.Chest &&
                TryGetBodyPartBounds(player, bodyPart, out Bounds chestBounds))
            {
                center = chestBounds.center;
                referenceName = "chest collider center";
                return true;
            }
            if (bodyPart == EBodyPart.Stomach &&
                TryGetThoracicSpineSegment(player, out Vector3 chestTop,
                    out Vector3 stomachTop))
            {
                center = ClosestPointOnSegment(hitPoint, chestTop, stomachTop);
                referenceName = "torso centerline";
                return true;
            }

            if (!TryGetBoneSegments(player, bodyPart, out Transform firstStart,
                out Transform firstEnd, out Transform secondStart,
                out Transform secondEnd)) return false;
            float bestDistance = float.MaxValue;
            CaptureNearestSegmentCenter(hitPoint, firstStart, firstEnd,
                ref center, ref bestDistance);
            CaptureNearestSegmentCenter(hitPoint, secondStart, secondEnd,
                ref center, ref bestDistance);
            if (IsLeg(bodyPart) && firstEnd != null && secondStart != null &&
                firstEnd != secondStart)
                CaptureNearestSegmentCenter(hitPoint, firstEnd, secondStart,
                    ref center, ref bestDistance);
            if (bestDistance == float.MaxValue) return false;
            referenceName = IsLeg(bodyPart)
                ? "nearest leg bone center" : "nearest arm bone center";
            return true;
        }

        private static void CaptureNearestSegmentCenter(Vector3 hitPoint,
            Transform start, Transform end, ref Vector3 center,
            ref float bestDistance)
        {
            if (start == null || end == null) return;
            Vector3 candidate = ClosestPointOnSegment(hitPoint,
                start.position, end.position);
            float distance = (candidate - hitPoint).sqrMagnitude;
            if (distance >= bestDistance) return;
            bestDistance = distance;
            center = candidate;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 point,
            Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f) return start;
            float position = Mathf.Clamp01(Vector3.Dot(point - start,
                segment) / lengthSquared);
            return start + segment * position;
        }

        private static bool IsLeg(EBodyPart bodyPart)
        { return bodyPart == EBodyPart.LeftLeg || bodyPart == EBodyPart.RightLeg; }

        private static Transform Original(BifacialTransform bone)
        { return bone == null ? null : bone.Original; }

        private static Transform FindBone(IDictionary<string, Transform> bones, params string[] names)
        {
            if (bones == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                Transform found;
                if (bones.TryGetValue(names[i], out found) && found != null) return found;
            }
            return null;
        }

        internal static void ClearLimbBoneCache() { LimbCaches.Clear(); }

        internal sealed class LimbBoneCache
        {
            internal Transform LeftUpperArm, LeftElbow, LeftHand;
            internal Transform RightUpperArm, RightElbow, RightHand;
            internal Transform LeftHip, LeftKnee, LeftCalf, LeftFoot;
            internal Transform RightHip, RightKnee, RightCalf, RightFoot;
            private bool _complete;
            private float _nextRetry;

            internal void Resolve(Player player)
            {
                if (_complete || Time.unscaledTime < _nextRetry || player == null || player.PlayerBones == null) return;
                _nextRetry = Time.unscaledTime + 1f;
                PlayerBones bones = player.PlayerBones;
                LeftHip = LeftHip != null ? LeftHip : Original(bones.LeftThigh1);
                RightHip = RightHip != null ? RightHip : Original(bones.RightThigh1);
                LeftKnee = LeftKnee != null ? LeftKnee : Original(bones.LeftThigh2);
                RightKnee = RightKnee != null ? RightKnee : Original(bones.RightThigh2);
                LeftHand = LeftHand != null ? LeftHand : bones.LeftPalm;
                RightHand = RightHand != null ? RightHand : bones.RightPalm;

                IDictionary<string, Transform> skeleton = player.PlayerBody != null &&
                    player.PlayerBody.SkeletonRootJoint != null
                    ? player.PlayerBody.SkeletonRootJoint.Bones : null;
                // EFT's Shoulder references sit at the collarbone root, not the upper-arm joint.
                LeftUpperArm = LeftUpperArm != null ? LeftUpperArm : FindBone(skeleton,
                    "Base HumanLUpperarm", "HumanLUpperarm", "LeftUpperArm");
                RightUpperArm = RightUpperArm != null ? RightUpperArm : FindBone(skeleton,
                    "Base HumanRUpperarm", "HumanRUpperarm", "RightUpperArm");
                LeftElbow = LeftElbow != null ? LeftElbow : FindBone(skeleton,
                    "HumanLForearm1", "HumanLForearm2", "LeftForearm");
                RightElbow = RightElbow != null ? RightElbow : FindBone(skeleton,
                    "HumanRForearm1", "HumanRForearm2", "RightForearm");
                LeftCalf = LeftCalf != null ? LeftCalf : FindBone(skeleton, "HumanLCalf", "LeftCalf");
                RightCalf = RightCalf != null ? RightCalf : FindBone(skeleton, "HumanRCalf", "RightCalf");
                LeftFoot = LeftFoot != null ? LeftFoot : FindBone(skeleton, "HumanLFoot", "LeftFoot", "LFoot");
                RightFoot = RightFoot != null ? RightFoot : FindBone(skeleton, "HumanRFoot", "RightFoot", "RFoot");

                Transform[] hierarchy = null;
                if (LeftUpperArm == null || RightUpperArm == null ||
                    LeftElbow == null || RightElbow == null || LeftCalf == null ||
                    RightCalf == null || LeftFoot == null || RightFoot == null)
                    hierarchy = player.GetComponentsInChildren<Transform>(true);
                LeftUpperArm = LeftUpperArm != null ? LeftUpperArm : FindHierarchyBone(hierarchy,
                    "HumanLUpperarm", "LeftUpperArm");
                RightUpperArm = RightUpperArm != null ? RightUpperArm : FindHierarchyBone(hierarchy,
                    "HumanRUpperarm", "RightUpperArm");
                LeftElbow = LeftElbow != null ? LeftElbow : FindHierarchyBone(hierarchy,
                    "HumanLForearm1", "HumanLForearm2", "LeftForearm");
                RightElbow = RightElbow != null ? RightElbow : FindHierarchyBone(hierarchy,
                    "HumanRForearm1", "HumanRForearm2", "RightForearm");
                LeftCalf = LeftCalf != null ? LeftCalf : FindHierarchyBone(hierarchy,
                    "HumanLCalf", "LeftCalf");
                RightCalf = RightCalf != null ? RightCalf : FindHierarchyBone(hierarchy,
                    "HumanRCalf", "RightCalf");
                LeftFoot = LeftFoot != null ? LeftFoot : FindHierarchyBone(hierarchy,
                    "HumanLFoot", "LeftFoot", "LFoot");
                RightFoot = RightFoot != null ? RightFoot : FindHierarchyBone(hierarchy,
                    "HumanRFoot", "RightFoot", "RFoot");

                Animator[] animators = player.GetComponentsInChildren<Animator>(true);
                Animator animator = null;
                for (int i = 0; i < animators.Length; i++)
                    if (animators[i] != null && animators[i].isHuman) { animator = animators[i]; break; }
                if (animator != null)
                {
                    SetMissing(ref LeftUpperArm, animator, HumanBodyBones.LeftUpperArm);
                    SetMissing(ref LeftElbow, animator, HumanBodyBones.LeftLowerArm);
                    SetMissing(ref LeftHand, animator, HumanBodyBones.LeftHand);
                    SetMissing(ref RightUpperArm, animator, HumanBodyBones.RightUpperArm);
                    SetMissing(ref RightElbow, animator, HumanBodyBones.RightLowerArm);
                    SetMissing(ref RightHand, animator, HumanBodyBones.RightHand);
                    SetMissing(ref LeftHip, animator, HumanBodyBones.LeftUpperLeg);
                    SetMissing(ref LeftKnee, animator, HumanBodyBones.LeftLowerLeg);
                    SetMissing(ref LeftCalf, animator, HumanBodyBones.LeftLowerLeg);
                    SetMissing(ref LeftFoot, animator, HumanBodyBones.LeftFoot);
                    SetMissing(ref RightHip, animator, HumanBodyBones.RightUpperLeg);
                    SetMissing(ref RightKnee, animator, HumanBodyBones.RightLowerLeg);
                    SetMissing(ref RightCalf, animator, HumanBodyBones.RightLowerLeg);
                    SetMissing(ref RightFoot, animator, HumanBodyBones.RightFoot);
                }
                _complete = LeftUpperArm != null && LeftElbow != null && LeftHand != null &&
                    RightUpperArm != null && RightElbow != null && RightHand != null &&
                    LeftHip != null && LeftKnee != null && LeftFoot != null &&
                    RightHip != null && RightKnee != null && RightFoot != null;
            }

            private static void SetMissing(ref Transform target, Animator animator, HumanBodyBones bone)
            { if (target == null) target = animator.GetBoneTransform(bone); }

            private static Transform FindHierarchyBone(Transform[] hierarchy, params string[] names)
            {
                if (hierarchy == null) return null;
                for (int i = 0; i < hierarchy.Length; i++)
                {
                    Transform candidate = hierarchy[i];
                    if (candidate == null) continue;
                    for (int n = 0; n < names.Length; n++)
                        if (candidate.name.EndsWith(names[n], System.StringComparison.OrdinalIgnoreCase))
                            return candidate;
                }
                return null;
            }
        }
    }
}
