using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using Comfort.Common;
using TraumaCore.Patches.HealthEffects;
using TraumaCore.Patches.Trauma;
using Systems.Effects;
using UnityEngine;
using System.Collections.Generic;

namespace TraumaCore
{
    internal sealed class TraumaController : MonoBehaviour
    {
        private const float ReferencePostArmorDamage = 50f;

        private sealed class WoundTrack
        {
            private sealed class Wound
            {
                internal float Severity;
                internal float Created;
                internal float Duration;
                internal BleedType Type;
            }

            private readonly List<Wound> _wounds = new List<Wound>();
            internal int Count { get { return _wounds.Count; } }
            internal bool Active { get { return _wounds.Count > 0; } }
            internal BleedType StrongestType
            {
                get
                {
                    BleedType strongest = BleedType.Light;
                    for (int i = 0; i < _wounds.Count; i++)
                        if (_wounds[i].Type > strongest) strongest = _wounds[i].Type;
                    return strongest;
                }
            }

            internal float Add(BleedType type, float damageMultiplier,
                float durationMultiplier, float? totalDamage = null,
                bool isPermanent = false)
            {
                float now = Time.unscaledTime;
                float duration = isPermanent
                    ? float.PositiveInfinity
                    : Mathf.Max(0.01f,
                        OrganSystem.NonHeartDecayDuration.Value *
                        Mathf.Max(0.01f, durationMultiplier));
                float woundMultiplier = Mathf.Max(0f, damageMultiplier);
                float severity;
                if (isPermanent)
                    severity = OrganSystem.HeartBaseBleedDamagePerSecond *
                        woundMultiplier;
                else
                {
                    float woundTotalDamage = totalDamage.HasValue
                        ? Mathf.Max(0f, totalDamage.Value)
                        : 0f;
                    severity = woundTotalDamage /
                        OrganSystem.GetBleedDecayArea(duration);
                }
                _wounds.Add(new Wound
                {
                    Severity = severity,
                    Created = now,
                    Duration = duration,
                    Type = type
                });
                return severity;
            }

            internal void Clear()
            {
                _wounds.Clear();
            }

            internal void AdvanceClotting(float progress)
            {
                if (!Active) return;
                float now = Time.unscaledTime;
                float remainingMultiplier = 1f - Mathf.Clamp01(progress);
                for (int i = 0; i < _wounds.Count; i++)
                {
                    Wound wound = _wounds[i];
                    float duration = wound.Duration;
                    float elapsed = Mathf.Clamp(now - wound.Created, 0f, duration);
                    float remaining = (duration - elapsed) * remainingMultiplier;
                    wound.Created = now - (duration - remaining);
                }
            }

            internal float GetDamagePerSecond(BleedType? type = null)
            {
                float damagePerSecond = 0f;
                for (int i = 0; i < _wounds.Count; i++)
                {
                    if (type.HasValue && _wounds[i].Type != type.Value) continue;
                    damagePerSecond += _wounds[i].Severity *
                        GetDecayStrength(_wounds[i].Created,
                            _wounds[i].Duration);
                }
                return damagePerSecond;
            }

            internal float GetPermanentDamagePerSecond()
            {
                float damagePerSecond = 0f;
                for (int i = 0; i < _wounds.Count; i++)
                    damagePerSecond += _wounds[i].Severity;
                return damagePerSecond;
            }

            internal int CountType(BleedType type)
            {
                int count = 0;
                for (int i = 0; i < _wounds.Count; i++)
                    if (_wounds[i].Type == type) count++;
                return count;
            }

            internal float GetTimeLeft(BleedType? type = null)
            {
                float now = Time.unscaledTime;
                float timeLeft = 0f;
                for (int i = 0; i < _wounds.Count; i++)
                {
                    Wound wound = _wounds[i];
                    if (type.HasValue && wound.Type != type.Value) continue;
                    if (float.IsInfinity(wound.Duration))
                        return float.PositiveInfinity;
                    timeLeft = Mathf.Max(timeLeft,
                        wound.Duration - (now - wound.Created));
                }
                return Mathf.Max(0f, timeLeft);
            }

            internal void RemoveClottedWounds()
            {
                float now = Time.unscaledTime;
                for (int i = _wounds.Count - 1; i >= 0; i--)
                {
                    if (now - _wounds[i].Created < _wounds[i].Duration) continue;
                    _wounds.RemoveAt(i);
                }
            }

        }
        internal struct DebugImpact
        {
            internal Vector3 HitPoint, Direction, Intersection, BoneIntersection;
            internal Vector3 ReferenceEntryPoint, ReferenceExitPoint;
            internal WoundTrajectory Trajectory;
            internal float Expires;
            internal float TissueThickness, PenetrationDepth, ImpactVelocity,
                BulletDiameter, WoundScore, ReferenceThickness, TraveledDepth,
                CenterDepthRatio, DepthRatio, BleedDamageMultiplier,
                BleedDurationMultiplier, OriginalPenetrationDepth,
                OriginalImpactVelocity, FirstBoneDistance, BoneResistanceDepth,
                FragmentationChance, FragmentationDepth, FragmentDamageBonus,
                ArmorPenetrationMargin, ArmorFragmentationChanceBonus,
                ArmorPenetrationRetention;
            internal float ArmorPenetrationDepth, KineticPenetrationDepth;
            internal int BoneCollisionCount, FireIndex, ProjectileIndex;
            internal int FragmentPathCount;
            internal EBodyPart BodyPart;
            internal string HitboxName;
            internal string DepthReferenceName;
            internal string PenetrationModel;
            internal BleedType BleedType;
            internal bool PassedThrough;
            internal bool Heart;
            internal bool Brain;
            internal bool CervicalSpine;
            internal bool ThoracicSpine;
            internal bool Ribcage;
            internal bool Skull;
            internal bool ArmorStopped;
            internal bool Bone;
            internal bool HasFragmentation;
        }
        internal struct ImpactCapture
        {
            internal Vector3 HitPoint, Direction, Intersection, BoneIntersection;
            internal Transform HitTransform;
            internal WoundBallistics Wound;
            internal EBodyPart BodyPart;
            internal string HitboxName;
            internal float OriginalPenetrationDepth, OriginalImpactVelocity,
                FirstBoneDistance, BoneResistanceDepth, FragmentDamageBonus;
            internal float ArmorPenetrationMargin,
                ArmorFragmentationChanceBonus, ArmorPenetrationRetention;
            internal int BoneCollisionCount, FireIndex, ProjectileIndex;
            internal EDamageType DamageType;
            internal bool Heart, Brain, ArmorStopped, Bone, CervicalSpine,
                ThoracicSpine, Ribcage, Skull;
        }
        internal struct DebugBloodParticle
        {
            internal Vector3 Position;
            internal Vector3 Velocity;
            internal float Expires;
            internal float Size;
            internal float TrailLength;
        }
        private sealed class DebugBloodSource
        {
            internal Transform Attachment;
            internal Vector3 LocalPosition;
            internal Vector3 LocalDirection;
            internal float Strength;
            internal float Created;
            internal float EmissionAccumulator;
            internal bool Heart;
            internal bool Head;
            internal EBodyPart BodyPart;
        }
        private Player _player;
        private ActiveHealthController _health;
        private float _traumaAccumulator;
        private const float TraumaStep = 1f / 60f;
        private const float MaximumBleedPresentationInterval = 1f;
        private const float MinimumBleedPresentationInterval = 0.5f;
        private const int MaximumPresentationBleedCount = 7;
        private float _nextPresentationTime;
        private readonly WoundTrack _chestWounds = new WoundTrack();
        private readonly WoundTrack _heartWounds = new WoundTrack();
        private readonly WoundTrack _faceWounds = new WoundTrack();
        private bool _subscribed;
        private bool _addingMarker;
        private bool _bloodLossBlockerWasActive;
        private float _bruiseStrength;
        private float _currentBruiseStrength;
        private float _bruiseExpires;
        private float _appliedRestorePenalty;
        private ActiveHealthController.Pain _bruiseUiEffect;
        private const float BruiseDuration = 15f;
        private const float CorpseBleedDuration = 8f;
        private Vector3 _lastImpactPoint;
        private Vector3 _lastImpactDirection = Vector3.forward;
        private WoundTrajectory _lastWoundTrajectory;
        private Transform _lastImpactTransform;
        private float _nextBloodDecalTime;
        private bool _wasAlive;
        private bool _corpseBloodInitialized;
        private float _corpseBloodReserve;
        private bool _traumaDeathVoicePending;
        private bool _heartDamageContext;
        private bool _headDeathVoicePending;
        private readonly List<DebugImpact> _impacts = new List<DebugImpact>(12);
        private readonly List<DebugBloodSource> _bloodSources = new List<DebugBloodSource>(16);
        private readonly List<DebugBloodParticle> _bloodParticles = new List<DebugBloodParticle>(192);
        private readonly List<BodyRenderer> _bodyRenderers = new List<BodyRenderer>(12);
        private readonly Dictionary<EBodyPart, WoundTrack> _bodyWounds =
            new Dictionary<EBodyPart, WoundTrack>();
        private readonly HashSet<IHealthEffect> _acceleratedBleedEffects =
            new HashSet<IHealthEffect>();

