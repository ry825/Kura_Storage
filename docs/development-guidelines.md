# 開発ガイドライン（Development Guidelines）

## 文書情報

| 項目 | 内容 |
| --- | --- |
| プロダクト名 | KuraStorage |
| 文書種別 | Development Guidelines |
| バージョン | 0.3.0 Draft |
| 作成日 | 2026-07-12 |
| 参照文書 | `docs/functional-design.md` Version 0.9.0 Draft |
| 参照文書 | `docs/architecture-design.md` Version 0.3.0 Draft |
| 関連文書 | `docs/repository-structure.md` |
| 対象フェーズ | Phase 1: Androidアプリ＋Raspberry Piバックエンド |

---

## Phase 1 ZeroTier開発規約

- KuraStorageにZeroTier SDK、Controller API連携、Network・Member管理、Node Identity管理、接続・切断操作を実装しない。
- Androidは別のZeroTierアプリが確立したZeroTier Networkを通常のHTTPS経路として利用する。
- ServerはOS管理ZeroTier InterfaceをNginx／Firewallの許可経路として扱うだけとする。
- IP Address、Hostname、Network ID、端末情報、証明書Path／FingerprintはGit管理外の`docs/environment-info.md`へ置き、Tracked Fileでは`docs/environment-info.example.md`の項目IDを参照する。
- Secretは環境情報文書にも置かず、既存のSecret Store、権限制限File、CI Secretを使用する。

将来の自己管理WireGuard、VPSを介したVPN、その他のオーバーレイ方式は現行の実装対象に含めない。導入時は専用の脅威分析、設計、テスト、運用手順を追加し、接続方式固有の処理をファイル管理や認証ドメインへ混入させない。

---

## MVP実装規約

- MVPに必要なHost、Project、Android Module、DB Table、API、依存だけを追加する。Worker、Room、WorkManager、Media3、Coil、PDF、FFmpeg、Popplerを先行追加しない。
- Uploadは`multipart/form-data`の単一File Partを逐次読み取り、Client、Nginx、APIで全体Bufferingしない。`Idempotency-Key`、期待Size、任意SHA-256を検証する。
- Upload中断は同じKeyで先頭から全体再試行する。Chunk Uploadまたは中断位置からの再開をMVPへ混在させない。
- HDD更新前に`FileOperation(PENDING)`を記録し、`FILESYSTEM_DONE`、`COMPLETED`へ進める。自動判定できない失敗は`RECOVERY_REQUIRED`とする。
- AndroidのUpload元とDownload先はStorage Access Frameworkの`content://` URIとして扱い、物理Pathへの変換、全体ByteArray化、不要な永続権限取得を禁止する。
- Trash・Restoreの同名競合では既存項目を上書きしない。Rename、Move、Permanent DeleteはMVP後とする。
- API内Hosted Serviceは未完了Upload清掃と`FileOperation`復旧だけに限定し、長時間Media処理や自動Backupを実行しない。

---

## 1. 目的と適用範囲

本書は、KuraStorageの実装、テスト、レビュー、Git運用、依存関係更新、リリースで守る共通ルールを定義する。

対象は次のとおり。

- C# / .NETによるDomain、Application、Infrastructure、API、管理CLI（独立WorkerはMVP後）
- KotlinによるAndroidアプリ
- PostgreSQLのスキーマ、Migration、Query
- Bash等の開発・配置・運用スクリプト
- ユニット、統合、Android、E2E、性能、セキュリティテスト
- Git、Pull Request、CI、リリース、依存関係管理

設計上の判断が競合した場合の優先順位は次のとおりとする。

1. `docs/product-requirements.md`
2. `docs/functional-design.md`
3. `docs/architecture-design.md`
4. `docs/repository-structure.md`
5. 本書
6. 各ライブラリの一般的な慣習

本書と上位文書の間に矛盾を見つけた場合、実装で独自判断して吸収せず、文書を修正してから実装する。

---

## 2. 開発の基本原則

### 2.1 アーキテクチャ境界を守る

依存方向は次のとおりとする。

```text
API / AdminCli / Android UI
                         ↓
                    Application
                         ↓
                       Domain

Infrastructure ── ApplicationまたはDomainが定義したinterfaceを実装
```

- DomainはASP.NET Core、EF Core、Npgsql、Android SDK、FFmpeg、ファイルシステムへ依存しない。
- Applicationはユースケース、認可、トランザクション境界、状態遷移を制御する。
- InfrastructureはDB、HDD、暗号、外部プロセス、systemd等の技術詳細を実装する。
- Presentation層は入力形式の検証とApplicationへの委譲を行い、業務ルールを持たない。
- API、CLIからDbContextや物理ファイルを直接操作しない。

### 2.2 セキュリティを既定値にする

- 保護APIは既定で認証必須とし、匿名エンドポイントだけを明示する。
- API入力の`userId`、`deviceId`、物理パスを認証・認可の根拠にしない。
- すべてのファイル操作で、サーバー側の認証、Device状態、Session状態、権限、Storage状態を確認する。
- APIプロセスをrootで実行しない。
- root処理へユーザー入力、任意パス、任意コマンドを渡さない。
- SQL、シェル、FFmpeg引数を文字列連結で組み立てない。

### 2.3 ファイルシステムとDBの途中状態を設計する

HDD操作とDB更新は単一トランザクションにできない。次を必須とする。

