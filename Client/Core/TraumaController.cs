using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using Comfort.Common;
using TraumaCore.Patches;
using Systems.Effects;
using UnityEngine;
using System.Collections.Generic;

namespace TraumaCore
{
    internal sealed class TraumaController : MonoBehaviour
    {
        private sealed class BodyWoundTrack
        {
            internal int Count;
            internal float Effective;
            internal float LastWoundTime = float.MinValue;
        }
        internal struct DebugImpact
        {
            internal Vector3 HitPoint, Direction, Intersection, BoneIntersection;
            internal float Expires;
            internal bool Heart;
            internal bool Brain;
            internal bool CervicalSpine;
            internal bool ThoracicSpine;
            internal bool ArmorStopped;
            internal bool Bone;
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
        private const float PresentationInterval = 0.25f;
        private float _nextPresentationTime;
        private int _chestStacks;
        private float _effectiveChestStacks;
        private float _lastChestWoundTime = float.MinValue;
        private float _lastChestInterval;
        private float _lastChestSeverity = 1f;
        private bool _heartWound;
        private int _heartWounds;
        private float _effectiveHeartWounds;
        private float _lastHeartWoundTime = float.MinValue;
        private float _lastHeartInterval;
        private float _lastHeartSeverity = 1f;
        private int _faceWounds;
        private float _effectiveFaceWounds;
        private float _lastFaceWoundTime = float.MinValue;
        private bool _subscribed;
        private bool _addingMarker;
        private bool _bloodLossBlockerWasActive;
        private float _bruiseStrength;
        private float _currentBruiseStrength;
        private float _bruiseExpires;
        private float _appliedRestorePenalty;
        private BruisedHealthEffect _bruiseUiEffect;
        private HeartWoundHealthEffect _heartWoundUiEffect;
        private const float BruiseDuration = 15f;
        private const float CorpseBleedDuration = 8f;
        private Vector3 _lastImpactPoint;
        private Vector3 _lastImpactDirection = Vector3.forward;
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
        private readonly Dictionary<int, int> _bloodVisualHitCounts =
            new Dictionary<int, int>();
        private readonly List<BodyRenderer> _bodyRenderers = new List<BodyRenderer>(12);
        private readonly Dictionary<EBodyPart, BodyWoundTrack> _bodyWounds =
            new Dictionary<EBodyPart, BodyWoundTrack>();

        internal int ChestStacks { get { return _chestStacks; } }
        internal float EffectiveChestStacks { get { return _effectiveChestStacks; } }
        internal float LastChestInterval { get { return _lastChestInterval; } }
        internal float LastChestSeverity { get { return _lastChestSeverity; } }
        internal bool HasHeartWound { get { return _heartWound; } }
        internal int HeartWounds { get { return _heartWounds; } }
        internal float EffectiveHeartWounds { get { return _effectiveHeartWounds; } }
        internal float LastHeartInterval { get { return _lastHeartInterval; } }
        internal float LastHeartSeverity { get { return _lastHeartSeverity; } }
        internal float BruiseStrength { get { return _currentBruiseStrength; } }
        internal float BruiseTimeLeft { get { return Mathf.Max(0f, _bruiseExpires - Time.unscaledTime); } }
        internal int FaceWounds { get { return _faceWounds; } }
        internal float EffectiveFaceWounds { get { return _effectiveFaceWounds; } }
        internal float ChestDecayStrength { get { return GetDecayStrength(_lastChestWoundTime); } }
        internal float FaceDecayStrength { get { return GetDecayStrength(_lastFaceWoundTime); } }
        internal bool TraumaDeathVoicePending { get { return _traumaDeathVoicePending; } }
        internal bool HeartDeathVoicePending
        { get { return _traumaDeathVoicePending && _heartDamageContext; } }
        internal bool HeadDeathVoicePending { get { return _headDeathVoicePending; } }

