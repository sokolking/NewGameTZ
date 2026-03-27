namespace BattleServer;

/// <summary>
/// When JWT is re-issued, battle WebSockets are closed with <c>session_revoked</c>.
/// The socket <c>finally</c> must not call <see cref="BattleRoomStore.PlayerLeft"/> — that would delete the room
/// while the user is reconnecting to the same unfinished battle.
/// </summary>
public static class BattleSessionRevokeSkip
{
    private static readonly object Gate = new();
    private static readonly HashSet<(long UserId, string BattleId, string PlayerId)> Pending = new();

    public static void MarkSkipPlayerLeft(long userId, string battleId, string playerId)
    {
        if (userId <= 0 || string.IsNullOrEmpty(battleId) || string.IsNullOrEmpty(playerId))
            return;
        lock (Gate)
            Pending.Add((userId, battleId, playerId));
    }

    public static bool TryConsumeSkipPlayerLeft(long userId, string battleId, string playerId)
    {
        lock (Gate)
            return Pending.Remove((userId, battleId, playerId));
    }
}
