using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.Video {
    public static class VideoExporter {
        public static string? FindFfmpeg() {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            foreach (var dir in pathEnv.Split(Path.PathSeparator)) {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try {
                    var candidate = Path.Combine(dir, exeName);
                    if (File.Exists(candidate)) return candidate;
                } catch { }
            }
            return null;
        }

        public static async Task ExportAsync(
            PianoRoll pianoRoll,
            UProject project,
            int fps,
            bool exportAudio,
            string outputPath,
            string ffmpegPath,
            Action<string, int, int> onProgress,
            CancellationToken ct) {

            if (fps <= 0) throw new ArgumentException("fps must be positive", nameof(fps));
            if (project.parts.Count == 0) throw new InvalidOperationException("Project is empty.");

            var tempDir = Path.Combine(Path.GetTempPath(), "utauv_video_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Log.Information("VideoExport: temp dir = {dir}", tempDir);

            var playbackVm = pianoRoll.ViewModel?.PlaybackViewModel;
            var notesVm = pianoRoll.ViewModel?.NotesViewModel;
            var origBg = pianoRoll.Background;

            try {
                string? wavPath = null;
                if (exportAudio) {
                    onProgress("audio", 0, 1);
                    wavPath = Path.Combine(tempDir, "audio.wav");
                    await PlaybackManager.Inst.RenderMixdown(project, wavPath);
                    onProgress("audio", 1, 1);
                }

                ct.ThrowIfCancellationRequested();

                int endTick = project.parts.Max(p => p.End);
                double t0ms = project.timeAxis.TickPosToMsPos(0);
                double frameMs = 1000.0 / fps;
                int tickStart = project.timeAxis.MsPosToTickPos(t0ms);
                int tickEnd = project.timeAxis.MsPosToTickPos(t0ms + frameMs);
                int ticksPerFrame = Math.Max(1, tickEnd - tickStart);
                int frameCount = (endTick + ticksPerFrame - 1) / ticksPerFrame;

                int w = (int)Math.Round(pianoRoll.Bounds.Width);
                int h = (int)Math.Round(pianoRoll.Bounds.Height);
                if (w <= 0 || h <= 0) throw new InvalidOperationException($"Piano roll has no size: {w}x{h}.");
                var pixelSize = new PixelSize(w, h);
                Log.Information("VideoExport: {n} frames, {w}x{h}, {fps} fps", frameCount, w, h, fps);

                // === 进入"伪装播放"模式 ===
                await Dispatcher.UIThread.InvokeAsync(() => {
                    PlaybackManager.Inst.ForcePlayingUI = true;
                    VideoClock.Begin(0);
                    if (Application.Current != null &&
                        Application.Current.TryFindResource("SystemRegionBrush", out var bgRes) &&
                        bgRes is IBrush themeBg) {
                        pianoRoll.Background = themeBg;
                    }
                    playbackVm?.RefreshForVideoExport();
                    
                }, DispatcherPriority.Normal);

                for (int i = 0; i < frameCount; i++) {
                    ct.ThrowIfCancellationRequested();

                    VideoClock.SetSeconds((double)i / fps);
                    int tick = tickStart + i * ticksPerFrame;

                    await Dispatcher.UIThread.InvokeAsync(() => {
                        DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(tick));
                        playbackVm?.RefreshForVideoExport();
                        notesVm?.RefreshPlayPosForVideoExport();
                    }, DispatcherPriority.Normal);
                    pianoRoll.AdvanceAnimationForExport();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

                    using var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
                    rtb.Render(pianoRoll);
                    var framePath = Path.Combine(tempDir, $"frame_{i:D6}.png");
                    #pragma warning disable CS0618
                    rtb.Save(framePath);
                    #pragma warning restore CS0618

                    onProgress("frames", i + 1, frameCount);
                }

                await Dispatcher.UIThread.InvokeAsync(() => {
                    VideoClock.End();
                    PlaybackManager.Inst.ForcePlayingUI = false;
                    pianoRoll.Background = origBg;
                    playbackVm?.RefreshForVideoExport();
                    
                }, DispatcherPriority.Normal);

                ct.ThrowIfCancellationRequested();

                onProgress("encoding", frameCount, frameCount);

                var args = $"-y -framerate {fps} -i \"{Path.Combine(tempDir, "frame_%06d.png")}\"";
                if (wavPath != null) args += $" -i \"{wavPath}\"";
                args += " -c:v libx264 -pix_fmt yuv420p -preset medium";
                if (wavPath != null) args += " -c:a aac -b:a 192k -shortest";
                args += $" \"{outputPath}\"";

                var psi = new ProcessStartInfo {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                using var proc = Process.Start(psi)!;
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync(ct);
                if (proc.ExitCode != 0) {
                    throw new Exception($"ffmpeg exited with code {proc.ExitCode}.\n{stderr}");
                }

                onProgress("done", frameCount, frameCount);
                Log.Information("VideoExport: done -> {path}", outputPath);
            } finally {
                await Dispatcher.UIThread.InvokeAsync(() => {
                    VideoClock.End();
                    PlaybackManager.Inst.ForcePlayingUI = false;
                    pianoRoll.Background = origBg;
                    playbackVm?.RefreshForVideoExport();
                    
                }, DispatcherPriority.Normal);
                try { Directory.Delete(tempDir, true); } catch (Exception e) { Log.Warning(e, "Failed to clean temp dir"); }
            }
        }
    }
}