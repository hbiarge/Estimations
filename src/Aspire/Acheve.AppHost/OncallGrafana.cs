using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// The on-call Grafana's copies of our dashboards and alert rules, without any mention of chaos or the tools: an agent
// investigating there could read them through the API, and they would tell it the faults are injected (talk E5).
// The files in the grafana folder stay as they are for the main Grafana. The copies are written at every start, so
// changes to the originals reach the on-call Grafana on the next AppHost start.
// When an original changes and a rule below no longer matches, the start fails with the file and the leftover text.
internal static partial class OncallGrafana
{
    // Whole values that exist only to show chaos: replaced by what they are without it.
    private static readonly Dictionary<string, string> Values = new()
    {
        // Overview > Services: "Down (chaos)" (value 2) for a service in the down scenario.
        ["((group by (service_name) (max_over_time(target_info{service_name!~\"otelcol-contrib|trafficgenerator|chaosmonkey\"}[2m]))) unless on (service_name) (group by (service_name) (chaos_scenario_active{chaos_scenario=\"down\"} == 1))) or (((group by (service_name) (max_over_time(target_info{service_name!~\"otelcol-contrib|trafficgenerator|chaosmonkey\"}[2m]))) and on (service_name) (group by (service_name) (chaos_scenario_active{chaos_scenario=\"down\"} == 1))) * 2) or (group by (service_name) (max_over_time(target_info{service_name!~\"otelcol-contrib|trafficgenerator|chaosmonkey\"}[1h])) * 0)"]
            = "(group by (service_name) (max_over_time(target_info{service_name!~\"otelcol-contrib\"}[2m]))) or (group by (service_name) (max_over_time(target_info{service_name!~\"otelcol-contrib\"}[1h])) * 0)",
        ["Services that sent telemetry in the last 2 minutes. A service that stopped shows as Down for an hour. A service running the down chaos scenario fails every request, so it shows as Down (chaos)."]
            = "Services that sent telemetry in the last 2 minutes. A service that stopped shows as Down for an hour.",
        ["Down (chaos)"] = "Down",
        // Edges > Service map: the tools are not in the on-call data, so there is nothing to leave out.
        ["{client!~\"trafficgenerator|chaosmonkey\", server!~\"trafficgenerator|chaosmonkey\"}"] = "{}",
    };

    // Parts of the alert rules file: the Service down rule counts a service in the down scenario as down.
    private static readonly Dictionary<string, string> AlertingParts = new()
    {
        ["              # The tools (traffic generator, chaos monkey) and the LGTM container's own collector are not services.\n              # A service running the down chaos scenario fails every request, so it counts as down too (value 2).\n"]
            = "              # The LGTM container's own collector is not a service.\n",
        ["\n                or\n                (group by (service_name) (chaos_scenario_active{chaos_scenario=\"down\"} == 1) * 2)"] = "",
        ["{{ if eq $values.A.Value 2.0 }}{{ $labels.service_name }} is in the down chaos scenario{{ else }}{{ $labels.service_name }} stopped sending telemetry{{ end }}"]
            = "{{ $labels.service_name }} stopped sending telemetry",
        ["{{ if eq $values.A.Value 2.0 }}The down chaos scenario makes this service fail every request. Switch it back\n            to healthy with its chaos command in the Aspire dashboard, or wait for the chaos monkey's next step.{{ else }}No\n            telemetry"]
            = "No\n            telemetry",
        ["the collector. Check its state and console logs in the Aspire dashboard.{{ end }}"]
            = "the collector. Check its state and console logs in the Aspire dashboard.",
    };

    // Everywhere else the tools are only left out of a query: the on-call data has no tools to leave out.
    private const string ToolsAlternation = "|trafficgenerator|chaosmonkey";

    // Readable output: queries keep their quotes and < > as written.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [GeneratedRegex("(?i)chaos|monkey|trafficgenerator")]
    private static partial Regex Mention();

    /// <summary>
    /// Writes the on-call copies under <paramref name="outputPath"/>: a dashboards folder (every dashboard but Chaos
    /// run) and alerting.yaml. Returns their paths.
    /// </summary>
    public static (string Dashboards, string Alerting) Prepare(string grafanaPath, string outputPath)
    {
        var dashboards = Path.Combine(outputPath, "dashboards");
        if (Directory.Exists(dashboards))
        {
            Directory.Delete(dashboards, recursive: true);
        }
        Directory.CreateDirectory(dashboards);

        foreach (var source in Directory.EnumerateFiles(Path.Combine(grafanaPath, "dashboards"), "*.json"))
        {
            var name = Path.GetFileName(source);
            if (name == "chaos-run.json")
            {
                continue;
            }

            var dashboard = JsonNode.Parse(File.ReadAllText(source))!;
            // Rewrite first: a panel like Overview > Services reads chaos_* only until its query is replaced.
            Clean(dashboard);
            RemoveChaosPanels(dashboard);
            Write(Path.Combine(dashboards, name), dashboard.ToJsonString(JsonOptions));
        }

        var alerting = File.ReadAllText(Path.Combine(grafanaPath, "provisioning", "alerting.yaml")).ReplaceLineEndings("\n");
        foreach (var (part, replacement) in AlertingParts)
        {
            alerting = alerting.Replace(part, replacement);
        }
        var alertingPath = Path.Combine(outputPath, "alerting.yaml");
        Write(alertingPath, alerting.Replace(ToolsAlternation, ""));

        return (dashboards, alertingPath);
    }

    // A panel whose every query still reads a chaos_* metric shows nothing but chaos (Edges > Chaos scenario of ...).
    private static void RemoveChaosPanels(JsonNode node)
    {
        if (node["panels"] is not JsonArray panels)
        {
            return;
        }

        foreach (var panel in panels.ToList())
        {
            var queries = (panel?["targets"] as JsonArray)?
                .Select(t => t?["expr"]?.GetValue<string>())
                .Where(e => e is not null)
                .ToList();

            if (queries is { Count: > 0 } && queries.All(q => q!.Contains("chaos_")))
            {
                panels.Remove(panel);
            }
            else if (panel is not null)
            {
                RemoveChaosPanels(panel); // rows hold their own panels
            }
        }
    }

    // Rewrites every string value in place.
    private static void Clean(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (Cleaned(obj[key]) is { } text)
                    {
                        obj[key] = text;
                    }
                    else if (obj[key] is { } child)
                    {
                        Clean(child);
                    }
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (Cleaned(array[i]) is { } text)
                    {
                        array[i] = text;
                    }
                    else if (array[i] is { } child)
                    {
                        Clean(child);
                    }
                }
                break;
        }
    }

    // The cleaned text of a string value, or null for anything else.
    private static string? Cleaned(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
            ? Values.TryGetValue(text, out var replacement) ? replacement : text.Replace(ToolsAlternation, "")
            : null;

    private static void Write(string path, string content)
    {
        var leftover = Mention().Matches(content).Select(m => LineAround(content, m.Index)).Distinct().ToList();
        if (leftover.Count > 0)
        {
            throw new InvalidOperationException(
                $"The on-call copy {path} still mentions chaos or the tools; update OncallGrafana.cs for the change in the original:\n" +
                string.Join("\n", leftover));
        }

        File.WriteAllText(path, content);
    }

    private static string LineAround(string content, int index)
    {
        var start = content.LastIndexOf('\n', index) + 1;
        var end = content.IndexOf('\n', index);
        return content[start..(end < 0 ? content.Length : end)].Trim();
    }
}