        internal int HeartWoundCount { get { return _heartWounds.Count; } }
        internal float HeartBleedDamagePerSecond
        { get { return _heartWounds.GetPermanentDamagePerSecond(); } }
        internal float BruiseStrength { get { return _currentBruiseStrength; } }
        internal float BruiseTimeLeft { get { return Mathf.Max(0f, _bruiseExpires - Time.unscaledTime); } }
        internal int LightWoundCount { get { return CountTreatableWounds(BleedType.Light); } }
        internal int HeavyWoundCount { get { return CountTreatableWounds(BleedType.Heavy); } }
        internal bool TraumaDeathVoicePending { get { return _traumaDeathVoicePending; } }
        internal bool HeartDeathVoicePending
        { get { return _traumaDeathVoicePending && _heartDamageContext; } }
        internal bool HeadDeathVoicePending { get { return _headDeathVoicePending; } }

        internal void SetHeadDeathVoicePending(bool pending)
        { _headDeathVoicePending = pending; }
        internal IList<DebugImpact> DebugImpacts { get { return _impacts; } }
        internal IList<DebugBloodParticle> DebugBloodParticles { get { return _bloodParticles; } }

        internal float BleedDamagePerSecond
        {
            get
            {
                if (_health == null) return 0f;
                float treatableDps = _chestWounds.GetDamagePerSecond();
                if (_faceWounds.Active)
                    treatableDps += _faceWounds.GetDamagePerSecond();
                foreach (KeyValuePair<EBodyPart, WoundTrack> pair in _bodyWounds)
                {
                    WoundTrack track = pair.Value;
                    if (!track.Active) continue;
                    float primary = track.GetDamagePerSecond();
                    treatableDps += primary * (1f + GetShareFraction(pair.Key) *
                        GetLinkageMultiplier(pair.Key));
                }
                float dps = treatableDps * GetTreatableBleedMultiplier();
                if (_heartWounds.Active)
                    dps += _heartWounds.GetPermanentDamagePerSecond();
                return dps;
            }
        }

        private int CountTreatableWounds(BleedType type)
        {
            int count = _chestWounds.CountType(type) + _faceWounds.CountType(type);
            foreach (KeyValuePair<EBodyPart, WoundTrack> pair in _bodyWounds)
                count += pair.Value.CountType(type);
            return count;
        }

        private int CountActiveWounds()
        {
            int count = _chestWounds.Count + _heartWounds.Count +
                _faceWounds.Count;
            foreach (KeyValuePair<EBodyPart, WoundTrack> pair in _bodyWounds)
                count += pair.Value.Count;
            return count;
        }

        private float GetBleedPresentationInterval()
        {
            float bleedCountProgress = Mathf.InverseLerp(
                1f, MaximumPresentationBleedCount, CountActiveWounds());
            return Mathf.Lerp(MaximumBleedPresentationInterval,
                MinimumBleedPresentationInterval, bleedCountProgress);
        }

        internal void InitializeForPlayer(Player player)
        {
            ActiveHealthController nextHealth = player != null
                ? player.ActiveHealthController : null;
            if (_player == player && _health == nextHealth) return;
            Unsubscribe();
            _player = player;
            _health = nextHealth;
            if (_health != null)
            {
                _wasAlive = _health.IsAlive;
                _corpseBloodInitialized = false;
                _corpseBloodReserve = 0f;
                _health.EffectRemovedEvent += OnEffectRemoved;
                _health.EffectResidualEvent += OnEffectResidual;
                _subscribed = true;
            }
            enabled = HasRecurringWork();
        }

        internal void AddHeartWound(float multiplier)
        {
            if (!AddWound(_heartWounds, EBodyPart.Chest, BleedType.Heavy,
                multiplier, 1f, null, true)) return;
            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info($"[Trauma] Permanent heavy heart wound added; " +
                    $"multiplier={multiplier:F2}, " +
                    $"woundDps={OrganSystem.HeartBaseBleedDamagePerSecond * multiplier:F1}, " +
                    $"active heart wounds={_heartWounds.Count}");
        }

