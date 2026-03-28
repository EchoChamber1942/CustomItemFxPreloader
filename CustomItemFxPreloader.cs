using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CustomItemFxPreloader", "EchoChamber", "1.2.1")]
    [Description("CIFP-Lite++ with GitHub Auto-Fetch & i18n: verify FX prefabs and publish aliases for CustomItemLoader.")]
    public class CustomItemFxPreloader : RustPlugin
    {
        private const string PermAdmin = "customitemfxpreloader.admin";
        private const string PermUse   = "customitemfxpreloader.use";

        #region === Config ===
        private PluginConfig config;

        public class PluginConfig
        {
            [JsonProperty("DefaultLang")] public string DefaultLang { get; set; } = "ja"; // "en" or "ja"

            [JsonProperty("AutoScanOnInit")] public bool AutoScanOnInit { get; set; } = true;
            [JsonProperty("ScanBatchSize")] public int ScanBatchSize { get; set; } = 64;
            [JsonProperty("ScanInterval")] public float ScanInterval { get; set; } = 0.05f;

            [JsonProperty("EnableCsvImport")] public bool EnableCsvImport { get; set; } = true;
            [JsonProperty("EnableJsonImport")] public bool EnableJsonImport { get; set; } = true;

            [JsonProperty("ExportOnChange")] public bool ExportOnChange { get; set; } = true;
            [JsonProperty("PublishOnChange")] public bool PublishOnChange { get; set; } = true;

            [JsonProperty("WriteLegacyFiles")] public bool WriteLegacyFiles { get; set; } = true; // verified_effects.json / fxlist.txt
            [JsonProperty("ExcludeSceneEffects")] public bool ExcludeSceneEffects { get; set; } = true; // assets/content/effects を除外

            // Auto-Fetch from GitHub
            [JsonProperty("AutoFetchFromUrl")] public bool AutoFetchFromUrl { get; set; } = true;
            [JsonProperty("FetchSourceUrl")] public string FetchSourceUrl { get; set; } =
                "https://raw.githubusercontent.com/OrangeWulf/Rust-Docs/master/Extended/Effects.md";
            [JsonProperty("FetchIntervalMinutes")] public int FetchIntervalMinutes { get; set; } = 360; // 6h
        }

        protected override void LoadDefaultConfig() => config = new PluginConfig();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<PluginConfig>() ?? new PluginConfig(); }
            catch { config = new PluginConfig(); }
            SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(config, true);
        #endregion

        #region === Paths ===
        private const string DataDirRel = "CustomItemLoader";
        private string DataDir => Path.Combine(Interface.Oxide.DataDirectory, DataDirRel);

        private string RefJson => Path.Combine(DataDir, "effects_reference.json");
        private string RefCsv => Path.Combine(DataDir, "effects_reference.csv");

        private string VerifiedJson => Path.Combine(DataDir, "effects_verified.json");
        private string VerifiedLegacyJson => Path.Combine(DataDir, "verified_effects.json");
        private string FxListTxt => Path.Combine(DataDir, "fxlist.txt");
        #endregion

        #region === State ===
        private readonly Dictionary<string, string> _aliasToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<(string alias, string path)> _verifyQueue = new Queue<(string alias, string path)>();
        private bool _scanWorkerRunning = false;
        private bool _dirty = false;

        // Timer 型は Oxide.Plugins.Timer（Every() の戻り値と一致）
        private Timer _fetchTimer;

        // Acquire WebRequests library (Oxide)
        private readonly Oxide.Core.Libraries.WebRequests webrequests =
            Interface.Oxide.GetLibrary<Oxide.Core.Libraries.WebRequests>("WebRequests");
        #endregion

        #region === Lifecycle ===
        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermUse, this);
            RegisterLangMessages();
        }

        private void OnServerInitialized()
        {
            EnsureDataDir();
            LoadVerified();
            EnqueueFromReferences();          // 既存CSV/JSONから取り込み
            StartAutoFetchIfEnabled();        // GitHub 自動取得
            if (config.AutoScanOnInit) StartScanWorker();
        }

        private void Unload()
        {
            TrySaveVerified();
            if (_fetchTimer != null) _fetchTimer.Destroy();
        }

        private void EnsureDataDir()
        {
            try { Directory.CreateDirectory(DataDir); }
            catch (Exception ex) { Puts($"[CIFP] データディレクトリ作成失敗: {ex.Message}"); }
        }

        private void StartAutoFetchIfEnabled()
        {
            try
            {
                if (config == null || !config.AutoFetchFromUrl) return;
                if (_fetchTimer != null) { _fetchTimer.Destroy(); _fetchTimer = null; }
                var seconds = Math.Max(60, config.FetchIntervalMinutes * 60);
                // immediate fetch
                FetchOnce();
                // periodic fetch
                _fetchTimer = timer.Every(seconds, () => FetchOnce());
            }
            catch { /* no-throw */ }
        }

        private void FetchOnce()
        {
            try
            {
                var url = config != null ? (config.FetchSourceUrl ?? "") : "";
                if (string.IsNullOrEmpty(url)) return;
                Puts(LConsole("FetchStart"));
                webrequests.EnqueueGet(url, (code, response) =>
                {
                    if (code != 200 || string.IsNullOrEmpty(response))
                    {
                        Puts(LConsole("FetchFail", code));
                        return;
                    }
                    var queued = ExtractAndQueueFromText(response);
                    if (queued == 0)
                    {
                        Puts(LConsole("FetchZero"));
                        return;
                    }
                    Puts(LConsole("FetchedSummary", queued, queued));
                    StartScanWorker();
                }, this, null, 20f);
            }
            catch { /* swallow */ }
        }

        private int ExtractAndQueueFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int queued = 0;
            try
            {
                var rx = new System.Text.RegularExpressions.Regex(@"\bassets/[a-z0-9/_\-\.\(\)]+\.prefab\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var raw in text.Replace("\r","").Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("```") || line.StartsWith(">")) continue;
                    line = System.Text.RegularExpressions.Regex.Replace(line, @"\[[^\]]+\]\([^)]+\)", "");
                    var m = rx.Match(line);
                    if (!m.Success) continue;
                    var path = m.Value;
                    if (config != null && config.ExcludeSceneEffects &&
                        path.StartsWith("assets/content/effects/", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(path)) continue;

                    var fname = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(fname)) continue;
                    var alias = fname;
                    int idx = 2;
                    while (_aliasToPath.ContainsKey(alias))
                    {
                        alias = fname + "_" + idx;
                        idx++;
                    }
                    if (Enqueue(alias, path)) queued++;
                }
            }
            catch (System.Exception ex) { Puts($"[CIFP] Extract error: {ex.Message}"); }
            return queued;
        }

        private void StartScanWorker()
        {
            if (_scanWorkerRunning) return;
            _scanWorkerRunning = true;

            var interval = Math.Max(0.01f, config?.ScanInterval ?? 0.05f);
            timer.Every(interval, () =>
            {
                if (_verifyQueue.Count == 0)
                {
                    _scanWorkerRunning = false;
                    if (_dirty) TrySaveVerified();
                    return;
                }
                var batch = Math.Max(1, config?.ScanBatchSize ?? 64);
                var processed = 0;
                while (processed < batch && _verifyQueue.Count > 0)
                {
                    var item = _verifyQueue.Dequeue();
                    if (!string.IsNullOrEmpty(item.alias) && !string.IsNullOrEmpty(item.path))
                    {
                        _aliasToPath[item.alias] = item.path;
                        _dirty = true;
                    }
                    processed++;
                }
            });
        }
        #endregion

        #region === Commands ===
        [ChatCommand("cifp")]
        private void CmdCifp(BasePlayer player, string command, string[] args)
        {
            var id = player?.UserIDString ?? "console";
            if (player != null &&
                !permission.UserHasPermission(id, PermAdmin) &&
                !permission.UserHasPermission(id, PermUse))
            {
                SendReply(player, L("NoPerm", player, id));
                return;
            }

            if (args == null || args.Length == 0)
            {
                SendReply(player, L("Help", player));
                return;
            }

            var sub = args[0].ToLowerInvariant();
            switch (sub)
            {
                case "fetch":
                    FetchOnce();
                    SendReply(player, L("FetchStart", player));
                    break;

                case "scan":
                    EnqueueFromReferences();
                    StartScanWorker();
                    SendReply(player, L("ScanStart", player));
                    break;

                case "apply":
                    TrySaveVerified();
                    SendReply(player, L("ApplyDone", player));
                    break;

                case "report":
                    SendReply(player, L("Report", player, _aliasToPath.Count));
                    break;

                case "lookup":
                    if (args.Length < 2)
                    {
                        SendReply(player, L("LookupUsage", player));
                        return;
                    }
                    var alias = args[1];
                    if (_aliasToPath.TryGetValue(alias, out var path))
                        SendReply(player, L("LookupHit", player, alias, path));
                    else
                        SendReply(player, L("LookupMiss", player, alias));
                    break;

                default:
                    SendReply(player, L("UnknownCmd", player));
                    break;
            }
        }
        #endregion

        #region === i18n ===
        private void RegisterLangMessages()
        {
            // English
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPerm"] = "You do not have permission. ({0})",
                ["Help"] = "/cifp fetch | scan | apply | report | lookup <alias>",
                ["FetchStart"] = "Start fetching FX list from GitHub. It will scan automatically after download.",
                ["FetchFail"] = "Fetch failed. code={0}",
                ["FetchZero"] = "No FX prefab paths were found in the fetched text.",
                ["FetchedSummary"] = "Fetched {0} items from GitHub / queued {1}.",
                ["ScanStart"] = "Scan started/continued.",
                ["ApplyDone"] = "Applied (saved & notified to CIL).",
                ["Report"] = "Registered: {0}",
                ["LookupUsage"] = "Usage: /cifp lookup <alias>",
                ["LookupHit"] = "alias='{0}' => {1}",
                ["LookupMiss"] = "alias='{0}' is not registered.",
                ["UnknownCmd"] = "Unknown command. Type /cifp for help.",
                ["CsvSavedFail"] = "Failed to write effects_reference.csv: {0}",
            }, this, "en");

            // Japanese
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPerm"] = "権限がありません。（{0}）",
                ["Help"] = "/cifp fetch | scan | apply | report | lookup <alias>",
                ["FetchStart"] = "GitHub から FX 一覧の取得を開始しました。完了後に自動スキャンします。",
                ["FetchFail"] = "取得に失敗しました。code={0}",
                ["FetchZero"] = "取得したテキスト内に FX のプレハブパスが見つかりませんでした。",
                ["FetchedSummary"] = "GitHub から {0} 件を抽出 / キュー投入 {1} 件。",
                ["ScanStart"] = "スキャン開始/継続。",
                ["ApplyDone"] = "反映完了（保存・CIL通知）。",
                ["Report"] = "登録済み: {0} 件",
                ["LookupUsage"] = "使い方: /cifp lookup <alias>",
                ["LookupHit"] = "alias='{0}' => {1}",
                ["LookupMiss"] = "alias='{0}' は未登録です。",
                ["UnknownCmd"] = "不明なコマンドです。/cifp でヘルプ。",
                ["CsvSavedFail"] = "effects_reference.csv の書き込みに失敗: {0}",
            }, this, "ja");
        }

        private string GetLang(BasePlayer player = null)
        {
            var code = config?.DefaultLang ?? "en";
            if (player != null)
            {
                var p = lang.GetLanguage(player.UserIDString);
                if (!string.IsNullOrEmpty(p)) code = p;
            }
            return code;
        }

        private string L(string key, BasePlayer player = null, params object[] args)
        {
            var id = player?.UserIDString;
            var msg = lang.GetMessage(key, this, id);
            return (args != null && args.Length > 0) ? string.Format(msg, args) : msg;
        }

        private string LConsole(string key, params object[] args)
        {
            var msg = lang.GetMessage(key, this); // server/global language
            return (args != null && args.Length > 0) ? string.Format(msg, args) : msg;
        }
        #endregion

        #region === Data I/O ===
        private void LoadVerified()
        {
            _aliasToPath.Clear();
            try
            {
                if (File.Exists(VerifiedJson))
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(VerifiedJson));
                    if (dict != null) foreach (var kv in dict) _aliasToPath[kv.Key] = kv.Value;
                }
                else if (File.Exists(VerifiedLegacyJson)) // fallback
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(VerifiedLegacyJson));
                    if (dict != null) foreach (var kv in dict) _aliasToPath[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex) { Puts($"[CIFP] Verified読込失敗: {ex.Message}"); }
        }

        private void TrySaveVerified()
        {
            if (!config.ExportOnChange && !_dirty) return;
            try
            {
                File.WriteAllText(VerifiedJson, JsonConvert.SerializeObject(_aliasToPath, Formatting.Indented));
                if (config.WriteLegacyFiles)
                {
                    File.WriteAllText(VerifiedLegacyJson, JsonConvert.SerializeObject(_aliasToPath, Formatting.Indented));
                    File.WriteAllLines(FxListTxt, new HashSet<string>(_aliasToPath.Values, StringComparer.OrdinalIgnoreCase));
                }
                _dirty = false;
            }
            catch (Exception ex) { Puts($"[CIFP] Verified書込失敗: {ex.Message}"); }
        }
        #endregion

        #region === Import references ===
        private class EffectRefItem
        {
            public string alias;
            public string prefab_path;
            public bool requires_scene; // optional
        }

        private bool Enqueue(string alias, string path)
        {
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(path)) return false;
            alias = alias.Trim();
            path = path.Trim();

            string existing;
            if (_aliasToPath.TryGetValue(alias, out existing) &&
                existing != null && existing.Equals(path, System.StringComparison.OrdinalIgnoreCase))
                return false;

            _verifyQueue.Enqueue((alias, path));
            return true;
        }

        private void EnqueueFromReferences()
        {
            int added = 0;

            // JSON
            if (config.EnableJsonImport && File.Exists(RefJson))
            {
                try
                {
                    var arr = JsonConvert.DeserializeObject<List<EffectRefItem>>(File.ReadAllText(RefJson)) ?? new List<EffectRefItem>();
                    foreach (var it in arr)
                    {
                        if (it == null || string.IsNullOrEmpty(it.alias) || string.IsNullOrEmpty(it.prefab_path)) continue;
                        if (config.ExcludeSceneEffects && (it.requires_scene || it.prefab_path.StartsWith("assets/content/effects/", StringComparison.OrdinalIgnoreCase))) continue;
                        added += Enqueue(it.alias.Trim(), it.prefab_path.Trim()) ? 1 : 0;
                    }
                }
                catch (Exception ex) { Puts($"[CIFP] JSON参照読込失敗: {ex.Message}"); }
            }

            // CSV（alias,prefab_path,requires_scene）
            if (config.EnableCsvImport && File.Exists(RefCsv))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(RefCsv))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var cols = line.Split(',');
                        if (cols.Length < 2) continue;
                        var alias = cols[0]?.Trim();
                        var path = cols[1]?.Trim();
                        var requiresScene = (cols.Length >= 3 && (cols[2].Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || cols[2].Trim() == "1"));
                        if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(path)) continue;
                        if (config.ExcludeSceneEffects && (requiresScene || path.StartsWith("assets/content/effects/", StringComparison.OrdinalIgnoreCase))) continue;
                        added += Enqueue(alias, path) ? 1 : 0;
                    }
                }
                catch (Exception ex) { Puts($"[CIFP] CSV参照読込失敗: {ex.Message}"); }
            }

            if (added > 0) _dirty = true;
        }
        #endregion
    }
}
