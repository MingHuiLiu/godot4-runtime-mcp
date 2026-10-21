using System.Net;
using System.Text;
using GodotRuntimeMcp.Recording;

namespace GodotRuntimeMcp;

/// <summary>
/// Explicit, offline export of bounded, verified frames into a self-contained
/// static HTML visual timeline. No JS, remote assets, network, or untrusted HTML.
/// </summary>
public sealed class TimelineGalleryExporter
{
    private const int MaxPage = 20;
    private const int MaxHtmlBytes = 18 * 1024 * 1024;

    public TimelineGalleryResult Export(string projectPath, string sessionId,
        int offset = 0, int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > MaxPage)
            throw new ArgumentOutOfRangeException(nameof(limit),
                "gallery_limit: offset>=0 and 1..20 samples");
        var store = new TimelineStore();
        var status = store.Info(projectPath, sessionId);
        var page = store.Events(projectPath, sessionId, offset, limit);
        string directory = store.TimelineDirectory(projectPath, sessionId);
        var html = new StringBuilder(4096);
        html.Append("""
<!doctype html>
<html lang="en">
<head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="referrer" content="no-referrer">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; base-uri 'none'; object-src 'none'; form-action 'none'">
<title>Runtime Black Box — observed timeline</title>
<style>
:root{color-scheme:dark;font-family:system-ui,-apple-system,Segoe UI,sans-serif}
body{background:#0f1420;color:#e2e8f5;max-width:1160px;margin:auto;padding:2rem}
h1{letter-spacing:-.04em;font-size:2rem;margin-bottom:.35rem}
p{color:#aebbd0;line-height:1.6}
code{color:#d0e5ff;overflow-wrap:anywhere}
nav{padding:1rem 1.3rem;border:1px solid #343e52;border-radius:12px;background:#161f31}
.timeline{border-left:2px solid #5978b5;margin:2rem 0 1rem 1rem;padding-left:1.6rem}
.sample{border:1px solid #34425d;background:#182237;border-radius:14px;padding:1.25rem;margin-bottom:1.5rem;position:relative}
.sample:before{content:'';width:13px;height:13px;background:#8db7ff;border-radius:50%;position:absolute;left:-2.12rem;top:1.45rem}
.topline{display:flex;justify-content:space-between;gap:1rem;flex-wrap:wrap}
small,.subtle{color:#aebbd0}
img{max-width:100%;height:auto;display:block;border:1px solid #36455b;border-radius:8px;margin:1rem 0}
.missing{border:1px dashed #61748d;background:#101a2c;padding:2rem;text-align:center;border-radius:8px;margin:1rem 0}
details{margin-top:.65rem;border-top:1px solid #354358;padding-top:.65rem}
summary{cursor:pointer;color:#d4e8ff}
ul{margin:.5rem 0;padding-left:1.25rem}
li{line-height:1.5;margin-bottom:.2rem;overflow-wrap:anywhere}
.pill{color:#a9cdff;font-size:.85rem}
</style></head><body>
<h1>Runtime Black Box</h1>
<p>Observed scene timeline — sampled checkpoints and callback evidence, not video or deterministic replay.</p>
""");
        html.Append("<nav><strong>Session</strong> <code>")
            .Append(H(sessionId))
            .Append("</code><p>Project instance: <code>")
            .Append(H(status.InstanceId))
            .Append("</code> · status: ")
            .Append(H(status.Status))
            .Append(" · total samples: ")
            .Append(page.Total)
            .Append(" · displaying ")
            .Append(page.Items.Count)
            .Append(" starting at ")
            .Append(page.Offset)
            .Append(" · missed sample opportunities: ")
            .Append(status.DroppedSamples)
            .Append(" · dropped callbacks: ")
            .Append(status.DroppedObservedEvents)
            .Append("</p></nav><div class=\"timeline\">");
        int images = 0, unavailable = 0;
        foreach (var sample in page.Items)
        {
            html.Append("<section class=\"sample\" id=\"sample-")
                .Append(sample.SampleIndex)
                .Append("\"><div class=\"topline\"><strong>Sample #")
                .Append(sample.SampleIndex)
                .Append("</strong><span class=\"pill\">Godot process frame ")
                .Append(sample.ObservedProcessFrame)
                .Append("</span></div>");
            html.Append("<p class=\"subtle\">Checkpoint <code>")
                .Append(H(sample.ToSnapshotId))
                .Append("</code> · ")
                .Append(sample.Events.Count)
                .Append(" state differences · ")
                .Append(sample.ObservedEvents?.Count ?? 0)
                .Append(" callback observations</p>");

            if (sample.Screenshot is { State: "captured" })
            {
                byte[] verified = store.ReadScreenshot(projectPath, sessionId, sample);
                html.Append("<img loading=\"lazy\" alt=\"Scene sample ")
                    .Append(sample.SampleIndex)
                    .Append("\" src=\"data:image/png;base64,")
                    .Append(Convert.ToBase64String(verified))
                    .Append("\">");
                images++;
            }
            else
            {
                unavailable++;
                string caption = sample.Screenshot?.Code ??
                    (status.CaptureScreenshots ? "not_available" : "screenshots_disabled");
                html.Append("<div class=\"missing\">No verified screenshot · ")
                    .Append(H(caption))
                    .Append("</div>");
            }
            AppendChanges(html, "Scene changes", sample.Events.Select(e =>
                e.Kind + " — " + e.NodeId + (e.Property is null ? "" : " / " + e.Property) +
                (e.OldText is null ? "" : " · from " + e.OldText) +
                (e.NewText is null ? "" : " · to " + e.NewText)));
            AppendChanges(html, "Observed callbacks", (sample.ObservedEvents ?? []).Select(e =>
                e.Kind + ": " + e.Name + " · " + e.SourcePath +
                " · frame " + e.ProcessFrame));
            if (sample.DroppedSamples > 0 || sample.DroppedObservedEvents > 0)
                html.Append("<p class=\"subtle\">This span missed ")
                    .Append(sample.DroppedSamples)
                    .Append(" sample opportunities and ")
                    .Append(sample.DroppedObservedEvents)
                    .Append(" callbacks.</p>");
            html.Append("</section>");
        }
        html.Append("</div><p>Static, locally generated gallery. No scripts or network requests. ")
            .Append("Screenshots may contain private on-screen content; do not share this file casually.</p></body></html>");
        if (Encoding.UTF8.GetByteCount(html.ToString()) > MaxHtmlBytes)
            throw new InvalidDataException("gallery_limit: self-contained page is too large");
        string dest = Path.Combine(directory, $"gallery-{offset:D6}-{page.Items.Count:D3}.html");
        if (File.Exists(dest) && new FileInfo(dest).LinkTarget is not null)
            throw new UnauthorizedAccessException("unsafe_output_path: linked gallery destination");
        string temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, html.ToString(), new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, dest, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return new TimelineGalleryResult(sessionId, dest, page.Total,
            page.Items.Count, images, unavailable);
    }

    private static string H(string? value) =>
        WebUtility.HtmlEncode(value is null ? "" :
            value.Length > 240 ? value[..240] + "…" : value);

    private static void AppendChanges(StringBuilder html, string title,
        IEnumerable<string> changes)
    {
        string[] items = changes.Take(201).ToArray();
        html.Append("<details><summary>").Append(H(title)).Append(" (")
            .Append(Math.Min(items.Length, 200)).Append(")</summary><ul>");
        foreach (string row in items.Take(200))
            html.Append("<li>").Append(H(row)).Append("</li>");
        if (items.Length > 200)
            html.Append("<li>More events available through the paginated JSON query.</li>");
        html.Append("</ul></details>");
    }
}

public sealed record TimelineGalleryResult(string SessionId, string HtmlPath,
    int Total, int IncludedSamples, int ImageCount, int UnavailableCount);
