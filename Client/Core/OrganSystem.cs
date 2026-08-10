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
        internal readonly bool ArmorPenetrationEnabled;
        internal readonly bool BrainEnabled;
        internal readonly bool HeartEnabled;
        internal readonly bool CervicalSpineEnabled;
        internal readonly bool ThoracicSpineEnabled;

        internal TargetRules(float damageMultiplier, bool bodyTraumaEnabled,
            bool armorPenetrationEnabled, bool brainEnabled,
            bool heartEnabled, bool cervicalSpineEnabled, bool thoracicSpineEnabled)
        {
            DamageMultiplier = damageMultiplier;
            BodyTraumaEnabled = bodyTraumaEnabled;
            ArmorPenetrationEnabled = armorPenetrationEnabled;
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
            if (Anchor == OrganAnchor.Head)
            {
                Transform head = OrganSystem.GetHeadAnchor(player);
                return head == null ? Vector3.zero : head.TransformPoint(LocalOffset);
            }
            Transform ribcage = OrganSystem.GetChestAnchor(player);
            Transform pelvis = OrganSystem.GetPelvisAnchor(player);
            if (ribcage == null) return Vector3.zero;

            Vector3 chestCenter = pelvis != null
                ? Vector3.Lerp(pelvis.position, ribcage.position, 0.72f)
                : ribcage.position - ribcage.up * 0.18f;
            return chestCenter + ribcage.right * LocalOffset.x +
                   ribcage.up * LocalOffset.y + ribcage.forward * LocalOffset.z;
        }

        public Quaternion WorldRotation(Player player)
        {
            Transform anchor = GetAnchor(player);
            return anchor == null ? Quaternion.identity :
                anchor.rotation * Quaternion.Euler(LocalRotationEuler);
        }

        public bool IntersectsShot(Player player, Vector3 hitPoint, Vector3 direction,
            out Vector3 intersection, out float travelDistance)
        {
            intersection = hitPoint;
            travelDistance = 0f;
            Transform anchor = GetAnchor(player);
            if (anchor == null || direction.sqrMagnitude < 0.0001f) return false;

            Vector3 center = WorldCenter(player);
            Quaternion rotation = WorldRotation(player);
            Vector3 axisRight = rotation * Vector3.right;
            Vector3 axisUp = rotation * Vector3.up;
            Vector3 axisForward = rotation * Vector3.forward;
            Vector3 ray = direction.normalized;
            Vector3 originDelta = hitPoint - center;
            Vector3 localOrigin = new Vector3(Vector3.Dot(originDelta, axisRight),
                Vector3.Dot(originDelta, axisUp), Vector3.Dot(originDelta, axisForward));
            Vector3 localDirection = new Vector3(Vector3.Dot(ray, axisRight),
                Vector3.Dot(ray, axisUp), Vector3.Dot(ray, axisForward));
            if (Shape == OrganShape.Ellipsoid)
            {
                Vector3 e = HalfExtents;
                float a = localDirection.x * localDirection.x / (e.x * e.x) +
                          localDirection.y * localDirection.y / (e.y * e.y) +
                          localDirection.z * localDirection.z / (e.z * e.z);
                float b = 2f * (localOrigin.x * localDirection.x / (e.x * e.x) +
                          localOrigin.y * localDirection.y / (e.y * e.y) +
                          localOrigin.z * localDirection.z / (e.z * e.z));
                float c = localOrigin.x * localOrigin.x / (e.x * e.x) +
                          localOrigin.y * localOrigin.y / (e.y * e.y) +
                          localOrigin.z * localOrigin.z / (e.z * e.z) - 1f;
                float discriminant = b * b - 4f * a * c;
                if (a <= 0.000001f || discriminant < 0f) return false;
                float root = Mathf.Sqrt(discriminant);
                float near = (-b - root) / (2f * a);
                float far = (-b + root) / (2f * a);
                float entry = near >= 0f ? near : far >= 0f ? 0f : -1f;
                if (entry < 0f || entry > 0.55f) return false;
                travelDistance = entry;
                intersection = hitPoint + ray * entry;
                return true;
            }
            float enter = 0f;
            float exit = 0.55f;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = localOrigin[axis];
                float delta = localDirection[axis];
                float extent = HalfExtents[axis];
                if (Mathf.Abs(delta) < 0.00001f)
                {
                    if (origin < -extent || origin > extent) return false;
                    continue;
                }
                float near = (-extent - origin) / delta;
                float far = (extent - origin) / delta;
                if (near > far) { float swap = near; near = far; far = swap; }
                enter = Mathf.Max(enter, near);
                exit = Mathf.Min(exit, far);
                if (enter > exit) return false;
            }
            travelDistance = enter;
            intersection = hitPoint + ray * enter;
            return exit >= 0f && enter <= 0.55f;
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

        internal const float ArmBoneRadius = 0.0103125f;
        internal const float LegBoneRadius = 0.01875f;
        internal const float UpperSpineRadius = 0.021875f;
        internal const float ThoracicSpineRadius = 0.025f;

        internal static bool TryGetUpperSpineSegment(Player player,
            out Vector3 brainBase, out Vector3 chestTop)
        {
            brainBase = chestTop = Vector3.zero;
            Transform head = GetHeadAnchor(player);
            Transform ribcage = GetChestAnchor(player);
            if (head == null || ribcage == null || Brain == null) return false;

            brainBase = Brain.WorldCenter(player) -
                (Brain.WorldRotation(player) * Vector3.up) * Brain.HalfExtents.y +
                head.TransformVector(CervicalBrainEndOffset);
            chestTop = ribcage.position +
                ribcage.TransformVector(CervicalChestEndOffset);
            return (brainBase - chestTop).sqrMagnitude > 0.0001f;
        }

        internal static bool IntersectsUpperSpine(Player player, Vector3 hitPoint,
            Vector3 direction, out Vector3 intersection)
        {
            intersection = hitPoint;
            if (direction.sqrMagnitude < 0.0001f) return false;
            Vector3 brainBase, chestTop;
            if (!TryGetUpperSpineSegment(player, out brainBase, out chestTop)) return false;
            Vector3 shotEnd = hitPoint + direction.normalized * 0.55f;
            return SegmentCapsuleHit(hitPoint, shotEnd, brainBase, chestTop,
                UpperSpineRadius, out intersection);
        }

        internal static bool TryGetThoracicSpineSegment(Player player,
            out Vector3 chestTop, out Vector3 stomachTop)
        {
            chestTop = stomachTop = Vector3.zero;
            Transform ribcage = GetChestAnchor(player);
            Transform pelvis = GetPelvisAnchor(player);
            if (ribcage == null || pelvis == null) return false;

            chestTop = ribcage.position +
                ribcage.TransformVector(SpineChestEndOffset);
            stomachTop = pelvis.position +
                pelvis.TransformVector(SpinePelvisEndOffset);
            return (chestTop - stomachTop).sqrMagnitude > 0.0001f;
        }

        internal static bool IntersectsThoracicSpine(Player player, Vector3 hitPoint,
            Vector3 direction, out Vector3 intersection)
        {
            intersection = hitPoint;
            if (direction.sqrMagnitude < 0.0001f) return false;
            Vector3 chestTop, stomachTop;
            if (!TryGetThoracicSpineSegment(player, out chestTop, out stomachTop)) return false;
            Vector3 shotEnd = hitPoint + direction.normalized * 0.55f;
            return SegmentCapsuleHit(hitPoint, shotEnd, chestTop, stomachTop,
                ThoracicSpineRadius, out intersection);
        }

        internal static bool TryGetBoneSegments(Player player, EBodyPart bodyPart,
            out Transform firstStart, out Transform firstEnd,
            out Transform secondStart, out Transform secondEnd)
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
                    firstStart = cache.LeftShoulder; firstEnd = cache.LeftElbow;
                    secondStart = cache.LeftElbow; secondEnd = cache.LeftHand;
                    break;
                case EBodyPart.RightArm:
                    firstStart = cache.RightShoulder; firstEnd = cache.RightElbow;
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
            return firstStart != null && firstEnd != null || secondStart != null && secondEnd != null;
        }

        internal static bool IntersectsLimbBone(Player player, EBodyPart bodyPart,
            Vector3 hitPoint, Vector3 direction, out Vector3 intersection)
        {
            intersection = hitPoint;
            if (direction.sqrMagnitude < 0.0001f) return false;
            Transform a, b, c, d;
            if (!TryGetBoneSegments(player, bodyPart, out a, out b, out c, out d)) return false;
            Vector3 shotEnd = hitPoint + direction.normalized * 0.55f;
            float radius = bodyPart == EBodyPart.LeftArm || bodyPart == EBodyPart.RightArm
                ? ArmBoneRadius : LegBoneRadius;
            if (a != null && b != null && SegmentCapsuleHit(hitPoint, shotEnd, a.position, b.position, radius, out intersection)) return true;
            if (c != null && d != null && SegmentCapsuleHit(hitPoint, shotEnd,
                c.position, d.position, radius, out intersection)) return true;
            return IsLeg(bodyPart) && b != null && c != null && b != c &&
                SegmentCapsuleHit(hitPoint, shotEnd, b.position, c.position,
                    LegBoneRadius, out intersection);
        }

        private static bool IsLeg(EBodyPart bodyPart)
        { return bodyPart == EBodyPart.LeftLeg || bodyPart == EBodyPart.RightLeg; }

        private static bool SegmentCapsuleHit(Vector3 shotStart, Vector3 shotEnd,
            Vector3 boneStart, Vector3 boneEnd, float radius, out Vector3 shotClosest)
        {
            Vector3 u = shotEnd - shotStart, v = boneEnd - boneStart, w = shotStart - boneStart;
            float a = Vector3.Dot(u, u), b = Vector3.Dot(u, v), c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w), e = Vector3.Dot(v, w);
            float denominator = a * c - b * b;
            float s = denominator > 0.000001f ? Mathf.Clamp01((b * e - c * d) / denominator) : 0f;
            float t = c > 0.000001f ? Mathf.Clamp01((b * s + e) / c) : 0f;
            s = a > 0.000001f ? Mathf.Clamp01((b * t - d) / a) : 0f;
            Vector3 onShot = shotStart + u * s;
            Vector3 onBone = boneStart + v * t;
            shotClosest = onShot;
            return (onShot - onBone).sqrMagnitude <= radius * radius;
        }

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
            internal Transform LeftShoulder, LeftElbow, LeftHand;
            internal Transform RightShoulder, RightElbow, RightHand;
            internal Transform LeftHip, LeftKnee, LeftCalf, LeftFoot;
            internal Transform RightHip, RightKnee, RightCalf, RightFoot;
            private bool _complete;
            private float _nextRetry;

            internal void Resolve(Player player)
            {
                if (_complete || Time.unscaledTime < _nextRetry || player == null || player.PlayerBones == null) return;
                _nextRetry = Time.unscaledTime + 1f;
                PlayerBones bones = player.PlayerBones;
                LeftShoulder = LeftShoulder != null ? LeftShoulder : Original(bones.LeftShoulder);
                RightShoulder = RightShoulder != null ? RightShoulder : Original(bones.RightShoulder);
                LeftHip = LeftHip != null ? LeftHip : Original(bones.LeftThigh1);
                RightHip = RightHip != null ? RightHip : Original(bones.RightThigh1);
                LeftKnee = LeftKnee != null ? LeftKnee : Original(bones.LeftThigh2);
                RightKnee = RightKnee != null ? RightKnee : Original(bones.RightThigh2);
                LeftHand = LeftHand != null ? LeftHand : bones.LeftPalm;
                RightHand = RightHand != null ? RightHand : bones.RightPalm;

                IDictionary<string, Transform> skeleton = player.PlayerBody != null &&
                    player.PlayerBody.SkeletonRootJoint != null
                    ? player.PlayerBody.SkeletonRootJoint.Bones : null;
                LeftElbow = LeftElbow != null ? LeftElbow : FindBone(skeleton,
                    "HumanLForearm1", "HumanLForearm2", "LeftForearm");
                RightElbow = RightElbow != null ? RightElbow : FindBone(skeleton,
                    "HumanRForearm1", "HumanRForearm2", "RightForearm");
                LeftCalf = LeftCalf != null ? LeftCalf : FindBone(skeleton, "HumanLCalf", "LeftCalf");
                RightCalf = RightCalf != null ? RightCalf : FindBone(skeleton, "HumanRCalf", "RightCalf");
                LeftFoot = LeftFoot != null ? LeftFoot : FindBone(skeleton, "HumanLFoot", "LeftFoot", "LFoot");
                RightFoot = RightFoot != null ? RightFoot : FindBone(skeleton, "HumanRFoot", "RightFoot", "RFoot");

                Transform[] hierarchy = null;
                if (LeftElbow == null || RightElbow == null || LeftCalf == null ||
                    RightCalf == null || LeftFoot == null || RightFoot == null)
                    hierarchy = player.GetComponentsInChildren<Transform>(true);
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
                    SetMissing(ref LeftShoulder, animator, HumanBodyBones.LeftUpperArm);
                    SetMissing(ref LeftElbow, animator, HumanBodyBones.LeftLowerArm);
                    SetMissing(ref LeftHand, animator, HumanBodyBones.LeftHand);
                    SetMissing(ref RightShoulder, animator, HumanBodyBones.RightUpperArm);
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
                _complete = LeftShoulder != null && LeftElbow != null && LeftHand != null &&
                    RightShoulder != null && RightElbow != null && RightHand != null &&
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
