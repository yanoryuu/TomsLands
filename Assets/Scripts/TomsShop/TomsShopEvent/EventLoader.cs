using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

public class EventLoader : MonoBehaviour
{
    private const string sheetUrl = "https://opensheet.elk.sh/YOUR_SPREADSHEET_ID/Sheet1";

    public async Task<List<TomsEvent>> LoadEventsAsync()
    {
        UnityWebRequest req = UnityWebRequest.Get(sheetUrl);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Failed to load sheet: " + req.error);
            return null;
        }

        var rawList = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(req.downloadHandler.text);
        return ConvertToStructuredEvents(rawList);
    }

    private List<TomsEvent> ConvertToStructuredEvents(List<Dictionary<string, object>> raws)
    {
        var result = new List<TomsEvent>();

        foreach (var raw in raws)
        {
            var e = new TomsEvent
            {
                id = raw.ContainsKey("id") ? raw["id"].ToString() : "",
                title = raw.ContainsKey("title") ? raw["title"].ToString() : "",
                description = raw.ContainsKey("description") ? raw["description"].ToString() : "",
                commands = new List<TomsEventCommand>()
            };

            for (int i = 1; i <= 10; i++)
            {
                string cKey = $"command{i}";
                if (!raw.ContainsKey(cKey) || string.IsNullOrWhiteSpace(raw[cKey]?.ToString())) continue;

                var cmd = new TomsEventCommand { command = raw[cKey].ToString(), parameters = new() };

                string pKey = $"param{i}Key1";
                string pVal = $"param{i}Value1";
                if (raw.ContainsKey(pKey) && raw.ContainsKey(pVal))
                {
                    string key = raw[pKey]?.ToString();
                    string val = raw[pVal]?.ToString();
                    if (!string.IsNullOrEmpty(key)) cmd.parameters[key] = val;
                }

                e.commands.Add(cmd);
            }

            result.Add(e);
        }

        return result;
    }
}