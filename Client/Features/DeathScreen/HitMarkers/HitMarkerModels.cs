using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using TMPro;
using TraumaCore.Features.DeathScreen.DamageTracking;
using UnityEngine;
using UnityEngine.UI;

namespace TraumaCore.Features.DeathScreen.HitMarkers
{
    internal sealed class ModelPreview
    {
        internal Camera Camera;
        internal RawImage Image;
        internal RectTransform Container;
        internal readonly List<BodyPartMarker> VisibleLeftMarkers = new(4);
        internal readonly List<BodyPartMarker> VisibleRightMarkers = new(4);
        internal float LabelWidthPixels = -1f;
    }

    internal sealed class BodyPartMarker
    {
        internal Transform Bone;
        internal RectTransform Label;
        internal TextMeshProUGUI Text;
        internal readonly List<DeathScreenDamageTracker.BulletImpactRecord>
            RecordedImpacts = new();
        internal readonly List<BulletImpactVisual> Impacts = new();

        internal EBodyPart BodyPart;
        internal float MaximumHealth;
        internal int DirectHits;
        internal float DirectDamage;
        internal float BleedDamage;
        internal float BleedDurationSeconds;
        internal float BleedDamagePerSecond;
        internal EDamageType DirectType;
        internal bool HasHeavyBleed;
        internal int LatestImpactSequence;
        internal Color Color;
        internal float LabelYPixels;
        internal bool LeftSide;
        internal bool Visible;
        internal bool HasLoggedProjection;
        internal bool HasLoggedLabelPosition;
    }

    internal sealed class BulletImpactVisual
    {
        internal Vector3 LocalPoint;
        internal Vector2 Point;
        internal RectTransform Dot;
        internal bool Visible;
    }
}
