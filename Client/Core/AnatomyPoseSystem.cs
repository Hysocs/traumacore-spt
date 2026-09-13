using EFT;
using UnityEngine;

namespace TraumaCore
{
    internal readonly struct AnatomyAnchor
    {
        private readonly Transform _transform;
        private readonly BifacialTransform _bone;

        internal AnatomyAnchor(Transform transform)
        {
            _transform = transform;
            _bone = null;
        }

        internal AnatomyAnchor(BifacialTransform bone)
        {
            _transform = null;
            _bone = bone;
        }

        internal bool HasValue => _transform != null || _bone?.Original != null;
        internal Vector3 Position => _bone == null ? _transform.position : _bone.position;
        internal Quaternion Rotation => _bone == null ? _transform.rotation : _bone.rotation;
        internal Vector3 Forward => _bone == null ? _transform.forward : _bone.forward;
        internal Vector3 Up => _bone == null ? _transform.up : _bone.up;
        internal Vector3 TransformPoint(Vector3 point) => _bone == null
            ? _transform.TransformPoint(point) : _bone.TransformPoint(point);
        internal Vector3 TransformVector(Vector3 vector) => _bone == null
            ? _transform.TransformVector(vector) : _bone.TransformVector(vector);
    }

    internal readonly struct BoneVolumePose
    {
        internal readonly Vector3 Center;
        internal readonly Quaternion Rotation;
        internal readonly Vector3 Size;

        internal BoneVolumePose(Vector3 center, Quaternion rotation,
            Vector3 size)
        {
            Center = center;
            Rotation = rotation;
            Size = size;
        }

        internal Vector3 TransformPoint(Vector3 local) =>
            Center + Rotation * local;
    }

    internal readonly struct AnatomyRig
    {
        internal readonly AnatomyAnchor Head;
        internal readonly AnatomyAnchor Chest;
        internal readonly AnatomyAnchor Pelvis;
        internal readonly Vector3 Forward;
        internal readonly Transform LeftCollarbone;
        internal readonly Transform RightCollarbone;

        internal AnatomyRig(Transform head, Transform chest, Transform pelvis,
            Vector3 forward, Transform leftCollarbone = null,
            Transform rightCollarbone = null)
        {
            Head = new AnatomyAnchor(head);
            Chest = new AnatomyAnchor(chest);
            Pelvis = new AnatomyAnchor(pelvis);
            Forward = forward;
            LeftCollarbone = leftCollarbone;
            RightCollarbone = rightCollarbone;
        }

        internal AnatomyRig(BifacialTransform head, BifacialTransform chest,
            BifacialTransform pelvis, Vector3 forward)
        {
            Head = new AnatomyAnchor(head);
            Chest = new AnatomyAnchor(chest);
            Pelvis = new AnatomyAnchor(pelvis);
            Forward = forward;
            LeftCollarbone = null;
            RightCollarbone = null;
        }

        internal static AnatomyRig FromPlayer(Player player)
        {
            return new AnatomyRig(OrganSystem.GetHeadAnchor(player),
                OrganSystem.GetChestAnchor(player),
                OrganSystem.GetPelvisAnchor(player), player == null
                    ? Vector3.zero : player.Transform.forward);
        }

        internal static AnatomyRig FromPlayerBones(PlayerBones bones,
            Vector3 forward)
        {
            return new AnatomyRig(bones?.Head, bones?.Ribcage,
                bones?.Pelvis, forward);
        }

        internal static AnatomyRig FromMesh(Transform root)
        {
            Transform[] bones = MeshSkeleton.ResolveBones(root);
            return new AnatomyRig(
                MeshSkeleton.Find(bones, "Base HumanHead"),
                MeshSkeleton.Find(bones, "Base HumanRibcage"),
                MeshSkeleton.Find(bones, "Base HumanPelvis"), Vector3.zero,
                MeshSkeleton.Find(bones, "Base HumanLCollarbone"),
                MeshSkeleton.Find(bones, "Base HumanRCollarbone"));
        }
    }