        internal void AddTreatableWound(EBodyPart bodyPart, BleedType type,
            float damageMultiplier, float durationMultiplier = 1f,
            float eftPostArmorDamage = 0f, float directDamage = 0f,
            float woundStrength = 1f)
        {
            WoundTrack wounds;
            switch (bodyPart)
            {
                case EBodyPart.Head:
                    wounds = _faceWounds;
                    break;
                case EBodyPart.Chest:
                    wounds = _chestWounds;
                    break;
                case EBodyPart.Stomach:
                case EBodyPart.LeftArm:
                case EBodyPart.RightArm:
                case EBodyPart.LeftLeg:
                case EBodyPart.RightLeg:
                    if (!_bodyWounds.TryGetValue(bodyPart, out wounds))
                    {
                        wounds = new WoundTrack();
                        _bodyWounds.Add(bodyPart, wounds);
                    }
                    break;
                default:
                    return;
            }

            bool isHead = bodyPart == EBodyPart.Head;
            float sourceDamage = eftPostArmorDamage > 0f
                ? eftPostArmorDamage
                : ReferencePostArmorDamage;
            float appliedDirectDamage = eftPostArmorDamage > 0f
                ? Mathf.Max(0f, directDamage)
                : sourceDamage * OrganSystem.DirectDamagePercent.Value *
                    OrganSystem.GetTargetRules(_player).DamageMultiplier;
            float fullWoundBleedBudget = Mathf.Max(0f,
                sourceDamage * OrganSystem.FullWoundTotalDamageMultiplier.Value -
                appliedDirectDamage);
            float totalDamage = fullWoundBleedBudget /
                EstimateLinkedDamageMultiplier(bodyPart) *
                Mathf.Clamp01(woundStrength);
            if (!AddWound(wounds, bodyPart, type, damageMultiplier,
                durationMultiplier, totalDamage, false, isHead))
                return;
            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info(string.Format(
                    "[Trauma] {0} {1} wound added; active wounds={2}",
                    bodyPart, type, wounds.Count));
        }

        internal void AddFatalHeadBlood()
        {
            float severity = OrganSystem.HeavyBloodEffectStrength;
            AddDebugBloodSource(severity, false, true, EBodyPart.Head);
        }

        internal void AddCorpseWound(EBodyPart bodyPart)
        {
            if (_health == null) return;
            EnsureCorpseBloodReserve();
            if (_corpseBloodReserve <= 0f) return;

            float severity = OrganSystem.HeavyBloodEffectStrength * 0.5f;
            AddDebugBloodSource(severity, false,
                bodyPart == EBodyPart.Head, bodyPart);
            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info(string.Format(
                    "[BloodFX] Corpse wound on {0}: strength={1:0.00}, reserve={2:0.0}",
                    bodyPart, severity, _corpseBloodReserve));
        }

        private bool AddWound(WoundTrack track, EBodyPart bodyPart, BleedType type,
            float damageMultiplier, float durationMultiplier,
            float? totalDamage = null, bool heart = false, bool head = false)
        {
            if (_health == null || track == null) return false;
            enabled = true;
            float severity = track.Add(type, damageMultiplier,
                durationMultiplier, totalDamage, heart);
            int exitCount = 0;
            if (_lastWoundTrajectory.HasPath)
                for (int index = 0;
                    index < _lastWoundTrajectory.Segments.Length; index++)
                    if (_lastWoundTrajectory.Segments[index].Endpoint ==
                        WoundPathEndpoint.Exit)
                        exitCount++;
            float sourceStrength = severity / (exitCount + 1f);
            AddDebugBloodSource(sourceStrength, heart, head, bodyPart);
            if (exitCount > 0)
            {
                Vector3 entry = _lastImpactPoint;
                Vector3 direction = _lastImpactDirection;
                for (int index = 0;
                    index < _lastWoundTrajectory.Segments.Length; index++)
                {
                    WoundPathSegment segment =
                        _lastWoundTrajectory.Segments[index];
                    if (segment.Endpoint != WoundPathEndpoint.Exit)
                        continue;
                    _lastImpactPoint = segment.EndPoint;
                    _lastImpactDirection = -segment.Direction;
                    AddDebugBloodSource(sourceStrength, heart, head, bodyPart);
                }
                _lastImpactPoint = entry;
                _lastImpactDirection = direction;
            }
            _lastWoundTrajectory = default;
            EnsureMarkers();
            return true;
        }

        private static float GetDecayStrength(float lastWoundTime,
            float duration)
        {
            if (lastWoundTime == float.MinValue) return 0f;
            duration = Mathf.Max(0.01f, duration);
            float elapsed = Time.unscaledTime - lastWoundTime;
            if (elapsed >= duration) return 0f;

            float rapidDuration = Mathf.Min(OrganSystem.RapidClotDuration, duration);
            if (elapsed <= rapidDuration)
                return Mathf.Lerp(1f, OrganSystem.RapidClotStrength,
                    Mathf.Clamp01(elapsed / rapidDuration));

            return Mathf.Lerp(OrganSystem.RapidClotStrength, 0f,
                Mathf.Clamp01((elapsed - rapidDuration) /
                    Mathf.Max(0.01f, duration - rapidDuration)));
        }

        private static float GetDecayStrength(float lastWoundTime) =>
            GetDecayStrength(lastWoundTime,
                OrganSystem.NonHeartDecayDuration.Value);

        private float GetTreatableBleedMultiplier()
        {
            return _health != null && _health.HasBloodLossBlockers()
                ? OrganSystem.BloodLossBlockerDamageMultiplier
                : 1f;
        }

