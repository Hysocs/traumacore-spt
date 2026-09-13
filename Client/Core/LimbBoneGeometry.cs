using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace TraumaCore
{
    internal readonly struct LimbAnchors
    {
        internal readonly Transform UpperStart, UpperEnd, LowerStart, LowerEnd;
        internal LimbAnchors(Transform upperStart, Transform upperEnd, Transform lowerStart, Transform lowerEnd)
        {
            UpperStart = upperStart;
            UpperEnd = upperEnd;
            LowerStart = lowerStart;
            LowerEnd = lowerEnd;
        }
    }

    internal readonly struct LimbBoneDimensions
    {
        internal readonly Vector3 StartOffsetMillimeters;
        internal readonly Vector3 EndOffsetMillimeters;
        internal readonly float DiameterMillimeters;
        internal readonly float EndDiameterMillimeters;

        internal LimbBoneDimensions(Vector3 startOffset, Vector3 endOffset, float diameter)
            : this(startOffset, endOffset, diameter, diameter)
        {
        }

        internal LimbBoneDimensions(Vector3 startOffset, Vector3 endOffset,
            float diameter, float endDiameter)
        {
            StartOffsetMillimeters = startOffset;
            EndOffsetMillimeters = endOffset;
            DiameterMillimeters = diameter;
            EndDiameterMillimeters = endDiameter;
        }
    }

    internal static class LimbBoneGeometry
    {
        internal static int GetBoneCount(EBodyPart bodyPart) =>
            bodyPart == EBodyPart.LeftArm || bodyPart == EBodyPart.RightArm ? 3 :
            bodyPart == EBodyPart.LeftLeg || bodyPart == EBodyPart.RightLeg ? 2 : 0;
        private const float UpperArmElbowDiameter = 30.32f;
        private const float UpperArmShoulderDiameter = 36.69f;
        // Final limb calibration captured from the installed BepInEx config,
        // with two successive 10% shoulder increases, rounded to 0.01 mm.
        private static readonly LimbBoneDimensions[] LeftArmBones =
        {
            new(Vector3.zero, Vector3.zero, UpperArmShoulderDiameter, UpperArmElbowDiameter),
            new(new Vector3(-10f, 0f, 0f), new Vector3(-10f, 0f, 0f), 12f),
            new(new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 0f), 12f)
        };
        private static readonly LimbBoneDimensions[] RightArmBones =
        {
            new(Vector3.zero, Vector3.zero, UpperArmShoulderDiameter, UpperArmElbowDiameter),
            new(new Vector3(-10f, 0f, 0f), new Vector3(-10f, 0f, 0f), 10f),
            new(new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 0f), 10f)
        };

        private static readonly LimbBoneDimensions[] LegBones =
        {
            new(Vector3.zero, Vector3.zero, 55f, 45f),
            new(Vector3.zero, Vector3.zero, 45f, 37.5f)
        };

        internal static bool IsLimb(EBodyPart bodyPart) =>
            bodyPart == EBodyPart.LeftArm || bodyPart == EBodyPart.RightArm ||
            bodyPart == EBodyPart.LeftLeg || bodyPart == EBodyPart.RightLeg;

        internal static LimbBoneDimensions ResolveDimensions(EBodyPart bodyPart, int boneIndex) =>
            bodyPart switch
            {
                EBodyPart.LeftArm => LeftArmBones[boneIndex],
                EBodyPart.RightArm => RightArmBones[boneIndex],
                EBodyPart.LeftLeg or EBodyPart.RightLeg => LegBones[boneIndex],
                _ => throw new System.ArgumentOutOfRangeException(nameof(bodyPart))
            };

        internal static bool TryGetCylinder(Player player, EBodyPart bodyPart,
            int boneIndex, out BoneSegmentPose cylinder,
            IReadOnlyDictionary<Transform, Transform> bonesBySource = null)
        {
            cylinder = default;
            if (!IsLimb(bodyPart) || boneIndex < 0 || boneIndex >= GetBoneCount(bodyPart) ||
                !OrganSystem.TryGetBoneSegments(player, bodyPart,
                    out Transform upperStart, out Transform upperEnd,
                    out Transform lowerStart, out Transform lowerEnd, bonesBySource)) return false;
            return TryGetCylinder(bodyPart, boneIndex,
                new LimbAnchors(upperStart, upperEnd, lowerStart, lowerEnd), out cylinder);
        }

        internal static bool TryGetCylinder(EBodyPart bodyPart, int boneIndex,
            LimbAnchors anchors, out BoneSegmentPose cylinder)
        {
            cylinder = default;
            if (boneIndex < 0 || boneIndex >= GetBoneCount(bodyPart)) return false;
            Transform upperStart = anchors.UpperStart, upperEnd = anchors.UpperEnd;
            Transform lowerStart = anchors.LowerStart, lowerEnd = anchors.LowerEnd;
            bool isLeg = bodyPart == EBodyPart.LeftLeg || bodyPart == EBodyPart.RightLeg;
            Transform start = boneIndex == 0 ? upperStart : lowerStart;
            Transform end = boneIndex == 0
                ? (isLeg && lowerStart != null ? lowerStart : upperEnd) : lowerEnd;
            if (start == null || end == null) return false;
            LimbBoneDimensions dimensions = ResolveDimensions(bodyPart, boneIndex);

            Vector3 up = (start.position - end.position).normalized;
            if (up.sqrMagnitude < 0.5f) return false;
            Vector3 forward = Vector3.ProjectOnPlane(start.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(start.up, up);
            Quaternion frame = Quaternion.LookRotation(forward.normalized, up);
            // A shared segment frame keeps offsets perpendicular to the bone;
            // wrist or ankle rotation cannot collapse the lower-bone pair.
            Vector3 startPoint = start.position + frame *
                (dimensions.StartOffsetMillimeters * 0.001f);
            Vector3 endPoint = end.position + frame *
                (dimensions.EndOffsetMillimeters * 0.001f);
            cylinder = new BoneSegmentPose(startPoint, endPoint,
                dimensions.DiameterMillimeters * 0.0005f,
                dimensions.EndDiameterMillimeters * 0.0005f);
            return (endPoint - startPoint).sqrMagnitude > 0.000001f;
        }
    }
}
