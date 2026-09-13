using System.Collections.Generic;
using EFT;
using UnityEngine;
using UnityEngine.UI;

namespace TraumaCore.Features.DeathScreen.HitMarkers
{
    internal sealed class DeathScreenAnatomyGraphic : MaskableGraphic
    {
        private readonly List<AnatomyOverlayLine> _geometry = new(2048);
        private readonly List<AnatomyScreenLine> _lines = new(2048);
        private AnatomyRig _rig;
        private Dictionary<EBodyPart, LimbAnchors> _limbs;
        private Camera _camera;
        private RawImage _preview;
        internal System.Action UpdateWounds;

        internal static DeathScreenAnatomyGraphic Create(RectTransform container,
            Transform modelRoot, Camera camera, RawImage preview)
        {
            GameObject root = new("DeathScreenAnatomy", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(DeathScreenAnatomyGraphic));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(container, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            DeathScreenAnatomyGraphic graphic = root.GetComponent<DeathScreenAnatomyGraphic>();
            graphic.raycastTarget = false;
            graphic._rig = AnatomyRig.FromMesh(modelRoot);
            graphic._limbs = MeshSkeleton.CaptureLimbAnchors(modelRoot);
            graphic._camera = camera;
            graphic._preview = preview;
            return graphic;
        }

        private void LateUpdate()
        {
            if (_camera == null || _preview == null) return;
            UpdateWounds?.Invoke();
            UpdateAnatomy();
        }

        private void UpdateAnatomy()
        {
            _geometry.Clear();
            _lines.Clear();
            if (Plugin.ShouldShowInspectionAnatomy.Value)
            {
                // Preview visibility is independent of which injuries can affect players.
                TargetRules rules = new(damageMultiplier: 1f, bodyTraumaEnabled: true,
                    brainEnabled: true, heartEnabled: true, cervicalSpineEnabled: true,
                    thoracicSpineEnabled: true);
                AnatomyOverlayGeometry.Build(_rig, null, rules, _geometry, limbAnchors: _limbs);
            }
            if (Plugin.ShouldShowInspectionAnchors.Value)
            {
                AddAnchor(_rig.Head);
                AddAnchor(_rig.Chest);
                AddAnchor(_rig.Pelvis);
                foreach (LimbAnchors limb in _limbs.Values)
                {
                    AddAnchor(limb.UpperStart);
                    AddAnchor(limb.UpperEnd);
                    AddAnchor(limb.LowerStart);
                    AddAnchor(limb.LowerEnd);
                }
            }
            AnatomyOverlayRenderer.AppendGeometry(_camera, rectTransform, _geometry, _lines, _preview);
            SetVerticesDirty();
        }

        private void AddAnchor(Transform anchor)
        {
            if (anchor != null)
                _geometry.Add(new AnatomyOverlayLine(anchor.position, anchor.position,
                    Color.cyan, 3f, -0.012f));
        }

        private void AddAnchor(AnatomyAnchor anchor)
        {
            if (anchor.HasValue)
                _geometry.Add(new AnatomyOverlayLine(anchor.Position, anchor.Position,
                    Color.cyan, 3f, -0.012f));
        }

        protected override void OnPopulateMesh(VertexHelper vertices) =>
            AnatomyOverlayRenderer.PopulateMesh(vertices, _lines);
    }
}