- 更新前に`FileOperation`を作成する。
- 一時ファイルへの書き込み、検証、atomic renameの順で正式公開する。
- 途中失敗を`RECOVERY_REQUIRED`として記録する。
- 再試行しても結果が壊れない冪等な処理にする。
- 失敗時に元ファイルを削除しない。
- DBだけ成功、HDDだけ成功の両方を統合テストする。

### 2.4 TDDを基本とする

実装は原則として次の順で進める。

1. **Red**: 要件を表す失敗テストを追加する。
2. **Green**: テストを通す最小限の実装を行う。
3. **Refactor**: 重複、命名、責務、依存方向を改善する。
4. **Verify**: 関連テスト、静的解析、実機依存テストを実行する。

例外は、短期間の技術検証用コード、OS・ライブラリの挙動調査、使い捨ての性能測定に限定する。検証コードを本実装へ移す場合は、先にテストを追加する。

### 2.5 測定可能な完了条件を使用する

「適切に」「十分に」「高速に」だけで完了としない。次のような確認可能な条件を定義する。

- 対象APIが設計されたHTTP StatusとError Codeを返す。
- 認証・認可・状態遷移・パス処理のユニットカバレッジが95%以上である。
- Domain/Application全体のユニットカバレッジが80%以上である。
- 30万件データで代表Queryの性能目標を満たす。
- Raspberry Pi実機とAndroid実機で対象E2Eが成功する。

---

## 3. 共通コーディング規約

### 3.1 文字コード・改行・インデント

| 対象 | 規則 |
| --- | --- |
| 文字コード | UTF-8、BOMなし |
| 改行 | LF |
| C# | 4スペース |
| Kotlin / Gradle Kotlin DSL | 4スペース |
| YAML / JSON / Markdown | 2スペースを基本とする |
| 行末空白 | 禁止 |
| ファイル末尾 | 改行を1つ置く |
| 1行の長さ | 原則120文字以下。URL、ログテンプレート、生成コードは例外可 |

`.editorconfig`をリポジトリの正とし、個人IDE設定で上書きしない。

### 3.2 命名の基本

- 名前は役割と対象が分かる具体的な英語にする。
- `data`、`info`、`item`、`manager`、`helper`、`util`の単独使用を避ける。
- 略語は一般的なものに限定する。`Id`、`Api`、`Url`を使用し、`ID`、`API`、`URL`を識別子途中で混在させない。
- Booleanは`Is`、`Has`、`Can`、`Should`、`Requires`、`is`、`has`等で始める。
- 非同期C#メソッドは`Async`で終える。Kotlinの`suspend`関数には`Async`を付けない。
- 単位を値名に含める。例: `timeoutSeconds`、`receivedBytes`、`durationMs`。
- 日時は原則UTCで保持し、`At`を付ける。例: `createdAt`、`ExpiresAt`。

### 3.3 コメント

コメントは「何をしているか」ではなく、コードだけでは分からない「なぜ」を説明する
また、必ず英語で記載する。

```csharp
// 悪い例: statusをCOMPLETEDへ変更する
operation.Status = FileOperationStatus.Completed;

// 良い例: HDDとDBの両方が確定した後だけ、復旧対象から除外する
operation.MarkCompleted(clock.UtcNow);
```

- 公開API、Application interface、セキュリティ境界、複雑な状態遷移にはドキュメントコメントを付ける。
- `TODO`、`FIXME`、`HACK`にはIssue番号を付ける。
- コメントアウトしたコードを残さない。Git履歴を使用する。
- 文書と実装が食い違う場合、コメントで説明して放置せず文書を更新する。

### 3.4 マジックナンバーと設定値

次の値をコードへ直接散在させない。

- Access Token 15分
- Refresh Token 24時間
- 認証失敗15分・10回
- Device上限10台
- キャッシュ上限10GB・清掃後6GB・TTL 24時間
- ゴミ箱30日
- テキスト取得・編集上限1 MiB
- 動画変換並列数1
- ページサイズ初期値100・最大500

業務上固定された値はDomain/Applicationの型付きOptionsまたは定数へ置く。環境差がある値は設定ファイルと環境変数から注入する。設定値には起動時検証を実装する。

### 3.5 ファイルサイズと責務

- 1ファイル300行以下を目安とする。
- 300〜500行では責務分割を検討する。
- 500行を超える手書きコードは原則分割する。
- 1メソッドは50行以下を目安とし、100行を超える場合は分割する。
- 4個を超える関連パラメータはCommand、Options、Value Objectへまとめる。
- 行数だけを理由に、不自然な委譲クラスや意味のない分割を作らない。

---

## 4. C# / .NET実装規約

### 4.1 言語設定

- Nullable Reference Typesを有効にする。
- Implicit Usingsはプロジェクト単位で統一する。
- Warningを原則Errorとして扱う。抑制する場合は理由をコードまたはプロジェクト設定へ記載する。
- file-scoped namespaceを使用する。
- `var`は右辺から型が明確な場合に使用する。業務上重要な型が隠れる場合は明示する。
- `dynamic`は使用しない。
- `DateTime.Now`、`Guid.NewGuid()`の直接使用をDomain/Applicationで禁止し、`IClock`、`IIdGenerator`を注入する。

### 4.2 命名

