/// <summary>
/// Must match server <c>body_parts.id</c> seed (see BattlePostgresDatabase / BattleBodyPartDatabase).
/// </summary>
public static class BodyPartIds
{
    public const int None = 0;
    public const int Head = 1;
    public const int Torso = 2;
    public const int Legs = 3;
    public const int LeftArm = 4;
    public const int RightArm = 5;

    public static string LocKey(int id) => id switch
    {
        Head => "body_part.head",
        Torso => "body_part.torso",
        Legs => "body_part.legs",
        LeftArm => "body_part.left_arm",
        RightArm => "body_part.right_arm",
        _ => "body_part.unspecified"
    };

    public static string DisplayName(int id) => Loc.T(LocKey(id));

    public static int FromHoldTargetPart(HoldTargetIndicator.BodyPartKind kind) => kind switch
    {
        HoldTargetIndicator.BodyPartKind.Head => Head,
        HoldTargetIndicator.BodyPartKind.Torso => Torso,
        HoldTargetIndicator.BodyPartKind.Legs => Legs,
        HoldTargetIndicator.BodyPartKind.LeftHand => LeftArm,
        HoldTargetIndicator.BodyPartKind.RightHand => RightArm,
        _ => None
    };
}
