using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;

namespace CallAdmin;
public partial class CallAdmin
{
  public async Task<bool> CancelReportInAPI(string messageId)
  {
    try
    {
      var httpClient = new HttpClient();
      httpClient.Timeout = TimeSpan.FromSeconds(10);
      
      // Create a proper object and use JsonSerializer instead of manual string construction
      var requestObj = new { 
        message_id = messageId,
        action = "cancel"
      };
      
      var jsonString = JsonSerializer.Serialize(requestObj);
      var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
      
      // Add debug logging
      Logger.LogInformation($"Sending cancel request to API: {jsonString}");
      
      // Post to the main endpoint rather than a separate cancel endpoint
      var result = await httpClient.PostAsync("https://bot.affinitycs2.com/api/calladmin", content);
      
      // Read and log the response for debugging
      var responseContent = await result.Content.ReadAsStringAsync();
      Logger.LogInformation($"API response status: {result.StatusCode}, body: {responseContent}");
      
      return result.IsSuccessStatusCode;
    }
    catch (Exception e)
    {
      // Log the full exception details
      Logger.LogError($"Error in CancelReportInAPI: {e.Message}");
      Logger.LogError($"Stack trace: {e.StackTrace}");
      if (e.InnerException != null)
      {
        Logger.LogError($"Inner exception: {e.InnerException.Message}");
      }
      return false;
    }
  }

  public async Task<bool> MarkReportHandledInAPI(string messageId, string adminName, string adminSteamId)
  {
    try
    {
      var httpClient = new HttpClient();
      httpClient.Timeout = TimeSpan.FromSeconds(10);
      
      var requestObj = new {
        message_id = messageId,
        admin_name = adminName,
        admin_steamid = adminSteamId,
        action = "handled"
      };
      
      var jsonContent = JsonSerializer.Serialize(requestObj);
      var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
      
      // Add debug logging
      Logger.LogInformation($"Marking report as handled: {jsonContent}");
      
      // Post to the main endpoint rather than a separate handled endpoint
      var result = await httpClient.PostAsync("https://bot.affinitycs2.com/api/calladmin", content);
      
      // Read and log the response for debugging
      var responseContent = await result.Content.ReadAsStringAsync();
      Logger.LogInformation($"API response status: {result.StatusCode}, body: {responseContent}");

      return result.IsSuccessStatusCode;
    }
    catch (Exception e)
    {
      // Log the full exception details
      Logger.LogError($"Error in MarkReportHandledInAPI: {e.Message}");
      Logger.LogError($"Stack trace: {e.StackTrace}");
      if (e.InnerException != null)
      {
        Logger.LogError($"Inner exception: {e.InnerException.Message}");
      }
      return false;
    }
  }

  public async Task<string> SendMessageToAPI(dynamic jsonObj)
  {
    try
    {
      var httpClient = new HttpClient();
      httpClient.Timeout = TimeSpan.FromSeconds(10);
      
      // Simple string content for the JSON
      var content = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");
      
      // Add debug logging
      Logger.LogInformation($"Sending simplified report to API: {jsonObj}");
      
      // Use await properly instead of .Result to avoid potential deadlocks
      var result = await httpClient.PostAsync("https://bot.affinitycs2.com/api/calladmin", content);

      // Read the response content
      var responseContent = await result.Content.ReadAsStringAsync();
      Logger.LogInformation($"API response status: {result.StatusCode}, body: {responseContent}");

      if (!result.IsSuccessStatusCode)
      {
        Logger.LogError($"API error response: {result.StatusCode} - {responseContent}");
        return "There was an error sending the data to API";
      }

      // Return the identifier that was included in the request - this will be used as the message_id
      try 
      {
        // Try to extract the identifier from our request to use as a unique ID for the database
        var requestData = System.Text.Json.JsonDocument.Parse(jsonObj.ToString());
        if (requestData.RootElement.TryGetProperty("identifier", out JsonElement identifierElement))
        {
          return identifierElement.GetString() ?? "report-" + Guid.NewGuid().ToString().Substring(0, 8);
        }
        return "report-" + Guid.NewGuid().ToString().Substring(0, 8);
      }
      catch (Exception ex)
      {
        // If parsing fails, generate a unique identifier
        Logger.LogWarning($"Could not extract identifier from request: {ex.Message}");
        return "report-" + Guid.NewGuid().ToString().Substring(0, 8);
      }
    }
    catch (Exception e)
    {
      // Log the full exception details
      Logger.LogError($"Error in SendMessageToAPI: {e.Message}");
      Logger.LogError($"Stack trace: {e.StackTrace}");
      if (e.InnerException != null)
      {
        Logger.LogError($"Inner exception: {e.InnerException.Message}");
      }
      throw;
    }
  }

