# Android UI PR8 Server Cache管理 検証記録

## 対象と環境

- 検証日: 2026-09-04
- 対象: Admin Cache状態API、永続`MediaCleanupRun`、manual/scheduled Worker処理、Migration、配備設定
- Server: .NET 10 Release build
- Database: Testcontainers PostgreSQL 17 Alpine
- Storage: テストごとに作成した一時ディレクトリ上の実File
- 構成検証: `shellcheck`と`nginx`を含む一時Ubuntu 26.04 Docker環境

実ユーザー名、Token、Idempotency key、Storage ID、File名、相対/物理Path、DB credentialはこの記録へ残していない。

## API契約と認証・認可

- `GET /api/v1/admin/media-cache`はREADYのImage/Video Low/Medium内訳、合計、10 GiB上限、6 GiB目標、Job/Run件数、最新Runだけを返した。
- `POST /api/v1/admin/media-cache/cleanup-requests`はUUID `Idempotency-Key`を必須とし、物理清掃を待たず`202 Accepted`と`PENDING` Runを返した。
- 同一Admin・同一keyの再送は同一Run IDを返し、GETによる再取得でも同じRunを確認した。DBにはSHA-256 hexだけが保存された。
- key欠落/不正は`400 VALIDATION_FAILED`、Memberは`403`、未認証と失効Deviceは`401`になった。
- ResponseはIdempotency key、File名、相対/物理Path、User名、Job入力、内部例外詳細を含まなかった。収集したAPI/Worker Logにもテストkey平文はなかった。

## PostgreSQL・Migration・復旧

- `MediaCleanupRun`はmanual/scheduled、pending/running/completed/failed、requesting Admin、key/fingerprint hash、worker token、lease、UTC日時、件数、解放Byte、残存Cache量、安全なfailure codeを保持した。
- 同一Admin/keyの並列登録は部分Unique Indexと競合後の再読込で1行へ収束した。同一keyと異なるfingerprintはconflictになった。
- manual優先claim、active lease中の二重claim拒否、期限切れrunning leaseの別workerによる回収、旧tokenの完了拒否、新tokenによる完了を確認した。
- scheduled要求はactive Runへ収束し、完了後も設定間隔内は新規Runを作らず、間隔到達後に次Runを作成した。
- Migration `AddMediaCleanupRuns`はUp、`AddBackupReceipts`へのDown、再Upに成功した。既存の元File/派生DB行は全段階で保持され、一意制約と4 Indexを確認した。
- `dotnet-ef migrations has-pending-model-changes`は`No changes have been made to the model since the last migration.`を返した。

## 実Storage・Worker処理

実`MediaCleanupWorker`、`MediaCleanupService`、`PostgreSqlMediaCleanupRepository`、`DerivativeStore`を同じテストで接続し、manual Runを処理した。

- Runは`PENDING -> RUNNING -> COMPLETED`へ遷移した。
- 期限切れImage Lowを1件、10 bytes削除し、Runへ`deletedCount=1`、`releasedBytes=10`を保存した。
- 元File、有効Delivery lease中のImage Medium、ThumbnailのDB行と物理Fileは保持した。
- advisory lock取得不可はRunを`PENDING`へ戻した。Storage unavailableは`STORAGE_UNAVAILABLE`、部分削除失敗は`PARTIAL_DELETE_FAILURE`、その他例外は`CLEANUP_FAILED`として自由形式Errorなしで記録した。
- Worker取消はRunをrunningのまま残し、PostgreSQL lease回収テストでProcess停止/DB中断相当から再claimできることを確認した。

## 自動検証結果

`./scripts/ci/verify-server.sh`のRelease buildは警告0で、次の全Testが成功した。

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 135 | 0 | 0 |
| Application | 353 | 0 | 0 |
| Integration | 229 | 0 | 0 |

Coverlet line coverageはDomain 84.57%、ApplicationのUnit/Integration統合結果88.57%で、全体80%基準を満たした。PR8の重要境界は`MediaCleanupRun.cs` 96.77%、`AdminMediaCacheService.cs` 98.98%、`MediaCleanupWorker.cs` 96.05%だった。

次も成功した。

- `./scripts/ci/verify-config.sh`（内部で`verify-deployment.sh`を実行）
- `./scripts/ci/verify-security.sh`
- `dotnet format server/KuraStorage.sln --verify-no-changes --no-restore`
- OpenAPI YAML parseと`OpenApiContractTests`
- Migration Up/Down/再Up、一意制約、pending model確認
- `git diff --check`

ホストには`nginx`と`shellcheck`がないため、構成・配備検証だけは使い捨てDocker環境でCIと同じスクリプトを実行した。systemd service metadata、netlink、kernel nft ruleset、listen socketを必要とする確認は、スクリプトが定義する非特権環境向けgrammar/syntax経路で成功した。