        internal void CaptureImpact(ImpactCapture capture)
        {
            Vector3 direction = capture.Direction.sqrMagnitude > 0.0001f
                ? capture.Direction.normalized : Vector3.forward;
            _lastImpactPoint = capture.HitPoint;
            _lastImpactDirection = direction;
            _lastWoundTrajectory = WoundTrajectory.Create(
                capture.Wound, direction);
            _lastImpactTransform = capture.HitTransform;
            if (Plugin.EnableDeathScreenReport.Value)
            {
                Features.DeathScreen.DamageTracking.DeathScreenDamageTracker
                    .CaptureTrajectory(
                        _player?.Profile,
                        _player,
                        capture.BodyPart,
                        capture.HitPoint,
                        direction,
                        capture.DamageType,
                        capture.FireIndex,
                        capture.ProjectileIndex,
                        capture.Wound);
            }
            if (!OrganSystem.DebugEsp.Value)
                return;

            enabled = true;
            if (_impacts.Count >= 12) _impacts.RemoveAt(0);
            _impacts.Add(new DebugImpact
            {
                HitPoint = capture.HitPoint,
                BodyPart = capture.BodyPart,
                HitboxName = capture.HitboxName,
                DepthReferenceName = capture.Wound.DepthReferenceName,
                PenetrationModel = capture.Wound.PenetrationModel,
                Direction = direction,
                Intersection = capture.Intersection,
                Heart = capture.Heart,
                Brain = capture.Brain,
                CervicalSpine = capture.CervicalSpine,
                ThoracicSpine = capture.ThoracicSpine,
                Ribcage = capture.Ribcage,
                Skull = capture.Skull,
                Trajectory = _lastWoundTrajectory,
                ReferenceEntryPoint = capture.Wound.ReferenceEntryPoint,
                ReferenceExitPoint = capture.Wound.ReferenceExitPoint,
                TissueThickness = capture.Wound.TissueThickness,
                PenetrationDepth = capture.Wound.PenetrationDepth,
                ArmorPenetrationDepth = capture.Wound.ArmorPenetrationDepth,
                KineticPenetrationDepth = capture.Wound.KineticPenetrationDepth,
                ReferenceThickness = capture.Wound.ReferenceThickness,
                TraveledDepth = capture.Wound.TraveledDepth,
                CenterDepthRatio = capture.Wound.CenterDepthRatio,
                DepthRatio = capture.Wound.DepthRatio,
                BleedDamageMultiplier = capture.Wound.BleedDamageMultiplier,
                BleedDurationMultiplier = capture.Wound.BleedDurationMultiplier,
                OriginalPenetrationDepth = capture.OriginalPenetrationDepth,
                OriginalImpactVelocity = capture.OriginalImpactVelocity,
                BoneCollisionCount = capture.BoneCollisionCount,
                FireIndex = capture.FireIndex,
                ProjectileIndex = capture.ProjectileIndex,
                HasFragmentation = capture.Wound.HasFragmentation,
                FragmentPathCount = capture.Wound.HasFragmentation
                    ? _lastWoundTrajectory.Segments.Length - 1 : 0,
                FragmentationChance = OrganSystem.ForceFragmentation.Value
                    ? 1f : capture.Wound.FragmentationChance,
                FragmentationDepth = capture.Wound.FragmentationDepth,
                FragmentDamageBonus = capture.FragmentDamageBonus,
                ArmorPenetrationMargin = capture.ArmorPenetrationMargin,
                ArmorFragmentationChanceBonus =
                    capture.ArmorFragmentationChanceBonus,
                ArmorPenetrationRetention =
                    capture.ArmorPenetrationRetention,
                FirstBoneDistance = capture.FirstBoneDistance,
                BoneResistanceDepth = capture.BoneResistanceDepth,
                ImpactVelocity = capture.Wound.ImpactVelocity,
                BulletDiameter = capture.Wound.BulletDiameter,
                WoundScore = capture.Wound.WoundScore,
                BleedType = capture.Wound.BleedType,
                PassedThrough = capture.Wound.PassedThrough,
                Bone = capture.Bone,
                BoneIntersection = capture.BoneIntersection,
                ArmorStopped = capture.ArmorStopped,
                Expires = Time.unscaledTime + 10f
            });
        }

        internal void AddBruise(float stoppedBulletDamage)
        {
            enabled = true;
            float existing = _currentBruiseStrength;
            _bruiseStrength = Mathf.Clamp01(existing + Mathf.Max(0f, stoppedBulletDamage) / 100f);
            _bruiseExpires = Time.unscaledTime + BruiseDuration;
            _currentBruiseStrength = _bruiseStrength;
            if (_health != null)
            {
                _bruiseUiEffect = _health.FindExistingEffect<
                    ActiveHealthController.Pain>(EBodyPart.Chest);
                if (_bruiseUiEffect == null)
                    _bruiseUiEffect = _health.AddEffect<
                        ActiveHealthController.Pain>(EBodyPart.Chest,
                            0f, BruiseDuration, 0f, _bruiseStrength);
                else
                {
                    _bruiseUiEffect.AddWorkTime(BruiseDuration, true);
                    if (_bruiseStrength > _bruiseUiEffect.Strength)
                        _bruiseUiEffect.SetStrength(_bruiseStrength);
                }
                NativeEffectLabels.MarkBruised(_bruiseUiEffect,
                    BruiseDuration);
            }
            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info(string.Format("[Bruised] +{0:0.00}, strength={1:0.00}, duration={2:0.0}s",
                    stoppedBulletDamage / 100f, _bruiseStrength, BruiseDuration));
        }

        internal void PaintNativeBloodAtHit(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (_player == null || _player.PlayerBody == null ||
                !Singleton<Effects>.Instantiated) return;
            Effects effects = Singleton<Effects>.Instance;
            if (effects == null || !effects.UseDecalPainter || effects.TexDecals == null)
                return;
            _bodyRenderers.Clear();
            _player.PlayerBody.GetBodyRenderersNonAlloc(_bodyRenderers);
            if (_bodyRenderers.Count == 0) return;
            Vector3 projectionNormal = hitNormal.sqrMagnitude > 0.0001f
                ? -hitNormal.normalized : Vector3.back;
            effects.PlayerMeshesHit(_bodyRenderers, hitPoint, projectionNormal);
        }

        private void Update()
        {
            UpdateBruising();
            UpdateDebugBlood();
            for (int i = _impacts.Count - 1; i >= 0; i--)
                if (_impacts[i].Expires <= Time.unscaledTime) _impacts.RemoveAt(i);
            if (_health == null || !_health.IsAlive)
            {
                DisableWhenIdle();
                return;
            }
            bool bloodLossBlockerActive = _health.HasBloodLossBlockers();
            if (bloodLossBlockerActive && !_bloodLossBlockerWasActive)
                ClearLinkedTreatableBleeds();
            _bloodLossBlockerWasActive = bloodLossBlockerActive;
            if (!_chestWounds.Active && !_heartWounds.Active && !_faceWounds.Active &&
                _bodyWounds.Count == 0)
            {
                DisableWhenIdle();
                return;
            }
            _traumaAccumulator += Time.deltaTime;
            if (_traumaAccumulator < TraumaStep) return;
            float deltaTime = _traumaAccumulator;
            _traumaAccumulator = 0f;
            ExpireClottedBleeds();

            float treatableMultiplier = GetTreatableBleedMultiplier();
            ApplyTreatableWoundDamage(EBodyPart.Chest, _chestWounds,
                deltaTime, treatableMultiplier);
            if (!IsHealthAlive()) return;
            ApplyTreatableWoundDamage(EBodyPart.Head, _faceWounds,
                deltaTime, treatableMultiplier);
            if (!IsHealthAlive()) return;

            foreach (KeyValuePair<EBodyPart, WoundTrack> pair in _bodyWounds)
            {
                ApplyTreatableWoundDamage(pair.Key, pair.Value,
                    deltaTime, treatableMultiplier);
                if (!IsHealthAlive()) return;
            }

            float heartDamage = _heartWounds.GetPermanentDamagePerSecond() * deltaTime;
            if (heartDamage > 0f)
            {
                _heartDamageContext = true;
                try
                {
                    ApplyPrimaryAndShared(EBodyPart.Chest, heartDamage,
                        DamageHelper.HeavyBleedingDamage);
                }
                finally
                {
                    _heartDamageContext = false;
                }
            }

            if (!IsHealthAlive()) return;
            EnsureMarkers();
            UpdateNativeBleedStrength();
        }