    internal readonly struct BoneSegmentPose
    {
        internal readonly Vector3 Start;
        internal readonly Vector3 End;
        internal readonly float Radius;
        internal readonly float EndRadius;

        internal BoneSegmentPose(Vector3 start, Vector3 end, float radius)
            : this(start, end, radius, radius)
        {
        }

        internal BoneSegmentPose(Vector3 start, Vector3 end, float radius, float endRadius)
        {
            Start = start;
            End = end;
            Radius = radius;
            EndRadius = endRadius;
        }
    }

    internal readonly struct RibcageRing
    {
        internal readonly Vector3 Center;
        internal readonly Quaternion Rotation;
        internal readonly float RadiusX;
        internal readonly float RadiusZ;

        internal RibcageRing(Vector3 center, Quaternion rotation,
            float radiusX, float radiusZ)
        {
            Center = center;
            Rotation = rotation;
            RadiusX = radiusX;
            RadiusZ = radiusZ;
        }

        internal Vector3 Point(float angle) => Center + Rotation * new Vector3(
            Mathf.Cos(angle) * RadiusX, 0f, Mathf.Sin(angle) * RadiusZ);
    }

    internal readonly struct RibcageShape
    {
        internal readonly RibcageRing[] Rings;

        internal RibcageShape(RibcageRing[] rings) { Rings = rings; }
    }

    internal static class AnatomyPoseSystem
    {
        private static readonly Vector3[] RibcageCenters =
        {
            new Vector3(0f, -334.39f, 42.25f) / 1000f,
            new Vector3(0f, -267.38f, 37.56f) / 1000f,
            new Vector3(0f, -195.67f, 28.17f) / 1000f,
            new Vector3(0f, -114.57f, 18.78f) / 1000f,
            new Vector3(0f, -52.25f, 9.39f) / 1000f,
            new Vector3(0f, 19.46f, 4.69f) / 1000f
        };
        private static readonly Vector2[] RibcageWidths =
        {
            new Vector2(314.51f, 264.82f) / 1000f,
            new Vector2(331.13f, 269.58f) / 1000f,
            new Vector2(365.31f, 278.43f) / 1000f,
            new Vector2(398.69f, 261.41f) / 1000f,
            new Vector2(308.87f, 202.52f) / 1000f,
            new Vector2(212.91f, 139.6f) / 1000f
        };
        internal static readonly Vector3 Skull1CenterOffset =
            new Vector3(-92.01f, 22.06f, 0.25f) / 1000f;
        internal static readonly Vector3 Skull1Size =
            new Vector3(166.9f, 211.5f, 160f) / 1000f;
        internal static bool TryGetOrgan(AnatomyRig rig,
            OrganDefinition organ, out BoneVolumePose pose)
        {
            pose = default;
            if (organ == null) return false;
            if (organ.Anchor == OrganAnchor.Head)
                return TryCreateAnchoredVolume(rig.Head, organ.LocalOffset,
                    organ.LocalRotationEuler, organ.HalfExtents * 2f, out pose);
            if (!TryResolveTorsoRotation(rig, out Quaternion torsoRotation) ||
                !rig.Chest.HasValue) return false;
            Vector3 center = !rig.Pelvis.HasValue
                ? rig.Chest.Position - torsoRotation * Vector3.up * 0.18f
                : Vector3.Lerp(rig.Pelvis.Position, rig.Chest.Position, 0.72f);
            pose = new BoneVolumePose(center + torsoRotation * organ.LocalOffset,
                torsoRotation * Quaternion.Euler(organ.LocalRotationEuler),
                organ.HalfExtents * 2f);
            return true;
        }

        internal static bool TryGetSkull(AnatomyRig rig,
            out BoneVolumePose pose)
        {
            return TryCreateAnchoredVolume(rig.Head, Skull1CenterOffset,
                Vector3.zero, Skull1Size, out pose);
        }