| 要素 | 規則 | 例 |
| --- | --- | --- |
| namespace / class / record / enum | PascalCase | `FileEntry`, `RefreshSessionStatus` |
| public member / method | PascalCase | `ResolvePermissionAsync` |
| private field | `_camelCase` | `_fileEntryRepository` |
| parameter / local variable | camelCase | `sourceFileId` |
| interface | `I`＋PascalCase | `IStorageGuard` |
| async method | `Async`接尾辞 | `CreateUploadSessionAsync` |
| CancellationToken | `cancellationToken` | 最後の引数に置く |
| test project | `[ProductionProject].Tests`等 | `KuraStorage.Domain.Tests` |

### 4.3 Domain

- Entityは不変条件を自身のメソッドで守る。setterを外部へ公開しない。
- 状態変更は`MarkTrashed`、`Revoke`、`Rotate`のような業務上の動詞で表す。
- ID、相対パス、接続元Address、権限等はValue Object化を検討する。ZeroTier固有のMemberやManaged IPをDomain Entityにしない。
- Domain ErrorはHTTP StatusやDB例外を知らない。
- Domain ServiceはEntity単体に置けない純粋な業務ルールに限定する。
- `FileEntry`に`DELETED`状態を追加しない。完全削除は行削除として扱う。
- 名前変更・移動だけで`fileVersion`を増加させない。

```csharp
public sealed class FileEntry
{
    public FileEntryStatus Status { get; private set; }
    public long FileVersion { get; private set; }

    public void MarkTrashed(DateTimeOffset occurredAt, RelativeStoragePath originalPath)
    {
        if (Status is not FileEntryStatus.Active)
        {
            throw new InvalidFileStateTransitionError(Status, FileEntryStatus.Trashed);
        }

        Status = FileEntryStatus.Trashed;
        TrashedAt = occurredAt;
        OriginalRelativePath = originalPath;
    }
}
```

### 4.4 Application

- ユースケースごとにCommandまたはQueryとHandlerを定義する。
- Handlerは入力、認証コンテキスト、Repository、外部interfaceを組み合わせる。
- CommandとQueryを同じHandlerへ混在させない。
- DBトランザクション境界はApplicationで明示する。
- 他モジュールのテーブルをDbContext経由で直接更新せず、公開Application interfaceを使用する。
- 認可はEndpointだけでなく、Applicationでも必ず実行する。
- `CancellationToken`をAPI、Application、Infrastructureまで伝播する。
- 予期される失敗は型付きApplication Errorとして返し、例外を通常分岐に使用しない。

### 4.5 InfrastructureとEF Core

- RepositoryはDomain/Applicationが必要とする操作だけを公開する。
- `IQueryable`をInfrastructure外へ返さない。
- 読み取り専用Queryでは原則`AsNoTracking()`を使用する。
- 一覧QueryでN+1を発生させない。必要な列だけをDTOへProjectionする。
- 生SQLは、再帰CTE、部分Indexを活用する権限Query等、EFで不明瞭になる場合に限定する。
- 生SQLは必ずパラメータ化し、SQL文字列連結を禁止する。
- MigrationにはUpとDownを実装する。ただし不可逆なデータ削除は明示的な運用手順を用意する。
- API起動時にMigrationを自動適用しない。管理CLIから明示的に実行する。
- DB制約で保証できる不変条件はApplication検証だけにせず、一意制約、外部キー、Check Constraintでも保証する。

### 4.6 非同期・並行処理

- I/O処理はasync/awaitを使用する。
- `.Result`、`.Wait()`、`GetAwaiter().GetResult()`をアプリケーションコードで使用しない。
- 非同期メソッド内で同期ファイルI/Oを使用しない。
- 並列実行は負荷と上限を明示する。
- MVPの同時Upload数はServer全体で2を初期値とし、設定で制限する。
- Media Job、永続Queue、独立Workerの並行処理規約はMVP後に追加する。

### 4.7 例外・エラー処理

- Domain/Applicationの予期されるエラーを、API境界で共通Error Responseへ変換する。
- Infrastructure例外をそのままクライアントへ返さない。
- catchして無視しない。
- 例外を再throwする場合は`throw;`を使用し、Stack Traceを保持する。
- 予期しない例外はrequestIdとともにErrorログへ記録する。
- 認証エラーでUserの存在有無を推測できる文言を返さない。

### 4.8 ストレージ操作

- 物理パスの解決は`StorageGuard`だけが行う。
- `../`、絶対パス、NUL、不正区切り、ストレージルート外、シンボリックリンクを拒否する。
- すべての書き込みでHDDのmount、`storageId`、書込み可否、空き容量を確認する。
- ファイル全体をメモリへ読み込まない。MVP Uploadは逐次Stream、DownloadはRangeを使用する。
- 一時ファイルは正式ファイルと同一Filesystemに置き、atomic renameを可能にする。
- `File.Open`等をPresentation層から直接呼び出さない。

### 4.9 MVP後: 外部プロセス

- FFmpeg、systemctl等の外部コマンドをシェル文字列として実行しない。
- `ProcessStartInfo.ArgumentList`等で引数を個別に渡す。
- 入出力パスは内部で生成した検証済み値だけを使用する。
- Timeout、Cancellation、終了コード、標準出力、標準エラー、出力サイズを扱う。
- 失敗時に生成途中ファイルをREADYとして公開しない。

### 4.10 ログ

構造化ログのMessage Templateを使用する。

```csharp
logger.LogInformation(
    "Upload completed. OperationId={OperationId} FileId={FileId} ReceivedBytes={ReceivedBytes}",
    fileOperation.Id,
    fileEntry.Id,
    uploadSession.ReceivedBytes);
```

