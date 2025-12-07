using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

public class PlacementUtil
{

    public enum RelativePlacement
    {
        Left,
        Right,
        Up,
        Down,
    }

    public static Dictionary<string, float> RelativeSizeDict = new Dictionary<string, float>()
    {
        { "smaller", 0.75f },
        { "same", 1.0f },
        { "larger", 1.25f },
    };

    public enum SpatialProximity
    {
        Close,
        Far,
    }

    public static RelativePlacement GetRandomPlacement()
    {
        var values = System.Enum.GetValues(typeof(RelativePlacement));
        return (RelativePlacement)values.GetValue(UnityEngine.Random.Range(0, values.Length));
    }

    public static RelativePlacement MatchRelativePlacementByString(string relativeLocation)
    {
        switch (relativeLocation)
        {
            case "left":
                return RelativePlacement.Left;
            case "right":
                return RelativePlacement.Right;
            case "up":
                return RelativePlacement.Up;
            case "down":
                return RelativePlacement.Down;
            default:
                return GetRandomPlacement();
        }
    }

    public static float MatchRelativeSizeByString(string relativeSize)
    {
        return DictUtil.GetOrDefault(RelativeSizeDict, relativeSize, 1.0f);
    }

    public static SpatialProximity MatchSpatialProximityByString(string spatialProximity)
    {
        switch (spatialProximity)
        {
            case "close":
                return SpatialProximity.Close;
            case "far":
                return SpatialProximity.Far;
            default:
                return SpatialProximity.Close;
        }
    }
}