        private void ApplyTreatableWoundDamage(EBodyPart bodyPart,
            WoundTrack wounds, float deltaTime, float multiplier)
        {
            if (wounds == null || !wounds.Active) return;
            float heavy = wounds.GetDamagePerSecond(BleedType.Heavy);
            float light = wounds.GetDamagePerSecond() - heavy;
            if (light > 0f)
                ApplyPrimaryAndShared(bodyPart,
                    light * multiplier * deltaTime,
                    DamageHelper.LightBleedingDamage);
            if (IsHealthAlive() && heavy > 0f)
                ApplyPrimaryAndShared(bodyPart,
                    heavy * multiplier * deltaTime,
                    DamageHelper.HeavyBleedingDamage);
        }

        private void AddDebugBloodSource(float strength, bool heart, bool head,
            EBodyPart bodyPart)
        {
            if (_player == null || strength <= 0f || !OrganSystem.BloodEffects.Value)
                return;

            enabled = true;
            Transform attachment = _lastImpactTransform != null
                ? _lastImpactTransform : _player.gameObject.transform;
            float visualStrength = strength;
            float now = Time.unscaledTime;

            if (_bloodSources.Count >= 24)
            {
                int remove = 0;
                for (int i = 0; i < _bloodSources.Count; i++)
                    if (!_bloodSources[i].Heart) { remove = i; break; }
                _bloodSources.RemoveAt(remove);
            }
            _bloodSources.Add(new DebugBloodSource
            {
                Attachment = attachment,
                LocalPosition = attachment.InverseTransformPoint(_lastImpactPoint),
                LocalDirection = (Quaternion.Inverse(attachment.rotation) * -_lastImpactDirection).normalized,
                Strength = visualStrength,
                Created = now,
                Heart = heart,
                Head = head,
                BodyPart = bodyPart
            });
        }

        private void UpdateDebugBlood()
        {
            if (!OrganSystem.BloodEffects.Value)
            {
                _bloodSources.Clear();
                _bloodParticles.Clear();
                return;
            }

            float now = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            for (int i = _bloodParticles.Count - 1; i >= 0; i--)
            {
                DebugBloodParticle particle = _bloodParticles[i];
                if (particle.Expires <= now)
                {
                    _bloodParticles.RemoveAt(i);
                    continue;
                }
                Vector3 previous = particle.Position;
                particle.Velocity += Physics.gravity * dt;
                Vector3 next = particle.Position + particle.Velocity * dt;
                Vector3 travel = next - previous;
                RaycastHit hit;
                if (travel.sqrMagnitude > 0.000001f && Physics.Raycast(previous,
                    travel.normalized, out hit, travel.magnitude,
                    EFTHardSettings.Instance.ENVIRONMENT_HIT_MASK))
                {
                    if (now >= _nextBloodDecalTime && Singleton<Effects>.Instantiated)
                    {
                        Singleton<Effects>.Instance.EmitBleeding(hit.point, hit.normal);
                        _nextBloodDecalTime = now + 0.10f;
                    }
                    _bloodParticles.RemoveAt(i);
                    continue;
                }
                particle.Position = next;
                _bloodParticles[i] = particle;
            }

            if (_player == null || _health == null) return;
            bool alive = _health.IsAlive;
            if (_wasAlive && !alive && !_corpseBloodInitialized)
            {
                EnsureCorpseBloodReserve();
                for (int i = 0; i < _bloodSources.Count; i++)
                    _bloodSources[i].Created = now;
            }
            _wasAlive = alive;

            if (!alive && _corpseBloodReserve <= 0f)
                return;

            float blockerMultiplier = GetTreatableBleedMultiplier();
            float totalSourceDps = 0f;
            for (int i = _bloodSources.Count - 1; i >= 0; i--)
            {
                DebugBloodSource source = _bloodSources[i];
                if (source.Attachment == null)
                {
                    _bloodSources.RemoveAt(i);
                    continue;
                }
                float corpseDecay = 1f;
                if (!alive)
                {
                    corpseDecay = 1f - Mathf.Clamp01(
                        (now - source.Created) / CorpseBleedDuration);
                    if (corpseDecay <= 0f)
                    {
                        _bloodSources.RemoveAt(i);
                        continue;
                    }
                }
                float decay = source.Heart ? 1f : GetDecayStrength(source.Created);
                float dps = source.Strength * decay * corpseDecay *
                    (source.Heart ? 1f : blockerMultiplier);
                totalSourceDps += dps;
                float rate = Mathf.Clamp(dps * 0.55f, 0.5f, 24f);
                source.EmissionAccumulator += rate * dt;
                int count = Mathf.Min(3, Mathf.FloorToInt(source.EmissionAccumulator));
                source.EmissionAccumulator -= count;
                for (int n = 0; n < count && _bloodParticles.Count < 192; n++)
                {
                    Vector3 origin = source.Attachment.TransformPoint(source.LocalPosition);
                    Vector3 outward = source.Attachment.rotation * source.LocalDirection;
                    if (source.Heart)
                    {
                        SpawnDebugBlood(origin, outward, dps, 1f, Vector3.zero);
                        SpawnDebugBlood(origin, outward, dps, 0.32f,
                            source.Attachment.right * 0.10f);
                        SpawnDebugBlood(origin, outward, dps, 0.24f,
                            -source.Attachment.right * 0.10f);
                    }
                    else if (source.Head)
                    {
                        SpawnDebugBlood(origin, outward, dps, 0.20f,
                            source.Attachment.right * 0.16f);
                        SpawnDebugBlood(origin, outward, dps, 0.20f,
                            -source.Attachment.right * 0.16f);
                    }
                    else SpawnDebugBlood(origin, outward, dps, 0.55f, Vector3.zero);
                }
            }
            if (!alive)
            {
                _corpseBloodReserve = Mathf.Max(0f,
                    _corpseBloodReserve - totalSourceDps * dt);
                if (_corpseBloodReserve <= 0f) _bloodSources.Clear();
            }
        }

        private float GetRemainingBodyHealth()
        {
            float total = 0f;
            total += Mathf.Max(0f, _health.GetBodyPartHealth(EBodyPart.Head).Current);
            total += Mathf.Max(0f, _health.GetBodyPartHealth(EBodyPart.Chest).Current);
            total += Mathf.Max(0f, _health.GetBodyPartHealth(EBodyPart.Stomach).Current);
            total += Mathf.Max(0f, _health.GetBodyPartHealth(EBodyPart.LeftArm).Current);
            total += Mathf.Max(0f, _health.GetBodyPartHealth(EBodyPart.RightArm).Current);
            total += Mathf.Max(0f, _health.GetBodyPartHealth(EBodyPart.LeftLeg).Current);
            total += Mathf.Max(0f, _health.GetBodyPartHealth(EBodyPart.RightLeg).Current);
            return total;
        }

