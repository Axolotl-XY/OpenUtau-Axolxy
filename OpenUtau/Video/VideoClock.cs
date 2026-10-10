using System;

namespace OpenUtau.App.Video {
    /// <summary>
    /// 全局虚拟时钟。视频导出时接管所有动画的时间来源，
    /// 让动画时间轴与视频帧时间轴精确对齐。
    /// </summary>
    public static class VideoClock {
        private static double? forcedSeconds = null;
        private static DateTime realStart = DateTime.UtcNow;

        public static bool IsForcing => forcedSeconds.HasValue;

        public static DateTime Now {
            get {
                if (!forcedSeconds.HasValue) return DateTime.UtcNow;
                return realStart + TimeSpan.FromSeconds(forcedSeconds.Value);
            }
        }

        public static void Begin(double startSeconds = 0) {
            realStart = DateTime.UtcNow;
            forcedSeconds = startSeconds;
        }

        public static void SetSeconds(double seconds) {
            if (forcedSeconds.HasValue) {
                forcedSeconds = seconds;
            }
        }

        public static void End() {
            forcedSeconds = null;
        }
    }
}