        internal static bool TryGetSecondSkull(AnatomyRig rig,
            out BoneVolumePose pose)
        {
            return TryCreateAnchoredVolume(rig.Head,
                OrganSystem.Skull2Offset, new Vector3(0f, 0f, -90f),
                OrganSystem.Skull2Size, out pose);
        }

        internal static bool TryGetRibcage(AnatomyRig rig,
            out RibcageShape shape)
        {
            shape = default;
            if (!rig.Chest.HasValue ||
                !TryResolveTorsoRotation(rig, out Quaternion torsoRotation))
                return false;
            RibcageRing[] rings = new RibcageRing[RibcageCenters.Length];
            for (int index = 0; index < rings.Length; index++)
            {
                Vector3 localCenter = RibcageCenters[index];
                Vector2 widths = RibcageWidths[index];
                rings[index] = new RibcageRing(rig.Chest.Position +
                    torsoRotation * localCenter, torsoRotation,
                    widths.x * 0.5f, widths.y * 0.5f);
            }
            shape = new RibcageShape(rings);
            return true;
        }

        internal static bool TryGetUpperSpine(AnatomyRig rig,
            out BoneSegmentPose segment)
        {
            segment = default;
            if (!rig.Head.HasValue || !rig.Chest.HasValue ||
                !TryGetOrgan(rig, OrganSystem.Brain,
                    out BoneVolumePose brain)) return false;
            Vector3 start = brain.Center - brain.Rotation * Vector3.up *
                OrganSystem.Brain.HalfExtents.y + rig.Head.TransformVector(
                    OrganSystem.CervicalBrainEndOffset);
            Vector3 end = rig.Chest.Position + rig.Chest.TransformVector(
                OrganSystem.CervicalChestEndOffset);
            segment = SpineBoneGeometry.Apply(rig, true, start, end);
            return (segment.Start - segment.End).sqrMagnitude >= 0.0001f;
        }

        internal static bool TryGetThoracicSpine(AnatomyRig rig,
            out BoneSegmentPose segment)
        {
            segment = default;
            if (!rig.Chest.HasValue || !rig.Pelvis.HasValue) return false;
            Vector3 start = rig.Chest.Position + rig.Chest.TransformVector(
                OrganSystem.SpineChestEndOffset);
            Vector3 end = rig.Pelvis.Position + rig.Pelvis.TransformVector(
                OrganSystem.SpinePelvisEndOffset);
            segment = SpineBoneGeometry.Apply(rig, false, start, end);
            return (segment.Start - segment.End).sqrMagnitude >= 0.0001f;
        }

        internal static bool TryResolveTorsoRotation(AnatomyRig rig,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!rig.Chest.HasValue) return false;
            Vector3 up = !rig.Pelvis.HasValue
                ? rig.Chest.Up : rig.Chest.Position - rig.Pelvis.Position;
            if (up.sqrMagnitude < 0.0001f) up = rig.Chest.Up;
            up.Normalize();
            if (rig.LeftCollarbone != null && rig.RightCollarbone != null)
            {
                Vector3 right = rig.RightCollarbone.position - rig.LeftCollarbone.position;
                Vector3 anatomicalForward = Vector3.Cross(right, up);
                if (anatomicalForward.sqrMagnitude < 0.0001f) return false;
                rotation = Quaternion.LookRotation(anatomicalForward.normalized, up);
                return true;
            }
            Vector3 forward = Vector3.ProjectOnPlane(rig.Forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(rig.Chest.Forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.Cross(up, Vector3.right);
            rotation = Quaternion.LookRotation(forward.normalized, up);
            return true;
        }

        private static bool TryCreateAnchoredVolume(AnatomyAnchor anchor,
            Vector3 offset, Vector3 rotation, Vector3 size,
            out BoneVolumePose pose)
        {
            pose = default;
            if (!anchor.HasValue) return false;
            pose = new BoneVolumePose(anchor.TransformPoint(offset),
                anchor.Rotation * Quaternion.Euler(rotation), size);
            return true;
        }
    }
}
