using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace TraumaCore
{
    internal readonly struct AnatomyOverlayLine
    {
        internal readonly Vector3 Start;
        internal readonly Vector3 End;
        internal readonly Color Color;
        internal readonly float ScreenThickness;
        internal readonly float WorldRadius;

        internal AnatomyOverlayLine(Vector3 start, Vector3 end, Color color,
            float screenThickness, float worldRadius = 0f)
        {
            Start = start;
            End = end;
            Color = color;
            ScreenThickness = screenThickness;
            WorldRadius = worldRadius;
        }
    }

    internal static class AnatomyOverlayGeometry
    {
        private const int RingSegments = 32;
        private const int OrganEllipsoidSegments = 36;
        private const int EllipsoidLatitudeSteps = 6;
        private const int EllipsoidArcSegments = 12;
        private const int EllipsoidLongitudeCount = 8;
        private static readonly int[] BoxEdgeStart =
            { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] BoxEdgeEnd =
            { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

        internal static void Build(Player player, TargetRules rules,
            List<AnatomyOverlayLine> lines)
        {
            if (player == null || !rules.BodyTraumaEnabled || lines == null)
                return;
            Build(AnatomyRig.FromPlayer(player), player, rules, lines);
        }

        internal static void Build(AnatomyRig rig, Player limbPlayer,
            TargetRules rules, List<AnatomyOverlayLine> lines,
            IReadOnlyDictionary<Transform, Transform> bonesBySource = null,
            IReadOnlyDictionary<EBodyPart, LimbAnchors> limbAnchors = null)
        {
            if (!rules.BodyTraumaEnabled || lines == null)
                return;
            if (rules.HeartEnabled)
                AddOrganOutline(rig, OrganSystem.Heart, lines);
            if (rules.BrainEnabled)
            {
                AddBrainUnion(rig, lines);
                AddSkullVolumes(rig, lines);
            }
            if (rules.CervicalSpineEnabled)
                AddSpine(rig, true, lines);
            if (rules.ThoracicSpineEnabled)
                AddSpine(rig, false, lines);
            AddRibcage(rig, lines);
            AddLimbBones(limbPlayer, lines, bonesBySource, limbAnchors);
        }

        private static void AddOrganOutline(AnatomyRig rig,
            OrganDefinition organ, List<AnatomyOverlayLine> lines)
        {
            if (!AnatomyPoseSystem.TryGetOrgan(rig, organ,
                    out BoneVolumePose pose)) return;
            Color color = ResolveOrganColor(organ);
            if (organ.Shape == OrganShape.Ellipsoid)
                AddOrganEllipsoidOutline(pose, color, lines);
            else AddBoxVolume(pose, color, lines);
        }

        private static void AddBrainUnion(AnatomyRig rig,
            List<AnatomyOverlayLine> lines)
        {
            if (!AnatomyPoseSystem.TryGetOrgan(rig, OrganSystem.Brain,
                    out BoneVolumePose upperBrain) ||
                !AnatomyPoseSystem.TryGetOrgan(rig, OrganSystem.LowerBrain,
                    out BoneVolumePose lowerBrain)) return;
            AddEllipsoidVolume(upperBrain, ResolveOrganColor(OrganSystem.Brain),
                lines, lowerBrain);
            AddEllipsoidVolume(lowerBrain,
                ResolveOrganColor(OrganSystem.LowerBrain), lines, upperBrain);
        }

        private static void AddSkullVolumes(AnatomyRig rig,
            List<AnatomyOverlayLine> lines)
        {
            if (!AnatomyPoseSystem.TryGetSkull(rig,
                    out BoneVolumePose firstSkull) ||
                !AnatomyPoseSystem.TryGetSecondSkull(rig,
                    out BoneVolumePose secondSkull)) return;
            Color color = new Color(0.82f, 0.88f, 1f,
                OrganSystem.BoneEspOpacity.Value);
            AddEllipsoidVolume(firstSkull, color, lines, secondSkull);
            AddEllipsoidVolume(secondSkull, color, lines, firstSkull);
        }

        private static void AddOrganEllipsoidOutline(BoneVolumePose pose,
            Color color, List<AnatomyOverlayLine> lines)
        {
            Vector3 radius = pose.Size * 0.5f;
            for (int ring = 0; ring < 3; ring++)
            {
                Vector3 previous = default;
                for (int segment = 0; segment <= OrganEllipsoidSegments;
                    segment++)
                {
                    float angle = segment * Mathf.PI * 2f /
                        OrganEllipsoidSegments;
                    Vector3 local = ring == 0
                        ? new Vector3(radius.x * Mathf.Cos(angle),
                            radius.y * Mathf.Sin(angle), 0f)
                        : ring == 1
                            ? new Vector3(radius.x * Mathf.Cos(angle), 0f,
                                radius.z * Mathf.Sin(angle))
                            : new Vector3(0f, radius.y * Mathf.Cos(angle),
                                radius.z * Mathf.Sin(angle));
                    Vector3 point = pose.TransformPoint(local);
                    if (segment > 0) AddLine(previous, point, color, lines);
                    previous = point;
                }
            }
        }

        private static void AddBoxVolume(BoneVolumePose pose, Color color,
            List<AnatomyOverlayLine> lines)
        {
            Vector3 half = pose.Size * 0.5f;
            Vector3[] corners = new Vector3[8];
            for (int index = 0; index < corners.Length; index++)
                corners[index] = pose.TransformPoint(new Vector3(
                    (index & 1) == 0 ? -half.x : half.x,
                    (index & 2) == 0 ? -half.y : half.y,
                    (index & 4) == 0 ? -half.z : half.z));
            for (int index = 0; index < BoxEdgeStart.Length; index++)
                AddLine(corners[BoxEdgeStart[index]], corners[BoxEdgeEnd[index]],
                    color, lines);
        }

        private static void AddEllipsoidVolume(BoneVolumePose pose, Color color,
            List<AnatomyOverlayLine> lines, BoneVolumePose? overlappingPose)
        {
            Vector3 radius = pose.Size * 0.5f;
            for (int latitudeIndex = -3; latitudeIndex <= 3; latitudeIndex++)
            {
                float latitude = latitudeIndex * Mathf.PI / EllipsoidLatitudeSteps;
                AddEllipsoidRing(pose, Mathf.Sin(latitude) * radius.y,
                    Mathf.Cos(latitude) * radius.x,
                    Mathf.Cos(latitude) * radius.z, color, lines, overlappingPose);
            }
            for (int longitudeIndex = 0;
                longitudeIndex < EllipsoidLongitudeCount; longitudeIndex++)
            {
                float longitude = longitudeIndex * Mathf.PI * 2f /
                    EllipsoidLongitudeCount;
                Vector3 previous = default;
                for (int segment = 0; segment <= EllipsoidArcSegments; segment++)
                {
                    float latitude = Mathf.Lerp(-Mathf.PI * 0.5f,
                        Mathf.PI * 0.5f, segment / (float)EllipsoidArcSegments);
                    Vector3 point = pose.TransformPoint(new Vector3(
                        Mathf.Cos(latitude) * Mathf.Cos(longitude) * radius.x,
                        Mathf.Sin(latitude) * radius.y,
                        Mathf.Cos(latitude) * Mathf.Sin(longitude) * radius.z));
                    if (segment > 0)
                        AddUnionLine(previous, point, color, lines, overlappingPose);
                    previous = point;
                }
            }
        }

        private static void AddEllipsoidRing(BoneVolumePose pose, float y,
            float radiusX, float radiusZ, Color color,
            List<AnatomyOverlayLine> lines, BoneVolumePose? overlappingPose)
        {
            Vector3 previous = default;
            for (int segment = 0; segment <= RingSegments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / RingSegments;
                Vector3 point = pose.TransformPoint(new Vector3(
                    Mathf.Cos(angle) * radiusX, y,
                    Mathf.Sin(angle) * radiusZ));
                if (segment > 0)
                    AddUnionLine(previous, point, color, lines, overlappingPose);
                previous = point;
            }
        }

        private static void AddUnionLine(Vector3 start, Vector3 end, Color color,
            List<AnatomyOverlayLine> lines, BoneVolumePose? overlappingPose)
        {
            if (!overlappingPose.HasValue || !AnatomyIntersections.IsInsideEllipsoid(
                    overlappingPose.Value, (start + end) * 0.5f))
                AddLine(start, end, color, lines);
        }

        private static void AddRibcage(AnatomyRig rig,
            List<AnatomyOverlayLine> lines)
        {
            if (!AnatomyPoseSystem.TryGetRibcage(rig,
                    out RibcageShape ribcage) || ribcage.Rings == null) return;
            Color color = new Color(0.95f, 0.82f, 0.55f,
                OrganSystem.RibcageEspOpacity.Value);
            for (int ring = 0; ring < ribcage.Rings.Length; ring++)
            {
                Vector3 previous = ribcage.Rings[ring].Point(0f);
                for (int segment = 1; segment <= RingSegments; segment++)
                {
                    Vector3 point = ribcage.Rings[ring].Point(segment * Mathf.PI *
                        2f / RingSegments);
                    AddLine(previous, point, color, lines);
                    previous = point;
                }
            }
            for (int segment = 0; segment < RingSegments; segment += 4)
            {
                float angle = segment * Mathf.PI * 2f / RingSegments;
                for (int ring = 0; ring < ribcage.Rings.Length - 1; ring++)
                    AddLine(ribcage.Rings[ring].Point(angle),
                        ribcage.Rings[ring + 1].Point(angle), color, lines);
            }
        }

        private static void AddSpine(AnatomyRig rig, bool isUpper,
            List<AnatomyOverlayLine> lines)
        {
            bool found = isUpper
                ? AnatomyPoseSystem.TryGetUpperSpine(rig, out BoneSegmentPose spine)
                : AnatomyPoseSystem.TryGetThoracicSpine(rig, out spine);
            if (!found) return;
            Color color = isUpper
                ? new Color(0.05f, 1f, 0.75f, OrganSystem.BoneEspOpacity.Value)
                : new Color(1f, 0.78f, 0.05f, OrganSystem.BoneEspOpacity.Value);
            AddCylinder(spine, color, lines);
        }

        private static void AddLimbBones(Player player,
            List<AnatomyOverlayLine> lines,
            IReadOnlyDictionary<Transform, Transform> bonesBySource,
            IReadOnlyDictionary<EBodyPart, LimbAnchors> limbAnchors)
        {
            AddLimbBones(player, EBodyPart.LeftArm,
                new Color(0.1f, 0.85f, 1f, OrganSystem.BoneEspOpacity.Value), lines, bonesBySource, limbAnchors);
            AddLimbBones(player, EBodyPart.RightArm,
                new Color(0.1f, 0.85f, 1f, OrganSystem.BoneEspOpacity.Value), lines, bonesBySource, limbAnchors);
            AddLimbBones(player, EBodyPart.LeftLeg,
                new Color(0.2f, 1f, 0.55f, OrganSystem.BoneEspOpacity.Value), lines, bonesBySource, limbAnchors);
            AddLimbBones(player, EBodyPart.RightLeg,
                new Color(0.2f, 1f, 0.55f, OrganSystem.BoneEspOpacity.Value), lines, bonesBySource, limbAnchors);
        }

        private static void AddLimbBones(Player player, EBodyPart bodyPart,
            Color color, List<AnatomyOverlayLine> lines,
            IReadOnlyDictionary<Transform, Transform> bonesBySource,
            IReadOnlyDictionary<EBodyPart, LimbAnchors> limbAnchors)
        {
            for (int boneIndex = 0; boneIndex < LimbBoneGeometry.GetBoneCount(bodyPart); boneIndex++)
            {
                BoneSegmentPose cylinder;
                bool hasCylinder = limbAnchors == null
                    ? LimbBoneGeometry.TryGetCylinder(player, bodyPart, boneIndex, out cylinder, bonesBySource)
                    : LimbBoneGeometry.TryGetCylinder(bodyPart, boneIndex,
                        limbAnchors.TryGetValue(bodyPart, out LimbAnchors anchors) ? anchors : default, out cylinder);
                if (!hasCylinder) continue;
                Color boneColor = boneIndex == 2
                    ? new Color(1f, 0.55f, 0.15f, color.a) : color;
                AddCylinder(cylinder, boneColor, lines);
            }
        }
        private static void AddCylinder(BoneSegmentPose cylinder, Color color,
            List<AnatomyOverlayLine> lines)
        {
            Vector3 axis = (cylinder.End - cylinder.Start).normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.95f
                ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Cross(axis, reference).normalized * cylinder.Radius;
            Vector3 forward = Vector3.Cross(axis, right).normalized * cylinder.Radius;
            Vector3 previous = right;
            for (int segment = 1; segment <= RingSegments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / RingSegments;
                Vector3 offset = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                float endScale = cylinder.EndRadius / cylinder.Radius;
                AddLine(cylinder.Start + previous, cylinder.Start + offset, color, lines);
                AddLine(cylinder.End + previous * endScale, cylinder.End + offset * endScale, color, lines);
                if (segment % 4 == 0)
                    AddLine(cylinder.Start + offset, cylinder.End + offset * endScale, color, lines);
                previous = offset;
            }
            AddMarker(cylinder.Start, color, lines);
            AddMarker(cylinder.End, color, lines);
        }

        private static void AddMarker(Vector3 point, Color color,
            List<AnatomyOverlayLine> lines)
        {
            lines.Add(new AnatomyOverlayLine(point, point, color, 3f, -0.012f));
        }

        private static void AddLine(Vector3 start, Vector3 end, Color color,
            List<AnatomyOverlayLine> lines)
        {
            lines.Add(new AnatomyOverlayLine(start, end, color, 2f));
        }

        private static Color ResolveOrganColor(OrganDefinition organ)
        {
            Color color = organ.Color;
            color.a = organ == OrganSystem.Heart
                ? OrganSystem.HeartEspOpacity.Value
                : OrganSystem.BrainEspOpacity.Value;
            return color;
        }
    }
}
