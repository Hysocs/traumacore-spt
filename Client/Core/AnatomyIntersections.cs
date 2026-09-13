using UnityEngine;

namespace TraumaCore
{
    internal readonly struct AnatomyHit
    {
        internal readonly Vector3 Point;
        internal readonly float EntryDistance;
        internal readonly float ExitDistance;

        internal AnatomyHit(Vector3 origin, Vector3 direction,
            float entryDistance, float exitDistance)
        {
            Point = origin + direction * entryDistance;
            EntryDistance = entryDistance;
            ExitDistance = exitDistance;
        }
    }

    internal static class AnatomyIntersections
    {
        internal const float MaximumTraceDistance = 0.55f;
        private const float Epsilon = 0.000001f;

        internal static bool TryIntersectBox(BoneVolumePose pose,
            Vector3 origin, Vector3 direction, out AnatomyHit hit)
        {
            hit = default;
            if (!TryCreateLocalRay(pose, origin, direction,
                out Vector3 localOrigin, out Vector3 localDirection,
                out Vector3 normalizedDirection)) return false;
            Vector3 half = pose.Size * 0.5f;
            float entry = 0f;
            float exit = MaximumTraceDistance;
            for (int axis = 0; axis < 3; axis++)
            {
                if (!ClipAxis(localOrigin[axis], localDirection[axis],
                    half[axis], ref entry, ref exit)) return false;
            }
            hit = new AnatomyHit(origin, normalizedDirection, entry, exit);
            return true;
        }

        internal static bool TryIntersectEllipsoid(BoneVolumePose pose,
            Vector3 origin, Vector3 direction, bool upperHalfOnly,
            out AnatomyHit hit)
        {
            hit = default;
            if (!TryCreateLocalRay(pose, origin, direction,
                out Vector3 localOrigin, out Vector3 localDirection,
                out Vector3 normalizedDirection)) return false;
            Vector3 radius = pose.Size * 0.5f;
            if (radius.x <= Epsilon || radius.y <= Epsilon ||
                radius.z <= Epsilon) return false;
            Vector3 scaledOrigin = Divide(localOrigin, radius);
            Vector3 scaledDirection = Divide(localDirection, radius);
            float a = Vector3.Dot(scaledDirection, scaledDirection);
            float b = 2f * Vector3.Dot(scaledOrigin, scaledDirection);
            float c = Vector3.Dot(scaledOrigin, scaledOrigin) - 1f;
            if (!TrySolveQuadratic(a, b, c, out float near, out float far))
                return false;
            float entry = SelectEllipsoidEntry(scaledOrigin, scaledDirection,
                near, far, upperHalfOnly);
            if (entry < 0f || entry > MaximumTraceDistance) return false;
            hit = new AnatomyHit(origin, normalizedDirection, entry,
                Mathf.Max(entry, far));
            return true;
        }

        internal static bool IsInsideEllipsoid(BoneVolumePose pose,
            Vector3 point)
        {
            Vector3 radius = pose.Size * 0.5f;
            if (radius.x <= Epsilon || radius.y <= Epsilon || radius.z <= Epsilon)
                return false;
            Vector3 local = Quaternion.Inverse(pose.Rotation) *
                (point - pose.Center);
            Vector3 scaled = Divide(local, radius);
            return Vector3.Dot(scaled, scaled) < 1f - Epsilon;
        }

