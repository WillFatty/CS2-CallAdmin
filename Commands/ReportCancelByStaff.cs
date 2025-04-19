using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CallAdmin;
public partial class CallAdmin
{
  [CommandHelper(minArgs: 1, usage: "[identifier]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
  public void ReportCancelByStaff(CCSPlayerController? player, CommandInfo command)
  {
    if (player == null || !player.IsValid || player.IsBot || !Config.Commands.ReportCanceled.ByStaff.Enabled) return;

    if (Config.Commands.ReportCanceled.ByStaff.Permission.Length > 0 && !AdminManager.PlayerHasPermissions(player, Config.Commands.ReportCanceled.ByStaff.Permission))
    {
      command.ReplyToCommand($"{Localizer["Prefix"]} {Localizer["MissingCommandPermission"]}");
      return;
    }

    if (!CanExecuteCommand(player.Slot))
    {
      command.ReplyToCommand($"{Localizer["Prefix"]} {Localizer["InCoolDown", Config.CooldownRefreshCommandSeconds]}");
      return;
    }

    string playerName = player.PlayerName;
    string playerSteamid = player.SteamID.ToString();
    string mapName = Server.MapName;

    Task.Run(async () =>
    {
      DatabaseReportClass? getReport = await GetReportDatabase(null, playerSteamid, Config.Commands.ReportCanceled.ByStaff.MaxTimeMinutes);

      if (getReport == null)
      {
        SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer["ReportNotFound"]}");
        return;
      }

      if (Config.Commands.ReportCanceled.ByStaff.DeleteOrEditEmbed == 1)
      {
        bool deleteMessage = await CancelReportInAPI(getReport.message_id);

        if (!deleteMessage)
        {
          SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer["WebhookError"]}");
          return;
        }
      }
      else
      {
        // Create a simplified payload with just the required information
        var simplifiedPayload = new
        {
          author_name = getReport.victim_name,
          author_steamid = getReport.victim_steamid,
          target_name = getReport.suspect_name,
          target_steamid = getReport.suspect_steamid,
          reason = getReport.reason,
          server_name = getReport.host_name,
          server_ip = getReport.host_ip,
          map_name = mapName,
          identifier = getReport.identifier,
          admin_name = playerName,
          admin_steamid = playerSteamid,
          action = "cancel",
          canceled_by_author = false
        };
        
        string sendMessageToDiscord = await SendMessageToAPI(JsonSerializer.Serialize(simplifiedPayload));

        if (sendMessageToDiscord == "There was an error sending the data to API" || sendMessageToDiscord == "Unable to get response ID")
        {
          SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer["WebhookError"]}");
          Logger.LogError(sendMessageToDiscord);
          return;
        }
      }

      bool updateReport = await UpdateReportDeletedDatabase(getReport.identifier, playerName, playerSteamid, true);

      SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer[!updateReport ? "MarkedAsDeletedButNotInDatabase" : "ReportMarkedAsDeleted"]}");

    });

  }
}
