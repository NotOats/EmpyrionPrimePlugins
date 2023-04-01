using Eleon.Modding;
using EmpyrionPrime.ModFramework;
using EmpyrionPrime.ModFramework.Configuration;
using EmpyrionPrime.ModFramework.Environment;
using EmpyrionPrime.ModFramework.Extensions;
using EmpyrionPrime.Plugin;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FactionPlayfieldKicker;

public sealed class FactionPlayfieldKicker : IEmpyrionPlugin, IDisposable
{
    private readonly ConcurrentDictionary<int, DropOutStack<string>> _playerLocationHistory = new();

    private readonly ILogger _logger;
    private readonly IEmpyrionAdminConfig _adminConfig;
    private readonly IApiEvents _apiEvents;
    private readonly IApiRequests _apiRequests;
    private readonly IPluginOptions<PluginSettings> _pluginSettings;

    private int _disposeCount = 0;

    public string Name => "Faction Playfield Kicker";
    public string Author => "NotOats";
    public Version Version => new("1.0.0");
    public ModInterface? ModInterface => null;

    public FactionPlayfieldKicker(ILogger logger, IEmpyrionAdminConfig adminConfig,
        IApiEvents apiEvents, IApiRequests apiRequests,
        IPluginOptionsFactory optionsFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adminConfig = adminConfig ?? throw new ArgumentNullException(nameof(adminConfig));
        _apiEvents = apiEvents ?? throw new ArgumentNullException(nameof(apiEvents));
        _apiRequests = apiRequests ?? throw new ArgumentNullException(nameof(apiRequests));
        _pluginSettings = optionsFactory?.Get<PluginSettings>() ?? throw new ArgumentNullException(nameof(optionsFactory));

        if (_pluginSettings.Value.FallbackPlayfield == string.Empty)
            throw new ArgumentException("FallbackPlayfield must be set", nameof(optionsFactory));

        _apiEvents.PlayerConnected        += PlayerConnected;
        _apiEvents.PlayerDisconnected     += PlayerDisconnected;
        _apiEvents.PlayerChangedPlayfield += PlayerChangedPlayfield;
    }

    public void Dispose()
    {
        if (Interlocked.Increment(ref _disposeCount) != 1)
            return;

        _apiEvents.PlayerChangedPlayfield -= PlayerChangedPlayfield;
        _apiEvents.PlayerConnected        -= PlayerConnected;
        _apiEvents.PlayerDisconnected     -= PlayerDisconnected;
    }

    private async Task PlayerChangedPlayfield(IdPlayfield e)
    {
        if (_disposeCount > 0) return;

        // Request is after playfield change so should have location data for e.playfield
        var latestPlayer = await _apiRequests.PlayerInfo(e.id.ToId());
        AddPlayerLocation(latestPlayer);

        // Ignore if we're not tracking this playfield
        if (!_pluginSettings.Value.FactionHomeWorlds.TryGetValue(e.playfield, out int factionId))
            return;

        // Ignore if the player has a high enough permission level
        if (long.TryParse(latestPlayer.steamId, out long steamId)
            && _adminConfig.Permissions.TryGetValue(steamId, out int permission)
            && permission >= _pluginSettings.Value.ImmunePermissionLevel)
        {
            _logger.LogInformation("Privileged player {PlayerName}(sid:{SteamId}) allowed into {PlayfieldNew}(fid:{OwnedFactionId})",
                latestPlayer.playerName, steamId, e.playfield, factionId);
            return;
        }

        // Ignore if the player is in the correct faction
        if (latestPlayer.factionId == factionId)
            return;

        _logger.LogInformation("Moving player {PlayerName}(sid:{SteamId}, fid:{FactionId}) out of {PlayfieldNew}(fid:{OwnedFactionId}) back to {PlayfieldOld}",
            latestPlayer.playerName, latestPlayer.steamId, latestPlayer.factionId, e.playfield, factionId, latestPlayer.playfield);

        await _apiRequests.InGameMessageSinglePlayer(new IdMsgPrio
        {
            id = latestPlayer.entityId,
            msg = _pluginSettings.Value.BootMessage,
            prio = 0,
            time = 10
        });

        await RollbackPlayerLocation(latestPlayer, e.playfield);
    }

    private async Task RollbackPlayerLocation(PlayerInfo latestPlayer, string restrictedPlayfield)
    {
        var dist = _pluginSettings.Value.DistanceFromPlanet;
        var vecPosition = new PVector3(dist, dist, dist);
        var vecRotation = new PVector3(0, 0, 0);

        // Move to fallback if there's no location history or the history points to the restricted playfield
        var lastLocation = GetPreviousLocation(latestPlayer, restrictedPlayfield);
        if (lastLocation == null || lastLocation == restrictedPlayfield)
        {
            var playfield = _pluginSettings.Value.FallbackPlayfield;

            _logger.LogWarning("No location history for player, moving to {FallbackPlayfield}", playfield);

            await TeleportPlayer(latestPlayer.entityId, playfield, vecPosition, vecRotation);
        }
        else
        {
            await TeleportPlayer(latestPlayer.entityId, lastLocation, vecPosition, vecRotation);
        }
    }

    private async Task TeleportPlayer(int entityId, string playfield, PVector3 position, PVector3 rotation)
    {
        var payload = new IdPlayfieldPositionRotation
        {
            id = entityId,
            playfield = playfield,
            pos = position,
            rot = rotation
        };

        await _apiRequests.PlayerChangePlayerfield(payload);
    }

    private async Task PlayerConnected(Id e)
    {
        if (_disposeCount > 0) return;

        var player = await _apiRequests.PlayerInfo(e);
        AddPlayerLocation(player);

        _logger.LogDebug("Player connected: {PlayerName}(sid:{SteamId})", player.playerName, player.steamId);
    }

    private Task PlayerDisconnected(Id e)
    {
        if (_disposeCount > 0) return Task.CompletedTask;

        if(_playerLocationHistory.TryRemove(e.id, out _))
            _logger.LogDebug("Player disconnected: EntId {PlayerId}", e.id);
        else
            _logger.LogDebug("Player disconnected: Not found in cache - EntId {PlayerId}", e.id);

        return Task.CompletedTask;
    }
    
    private void AddPlayerLocation(PlayerInfo player)
    {
        var history = _playerLocationHistory.GetOrAdd(player.entityId, _ => new DropOutStack<string>(10));
        history.Push(player.playfield);
    }
    
    private string? GetPreviousLocation(PlayerInfo player, string currentPlayfield)
    {
        if (!_playerLocationHistory.TryGetValue(player.entityId, out DropOutStack<string>? history) ||
            history == null)
            return null;

        return history.FirstOrDefault(x => x != currentPlayfield);
    }
}
