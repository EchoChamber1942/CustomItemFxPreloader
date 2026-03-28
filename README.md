# CustomItemFxPreloader (CIFP)

**Bilingual README / 英和README**

CustomItemFxPreloader (CIFP) is a lightweight helper plugin for Rust that discovers and manages FX prefab paths for other plugins, especially CustomItemLoader.  
CustomItemFxPreloader（CIFP）は、Rust向けの軽量補助プラグインで、主に CustomItemLoader など他プラグイン向けに FX prefab path の収集・管理を行います。

It can import effect references from JSON/CSV files, periodically fetch new FX paths from a GitHub source, queue them for processing, and write a verified alias map and fxlist-style files under `oxide/data/CustomItemLoader/`.  
JSON/CSV からエフェクト参照を取り込み、GitHub ソースから新しい FX path を定期取得し、処理キューへ投入し、検証済み alias map や fxlist 形式のファイルを `oxide/data/CustomItemLoader/` 配下へ出力できます。

> **Note / 補足**  
> This plugin does **not** spawn effects by itself. It maintains verified FX alias data for other plugins to consume.  
> このプラグイン自体は **エフェクトを直接再生しません**。他プラグインが利用するための、検証済みFX aliasデータを維持します。

---

## Overview / 概要

### English
**Main purposes:**
- Discover and maintain FX alias → prefab mappings
- Import FX references from JSON and CSV sources
- Fetch FX reference data from a configurable GitHub URL
- Queue and process FX paths for verification
- Export verified FX data for other plugins
- Keep `effects_verified.json` and optional legacy files up to date

### 日本語
**主な用途:**
- FX alias → prefab の対応関係を収集・維持
- JSON / CSV ソースからFX参照を取り込み
- 設定可能なGitHub URLからFX参照データを取得
- FX path をキュー処理して検証
- 他プラグイン向けに検証済みFXデータを出力
- `effects_verified.json` と任意の旧形式ファイルを最新化

---

## Integrations / Recommended Plugins  
## 連携プラグイン / 推奨連携

### 1. CustomItemLoader *(recommended / 推奨)*
**EN:** CustomItemFxPreloader is designed to work together with CustomItemLoader. CustomItemLoader can treat the verified alias map and `fxlist.txt` written by this plugin as a trusted source of FX paths for lightsabers and other custom items.  
**JP:** CustomItemFxPreloader は CustomItemLoader と併用することを前提に設計されています。本プラグインが出力する検証済み alias map や `fxlist.txt` は、ライトセーバーや他のカスタムアイテム用FX pathの信頼できる参照元として CustomItemLoader 側で利用できます。

### 2. External FX Reference Sources *(optional / 任意)*
**EN:** The plugin can import FX references from `effects_reference.json` and `effects_reference.csv` in the CustomItemLoader data folder, and can auto-fetch FX paths from a configurable GitHub URL.  
**JP:** `effects_reference.json` および `effects_reference.csv` から FX 参照を取り込めます。また、設定可能な GitHub URL から FX path を自動取得することもできます。

---

## Permissions / 権限

### English
This plugin uses the Oxide permission system.

**Grant**
```txt
oxide.grant <user or group> <name or steam id> <permission>
```

**Revoke**
```txt
oxide.revoke <user or group> <name or steam id> <permission>
```

**Defined permissions**
- `customitemfxpreloader.admin`  
  Full access to all `/cifp` commands
- `customitemfxpreloader.use`  
  Allows non-admin users to run `/cifp` commands

If a player has neither permission, `/cifp` will be rejected.

### 日本語
このプラグインは Oxide の権限システムを使用します。

**付与**
```txt
oxide.grant <user or group> <name or steam id> <permission>
```

**剥奪**
```txt
oxide.revoke <user or group> <name or steam id> <permission>
```

**定義済み権限**
- `customitemfxpreloader.admin`  
  すべての `/cifp` コマンドへフルアクセス
- `customitemfxpreloader.use`  
  非管理者ユーザーによる `/cifp` コマンド実行を許可

どちらの権限も持たないプレイヤーは `/cifp` を使用できません。

---

## Chat Commands / チャットコマンド

| Command | English | 日本語 |
|---|---|---|
| `/cifp` | Shows help for all CIFP sub-commands | CIFPサブコマンドのヘルプを表示 |
| `/cifp fetch` | Immediately fetches effect paths from the configured GitHub URL and queues them for scanning | 設定済みGitHub URLから即時取得し、スキャンキューへ投入 |
| `/cifp scan` | Imports references from JSON/CSV and starts or restarts the scan worker | JSON/CSV から参照を取り込み、スキャンワーカーを開始または再開 |
| `/cifp apply` | Forces saving of the verified alias map and legacy files | 検証済み alias map と旧形式ファイルの保存を強制実行 |
| `/cifp report` | Shows how many FX aliases are currently registered | 現在登録されているFX alias数を表示 |
| `/cifp lookup <alias>` | Looks up a single alias and prints the mapped prefab path | 指定 alias の対応 prefab path を表示 |