        private void EnsureCorpseBloodReserve()
        {
            if (_corpseBloodInitialized || _health == null) return;
            _corpseBloodReserve = GetRemainingBodyHealth();
            _corpseBloodInitialized = true;
            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info(string.Format(
                    "[BloodFX] Corpse blood reserve={0:0.0} HP; duration will follow wound DPS",
                    _corpseBloodReserve));
        }

        private void SpawnDebugBlood(Vector3 origin, Vector3 outward, float dps,
            float speedScale, Vector3 lateral)
        {
            if (_bloodParticles.Count >= 192) return;
            float speed = Mathf.Lerp(0.20f, 0.82f, Mathf.Clamp01(dps / 40f)) * speedScale;
            Vector3 velocity = outward * speed + lateral;
            velocity += Random.insideUnitSphere * 0.10f +
                Vector3.up * Random.Range(0.03f, 0.16f) * speedScale;
            _bloodParticles.Add(new DebugBloodParticle
            {
                Position = origin + outward * 0.012f,
                Velocity = velocity,
                Expires = Time.unscaledTime + Random.Range(0.8f, 1.45f),
                Size = Random.Range(0.007f, 0.014f),
                TrailLength = Mathf.Clamp(velocity.magnitude *
                    Random.Range(0.045f, 0.085f), 0.012f, 0.065f)
            });
        }

        private void UpdateBruising()
        {
            if (_bruiseStrength <= 0f && Mathf.Approximately(_appliedRestorePenalty, 0f))
                return;

            if (_player == null || _player.Physical == null) return;
            if (!Mathf.Approximately(_appliedRestorePenalty, 0f))
                _player.Physical.RestoreRateBuff -= _appliedRestorePenalty;
            float remaining = Mathf.Clamp01((_bruiseExpires - Time.unscaledTime) / BruiseDuration);
            _currentBruiseStrength = _bruiseStrength * remaining;
            if (remaining <= 0f) _bruiseStrength = 0f;
            if (remaining <= 0f) _bruiseUiEffect = null;
            _appliedRestorePenalty = -_player.Physical.StaminaRestoreRate *
                0.30f * _currentBruiseStrength;
            _player.Physical.RestoreRateBuff += _appliedRestorePenalty;
        }

        private bool HasRecurringWork()
        {
            bool hasActiveTrauma = _health != null && _health.IsAlive &&
                (_chestWounds.Active || _heartWounds.Active || _faceWounds.Active ||
                 _bodyWounds.Count > 0);
            return hasActiveTrauma ||
                   _bruiseStrength > 0f ||
                   !Mathf.Approximately(_appliedRestorePenalty, 0f) ||
                   _impacts.Count > 0 ||
                   _bloodSources.Count > 0 ||
                   _bloodParticles.Count > 0;
        }

        private void DisableWhenIdle()
        {
            if (!HasRecurringWork())
                enabled = false;
        }

        private float GetShareFraction(EBodyPart source)
        {
            ValueStruct health = _health.GetBodyPartHealth(source);
            float ratio = health.Maximum > 0f
                ? Mathf.Clamp01(health.Current / health.Maximum)
                : 0f;
            return Mathf.Lerp(1f, 0.25f, ratio);
        }

        private float EstimateLinkedDamageMultiplier(EBodyPart source)
        {
            EBodyPart[] targets = GetSharedTargets(source);
            if (targets == null || targets.Length == 0)
                return 1f;
            return 1f + GetShareFraction(source) *
                GetLinkageMultiplier(source);
        }

        private void ApplyPrimaryAndShared(EBodyPart source, float damage, DamageInfo damageInfo)
        {
            if (damage <= 0f || !IsHealthAlive()) return;

            if (_health.IsBodyPartDestroyed(source))
            {
                EBodyPart bypass = GetBypassTarget(source);
                if (bypass != EBodyPart.Common)
                    ApplyBypassedDamage(bypass, damage * GetLinkageMultiplier(source),
                        damageInfo, 1);
                return;
            }

            float sharedPool = damage * GetShareFraction(source) *
                GetLinkageMultiplier(source);
            ApplyTraumaDamage(source, damage, damageInfo);
            if (!IsHealthAlive()) return;

            EBodyPart[] targets = GetSharedTargets(source);
            if (targets == null || targets.Length == 0 || sharedPool <= 0f) return;
            float perTarget = sharedPool / targets.Length;
            for (int i = 0; i < targets.Length; i++)
            {
                if (!IsHealthAlive()) break;
                ApplyBypassedDamage(targets[i], perTarget, damageInfo, 0);
            }
        }

        private void ApplyBypassedDamage(EBodyPart target, float damage,
            DamageInfo damageInfo, int crossedBlackedParts)
        {
            if (damage <= 0f || !IsHealthAlive()) return;
            int guard = 0;
            while (IsHealthAlive() && target != EBodyPart.Common &&
                _health.IsBodyPartDestroyed(target) && guard++ < 4)
            {
                crossedBlackedParts++;
                EBodyPart next = GetBypassTarget(target);
                if (next == target) return;
                target = next;
            }
            if (IsHealthAlive() && target != EBodyPart.Common &&
                !_health.IsBodyPartDestroyed(target))
            {
                float retained = crossedBlackedParts <= 0 ? 1f : crossedBlackedParts == 1
                    ? OrganSystem.OneBlackedRetention.Value : crossedBlackedParts == 2
                        ? OrganSystem.TwoBlackedRetention.Value
                        : OrganSystem.ThreePlusBlackedRetention.Value;
                ApplyTraumaDamage(target, damage * retained, damageInfo);
            }
        }

        private void ApplyTraumaDamage(EBodyPart bodyPart, float damage,
            DamageInfo damageInfo)
        {
            if (damage <= 0f || !IsHealthAlive()) return;

            float now = Time.unscaledTime;
            bool allowPresentation = now >= _nextPresentationTime;
            if (allowPresentation)
                _nextPresentationTime = now + GetBleedPresentationInterval();

            bool previousInside = TraumaPresentationContext.InsideTraumaDamage;
            bool previousAllowance = TraumaPresentationContext.AllowPresentation;
            TraumaPresentationContext.InsideTraumaDamage = true;
            TraumaPresentationContext.AllowPresentation = allowPresentation;
            _traumaDeathVoicePending = bodyPart != EBodyPart.Head;
            try
            {
                _health.ApplyDamage(bodyPart, damage, damageInfo);
            }
            catch (System.NullReferenceException) when (!IsHealthAlive())
            {
            }
            finally
            {
                TraumaPresentationContext.InsideTraumaDamage = previousInside;
                TraumaPresentationContext.AllowPresentation = previousAllowance;
                _traumaDeathVoicePending = false;
            }
        }

