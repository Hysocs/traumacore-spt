using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EFT;
using EFT.UI;
using UnityEngine;

namespace TraumaCore.Features.DeathScreen.HitMarkers
{
    internal static class BodyPartAnchorResolver
    {
        private static readonly Dictionary<EBodyPart, string[]> NamesByBodyPart = new()
        {
            { EBodyPart.Head, new[] { "Base HumanHead" } },
            { EBodyPart.Chest, new[] { "Base HumanSpine3", "Base HumanSpine2" } },
            { EBodyPart.Stomach, new[] { "Base HumanPelvis" } },
            { EBodyPart.LeftArm, new[]
                { "Base HumanLUpperarm", "HumanLUpperarm", "LeftUpperArm" } },
            { EBodyPart.RightArm, new[]
                { "Base HumanRUpperarm", "HumanRUpperarm", "RightUpperArm" } },
            { EBodyPart.LeftLeg, new[]
                { "Base HumanLThigh1", "HumanLThigh1", "LeftThigh1",
                    "Base HumanLCalf", "HumanLCalf", "LeftCalf" } },
            { EBodyPart.RightLeg, new[]
                { "Base HumanRThigh1", "HumanRThigh1", "RightThigh1",
                    "Base HumanRCalf", "HumanRCalf", "RightCalf" } }
        };
        private static readonly ConditionalWeakTable<Transform,
            Dictionary<EBodyPart, Transform>> CachedAnchorsByRoot = new();

        internal static IEnumerable<EBodyPart> BodyParts => NamesByBodyPart.Keys;

        internal static void ClearLiveAnchorCache() => CachedAnchorsByRoot.Clear();

        internal static Dictionary<EBodyPart, Transform> Resolve(PlayerModelView modelView)
        {
            Transform[] transforms = modelView.GetComponentsInChildren<Transform>(true);
            Dictionary<EBodyPart, Transform> anchorsByBodyPart = new();

            foreach (KeyValuePair<EBodyPart, string[]> entry in NamesByBodyPart)
            {
                Transform anchor = FindMatchingTransform(transforms, entry.Value);
                if (anchor != null)
                    anchorsByBodyPart[entry.Key] = anchor;
            }

            Animator animator = modelView
                .GetComponentsInChildren<Animator>(true)
                .FirstOrDefault(candidate => candidate != null && candidate.isHuman);
            if (animator == null)
                return anchorsByBodyPart;

            AddHumanoidAnchor(EBodyPart.LeftArm, HumanBodyBones.LeftUpperArm);
            AddHumanoidAnchor(EBodyPart.RightArm, HumanBodyBones.RightUpperArm);
            AddHumanoidAnchor(EBodyPart.LeftLeg, HumanBodyBones.LeftUpperLeg);
            AddHumanoidAnchor(EBodyPart.RightLeg, HumanBodyBones.RightUpperLeg);
            return anchorsByBodyPart;

            void AddHumanoidAnchor(EBodyPart bodyPart, HumanBodyBones humanoidBone)
            {
                if (anchorsByBodyPart.ContainsKey(bodyPart))
                    return;

                Transform anchor = animator.GetBoneTransform(humanoidBone);
                if (anchor != null)
                    anchorsByBodyPart.Add(bodyPart, anchor);
            }
        }

        internal static Transform Find(Transform root, EBodyPart bodyPart)
        {
            if (root == null || !NamesByBodyPart.TryGetValue(bodyPart, out string[] names))
                return null;

            Dictionary<EBodyPart, Transform> cachedAnchors =
                CachedAnchorsByRoot.GetValue(root, ResolveHierarchyAnchors);
            return cachedAnchors.TryGetValue(bodyPart, out Transform anchor)
                ? anchor
                : null;
        }

        private static Dictionary<EBodyPart, Transform> ResolveHierarchyAnchors(Transform root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Dictionary<EBodyPart, Transform> anchorsByBodyPart = new();
            foreach (KeyValuePair<EBodyPart, string[]> entry in NamesByBodyPart)
            {
                Transform anchor = FindMatchingTransform(transforms, entry.Value);
                if (anchor != null)
                    anchorsByBodyPart.Add(entry.Key, anchor);
            }
            return anchorsByBodyPart;
        }

        internal static void LogResolution(
            PlayerModelView modelView,
            IReadOnlyDictionary<EBodyPart, Transform> anchorsByBodyPart)
        {
            foreach (EBodyPart bodyPart in NamesByBodyPart.Keys)
            {
                if (anchorsByBodyPart.TryGetValue(bodyPart, out Transform anchor))
                    TraumaLog.Info(
                        $"[DeathScreenHitMarkers] Resolved {bodyPart} anchor: {anchor.name}");
                else
                    TraumaLog.Warning(
                        $"[DeathScreenHitMarkers] Missing {bodyPart} anchor");
            }

            if (anchorsByBodyPart.ContainsKey(EBodyPart.LeftLeg) &&
                anchorsByBodyPart.ContainsKey(EBodyPart.RightLeg))
                return;

            string candidates = string.Join(", ", modelView
                .GetComponentsInChildren<Transform>(true)
                .Where(transform =>
                    transform.name.IndexOf("thigh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("calf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("leg", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(transform => transform.name)
                .Distinct()
                .Take(40));
            TraumaLog.Warning(
                $"[DeathScreenHitMarkers] Leg-like hierarchy names: {candidates}");
        }

        private static Transform FindMatchingTransform(
            IEnumerable<Transform> transforms,
            IEnumerable<string> expectedNames) =>
            expectedNames
                .Select(expectedName => transforms.FirstOrDefault(transform =>
                    IsMatchingName(transform.name, expectedName)))
                .FirstOrDefault(transform => transform != null);

        private static bool IsMatchingName(string hierarchyName, string expectedName)
        {
            if (string.IsNullOrEmpty(hierarchyName) || string.IsNullOrEmpty(expectedName))
                return false;

            if (string.Equals(hierarchyName, expectedName, StringComparison.OrdinalIgnoreCase))
                return true;

            string nameWithoutPrefix = expectedName.StartsWith(
                "Base ",
                StringComparison.OrdinalIgnoreCase)
                ? expectedName.Substring(5)
                : expectedName;
            return hierarchyName.EndsWith(nameWithoutPrefix, StringComparison.OrdinalIgnoreCase) ||
                   hierarchyName.StartsWith(
                       nameWithoutPrefix + " (",
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
