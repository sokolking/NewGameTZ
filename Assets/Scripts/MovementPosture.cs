using System;

public enum MovementPosture
{
    Walk = 0,
    Run = 1,
    Sit = 2,
    Hide = 3
}

public static class MovementPostureUtility
{
    public const string WalkId = "walk";
    public const string RunId = "run";
    public const string SitId = "sit";
    public const string HideId = "hide";

    public static string ToId(MovementPosture posture)
    {
        return posture switch
        {
            MovementPosture.Run => RunId,
            MovementPosture.Sit => SitId,
            MovementPosture.Hide => HideId,
            _ => WalkId
        };
    }

    public static MovementPosture FromId(string posture)
    {
        if (string.IsNullOrWhiteSpace(posture))
            return MovementPosture.Walk;

        return posture.Trim().ToLowerInvariant() switch
        {
            RunId => MovementPosture.Run,
            SitId => MovementPosture.Sit,
            HideId => MovementPosture.Hide,
            _ => MovementPosture.Walk
        };
    }

    public static bool CanMove(MovementPosture posture) => posture != MovementPosture.Hide;

    public static MovementPosture GetPreviewMovementPosture(MovementPosture posture)
    {
        return posture == MovementPosture.Hide ? MovementPosture.Sit : posture;
    }
}
