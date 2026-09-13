using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

Assembly trauma = Assembly.Load("TraumaCore");
Type segmentType = trauma.GetType("TraumaCore.BoneSegmentPose", true);
Type intersectionType = trauma.GetType("TraumaCore.AnatomyIntersections", true);
ConstructorInfo createSegment = segmentType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
    null, new[] { typeof(Vector3), typeof(Vector3), typeof(float) }, null);
ConstructorInfo createTaperedSegment = segmentType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
    null, new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(float) }, null);
MethodInfo intersect = intersectionType.GetMethod("TryIntersectCylinder", BindingFlags.Static | BindingFlags.NonPublic);
int checkedCases = 0;
object cylinder = CreateCylinder(Vector3.zero, new Vector3(0, 1, 0), 0.1f);

Check("side entry", cylinder, new Vector3(-0.3f, 0.5f, 0), Vector3.right, true, 0.2f, 0.4f);
Check("non-unit ray", cylinder, new Vector3(-0.3f, 0.5f, 0), Vector3.right * 7f, true, 0.2f, 0.4f);
Check("bottom cap", cylinder, new Vector3(0, -0.2f, 0), Vector3.up, true, 0.2f, 1.2f);
Check("top cap", cylinder, new Vector3(0, 1.2f, 0), Vector3.down, true, 0.2f, 1.2f);
Check("inside", cylinder, new Vector3(0, 0.5f, 0), Vector3.right, true, 0f, 0.1f);
Check("parallel outside radius", cylinder, new Vector3(0.2f, -0.2f, 0), Vector3.up, false);
Check("flat cap excludes capsule dome", cylinder, new Vector3(-0.3f, 1.05f, 0), Vector3.right, false);
Check("behind ray", cylinder, new Vector3(-0.3f, 0.5f, 0), Vector3.left, false);
Check("beyond trace limit", cylinder, new Vector3(-0.7f, 0.5f, 0), Vector3.right, false);
Check("zero direction", cylinder, new Vector3(0, 0.5f, 0), Vector3.zero, false);
Check("zero length", CreateCylinder(Vector3.zero, Vector3.zero, 0.1f), Vector3.left, Vector3.right, false);
Check("zero radius", CreateCylinder(Vector3.zero, Vector3.up, 0f), Vector3.left, Vector3.right, false);
Check("rotated cylinder", CreateCylinder(new Vector3(1, 2, 3), new Vector3(2, 2, 3), 0.1f),
    new Vector3(1.5f, 1.7f, 3), Vector3.up, true, 0.2f, 0.4f);

object firstForearm = CreateCylinder(new Vector3(-0.007f, 0, 0), new Vector3(-0.007f, 0.25f, 0), 0.005f);
object secondForearm = CreateCylinder(new Vector3(0.007f, 0, 0), new Vector3(0.007f, 0.25f, 0), 0.005f);
Check("forearm 1 hit", firstForearm, new Vector3(-0.007f, 0.1f, -0.1f), Vector3.forward, true, 0.095f, 0.105f);
Check("forearm 2 independent miss", secondForearm, new Vector3(-0.007f, 0.1f, -0.1f), Vector3.forward, false);
Check("forearm gap 1", firstForearm, new Vector3(0, 0.1f, -0.1f), Vector3.forward, false);
Check("forearm gap 2", secondForearm, new Vector3(0, 0.1f, -0.1f), Vector3.forward, false);
object widening = createTaperedSegment.Invoke(new object[] { Vector3.zero, Vector3.up, 0.1f, 0.2f });
object narrowing = createTaperedSegment.Invoke(new object[] { Vector3.zero, Vector3.up, 0.2f, 0.1f });
Check("taper midpoint", widening, new Vector3(-0.3f, 0.5f, 0), Vector3.right, true, 0.15f, 0.45f);
Check("taper narrow end", widening, new Vector3(-0.3f, 0, 0), Vector3.right, true, 0.2f, 0.4f);
Check("taper wide end", widening, new Vector3(-0.3f, 1, 0), Vector3.right, true, 0.1f, 0.5f);
Check("taper axial side entry", widening, new Vector3(0.15f, 0, 0), Vector3.up, true, 0.5f, 1f);
Check("taper axial side exit", narrowing, new Vector3(0.15f, 0, 0), Vector3.up, true, 0f, 0.5f);
Check("taper cap entry", widening, new Vector3(0, -0.2f, 0), Vector3.up, true, 0.2f, 1.2f);
Check("taper excludes extended cone", widening, new Vector3(-0.3f, 1.05f, 0), Vector3.right, false);
Check("taper linear intersection", widening, new Vector3(-0.2f, 0, 0), new Vector3(0.1f, 1f, 0),
    true, 0.5f * (float)Math.Sqrt(1.01), (float)Math.Sqrt(1.01));
