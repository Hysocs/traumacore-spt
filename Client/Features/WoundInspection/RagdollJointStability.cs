using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace TraumaCore.Features.WoundInspection
{
    internal sealed class RagdollJointStability
    {
        private const float MaximumAnchorSeparation = 0.015f;
        private const float RepairAnchorSeparation = 0.025f;
        private const float MaximumElbowFoldAngle = 135f;
        private const int MinimumSolverIterations = 12;
        private const int MinimumSolverVelocityIterations = 4;

        private sealed class JointState
        {
            internal CharacterJoint Joint;
            internal bool IsProjectionEnabled;
            internal float ProjectionDistance;
            internal float ProjectionAngle;
            internal bool IsPreprocessingEnabled;
            internal float BreakForce;
            internal float BreakTorque;
            internal Rigidbody Body;
            internal bool IsElbow;
            internal int SolverIterations;
            internal int SolverVelocityIterations;
        }

        private readonly List<JointState> _jointStates = new();

        internal void Capture(IEnumerable<CharacterJointSpawner> spawners)
        {
            Restore();
            if (spawners == null)
                return;
            foreach (CharacterJointSpawner spawner in spawners)
            {
                CharacterJoint joint = spawner?.GetComponent<CharacterJoint>();
                if (joint == null)
                    continue;
                Rigidbody body = joint.GetComponent<Rigidbody>();
                _jointStates.Add(new JointState
                {
                    Joint = joint,
                    IsProjectionEnabled = joint.enableProjection,
                    ProjectionDistance = joint.projectionDistance,
                    ProjectionAngle = joint.projectionAngle,
                    IsPreprocessingEnabled = joint.enablePreprocessing,
                    BreakForce = joint.breakForce,
                    BreakTorque = joint.breakTorque,
                    Body = body,
                    IsElbow = IsForearmJoint(spawner, joint, body),
                    SolverIterations = body != null ? body.solverIterations : 0,
                    SolverVelocityIterations = body != null
                        ? body.solverVelocityIterations : 0
                });
                joint.breakForce = float.PositiveInfinity;
                joint.breakTorque = float.PositiveInfinity;
                if (body != null)
                {
                    body.solverIterations = Mathf.Max(body.solverIterations,
                        MinimumSolverIterations);
                    body.solverVelocityIterations = Mathf.Max(
                        body.solverVelocityIterations,
                        MinimumSolverVelocityIterations);
                }
            }
        }

        internal void RepairExcessiveSeparation()
        {
            for (int i = 0; i < _jointStates.Count; i++)
            {
                CharacterJoint joint = _jointStates[i].Joint;
                Rigidbody body = joint != null ? joint.GetComponent<Rigidbody>() : null;
                if (body == null)
                    continue;
                if (_jointStates[i].IsElbow)
                    ClampElbowFold(joint, body);
                Vector3 bodyAnchor = joint.transform.TransformPoint(joint.anchor);
                Vector3 connectedAnchor = joint.connectedBody != null
                    ? joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)
                    : joint.connectedAnchor;
                Vector3 separation = connectedAnchor - bodyAnchor;
                float distance = separation.magnitude;
                if (distance <= RepairAnchorSeparation)
                    continue;
                body.position += separation *
                    ((distance - MaximumAnchorSeparation) / distance);
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private static bool IsForearmJoint(CharacterJointSpawner spawner,
            CharacterJoint joint, Rigidbody body)
        {
            string names = $"{spawner?.name} {joint?.name} {body?.name}";
            return names.IndexOf("forearm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                names.IndexOf("lowerarm", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ClampElbowFold(CharacterJoint joint, Rigidbody forearm)
        {
            Rigidbody upperArm = joint.connectedBody;
            if (upperArm == null)
                return;

            Vector3 elbow = joint.transform.TransformPoint(joint.anchor);
            Vector3 upperDirection = elbow - upperArm.worldCenterOfMass;
            Vector3 forearmDirection = forearm.worldCenterOfMass - elbow;
            if (upperDirection.sqrMagnitude < 0.0001f ||
                forearmDirection.sqrMagnitude < 0.0001f)
                return;

            upperDirection.Normalize();
            forearmDirection.Normalize();
            if (Vector3.Angle(upperDirection, forearmDirection) <=
                MaximumElbowFoldAngle)
                return;

            Vector3 clampedDirection = Vector3.RotateTowards(upperDirection,
                forearmDirection, MaximumElbowFoldAngle * Mathf.Deg2Rad, 0f);
            Quaternion correction = Quaternion.FromToRotation(
                forearmDirection, clampedDirection);
            forearm.position = elbow + correction * (forearm.position - elbow);
            forearm.rotation = correction * forearm.rotation;
            forearm.velocity = Vector3.zero;
            forearm.angularVelocity = Vector3.zero;
        }

        internal void Restore()
        {
            for (int i = 0; i < _jointStates.Count; i++)
            {
                JointState state = _jointStates[i];
                if (state.Joint == null)
                    continue;
                state.Joint.enableProjection = state.IsProjectionEnabled;
                state.Joint.projectionDistance = state.ProjectionDistance;
                state.Joint.projectionAngle = state.ProjectionAngle;
                state.Joint.enablePreprocessing = state.IsPreprocessingEnabled;
                state.Joint.breakForce = state.BreakForce;
                state.Joint.breakTorque = state.BreakTorque;
                if (state.Body != null)
                {
                    state.Body.solverIterations = state.SolverIterations;
                    state.Body.solverVelocityIterations =
                        state.SolverVelocityIterations;
                }
            }
            _jointStates.Clear();
        }
    }
}
