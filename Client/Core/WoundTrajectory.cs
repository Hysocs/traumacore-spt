using UnityEngine;

namespace TraumaCore
{
    internal enum WoundPathEndpoint
    {
        Split,
        Stop,
        Exit
    }

    internal readonly struct WoundPathSegment
    {
        internal readonly Vector3 StartPoint;
        internal readonly Vector3 Direction;
        internal readonly float TraveledDepth;
        internal readonly WoundPathEndpoint Endpoint;
        internal readonly bool IsBranch;

        internal Vector3 EndPoint =>
            StartPoint + Direction * TraveledDepth;

        internal WoundPathSegment(Vector3 startPoint, Vector3 direction,
            float traveledDepth, WoundPathEndpoint endpoint, bool isBranch)
        {
            StartPoint = startPoint;
            Direction = direction.sqrMagnitude > 0.0001f
                ? direction.normalized : Vector3.forward;
            TraveledDepth = Mathf.Max(0f, traveledDepth);
            Endpoint = endpoint;
            IsBranch = isBranch;
        }
    }

    internal readonly struct WoundTrajectory
    {
        internal readonly Vector3 EntryPoint;
        internal readonly Vector3 EntryDirection;
        internal readonly WoundPathSegment[] Segments;

        internal bool HasPath => Segments != null && Segments.Length > 0;
        internal bool HasBranches => Segments != null && Segments.Length > 1;

        private WoundTrajectory(Vector3 entryPoint, Vector3 entryDirection,
            WoundPathSegment[] segments)
        {
            EntryPoint = entryPoint;
            EntryDirection = entryDirection.sqrMagnitude > 0.0001f
                ? entryDirection.normalized : Vector3.forward;
            Segments = segments;
        }

        internal static WoundTrajectory Create(WoundBallistics wound,
            Vector3 direction)
        {
            Vector3 forward = direction.sqrMagnitude > 0.0001f
                ? direction.normalized : Vector3.forward;
            if (!wound.HasFragmentation)
                return new WoundTrajectory(wound.EntryPoint, forward,
                    new[]
                    {
                        new WoundPathSegment(wound.EntryPoint, forward,
                            wound.TraveledDepth,
                            wound.PassedThrough
                                ? WoundPathEndpoint.Exit
                                : WoundPathEndpoint.Stop,
                            false)
                    });

            WoundPathSegment[] segments =
                new WoundPathSegment[wound.FragmentPaths.Length + 1];
            segments[0] = new WoundPathSegment(wound.EntryPoint, forward,
                wound.FragmentationDepth, WoundPathEndpoint.Split, false);
            for (int index = 0; index < wound.FragmentPaths.Length; index++)
            {
                WoundBallistics.FragmentPath path = wound.FragmentPaths[index];
                segments[index + 1] = new WoundPathSegment(path.StartPoint,
                    path.Direction, path.TraveledDepth,
                    path.PassedThrough
                        ? WoundPathEndpoint.Exit
                        : WoundPathEndpoint.Stop,
                    true);
            }
            return new WoundTrajectory(wound.EntryPoint, forward, segments);
        }

        internal WoundTrajectory ToLocal(Transform anchor)
        {
            if (anchor == null || !HasPath)
                return this;
            WoundPathSegment[] localSegments =
                new WoundPathSegment[Segments.Length];
            for (int index = 0; index < Segments.Length; index++)
            {
                WoundPathSegment segment = Segments[index];
                localSegments[index] = new WoundPathSegment(
                    anchor.InverseTransformPoint(segment.StartPoint),
                    anchor.InverseTransformDirection(segment.Direction),
                    segment.TraveledDepth, segment.Endpoint,
                    segment.IsBranch);
            }
            return new WoundTrajectory(
                anchor.InverseTransformPoint(EntryPoint),
                anchor.InverseTransformDirection(EntryDirection),
                localSegments);
        }
    }
}
