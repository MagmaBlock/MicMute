using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

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
    private readonly TableLayoutPanel _root;
    private const int DesignW = 360;       // 96-DPI design client WIDTH; height is fit to content in OnLoad
    private const int ProgressBarW = 312;  // progress track width (centered in the 360-wide form)
    private const int ProgressBarH = 18;   // progress track height; also the reserved (Absolute) progress-row height
    private CancellationTokenSource _cts;

    // In-flight gate — prevents parallel update chains on rapid double-click (A7-F15).
    private int _inFlight;

    private string _remoteVersion;
    private string _downloadUrl;
    private string _hashFileUrl;

    private readonly Font _boldFont;
    private readonly Font _italicFont;

    private readonly System.Windows.Forms.Timer _marqueeTimer;
    private int _marqueePos;
    private bool _marqueeForward = true;

    // Toast timer stored at class scope so it can be disposed on ApplicationExit (A1-F05).
    private static System.Windows.Forms.Timer _toastOuterTimer;

    private const string AppName = "MicMute";
    private const string GitHubRepo = "MagmaBlock/MicMute";

    // Defense-in-depth size caps — prevent OOM/disk-fill if an attacker-
    // controlled release ever serves a pathologically large response.
    // Legitimate values are much smaller than these ceilings.
    private const long MaxJsonBytes = 1_048_576;        //  1 MB for GitHub API JSON
    private const long MaxHashFileBytes = 65_536;       // 64 KB for SHA256SUMS
    private const long MaxExeBytes = 209_715_200;       // 200 MB for MicMute.exe

    // First version tag that emits a SHA256SUMS release asset (BUG-001 / A7-F01).
    // For any remote version >= this, a missing SHA256SUMS is treated as a
    // supply-chain error and the update is aborted. Older releases are
    // grandfathered so existing users can still self-update to this version.
    private static readonly Version FIRST_HASH_EMITTING_VERSION = new Version(2, 1, 10);

    public UpdateDialog()
    {
        Text = $"{AppName} — Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        BackColor = Theme.BgColor;
        ForeColor = Theme.FgColor;
        // DPI scaling is explicit (OnLoad: UiLayout.ApplyDpi + a device-scaled ClientSize) —
        // AutoScaleMode.Dpi under PerMonitorV2 left this dialog's frame + fixed controls
        // unscaled at 150% (see SettingsDialog for the full rationale). None = no framework
        // scaling; point-fonts still grow, and ApplyDpi scales every pixel literal.
        AutoScaleMode = AutoScaleMode.None;
        // Placeholder client size — OnLoad pins the width to the DPI-scaled DesignW and fits
        // the height to the laid-out content, so the window ends up exactly content-tall with
        // no dead band at any DPI. See OnLoad.
        ClientSize = new Size(DesignW, 150);

        _boldFont = new Font(UiTokens.PrimaryFont, 9.5f, FontStyle.Bold);
        _italicFont = new Font(UiTokens.PrimaryFont, 7.5f, FontStyle.Italic);

        // Relational layout — a single-column table fills the dialog; each row centers its
        // content (Anchor=None). No absolute positions; OnLoad's UiLayout.ApplyDpi scales the
        // fixed progress/button sizes + the client size together at 125%/150%.
        _lblStatus = new Label
        {
            Text = "Checking GitHub for new version...",
            AutoSize = true,
            Font = _boldFont,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 6),
        };

        _lblDetail = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Theme.DimColor,
            Font = _italicFont,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 10),
        };

        // The progress track lives in a fixed-height (reserved) row — see _root below — so the
        // dialog is the same height whether the bar is shown (checking/downloading) or hidden
        // (the resting "latest version"/error/winget states). The button never jumps and no
        // dead band opens up when the bar disappears.
        _progressOuter = new Panel
        {
            Size = new Size(ProgressBarW, ProgressBarH),
            BackColor = Theme.EditBgColor,
            BorderStyle = BorderStyle.None,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0),
        };
        _progressFill = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(0, ProgressBarH),
            BackColor = UiTokens.SuccessGreen,
        };
        _progressOuter.Controls.Add(_progressFill);

        _btnAction = Fields.Button("Upgrade Now", UiTokens.BtnWideWidth);
        _btnAction.Visible = false;
        _btnAction.Click += OnActionClick;

        _btnCancel = Fields.Action("Cancel");
        _btnCancel.Click += (_, _) =>
        {
            _cts?.Cancel();
            DialogResult = DialogResult.Cancel;
            Close();
        };

        // Buttons live in a centered flow — toggling _btnAction.Visible re-centers the
        // remaining Cancel/OK automatically (a FlowLayoutPanel lays out only visible
        // children), so no dynamic Location math is needed.
        _btnAction.Margin = new Padding(0, 0, UiTokens.BtnGap, 0);
        _btnCancel.Margin = new Padding(0);
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.None,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0),   // small gap above the button row (replaces the old spacer)
        };
        buttons.Controls.Add(_btnAction);
        buttons.Controls.Add(_btnCancel);

        // Dock=Top + AutoSize so OnLoad can fit the form's ClientSize to the laid-out content
        // height (MicMute's content-fit convention — see SettingsDialog.OnLoad). The pre-fix
        // layout had a 5th Percent(100) spacer row that, inside a fixed 180px-tall form,
        // stretched to fill all leftover space and pushed the buttons to the very bottom —
        // leaving a large dead band under the two status lines (the "too large / dispro-
        // portionate" report). The four rows now stack tight from the top, and the progress
        // row is Absolute so it reserves its slot even when the bar is hidden (stable height,
        // no button jump between states).
        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Padding = new Padding(16, 16, 16, 16),
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100f) },
            RowStyles =
            {
                new RowStyle(SizeType.AutoSize),                // status
                new RowStyle(SizeType.AutoSize),                // detail
                new RowStyle(SizeType.Absolute, ProgressBarH),  // progress (reserved slot)
                new RowStyle(SizeType.AutoSize),                // buttons
            },
        };
        _root.Controls.Add(_lblStatus, 0, 0);
        _root.Controls.Add(_lblDetail, 0, 1);
        _root.Controls.Add(_progressOuter, 0, 2);
        _root.Controls.Add(buttons, 0, 3);
        Controls.Add(_root);

        _marqueeTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _marqueeTimer.Tick += (_, _) =>
        {
            // Scaled to the device so the bouncing bar stays proportional at 125%/150%.
            int step = LogicalToDeviceUnits(4), barW = LogicalToDeviceUnits(80);
            if (_marqueeForward) _marqueePos += step; else _marqueePos -= step;
            if (_marqueePos + barW >= _progressOuter.Width) _marqueeForward = false;
            if (_marqueePos <= 0) _marqueeForward = true;
            _progressFill.Location = new Point(_marqueePos, 0);
            _progressFill.Size = new Size(barW, _progressOuter.Height);
        };

        Shown += async (_, _) =>
        {
#if DEBUG
            // The DPI render harness seeds a settled state via DiagPopulate and captures
            // layout only — skip the live GitHub check so the capture is deterministic
            // (and doesn't depend on network / the air-gapped test VM).
            if (DiagRender.Active) return;
#endif
            await CheckForUpdateAsync();
        };
    }