        internal static bool TryIntersectCylinder(BoneSegmentPose cylinder,
            Vector3 origin, Vector3 direction, out AnatomyHit hit)
        {
            hit = default;
            if (cylinder.Radius <= Epsilon || cylinder.EndRadius <= Epsilon ||
                !TryNormalize(direction, out Vector3 ray)) return false;
            Vector3 axis = cylinder.End - cylinder.Start;
            float length = axis.magnitude;
            if (length <= Epsilon) return false;
            axis /= length;
            Vector3 relativeOrigin = origin - cylinder.Start;
            float axialOrigin = Vector3.Dot(relativeOrigin, axis);
            float axialDirection = Vector3.Dot(ray, axis);
            float entry = 0f;
            float exit = float.PositiveInfinity;
            if (!ClipAxis(axialOrigin - length * 0.5f, axialDirection,
                length * 0.5f, ref entry, ref exit)) return false;

            Vector3 radialOrigin = relativeOrigin - axis * axialOrigin;
            Vector3 radialDirection = ray - axis * axialDirection;
            float taper = (cylinder.EndRadius - cylinder.Radius) / length;
            float originRadius = cylinder.Radius + taper * axialOrigin;
            float radiusDirection = taper * axialDirection;
            float a = radialDirection.sqrMagnitude - radiusDirection * radiusDirection;
            float b = 2f * (Vector3.Dot(radialOrigin, radialDirection) -
                originRadius * radiusDirection);
            float c = radialOrigin.sqrMagnitude - originRadius * originRadius;
            if (Mathf.Abs(a) <= Epsilon)
            {
                if (Mathf.Abs(b) <= Epsilon)
                {
                    if (c > 0f) return false;
                }
                else if (b > 0f) exit = Mathf.Min(exit, -c / b);
                else entry = Mathf.Max(entry, -c / b);
            }
            else
            {
                if (!TrySolveQuadratic(a, b, c, out float near, out float far))
                {
                    if (a > 0f) return false;
                }
                else
                {
                    if (near > far) (near, far) = (far, near);
                    if (a > 0f)
                    {
                        entry = Mathf.Max(entry, near);
                        exit = Mathf.Min(exit, far);
                    }
                    // A tapered cylinder can produce a downward quadratic;
                    // only the interval within its flat end caps is physical.
                    else if (entry <= near) exit = Mathf.Min(exit, near);
                    else entry = Mathf.Max(entry, far);
                }
            }
            if (entry > exit || exit < 0f || entry > MaximumTraceDistance)
                return false;
            hit = new AnatomyHit(origin, ray, entry, exit);
            return true;
        }

        internal static bool TryIntersectCapsule(BoneSegmentPose capsule,
            Vector3 origin, Vector3 direction, out AnatomyHit hit)
        {
            hit = default;
            if (!TryNormalize(direction, out Vector3 ray)) return false;
            Vector3 axis = capsule.End - capsule.Start;
            float axisLengthSquared = axis.sqrMagnitude;
            if (axisLengthSquared <= Epsilon)
                return TryIntersectSphere(capsule.Start, capsule.Radius,
                    origin, ray, out hit);
            if (DistanceSquaredToSegment(origin, capsule.Start, capsule.End) <=
                capsule.Radius * capsule.Radius)
            {
                hit = new AnatomyHit(origin, ray, 0f, 0f);
                return true;
            }

            Vector3 fromStart = origin - capsule.Start;
            float axisRay = Vector3.Dot(axis, ray);
            float axisOrigin = Vector3.Dot(axis, fromStart);
            float rayOrigin = Vector3.Dot(ray, fromStart);
            float originSquared = Vector3.Dot(fromStart, fromStart);
            float a = axisLengthSquared - axisRay * axisRay;
            float b = axisLengthSquared * rayOrigin - axisOrigin * axisRay;
            float c = axisLengthSquared * originSquared -
                axisOrigin * axisOrigin - capsule.Radius * capsule.Radius *
                axisLengthSquared;
            float bestDistance = float.MaxValue;
            float discriminant = b * b - a * c;
            if (Mathf.Abs(a) > Epsilon && discriminant >= 0f)
            {
                float distance = (-b - Mathf.Sqrt(discriminant)) / a;
                float axisPosition = axisOrigin + distance * axisRay;
                if (distance >= 0f && axisPosition > 0f &&
                    axisPosition < axisLengthSquared)
                    bestDistance = distance;
            }
            CaptureSphereEntry(capsule.Start, capsule.Radius, origin, ray,
                ref bestDistance);
            CaptureSphereEntry(capsule.End, capsule.Radius, origin, ray,
                ref bestDistance);
            if (bestDistance > MaximumTraceDistance) return false;
            hit = new AnatomyHit(origin, ray, bestDistance, bestDistance);
            return true;
        }

