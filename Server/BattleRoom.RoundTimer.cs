using System.Collections.Generic;
using System.Linq;
using BattleServer.Models;

namespace BattleServer;

public partial class BattleRoom
{
    private static long GetUtcNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void ResetRoundTimer()
    {
        RoundDeadlineUtcMs = GetUtcNowMs() + (long)Math.Round(RoundDuration * 1000f);
        RoundTimeLeft = RoundDuration;
    }

    private void RefreshRoundTimeLeft()
    {
        if (RoundDeadlineUtcMs <= 0)
        {
            RoundTimeLeft = 0f;
            return;
        }

        long remainingMs = RoundDeadlineUtcMs - GetUtcNowMs();
        RoundTimeLeft = Math.Max(0f, remainingMs / 1000f);
    }
}