        private bool IsHealthAlive()
        { return _health != null && _health.IsAlive; }

        private static EBodyPart[] GetSharedTargets(EBodyPart source)
        {
            switch (source)
            {
                case EBodyPart.Head: return HeadSharedTargets;
                case EBodyPart.Chest: return ChestSharedTargets;
                case EBodyPart.Stomach: return StomachSharedTargets;
                case EBodyPart.LeftArm:
                case EBodyPart.RightArm: return ArmSharedTargets;
                case EBodyPart.LeftLeg:
                case EBodyPart.RightLeg: return LegSharedTargets;
                default: return null;
            }
        }

        private static EBodyPart GetBypassTarget(EBodyPart source)
        {
            switch (source)
            {
                case EBodyPart.LeftLeg:
                case EBodyPart.RightLeg: return EBodyPart.Stomach;
                case EBodyPart.LeftArm:
                case EBodyPart.RightArm:
                case EBodyPart.Stomach: return EBodyPart.Chest;
                default: return EBodyPart.Common;
            }
        }

        private static float GetLinkageMultiplier(EBodyPart source)
        {
            switch (source)
            {
                case EBodyPart.LeftArm:
                case EBodyPart.RightArm: return OrganSystem.ArmLinkageMultiplier.Value;
                case EBodyPart.LeftLeg:
                case EBodyPart.RightLeg: return OrganSystem.LegLinkageMultiplier.Value;
                case EBodyPart.Stomach: return OrganSystem.StomachLinkageMultiplier.Value;
                default: return 1f;
            }
        }

