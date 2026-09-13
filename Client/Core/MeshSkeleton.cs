using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace TraumaCore
{
    internal static class MeshSkeleton
    {
        internal static Dictionary<EBodyPart, LimbAnchors> CaptureLimbAnchors(Transform root)
        {
            Transform[] bones = ResolveBones(root);
            Dictionary<EBodyPart, LimbAnchors> limbs = new();
            CaptureArm(EBodyPart.LeftArm, "L");
            CaptureArm(EBodyPart.RightArm, "R");
            CaptureLeg(EBodyPart.LeftLeg, "L");
            CaptureLeg(EBodyPart.RightLeg, "R");
            return limbs;

            Transform FindJoint(string name) => Find(bones, "Base Human" + name);
            void CaptureArm(EBodyPart part, string side)
            {
                Transform elbow = FindJoint(side + "Forearm1") ?? FindJoint(side + "Forearm2");
                limbs[part] = new LimbAnchors(FindJoint(side + "Upperarm"), elbow,
                    elbow, FindJoint(side + "Palm"));
            }
            void CaptureLeg(EBodyPart part, string side)
            {
                Transform knee = FindJoint(side + "Calf") ?? FindJoint(side + "Thigh2");
                limbs[part] = new LimbAnchors(FindJoint(side + "Thigh1"), knee,
                    knee, FindJoint(side + "Foot"));
            }
        }

        internal static Dictionary<Transform, Transform> CaptureLimbMapping(
            Player source, Transform root)
        {
            Transform[] meshBones = ResolveBones(root);
            Dictionary<Transform, Transform> bonesBySource = new();
            foreach (EBodyPart bodyPart in new[] { EBodyPart.LeftArm, EBodyPart.RightArm,
                EBodyPart.LeftLeg, EBodyPart.RightLeg })
            {
                if (!OrganSystem.TryGetBoneSegments(source, bodyPart,
                    out Transform firstStart, out Transform firstEnd,
                    out Transform secondStart, out Transform secondEnd)) continue;
                CaptureBone(firstStart);
                CaptureBone(firstEnd);
                CaptureBone(secondStart);
                CaptureBone(secondEnd);
            }
            return bonesBySource;

            void CaptureBone(Transform bone)
            {
                if (bone == null || bonesBySource.ContainsKey(bone)) return;
                Transform mapped = Find(meshBones, bone.name);
                if (mapped != null) bonesBySource.Add(bone, mapped);
            }
        }

        internal static Transform[] ResolveBones(Transform root)
        {
            HashSet<Transform> bones = new();
            if (root == null) return System.Array.Empty<Transform>();
            foreach (SkinnedMeshRenderer renderer in
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                foreach (Transform bone in renderer.bones)
                    for (Transform current = bone;
                        current != null && current != root && current.IsChildOf(root);
                        current = current.parent)
                        bones.Add(current);
            }
            Transform[] result = new Transform[bones.Count];
            bones.CopyTo(result);
            return result;
        }

        internal static Transform Find(Transform[] bones, string name)
        {
            foreach (Transform bone in bones)
                if (bone != null && bone.name == name) return bone;
            return null;
        }
    }
}
