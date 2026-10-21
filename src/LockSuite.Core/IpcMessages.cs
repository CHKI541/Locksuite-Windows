using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LockSuite.Core;

public static class IpcConstants
{
    public const string PipeName = "LockSuiteServicePipe";
}

public sealed class IpcRequest
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; set; }

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    public static string Serialize(IpcRequest request) => JsonSerializer.Serialize(request);
    public static IpcRequest? Deserialize(string json) => JsonSerializer.Deserialize<IpcRequest>(json);
}

public sealed class IpcResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    public static string Serialize(IpcResponse response) => JsonSerializer.Serialize(response);
    public static IpcResponse? Deserialize(string json) => JsonSerializer.Deserialize<IpcResponse>(json);

    public static IpcResponse Ok(string? payload = null) => new() { Success = true, Payload = payload };
    public static IpcResponse Fail(string error) => new() { Success = false, ErrorMessage = error };
}

public sealed class ServiceStatusDto
{
    [JsonPropertyName("isRunning")]
    public bool IsRunning { get; set; }

    [JsonPropertyName("isWfpActive")]
    public bool IsWfpActive { get; set; }

    [JsonPropertyName("isSimulationMode")]
    public bool IsSimulationMode { get; set; }

    [JsonPropertyName("failClosed")]
    public bool FailClosed { get; set; }

    [JsonPropertyName("learningMode")]
    public bool LearningMode { get; set; }

    [JsonPropertyName("isPasswordConfigured")]
    public bool IsPasswordConfigured { get; set; }

    [JsonPropertyName("activeDynamicRulesCount")]
    public int ActiveDynamicRulesCount { get; set; }

    [JsonPropertyName("totalQueries")]
    public long TotalQueries { get; set; }

    [JsonPropertyName("allowedQueries")]
    public long AllowedQueries { get; set; }

    [JsonPropertyName("blockedQueries")]
    public long BlockedQueries { get; set; }

    [JsonPropertyName("whitelistPatternsCount")]
    public int WhitelistPatternsCount { get; set; }
}

public sealed class AuthRequestDto
{
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("newPassword")]
    public string? NewPassword { get; set; }
}

public sealed class AddDomainDto
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("packName")]
    public string? PackName { get; set; }
}

public sealed class TogglePackDto
{
    [JsonPropertyName("packName")]
    public string PackName { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public sealed class BlockedDomainEvent
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("clientIp")]
    public string ClientIp { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}