  private void SendReportToApi(CCSPlayerController player, CCSPlayerController target, string reason)
  {
    string? hostName = ConVar.Find("hostname")?.StringValue;
    ReportInfos infos = new()
    {
      PlayerName = player.PlayerName,
      PlayerSteamId = player.SteamID.ToString(),
      TargetName = target.PlayerName,
      TargetSteamId = target.SteamID.ToString(),
      TargetUserid = target.UserId,
      MapName = Server.MapName
    };

    Task.Run(async () =>
    {
      if (Config.Commands.Report.CanReportPlayerAlreadyReported.Enabled)
      {
        var task1 = await FindReportedPlayer(infos.PlayerSteamId, infos.TargetSteamId, reason);

        if (!string.IsNullOrEmpty(task1) && task1 != "skip")
        {

          if (task1 == "erro")
            SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer["InternalServerError"]}");
          else if (task1 == 1 || task1 == 4)
            SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer["PlayerAlreadyReportedByYourself"]}");
          else if (task1 == 2 || task1 == 3)
            SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer["PlayerAlreadyReported"]}");
          return;
        }
      }
      if (string.IsNullOrEmpty(hostName))
      {
        hostName = "Empty";
      }

      string RandomString(int length)
      {
        Random random = new();
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
      }

      string identifier = RandomString(15);
      
      // Create a simplified payload with just the required information
      var simplifiedPayload = new
      {
        author_name = infos.PlayerName,
        author_steamid = infos.PlayerSteamId,
        target_name = infos.TargetName,
        target_steamid = infos.TargetSteamId,
        reason = reason,
        server_name = hostName,
        server_ip = string.IsNullOrEmpty(Config.ServerIpWithPort) ? "Empty" : Config.ServerIpWithPort,
        map_name = infos.MapName,
        identifier = identifier
      };
      
      var task2 = await SendMessageToAPI(JsonSerializer.Serialize(simplifiedPayload));

