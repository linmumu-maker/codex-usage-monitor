using System;

namespace CodexUsageMonitor
{
    internal static class OverlayVisibility
    {
        // The status page's WindowState is deliberately not a visibility requirement.
        // Only the actual target and foreground window determine eligibility.
        internal static string HiddenReason(bool targetExists, bool targetMinimized, bool targetVisible,
            bool targetForeground, bool statusForeground)
        {
            if (!targetExists) return "等待 Codex 窗口";
            if (targetMinimized) return "Codex 已最小化，悬浮栏已隐藏";
            if (!targetVisible) return "Codex 窗口不可见，悬浮栏已隐藏";
            if (targetForeground) return null;
            return statusForeground ? "监控状态页在前台；回到 Codex 后自动恢复悬浮栏" :
                "Codex 不在前台，悬浮栏已隐藏";
        }
    }

    internal sealed class PlacementHistory
    {
        private IntPtr target;
        private string reason;
        private DateTimeOffset? recorded;
        internal void Observe(IntPtr currentTarget, bool codexForeground, string currentReason, DateTimeOffset now)
        {
            if (currentTarget != IntPtr.Zero && target != IntPtr.Zero && currentTarget != target) Clear();
            if (!codexForeground || currentTarget == IntPtr.Zero || string.IsNullOrEmpty(currentReason)) return;
            target = currentTarget;
            reason = currentReason;
            recorded = now;
        }
        internal string Description
        {
            get
            {
                return recorded.HasValue ? "最近一次 Codex 前台定位 · " +
                    recorded.Value.ToLocalTime().ToString("MM-dd HH:mm:ss") + "\n" + reason : null;
            }
        }
        internal void Clear() { target = IntPtr.Zero; reason = null; recorded = null; }
    }
}