禁止例。

```csharp
logger.LogInformation($"token={refreshToken} path={absolutePath}");
```

- requestId、userId、deviceId、fileId、operationId等を必要な範囲でScopeへ入れる。
- 物理絶対パスは通常ログへ出さない。
- 失敗理由はError Codeと例外を分けて記録する。
- 同じ失敗を複数レイヤーで重複してErrorログへ記録しない。

### 4.11 パスワードハッシュ

- パスワードはArgon2id v1.3（`v=19`）でハッシュ化する。
- Saltは設定・再設定ごとに暗号学的に安全な16バイト乱数を生成する。
- MVP初期値はメモリ19MiB、反復2回、並列度1とし、これ未満へ弱めない。
- DBにはAlgorithm、Version、Parameter、Salt、Hashを含む自己記述形式だけを保存する。
- 平文、復号可能な暗号文、MD5、SHA-1、SHA-256、SHA-512等の単純高速Hashを使用しない。
- Hash生成・検証は保守されているLibraryを使用し、独自暗号実装を行わない。
- Login成功時に保存済みParameterが現在基準より弱い場合、入力済みPasswordを現在設定で再Hashする。
- Password再設定時は対象UserのRefresh Sessionを失効する。
- Password、Salt、HashをDebug Logや監査Logへ出力しない。

---

## 5. Kotlin / Android実装規約

### 5.1 基本

- Kotlin公式コーディング規約を基準とし、`ktlint`でFormattingと機械的なStyleを統一する。
- `detekt`で複雑度、潜在Bug、命名を含む静的解析を行う。
- Android固有のCorrectness、Security、Performance、Accessibility、Resource問題はAndroid Lintで検証する。
- `!!`は原則禁止する。使用する場合は不変条件と理由をコメントする。
- Platform Typeを境界でnullableまたはnon-nullへ明示する。
- Mutableな状態を外部へ公開しない。
- 日時、サイズ、接続経路、品質等はStringやIntの乱用を避け、enumまたはValue Typeで表す。
- Android ContextをDomain層やViewModelへ長期保持しない。

### 5.2 命名

| 要素 | 規則 | 例 |
| --- | --- | --- |
| class / interface / object / enum | PascalCase | `ConnectionCoordinator` |
| function / property | camelCase | `observeConnectionState` |
| compile-time constant | UPPER_SNAKE_CASE | `MAX_PAGE_SIZE` |
| Composable | PascalCase、名詞 | `FileListScreen` |
| ViewModel | `[Feature]ViewModel` | `FileListViewModel` |
| UI state | `[Feature]UiState` | `MediaViewerUiState` |
| event / action | `[Feature]Action` | `FileListAction` |
| Room entity | `[Name]Entity` | `LocalSyncItemEntity` |
| DAO | `[Name]Dao` | `LocalSyncItemDao` |
| Worker | `[Purpose]Worker` | `BackupUploadWorker` |

### 5.3 Composeと状態管理

- Composableは状態を受け取りEventを返す、可能な限りstatelessな構造にする。
- Screen-level ComposableだけがViewModelを取得する。
- Navigation、Toast、Snackbar等の一回限りのEventを永続UI Stateと混同しない。
- `Loading`、`Content`、`Empty`、`Generating`、`Disconnected`、`AuthenticationRequired`、`DeviceRevoked`、`StorageUnavailable`、`RecoverableError`、`FatalError`を明示する。
- 接続、認証、Device、Storageを単一Booleanに潰さない。
- `LaunchedEffect`のkeyを明示し、再CompositionによるAPI重複呼び出しを防ぐ。
- 大きな一覧では安定したkeyを使用する。

```kotlin
sealed interface FileListUiState {
    data object Loading : FileListUiState
    data class Content(val entries: List<FileListItem>) : FileListUiState
    data object Empty : FileListUiState
    data class RecoverableError(val code: ErrorCode) : FileListUiState
}
```

### 5.4 CoroutinesとFlow

- Main Threadでネットワーク、DB、ファイルI/Oを行わない。
- ScopeはLifecycleに結び付ける。`GlobalScope`は禁止する。
- FlowはRepositoryから公開し、ViewModelで`stateIn`等を用いてUI Stateへ変換する。
- CancellationExceptionをcatchして通常エラーへ変換しない。
- リトライは回数、待機、対象エラーを限定する。
- Network変更やZeroTier経路状態の購読は重複登録と解除漏れを防ぐ。

### 5.5 RepositoryとData Source

- FeatureはRepository interfaceへ依存する。
- API DTO、永続化Model、Domain Modelを同一型にしない。
- Mapperで境界を明示する。
- Repositoryは接続経路、認証、ローカルキャッシュ等を調整するが、UI表示文言を返さない。

### 5.6 MVP後: Room

- Migrationを必ず用意し、破壊的Migrationを本番設定で許可しない。
- DAOは必要なQueryだけを公開する。
- 長時間Transaction内でネットワークI/Oを行わない。
- `LocalSyncItem`の状態遷移を1箇所へ集約する。

### 5.7 MVP後: WorkManager

- Backup Ruleごとに一意なWork名を使用する。
- Worker開始時にネットワークポリシーを再評価する。
- モバイル通信、未登録Wi-Fi、外部Wi-FiでZeroTier未接続の場合は転送せず短時間で正常終了する。
- 端末側削除をサーバー削除へ変換しない。
- 大量転送だけForeground Workerと進捗通知を使用する。
- 無制限リトライを禁止し、再試行可能エラーと永久失敗を分ける。