Console.WriteLine($"Passed {checkedCases} cylinder geometry checks.");
MethodInfo projectPreview = trauma.GetType("TraumaCore.AnatomyOverlayRenderer", true)
    .GetMethod("ConvertPreviewPoint", BindingFlags.Static | BindingFlags.NonPublic);
CheckPreview("preview center", new Rect(-200, -300, 400, 600), new Rect(0, 0, 1, 1),
    new Vector3(0.5f, 0.5f, 1), Vector2.zero);
CheckPreview("preview corner", new Rect(-200, -300, 400, 600), new Rect(0, 0, 1, 1),
    new Vector3(1, 1, 1), new Vector2(200, 300));
CheckPreview("cropped preview", new Rect(-200, -300, 400, 600), new Rect(0.25f, 0.25f, 0.5f, 0.5f),
    new Vector3(0.25f, 0.75f, 1), new Vector2(-200, 300));
CheckPreview("flipped preview", new Rect(-200, -300, 400, 600), new Rect(0, 1, 1, -1),
    new Vector3(0, 1, 1), new Vector2(-200, -300));
Console.WriteLine("Passed 4 shared preview projection checks.");
if (args.Length > 0) VerifySavedLimbCalibration(args[0]);

void CheckPreview(string name, Rect bounds, Rect uv, Vector3 viewport, Vector2 expected)
{
    Vector2 actual = (Vector2)projectPreview.Invoke(null, new object[] { bounds, uv, viewport });
    if ((actual - expected).sqrMagnitude > 0.0001f)
        throw new Exception($"{name}: preview coordinates differ from expected bounds.");
}

