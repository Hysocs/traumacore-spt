using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using TraumaCore.Patches;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Systems.Effects;
using UnityEngine;
using UnityEngine.UI;

namespace TraumaCore
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.hysocs.traumacore";
        public const string Name = "TraumaCore";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log { get; private set; }

        private readonly List<LineCommand> _lines = new List<LineCommand>(2048);
        private readonly List<BloodQuadCommand> _bloodQuads = new List<BloodQuadCommand>(512);
        private readonly List<WorldBloodCommand> _worldBlood = new List<WorldBloodCommand>(512);
        private readonly List<TraumaController.DebugBloodParticle> _particleBlood =
            new List<TraumaController.DebugBloodParticle>(1024);
        private readonly List<CustomArmorPenetrationPatch.DebugWeakSpot> _armorWeakSpots =
            new List<CustomArmorPenetrationPatch.DebugWeakSpot>(32);
        private readonly List<Text> _debugLabels = new List<Text>(32);
        private readonly StringBuilder _debugText = new StringBuilder(256);
        private readonly Vector2[] _boxPoints = new Vector2[8];
        private static readonly int[] BoxEdgeStart =
            { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] BoxEdgeEnd =
            { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };
        private const int EllipsoidSegments = 36;
        private static readonly float[] CircleCos = BuildCircleTable(true);
        private static readonly float[] CircleSin = BuildCircleTable(false);
        private Font _debugFont;
        private Harmony _harmony;
        private GameWorld _world;
        private Player _localPlayer;
        private Camera _camera;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private OrganGraphic _graphic;
        private BloodGraphic _bloodGraphic;
        private Texture _nativeBloodTexture;
        private Texture2D _generatedBloodTexture;
        private int _nativeBloodColumns = 1, _nativeBloodRows = 1;
        private float _nextBloodTextureLookup;
        private bool _loggedBloodTexture;
        private bool _usingNativeParticleMaterial;
        private Color32 _nativeParticleColor = new Color32(255, 255, 255, 235);
        private GameObject _bloodWorldObject;
        private Mesh _bloodWorldMesh;
        private MeshRenderer _bloodWorldRenderer;
        private Material _bloodWorldMaterial;
        private GameObject _bloodParticleObject;
        private ParticleSystem _bloodParticleSystem;
        private ParticleSystemRenderer _bloodParticleRenderer;
        private Material _bloodParticleMaterial;
        private ParticleSystem.Particle[] _bloodParticleBuffer =
            new ParticleSystem.Particle[1024];
        private int _lastRenderFrame = -1;
        private float _nextWorldRefresh;
        private bool _shuttingDown;
        private static Player _lastHitTarget;

        internal static void SetLastHitTarget(Player player) { _lastHitTarget = player; }

        private void Awake()
        {
            Log = Logger;
            OrganSystem.Initialize(Config);
            BindEffectTestButtons();
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(BodyTraumaPatch).Assembly);
            Canvas.preWillRenderCanvases += RenderFrame;
            Logger.LogInfo(Name + " " + Version + " loaded");
        }

        private void Update()
        {
            UpdateBloodParticlesIndependent();
            if (Time.unscaledTime < _nextWorldRefresh)
                return;

            _nextWorldRefresh = Time.unscaledTime + 0.25f;
            RefreshWorld();
        }

        private void UpdateBloodParticlesIndependent()
        {
            if (!OrganSystem.BloodEffects.Value || _world == null || _localPlayer == null)
            {
                if (_bloodParticleSystem != null) _bloodParticleSystem.Clear(false);
                if (_bloodParticleRenderer != null) _bloodParticleRenderer.enabled = false;
                return;
            }
            EnsureBloodParticleRenderer();
            ResolveNativeBloodTexture();
            _particleBlood.Clear();
            float rangeSq = OrganSystem.DebugEspRange.Value * OrganSystem.DebugEspRange.Value;
            Vector3 localPosition = _localPlayer.Transform.position;
            IEnumerable<Player> players = _world.AllPlayersEverExisted;
            if (players != null)
                foreach (Player player in players)
                {
                    if (player == null || player == _localPlayer ||
                        (player.Transform.position - localPosition).sqrMagnitude > rangeSq)
                        continue;
                    AddBloodDebug(player.GetComponent<TraumaController>());
                }
            UpdateParticleBlood();
        }

        private void RefreshWorld()
        {
            BruisedHealthEffect.EnsureIconRegistered();
            HeartWoundHealthEffect.EnsureIconRegistered();
            SpinalFractureHealthEffect.EnsureIconRegistered();
            GameWorld next = Singleton<GameWorld>.Instance;
            if (next != _world)
                AttachWorld(next);

            if (_world == null)
                return;

            _localPlayer = _world.MainPlayer;
            RefreshCamera();
        }

        private void AttachWorld(GameWorld world)
        {
            DetachWorld();
            _world = world;
            _camera = null;
            if (_world != null)
                GameWorld.OnDispose += OnWorldDisposed;
        }

        private void DetachWorld()
        {
            if (_world != null)
                GameWorld.OnDispose -= OnWorldDisposed;
            _world = null;
            _localPlayer = null;
            _camera = null;
            _lastHitTarget = null;
            if (_generatedBloodTexture != null) Destroy(_generatedBloodTexture);
            _generatedBloodTexture = null;
            _nativeBloodTexture = null;
            _usingNativeParticleMaterial = false;
            _nativeParticleColor = new Color32(255, 255, 255, 235);
            _nextBloodTextureLookup = 0f;
            _loggedBloodTexture = false;
            if (_bloodParticleSystem != null) _bloodParticleSystem.Clear(false);
            if (_bloodParticleRenderer != null) _bloodParticleRenderer.enabled = false;
            OrganSystem.ClearLimbBoneCache();
            ClearOverlay();
        }

        private void OnWorldDisposed() { DetachWorld(); }

        private void RefreshCamera()
        {
            if (_camera != null && _camera.enabled &&
                _camera.gameObject.activeInHierarchy && _camera.targetTexture == null)
                return;

            Camera best = null;
            float bestScore = float.MinValue;
            foreach (Camera candidate in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (candidate == null || !candidate.enabled ||
                    !candidate.gameObject.activeInHierarchy || candidate.orthographic ||
                    candidate.targetTexture != null)
                    continue;

                float score = candidate.depth;
                if (candidate.CompareTag("MainCamera")) score += 500f;
                if (candidate.name.IndexOf("FPS", StringComparison.OrdinalIgnoreCase) >= 0) score += 1000f;
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            _camera = best != null ? best : Camera.main;
        }

        private void RenderFrame()
        {
            if (_lastRenderFrame == Time.frameCount)
                return;
            _lastRenderFrame = Time.frameCount;

            bool debugEsp = OrganSystem.DebugEsp.Value;
            if (_world == null || _localPlayer == null || !debugEsp)
            {
                ClearOverlay();
                return;
            }

            if (!EnsureOverlay()) return;
            RefreshCamera();
            if (_camera == null) { ClearOverlay(); return; }

            _canvas.targetDisplay = _camera.targetDisplay;
            _lines.Clear();
            _bloodQuads.Clear();
            _worldBlood.Clear();
            int labelCount = 0;
            Vector3 localPosition = _localPlayer.Transform.position;
            float rangeSq = OrganSystem.DebugEspRange.Value *
                OrganSystem.DebugEspRange.Value;
            Player nearest = null;
            float nearestDistanceSq = float.MaxValue;
            IEnumerable<Player> players = _world.AllPlayersEverExisted;
            if (players != null)
            {
                foreach (Player player in players)
                {
                    if (player == null || player == _localPlayer || player.HealthController == null)
                        continue;
                    float distanceSq = (player.Transform.position - localPosition).sqrMagnitude;
                    if (distanceSq > rangeSq)
                        continue;
                    TraumaController trauma = player.GetComponent<TraumaController>();
                    if (!player.HealthController.IsAlive) continue;
                    if (distanceSq < nearestDistanceSq)
                    { nearest = player; nearestDistanceSq = distanceSq; }

                    if (!debugEsp || OrganSystem.GetChestAnchor(player) == null) continue;
                    Vector2 labelPosition;
                    TargetRules rules = OrganSystem.GetTargetRules(player);
                    if (rules.BodyTraumaEnabled)
                    {
                        if (rules.HeartEnabled)
                            AddOrganShape(player, OrganSystem.Heart, out labelPosition);
                        if (rules.BrainEnabled)
                        {
                            AddOrganShape(player, OrganSystem.Brain, out labelPosition);
                            AddOrganShape(player, OrganSystem.LowerBrain, out labelPosition);
                        }
                        if (rules.CervicalSpineEnabled) AddUpperSpine(player);
                        if (rules.ThoracicSpineEnabled) AddThoracicSpine(player);
                        AddLimbBones(player);
                        AddImpactDebug(trauma);
                    }
                    if (rules.ArmorPenetrationEnabled) AddArmorWeakSpotDebug(player);
                }
            }

            Player displayed = debugEsp && IsValidDebugTarget(_lastHitTarget, rangeSq, localPosition)
                ? _lastHitTarget : (debugEsp ? nearest : null);
            if (displayed != null && UpdatePlayerDebugPanel(0, displayed,
                displayed.GetComponent<TraumaController>())) labelCount = 1;

            HideLabelsFrom(labelCount);

            if (_lines.Count == 0 && _bloodQuads.Count == 0 && labelCount == 0) { ClearOverlay(); return; }
            _graphic.SetGeometry(_lines);
            _canvas.enabled = true;
        }

        private static bool IsValidDebugTarget(Player player, float rangeSq, Vector3 localPosition)
        {
            return player != null && player.HealthController != null &&
                (player.Transform.position - localPosition).sqrMagnitude <= rangeSq;
        }

        private bool AddOrganShape(Player player, OrganDefinition organ, out Vector2 labelPosition)
        {
            if (organ.Shape == OrganShape.Ellipsoid)
                return AddOrganEllipsoid(player, organ, out labelPosition);
            labelPosition = default(Vector2);
            Vector3 center = organ.WorldCenter(player);
            Transform anchor = organ.GetAnchor(player);
            if (anchor == null) return false;
            Quaternion rotation = organ.WorldRotation(player);
            Vector3 axisRight = rotation * Vector3.right;
            Vector3 axisUp = rotation * Vector3.up;
            Vector3 axisForward = rotation * Vector3.forward;

            float top = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 corner = center + axisRight * organ.HalfExtents.x * sign.x +
                    axisUp * organ.HalfExtents.y * sign.y +
                    axisForward * organ.HalfExtents.z * sign.z;
                Vector3 projected = _camera.WorldToScreenPoint(corner);
                if (projected.z <= 0f || !TryScreenPointToCanvas(projected, out _boxPoints[i])) return false;
                top = Mathf.Max(top, _boxPoints[i].y);
            }

            for (int i = 0; i < BoxEdgeStart.Length; i++)
                _lines.Add(new LineCommand(_boxPoints[BoxEdgeStart[i]],
                    _boxPoints[BoxEdgeEnd[i]], organ.Color, 2f));
            labelPosition = new Vector2((_boxPoints[2].x + _boxPoints[3].x +
                _boxPoints[6].x + _boxPoints[7].x) * 0.25f, top);
            return true;
        }

        private bool AddOrganEllipsoid(Player player, OrganDefinition organ,
            out Vector2 labelPosition)
        {
            labelPosition = default(Vector2);
            Transform anchor = organ.GetAnchor(player);
            if (anchor == null) return false;
            Vector3 center = organ.WorldCenter(player);
            Vector3 e = organ.HalfExtents;
            Quaternion rotation = organ.WorldRotation(player);
            float top = float.MinValue;
            float sumX = 0f;
            int projectedCount = 0;

            for (int ring = 0; ring < 3; ring++)
            {
                Vector2 first = default(Vector2), previous = default(Vector2);
                for (int i = 0; i <= EllipsoidSegments; i++)
                {
                    float c = CircleCos[i], s = CircleSin[i];
                    Vector3 local = ring == 0 ? new Vector3(e.x * c, e.y * s, 0f) :
                        ring == 1 ? new Vector3(e.x * c, 0f, e.z * s) :
                        new Vector3(0f, e.y * c, e.z * s);
                    Vector3 world = center + rotation * local;
                    Vector3 screen = _camera.WorldToScreenPoint(world);
                    Vector2 point;
                    if (screen.z <= 0f || !TryScreenPointToCanvas(screen, out point)) return false;
                    if (i == 0) first = point;
                    else _lines.Add(new LineCommand(previous, point, organ.Color, 2f));
                    previous = point;
                    if (ring == 0 && i < EllipsoidSegments)
                    {
                        top = Mathf.Max(top, point.y); sumX += point.x; projectedCount++;
                    }
                }
            }
            labelPosition = new Vector2(projectedCount > 0 ? sumX / projectedCount : 0f, top);
            return true;
        }

        private void AddImpactDebug(TraumaController trauma)
        {
            if (trauma == null) return;
            IList<TraumaController.DebugImpact> impacts = trauma.DebugImpacts;
            for (int i = 0; i < impacts.Count; i++)
            {
                TraumaController.DebugImpact impact = impacts[i];
                Color cervical = new Color(0.05f, 1f, 0.75f, 1f);
                Color thoracic = new Color(1f, 0.78f, 0.05f, 1f);
                Color color = impact.ArmorStopped ? Color.gray :
                    impact.CervicalSpine ? cervical :
                    impact.ThoracicSpine ? thoracic :
                    impact.Brain ? Color.magenta :
                    impact.Heart ? Color.green : new Color(1f, 0.35f, 0.08f, 1f);
                AddWorldLine(impact.HitPoint - impact.Direction * 0.45f,
                    impact.HitPoint + impact.Direction * 0.55f, color, 2.5f);
                AddWorldMarker(impact.HitPoint, Color.white, 0.018f);
                if (!impact.ArmorStopped && (impact.Heart || impact.Brain ||
                    impact.CervicalSpine || impact.ThoracicSpine))
                {
                    AddWorldLine(impact.HitPoint, impact.Intersection, color, 6f);
                    AddWorldSphere(impact.Intersection, 0.014f, color);
                    AddWorldMarker(impact.Intersection, Color.white, 0.032f);
                }
                if (!impact.ArmorStopped && impact.Bone)
                {
                    Color limbBone = new Color(0.1f, 0.55f, 1f, 1f);
                    AddWorldLine(impact.HitPoint, impact.BoneIntersection,
                        limbBone, 5f);
                    AddWorldSphere(impact.BoneIntersection, 0.012f, limbBone);
                }
            }
        }

        private void AddArmorWeakSpotDebug(Player player)
        {
            _armorWeakSpots.Clear();
            CustomArmorPenetrationPatch.CopyDebugWeakSpots(player, _armorWeakSpots);
            for (int i = 0; i < _armorWeakSpots.Count; i++)
            {
                CustomArmorPenetrationPatch.DebugWeakSpot spot = _armorWeakSpots[i];
                float strength = Mathf.InverseLerp(2f, 5f, spot.Multiplier);
                Color color = Color.Lerp(new Color(1f, 0.85f, 0.1f, 0.95f),
                    new Color(1f, 0.05f, 0.02f, 0.98f), strength);
                AddWorldSphere(spot.Position,
                    CustomArmorPenetrationPatch.WeakSpotRadius, color);
                AddWorldMarker(spot.Position, color, 0.012f);
            }
        }

        private void AddWorldSphere(Vector3 center, float radius, Color color)
        {
            for (int ring = 0; ring < 3; ring++)
            {
                Vector3 previous = default(Vector3);
                for (int i = 0; i <= EllipsoidSegments; i++)
                {
                    float c = CircleCos[i], s = CircleSin[i];
                    Vector3 offset = ring == 0 ? new Vector3(radius * c, radius * s, 0f) :
                        ring == 1 ? new Vector3(radius * c, 0f, radius * s) :
                        new Vector3(0f, radius * c, radius * s);
                    Vector3 point = center + offset;
                    if (i > 0) AddWorldLine(previous, point, color, 2f);
                    previous = point;
                }
            }
        }

        private void AddBloodDebug(TraumaController trauma)
        {
            if (trauma == null) return;
            IList<TraumaController.DebugBloodParticle> particles = trauma.DebugBloodParticles;
            Color blood = new Color(0.9f, 0.015f, 0.025f, 0.98f);
            for (int i = 0; i < particles.Count; i++)
            {
                TraumaController.DebugBloodParticle particle = particles[i];
                _particleBlood.Add(particle);
            }
        }

        private void AddWorldBloodSegment(Vector3 start, Vector3 end,
            float worldRadius, Color color)
        {
            if (_nativeBloodTexture != null)
            {
                _worldBlood.Add(new WorldBloodCommand(start, end,
                    worldRadius * 2.2f));
                return;
            }
            Vector3 screenStart = _camera.WorldToScreenPoint(start);
            Vector3 screenEnd = _camera.WorldToScreenPoint(end);
            Vector3 midpoint = (start + end) * 0.5f;
            Vector3 screenMid = _camera.WorldToScreenPoint(midpoint);
            Vector3 screenRadius = _camera.WorldToScreenPoint(
                midpoint + _camera.transform.right * worldRadius);
            Vector2 localStart, localEnd, localMid, localRadius;
            if (screenStart.z <= 0f || screenEnd.z <= 0f || screenMid.z <= 0f ||
                screenRadius.z <= 0f || !TryScreenPointToCanvas(screenStart, out localStart) ||
                !TryScreenPointToCanvas(screenEnd, out localEnd) ||
                !TryScreenPointToCanvas(screenMid, out localMid) ||
                !TryScreenPointToCanvas(screenRadius, out localRadius)) return;
            float thickness = Mathf.Clamp(Vector2.Distance(localMid, localRadius) * 2f,
                1.25f, 18f);
            Vector2 delta = localEnd - localStart;
            if (delta.sqrMagnitude < 0.01f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * thickness * 0.5f;
            _lines.Add(new LineCommand(localStart, localEnd,
                new Color(0.55f, 0.005f, 0.01f, 0.92f),
                Mathf.Max(1f, thickness * 0.32f)));
            if (_nativeBloodTexture != null)
                _bloodQuads.Add(new BloodQuadCommand(localStart - normal,
                    localStart + normal, localEnd + normal, localEnd - normal,
                    _bloodQuads.Count));
        }

        private void UpdateBloodGraphic()
        {
            _bloodGraphic.Clear();
            UpdateParticleBlood();
        }

        private void UpdateParticleBlood()
        {
            if (_bloodParticleSystem == null || _bloodParticleRenderer == null) return;
            if (_nativeBloodTexture == null || _particleBlood.Count == 0)
            {
                _bloodParticleSystem.Clear(false);
                _bloodParticleRenderer.enabled = false;
                return;
            }
            if (!_usingNativeParticleMaterial &&
                _bloodParticleMaterial.mainTexture != _nativeBloodTexture)
                _bloodParticleMaterial.mainTexture = _nativeBloodTexture;
            if (_bloodParticleBuffer.Length < _particleBlood.Count)
                _bloodParticleBuffer = new ParticleSystem.Particle[
                    Mathf.NextPowerOfTwo(_particleBlood.Count)];

            float now = Time.unscaledTime;
            int count = 0;
            for (int i = 0; i < _particleBlood.Count; i++)
            {
                TraumaController.DebugBloodParticle source = _particleBlood[i];
                float remaining = source.Expires - now;
                if (remaining <= 0f) continue;
                _bloodParticleBuffer[count++] = new ParticleSystem.Particle
                {
                    position = source.Position,
                    velocity = source.Velocity,
                    startLifetime = 1.5f,
                    remainingLifetime = remaining,
                    startSize = source.Size * 4.4f,
                    startColor = _nativeParticleColor,
                    randomSeed = (uint)i * 2654435761u + 1u
                };
            }
            _bloodParticleSystem.SetParticles(_bloodParticleBuffer, count);
            _bloodParticleRenderer.enabled = count > 0;
        }

        private void UpdateWorldBloodMesh()
        {
            if (_bloodWorldMesh == null || _bloodWorldRenderer == null) return;
            if (_nativeBloodTexture == null || _worldBlood.Count == 0)
            {
                _bloodWorldMesh.Clear();
                _bloodWorldRenderer.enabled = false;
                return;
            }
            if (_bloodWorldMaterial.mainTexture != _nativeBloodTexture)
                _bloodWorldMaterial.mainTexture = _nativeBloodTexture;

            int count = _worldBlood.Count;
            Vector3[] vertices = new Vector3[count * 4];
            Vector2[] uv = new Vector2[count * 4];
            Color32[] colors = new Color32[count * 4];
            int[] triangles = new int[count * 6];
            Color32 tint = new Color32(150, 18, 20, 225);
            for (int i = 0; i < count; i++)
            {
                WorldBloodCommand command = _worldBlood[i];
                Vector3 direction = command.End - command.Start;
                Vector3 side = Vector3.Cross(_camera.transform.forward, direction);
                if (side.sqrMagnitude < 0.00001f) side = _camera.transform.right;
                side = side.normalized * command.Radius;
                int v = i * 4, t = i * 6;
                vertices[v] = command.Start - side;
                vertices[v + 1] = command.Start + side;
                vertices[v + 2] = command.End + side;
                vertices[v + 3] = command.End - side;
                uv[v] = new Vector2(0f, 0f); uv[v + 1] = new Vector2(0f, 1f);
                uv[v + 2] = new Vector2(1f, 1f); uv[v + 3] = new Vector2(1f, 0f);
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = tint;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            _bloodWorldMesh.Clear();
            _bloodWorldMesh.vertices = vertices;
            _bloodWorldMesh.uv = uv;
            _bloodWorldMesh.colors32 = colors;
            _bloodWorldMesh.triangles = triangles;
            _bloodWorldMesh.RecalculateBounds();
            _bloodWorldRenderer.enabled = true;
        }

        private void ResolveNativeBloodTexture()
        {
            if (_nativeBloodTexture != null || Time.unscaledTime < _nextBloodTextureLookup)
                return;
            _nextBloodTextureLookup = Time.unscaledTime + 1f;

            if (TryUseNativeBloodParticleMaterial()) return;

            TextureDecalsPainter[] painters = Resources.FindObjectsOfTypeAll<TextureDecalsPainter>();
            for (int i = 0; i < painters.Length; i++)
            {
                if (painters[i] != null && painters[i]._bloodDecalTexture != null)
                {
                    _nativeBloodTexture = BuildBloodSpriteTexture(
                        painters[i]._bloodDecalTexture);
                    _nativeBloodColumns = _nativeBloodRows = 1;
                    break;
                }
            }

            if (_nativeBloodTexture == null && Singleton<Effects>.Instantiated)
            {
                Effects effects = Singleton<Effects>.Instance;
                if (effects != null && effects.DeferredDecals != null &&
                    effects.DeferredDecals._bleedingDecal != null)
                {
                    Material material = effects.DeferredDecals._bleedingDecal.DecalMaterial;
                    if (material != null)
                    {
                        string[] properties = material.GetTexturePropertyNames();
                        for (int i = 0; i < properties.Length; i++)
                        {
                            Texture candidate = material.GetTexture(properties[i]);
                            if (candidate is Texture2D)
                            {
                                _nativeBloodTexture = BuildBloodSpriteTexture(candidate);
                                if (_nativeBloodTexture != null) break;
                            }
                        }
                    }
                    _nativeBloodColumns = Mathf.Max(1,
                        effects.DeferredDecals._bleedingDecal.TileSheetColumns);
                    _nativeBloodRows = Mathf.Max(1,
                        effects.DeferredDecals._bleedingDecal.TileSheetRows);
                }
            }

            if (_nativeBloodTexture != null && !_loggedBloodTexture)
            {
                _loggedBloodTexture = true;
                Log.LogInfo("[BloodFX] Using native EFT blood texture: " +
                    _nativeBloodTexture.name);
            }
        }

        private bool TryUseNativeBloodParticleMaterial()
        {
            if (!Singleton<Effects>.Instantiated || _bloodParticleRenderer == null)
                return false;
            Effects effects = Singleton<Effects>.Instance;
            if (effects == null || effects.EffectsArray == null) return false;
            ParticleSystemRenderer fallbackRenderer = null;
            ParticleSystemAdapter fallbackAdapter = null;
            for (int i = 0; i < effects.EffectsArray.Length; i++)
            {
                Effects.Effect effect = effects.EffectsArray[i];
                if (effect == null || effect.MaterialTypes == null) continue;
                bool bodyEffect = false;
                for (int m = 0; m < effect.MaterialTypes.Length; m++)
                    if (effect.MaterialTypes[m] == EFT.Ballistics.MaterialType.Body)
                    { bodyEffect = true; break; }
                if (!bodyEffect || effect.Particles == null) continue;

                for (int p = 0; p < effect.Particles.Length; p++)
                {
                    ParticleSystemAdapter adapter =
                        effect.Particles[p].Particle as ParticleSystemAdapter;
                    if (adapter == null || adapter.ParticleSystemObject == null) continue;
                    ParticleSystemRenderer renderer =
                        adapter.ParticleSystemObject.GetComponent<ParticleSystemRenderer>();
                    if (renderer == null || renderer.sharedMaterial == null) continue;
                    if (fallbackRenderer == null)
                    { fallbackRenderer = renderer; fallbackAdapter = adapter; }
                    string combined = (renderer.name + " " + renderer.sharedMaterial.name +
                        " " + adapter.ParticleSystemObject.name).ToLowerInvariant();
                    if (combined.Contains("blood"))
                    { fallbackRenderer = renderer; fallbackAdapter = adapter; p = effect.Particles.Length; }
                }
                if (fallbackRenderer != null) break;
            }
            if (fallbackRenderer == null || fallbackRenderer.sharedMaterial == null)
                return false;

            if (_bloodParticleMaterial != null) Destroy(_bloodParticleMaterial);
            _bloodParticleMaterial = new Material(fallbackRenderer.sharedMaterial)
            {
                name = "DAO EFT Native Blood Particle Material"
            };
            _bloodParticleRenderer.sharedMaterial = _bloodParticleMaterial;
            _nativeBloodTexture = _bloodParticleMaterial.mainTexture != null
                ? _bloodParticleMaterial.mainTexture : Texture2D.whiteTexture;
            _nativeParticleColor = fallbackAdapter != null
                ? fallbackAdapter.Color : new Color32(255, 255, 255, 235);
            _nativeParticleColor.a = 255;
            ForceMaterialTintOpaque(_bloodParticleMaterial, "_Color");
            ForceMaterialTintOpaque(_bloodParticleMaterial, "_TintColor");
            ForceMaterialTintOpaque(_bloodParticleMaterial, "_BaseColor");
            _usingNativeParticleMaterial = true;
            _loggedBloodTexture = true;
            Log.LogInfo("[BloodFX] Cloned EFT body-impact particle material: " +
                fallbackRenderer.sharedMaterial.name + " shader=" +
                fallbackRenderer.sharedMaterial.shader.name);
            return true;
        }

        private static void ForceMaterialTintOpaque(Material material, string property)
        {
            if (material == null || !material.HasProperty(property)) return;
            Color color = material.GetColor(property);
            color.a = 1f;
            material.SetColor(property, color);
        }

        private Texture2D BuildBloodSpriteTexture(Texture source)
        {
            if (source == null) return null;
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                int width = Mathf.Clamp(source.width, 8, 1024);
                int height = Mathf.Clamp(source.height, 8, 1024);
                temporary = RenderTexture.GetTemporary(width, height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                Texture2D readable = new Texture2D(width, height, TextureFormat.RGBA32,
                    false, false);
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readable.Apply(false, false);
                Color32[] pixels = readable.GetPixels32();

                byte minAlpha = 255, maxAlpha = 0, maxIntensity = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    if (p.a < minAlpha) minAlpha = p.a;
                    if (p.a > maxAlpha) maxAlpha = p.a;
                    byte intensity = (byte)Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                    if (intensity > maxIntensity) maxIntensity = intensity;
                }
                bool hasUsefulAlpha = minAlpha < 48 && maxAlpha - minAlpha > 96;
                float intensityScale = maxIntensity > 0 ? 1f / maxIntensity : 0f;
                int minX = width, minY = height, maxX = -1, maxY = -1;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color32 p = pixels[index];
                    float mask = hasUsefulAlpha ? p.a / 255f :
                        Mathf.Max(p.r, Mathf.Max(p.g, p.b)) * intensityScale;
                    mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.72f, mask));
                    byte alpha = (byte)Mathf.RoundToInt(mask * 235f);
                    pixels[index] = new Color32(255, 255, 255, alpha);
                    if (alpha > 10)
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
                }
                Destroy(readable);
                if (maxX < minX || maxY < minY) return null;

                int croppedWidth = maxX - minX + 1, croppedHeight = maxY - minY + 1;
                Color32[] cropped = new Color32[croppedWidth * croppedHeight];
                for (int y = 0; y < croppedHeight; y++)
                    for (int x = 0; x < croppedWidth; x++)
                        cropped[y * croppedWidth + x] =
                            pixels[(y + minY) * width + x + minX];
                _generatedBloodTexture = new Texture2D(croppedWidth, croppedHeight,
                    TextureFormat.RGBA32, false, false);
                _generatedBloodTexture.name = "DAO Native EFT Blood Sprite";
                _generatedBloodTexture.wrapMode = TextureWrapMode.Clamp;
                _generatedBloodTexture.filterMode = FilterMode.Bilinear;
                _generatedBloodTexture.SetPixels32(cropped);
                _generatedBloodTexture.Apply(false, true);
                return _generatedBloodTexture;
            }
            catch (Exception e)
            {
                Log.LogWarning("[BloodFX] Could not convert EFT decal mask: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private void AddLimbBones(Player player)
        {
            AddLimbBones(player, EBodyPart.LeftArm, new Color(0.1f, 0.85f, 1f, 0.95f));
            AddLimbBones(player, EBodyPart.RightArm, new Color(0.1f, 0.85f, 1f, 0.95f));
            AddLimbBones(player, EBodyPart.LeftLeg, new Color(0.2f, 1f, 0.55f, 0.95f));
            AddLimbBones(player, EBodyPart.RightLeg, new Color(0.2f, 1f, 0.55f, 0.95f));
        }

        private void AddUpperSpine(Player player)
        {
            Vector3 brainBase, chestTop;
            if (!OrganSystem.TryGetUpperSpineSegment(player, out brainBase, out chestTop)) return;
            Color color = new Color(0.05f, 1f, 0.75f, 0.98f);
            AddWorldBoneSegment(brainBase, chestTop, OrganSystem.UpperSpineRadius, color);
            AddWorldMarker(brainBase, color, 0.012f);
            AddWorldMarker(chestTop, color, 0.012f);
        }

        private void AddThoracicSpine(Player player)
        {
            Vector3 chestTop, stomachTop;
            if (!OrganSystem.TryGetThoracicSpineSegment(player, out chestTop, out stomachTop)) return;
            Color color = new Color(1f, 0.78f, 0.05f, 0.98f);
            AddWorldBoneSegment(chestTop, stomachTop, OrganSystem.ThoracicSpineRadius, color);
            AddWorldMarker(chestTop, color, 0.012f);
            AddWorldMarker(stomachTop, color, 0.012f);
        }

        private void AddLimbBones(Player player, EBodyPart bodyPart, Color color)
        {
            Transform a, b, c, d;
            if (!OrganSystem.TryGetBoneSegments(player, bodyPart, out a, out b, out c, out d)) return;
            float radius = bodyPart == EBodyPart.LeftArm || bodyPart == EBodyPart.RightArm
                ? OrganSystem.ArmBoneRadius : OrganSystem.LegBoneRadius;
            if (a != null && b != null)
            {
                AddWorldBoneSegment(a.position, b.position, radius, color);
                AddWorldMarker(a.position, color, 0.012f); AddWorldMarker(b.position, color, 0.012f);
            }
            if (c != null && d != null)
            {
                AddWorldBoneSegment(c.position, d.position, radius, color);
                AddWorldMarker(c.position, color, 0.012f); AddWorldMarker(d.position, color, 0.012f);
            }
            if ((bodyPart == EBodyPart.LeftLeg || bodyPart == EBodyPart.RightLeg) &&
                b != null && c != null && b != c)
            {
                AddWorldBoneSegment(b.position, c.position, radius, color);
                AddWorldMarker(b.position, color, 0.012f);
                AddWorldMarker(c.position, color, 0.012f);
            }
        }

        private void AddWorldBoneSegment(Vector3 start, Vector3 end,
            float worldRadius, Color color)
        {
            Vector3 screenStart = _camera.WorldToScreenPoint(start);
            Vector3 screenEnd = _camera.WorldToScreenPoint(end);
            Vector3 midpoint = (start + end) * 0.5f;
            Vector3 screenMid = _camera.WorldToScreenPoint(midpoint);
            Vector3 screenRadius = _camera.WorldToScreenPoint(
                midpoint + _camera.transform.right * worldRadius);
            Vector2 localStart, localEnd, localMid, localRadius;
            if (screenStart.z <= 0f || screenEnd.z <= 0f || screenMid.z <= 0f ||
                screenRadius.z <= 0f || !TryScreenPointToCanvas(screenStart, out localStart) ||
                !TryScreenPointToCanvas(screenEnd, out localEnd) ||
                !TryScreenPointToCanvas(screenMid, out localMid) ||
                !TryScreenPointToCanvas(screenRadius, out localRadius)) return;
            float diameterPixels = Mathf.Clamp(Vector2.Distance(localMid, localRadius) * 2f, 0.75f, 80f);
            _lines.Add(new LineCommand(localStart, localEnd, color, diameterPixels));
        }

        private void AddWorldLine(Vector3 start, Vector3 end, Color color, float thickness)
        {
            Vector3 a = _camera.WorldToScreenPoint(start);
            Vector3 b = _camera.WorldToScreenPoint(end);
            Vector2 localA, localB;
            if (a.z <= 0f || b.z <= 0f || !TryScreenPointToCanvas(a, out localA) ||
                !TryScreenPointToCanvas(b, out localB)) return;
            _lines.Add(new LineCommand(localA, localB, color, thickness));
        }

        private void AddWorldMarker(Vector3 point, Color color, float worldSize)
        {
            AddWorldLine(point - _camera.transform.right * worldSize,
                point + _camera.transform.right * worldSize, color, 3f);
            AddWorldLine(point - _camera.transform.up * worldSize,
                point + _camera.transform.up * worldSize, color, 3f);
        }

        private bool UpdatePlayerDebugPanel(int index, Player player, TraumaController trauma)
        {
            Rect rect = _canvasRect.rect;
            Vector2 position = new Vector2(rect.xMax - 190f, rect.yMax - 175f);

            Text label = GetDebugLabel(index);
            float dps = trauma != null ? trauma.BleedDamagePerSecond : 0f;

            _debugText.Length = 0;
            _debugText.Append("DAMAGE DEBUG  ").Append(player.Profile != null ? player.Profile.Nickname : "TARGET")
                .Append(player == _lastHitTarget
                    ? (player.HealthController.IsAlive ? "  [LAST HIT]" : "  [LAST HIT - DEAD]")
                    : "  [NEAREST]");
            AppendHealth(EBodyPart.Head, "HEAD"); AppendHealth(EBodyPart.Chest, "CHEST");
            AppendHealth(EBodyPart.Stomach, "STOMACH");
            AppendHealth(EBodyPart.LeftArm, "L ARM"); AppendHealth(EBodyPart.RightArm, "R ARM");
            AppendHealth(EBodyPart.LeftLeg, "L LEG"); AppendHealth(EBodyPart.RightLeg, "R LEG");
            _debugText.Append("\nORGANS  BRAIN=INSTANT  HEART wounds=").Append(trauma != null ? trauma.HeartWounds : 0)
                .Append(" bleed=").Append(trauma != null ? trauma.EffectiveHeartWounds.ToString("0.0") : "0").Append(" HP/s");
            _debugText.Append("\nCHEST  wounds=").Append(trauma != null ? trauma.ChestStacks : 0)
                .Append(" bleed=").Append(trauma != null ? trauma.EffectiveChestStacks.ToString("0.0") : "0").Append(" HP/s")
                .Append(" decay=").Append(trauma != null ? trauma.ChestDecayStrength.ToString("0.00") : "0").Append('x')
                .Append(" last+=").Append(trauma != null ? trauma.LastChestSeverity.ToString("0.00") : "0").Append(" HP/s");
            if (trauma != null && trauma.LastChestInterval > 0f)
                _debugText.Append('@').Append((trauma.LastChestInterval * 1000f).ToString("0")).Append("ms");
            _debugText
                .Append("\nHEART  last+=").Append(trauma != null ? trauma.LastHeartSeverity.ToString("0.00") : "0").Append(" HP/s");
            if (trauma != null && trauma.LastHeartInterval > 0f)
                _debugText.Append('@').Append((trauma.LastHeartInterval * 1000f).ToString("0")).Append("ms");
            _debugText
                .Append("  FACE=").Append(trauma != null ? trauma.FaceWounds : 0)
                .Append("  STOMACH=").Append(trauma != null ? trauma.StomachWounds : 0)
                .Append("  LIMBS=").Append(trauma != null ? trauma.LimbWounds : 0)
                .Append("\nTOTAL BLEED  ").Append(dps.ToString("0.0")).Append(" HP/s");
            _armorWeakSpots.Clear();
            CustomArmorPenetrationPatch.CopyDebugWeakSpots(player, _armorWeakSpots);
            _debugText.Append("\nARMOR WEAK SPOTS  ").Append(_armorWeakSpots.Count)
                .Append("  radius=")
                .Append((CustomArmorPenetrationPatch.WeakSpotRadius * 100f).ToString("0.0"))
                .Append("cm  next=");
            if (_armorWeakSpots.Count == 0)
                _debugText.Append("none");
            else
                for (int i = 0; i < _armorWeakSpots.Count; i++)
                {
                    if (i > 0) _debugText.Append(',');
                    _debugText.Append(_armorWeakSpots[i].Multiplier.ToString("0")).Append('x');
                }
            if (trauma != null && trauma.BruiseStrength > 0f)
                _debugText.Append("\nBRUISED  ").Append((trauma.BruiseStrength * 100f).ToString("0"))
                    .Append("%  ").Append(trauma.BruiseTimeLeft.ToString("0.0")).Append('s');
            ValueStruct chest = player.ActiveHealthController.GetBodyPartHealth(EBodyPart.Chest);
            if (dps > 0f) _debugText.Append("  chest ETA=").Append((chest.Current / dps).ToString("0.0")).Append('s');

            _debugText.Append("\nEFFECTS: ");
            bool any = false;
            foreach (EFT.HealthSystem.IHealthEffect effect in player.ActiveHealthController.GetAllActiveEffects())
            {
                if (effect == null) continue;
                if (any) _debugText.Append(", ");
                _debugText.Append(effect.Type != null ? effect.Type.Name : effect.GetType().Name);
                if (effect.BodyPart != EBodyPart.Common) _debugText.Append('(').Append(effect.BodyPart).Append(')');
                any = true;
            }
            if (!any) _debugText.Append("none");

            label.text = _debugText.ToString();
            ((RectTransform)label.rectTransform.parent).anchoredPosition = position;
            label.gameObject.SetActive(true);
            label.transform.parent.gameObject.SetActive(true);
            return true;

            void AppendHealth(EBodyPart part, string name)
            {
                ValueStruct hp = player.ActiveHealthController.GetBodyPartHealth(part);
                float ratio = hp.Maximum > 0f ? hp.Current / hp.Maximum : 0f;
                int filled = Mathf.Clamp(Mathf.RoundToInt(ratio * 8f), 0, 8);
                _debugText.Append("\n").Append(name.PadRight(7)).Append(' ')
                    .Append(hp.Current.ToString("0.0")).Append('/').Append(hp.Maximum.ToString("0")).Append(" [");
                for (int i = 0; i < 8; i++) _debugText.Append(i < filled ? '|' : '.');
                _debugText.Append(']');
            }
        }

        private Text GetDebugLabel(int index)
        {
            if (index < _debugLabels.Count) return _debugLabels[index];
            if (_debugFont == null)
            {
                try { _debugFont = Font.CreateDynamicFontFromOSFont("Segoe UI", 16); } catch { }
                if (_debugFont == null) _debugFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            GameObject panel = new GameObject("Damage Debug Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(_canvasRect, false);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f); panelRect.sizeDelta = new Vector2(350f, 320f);
            Image background = panel.GetComponent<Image>();
            background.color = new Color(0.025f, 0.035f, 0.045f, 0.88f); background.raycastTarget = false;
            GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            obj.transform.SetParent(panelRect, false);
            Text label = obj.GetComponent<Text>();
            label.font = _debugFont; label.fontSize = 12; label.alignment = TextAnchor.UpperLeft;
            label.color = Color.white; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            Outline outline = obj.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f); outline.effectDistance = new Vector2(1f, -1f);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 8f); rect.offsetMax = new Vector2(-10f, -8f);
            _debugLabels.Add(label);
            return label;
        }

        private void HideLabelsFrom(int first)
        {
            for (int i = first; i < _debugLabels.Count; i++)
                if (_debugLabels[i] != null) _debugLabels[i].transform.parent.gameObject.SetActive(false);
        }

        private bool TryScreenPointToCanvas(Vector2 screen, out Vector2 local)
        {
            local = default(Vector2);
            if (_canvasRect == null || Screen.width <= 0 || Screen.height <= 0) return false;
            if (_camera.targetTexture == null && _camera.rect.width > 0f && _camera.rect.height > 0f)
            { screen.x /= _camera.rect.width; screen.y /= _camera.rect.height; }
            Rect rect = _canvasRect.rect;
            local = new Vector2(rect.xMin + screen.x * rect.width / Screen.width,
                rect.yMin + screen.y * rect.height / Screen.height);
            return true;
        }

        private bool EnsureOverlay()
        {
            if (_shuttingDown) return false;
            if (_canvas != null && _canvasRect != null && _graphic != null && _bloodGraphic != null) return true;
            GameObject root = new GameObject("DAO Organ ESP", typeof(RectTransform), typeof(Canvas));
            DontDestroyOnLoad(root);
            _canvas = root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;
            _canvas.enabled = false;
            _canvasRect = (RectTransform)root.transform;
            GameObject drawing = new GameObject("Organs", typeof(RectTransform), typeof(CanvasRenderer), typeof(OrganGraphic));
            drawing.transform.SetParent(_canvasRect, false);
            RectTransform rect = (RectTransform)drawing.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            _graphic = drawing.GetComponent<OrganGraphic>();
            _graphic.raycastTarget = false;
            GameObject bloodDrawing = new GameObject("Blood", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(BloodGraphic));
            bloodDrawing.transform.SetParent(_canvasRect, false);
            RectTransform bloodRect = (RectTransform)bloodDrawing.transform;
            bloodRect.anchorMin = Vector2.zero; bloodRect.anchorMax = Vector2.one;
            bloodRect.offsetMin = Vector2.zero; bloodRect.offsetMax = Vector2.zero;
            _bloodGraphic = bloodDrawing.GetComponent<BloodGraphic>();
            _bloodGraphic.raycastTarget = false;
            EnsureBloodParticleRenderer();
            return true;
        }

        private void EnsureBloodParticleRenderer()
        {
            if (_bloodParticleObject != null) return;
            _bloodParticleObject = new GameObject("DAO World Blood Particles");
            DontDestroyOnLoad(_bloodParticleObject);
            _bloodParticleSystem = _bloodParticleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _bloodParticleSystem.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4096;
            main.startLifetime = 1.5f;
            main.startSpeed = 0f;
            main.startSize = 0.04f;
            ParticleSystem.EmissionModule emission = _bloodParticleSystem.emission;
            emission.enabled = false;

            _bloodParticleRenderer = _bloodParticleObject.GetComponent<ParticleSystemRenderer>();
            _bloodParticleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            _bloodParticleRenderer.alignment = ParticleSystemRenderSpace.View;
            _bloodParticleRenderer.velocityScale = 0.085f;
            _bloodParticleRenderer.lengthScale = 0.55f;
            _bloodParticleRenderer.cameraVelocityScale = 0f;
            _bloodParticleRenderer.sortMode = ParticleSystemSortMode.Distance;
            _bloodParticleRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            _bloodParticleRenderer.receiveShadows = true;

            Shader shader = Shader.Find("Particles/Standard Surface");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            _bloodParticleMaterial = new Material(shader)
            {
                name = "DAO Native Blood Particle Material",
                renderQueue = 3000
            };
            if (_bloodParticleMaterial.HasProperty("_Color"))
                _bloodParticleMaterial.SetColor("_Color",
                    new Color(0.22f, 0.012f, 0.016f, 0.94f));
            if (_bloodParticleMaterial.HasProperty("_EmissionColor"))
                _bloodParticleMaterial.SetColor("_EmissionColor", Color.black);
            if (_bloodParticleMaterial.HasProperty("_EmissionEnabled"))
                _bloodParticleMaterial.SetFloat("_EmissionEnabled", 0f);
            if (_bloodParticleMaterial.HasProperty("_Mode"))
                _bloodParticleMaterial.SetFloat("_Mode", 2f);
            if (_bloodParticleMaterial.HasProperty("_SrcBlend"))
                _bloodParticleMaterial.SetFloat("_SrcBlend",
                    (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_bloodParticleMaterial.HasProperty("_DstBlend"))
                _bloodParticleMaterial.SetFloat("_DstBlend",
                    (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (_bloodParticleMaterial.HasProperty("_ZWrite"))
                _bloodParticleMaterial.SetFloat("_ZWrite", 0f);
            _bloodParticleMaterial.DisableKeyword("_EMISSION");
            _bloodParticleMaterial.DisableKeyword("_EMISSIONENABLED_ON");
            _bloodParticleMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _bloodParticleMaterial.EnableKeyword("_ALPHABLEND_ON");
            _bloodParticleRenderer.sharedMaterial = _bloodParticleMaterial;
            _bloodParticleRenderer.enabled = false;
            _bloodParticleSystem.Play(false);
        }

        private void EnsureWorldBloodRenderer()
        {
            if (_bloodWorldObject != null) return;
            _bloodWorldObject = new GameObject("DAO World Blood");
            DontDestroyOnLoad(_bloodWorldObject);
            MeshFilter filter = _bloodWorldObject.AddComponent<MeshFilter>();
            _bloodWorldRenderer = _bloodWorldObject.AddComponent<MeshRenderer>();
            _bloodWorldMesh = new Mesh { name = "DAO Dynamic Blood Mesh" };
            _bloodWorldMesh.MarkDynamic();
            filter.sharedMesh = _bloodWorldMesh;
            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _bloodWorldMaterial = new Material(shader)
            {
                name = "DAO Depth-Tested Blood Material",
                renderQueue = 3000
            };
            _bloodWorldRenderer.sharedMaterial = _bloodWorldMaterial;
            _bloodWorldRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bloodWorldRenderer.receiveShadows = false;
            _bloodWorldRenderer.enabled = false;
        }

        private static float[] BuildCircleTable(bool cosine)
        {
            float[] values = new float[EllipsoidSegments + 1];
            for (int i = 0; i <= EllipsoidSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / EllipsoidSegments;
                values[i] = cosine ? Mathf.Cos(angle) : Mathf.Sin(angle);
            }
            return values;
        }

        private void ClearOverlay()
        {
            if (_graphic != null) _graphic.Clear();
            if (_bloodGraphic != null) _bloodGraphic.Clear();
            if (_bloodWorldMesh != null) _bloodWorldMesh.Clear();
            if (_bloodWorldRenderer != null) _bloodWorldRenderer.enabled = false;
            HideLabelsFrom(0);
            if (_canvas != null) _canvas.enabled = false;
        }

        private void OnDestroy()
        {
            _shuttingDown = true;
            Canvas.preWillRenderCanvases -= RenderFrame;
            DetachWorld();
            if (_harmony != null) _harmony.UnpatchSelf();
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_bloodWorldObject != null) Destroy(_bloodWorldObject);
            if (_bloodWorldMaterial != null) Destroy(_bloodWorldMaterial);
            if (_bloodWorldMesh != null) Destroy(_bloodWorldMesh);
            if (_bloodParticleObject != null) Destroy(_bloodParticleObject);
            if (_bloodParticleMaterial != null) Destroy(_bloodParticleMaterial);
            Log = null;
        }

        private struct LineCommand
        {
            public readonly Vector2 Start, End; public readonly Color Color; public readonly float Thickness;
            public LineCommand(Vector2 start, Vector2 end, Color color, float thickness)
            { Start = start; End = end; Color = color; Thickness = thickness; }
        }

        private struct BloodQuadCommand
        {
            public readonly Vector2 A, B, C, D;
            public readonly int Tile;
            public BloodQuadCommand(Vector2 a, Vector2 b, Vector2 c, Vector2 d, int tile)
            { A = a; B = b; C = c; D = d; Tile = tile; }
        }

        private struct WorldBloodCommand
        {
            public readonly Vector3 Start, End;
            public readonly float Radius;
            public WorldBloodCommand(Vector3 start, Vector3 end, float radius)
            { Start = start; End = end; Radius = radius; }
        }

        private sealed class OrganGraphic : Graphic
        {
            private IList<LineCommand> _lines;
            public void SetGeometry(IList<LineCommand> lines) { _lines = lines; SetVerticesDirty(); }
            public void Clear() { _lines = null; SetVerticesDirty(); }
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear(); if (_lines == null) return;
                for (int i = 0; i < _lines.Count; i++)
                {
                    LineCommand line = _lines[i]; Vector2 delta = line.End - line.Start;
                    if (delta.sqrMagnitude < 0.01f) continue;
                    Vector2 normal = new Vector2(-delta.y, delta.x).normalized * line.Thickness * 0.5f;
                    int v = vh.currentVertCount;
                    vh.AddVert(line.Start - normal, line.Color, Vector2.zero); vh.AddVert(line.Start + normal, line.Color, Vector2.zero);
                    vh.AddVert(line.End + normal, line.Color, Vector2.zero); vh.AddVert(line.End - normal, line.Color, Vector2.zero);
                    vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v, v + 2, v + 3);
                }
            }
        }

        private sealed class BloodGraphic : Graphic
        {
            private IList<BloodQuadCommand> _quads;
            private Texture _texture;
            private int _columns = 1, _rows = 1;
            private Color _tint = Color.white;
            public override Texture mainTexture { get { return _texture != null ? _texture : Texture2D.whiteTexture; } }
            public void SetGeometry(IList<BloodQuadCommand> quads, Texture texture,
                int columns, int rows, Color tint)
            {
                _quads = quads; _texture = texture; _columns = Mathf.Max(1, columns);
                _rows = Mathf.Max(1, rows); _tint = tint;
                SetMaterialDirty(); SetVerticesDirty();
            }
            public void Clear() { _quads = null; SetVerticesDirty(); }
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear(); if (_quads == null) return;
                float tileWidth = 1f / _columns, tileHeight = 1f / _rows;
                int tileCount = _columns * _rows;
                for (int i = 0; i < _quads.Count; i++)
                {
                    BloodQuadCommand q = _quads[i];
                    int tile = q.Tile % tileCount;
                    float u0 = (tile % _columns) * tileWidth;
                    float v0 = ((tile / _columns) % _rows) * tileHeight;
                    float u1 = u0 + tileWidth, v1 = v0 + tileHeight;
                    int v = vh.currentVertCount;
                    vh.AddVert(q.A, _tint, new Vector2(u0, v0));
                    vh.AddVert(q.B, _tint, new Vector2(u0, v1));
                    vh.AddVert(q.C, _tint, new Vector2(u1, v1));
                    vh.AddVert(q.D, _tint, new Vector2(u1, v0));
                    vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v, v + 2, v + 3);
                }
            }
        }
    }
}