### 5.8 接続経路

- 同じHTTPSホスト名を使用し、接続経路に応じて解決先IPを切り替える。
- `LOCAL_DIRECT`確認は同一サブネット確認、非ZeroTier Networkへの明示バインド、HTTPS Health、TLS証明書・ホスト名検証をすべて行う。
- 異なるサブネットからZeroTierなしで到達できても`LOCAL_DIRECT`としない。
- SSID/BSSIDをサーバー本人確認に使用しない。
- LocalとZeroTierが両方成功した場合はLocalを優先する。
- ZeroTier SDKやController APIを呼び出さず、`REMOTE_SECURE`はZeroTier経由のHTTPS到達性とTLS検証の成功で判定する。
- ZeroTierの未接続時は別アプリの確認案内と再確認操作を提供し、KuraStorageから接続・切断・Member認可を行わない。
- TLS失敗をHTTP再試行や証明書検証無効化で回避しない。
- Phase 1の固定URLはLAN/ZeroTierともに、Local専用`docs/environment-info.md`の`NET-API-HOSTNAME`から構成したHTTPS URLへ統一し、IP AddressをHTTPS URLへ直接使用しない。項目構造は`docs/environment-info.example.md`を参照する。
- AndroidのKuraStorage用Trust Anchorは`TLS-ROOT-CA-CERT-PATH`で指定した専用Root CA公開証明書に限定し、`domain-config`を`NET-API-HOSTNAME`の値へ限定する。
- 全証明書を許可するTrustManager、常に成功するHostnameVerifier、Productionで有効化可能な証明書検証回避Flagを実装しない。
- Root CA秘密鍵、サーバー秘密鍵、生成済み証明書をCommitせず、発行・検証・更新にはRepository管理の再現可能なScriptを使用する。

### 5.9 秘密情報

- StrongBox対応時はStrongBox-backed鍵を優先する。
- Keystore内の取り出し不可AES-256鍵とAES-GCMで秘密情報を保護する。
- Access Tokenは原則メモリだけに保持する。
- ログアウト、Device失効、登録失敗時に対象秘密情報を削除する。
- 証明書検証を無効化するDebugコードをReleaseへ含めない。

---

## 6. API・契約規約

### 6.1 URLとHTTP

- API Base Pathは`/api/v1`とする。
- Resource名は複数形、kebab-caseを使用する。
- Command的な操作は、ResourceまたはSub-resourceで表現する。
- GETは副作用を持たない。
- PUT/DELETE等の再送が想定される操作は冪等にする。
- MVP後の長時間Jobは同期完了を待たず、`202 Accepted`と状態確認Resourceを返す。
- MVPの元ファイル配信はRange Requestを正しく扱う。派生配信はMVP後とする。
- 物理パスをRequest/Responseへ含めない。

### 6.2 DTO

- API ContractはEndpoint層に置き、Domain Entityを直接Serializeしない。
- RequestとResponseを別型にする。
- 外部入力には長さ、形式、範囲、列挙値、件数上限を設定する。
- Paginationは既定値と最大値をサーバー側で強制する。
- Serverが決める`userId`、`deviceId`、`ownerUserId`をClient入力にしない。

### 6.3 Error Response

共通形式を使用する。

```json
{
  "code": "FILE_VERSION_CONFLICT",
  "message": "The file was updated by another operation.",
  "requestId": "01J...",
  "details": {}
}
```

- `code`はClient分岐に使用する安定値とする。
- `message`は機密情報や内部例外を含めない。
- `requestId`を必ず返す。
- Field Validationでは`details.fields`等に対象項目を含める。
- 同じError Codeの意味をEndpointごとに変えない。

### 6.4 API変更

- Phase 1中もAndroid以外のClientから利用できる契約にする。
- 破壊的変更は原則`/api/v2`または段階的移行で行う。
- Field追加は既存Clientが無視できる形にする。
- Enum追加でClientがCrashしないUnknown処理を持たせる。
- API Contract変更時はIntegration TestとAndroid Contract Testを同じPRで更新する。

---

## 7. PostgreSQL・Migration規約

### 7.1 命名

- DBオブジェクト名は`snake_case`を使用する。
- table名は複数形に統一する。
- Primary Keyは`id`、Foreign Keyは`[entity]_id`とする。
- Indexは`ix_[table]_[columns]`、Unique Indexは`ux_[table]_[columns]`とする。
- Check Constraintは`ck_[table]_[rule]`、Foreign Keyは`fk_[table]_[target]`とする。

### 7.2 スキーマ変更

- 1 Migrationは1つの論理変更に限定する。
- Migration名は変更内容を表す。例: `AddRefreshSessionFamilyIndex`。
- 大量データ更新を伴う場合は、DDL、Backfill、制約追加を分ける。
- `NOT NULL`追加は既存データの移行手順を含める。
- 大きなtableへのIndex追加はロック時間を確認する。
- Migration適用前後のバックアップとRollback可否をPRへ記載する。
- Production MigrationをAPI起動時に自動実行しない。

### 7.3 Query

- 30万件規模を前提に実行計画を確認する。
- 一覧・検索はPaginationを必須とする。
- 権限Queryは所有者、直接共有、祖先フォルダ共有をSQLレベルで絞り込む。
- `%keyword%`検索等は`pg_trgm`等のIndex利用を確認する。
- `SELECT *`を本番Queryで使用しない。
- Query追加時は必要なIndexと最悪件数を検討する。