void VerifySavedLimbCalibration(string configPath)
{
    Dictionary<string, Dictionary<string, string>> sections = new();
    Dictionary<string, string> currentSection = null;
    foreach (string rawLine in File.ReadLines(configPath))
    {
        string line = rawLine.Trim();
        if (line.StartsWith("["))
        {
            currentSection = new Dictionary<string, string>();
            sections.Add(line.Trim('[', ']'), currentSection);
        }
        else if (currentSection != null && !line.StartsWith("#") && line.Contains('='))
        {
            int separator = line.IndexOf('=');
            currentSection[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
        }
    }
    MethodInfo resolve = trauma.GetType("TraumaCore.LimbBoneGeometry", true)
        .GetMethod("ResolveDimensions", BindingFlags.Static | BindingFlags.NonPublic);
    Type bodyPartType = resolve.GetParameters()[0].ParameterType;
    foreach (string side in new[] { "Left", "Right" })
    foreach (string limb in new[] { "Arm", "Leg" })
    {
        bool isArm = limb == "Arm";
        for (int boneIndex = 0; boneIndex < (isArm ? 3 : 2); boneIndex++)
        {
            string boneName = isArm ? (boneIndex == 0 ? "upper arm" : $"forearm {boneIndex}")
                : (boneIndex == 0 ? "thigh" : "lower leg");
            Dictionary<string, string> saved = sections[$"{limb} Calibration - {side} {boneName}"];
            object dimensions = resolve.Invoke(null, new[] { Enum.Parse(bodyPartType, side + limb), (object)boneIndex });
            Type dimensionType = dimensions.GetType();
            foreach (string endpoint in new[] { "Start", "End" })
            {
                string anchor = isArm ? (endpoint == "Start"
                    ? (boneIndex == 0 ? "Shoulder" : "Elbow")
                    : (boneIndex == 0 ? "Elbow" : "Wrist"))
                    : (endpoint == "Start" ? (boneIndex == 0 ? "Hip" : "Knee")
                        : (boneIndex == 0 ? "Knee" : "Ankle"));
                using JsonDocument offset = JsonDocument.Parse(saved[anchor + " anchor"]);
                Vector3 expected = new(offset.RootElement.GetProperty("x").GetSingle(),
                    offset.RootElement.GetProperty("y").GetSingle(), offset.RootElement.GetProperty("z").GetSingle());
                Vector3 actual = (Vector3)dimensionType.GetField(endpoint + "OffsetMillimeters",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dimensions);
                if (!actual.Equals(expected)) throw new Exception($"Saved {side} {boneName} {anchor} differs from code.");
                float diameter = (float)dimensionType.GetField(
                    endpoint == "Start" ? "DiameterMillimeters" : "EndDiameterMillimeters",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dimensions);
                float expectedDiameter = float.Parse(saved[isArm ? "Thickness mm" : anchor + " thickness mm"],
                    CultureInfo.InvariantCulture);
                if (isArm && boneIndex == 0 && endpoint == "Start") expectedDiameter *= 1.1f * 1.1f;
                expectedDiameter = (float)Math.Round(expectedDiameter, 2, MidpointRounding.AwayFromZero);
                if (diameter != expectedDiameter)
                    throw new Exception($"Saved {side} {boneName} {anchor} diameter differs from expected calibration.");
            }
        }
    }
    Console.WriteLine("All 10 limb bones match rounded calibration with two successive 10% upper-arm shoulder increases.");
    Type spineType = trauma.GetType("TraumaCore.SpineBoneGeometry", true);
    foreach (string spine in new[] { "Cervical", "Thoracic" })
    foreach (string endpoint in new[] { "Start", "End" })
    {
        string anchor = spine == "Cervical"
            ? (endpoint == "Start" ? "Brain" : "Chest")
            : (endpoint == "Start" ? "Chest" : "Pelvis");
        Dictionary<string, string> saved = sections[$"Spine Calibration - {spine}"];
        using JsonDocument offset = JsonDocument.Parse(saved[anchor + " anchor offset mm"]);
        Vector3 expected = new(offset.RootElement.GetProperty("x").GetSingle(),
            offset.RootElement.GetProperty("y").GetSingle(), offset.RootElement.GetProperty("z").GetSingle());
        Vector3 actual = (Vector3)spineType.GetField(spine + endpoint + "OffsetMillimeters",
            BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        float diameter = (float)spineType.GetField(spine + endpoint + "DiameterMillimeters",
            BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        if (!actual.Equals(expected) || diameter != float.Parse(saved[anchor + " thickness mm"], CultureInfo.InvariantCulture))
            throw new Exception($"Saved {spine} {anchor} calibration differs from code.");
    }
    Console.WriteLine("All 4 spine endpoints and diameters exactly match saved calibration.");
}

object CreateCylinder(Vector3 start, Vector3 end, float radius) =>
    createSegment.Invoke(new object[] { start, end, radius });

void Check(string name, object segment, Vector3 origin, Vector3 direction,
    bool expectedHit, float expectedEntry = 0, float expectedExit = 0)
{
    object[] arguments = { segment, origin, direction, null };
    bool actualHit = (bool)intersect.Invoke(null, arguments);
    if (actualHit != expectedHit) throw new Exception($"{name}: expected hit={expectedHit}, got {actualHit}");
    if (actualHit)
    {
        object hit = arguments[3];
        Type hitType = hit.GetType();
        float entry = (float)hitType.GetField("EntryDistance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hit);
        float exit = (float)hitType.GetField("ExitDistance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hit);
        if (Math.Abs(entry - expectedEntry) > 0.0001f || Math.Abs(exit - expectedExit) > 0.0001f)
            throw new Exception($"{name}: interval [{entry}, {exit}], expected [{expectedEntry}, {expectedExit}]");
    }
    checkedCases++;
}