**EN:** All commands use the `/cifp` prefix and require either `customitemfxpreloader.admin` or `customitemfxpreloader.use`.  
**JP:** すべてのコマンドは `/cifp` プレフィックスを使用し、`customitemfxpreloader.admin` または `customitemfxpreloader.use` のいずれかが必要です。

---

## Configuration / 設定

### English
Settings are configured in:
```txt
oxide/config/CustomItemFxPreloader.json
```

Using a JSON editor and validator is recommended to avoid formatting issues and syntax errors.

**Main options**
- `DefaultLang` — Default language code (`"en"` or `"ja"`)
- `AutoScanOnInit` — Starts the scan worker automatically on server init
- `ScanBatchSize` — Number of queued items processed per scan tick
- `ScanInterval` — Interval in seconds between scan ticks
- `EnableCsvImport` — Enables import from `effects_reference.csv`
- `EnableJsonImport` — Enables import from `effects_reference.json`
- `ExportOnChange` — Saves verified alias data automatically when changed
- `PublishOnChange` — Reserved for notifying other plugins in the future
- `WriteLegacyFiles` — Also writes `verified_effects.json` and `fxlist.txt`
- `ExcludeSceneEffects` — Ignores scene-only FX such as `assets/content/effects/`
- `AutoFetchFromUrl` — Enables periodic auto-fetch from the configured GitHub URL
- `FetchSourceUrl` — Source URL used to fetch FX reference text
- `FetchIntervalMinutes` — Interval in minutes between automatic fetches

### 日本語
設定ファイル:
```txt
oxide/config/CustomItemFxPreloader.json
```

書式崩れや構文エラーを避けるため、JSONエディタやバリデータの利用を推奨します。

**主な設定項目**
- `DefaultLang` — 既定の言語コード（`"en"` または `"ja"`）
- `AutoScanOnInit` — サーバー初期化時にスキャンワーカーを自動開始
- `ScanBatchSize` — 1回のスキャンtickで処理するキュー件数
- `ScanInterval` — スキャンtick間隔（秒）
- `EnableCsvImport` — `effects_reference.csv` からの取り込みを有効化
- `EnableJsonImport` — `effects_reference.json` からの取り込みを有効化
- `ExportOnChange` — 変更時に検証済み alias データを自動保存
- `PublishOnChange` — 将来的な他プラグイン通知用の予約項目
- `WriteLegacyFiles` — `verified_effects.json` と `fxlist.txt` も出力
- `ExcludeSceneEffects` — `assets/content/effects/` などの scene-only FX を除外
- `AutoFetchFromUrl` — 設定済みGitHub URLからの定期自動取得を有効化
- `FetchSourceUrl` — FX参照テキスト取得元のURL
- `FetchIntervalMinutes` — 自動取得の実行間隔（分）

---

## Data Folder & Generated Files  
## データ保存先と生成ファイル

### English
All files are stored under:
```txt
oxide/data/CustomItemLoader/
```

**Input files**
- `effects_reference.json` — Optional JSON reference list of FX aliases
- `effects_reference.csv` — Optional CSV reference list of FX aliases

**Output files**
- `effects_verified.json` — Main verified alias map written by this plugin
- `verified_effects.json` — Legacy alias map for older tools *(optional)*
- `fxlist.txt` — Flat list of verified FX prefab paths *(optional)*

### 日本語
すべてのファイルは次の場所に保存されます:
```txt
oxide/data/CustomItemLoader/
```

**入力ファイル**
- `effects_reference.json` — FX alias の任意JSON参照リスト
- `effects_reference.csv` — FX alias の任意CSV参照リスト

**出力ファイル**
- `effects_verified.json` — 本プラグインが出力するメインの検証済み alias map
- `verified_effects.json` — 一部旧ツール互換用の旧形式 alias map（任意）
- `fxlist.txt` — 検証済みFX prefab path のフラットリスト（任意）

---

## Notes / 補足

### English
- This plugin focuses on collecting and maintaining FX aliases and paths. It does not spawn effects directly.
- Best results are achieved when running together with CustomItemLoader, which consumes the verified FX data for lightsabers and other custom items.
- The plugin includes English and Japanese messages via the Oxide lang system.

### 日本語
- このプラグインは FX alias と path の収集・維持に特化しており、エフェクト自体を直接再生しません。
- 検証済みFXデータを消費する CustomItemLoader と併用したときに、最も高い効果を発揮します。
- Oxide の lang システムにより、英語と日本語のメッセージを備えています。

---

## Summary / まとめ

**EN:** CustomItemFxPreloader is a helper plugin for building and maintaining a trusted FX reference layer for other Rust plugins, especially CustomItemLoader.  
**JP:** CustomItemFxPreloader は、主に CustomItemLoader など他のRustプラグイン向けに、信頼できるFX参照レイヤーを構築・維持するための補助プラグインです。
