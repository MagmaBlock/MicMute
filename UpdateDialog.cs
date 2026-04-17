using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace MicMute;

/// <summary>
/// Manual update checker — no telemetry, no background requests.
/// User clicks the button, we check GitHub once, download if needed.
/// </summary>
internal sealed class UpdateDialog : Form
{
    private static readonly HttpClient _http = CreateHttpClient();

    private readonly Label _lblStatus;
    private readonly Label _lblDetail;
    private readonly Panel _progressOuter;
    private readonly Panel _progressFill;
    private readonly Button _btnAction;
    private readonly Button _btnCancel;
    private CancellationTokenSource _cts;

    private string _remoteVersion;
    private string _downloadUrl;
    private string _hashFileUrl;

    private readonly Font _boldFont;
    private readonly Font _italicFont;

    private readonly System.Windows.Forms.Timer _marqueeTimer;
    private int _marqueePos;
    private bool _marqueeForward = true;

    private const string AppName = "MicMute";
    private const string GitHubRepo = "itsnateai/MicMute";

    // Defense-in-depth size caps — prevent OOM/disk-fill if an attacker-
    // controlled release ever serves a pathologically large response.
    // Legitimate values are much smaller than these ceilings.
    private const long MaxJsonBytes = 1_048_576;        //  1 MB for GitHub API JSON
    private const long MaxHashFileBytes = 65_536;       // 64 KB for SHA256SUMS
    private const long MaxExeBytes = 209_715_200;       // 200 MB for MicMute.exe

    public UpdateDialog()
    {
        Text = $"{AppName} — Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 180);

        _boldFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _italicFont = new Font("Segoe UI", 7.5f, FontStyle.Italic);

        _lblStatus = new Label
        {
            Text = "Checking GitHub for new version...",
            Location = new Point(20, 20),
            Size = new Size(370, 24),
            Font = _boldFont,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_lblStatus);

        _lblDetail = new Label
        {
            Text = "",
            Location = new Point(20, 48),
            Size = new Size(370, 20),
            ForeColor = SystemColors.GrayText,
            Font = _italicFont,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_lblDetail);

        _progressOuter = new Panel
        {
            Location = new Point(30, 80),
            Size = new Size(350, 18),
            BackColor = SystemColors.ControlDark,
            BorderStyle = BorderStyle.None
        };
        _progressFill = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(0, 18),
            BackColor = Color.FromArgb(76, 175, 80)
        };
        _progressOuter.Controls.Add(_progressFill);
        Controls.Add(_progressOuter);

        _btnAction = new Button
        {
            Text = "Upgrade Now",
            Location = new Point(155, 112),
            Size = new Size(110, 32),
            Visible = false
        };
        _btnAction.Click += OnActionClick;
        Controls.Add(_btnAction);

