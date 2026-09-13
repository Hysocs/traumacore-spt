using UnityEngine;

namespace TraumaCore
{
    internal static class SpineBoneGeometry
    {
        // Saved offsets remain relative to the tuned anchors in the torso frame.
        private static readonly Vector3 CervicalStartOffsetMillimeters = new(10f, -13f, 24f);
        private static readonly Vector3 CervicalEndOffsetMillimeters = new(-2f, 1f, -17f);
        private static readonly Vector3 ThoracicStartOffsetMillimeters = new(0f, 0f, -25f);
        private static readonly Vector3 ThoracicEndOffsetMillimeters = Vector3.zero;
        private const float CervicalStartDiameterMillimeters = 70f;
        private const float CervicalEndDiameterMillimeters = 55f;
        private const float ThoracicStartDiameterMillimeters = 60f;
        private const float ThoracicEndDiameterMillimeters = 50f;

        internal static BoneSegmentPose Apply(AnatomyRig rig, bool isUpper,
            Vector3 start, Vector3 end)
        {
            if (!AnatomyPoseSystem.TryResolveTorsoRotation(rig, out Quaternion frame))
                frame = rig.Chest.Rotation;
            Vector3 startOffset = isUpper ? CervicalStartOffsetMillimeters : ThoracicStartOffsetMillimeters;
            Vector3 endOffset = isUpper ? CervicalEndOffsetMillimeters : ThoracicEndOffsetMillimeters;
            float startDiameter = isUpper ? CervicalStartDiameterMillimeters : ThoracicStartDiameterMillimeters;
            float endDiameter = isUpper ? CervicalEndDiameterMillimeters : ThoracicEndDiameterMillimeters;
            return new BoneSegmentPose(
                start + frame * (startOffset / 1000f),
                end + frame * (endOffset / 1000f),
                startDiameter / 2000f,
                endDiameter / 2000f);
        }
    }
}