        internal static bool TryIntersectRibcage(RibcageShape ribcage,
            Vector3 origin, Vector3 direction, out AnatomyHit hit)
        {
            hit = default;
            if (!TryNormalize(direction, out Vector3 ray) ||
                ribcage.Rings == null || ribcage.Rings.Length < 2) return false;
            if (IsInsideRibcage(ribcage, origin))
            {
                hit = new AnatomyHit(origin, ray, 0f, 0f);
                return true;
            }
            float bestDistance = float.MaxValue;
            const int segments = 32;
            for (int ring = 0; ring < ribcage.Rings.Length - 1; ring++)
            {
                RibcageRing lower = ribcage.Rings[ring];
                RibcageRing upper = ribcage.Rings[ring + 1];
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    float nextAngle = (segment + 1) * Mathf.PI * 2f / segments;
                    Vector3 lowerA = lower.Point(angle);
                    Vector3 lowerB = lower.Point(nextAngle);
                    Vector3 upperA = upper.Point(angle);
                    Vector3 upperB = upper.Point(nextAngle);
                    CaptureTriangleEntry(origin, ray, lowerA, upperA, lowerB,
                        ref bestDistance);
                    CaptureTriangleEntry(origin, ray, lowerB, upperA, upperB,
                        ref bestDistance);
                }
            }
            CaptureRingCap(ribcage.Rings[0], origin, ray, ref bestDistance);
            CaptureRingCap(ribcage.Rings[ribcage.Rings.Length - 1], origin,
                ray, ref bestDistance);
            if (bestDistance > MaximumTraceDistance) return false;
            hit = new AnatomyHit(origin, ray, bestDistance, bestDistance);
            return true;
        }

        private static bool IsInsideRibcage(RibcageShape ribcage,
            Vector3 point)
        {
            Vector3 up = ribcage.Rings[0].Rotation * Vector3.up;
            for (int index = 0; index < ribcage.Rings.Length - 1; index++)
            {
                RibcageRing lower = ribcage.Rings[index];
                RibcageRing upper = ribcage.Rings[index + 1];
                float height = Vector3.Dot(upper.Center - lower.Center, up);
                if (Mathf.Abs(height) <= Epsilon) continue;
                float position = Vector3.Dot(point - lower.Center, up) / height;
                if (position < 0f || position > 1f) continue;
                Vector3 center = Vector3.Lerp(lower.Center, upper.Center,
                    position);
                float radiusX = Mathf.Lerp(lower.RadiusX, upper.RadiusX,
                    position);
                float radiusZ = Mathf.Lerp(lower.RadiusZ, upper.RadiusZ,
                    position);
                Vector3 local = Quaternion.Inverse(lower.Rotation) *
                    (point - center);
                if (local.x * local.x / (radiusX * radiusX) +
                    local.z * local.z / (radiusZ * radiusZ) <= 1f + Epsilon)
                    return true;
            }
            return false;
        }