        internal void SetHeadDeathVoicePending(bool pending)
        { _headDeathVoicePending = pending; }
        internal int StomachWounds
        { get { return _bodyWounds.TryGetValue(EBodyPart.Stomach, out BodyWoundTrack t) ? t.Count : 0; } }
        internal int LimbWounds
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<EBodyPart, BodyWoundTrack> pair in _bodyWounds)
                    if (pair.Key != EBodyPart.Stomach) count += pair.Value.Count;
                return count;
            }
        }
        internal IList<DebugImpact> DebugImpacts { get { return _impacts; } }
        internal IList<DebugBloodParticle> DebugBloodParticles { get { return _bloodParticles; } }

        internal float BleedDamagePerSecond
        {
            get
            {
                if (_health == null) return 0f;
                float treatableDps = _effectiveChestStacks * ChestDecayStrength;
                if (_faceWounds > 0) treatableDps += _effectiveFaceWounds * FaceDecayStrength;
                foreach (KeyValuePair<EBodyPart, BodyWoundTrack> pair in _bodyWounds)
                {
                    BodyWoundTrack track = pair.Value;
                    if (track.Count <= 0) continue;
                    float primary = track.Effective * GetDecayStrength(track.LastWoundTime);
                    treatableDps += primary * (1f + GetShareFraction(pair.Key) *
                        GetLinkageMultiplier(pair.Key));
                }
                float dps = treatableDps * GetTreatableBleedMultiplier();
                if (_heartWound) dps += _effectiveHeartWounds;
                return dps;
            }
        }

        internal void Initialize(Player player)
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
        }

        internal void AddChestWound(float originalBulletDamage)
        {
            if (_health == null) return;
            _chestStacks++;
            float now = Time.unscaledTime;
            float interval = now - _lastChestWoundTime;
            _lastChestWoundTime = now;
            _lastChestInterval = _chestStacks > 1 && interval > 0f ? interval : 0f;
            _lastChestSeverity = Mathf.Max(0f, originalBulletDamage) /
                Mathf.Max(0.01f, OrganSystem.NormalBleedDivisor.Value);
            _effectiveChestStacks += _lastChestSeverity;
            AddDebugBloodSource(_lastChestSeverity, false, false, EBodyPart.Chest);
            if (OrganSystem.DebugLogging.Value)
                Plugin.Log.LogInfo(string.Format(
                    "[Trauma] Chest wound #{0}: interval={1:0.0}ms, added={2:0.00} HP/s, total={3:0.00} HP/s",
                    _chestStacks, _lastChestInterval * 1000f,
                    _lastChestSeverity, _effectiveChestStacks));
            EnsureMarkers();
        }

        internal void AddHeartWound(float originalBulletDamage)
        {
            if (_health == null) return;
            _heartWound = true;
            float now = Time.unscaledTime;
            float interval = now - _lastHeartWoundTime;
            _heartWounds++;
            _lastHeartWoundTime = now;
            _lastHeartInterval = _heartWounds > 1 ? interval : 0f;
            _lastHeartSeverity = Mathf.Max(0f, originalBulletDamage) /
                Mathf.Max(0.01f, OrganSystem.HeartBleedDivisor.Value);
            _effectiveHeartWounds += _lastHeartSeverity;
            _heartWoundUiEffect = _health.FindExistingEffect<HeartWoundHealthEffect>(
                EBodyPart.Chest);
            if (_heartWoundUiEffect == null)
                _heartWoundUiEffect = _health.AddEffect<HeartWoundHealthEffect>(
                    EBodyPart.Chest, 0f, null, null, null);
            AddDebugBloodSource(_lastHeartSeverity, true, false, EBodyPart.Chest);
            if (OrganSystem.DebugLogging.Value)
                Plugin.Log.LogInfo(string.Format(
                    "[Trauma] Heart wound #{0}: interval={1:0.0}ms, added={2:0.00} HP/s, total={3:0.00} HP/s",
                    _heartWounds, _lastHeartInterval * 1000f,
                    _lastHeartSeverity, _effectiveHeartWounds));
            EnsureMarkers();
        }

        internal void AddFaceWound(float originalBulletDamage)
        {
            if (_health == null) return;
            _faceWounds++;
            _lastFaceWoundTime = Time.unscaledTime;
            float severity = Mathf.Max(0f, originalBulletDamage) /
                Mathf.Max(0.01f, OrganSystem.HeadBleedDivisor.Value);
            _effectiveFaceWounds += severity;
            AddDebugBloodSource(severity, false, true, EBodyPart.Head);
            EnsureMarkers();
        }

        internal void AddFatalHeadBlood(float originalBulletDamage)
        {
            float severity = Mathf.Max(0f, originalBulletDamage) /
                Mathf.Max(0.01f, OrganSystem.HeadBleedDivisor.Value);
            AddDebugBloodSource(severity, false, true, EBodyPart.Head);
        }

        internal void AddCorpseWound(EBodyPart bodyPart, float originalBulletDamage)
        {
            if (_health == null) return;
            EnsureCorpseBloodReserve();
            if (_corpseBloodReserve <= 0f) return;

            float divisor = bodyPart == EBodyPart.Head
                ? OrganSystem.HeadBleedDivisor.Value
                : OrganSystem.NormalBleedDivisor.Value;
            float severity = Mathf.Clamp(Mathf.Max(0f, originalBulletDamage) /
                Mathf.Max(0.01f, divisor) * 0.25f, 0.20f, 3f);
            AddDebugBloodSource(severity, false,
                bodyPart == EBodyPart.Head, bodyPart);
            if (OrganSystem.DebugLogging.Value)
                Plugin.Log.LogInfo(string.Format(
                    "[BloodFX] Corpse wound on {0}: strength={1:0.00}, reserve={2:0.0}",
                    bodyPart, severity, _corpseBloodReserve));
        }

        internal void AddBodyWound(EBodyPart bodyPart, float originalBulletDamage)
        {
            if (_health == null || (bodyPart != EBodyPart.Stomach &&
                bodyPart != EBodyPart.LeftArm && bodyPart != EBodyPart.RightArm &&
                bodyPart != EBodyPart.LeftLeg && bodyPart != EBodyPart.RightLeg)) return;
            if (!_bodyWounds.TryGetValue(bodyPart, out BodyWoundTrack track))
            {
                track = new BodyWoundTrack();
                _bodyWounds.Add(bodyPart, track);
            }
            track.Count++;
            float bodyScale = bodyPart == EBodyPart.Stomach ? 0.75f : 0.50f;
            float severity = Mathf.Max(0f, originalBulletDamage) /
                Mathf.Max(0.01f, OrganSystem.NormalBleedDivisor.Value) * bodyScale;
            track.Effective += severity;
            track.LastWoundTime = Time.unscaledTime;
            AddDebugBloodSource(severity, false, false, bodyPart);
            EnsureMarkers();
            if (OrganSystem.DebugLogging.Value)
                Plugin.Log.LogInfo(string.Format(
                    "[Trauma] {0} wound #{1}: effective={2:0.00} scale={3:0.00}",
                    bodyPart, track.Count, track.Effective, bodyScale));
        }

        private static float GetDecayStrength(float lastWoundTime)
        {
            if (lastWoundTime == float.MinValue) return 0f;
            float duration = Mathf.Max(0.01f, OrganSystem.NonHeartDecayDuration.Value);
            float progress = Mathf.Clamp01((Time.unscaledTime - lastWoundTime) / duration);
            return Mathf.Lerp(1f, OrganSystem.NonHeartMinimumStrength, progress);
        }

        private float GetTreatableBleedMultiplier()
        {
            return _health != null && _health.HasBloodLossBlockers()
                ? OrganSystem.BloodLossBlockerDamageMultiplier
                : 1f;
        }

        internal void RecordImpact(Vector3 hitPoint, Vector3 direction,
            Vector3 intersection, bool heart, bool brain = false,
            bool armorStopped = false, bool bone = false,
            Vector3 boneIntersection = default(Vector3),
            Transform hitTransform = null, bool cervicalSpine = false,
            bool thoracicSpine = false)
        {
            _lastImpactPoint = hitPoint;
            _lastImpactDirection = direction.sqrMagnitude > 0.0001f
                ? direction.normalized : Vector3.forward;
            _lastImpactTransform = hitTransform;
            if (_impacts.Count >= 12) _impacts.RemoveAt(0);
            _impacts.Add(new DebugImpact
            {
                HitPoint = hitPoint,
                Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward,
                Intersection = intersection,
                Heart = heart,
                Brain = brain,
                CervicalSpine = cervicalSpine,
                ThoracicSpine = thoracicSpine,
                Bone = bone,
                BoneIntersection = boneIntersection,
                ArmorStopped = armorStopped,
                Expires = Time.unscaledTime + 10f
            });
        }

        internal void AddBruise(float stoppedBulletDamage)
        {
            float existing = _currentBruiseStrength;
            _bruiseStrength = Mathf.Clamp01(existing + Mathf.Max(0f, stoppedBulletDamage) / 100f);
            _bruiseExpires = Time.unscaledTime + BruiseDuration;
            _currentBruiseStrength = _bruiseStrength;
            if (_health != null)
            {
                _bruiseUiEffect = _health.FindExistingEffect<BruisedHealthEffect>(EBodyPart.Chest);
                if (_bruiseUiEffect == null)
                    _bruiseUiEffect = _health.AddEffect<BruisedHealthEffect>(EBodyPart.Chest,
                        0f, BruiseDuration, 0f, _bruiseStrength);
                else
                {
                    _bruiseUiEffect.AddWorkTime(BruiseDuration, true);
                    if (_bruiseStrength > _bruiseUiEffect.Strength)
                        _bruiseUiEffect.SetStrength(_bruiseStrength);
                }
            }
            if (OrganSystem.DebugLogging.Value)
                Plugin.Log.LogInfo(string.Format("[Bruised] +{0:0.00}, strength={1:0.00}, duration={2:0.0}s",
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
            if (_health == null || !_health.IsAlive) return;
            bool bloodLossBlockerActive = _health.HasBloodLossBlockers();
            if (bloodLossBlockerActive && !_bloodLossBlockerWasActive)
                ClearLinkedTreatableBleeds();
            _bloodLossBlockerWasActive = bloodLossBlockerActive;
            if (_chestStacks == 0 && !_heartWound && _faceWounds == 0 &&
                _bodyWounds.Count == 0) return;
            _traumaAccumulator += Time.deltaTime;
            if (_traumaAccumulator < TraumaStep) return;
            float deltaTime = _traumaAccumulator;
            _traumaAccumulator = 0f;
            UpdateNativeBleedStrength();

            float treatableMultiplier = GetTreatableBleedMultiplier();
            float damagePerSecond = _effectiveChestStacks * ChestDecayStrength * treatableMultiplier;

            if (_heartWound) damagePerSecond += _effectiveHeartWounds;

            if (_faceWounds > 0)
            {
                float faceDps = GetAdditionalFaceBleedDps() * treatableMultiplier;
                if (faceDps > 0f)
                    ApplyPrimaryAndShared(EBodyPart.Head, faceDps * deltaTime,
                        DamageHelper.HeavyBleedingDamage);
                if (!IsHealthAlive()) return;
            }

            foreach (KeyValuePair<EBodyPart, BodyWoundTrack> pair in _bodyWounds)
            {
                BodyWoundTrack track = pair.Value;
                if (track.Count <= 0) continue;
                float decay = GetDecayStrength(track.LastWoundTime);
                float bodyDps = track.Effective * decay * treatableMultiplier;
                ApplyPrimaryAndShared(pair.Key, bodyDps * deltaTime,
                    DamageHelper.LightBleedingDamage);
                if (!IsHealthAlive()) return;
            }

            float damage = damagePerSecond * deltaTime;
            if (damage > 0f)
            {
                _heartDamageContext = _heartWound;
                try
                {
                    ApplyPrimaryAndShared(EBodyPart.Chest, damage,
                        _heartWound ? DamageHelper.HeavyBleedingDamage : DamageHelper.LightBleedingDamage);
                }
                finally
                {
                    _heartDamageContext = false;
                }
            }

            if (!IsHealthAlive()) return;
            EnsureMarkers();
        }

        private void AddDebugBloodSource(float strength, bool heart, bool head,
            EBodyPart bodyPart)
        {
            if (_player == null || strength <= 0f) return;
            Transform attachment = _lastImpactTransform != null
                ? _lastImpactTransform : _player.gameObject.transform;
            int visualKey = ((int)bodyPart << 1) | (heart ? 1 : 0);
            int priorHits = _bloodVisualHitCounts.TryGetValue(visualKey, out int hits)
                ? hits : 0;
            _bloodVisualHitCounts[visualKey] = priorHits + 1;
            float visualStrength = strength * Mathf.Pow(0.75f, priorHits);
            float now = Time.unscaledTime;

            for (int i = 0; i < _bloodSources.Count; i++)
                if (_bloodSources[i].BodyPart == bodyPart &&
                    _bloodSources[i].Heart == heart)
                    _bloodSources[i].Created = now;

            for (int i = 0; i < _bloodSources.Count; i++)
            {
                DebugBloodSource existing = _bloodSources[i];
                if (existing.Attachment == null || existing.Heart != heart || existing.Head != head)
                    continue;
                Vector3 existingWorld = existing.Attachment.TransformPoint(existing.LocalPosition);
                if ((existingWorld - _lastImpactPoint).sqrMagnitude > 0.0064f) continue;
                existing.Strength += visualStrength;
                existing.Created = now;
                existing.Attachment = attachment;
                existing.LocalPosition = attachment.InverseTransformPoint(_lastImpactPoint);
                existing.LocalDirection = (Quaternion.Inverse(attachment.rotation) *
                    -_lastImpactDirection).normalized;
                return;
            }

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

            if (!OrganSystem.BloodEffects.Value || (!alive && _corpseBloodReserve <= 0f))
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
                Plugin.Log.LogInfo(string.Format(
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

        private float GetShareFraction(EBodyPart source)
        {
            ValueStruct health = _health.GetBodyPartHealth(source);
            float ratio = health.Maximum > 0f
                ? Mathf.Clamp01(health.Current / health.Maximum)
                : 0f;
            return Mathf.Lerp(1f, 0.25f, ratio);
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
            if (allowPresentation) _nextPresentationTime = now + PresentationInterval;

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

        private void EnsureMarkers()
        {
            if (_health == null || _addingMarker) return;
            _addingMarker = true;
            try
            {
                if (_heartWound && _health.FindExistingEffect<IHeavyBleeding>(EBodyPart.Chest) == null)
                    _health.DoBleed<ActiveHealthController.HeavyBleeding>(EBodyPart.Chest);
                else if (_chestStacks > 0 &&
                    _health.FindExistingEffect<ILightBleeding>(EBodyPart.Chest) == null)
                    _health.DoBleed<ActiveHealthController.LightBleeding>(EBodyPart.Chest);
                if (_faceWounds > 0 &&
                    _health.FindExistingEffect<IHeavyBleeding>(EBodyPart.Head) == null)
                    _health.DoBleed<ActiveHealthController.HeavyBleeding>(EBodyPart.Head);
                foreach (KeyValuePair<EBodyPart, BodyWoundTrack> pair in _bodyWounds)
                    if (pair.Value.Count > 0 &&
                        _health.FindExistingEffect<ILightBleeding>(pair.Key) == null)
                        _health.DoBleed<ActiveHealthController.LightBleeding>(pair.Key);
            }
            finally { _addingMarker = false; }
        }

        private void UpdateNativeBleedStrength()
        {
            if (_health == null) return;
            ActiveHealthController.LightBleeding chestBleed =
                _health.FindExistingEffect<ActiveHealthController.LightBleeding>(EBodyPart.Chest);
            if (chestBleed != null) chestBleed.float_15 = 0f;

            ActiveHealthController.HeavyBleeding heartBleed =
                _health.FindExistingEffect<ActiveHealthController.HeavyBleeding>(EBodyPart.Chest);
            if (heartBleed != null) heartBleed.float_15 = 0f;

            ActiveHealthController.HeavyBleeding faceBleed =
                _health.FindExistingEffect<ActiveHealthController.HeavyBleeding>(EBodyPart.Head);
            if (faceBleed != null) faceBleed.float_15 = 0f;
            foreach (KeyValuePair<EBodyPart, BodyWoundTrack> pair in _bodyWounds)
            {
                ActiveHealthController.LightBleeding bleed =
                    _health.FindExistingEffect<ActiveHealthController.LightBleeding>(pair.Key);
                if (bleed != null)
                    bleed.float_15 = 0f;
            }
        }

        private float GetAdditionalFaceBleedDps()
        {
            if (_health == null || _faceWounds <= 0) return 0f;
            return _effectiveFaceWounds * FaceDecayStrength;
        }

        private void OnEffectRemoved(IHealthEffect effect)
        {
            if (_addingMarker || effect == null) return;
            if (effect is ILightBleeding || effect is IHeavyBleeding)
                ClearTreatableBleeds(effect);
        }

        private void OnEffectResidual(IHealthEffect effect)
        {
            if (_addingMarker || effect == null) return;
            if (effect is ILightBleeding || effect is IHeavyBleeding)
                ClearTreatableBleeds(effect);
        }

        private void ClearTreatableBleeds(IHealthEffect removedEffect)
        {
            if (_health == null || _addingMarker || removedEffect == null) return;

            EBodyPart bodyPart = removedEffect.BodyPart;

            if (bodyPart == EBodyPart.Chest && removedEffect is IHeavyBleeding && _heartWound)
                return;

            bool hadWound;
            switch (bodyPart)
            {
                case EBodyPart.Chest:
                    hadWound = _chestStacks > 0;
                    _chestStacks = 0;
                    _effectiveChestStacks = 0f;
                    _lastChestWoundTime = float.MinValue;
                    _lastChestInterval = 0f;
                    _lastChestSeverity = 0f;
                    break;
                case EBodyPart.Head:
                    hadWound = _faceWounds > 0;
                    _faceWounds = 0;
                    _effectiveFaceWounds = 0f;
                    _lastFaceWoundTime = float.MinValue;
                    break;
                default:
                    hadWound = _bodyWounds.Remove(bodyPart);
                    break;
            }

            if (!hadWound) return;

            for (int i = _bloodSources.Count - 1; i >= 0; i--)
                if (!_bloodSources[i].Heart && _bloodSources[i].BodyPart == bodyPart)
                    _bloodSources.RemoveAt(i);
            _bloodVisualHitCounts.Remove((int)bodyPart << 1);

            if (OrganSystem.DebugLogging.Value)
                Plugin.Log.LogInfo($"[Trauma] Healed all treatable bleed stacks on {bodyPart}");
        }

        private void ClearLinkedTreatableBleeds()
        {
            if (_health == null || _addingMarker) return;
            _addingMarker = true;
            try
            {
                _chestStacks = 0;
                _effectiveChestStacks = 0f;
                _lastChestWoundTime = float.MinValue;
                _lastChestInterval = 0f;
                _lastChestSeverity = 0f;
                _faceWounds = 0;
                _effectiveFaceWounds = 0f;
                _lastFaceWoundTime = float.MinValue;
                _bodyWounds.Clear();
                for (int i = _bloodSources.Count - 1; i >= 0; i--)
                    if (!_bloodSources[i].Heart) _bloodSources.RemoveAt(i);
                _bloodVisualHitCounts.Clear();
                if (_heartWound)
                    _bloodVisualHitCounts[((int)EBodyPart.Chest << 1) | 1] =
                        Mathf.Max(1, _heartWounds);

                List<IHealthEffect> effects = new List<IHealthEffect>(_health.GetAllActiveEffects());
                for (int i = 0; i < effects.Count; i++)
                    if ((effects[i] is ILightBleeding || effects[i] is IHeavyBleeding) &&
                        effects[i] is ActiveHealthController.Effect activeEffect)
                        activeEffect.ForceRemove();
            }
            finally { _addingMarker = false; }

            if (_heartWound) EnsureMarkers();
            if (OrganSystem.DebugLogging.Value)
                Plugin.Log.LogInfo(_heartWound
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
        }

        private void OnDestroy()
        {
            if (_player != null && _player.Physical != null &&
                !Mathf.Approximately(_appliedRestorePenalty, 0f))
                _player.Physical.RestoreRateBuff -= _appliedRestorePenalty;
            _appliedRestorePenalty = 0f;
            _bloodSources.Clear();
            _bloodParticles.Clear();
            _bloodVisualHitCounts.Clear();
            Unsubscribe();
        }
    }
}