---

## 8. Nginx・systemd・Shell規約

### 8.1 Nginx

- Clientから到達するHTTPS入口をNginxへ限定する。
- APIはUnix Socketまたは内部Private Portだけで待ち受ける。
- Forwarded HeaderはNginxからだけ信頼する。
- Upload Size、Timeout、Buffering、Rangeの設定理由をコメントする。
- TLS秘密鍵をリポジトリへ保存しない。
- 設定変更時は`nginx -t`をCIまたは配置前確認で実行する。

### 8.2 systemd

- API、Workerは専用非root Userで実行する。
- `NoNewPrivileges`、`ProtectSystem`、`PrivateTmp`等のhardeningを可能な範囲で使用する。
- Write可能PathをKuraStorageの必要領域へ限定する。
- API Userのsudoers/polkit権限は固定unit起動だけに限定する。
- Unit変更後は`systemd-analyze verify`等で検証する。

### 8.3 Shell

- 新規Bash Scriptは`set -euo pipefail`を基本とする。
- 変数を必ずquoteする。
- 秘密情報を引数や標準出力へ出さない。
- `eval`を使用しない。
- ShellCheckを通す。
- 配置Scriptは再実行可能にする。

---

## 9. テストガイドライン

### 9.1 テスト区分

| 種類 | 主対象 | 外部依存 |
| --- | --- | --- |
| Unit | Domain、Application、Mapper、Policy、ViewModel | Fake/Stubを使用 |
| Integration | PostgreSQL、HDD相当領域、API、Recovery Hosted Service | 実依存またはTestcontainers |
| Android Instrumented | Compose、Keystore、SAF、Android API | Emulator/実機 |
| Performance | 一覧、Range、Streaming Upload | k6、Benchmark |
| Security | Path traversal、IDOR、Token、ZeroTier境界、root境界 | 実構成を含む |

### 9.2 Androidテストの配置

AndroidのTest本体は、対象Production Codeと同じGradle Module内のSource Setへ配置する。

| 配置 | 用途 |
| --- | --- |
| `<module>/src/test/` | Android Runtimeを必要としないJVM Unit Test |
| `<module>/src/androidTest/` | EmulatorまたはAndroid実機を必要とするInstrumented Test |
| `apps/android/app/src/androidTest/` | Navigation、Application起動、複数FeatureをまたぐUI統合Test |

配置ルール。

- `apps/android/tests/`のようなAndroid全体用Test Directoryは作成しない。
- ViewModel、Use Case、Mapper、Policy、状態変換、Fake Repositoryで検証できる処理は`src/test/`を優先する。
- Compose UI、Keystore、SAF、Android Framework APIを実際に必要とするMVP Testは
  `src/androidTest/`へ置く。
- Test ClassのPackageは、原則として対象Production Packageの構造をMirrorする。
- `core-testing/`には複数Moduleで再利用するFake、Fixture、Test Rule、Dispatcher、Assertionだけを置く。
  Feature固有またはCore固有のTest Classは対象Module内に置く。
- Production Source Setから`core-testing`を参照しない。依存は`testImplementation`または
  `androidTestImplementation`に限定する。
- `app/src/androidTest/`で実行するUI統合Testと、Raspberry Piを含む製品全体E2Eを区別する。
- 不要な`src/test/`、`src/androidTest/`を空Directoryとして作成しない。

### 9.3 テスト命名

C#。

```csharp
[Fact]
public void ResolvePermission_DirectViewerAndInheritedEditor_ReturnsEditor()
```

Kotlin。

```kotlin
@Test
fun `registered external wifi without remote route does not start backup`()
```

命名は「対象・条件・期待結果」が分かる形にする。`Test1`、`Works`、`正常系`だけの名称は禁止する。

### 9.4 Given-When-Then

```csharp
[Fact]
public void Rotate_UsedTokenIsPresented_RevokesSessionFamily()
{
    // Given
    var family = RefreshSessionFamilyFixture.Active();
    var usedToken = family.CurrentToken;
    family.Rotate(TestClock.UtcNow);

    // When
    var result = family.Validate(usedToken);

    // Then
    result.Should().Be(RefreshTokenValidationResult.ReuseDetected);
    family.IsRevoked.Should().BeTrue();
}
```

### 9.5 カバレッジ目標

- Domain/Application全体: 80%以上
- 認証、認可、状態遷移、パス処理: 95%以上
- E2E: 主要受け入れフローをすべて対象にする

カバレッジを上げるだけの価値の低いテストを増やさない。境界値、失敗、再試行、途中停止、権限拒否を優先する。

### 9.6 モック方針

- Domainテストでは外部依存を持ち込まない。
- ApplicationテストではRepository、Clock、Storage、Token、Systemd等をinterfaceでFake化する。
- EF CoreのInMemory ProviderをPostgreSQL統合テストの代替にしない。
- PostgreSQL固有制約、再帰CTE、LockはTestcontainersで検証する。
- ファイル操作は一時Directoryだけでなく、可能な範囲でext4相当環境も検証する。

### 9.7 必須失敗系

少なくとも次を各対象機能で検証する。