        private static void CaptureRingCap(RibcageRing ring, Vector3 origin,
            Vector3 direction, ref float bestDistance)
        {
            const int segments = 32;
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / segments;
                float nextAngle = (segment + 1) * Mathf.PI * 2f / segments;
                CaptureTriangleEntry(origin, direction, ring.Center,
                    ring.Point(angle), ring.Point(nextAngle), ref bestDistance);
            }
        }

        private static void CaptureTriangleEntry(Vector3 origin,
            Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
            ref float bestDistance)
        {
            Vector3 edgeA = b - a;
            Vector3 edgeB = c - a;
            Vector3 cross = Vector3.Cross(direction, edgeB);
            float determinant = Vector3.Dot(edgeA, cross);
            if (Mathf.Abs(determinant) <= Epsilon) return;
            float inverse = 1f / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, cross) * inverse;
            if (u < 0f || u > 1f) return;
            Vector3 perpendicular = Vector3.Cross(offset, edgeA);
            float v = Vector3.Dot(direction, perpendicular) * inverse;
            if (v < 0f || u + v > 1f) return;
            float distance = Vector3.Dot(edgeB, perpendicular) * inverse;
            if (distance >= 0f && distance < bestDistance)
                bestDistance = distance;
        }

        private static bool TryCreateLocalRay(BoneVolumePose pose,
            Vector3 origin, Vector3 direction, out Vector3 localOrigin,
            out Vector3 localDirection, out Vector3 normalizedDirection)
        {
            localOrigin = localDirection = normalizedDirection = Vector3.zero;
            if (!TryNormalize(direction, out normalizedDirection)) return false;
            Quaternion inverse = Quaternion.Inverse(pose.Rotation);
            localOrigin = inverse * (origin - pose.Center);
            localDirection = inverse * normalizedDirection;
            return true;
        }

        private static bool TryNormalize(Vector3 direction,
            out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (direction.sqrMagnitude <= Epsilon) return false;
            normalized = direction.normalized;
            return true;
        }

        private static bool ClipAxis(float origin, float direction,
            float extent, ref float entry, ref float exit)
        {
            if (extent <= Epsilon) return false;
            if (Mathf.Abs(direction) <= Epsilon)
                return origin >= -extent && origin <= extent;
            float near = (-extent - origin) / direction;
            float far = (extent - origin) / direction;
            if (near > far) (near, far) = (far, near);
            entry = Mathf.Max(entry, near);
            exit = Mathf.Min(exit, far);
            return entry <= exit && exit >= 0f;
        }

        private static Vector3 Divide(Vector3 value, Vector3 divisor) =>
            new Vector3(value.x / divisor.x, value.y / divisor.y,
                value.z / divisor.z);

        private static bool TrySolveQuadratic(float a, float b, float c,
            out float near, out float far)
        {
            near = far = 0f;
            if (Mathf.Abs(a) <= Epsilon) return false;
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return false;
            float root = Mathf.Sqrt(discriminant);
            near = (-b - root) / (2f * a);
            far = (-b + root) / (2f * a);
            return true;
        }

        private static float SelectEllipsoidEntry(Vector3 origin,
            Vector3 direction, float near, float far, bool upperHalfOnly)
        {
            bool startsInside = origin.sqrMagnitude <= 1f + Epsilon;
            if (startsInside && (!upperHalfOnly || origin.y >= 0f)) return 0f;
            float entry = float.MaxValue;
            CaptureUpperEllipsoidSurface(origin, direction, near,
                upperHalfOnly, ref entry);
            CaptureUpperEllipsoidSurface(origin, direction, far,
                upperHalfOnly, ref entry);
            if (upperHalfOnly && origin.y < 0f && direction.y > Epsilon)
            {
                float planeDistance = -origin.y / direction.y;
                Vector3 planePoint = origin + direction * planeDistance;
                if (planePoint.x * planePoint.x + planePoint.z * planePoint.z <=
                    1f + Epsilon) entry = Mathf.Min(entry, planeDistance);
            }
            return entry == float.MaxValue ? -1f : entry;
        }

        private static void CaptureUpperEllipsoidSurface(Vector3 origin,
            Vector3 direction, float distance, bool upperHalfOnly,
            ref float entry)
        {
            if (distance < 0f || distance >= entry) return;
            if (!upperHalfOnly || origin.y + direction.y * distance >= 0f)
                entry = distance;
        }

        private static bool TryIntersectSphere(Vector3 center, float radius,
            Vector3 origin, Vector3 direction, out AnatomyHit hit)
        {
            hit = default;
            float distance = float.MaxValue;
            CaptureSphereEntry(center, radius, origin, direction, ref distance);
            if (distance > MaximumTraceDistance) return false;
            hit = new AnatomyHit(origin, direction, distance, distance);
            return true;
        }

        private static void CaptureSphereEntry(Vector3 center, float radius,
            Vector3 origin, Vector3 direction, ref float bestDistance)
        {
            Vector3 offset = origin - center;
            float b = Vector3.Dot(offset, direction);
            float c = offset.sqrMagnitude - radius * radius;
            float discriminant = b * b - c;
            if (discriminant < 0f) return;
            float distance = -b - Mathf.Sqrt(discriminant);
            if (distance >= 0f && distance < bestDistance)
                bestDistance = distance;
        }

        private static float DistanceSquaredToSegment(Vector3 point,
            Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Epsilon) return (point - start).sqrMagnitude;
            float position = Mathf.Clamp01(Vector3.Dot(point - start,
                segment) / lengthSquared);
            return (point - (start + segment * position)).sqrMagnitude;
        }

    }
}