      if (task2 == "There was an error sending the data to API" || task2 == "Unable to get response ID")
      {
        SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer["WebhookError"]}");
        return;
      }
      SendMessageToPlayer(player, $"{Localizer["Prefix"]} {Localizer["ReportSent"]}");

      // Always try to insert into database, not based on ReportHandled.Enabled
      var task3 = await
        InsertIntoDatabase(
          infos.PlayerName,
          infos.PlayerSteamId,
          infos.TargetName,
          infos.TargetSteamId,
          reason,
          task2, // Use the identifier as the message_id for database
          identifier,
          hostName,
          string.IsNullOrEmpty(Config.ServerIpWithPort) ? "Empty" : Config.ServerIpWithPort
        );


      if (!task3)
      {
        Logger.LogError($"{Localizer["Prefix"]} {Localizer["InsertIntoDatabaseError"]}");
      }

      if (Config.Commands.Report.MaximumReports.HowShouldBeChecked == -1 || Config.Commands.Report.MaximumReports.ActionToDoWhenMaximumLimitReached <= 0) return;

      ReportedPlayersClass? findReportedPlayer = ReportedPlayers.Find(rp => rp.Player == infos.TargetSteamId);


      if (findReportedPlayer != null)
        findReportedPlayer.Reports += 1;
      else
      {
        ReportedPlayers.Add(new ReportedPlayersClass
        {
          Player = infos.TargetSteamId,
          Reports = 1,
          FirstReport = DateTime.UtcNow
        });
        findReportedPlayer = ReportedPlayers.Find(rp => rp.Player == infos.TargetSteamId);
      }
      if (findReportedPlayer?.Reports >= Config.Commands.Report.MaximumReports.PlayerCanReceiveBeforeAction)
      {
        if (Config.Commands.Report.MaximumReports.HowShouldBeChecked == 0 || (Config.Commands.Report.MaximumReports.HowShouldBeChecked >= 1 && findReportedPlayer.FirstReport.AddMinutes(Config.Commands.Report.MaximumReports.HowShouldBeChecked) >= DateTime.UtcNow))
        {
          Server.NextFrame(() =>
          {
            if (Config.Commands.Report.MaximumReports.ActionToDoWhenMaximumLimitReached == 1)
            {
              Server.ExecuteCommand($"css_kick #{infos.TargetUserid} {Localizer["ReasonToKick"].Value}");
            }
            else if (Config.Commands.Report.MaximumReports.ActionToDoWhenMaximumLimitReached == 2)
            {
              Server.ExecuteCommand($"css_ban #{infos.TargetUserid} {Config.Commands.Report.MaximumReports.IfActionIsBanThenBanForHowManyMinutes} {Localizer["ReasonToBan"].Value}");
            }
          });
        }
        ReportedPlayers.RemoveAll(p => p.Player == infos.TargetSteamId);
      }
    });
  }

  public bool CanExecuteCommand(int playerSlot)
  {
    if (Config.CooldownRefreshCommandSeconds <= 0) return true;
    if (commandCooldown.TryGetValue(playerSlot, out DateTime value))
    {
      if (DateTime.UtcNow >= value)
      {
        commandCooldown[playerSlot] = value.AddSeconds(Config.CooldownRefreshCommandSeconds);
        return true;
      }
      else
      {
        return false;
      }
    }
    else
    {
      commandCooldown.Add(playerSlot, DateTime.UtcNow.AddSeconds(Config.CooldownRefreshCommandSeconds));
      return true;
    }
  }
  public static void SendMessageToPlayer(CCSPlayerController player, string message)
  {
    Server.NextFrame(() => player.PrintToChat(message));
  }
  //thanks to cs2-WeaponPaints
  public class IRemoteVersion
  {
    public required string tag_name { get; set; }
  }
  public void CheckVersion()
  {
    Task.Run(async () =>
    {
      using HttpClient client = new();
      try
      {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CallAdmin");
        HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/1Mack/CS2-CallAdmin/releases/latest");

        if (response.IsSuccessStatusCode)
        {
          IRemoteVersion? toJson = JsonSerializer.Deserialize<IRemoteVersion>(await response.Content.ReadAsStringAsync());

          if (toJson == null)
          {
            Logger.LogWarning("Failed to check version1");
          }
          else
          {
            int comparisonResult = string.Compare(ModuleVersion, toJson.tag_name[1..]);

            if (comparisonResult < 0)
            {
              Logger.LogWarning("Plugin is outdated! Check https://github.com/1Mack/CS2-CallAdmin/releases/latest");
            }
            else if (comparisonResult > 0)
            {
              Logger.LogInformation("Probably dev version detected");
            }
            else
            {
              Logger.LogInformation("Plugin is up to date");
            }
          }

        }
        else
        {
          Logger.LogWarning("Failed to check version2");
        }
      }
      catch (HttpRequestException ex)
      {
        Logger.LogError(ex, "Failed to connect to the version server.");
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "An error occurred while checking version.");
      }
    });
  }
}

