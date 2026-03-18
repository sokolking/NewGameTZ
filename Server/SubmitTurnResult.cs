namespace BattleServer;

public readonly struct SubmitTurnResult
{
    public bool NotFound { get; init; }
    public bool UnknownPlayer { get; init; }
    public bool WrongRound { get; init; }
    public int ExpectedRound { get; init; }
    /// <summary>Раунд был только что закрыт (CloseRound вызван в результате этого SubmitTurn).</summary>
    public bool RoundJustClosed { get; init; }

    public static SubmitTurnResult NotFoundRoom() => new() { NotFound = true };
    public static SubmitTurnResult BadPlayer() => new() { UnknownPlayer = true };
    public static SubmitTurnResult RoundMismatch(int expected) => new() { WrongRound = true, ExpectedRound = expected };
    public static SubmitTurnResult Accepted(bool roundJustClosed = false) => new() { RoundJustClosed = roundJustClosed };
}