        private static readonly EBodyPart[] HeadSharedTargets = { EBodyPart.Chest };
        private static readonly EBodyPart[] ChestSharedTargets =
            { EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.Stomach };
        private static readonly EBodyPart[] StomachSharedTargets =
            { EBodyPart.Chest, EBodyPart.LeftLeg, EBodyPart.RightLeg };
        private static readonly EBodyPart[] ArmSharedTargets = { EBodyPart.Chest };
        private static readonly EBodyPart[] LegSharedTargets = { EBodyPart.Stomach };
        private static readonly EBodyPart[] TreatableBodyParts =
        {
            EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm,
            EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        private void EnsureMarkers()
        {
            if (_health == null || _addingMarker) return;
            _addingMarker = true;
            try
            {
                if (_heartWounds.Active && _health.FindExistingEffect<IHeavyBleeding>(EBodyPart.Chest) == null)
                    _health.DoBleed<ActiveHealthController.HeavyBleeding>(EBodyPart.Chest);
                if (_chestWounds.Active && !_heartWounds.Active)
                    EnsureWoundMarker(EBodyPart.Chest, _chestWounds.StrongestType);
                if (_faceWounds.Active)
                    EnsureWoundMarker(EBodyPart.Head, _faceWounds.StrongestType);
                foreach (KeyValuePair<EBodyPart, WoundTrack> pair in _bodyWounds)
                    if (pair.Value.Active)
                        EnsureWoundMarker(pair.Key, pair.Value.StrongestType);
            }
            finally { _addingMarker = false; }
        }

        private void EnsureWoundMarker(EBodyPart bodyPart, BleedType type)
        {
            bool needsHeavy = type == BleedType.Heavy;
            ActiveHealthController.LightBleeding light =
                _health.FindExistingEffect<ActiveHealthController.LightBleeding>(bodyPart);
            ActiveHealthController.HeavyBleeding heavy =
                _health.FindExistingEffect<ActiveHealthController.HeavyBleeding>(bodyPart);
            if (needsHeavy)
            {
                if (light != null) light.ForceRemove();
                if (heavy == null)
                    _health.DoBleed<ActiveHealthController.HeavyBleeding>(bodyPart);
                return;
            }
            if (heavy != null) heavy.ForceRemove();
            if (light == null)
                _health.DoBleed<ActiveHealthController.LightBleeding>(bodyPart);
        }

        private void UpdateNativeBleedStrength()
        {
            if (_health == null) return;
            float treatableMultiplier = GetTreatableBleedMultiplier();
            ActiveHealthController.LightBleeding chestBleed =
                _health.FindExistingEffect<ActiveHealthController.LightBleeding>(EBodyPart.Chest);
            if (chestBleed != null)
            {
                chestBleed.float_15 = 0f;
                NativeEffectLabels.UpdateBleedPresentation(chestBleed,
                    _chestWounds.GetTimeLeft(BleedType.Light),
                    _chestWounds.GetDamagePerSecond(BleedType.Light) *
                    treatableMultiplier);
            }

            ActiveHealthController.HeavyBleeding heartBleed =
                _health.FindExistingEffect<ActiveHealthController.HeavyBleeding>(EBodyPart.Chest);
            if (heartBleed != null)
            {
                heartBleed.float_15 = 0f;
                float timeLeft = _heartWounds.Active
                    ? float.PositiveInfinity
                    : _chestWounds.GetTimeLeft(BleedType.Heavy);
                float damagePerSecond =
                    _heartWounds.GetPermanentDamagePerSecond() +
                    _chestWounds.GetDamagePerSecond(BleedType.Heavy) *
                    treatableMultiplier;
                NativeEffectLabels.UpdateBleedPresentation(heartBleed,
                    timeLeft, damagePerSecond);
            }

            ActiveHealthController.LightBleeding faceLightBleed =
                _health.FindExistingEffect<ActiveHealthController.LightBleeding>(EBodyPart.Head);
            if (faceLightBleed != null)
            {
                faceLightBleed.float_15 = 0f;
                NativeEffectLabels.UpdateBleedPresentation(faceLightBleed,
                    _faceWounds.GetTimeLeft(BleedType.Light),
                    _faceWounds.GetDamagePerSecond(BleedType.Light) *
                    treatableMultiplier);
            }
            ActiveHealthController.HeavyBleeding faceBleed =
                _health.FindExistingEffect<ActiveHealthController.HeavyBleeding>(EBodyPart.Head);
            if (faceBleed != null)
            {
                faceBleed.float_15 = 0f;
                NativeEffectLabels.UpdateBleedPresentation(faceBleed,
                    _faceWounds.GetTimeLeft(BleedType.Heavy),
                    _faceWounds.GetDamagePerSecond(BleedType.Heavy) *
                    treatableMultiplier);
            }
            foreach (KeyValuePair<EBodyPart, WoundTrack> pair in _bodyWounds)
            {
                ActiveHealthController.LightBleeding bleed =
                    _health.FindExistingEffect<ActiveHealthController.LightBleeding>(pair.Key);
                if (bleed != null)
                {
                    bleed.float_15 = 0f;
                    NativeEffectLabels.UpdateBleedPresentation(bleed,
                        pair.Value.GetTimeLeft(BleedType.Light),
                        pair.Value.GetDamagePerSecond(BleedType.Light) *
                        treatableMultiplier);
                }
                ActiveHealthController.HeavyBleeding heavyBleed =
                    _health.FindExistingEffect<ActiveHealthController.HeavyBleeding>(pair.Key);
                if (heavyBleed != null)
                {
                    heavyBleed.float_15 = 0f;
                    NativeEffectLabels.UpdateBleedPresentation(heavyBleed,
                        pair.Value.GetTimeLeft(BleedType.Heavy),
                        pair.Value.GetDamagePerSecond(BleedType.Heavy) *
                        treatableMultiplier);
                }
            }
        }

        private void OnEffectRemoved(IHealthEffect effect)
        {
            if (_addingMarker || effect == null) return;
            if (effect is ILightBleeding || effect is IHeavyBleeding)
                AccelerateTreatableBleed(effect);
        }

        private void OnEffectResidual(IHealthEffect effect)
        {
            if (_addingMarker || effect == null) return;
            if (effect is ILightBleeding || effect is IHeavyBleeding)
                AccelerateTreatableBleed(effect);
        }

        private void AccelerateTreatableBleed(IHealthEffect removedEffect)
        {
            if (_health == null || _addingMarker || removedEffect == null ||
                !_acceleratedBleedEffects.Add(removedEffect)) return;

            EBodyPart bodyPart = removedEffect.BodyPart;

            if (bodyPart == EBodyPart.Chest &&
                removedEffect is IHeavyBleeding && _heartWounds.Active)
            {
                enabled = true;
                if (OrganSystem.DebugLogging.Value)
                    TraumaLog.Info(
                        "[Trauma] In-raid treatment cannot close a heart " +
                        "wound; restoring its native Heavy Bleeding marker");
                return;
            }

            WoundTrack wound;
            switch (bodyPart)
            {
                case EBodyPart.Chest:
                    wound = _chestWounds;
                    break;
                case EBodyPart.Head:
                    wound = _faceWounds;
                    break;
                default:
                    _bodyWounds.TryGetValue(bodyPart, out wound);
                    break;
            }

            if (wound == null || !wound.Active) return;

            float clottingProgress = removedEffect is IHeavyBleeding ? 0.99f : 0.90f;
            wound.AdvanceClotting(clottingProgress);
            EnsureMarkers();

            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info(string.Format(
                    "[Trauma] Treatment advanced clotting on {0} by {1:0}%",
                    bodyPart, clottingProgress * 100f));
        }

        private void ExpireClottedBleeds()
        {
            if (_health == null) return;
            bool hadChestWounds = _chestWounds.Active;
            _chestWounds.RemoveClottedWounds();
            if (hadChestWounds && !_chestWounds.Active)
                RemoveClottedBleedMarker(EBodyPart.Chest,
                    FindTreatableBleedMarker(EBodyPart.Chest));
            bool hadFaceWounds = _faceWounds.Active;
            _faceWounds.RemoveClottedWounds();
            if (hadFaceWounds && !_faceWounds.Active)
                RemoveClottedBleedMarker(EBodyPart.Head,
                    FindTreatableBleedMarker(EBodyPart.Head));

            for (int i = 0; i < TreatableBodyParts.Length; i++)
            {
                EBodyPart bodyPart = TreatableBodyParts[i];
                if (!_bodyWounds.TryGetValue(bodyPart, out WoundTrack wound)) continue;
                wound.RemoveClottedWounds();
                if (wound.Active) continue;
                RemoveClottedBleedMarker(bodyPart,
                    FindTreatableBleedMarker(bodyPart));
                _bodyWounds.Remove(bodyPart);
            }
        }

        private ActiveHealthController.Effect FindTreatableBleedMarker(
            EBodyPart bodyPart)
        {
            ActiveHealthController.LightBleeding light =
                _health.FindExistingEffect<ActiveHealthController.LightBleeding>(bodyPart);
            return light != null ? light :
                _health.FindExistingEffect<ActiveHealthController.HeavyBleeding>(bodyPart);
        }

        private void RemoveClottedBleedMarker(EBodyPart bodyPart,
            ActiveHealthController.Effect marker)
        {

            for (int i = _bloodSources.Count - 1; i >= 0; i--)
                if (!_bloodSources[i].Heart && _bloodSources[i].BodyPart == bodyPart)
                    _bloodSources.RemoveAt(i);

            _addingMarker = true;
            try
            {
                if (marker != null) marker.ForceRemove();
            }
            finally { _addingMarker = false; }

            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info($"[Trauma] Treatable bleed clotted on {bodyPart}");
        }

        private void ClearLinkedTreatableBleeds()
        {
            if (_health == null || _addingMarker) return;
            _addingMarker = true;
            try
            {
                _chestWounds.Clear();
                _faceWounds.Clear();
                _bodyWounds.Clear();
                for (int i = _bloodSources.Count - 1; i >= 0; i--)
                    if (!_bloodSources[i].Heart) _bloodSources.RemoveAt(i);
                List<IHealthEffect> effects = new List<IHealthEffect>(_health.GetAllActiveEffects());
                for (int i = 0; i < effects.Count; i++)
                    if ((effects[i] is ILightBleeding || effects[i] is IHeavyBleeding) &&
                        effects[i] is ActiveHealthController.Effect activeEffect)
                        activeEffect.ForceRemove();
            }
            finally { _addingMarker = false; }

            if (_heartWounds.Active) EnsureMarkers();
            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info(_heartWounds.Active
                    ? "[Trauma] All treatable linked bleeds healed; permanent heart hemorrhage continues"
                    : "[Trauma] All linked bleed wounds healed");
        }

        private void Unsubscribe()
        {
            if (_subscribed && _health != null)
            {
                _health.EffectRemovedEvent -= OnEffectRemoved;
                _health.EffectResidualEvent -= OnEffectResidual;
            }
            _subscribed = false;
            _bloodLossBlockerWasActive = false;
            _acceleratedBleedEffects.Clear();
        }

        private void OnDestroy()
        {
            if (_player != null && _player.Physical != null &&
                !Mathf.Approximately(_appliedRestorePenalty, 0f))
                _player.Physical.RestoreRateBuff -= _appliedRestorePenalty;
            _appliedRestorePenalty = 0f;
            _bloodSources.Clear();
            _bloodParticles.Clear();
            _heartWounds.Clear();
            Unsubscribe();
        }
    }
}
