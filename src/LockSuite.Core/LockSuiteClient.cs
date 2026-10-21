using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LockSuite.Core;

public sealed class LockSuiteClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly string _pipeName;

    public LockSuiteClient(string? pipeName = null)
    {
        _pipeName = pipeName ?? IpcConstants.PipeName;
    }

    public async Task<IpcResponse> SendCommandAsync(string command, string? payload = null, string? sessionToken = null, int timeoutMs = 3000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeoutMs);

            await pipe.ConnectAsync(cts.Token);

            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var req = new IpcRequest
            {
                Command = command,
                Payload = payload,
                SessionToken = sessionToken
            };

            string reqJson = IpcRequest.Serialize(req);
            await writer.WriteLineAsync(reqJson.AsMemory(), cts.Token);
            await writer.FlushAsync(cts.Token);

            string? resJson = await reader.ReadLineAsync(cts.Token);
            if (string.IsNullOrEmpty(resJson))
            {
                return IpcResponse.Fail("Respuesta vacía del servicio");
            }

            var resp = IpcResponse.Deserialize(resJson);
            return resp ?? IpcResponse.Fail("No se pudo deserializar la respuesta");
        }
        catch (TimeoutException)
        {
            return IpcResponse.Fail("Tiempo de espera agotado al conectar con el servicio");
        }
        catch (Exception ex)
        {
            return IpcResponse.Fail($"No se pudo conectar al servicio LockSuite: {ex.Message}");
        }
    }

    public async Task<ServiceStatusDto?> GetStatusAsync()
    {
        var resp = await SendCommandAsync("GetStatus");
        if (resp.Success && !string.IsNullOrEmpty(resp.Payload))
        {
            return JsonSerializer.Deserialize<ServiceStatusDto>(resp.Payload, JsonOpts);
        }
        return null;
    }

    public async Task<WhitelistConfig?> GetConfigAsync()
    {
        var resp = await SendCommandAsync("GetConfig");
        if (resp.Success && !string.IsNullOrEmpty(resp.Payload))
        {
            return WhitelistConfig.FromJson(resp.Payload);
        }
        return null;
    }

    public async Task<List<BlockedDomainEvent>> GetRecentBlockedAsync()
    {
        var resp = await SendCommandAsync("GetRecentBlocked");
        if (resp.Success && !string.IsNullOrEmpty(resp.Payload))
        {
            return JsonSerializer.Deserialize<List<BlockedDomainEvent>>(resp.Payload, JsonOpts) ?? new();
        }
        return new();
    }

    public async Task<bool> ClearRecentBlockedAsync(string sessionToken)
    {
        var resp = await SendCommandAsync("ClearRecentBlocked", sessionToken: sessionToken);
        return resp.Success;
    }

    public async Task<(bool Success, string? Token, string? Error)> AuthenticateAsync(string password)
    {
        string payload = JsonSerializer.Serialize(new AuthRequestDto { Password = password });
        var resp = await SendCommandAsync("Authenticate", payload: payload);
        if (resp.Success)
        {
            return (true, resp.Payload, null);
        }
        return (false, null, resp.ErrorMessage ?? "Contraseña incorrecta");
    }

    public async Task<(bool Success, string? Error)> SetInitialPasswordAsync(string password)
    {
        string payload = JsonSerializer.Serialize(new AuthRequestDto { Password = password });
        var resp = await SendCommandAsync("SetInitialPassword", payload: payload);
        return (resp.Success, resp.ErrorMessage);
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string oldPassword, string newPassword, string sessionToken)
    {
        string payload = JsonSerializer.Serialize(new AuthRequestDto { Password = oldPassword, NewPassword = newPassword });
        var resp = await SendCommandAsync("ChangePassword", payload: payload, sessionToken: sessionToken);
        return (resp.Success, resp.ErrorMessage);
    }

    public async Task<bool> TogglePackAsync(string packName, bool enabled, string sessionToken)
    {
        string payload = JsonSerializer.Serialize(new TogglePackDto { PackName = packName, Enabled = enabled });
        var resp = await SendCommandAsync("TogglePack", payload: payload, sessionToken: sessionToken);
        return resp.Success;
    }

    public async Task<bool> AddDomainAsync(string domain, string? packName, string sessionToken)
    {
        string payload = JsonSerializer.Serialize(new AddDomainDto { Domain = domain, PackName = packName });
        var resp = await SendCommandAsync("AddDomain", payload: payload, sessionToken: sessionToken);
        return resp.Success;
    }

    public async Task<bool> RemoveDomainAsync(string domain, string sessionToken)
    {
        var resp = await SendCommandAsync("RemoveDomain", payload: domain, sessionToken: sessionToken);
        return resp.Success;
    }

    public async Task<bool> SaveConfigAsync(WhitelistConfig config, string sessionToken)
    {
        string payload = config.ToJson();
        var resp = await SendCommandAsync("SaveConfig", payload: payload, sessionToken: sessionToken);
        return resp.Success;
    }

    public async Task<string> TestWfpAsync(string sessionToken)
    {
        var resp = await SendCommandAsync("TestWfp", sessionToken: sessionToken);
        return resp.Success ? (resp.Payload ?? "Test iniciado") : (resp.ErrorMessage ?? "Error");
    }

    public async Task<bool> SetLearningModeAsync(bool enabled, string sessionToken)
    {
        var resp = await SendCommandAsync("SetLearningMode", payload: enabled ? "true" : "false", sessionToken: sessionToken);
        return resp.Success;
    }

    // ----------------- Modo de prueba (trial) -----------------

    public async Task<TrialStatusDto?> GetTrialStatusAsync()
    {
        var resp = await SendCommandAsync("GetTrialStatus");
        if (resp.Success && !string.IsNullOrEmpty(resp.Payload))
        {
            return JsonSerializer.Deserialize<TrialStatusDto>(resp.Payload, JsonOpts);
        }
        return null;
    }

    /// <summary>Decisión del usuario al terminar la prueba. keep=true conserva; keep=false quita todo.
    /// El token solo es necesario si la prueba fue configurada con "pedir contraseña del tutor para quitar".</summary>
    public async Task<(bool Success, string? Error)> DecideTrialAsync(bool keep, string? sessionToken = null)
    {
        string payload = JsonSerializer.Serialize(new TrialDecisionDto { Keep = keep });
        var resp = await SendCommandAsync("DecideTrial", payload: payload, sessionToken: sessionToken);
        return (resp.Success, resp.ErrorMessage);
    }

    /// <summary>Inicia/reinicia un período de prueba desde la GUI (requiere tutor autenticado).</summary>
    public async Task<(bool Success, string? Error)> SetTrialAsync(int durationMinutes, string endAction, bool requireTutorToRemove, string sessionToken)
    {
        string payload = JsonSerializer.Serialize(new SetTrialDto
        {
            DurationMinutes = durationMinutes,
            EndAction = endAction,
            RequireTutorToRemove = requireTutorToRemove
        });
        var resp = await SendCommandAsync("SetTrial", payload: payload, sessionToken: sessionToken);
        return (resp.Success, resp.ErrorMessage);
    }
}