        _btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(295, 112),
            Size = new Size(80, 32)
        };
        _btnCancel.Click += (_, _) =>
        {
            _cts?.Cancel();
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(_btnCancel);

        _marqueeTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _marqueeTimer.Tick += (_, _) =>
        {
            const int step = 4, barW = 80;
            if (_marqueeForward) _marqueePos += step; else _marqueePos -= step;
            if (_marqueePos + barW >= _progressOuter.Width) _marqueeForward = false;
            if (_marqueePos <= 0) _marqueeForward = true;
            _progressFill.Location = new Point(_marqueePos, 0);
            _progressFill.Size = new Size(barW, 18);
        };

        Shown += async (_, _) => await CheckForUpdateAsync();
    }

    private static HttpClient CreateHttpClient()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppName, version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    // ─── Check GitHub ───────────────────────────────────────────

    private async Task CheckForUpdateAsync()
    {
        _cts = new CancellationTokenSource();

        if (IsWingetManaged())
        {
            _marqueeTimer.Stop();
            _progressOuter.Visible = false;
            _lblStatus.Text = "Managed by winget";
            _lblDetail.Text = "Use:  winget upgrade itsnateai.MicMute";
            _btnAction.Visible = false;
            _btnCancel.Text = "OK";
            _btnCancel.Location = new Point(170, 112);
            return;
        }

        _marqueeTimer.Start();

        try
        {
            var response = await _http.GetAsync(
                $"https://api.github.com/repos/{GitHubRepo}/releases/latest",
                HttpCompletionOption.ResponseHeadersRead,
                _cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var vals)
                    ? vals.FirstOrDefault() : null;
                ShowError(remaining == "0"
                    ? "GitHub API rate limit reached." : "GitHub API access denied (403).",
                    remaining == "0" ? "Try again in a few minutes." : "Check your network connection.");
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                ShowError("No releases found on GitHub.", "The repository may not have any published releases.");
                return;
            }

            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long jsonLen && jsonLen > MaxJsonBytes)
            {
                ShowError("Unexpected response from GitHub.", "Release metadata is larger than expected.");
                return;
            }

            var json = await ReadBoundedStringAsync(response, MaxJsonBytes, _cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _remoteVersion = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Equals("MicMute.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        _downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    }
                    if (name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        _hashFileUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    }
                }
            }

            if (string.IsNullOrEmpty(_downloadUrl))
            {
                ShowError("No update package found in the latest release.", "The release may be incomplete.");
                return;
            }

            ShowVersionComparison();
        }
        catch (TaskCanceledException)
        {
            if (_cts?.IsCancellationRequested != true)
                ShowError("Request timed out.", "Check your internet connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            ShowError("Could not reach GitHub.", ex.Message);
        }
        catch (JsonException)
        {
            ShowError("Unexpected response from GitHub.", "The API response format may have changed.");
        }
        catch (Exception ex)
        {
            ShowError("Update check failed.", ex.Message);
        }
    }

    // ─── Compare Versions ───────────────────────────────────────

    private void ShowVersionComparison()
    {
        _marqueeTimer.Stop();
        _progressFill.Size = new Size(0, 18);
        _progressFill.Location = new Point(0, 0);

        var localVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var isNewer = Version.TryParse(_remoteVersion, out var remote)
                   && Version.TryParse(localVersion, out var local)
                   && remote > local;

        _lblDetail.Text = $"Current: {localVersion}  →  GitHub: {_remoteVersion}";
        _progressOuter.Visible = false;

        if (isNewer)
        {
            _lblStatus.Text = "A new version is available!";
            _btnAction.Text = "Upgrade Now";
            _btnAction.Visible = true;
            _btnCancel.Text = "Cancel";
        }
        else
        {
            _lblStatus.Text = "You're on the latest version!";
            _btnAction.Visible = false;
            _btnCancel.Text = "OK";
            _btnCancel.Location = new Point(170, 112);
        }
    }

    // ─── Download & Apply ───────────────────────────────────────

    private async void OnActionClick(object sender, EventArgs e)
    {
        _btnAction.Enabled = false;
        _btnCancel.Text = "Cancel";
        _progressOuter.Visible = true;
        _progressFill.Location = new Point(0, 0);
        _lblStatus.Text = $"Downloading {AppName} {_remoteVersion}...";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        // Capture once — if the field is reassigned by a concurrent click sequence,
        // our awaits stay pinned to the token we started with.
        var ct = _cts.Token;
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine executable path.");
        var newPath = exePath + ".new";
        var oldPath = exePath + ".old";

        try
        {
            // Validate download URL origin before fetching
            if (!IsAllowedReleaseOrigin(_downloadUrl))
            {
                ShowError("Update failed: download URL is not from the expected source.", _downloadUrl ?? "(null)");
                return;
            }

            if (!await DownloadFileAsync(_downloadUrl!, newPath, ct))
                return;

            // Verify SHA256 hash if the release includes a SHA256SUMS file
            if (!string.IsNullOrEmpty(_hashFileUrl))
            {
                // Apply the same origin allowlist to the checksum file. Both
                // URLs come from the GitHub releases API today, but verifying
                // both halves keeps the self-update pipeline belt-and-suspenders.
                if (!IsAllowedReleaseOrigin(_hashFileUrl))
                {
                    TryDelete(newPath);
                    ShowError("Update integrity check failed.",
                        "Checksum file URL is not from the expected source.");
                    return;
                }

                _lblStatus.Text = "Verifying integrity...";
                try
                {
                    using var hashResponse = await _http.GetAsync(
                        _hashFileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    hashResponse.EnsureSuccessStatusCode();
                    if (hashResponse.Content.Headers.ContentLength is long hashLen && hashLen > MaxHashFileBytes)
                    {
                        TryDelete(newPath);
                        ShowError("Update integrity check failed.",
                            "Checksum file is larger than expected.");
                        return;
                    }
                    var hashContent = await ReadBoundedStringAsync(hashResponse, MaxHashFileBytes, ct);
                    string expectedHash = null;
                    foreach (var line in hashContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        // Format: "hexhash  filename" or "hexhash *filename"
                        var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 &&
                            parts[1].Trim().TrimStart('*').Equals("MicMute.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            expectedHash = parts[0].Trim();
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        var actualHash = ComputeFileHash(newPath);
                        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                        {
                            TryDelete(newPath);
                            ShowError("Hash verification failed.",
                                "The downloaded file doesn't match the expected SHA256 checksum.");
                            return;
                        }
                    }
                    else
                    {
                        TryDelete(newPath);
                        ShowError("Hash verification failed.",
                            "SHA256SUMS file found but contains no entry for MicMute.exe.");
                        return;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Fail-closed: if a SHA256SUMS URL was advertised on the release,
                    // any verify failure (network blip, parse error, hash mismatch reader error)
                    // must abort the update rather than silently installing an unverified binary.
                    Log.Error("SHA256 verification failed during update", ex);
                    TryDelete(newPath);
                    ShowError("Update integrity check failed.",
                        "Could not verify the downloaded file. Try again, or download manually from GitHub.");
                    return;
                }
            }

            _lblStatus.Text = "Applying update...";
            _progressOuter.Visible = false;

            TryDelete(oldPath);
            if (File.Exists(exePath))
                File.Move(exePath, oldPath);
            File.Move(newPath, exePath);

            using var _ = Process.Start(new ProcessStartInfo(exePath)
            {
                Arguments = "--after-update",
                UseShellExecute = true
            });
            Application.Exit();
        }
        catch (IOException ex)
        {
            Log.Error("Update apply failed (IO)", ex);
            RollbackUpdate(exePath, oldPath, newPath);

            ShowError(
                ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                    ? "Cannot replace the executable." : "Failed to apply update.",
                ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                    ? "Your antivirus may be locking the file. Try again." : ex.Message);
        }
        catch (TaskCanceledException)
        {
            RollbackUpdate(exePath, oldPath, newPath);
            if (!IsDisposed) ShowVersionComparison();
        }
        catch (Exception ex)
        {
            Log.Error("Update apply failed", ex);
            RollbackUpdate(exePath, oldPath, newPath);
            if (!IsDisposed) ShowError("Update failed.", ex.Message);
        }
    }

    private static void RollbackUpdate(string exePath, string oldPath, string newPath)
    {
        if (File.Exists(oldPath))
        {
            TryDelete(exePath);
            try { File.Move(oldPath, exePath); }
            catch (Exception ex) { Log.Error("Rollback failed to restore old exe — user may need to reinstall", ex); }
        }
        TryDelete(newPath);
    }

    private async Task<bool> DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        if (totalBytes > MaxExeBytes)
        {
            ShowError("Update package is too large.",
                      $"Server reported {totalBytes:N0} bytes; max allowed {MaxExeBytes:N0}.");
            return false;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            downloaded += read;
            if (downloaded > MaxExeBytes)
            {
                await fileStream.DisposeAsync();
                TryDelete(destPath);
                ShowError("Update package is too large.",
                          $"Download exceeded {MaxExeBytes:N0} bytes before completing.");
                return false;
            }
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);

            if (totalBytes > 0 && !IsDisposed) BeginInvoke(() =>
            {
                if (IsDisposed) return;
                int pct = (int)(downloaded * 100 / totalBytes);
                _progressFill.Size = new Size(
                    (int)(_progressOuter.Width * downloaded / totalBytes), 18);
                var dlMB = downloaded / (1024.0 * 1024.0);
                var totalMB = totalBytes / (1024.0 * 1024.0);
                _lblDetail.Text = totalMB < 1
                    ? $"{pct}% ({downloaded / 1024.0:F0} / {totalBytes / 1024.0:F0} KB)"
                    : $"{pct}% ({dlMB:F0} / {totalMB:F0} MB)";
            });
        }

        if (totalBytes > 0 && downloaded != totalBytes)
        {
            TryDelete(destPath);
            ShowError("Download was incomplete.",
                      $"Expected {totalBytes:N0} bytes, got {downloaded:N0}.");
            return false;
        }

        // Minimum size sanity check for self-contained .NET exe
        if (downloaded < 1_000_000)
        {
            TryDelete(destPath);
            ShowError("Downloaded file is too small.",
                      $"Got {downloaded:N0} bytes — expected a valid executable.");
            return false;
        }

        return true;
    }

    // ─── Error ──────────────────────────────────────────────────

    private void ShowError(string message, string detail)
    {
        _marqueeTimer.Stop();
        _progressOuter.Visible = false;
        _lblStatus.Text = message;
        _lblStatus.ForeColor = Color.FromArgb(255, 152, 0);
        _lblDetail.Text = detail;
        _btnAction.Visible = false;
        _btnCancel.Text = "OK";
        _btnCancel.Location = new Point(170, 112);
    }

    // ─── Static Helpers (called from Program.cs) ────────────────

    /// <summary>Returns true if the app is installed via winget (portable package).</summary>
    /// <remarks>
    /// User-scope installs:    %LOCALAPPDATA%\Microsoft\WinGet\Packages\...
    /// Machine-scope installs: %ProgramFiles%\WinGet\Packages\...
    /// The narrower prefix `Microsoft\WinGet\Packages` misses machine-scope.
    /// Match just `\WinGet\Packages\` so both flavors are detected.
    /// </remarks>
    internal static bool IsWingetManaged() =>
        (Environment.ProcessPath ?? "").Contains(@"\WinGet\Packages\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Clean up .old/.new artifacts from a previous update.</summary>
    internal static void CleanupUpdateArtifacts()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        // Torn-state recovery: if update was interrupted between moving exe→.old
        // and .new→exe, the exe is gone but .old still has the previous version.
        if (!File.Exists(exePath))
        {
            var oldPath = exePath + ".old";
            if (File.Exists(oldPath))
            {
                try { File.Move(oldPath, exePath); }
                catch (Exception ex) { Log.Error("Torn-state recovery failed — exe missing and .old restore failed", ex); }
            }
            return;
        }

        foreach (var suffix in new[] { ".old", ".new" })
        {
            var path = exePath + suffix;
            if (!File.Exists(path)) continue;
            try { File.Delete(path); } catch { /* will be cleaned on next launch */ }
        }
    }

    /// <summary>Show a brief floating toast near the system tray after a successful update.</summary>
    internal static void ShowUpdateToast()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();

            var toast = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                TopMost = true,
                StartPosition = FormStartPosition.Manual,
                BackColor = Color.FromArgb(240, 240, 240),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 8, 12, 8)
            };
            var toastFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            var lbl = new Label
            {
                Text = $"\u2705 {AppName} updated to v{version}!",
                AutoSize = true,
                Font = toastFont,
                ForeColor = Color.FromArgb(30, 30, 30)
            };
            toast.Controls.Add(lbl);
            toast.FormClosed += (_, _) => toastFont.Dispose();

            var screen = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            toast.Load += (_, _) =>
                toast.Location = new Point(screen.Right - toast.Width - 20, screen.Bottom - toast.Height - 20);
            toast.Show();

            var dismiss = new System.Windows.Forms.Timer { Interval = 5000 };
            dismiss.Tick += (_, _) =>
            {
                dismiss.Stop();
                dismiss.Dispose();
                toast.Close();
            };
            dismiss.Start();
        };
        timer.Start();
    }

    // ─── Helpers ────────────────────────────────────────────────

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool IsAllowedReleaseOrigin(string url) =>
        !string.IsNullOrEmpty(url) &&
        (url.StartsWith("https://github.com/itsnateai/", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("https://objects.githubusercontent.com/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the response body as a string but caps the total bytes read.
    /// Content-Length is checked in the caller; this guards against
    /// servers that omit Content-Length (chunked transfer) from sending
    /// an unbounded stream.
    /// </summary>
    private static async Task<string> ReadBoundedStringAsync(
        HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new IOException($"Response exceeded {maxBytes:N0} bytes.");
            ms.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _italicFont?.Dispose();
            _marqueeTimer.Stop();
            _marqueeTimer.Dispose();
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            try { _cts?.Dispose(); } catch (ObjectDisposedException) { }
        }
        base.Dispose(disposing);
    }
}
