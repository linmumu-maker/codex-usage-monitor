using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace CodexUsageMonitor
{
    internal static class Json
    {
        public static Dictionary<string, object> Parse(string value)
        {
            return new JavaScriptSerializer { MaxJsonLength = 1048576 }.Deserialize<Dictionary<string, object>>(value);
        }
        public static Dictionary<string, object> Object(Dictionary<string, object> value, string key)
        {
            object item;
            return value != null && value.TryGetValue(key, out item) ? item as Dictionary<string, object> : null;
        }
        public static object Get(Dictionary<string, object> value, string key)
        {
            object item;
            return value != null && value.TryGetValue(key, out item) ? item : null;
        }
        public static double? Number(Dictionary<string, object> value, string key)
        {
            object item = Get(value, key);
            if (!(item is int || item is long || item is double || item is decimal || item is float)) return null;
            double result = Convert.ToDouble(item, CultureInfo.InvariantCulture);
            return double.IsNaN(result) || double.IsInfinity(result) ? null : (double?)result;
        }
        public static string Serialize(object value) { return new JavaScriptSerializer().Serialize(value); }
    }

    internal sealed class QuotaWindow
    {
        public double? UsedPercent;
        public long? ResetsAt;
        public double? Remaining
        {
            get { return UsedPercent.HasValue ? (double?)Math.Max(0, Math.Min(100, 100 - UsedPercent.Value)) : null; }
        }
        public DateTimeOffset? LocalReset
        {
            get { return Time.LocalReset(ResetsAt); }
        }
        public string ResetText
        {
            get { var reset = LocalReset; return reset.HasValue ? reset.Value.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) : "暂无数据"; }
        }
    }
    internal static class Time
    {
        public static DateTimeOffset? LocalReset(long? seconds)
        {
            if (!seconds.HasValue) return null;
            try { return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds.Value).ToLocalTime(); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
    }
    internal sealed class ResetCardDetail
    {
        public string Title;
        public long? GrantedAt, ExpiresAt;
        public bool NoExpiry;
        public string ValidityText
        {
            get
            {
                var start = Time.LocalReset(GrantedAt); var end = Time.LocalReset(ExpiresAt);
                return (start.HasValue ? "发放 " + start.Value.ToString("yyyy-MM-dd HH:mm") : "发放时间暂无数据") + "\n" +
                    (NoExpiry ? "无到期时间" : end.HasValue ? "到期 " + end.Value.ToString("yyyy-MM-dd HH:mm") : "到期时间暂无数据");
            }
        }
    }
    internal sealed class QuotaSnapshot
    {
        public QuotaWindow FiveHour;
        public QuotaWindow Weekly;
        public long? ResetCards;
        public List<ResetCardDetail> ResetDetails;
        internal static long? ReadResetCount(object raw)
        {
            if (raw is int) return (int)raw >= 0 ? (long?)(int)raw : null;
            if (raw is long) return (long)raw >= 0 ? (long?)raw : null;
            if (raw is double || raw is float || raw is decimal)
            {
                double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                if (!Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0 && value < 9007199254740992 && value == Math.Truncate(value)) return (long)value;
            }
            return null;
        }
        public static QuotaSnapshot Read(Dictionary<string, object> result)
        {
            if (result == null) throw new InvalidDataException();
            var byId = Json.Object(result, "rateLimitsByLimitId");
            var bucket = byId == null ? null : Json.Object(byId, "codex");
            if (bucket == null && (byId == null || !byId.ContainsKey("codex")))
            {
                var legacy = Json.Object(result, "rateLimits");
                string id = Json.Get(legacy, "limitId") as string;
                if (id == null || id == "codex") bucket = legacy;
            }
            var snapshot = new QuotaSnapshot { ResetCards = ReadResetCount(Json.Get(Json.Object(result, "rateLimitResetCredits"), "availableCount")) };
            var summary = Json.Object(result, "rateLimitResetCredits");
            var details = Json.Get(summary, "credits") as System.Collections.IEnumerable;
            if (details != null && !(details is string) && !(details is Dictionary<string, object>))
            {
                snapshot.ResetDetails = new List<ResetCardDetail>();
                foreach (object item in details)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null || Json.Get(row, "status") as string != "available") continue;
                    string title = Json.Get(row, "resetType") as string == "codexRateLimits" ? "完全重置（每周 + 5 小时）" : "使用限额重置卡";
                    if (title.Length > 120) title = title.Substring(0, 120);
                    long? granted = ReadResetCount(Json.Get(row, "grantedAt")), expires = ReadResetCount(Json.Get(row, "expiresAt"));
                    snapshot.ResetDetails.Add(new ResetCardDetail { Title = title, GrantedAt = granted, ExpiresAt = expires,
                        NoExpiry = row.ContainsKey("expiresAt") && Json.Get(row, "expiresAt") == null });
                    if (snapshot.ResetDetails.Count >= 200) break;
                }
            }
            foreach (var key in new[] { "primary", "secondary" })
            {
                var raw = Json.Object(bucket, key);
                double? duration = Json.Number(raw, "windowDurationMins");
                if (duration != 300 && duration != 10080) continue;
                double? reset = Json.Number(raw, "resetsAt");
                long? timestamp = null;
                if (reset.HasValue && reset.Value >= 0 && reset.Value <= 253402300799 && reset.Value == Math.Truncate(reset.Value))
                    timestamp = (long)reset.Value;
                var window = new QuotaWindow { UsedPercent = Json.Number(raw, "usedPercent"), ResetsAt = timestamp };
                if (duration == 300) snapshot.FiveHour = window;
                else snapshot.Weekly = window;
            }
            return snapshot;
        }
    }
    internal sealed class QuotaState
    {
        public QuotaSnapshot Snapshot { get; private set; }
        public DateTimeOffset? LastSuccess { get; private set; }
        public string Error { get; private set; }
        public bool Refreshing { get; set; }
        public void Success(QuotaSnapshot snapshot, DateTimeOffset now) { Snapshot = snapshot; LastSuccess = now; Error = null; }
        public void Failure(string error) { Error = error; }
        public void Clear() { Snapshot = null; LastSuccess = null; Error = null; }
        public bool IsStale(DateTimeOffset now)
        {
            if (Snapshot == null) return false;
            if (Error != null || !LastSuccess.HasValue || now - LastSuccess.Value > TimeSpan.FromSeconds(90)) return true;
            return HasPassed(Snapshot.FiveHour, now) || HasPassed(Snapshot.Weekly, now);
        }
        private static bool HasPassed(QuotaWindow window, DateTimeOffset now)
        {
            var reset = window == null ? null : window.LocalReset;
            return reset.HasValue && now >= reset.Value;
        }
        public string Status(DateTimeOffset now)
        {
            if (Refreshing) return IsStale(now) ? "已过期·刷新中" : "刷新中";
            if (IsStale(now)) return "已过期";
            if (Error != null) return "查询失败";
            return Snapshot == null ? "正在连接" : "自动刷新";
        }
        public string Summary(DateTimeOffset now)
        {
            return "5h 剩余 " + Percent(Snapshot == null ? null : Snapshot.FiveHour) +
                "  重置 " + Reset(Snapshot == null ? null : Snapshot.FiveHour) +
                "\n周 剩余 " + Percent(Snapshot == null ? null : Snapshot.Weekly) +
                "  重置 " + Reset(Snapshot == null ? null : Snapshot.Weekly) +
                "\n重置卡 " + (Snapshot != null && Snapshot.ResetCards.HasValue ? "可用 " + Snapshot.ResetCards.Value + " 张" : "暂无数据") +
                "\n" + Status(now) +
                (LastSuccess.HasValue ? " · 更新 " + LastSuccess.Value.ToLocalTime().ToString("HH:mm:ss") : "") +
                (Error == null ? "" : "\n" + Error);
        }
        public static string Percent(QuotaWindow window)
        {
            return window != null && window.Remaining.HasValue ? window.Remaining.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "暂无数据";
        }
        public static string Reset(QuotaWindow window) { return window == null ? "暂无数据" : window.ResetText; }
    }
    internal sealed class MonitorSettings
    {
        public string CodexExecutable { get; set; }
        public string PositionMode { get; set; }
        public bool RelativeCalibrated { get; set; }
        public double RelativeX { get; set; }
        public double RelativeBottom { get; set; }
        public string Theme { get; set; }
        public string DisplayMode { get; set; }
        public SidebarRegion SidebarRegion { get; set; }
        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"); } }
        public MonitorSettings() { CodexExecutable = ""; PositionMode = "auto"; RelativeBottom = 150; Theme = "auto"; DisplayMode = "sidebarCard"; }
        public void Validate()
        {
            if (DisplayMode != "sidebarCard" && DisplayMode != "compactBar") throw new InvalidDataException();
            if (SidebarRegion != null) SidebarRegion.Validate();
            if (PositionMode != "auto" && PositionMode != "relative") throw new InvalidDataException();
            if (Theme != "auto" && Theme != "light" && Theme != "dark") throw new InvalidDataException();
            if (double.IsNaN(RelativeX) || double.IsInfinity(RelativeX) || Math.Abs(RelativeX) > 8192 ||
                double.IsNaN(RelativeBottom) || double.IsInfinity(RelativeBottom) || RelativeBottom < 0 || RelativeBottom > 8192)
                throw new InvalidDataException();
            if (!string.IsNullOrWhiteSpace(CodexExecutable) &&
                (!Path.IsPathRooted(CodexExecutable) || !string.Equals(Path.GetExtension(CodexExecutable), ".exe", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException();
        }
        public static MonitorSettings Load(out string warning)
        {
            warning = null;
            try
            {
                if (!File.Exists(FilePath)) return new MonitorSettings();
                var settings = new JavaScriptSerializer().Deserialize<MonitorSettings>(File.ReadAllText(FilePath));
                if (settings == null) throw new InvalidDataException();
                settings.Validate();
                return settings;
            }
            catch { warning = "配置无法读取或字段无效，已使用默认设置；原文件未覆盖。"; return new MonitorSettings(); }
        }
        public void Save()
        {
            Validate();
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, Json.Serialize(this), new System.Text.UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
    }
}
