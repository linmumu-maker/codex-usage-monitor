using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexUsageMonitor
{
    internal sealed class RpcFailure : Exception
    {
        public RpcFailure() : base("额度服务返回错误；请检查 Codex 登录状态或稍后刷新。") { }
    }
    internal sealed class AppServerClient : IDisposable
    {
        private readonly object sync = new object();
        private readonly SemaphoreSlim readGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending =
            new Dictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private Process process;
        private StreamWriter input;
        private IntPtr job;
        private int nextId;
        private int generation;
        private bool initialized;
        private bool disposed;
        private readonly string configuredPath;
        public event Action<bool> AccountOrLimitsChanged;
        public int StartCount { get; private set; }
        public string LastStartupDiagnostic { get; private set; }
        public string ResolvedExecutable { get; private set; }
        public string StartupSignals { get; private set; }
        private int receivedLines, invalidLines;
        public int ProcessId { get { lock (sync) { return process == null ? 0 : process.Id; } } }
        public AppServerClient(string path) { configuredPath = path; }

        private IEnumerable<string> Candidates()
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                yield return configuredPath;
                yield break;
            }
            string desktopBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(desktopBin))
            {
                foreach (string file in Directory.GetDirectories(desktopBin).OrderByDescending(Directory.GetLastWriteTimeUtc)
                    .Select(directory => Path.Combine(directory, "codex.exe")).Where(File.Exists)) yield return file;
            }
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                string file;
                try { file = Path.Combine(directory.Trim('"'), "codex.exe"); }
                catch { continue; }
                if (File.Exists(file) && file.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) < 0) yield return file;
                string vendor = Path.Combine(directory.Trim('"'), "node_modules", "@openai", "codex", "vendor");
                if (Directory.Exists(vendor))
                    foreach (string sub in Directory.GetDirectories(vendor))
                    {
                        string native = Path.Combine(sub, "codex", "codex.exe");
                        if (File.Exists(native)) yield return native;
                    }
            }
        }
        private void Start(string path)
        {
            Stop();
            if (!File.Exists(path)) throw new FileNotFoundException();
            var info = new ProcessStartInfo(path, "app-server --listen stdio:// -c analytics.enabled=false");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            ResolvedExecutable = path;
            Process child = Process.Start(info);
            int current;
            lock (sync)
            {
                if (disposed) { child.Kill(); child.Dispose(); throw new ObjectDisposedException("AppServerClient"); }
                process = child;
                // Framework creates StandardInput with a preamble-writing encoding.
                // Its initial AutoFlush may already have emitted BOM; isolate that preamble
                // on its own line before sending the first JSON-RPC message.
                var originalInput = child.StandardInput;
                input = new StreamWriter(originalInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
                if (originalInput.Encoding.GetPreamble().Length != 0) input.WriteLine();
                initialized = false;
                current = ++generation;
                job = Native.CreateKillJob(child.Handle);
                StartCount++;
            }
            Task.Run(() => Pump(child, current));
            // Drain stderr without retaining, logging or displaying raw messages.
            Task.Run(() =>
            {
                try
                {
                    string stderrLine;
                    while ((stderrLine = child.StandardError.ReadLine()) != null)
                    {
                        string lower = stderrLine.ToLowerInvariant();
                        foreach (string category in new[] { "parse", "utf-8", "denied", "host", "spawn", "invalid", "config", "socket" })
                            if (lower.Contains(category)) StartupSignals = category;
                    }
                }
                catch { }
            });
        }
        private void Pump(Process child, int current)
        {
            try
            {
                string line;
                while ((line = child.StandardOutput.ReadLine()) != null)
                {
                    Interlocked.Increment(ref receivedLines);
                    if (line.Length > 1048576) continue;
                    Dictionary<string, object> message;
                    try { message = Json.Parse(line); }
                    catch { Interlocked.Increment(ref invalidLines); continue; }
                    if (message == null) continue;
                    double? numericId = Json.Number(message, "id");
                    if (numericId.HasValue && numericId.Value >= 0 && numericId.Value <= int.MaxValue)
                    {
                        TaskCompletionSource<Dictionary<string, object>> completion = null;
                        lock (sync)
                        {
                            if (current != generation) return;
                            int id = (int)numericId.Value;
                            if (pending.TryGetValue(id, out completion)) pending.Remove(id);
                        }
                        if (completion != null) completion.TrySetResult(message);
                    }
                    else
                    {
                        string method = Json.Get(message, "method") as string;
                        if (method != "account/updated" && method != "account/rateLimits/updated") continue;
                        lock (sync) { if (current != generation) return; }
                        var changed = AccountOrLimitsChanged;
                        if (changed != null) changed(method == "account/updated");
                    }
                }
            }
            catch { }
            finally
            {
                lock (sync)
                {
                    if (current == generation)
                    {
                        initialized = false;
                        foreach (var completion in pending.Values) completion.TrySetException(new IOException());
                        pending.Clear();
                    }
                }
            }
        }
        private async Task<Dictionary<string, object>> Request(string method, object parameters)
        {
            var completion = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int id = Interlocked.Increment(ref nextId);
            lock (sync)
            {
                if (disposed || process == null || process.HasExited) throw new IOException();
                pending.Add(id, completion);
                try
                {
                    var message = new Dictionary<string, object> { { "id", id }, { "method", method } };
                    if (parameters != null) message.Add("params", parameters);
                    input.WriteLine(Json.Serialize(message));
                    input.Flush();
                }
                catch { pending.Remove(id); throw; }
            }
            Task winner = await Task.WhenAny(completion.Task, Task.Delay(15000)).ConfigureAwait(false);
            if (winner != completion.Task) { lock (sync) pending.Remove(id); throw new TimeoutException(); }
            var response = await completion.Task.ConfigureAwait(false);
            if (response.ContainsKey("error")) throw new RpcFailure();
            var result = Json.Object(response, "result");
            if (result == null) throw new InvalidDataException();
            return result;
        }
        private async Task EnsureConnected()
        {
            bool usable;
            lock (sync) { usable = !disposed && process != null && !process.HasExited && initialized; }
            if (usable) return;
            bool found = false;
            foreach (string path in Candidates().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                found = true;
                try
                {
                    Start(path);
                    await Request("initialize", new
                    {
                        clientInfo = new { name = "codex_usage_monitor_windows", title = "Codex 额度监控", version = "1.0.0" }
                    }).ConfigureAwait(false);
                    lock (sync)
                    {
                        input.WriteLine(Json.Serialize(new { method = "initialized" }));
                        input.Flush();
                        initialized = true;
                    }
                    return;
                }
                catch (Exception error)
                {
                    LastStartupDiagnostic = error.GetType().Name + ";lines=" + receivedLines + ";invalid=" + invalidLines + ";signal=" + StartupSignals;
                    lock (sync) { if (process != null && process.HasExited) LastStartupDiagnostic += ";exit=" + process.ExitCode; }
                    Stop();
                }
            }
            if (!found) throw new FileNotFoundException();
            throw new IOException();
        }
        public async Task<QuotaSnapshot> Read()
        {
            await readGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureConnected().ConfigureAwait(false);
                return QuotaSnapshot.Read(await Request("account/rateLimits/read", null).ConfigureAwait(false));
            }
            catch (RpcFailure) { throw; }
            catch { Stop(); throw; }
            finally { readGate.Release(); }
        }
        internal void KillForTest()
        {
            lock (sync) { if (process != null && !process.HasExited) process.Kill(); }
        }
        private void Stop()
        {
            Process old;
            IntPtr oldJob;
            lock (sync)
            {
                ++generation;
                initialized = false;
                old = process; process = null; input = null;
                oldJob = job; job = IntPtr.Zero;
                foreach (var completion in pending.Values) completion.TrySetException(new IOException());
                pending.Clear();
            }
            if (oldJob != IntPtr.Zero) Native.CloseHandle(oldJob);
            if (old != null)
            {
                try { if (!old.HasExited) old.Kill(); }
                catch { }
                old.Dispose();
            }
        }
        public void Dispose() { lock (sync) disposed = true; Stop(); }
    }
    internal sealed class RefreshService : IDisposable
    {
        private readonly AppServerClient client;
        private Timer timer;
        private int busy;
        private int accountEpoch;
        private bool disposed;
        public event Action<QuotaSnapshot, string, bool> Completed;
        public event Action AccountChanged;
        public event Action Started;
        public int SuccessfulReads { get; private set; }
        public int AutomaticReads { get; private set; }
        public AppServerClient Client { get { return client; } }
        public RefreshService(string path)
        {
            client = new AppServerClient(path);
            client.AccountOrLimitsChanged += account =>
            {
                if (account)
                {
                    Interlocked.Increment(ref accountEpoch);
                    var changed = AccountChanged; if (changed != null) changed();
                }
                // Coalesced notification reads; do not log or retain notification payloads.
                Refresh(false);
            };
        }
        public void Start()
        {
            Refresh(false);
            timer = new Timer(_ => Refresh(true), null, 60000, 60000);
        }
        public async void Refresh(bool automatic)
        {
            if (disposed || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            int epoch = Interlocked.CompareExchange(ref accountEpoch, 0, 0);
            if (automatic) AutomaticReads++;
            var started = Started; if (started != null) started();
            QuotaSnapshot snapshot = null; string error = null;
            try { snapshot = await client.Read().ConfigureAwait(false); SuccessfulReads++; }
            catch (FileNotFoundException) { error = "找不到 Codex CLI；请在配置中指定 codex.exe。"; }
            catch (TimeoutException) { error = "额度查询超时；保留最后成功数据。"; }
            catch (RpcFailure) { error = "额度查询失败；请检查 Codex 登录状态后刷新。"; }
            catch { error = "额度服务连接失败；请确认 Codex 已登录，稍后刷新。"; }
            bool accountMismatch = epoch != Interlocked.CompareExchange(ref accountEpoch, 0, 0);
            Interlocked.Exchange(ref busy, 0);
            if (!disposed)
            {
                var completed = Completed;
                if (completed != null && !accountMismatch) completed(snapshot, error, automatic);
                if (accountMismatch) Refresh(false);
            }
        }
        public void Dispose() { disposed = true; if (timer != null) timer.Dispose(); client.Dispose(); }
    }
}