- 未認証、期限切れToken、失効Device、失効Session
- 権限不足とIDOR
- Storage未mount、容量不足、途中切断
- DB成功後のHDD失敗、HDD成功後のDB失敗
- Process停止後の復旧
- 二重送信、重複ジョブ、Refresh Token再利用
- Path traversal、Symbolic Link、巨大/破損Media
- Android再起動、Worker再実行、ZeroTier切断

---

## 10. Git運用

### 10.1 ブランチ戦略

Phase 1では、`main`を基点とする短命Branch運用を使用する。
常設の`develop` Branchは使用しない。

```text
main
├── feat/phase1-server-foundation
├── feat/phase1-android-foundation
├── fix/upload-recovery
├── test/phase1-integration
├── refactor/authorization-query
└── docs/repository-structure
```

| Branch | 用途 |
| --- | --- |
| `main` | 統合済みの基準Branch |
| `feat/*` | 新機能 |
| `fix/*` | 不具合修正 |
| `test/*` | Testまたは検証 |
| `refactor/*` | 挙動を変えない構造改善 |
| `docs/*` | 文書のみの変更 |
| `chore/*` | Build、依存、運用補助 |
| `hotfix/*` | `main`上の緊急修正 |

- `main`への直接Pushを禁止する。
- Pull Request単位ごとに、最新の`main`から作業Branchを作成する。
- Branch名は変更種別、対象フェーズまたは機能、目的が分かる名前にする。
- Pull Request作成後はBranch上で追加作業を続けず、
  修正依頼がある場合だけ同じBranchを更新する。
- Pull RequestのMergeは人間が行う。コーディングエージェントはMergeしない。
- 次の作業が未MergeのPull Requestへ依存する場合は、
  そのPull Requestが`main`へMergeされた後に開始する。

### 10.2 Commit Message

Conventional Commitsを使用する。

```text
<type>(<scope>): <subject>
```

Type。

- `feat`: 新機能
- `fix`: 不具合修正
- `refactor`: 挙動を変えない改善
- `test`: テスト
- `docs`: 文書
- `perf`: 性能改善
- `security`: セキュリティ修正
- `build`: Build System
- `ci`: CI
- `chore`: その他

代表Scope。

- `domain`
- `identity`
- `files`
- `sharing`
- `transfer`
- `media`
- `backup`
- `indexing`
- `api`
- `worker`
- `admin-cli`
- `android-auth`
- `android-files`
- `android-backup`
- `database`
- `deploy`

例。

```text
feat(transfer): add resumable upload completion flow
security(files): reject symbolic links during path resolution
```

- Subjectは命令形の英語を基本とし、72文字以内を目安にする。
- 変更理由が自明でない場合はBodyに「なぜ」を記載する。
- 破壊的変更は`BREAKING CHANGE:`をFooterへ記載する。
- 1Commitに無関係な変更を混在させない。

### 10.3 Pull Request

フェーズ内の作業は、次に実施する範囲をPull Request単位として選び、
その単位ごとにBranch、実装、Test、Pull Request作成を行う。
フェーズ全体を一度に実装してから後で分割しない。

Pull Request単位は次を基準にする。

- 1つの変更目的を一文で説明できる。
- 実装と、それを検証するTestを同じPull Requestに含められる。
- 独立してBuild、Test、Reviewできる。
- 強く依存する変更はまとめ、無関係な機能やRefactorは分ける。
- チェック項目の数、変更行数、File数だけを理由に機械的に分割しない。

Pull Requestのタイトルと本文は英語で作成する。タイトルは変更目的を簡潔に表し、本文には目的、対象タスク、変更内容、テスト結果、影響または未実施事項を記載する。`tasklist.md`の完了記録とユーザーへの報告は、別途指定がない限り日本語でよい。

PR本文は次の英語見出しを基本構成とする。

```markdown
## Summary

## Included tasklist items

## Changes

## Tests

## Impact and notes
```

PR作成前に次を確認する。

- 対象範囲の実装が完了している。
- `scripts/ci/verify-server.sh`または`scripts/ci/verify-android.sh`など、必要な検証が成功する。
- 設計文書との整合性をセルフレビューする。
- Migration、設定、秘密情報、権限変更の有無を確認する。
- API、UI、HDD/DB境界に影響する場合は、その影響と検証内容を記載する。
- 対象のtasklist項目を`[x]`へ更新する。

コーディングエージェントはCommit、Push、Pull Request作成まで行い、Mergeは行わない。
Pull Request作成後は停止し、次の作業へ自動的に進まない。

### 10.4 Review Comment

Priorityを明示する。

- `[required]`: Merge前に修正必須
- `[recommended]`: 原則修正。見送る場合は理由が必要
- `[suggestion]`: 改善案
- `[question]`: 意図確認

レビューではコード形式だけでなく、要件、権限境界、失敗時のデータ状態、性能、運用を確認する。

---

## 11. CI・品質ゲート

### 11.1 Pull Request CI

最低限、次を自動実行する。

#### Server

```bash
./scripts/ci/verify-server.sh
```

このScriptはlocked restore、`dotnet format --verify-no-changes`、Analyzerを有効にしたRelease Build、Testを実行する。

#### Android

```bash
./scripts/ci/verify-android.sh
```

このScriptは`ktlintCheck`、`detekt`、Android `lint`、各Moduleの`src/test/`にあるJVM Unit Test、
Debug Assemblyを実行する。

`src/androidTest/`にあるInstrumented Testは、EmulatorまたはAndroid実機を用意した専用Jobで
`connectedDebugAndroidTest`等を実行する。Raspberry Piを含むE2Eは通常のAndroid Instrumented Test Jobへ混在させず、
専用の実機検証として実行する。

