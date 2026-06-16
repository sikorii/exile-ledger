using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal static class UpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/sikorii/exile-ledger/releases";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static void CheckForUpdatesAsync(Form owner)
    {
        _ = CheckForUpdatesCoreAsync(owner);
    }

    private static async Task CheckForUpdatesCoreAsync(Form owner)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(Timeout);
            var currentVersion = GetCurrentVersion();
            var latestRelease = await GetLatestReleaseAsync(cancellation.Token).ConfigureAwait(false);
            if (latestRelease is null ||
                !TryGetVersionParts(currentVersion, out var currentParts) ||
                !TryGetVersionParts(latestRelease.TagName, out var latestParts) ||
                CompareVersionParts(latestParts, currentParts) <= 0)
            {
                return;
            }

            ShowUpdateAvailable(owner, currentVersion, NormalizeVersionText(latestRelease.TagName), latestRelease.HtmlUrl);
        }
        catch
        {
            // Update checks are best-effort only; never interrupt app startup.
        }
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = Timeout };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ExileLedger");
        await using var stream = await httpClient.GetStreamAsync(ReleasesApiUrl, cancellationToken).ConfigureAwait(false);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken).ConfigureAwait(false);

        return releases?
            .Where(release => !release.Draft && !string.IsNullOrWhiteSpace(release.TagName) && !string.IsNullOrWhiteSpace(release.HtmlUrl))
            .OrderByDescending(release => release.PublishedAt ?? release.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "unknown";
    }

    private static void ShowUpdateAvailable(Form owner, string currentVersion, string latestVersion, string releaseUrl)
    {
        if (owner.IsDisposed)
        {
            return;
        }

        void Show()
        {
            try
            {
                if (owner.IsDisposed)
                {
                    return;
                }

                using var dialog = CreateUpdateDialog(owner, currentVersion, latestVersion);
                if (dialog.ShowDialog(owner) == DialogResult.OK)
                {
                    OpenReleasePage(releaseUrl);
                }
            }
            catch
            {
            }
        }

        try
        {
            if (owner.InvokeRequired)
            {
                owner.BeginInvoke((Action)Show);
            }
            else
            {
                Show();
            }
        }
        catch
        {
        }
    }

    private static Form CreateUpdateDialog(Form owner, string currentVersion, string latestVersion)
    {
        var dialog = new Form
        {
            Text = "Update available",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(430, 205),
            Font = owner.Font
        };

        var message = new Label
        {
            AutoSize = false,
            Left = 18,
            Top = 18,
            Width = 394,
            Height = 120,
            Text = "A newer Exile Ledger build is available." + Environment.NewLine +
                Environment.NewLine +
                $"Current version: {currentVersion}" + Environment.NewLine +
                $"Latest version: {latestVersion}" + Environment.NewLine +
                Environment.NewLine +
                "Open the release page to download it?"
        };

        var openButton = new Button
        {
            Text = "Open release page",
            DialogResult = DialogResult.OK,
            Left = 178,
            Top = 155,
            Width = 135,
            Height = 30
        };

        var laterButton = new Button
        {
            Text = "Later",
            DialogResult = DialogResult.Cancel,
            Left = 323,
            Top = 155,
            Width = 88,
            Height = 30
        };

        dialog.Controls.Add(message);
        dialog.Controls.Add(openButton);
        dialog.Controls.Add(laterButton);
        dialog.AcceptButton = openButton;
        dialog.CancelButton = laterButton;

        return dialog;
    }

    private static void OpenReleasePage(string releaseUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static string NormalizeVersionText(string version)
    {
        return version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? version[1..]
            : version;
    }

    private static bool TryGetVersionParts(string version, out int[] parts)
    {
        version = NormalizeVersionText(version).Trim();
        var values = new List<int>();
        var index = 0;
        while (index < version.Length)
        {
            if (!char.IsDigit(version[index]))
            {
                break;
            }

            var start = index;
            while (index < version.Length && char.IsDigit(version[index]))
            {
                index++;
            }

            if (!int.TryParse(version[start..index], out var value))
            {
                parts = [];
                return false;
            }

            values.Add(value);
            if (index >= version.Length || version[index] != '.')
            {
                break;
            }

            index++;
        }

        parts = values.ToArray();
        return parts.Length > 0;
    }

    private static int CompareVersionParts(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        var length = Math.Max(left.Count, right.Count);
        for (var i = 0; i < length; i++)
        {
            var leftValue = i < left.Count ? left[i] : 0;
            var rightValue = i < right.Count ? right[i] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);
}
