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

/// <summary>Server → client: <c>profileResponse</c> after <c>profileRequest</c>.</summary>
[Serializable]
public sealed class UserProfileSocketDto
{
    [JsonProperty("username")] public string username;
    [JsonProperty("level")] public int level;
    [JsonProperty("strength")] public int strength;
    [JsonProperty("agility")] public int agility;
    [JsonProperty("intuition")] public int intuition;
    [JsonProperty("endurance")] public int endurance;
    [JsonProperty("accuracy")] public int accuracy;
    [JsonProperty("intellect")] public int intellect;
    [JsonProperty("maxHp")] public int maxHp;
    [JsonProperty("currentHp")] public int currentHp;
    [JsonProperty("maxAp")] public int maxAp;
}
