using System.Collections;
using System.Collections.Generic;
using System;
using EFT.Interactive;
using UnityEngine;

namespace TraumaCore.Features.WoundInspection
{
    internal static class CorpseRagdollSettlement
    {
        private const float HandoffDelaySeconds = 1f;
        private const float MaximumSettlementSeconds = 8f;
        private const float SettlementSpeed = 0.08f;

        private sealed class PendingSettlement
        {
            internal MonoBehaviour Owner;
            internal Coroutine Coroutine;
        }

        private static readonly Dictionary<Corpse, PendingSettlement>
            PendingByCorpse = new();

        internal static void Cancel(Corpse corpse)
        {
            if (corpse == null ||
                !PendingByCorpse.TryGetValue(corpse, out PendingSettlement pending))
                return;
            if (pending.Owner != null && pending.Coroutine != null)
                pending.Owner.StopCoroutine(pending.Coroutine);
            PendingByCorpse.Remove(corpse);
            TraumaLog.Info("[CorpseRagdoll] Cancelled previous settling handoff");
        }

        internal static void Schedule(Corpse corpse, CorpseRagdoll ragdoll)
        {
            Cancel(corpse);
            if (corpse == null || ragdoll == null || ragdoll._owner == null)
                return;
            ragdoll._putToSleep = true;
            PendingSettlement pending = new PendingSettlement
            {
                Owner = ragdoll._owner
            };
            pending.Coroutine = ragdoll._owner.StartCoroutine(
                SettleAfterHandoff(corpse, ragdoll));
            PendingByCorpse.Add(corpse, pending);
        }

        private static IEnumerator SettleAfterHandoff(
            Corpse corpse, CorpseRagdoll ragdoll)
        {
            yield return new WaitForSeconds(HandoffDelaySeconds);
            float settlementDuration = 0f;
            while (settlementDuration < MaximumSettlementSeconds &&
                HasMovingBody(ragdoll))
            {
                settlementDuration += Time.deltaTime;
                yield return null;
            }

            PendingByCorpse.Remove(corpse);
            if (ragdoll != null && !ragdoll._isPhysicsDone)
                CompleteSettlement(ragdoll);
        }

        private static void CompleteSettlement(CorpseRagdoll ragdoll)
        {
            if (HasCompleteBodySet(ragdoll))
            {
                try
                {
                    ragdoll.ForceStopRigidBody();
                    return;
                }
                catch (NullReferenceException exception)
                {
                    TraumaLog.Warning(
                        "[CorpseRagdoll] EFT could not stop the complete " +
                        "ragdoll; settling its available bodies instead: " +
                        exception.Message);
                }
            }
            else
            {
                TraumaLog.Warning(
                    "[CorpseRagdoll] Ragdoll lost one or more rigidbodies; " +
                    "settling the remaining bodies without EFT's bulk stop");
            }

            StopAvailableBodies(ragdoll);
        }

        private static bool HasCompleteBodySet(CorpseRagdoll ragdoll)
        {
            if (ragdoll?._rigidbodySpawners == null ||
                ragdoll._rigidbodySpawners.Length == 0)
                return false;

            foreach (RigidbodySpawner spawner in ragdoll._rigidbodySpawners)
                if (spawner == null || spawner.Rigidbody == null)
                    return false;

            return true;
        }

        private static void StopAvailableBodies(CorpseRagdoll ragdoll)
        {
            if (ragdoll?._rigidbodySpawners != null)
                foreach (RigidbodySpawner spawner in ragdoll._rigidbodySpawners)
                {
                    Rigidbody body = spawner?.Rigidbody;
                    if (body == null)
                        continue;
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                    body.Sleep();
                }

            ragdoll._putToSleep = true;
            ragdoll._isPhysicsDone = true;
        }

        private static bool HasMovingBody(CorpseRagdoll ragdoll)
        {
            if (ragdoll?._rigidbodySpawners == null)
                return false;

            float maximumSpeedSquared = SettlementSpeed * SettlementSpeed;
            foreach (RigidbodySpawner spawner in ragdoll._rigidbodySpawners)
            {
                Rigidbody body = spawner?.Rigidbody;
                if (body != null && (body.velocity.sqrMagnitude > maximumSpeedSquared ||
                    body.angularVelocity.sqrMagnitude > maximumSpeedSquared))
                    return true;
            }
            return false;
        }
    }
}
