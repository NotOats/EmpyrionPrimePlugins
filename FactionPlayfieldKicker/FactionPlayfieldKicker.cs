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
    private readonly ConcurrentDictionary<int, PlayerInfo> _playerCache = new();

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

        try
        {
            // Ignore if we're not tracking this playfield
            if (!_pluginSettings.Value.FactionHomeWorlds.TryGetValue(e.playfield, out int factionId))
                return;

            // Ignore if we don't have the player cached
            // This can only happen if the plugin launched _after_ the player joined
            // TODO: Figure out what to do with players that have no old position information
            // Move to nearest sun? Starting system?
            if (!_playerCache.TryGetValue(e.id, out PlayerInfo? player) || player == null)
                return;

            // Ignore if the player has a high enough permission level
            if (long.TryParse(player.steamId, out long steamId)
                && _adminConfig.Permissions.TryGetValue(steamId, out int permission)
                && permission >= _pluginSettings.Value.ImmunePermissionLevel)
            {
                _logger.LogInformation("Privileged player {PlayerName}(sid:{SteamId}) allowed into {PlayfieldNew}(fid:{OwnedFactionId})",
                    player.playerName, steamId, e.playfield, factionId);
                return;
            }

            // Ignore if the player is in the correct faction
            if (factionId == player.factionId)
                return;

            _logger.LogInformation("Moving player {PlayerName}(sid:{SteamId}, fid:{FactionId}) out of {PlayfieldNew}(fid:{OwnedFactionId}) back to {PlayfieldOld}",
                player.playerName, steamId, player.factionId, e.playfield, factionId, player.playfield);

            // Issue with EPM eating OK responses so these never finish
            await SendAlert(player);
            await TeleportPlayerBack(player);
        }
        finally
        {
            // Update player cache with new playfield
            // TODO: Just update exiting PlayerInfo with new playfield or add a slim player object with just what we need
            await AddPlayerToCache(e.id);
        }
    }
    private async Task SendAlert(PlayerInfo playerInfo)
    {
        var payload = new IdMsgPrio
        {
            id = playerInfo.entityId,
            msg = _pluginSettings.Value.BootMessage,
            prio = 0,
            time = 10
        };

        await _apiRequests.InGameMessageSinglePlayer(payload);
    }

    private async Task TeleportPlayerBack(PlayerInfo playerInfo)
    {
        var payload = new IdPlayfieldPositionRotation
        {
            id = playerInfo.entityId,
            playfield = playerInfo.playfield,
            pos = playerInfo.pos,
            rot = playerInfo.rot
        };

        await _apiRequests.PlayerChangePlayerfield(payload);
    }

    private async Task PlayerConnected(Id e)
    {
        if (_disposeCount > 0) return;

        var player = await AddPlayerToCache(e.id);
        _logger.LogDebug("Player connected: {PlayerName}(sid:{SteamId})", player.playerName, player.steamId);
    }

    private Task PlayerDisconnected(Id e)
    {
        if (_disposeCount > 0) return Task.CompletedTask;

        if (_playerCache.TryRemove(e.id, out PlayerInfo? player) && player != null)
            _logger.LogDebug("Player disconnected: {PlayerName}(sid:{SteamId})", player.playerName, player.steamId);
        else
            _logger.LogDebug("Player disconnected: Not found in cache - Id:{PlayerId}", e.id);

        return Task.CompletedTask;
    }

    private async Task<PlayerInfo> AddPlayerToCache(int id)
    {
        var player = await _apiRequests.PlayerInfo(id.ToId());
        return _playerCache.AddOrUpdate(id, player, (_, _) => player);
    }
}