#if DEBUG
    /// <summary>
    /// Render-harness only: force the settled "you're on the latest version" resting state
    /// (the surface in the size-tightening report) so the rebuilt content-fit layout is
    /// captured deterministically at the test DPI. Mirrors CapsNumTray.UpdateDialog.DiagPopulate.
    /// </summary>
    internal void DiagPopulate()
    {
        _marqueeTimer.Stop();
        _lblStatus.Text = "You're on the latest version!";
        _lblStatus.ForeColor = Theme.FgColor;
        _lblDetail.Text = "Current: 2.3.0  →  GitHub: 2.3.0";
        _progressOuter.Visible = false;
        _btnAction.Visible = false;
        _btnCancel.Text = "OK";
    }

    /// <summary>
    /// Render-harness only: drive the post-show transition to the longest real error state. Called
    /// AFTER Show (so OnLoad has already fit the window to the short initial state) — it exercises
    /// the real "short at load, long later" path and verifies FitToContentHeight grows the window
    /// so the wrapped 2-line message doesn't clip the OK button.
    /// </summary>
    internal void DiagShowLongError()
        => ShowError("Update integrity file missing. Download manually from GitHub.",
                     "SHA256SUMS was not found in release 2.3.1. Aborting for security.");
#endif

    private static HttpClient CreateHttpClient()
    {
        // Disable automatic redirects so every hop's Location header can be
        // revalidated against the allowlist. Default .NET behaviour would
        // follow up to 50 redirects silently — and IsAllowedReleaseOrigin
        // would only have seen the initial URL, so a compromised edge/bucket
        // could 302 to an attacker host and slip past the check.
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppName, version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    // Max redirect hops — legitimate GitHub → objects.githubusercontent.com
    // is 1 hop. 5 is enough slack for regional CDN or tag-to-release rewrites
    // while keeping a compromised release from pointing at an attacker chain.
    private const int MaxRedirectHops = 5;

    /// <summary>
    /// Wrapper around `_http.GetAsync` that manually follows 3xx redirects
    /// and re-validates every `Location` header against the allowlist.
    /// Throws HttpRequestException if the hop limit is exceeded or if any
    /// intermediate URL falls outside the allowed origins.
    /// </summary>
    private static async Task<HttpResponseMessage> GetWithValidatedRedirectsAsync(
        string url, HttpCompletionOption completionOption, CancellationToken ct)
    {
        if (!IsAllowedReleaseOrigin(url))
            throw new HttpRequestException($"URL is not from an allowed origin: {url}");

        string current = url;
        for (int hop = 0; hop <= MaxRedirectHops; hop++)
        {
            var response = await _http.GetAsync(current, completionOption, ct);
            int status = (int)response.StatusCode;
            if (status < 300 || status >= 400)
                return response; // non-redirect — caller owns the response

            var location = response.Headers.Location;
            response.Dispose();
            if (location == null)
                throw new HttpRequestException($"Redirect from {current} had no Location header");

            Uri nextUri = location.IsAbsoluteUri
                ? location
                : new Uri(new Uri(current), location);
            string next = nextUri.ToString();
            if (!IsAllowedReleaseOrigin(next))
                throw new HttpRequestException($"Redirect target is not from an allowed origin: {next}");
            current = next;
        }
        throw new HttpRequestException($"Too many redirects (>{MaxRedirectHops}) for {url}");
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
            FitToContentHeight();
            return;
        }

        _marqueeTimer.Start();

        try
        {
            var response = await GetWithValidatedRedirectsAsync(
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

            // A7-F19: empty tag_name means the release is malformed — abort rather than
            // continuing with an empty version string that would silently pass comparisons.
            if (string.IsNullOrEmpty(_remoteVersion))
            {
                ShowError("Could not read version from GitHub release.", "The release tag may be missing or malformed.");
                return;
            }

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
        _progressFill.Size = new Size(0, _progressOuter.Height);
        _progressFill.Location = new Point(0, 0);

        var localVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        // A7-F06: if either TryParse fails, show neutral status rather than
        // falsely claiming "You're on the latest version!" when we can't compare.
        if (!Version.TryParse(_remoteVersion, out var remote) ||
            !Version.TryParse(localVersion, out var local))
        {
            _lblDetail.Text = $"Current: {localVersion}  →  GitHub: {_remoteVersion}";
            _progressOuter.Visible = false;
            ShowError("Could not compare versions.", "Try again or check the GitHub releases page manually.");
            return;
        }

        bool isNewer = remote > local;

        _lblDetail.Text = $"Current: {localVersion}  →  GitHub: {_remoteVersion}";
        _progressOuter.Visible = false;

        if (isNewer)
        {
            _lblStatus.Text = "A new version is available!";
            _lblStatus.ForeColor = Theme.FgColor;
            _btnAction.Text = "Upgrade Now";
            _btnAction.Visible = true;       // the centered button flow shows [Upgrade Now][Cancel]
            _btnCancel.Text = "Cancel";
        }
        else
        {
            _lblStatus.Text = "You're on the latest version!";
            _lblStatus.ForeColor = Theme.FgColor;
            _btnAction.Visible = false;       // flow re-centers on the lone OK button
            _btnCancel.Text = "OK";
        }

        FitToContentHeight();   // re-fit in case a neutral-status fallback wrapped the detail line
    }

    // ─── Download & Apply ───────────────────────────────────────

    private async void OnActionClick(object sender, EventArgs e)
    {
        // A7-F15: prevent parallel update chains on rapid double-click.
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            return;

        try
        {
            await DoUpdateAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    private async Task DoUpdateAsync()
    {
        _btnAction.Enabled = false;
        _btnCancel.Text = "Cancel";
        _progressOuter.Visible = true;
        _progressFill.Location = new Point(0, 0);
        _lblStatus.Text = $"Downloading {AppName} {_remoteVersion}...";

        // A7-F04: cancel and dispose the old CTS before reassigning to prevent
        // in-flight continuations from outliving their token.
        var oldCts = _cts;
        _cts = new CancellationTokenSource();
        try { oldCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { oldCts?.Dispose(); } catch (ObjectDisposedException) { }

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

            // ─── SHA256 integrity gate (BUG-001 / A7-F01) ──────────────────────
            // Version-gated fail-closed: if the remote release is >= FIRST_HASH_EMITTING_VERSION
            // and no SHA256SUMS asset was found, abort rather than installing unverified.
            // Grandfathered older releases (< 2.1.10) keep the skip-with-log behavior so
            // users upgrading from very old builds can still reach 2.1.10 safely.
            if (string.IsNullOrEmpty(_hashFileUrl))
            {
                bool isGrandfathered = Version.TryParse(_remoteVersion, out var remoteVer)
                                    && remoteVer < FIRST_HASH_EMITTING_VERSION;
                if (isGrandfathered)
                {
                    Log.Warn($"Update verify SKIPPED (grandfathered release {_remoteVersion} < {FIRST_HASH_EMITTING_VERSION})");
                    // continue to apply
                }
                else
                {
                    TryDelete(newPath);
                    Log.Error($"Update aborted: SHA256SUMS missing for release {_remoteVersion} (>= {FIRST_HASH_EMITTING_VERSION}). Fail-closed.");
                    ShowError("Update integrity file missing. Download manually from GitHub.",
                        $"SHA256SUMS was not found in release {_remoteVersion}. Aborting for security.");
                    return;
                }
            }
            else
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
                    using var hashResponse = await GetWithValidatedRedirectsAsync(
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
                    // Split on both \r and \n so Windows-CI-generated SHA256SUMS files
                    // (CRLF) parse without relying on the trailing-Trim() chain below
                    // to strip the stray \r — a future cleanup of those Trim()s would
                    // otherwise silently break verification on CRLF SUMS files.
                    foreach (var line in hashContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        // Format: "hexhash  filename" or "hexhash *filename"
                        // A7-F11: use Path.GetFileName so entries like "./MicMute.exe" also match.
                        var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 &&
                            Path.GetFileName(parts[1].Trim().TrimStart('*'))
                                .Equals("MicMute.exe", StringComparison.OrdinalIgnoreCase))
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
                        Log.Info($"Update verified via SHA256SUMS (remote={_remoteVersion})");
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

            // BUG-005: replace the three-line TryDelete+Move+Move sequence with a single
            // File.Replace call. On NTFS this is a near-atomic rename-pair: exePath→oldPath
            // and newPath→exePath happen as one logical operation, eliminating the window
            // where the exe is absent from disk.
            // Log distinct failure modes so support can distinguish "stale .old" from swap failure.
            if (File.Exists(oldPath))
            {
                // Stale .old from a prior interrupted update — must clear it first because
                // File.Replace uses it as the backup destination and will fail if it already exists
                // on some NTFS configurations.
                try { File.Delete(oldPath); }
                catch (Exception ex)
                {
                    Log.Error($"Update apply: could not clear stale .old file at {oldPath}", ex);
                    ShowError("Failed to apply update.",
                        "A leftover file from a previous update is blocking the install. Delete MicMute.exe.old and retry.");
                    TryDelete(newPath);
                    return;
                }
            }

            try
            {
                File.Replace(newPath, exePath, oldPath, ignoreMetadataErrors: true);
            }
            catch (Exception ex)
            {
                Log.Error($"Update apply: File.Replace(new→exe, backup→old) failed", ex);
                // newPath still exists (Replace failed before touching exePath), so rollback just cleans it.
                TryDelete(newPath);
                ShowError(
                    ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                        ? "Cannot replace the executable." : "Failed to apply update.",
                    ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                        ? "Your antivirus may be locking the file. Try again." : ex.Message);
                return;
            }

            // BUG-003: capture the Process return value and verify the child actually started.
            // Do NOT delete .old here — leave it for CleanupUpdateArtifacts on --after-update
            // entry as a proof-of-life safety net. If the child dies immediately, roll back.
            Process proc;
            try
            {
                // nosemgrep: gitlab.security_code_scan.SCS0001-1 -- exePath is Environment.ProcessPath; the replacement binary was SHA256-verified above (version-gated fail-closed at FIRST_HASH_EMITTING_VERSION 2.1.10) against a SHA256SUMS asset from the github.com/itsnateai/ allowlisted origin
                proc = Process.Start(new ProcessStartInfo(exePath)
                {
                    Arguments = "--after-update",
                    UseShellExecute = true,
                    // A7-F07: set working directory explicitly so the child doesn't inherit
                    // a temp CWD that may not be accessible under all deployment scenarios.
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                });
            }
            catch (Exception ex)
            {
                Log.Error("Update restart: Process.Start failed", ex);
                RollbackUpdate(exePath, oldPath, newPath);
                ShowError("Update applied but restart failed.", "Please relaunch MicMute manually.");
                return;
            }

            if (proc == null)
            {
                Log.Error("Update restart: Process.Start returned null");
                RollbackUpdate(exePath, oldPath, newPath);
                ShowError("Update applied but restart failed.", "Please relaunch MicMute manually.");
                return;
            }

            // Brief wait so the child has time to start its message pump. We're
            // about to call Application.Exit so UI-thread sleep is acceptable here.
            try { proc.WaitForInputIdle(2000); } catch { }

            if (proc.HasExited)
            {
                Log.Error($"Update restart: new exe exited immediately (ExitCode={proc.ExitCode}). Rolling back.");
                proc.Dispose();
                RollbackUpdate(exePath, oldPath, newPath);
                ShowError("Update applied but new version failed to start.",
                    "The new executable exited immediately. Rolled back. Try again or download manually from GitHub.");
                return;
            }

            proc.Dispose();

            // A2-F12: cancel the CTS before exiting so any in-flight continuations
            // don't attempt UI updates on a disposed context. Wrap Application.Exit
            // defensively in case we're already shutting down.
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            try { Application.Exit(); } catch { }
        }
        catch (IOException ex)
        {
            Log.Error("Update apply failed (IO)", ex);
            // A3-F08: wrap RollbackUpdate+ShowError in an inner try so a secondary
            // fault during rollback doesn't propagate out of the async void chain.
            try
            {
                RollbackUpdate(exePath, oldPath, newPath);
                ShowError(
                    ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                        ? "Cannot replace the executable." : "Failed to apply update.",
                    ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                        ? "Your antivirus may be locking the file. Try again." : ex.Message);
            }
            catch (Exception inner)
            {
                Log.Error("Update: secondary fault during IO-error rollback", inner);
            }
        }
        catch (TaskCanceledException)
        {
            try
            {
                RollbackUpdate(exePath, oldPath, newPath);
                if (IsHandleCreated && !IsDisposed) ShowVersionComparison();
            }
            catch (Exception inner)
            {
                Log.Error("Update: secondary fault during cancel rollback", inner);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Update apply failed", ex);
            try
            {
                RollbackUpdate(exePath, oldPath, newPath);
                if (IsHandleCreated && !IsDisposed) ShowError("Update failed.", ex.Message);
            }
            catch (Exception inner)
            {
                Log.Error("Update: secondary fault during exception rollback", inner);
            }
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
        using var response = await GetWithValidatedRedirectsAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct);
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

            // A5-F07: wrap BeginInvoke in try/catch to defend against ObjectDisposedException
            // if the dialog is closed while a download is still in progress.
            if (totalBytes > 0 && IsHandleCreated && !IsDisposed)
            {
                try
                {
                    BeginInvoke(() =>
                    {
                        if (IsHandleCreated && !IsDisposed)
                        {
                            int pct = (int)(downloaded * 100 / totalBytes);
                            _progressFill.Size = new Size(
                                (int)(_progressOuter.Width * downloaded / totalBytes), _progressOuter.Height);
                            var dlMB = downloaded / (1024.0 * 1024.0);
                            var totalMB = totalBytes / (1024.0 * 1024.0);
                            _lblDetail.Text = totalMB < 1
                                ? $"{pct}% ({downloaded / 1024.0:F0} / {totalBytes / 1024.0:F0} KB)"
                                : $"{pct}% ({dlMB:F0} / {totalMB:F0} MB)";
                        }
                    });
                }
                catch (InvalidOperationException) { } // includes ObjectDisposedException
            }
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
        _lblStatus.ForeColor = UiTokens.WarnOrange;
        _lblDetail.Text = detail;
        _btnAction.Visible = false;       // flow re-centers on the lone OK button
        _btnCancel.Text = "OK";
        FitToContentHeight();   // long error/detail strings wrap to 2-3 lines — grow to fit, no clip
    }

    // ─── Static Helpers (called from Program.cs) ────────────────

    /// <summary>Returns true if the app is installed via winget (portable package).</summary>
    /// <remarks>
    /// User-scope installs:    %LOCALAPPDATA%\Microsoft\WinGet\Packages\...
    /// Machine-scope installs: %ProgramFiles%\WinGet\Packages\...
    /// A7-F18: resolve %LOCALAPPDATA% at runtime and match the full user-scope path prefix
    /// to avoid false positives from directories that merely contain "WinGet\Packages\" in
    /// their name. Machine-scope falls back to the looser suffix check.
    /// </remarks>
    internal static bool IsWingetManaged()
    {
        var path = Environment.ProcessPath ?? "";
        // User-scope (most common): %LOCALAPPDATA%\Microsoft\WinGet\Packages\...
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userScope = Path.Combine(localApp, "Microsoft", "WinGet", "Packages") + Path.DirectorySeparatorChar;
        if (path.StartsWith(userScope, StringComparison.OrdinalIgnoreCase))
            return true;
        // Machine-scope: %ProgramFiles%\WinGet\Packages\...
        if (path.Contains(@"\WinGet\Packages\", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

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
                // A7-F17: sanity-check the .old file size before restoring — a zero-byte or
                // suspiciously tiny file is more likely corruption than a valid executable.
                // Self-contained .NET single-file binaries are always several MB.
                const long MinValidExeBytes = 1_000_000;
                var oldSize = new FileInfo(oldPath).Length;
                if (oldSize < MinValidExeBytes)
                {
                    Log.Error($"Torn-state recovery: .old file is suspiciously small ({oldSize} bytes); skipping restore to avoid replacing exe with corrupt data.");
                    return;
                }
                try { File.Move(oldPath, exePath); }
                catch (Exception ex) { Log.Error("Torn-state recovery failed — exe missing and .old restore failed", ex); }
            }
            return;
        }

        // Clean up .old (proof-of-life from prior successful restart) and .new (partial download).
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

        // A1-F05: store the outer timer in a static field and hook ApplicationExit so it
        // can be stopped and disposed if the application exits before the tick fires.
        // Also wrap Form construction in try/catch so a Font allocation failure doesn't
        // leak the timer.
        _toastOuterTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        Application.ApplicationExit += OnApplicationExitCleanupToastTimer;

        _toastOuterTimer.Tick += (_, _) =>
        {
            _toastOuterTimer.Stop();
            _toastOuterTimer.Dispose();
            _toastOuterTimer = null;
            Application.ApplicationExit -= OnApplicationExitCleanupToastTimer;

            Form toast = null;
            Font toastFont = null;
            try
            {
                toastFont = new Font(UiTokens.PrimaryFont, 9.5f, FontStyle.Bold);
                toast = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    ShowInTaskbar = false,
                    TopMost = true,
                    StartPosition = FormStartPosition.Manual,
                    BackColor = Theme.BgColor,
                    ForeColor = Theme.FgColor,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(12, 8, 12, 8)
                };
                var lbl = new Label
                {
                    Text = $"\u2705 {AppName} updated to v{version}!",
                    AutoSize = true,
                    Font = toastFont,
                    ForeColor = Theme.FgColor,
                };
                toast.Controls.Add(lbl);
                toast.FormClosed += (_, _) => toastFont?.Dispose();

                var screen = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
                toast.Load += (_, _) =>
                    toast.Location = new Point(screen.Right - toast.Width - 20, screen.Bottom - toast.Height - 20);
                toast.Show();

                var dismiss = new System.Windows.Forms.Timer { Interval = 5000 };
                dismiss.Tick += (_, _) =>
                {
                    dismiss.Stop();
                    dismiss.Dispose();
                    if (!toast.IsDisposed) toast.Close();
                };
                dismiss.Start();
            }
            catch (Exception ex)
            {
                // If toast construction fails, dispose what was allocated and log — don't crash.
                Log.Error("ShowUpdateToast: failed to show toast", ex);
                toastFont?.Dispose();
                toast?.Dispose();
            }
        };
        _toastOuterTimer.Start();
    }

    private static void OnApplicationExitCleanupToastTimer(object sender, EventArgs e)
    {
        try
        {
            _toastOuterTimer?.Stop();
            _toastOuterTimer?.Dispose();
            _toastOuterTimer = null;
        }
        catch { }
        Application.ApplicationExit -= OnApplicationExitCleanupToastTimer;
    }

    // ─── Helpers ────────────────────────────────────────────────

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>
    /// Origin allowlist for both the exe download and the SHA256SUMS fetch.
    /// Host-based (not prefix) so a future attacker-controlled subdomain
    /// can't sneak past — `objects.githubusercontent.com.evil.example` no
    /// longer matches the way a `StartsWith` prefix check would have.
    /// HTTPS-only, explicit host set, explicit owner scope on github.com.
    /// </summary>
    internal static bool IsAllowedReleaseOrigin(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        string host = uri.Host;
        // GitHub release-asset CDN. Both hosts seen in the wild — GitHub rolled
        // `release-assets.githubusercontent.com` alongside the legacy
        // `objects.githubusercontent.com`, and either can be the redirect target
        // for a `github.com/.../releases/download/...` GET. Both are allow-listed
        // so the manual per-hop redirect validator doesn't fail when GitHub
        // routes through the new edge.
        if (host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith($"/repos/{GitHubRepo}/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith($"/{GitHubRepo}/", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Scale every pixel literal (margins, progress track, button sizes, and the Absolute
        // progress-row height) by the device factor, pin the width to the scaled design width,
        // then fit the height to the laid-out content. The height is RE-FIT on every later state
        // change (FitToContentHeight) because a long error/winget message wraps to 2-3 lines in
        // the fixed-width form — measuring once here (in the short "Checking..." state) would clip
        // the OK button when a taller state swaps in. Short states stay tight; long messages grow
        // to fit. Done before the first paint → no visible initial resize.
        UiLayout.ApplyDpi(_root);
        ClientSize = new Size(LogicalToDeviceUnits(DesignW), ClientSize.Height);
        FitToContentHeight();
    }

    /// <summary>
    /// Re-fit the window height to the current laid-out content (width stays fixed). Called from
    /// OnLoad and at the end of every state change that swaps the status/detail text. The fixed-
    /// width form wraps long messages to 2-3 lines, so content height varies by state; the reserved
    /// Absolute progress row keeps the bar's show/hide from changing height, so this only resizes
    /// on genuine text-wrap changes. MUST NOT be called from the per-frame download-progress
    /// callback — the % detail is always one line, and re-fitting every frame would thrash the
    /// window size.
    /// </summary>
    private void FitToContentHeight()
    {
        if (!IsHandleCreated) return;
        _root.PerformLayout();
        int h = _root.Height;
        if (ClientSize.Height != h)
            ClientSize = new Size(ClientSize.Width, h);
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
