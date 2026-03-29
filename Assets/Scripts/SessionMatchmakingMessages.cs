using System;
using Newtonsoft.Json;

/// <summary>Payloads for <c>/ws/session</c> matchmaking (server → client).</summary>
[Serializable]
public class MatchmakingMatchStartedMessage
{
    [JsonProperty("type")] public string type;
    [JsonProperty("mode")] public string mode;
    [JsonProperty("battleId")] public string battleId;
    [JsonProperty("playerId")] public string playerId;
    [JsonProperty("battleStarted")] public BattleStartedPayload battleStarted;
}