#### Configuration / Scripts

- ShellCheck
- `nginx -t`相当の構文確認
- systemd unit検証
- Markdown Link / Format Check
- Migration生成漏れ確認
- Secret Scan
- Dependency Vulnerability Scan

### 11.2 Merge条件

- 必須CIがすべて成功している。
- Required Reviewを満たしている。
- 未解決の`[required]`コメントがない。
- 新規警告、lint error、formatter差分がない。
- 上位文書に影響する変更では文書も更新されている。
- Security-sensitive変更はSecurity Testまたは実機確認結果がある。

### 11.3 Release CI

- ARM64向け.NET self-contained artifactを作成する。
- Android Release artifactを作成する。
- Test結果、version、commit SHAをartifact metadataへ含める。
- SBOMを生成する。
- Checksumを生成する。
- Migration一覧とRollback手順をRelease Noteへ含める。
- 秘密鍵、`.env`、Production設定をartifactへ含めない。

---

## 12. 依存関係管理

- .NET SDKは`global.json`で固定する。
- NuGetはCentral Package Managementとlock fileを使用する。
- Kotlin、Android Gradle Plugin、Compose、AndroidXはVersion Catalog/BOMで固定する。
- Dependency Updateは機能変更と別PRにする。
- CVE対応を優先する。
- 未使用依存を追加しない。
- 1つの目的に複数のLibraryを併用しない。
- Major Version更新ではArchitectureへの影響を確認する。
- Licenseと配布条件を確認する。

---

## 13. 開発環境

### 13.1 必要ツール

| ツール | 用途 |
| --- | --- |
| Git | Source管理 |
| .NET 10 SDK | Server Build/Test |
| JDK（Android Gradle Plugin対応版） | Android Build |
| Android Studio / Android SDK | Android開発・Emulator |
| PostgreSQL Client | DB確認 |
| Dockerまたは互換Runtime | Testcontainers用。Productionでは必須ではない |
| FFmpeg | Media Integration Test |
| Nginx | 配置・E2E |
| ShellCheck | Shell Script検査 |
| k6 | Performance Test |

### 13.2 初期セットアップ例

```bash
git clone <repository-url>
cd kurastorage

cp deployment/config/server/appsettings.Development.example.json \
   server/src/KuraStorage.Api/appsettings.Development.json

cp apps/android/local.properties.example apps/android/local.properties

dotnet restore server/KuraStorage.sln --locked-mode
./apps/android/gradlew -p apps/android dependencies

dotnet test server/KuraStorage.sln
./apps/android/gradlew -p apps/android testDebugUnitTest

# EmulatorまたはAndroid実機を接続している場合
./apps/android/gradlew -p apps/android connectedDebugAndroidTest
```

実際の秘密情報はExample Fileへ記載せず、ローカル専用設定またはSecret Storeへ保存する。

環境固有の非Secret情報はGit管理外の`docs/environment-info.md`へ集約し、Tracked Fileでは`docs/environment-info.example.md`の項目IDを参照する。IP Address、Hostname、ZeroTier Network ID、端末Model、OS／Kernel／Middleware Version、証明書Path／FingerprintをTracked文書へ実値で複製しない。Password、Token、Private Key、Node Identity、Keystore内容は`docs/environment-info.md`にも保存しない。

---

## 14. Definition of Done

機能は次をすべて満たした時点で完了とする。

- [ ] 要求と受け入れ条件が特定されている
- [ ] ArchitectureとRepository境界を守っている
- [ ] Redから始めた自動テストがある、または例外理由が記録されている
- [ ] 正常系、境界値、権限拒否、途中失敗、再試行を確認している
- [ ] Domain/Applicationのカバレッジ基準を満たす
- [ ] API Error CodeとHTTP Statusが設計に一致する
- [ ] Token、秘密鍵、物理パス、ファイル本文がログへ出ない
- [ ] DB/HDDをまたぐ場合はJournalと復旧経路がある
- [ ] Uploadが全体Bufferingせず、Idempotency、途中切断、全体再試行を検証している
- [ ] Android SAFのURI、権限拒否、書込失敗を実機またはInstrumented Testで確認している
- [ ] MVP後のHost、Module、Table、依存を理由なく先行追加していない
- [ ] Migration、Index、Rollback影響を確認している
- [ ] Raspberry Pi/Android依存機能は実機確認している
- [ ] 文書、設定例、運用手順を更新している
- [ ] CIが成功し、Review承認済みである

---

## 15. 禁止事項

- DomainからInfrastructure、ASP.NET Core、Android SDKを参照する
- API、CLIからDBまたはHDDを直接変更する
- Client入力の`userId`、`deviceId`を認可根拠にする
- 物理絶対パスをAPIへ公開する
- Path検証をEndpointごとに再実装する
- HTTPS/TLS検証を無効化する
- APIをrootで実行する
- root処理へ任意Commandや任意Pathを渡す
- SQL、Shell、FFmpeg引数を未検証文字列で連結する
- Upload完了前のファイルを正式公開する
- HDD未mount時に通常Directoryへ誤保存する
- DB Failureまたは変換Failureを元ファイル削除へつなげる
- EF Core InMemoryだけでPostgreSQL固有挙動を検証したことにする
- `main`、`develop`へ直接pushする
- CI失敗、未解決Required Reviewを無視してMergeする
