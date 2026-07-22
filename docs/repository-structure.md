# リポジトリ構造定義書（Repository Structure Document）

## 文書情報

| 項目 | 内容 |
| --- | --- |
| プロダクト名 | KuraStorage |
| 文書種別 | Repository Structure Document |
| バージョン | 0.4.0 Draft |
| 作成日 | 2026-07-12 |
| 参照文書 | `docs/functional-design.md` Version 0.9.0 Draft |
| 参照文書 | `docs/architecture-design.md` Version 0.3.0 Draft |
| 関連文書 | `docs/development-guidelines.md` |
| 対象フェーズ | Phase 1: Androidアプリ＋Raspberry Piバックエンド |

---

## Phase 1 ZeroTier配置方針

ZeroTier daemonとそのNetwork資格情報はRepository外のOS・別アプリ管理とする。KuraStorageにZeroTier用Host、Domain Entity、DB Table、Controller API、Android SDK連携を追加しない。

- 接続経路判定と固定Hostname Resolverは`apps/android/core-network/`へ置く。
- ZeroTier未接続案内はAndroid Featureへ置くが、ZeroTier操作APIへ依存させない。
- LAN／ZeroTier HTTPS許可は`deployment/config/firewall/`の公開TemplateとLocal環境情報から生成する。
- 環境固有値はGit管理外の`docs/environment-info.md`へ置く。詳細なZeroTier運用手順は運用作業時に`docs/operations/`へ追加する。

将来、自己管理WireGuard、VPSを介したVPN、その他の安全なオーバーレイ方式を導入する場合は、その時点のArchitecture ReviewとSteeringで必要な配置を定義する。Phase 1で将来用のHost、Directory、依存ライブラリを先行追加しない。

---

## 1. 目的

本書は、KuraStorageリポジトリ内のディレクトリ、プロジェクト、ファイルの配置、依存方向、命名、テスト配置、設定・配置資材の管理方法を定義する。

この構造は次を実現するために使用する。

1. モジュラーモノリスの境界を物理構造へ反映する。
2. Domain、Application、Infrastructure、Presentationの依存方向を守る。
3. MVPはAPIと管理CLIだけをHostとし、ZeroTier daemonをRepository管理のHostへ含めない。
4. Androidを`core-*`と`feature-*`へ分け、OS依存とUIを隔離する。
5. テスト種別と実行環境を明確にする。
6. Production秘密情報をSource管理から除外する。
7. Phase 2 Web追加時に既存Backendを再構成しなくてよい状態を維持する。

---

## 2. リポジトリ全体構造

MVPで作成する最小構造は次を正とする。将来用の空Project、Module、Directory、依存ライブラリは先行作成しない。

```text
kurastorage/
├── apps/android/
│   ├── app/
│   ├── core-model/
│   ├── core-network/
│   ├── core-data/
│   ├── core-security/
│   ├── core-ui/
│   ├── feature-connection/
│   ├── feature-auth/
│   └── feature-files/
├── server/
│   ├── src/
│   │   ├── KuraStorage.Domain/
│   │   ├── KuraStorage.Application/
│   │   ├── KuraStorage.Infrastructure/
│   │   ├── KuraStorage.Api/
│   │   └── KuraStorage.AdminCli/
│   └── tests/
│       ├── KuraStorage.Domain.Tests/
│       ├── KuraStorage.Application.Tests/
│       └── KuraStorage.IntegrationTests/
├── contracts/openapi/
├── deployment/
├── docs/
├── scripts/ci/
└── .steering/
```

以下の大規模TreeはMVP後を含む配置候補の参考である。MVPとの差分にある`KuraStorage.Worker`、`core-database`、`core-testing`、Media・Sharing・Backup・Settings Feature、専用Performance/Security Project等は、それを必要とする機能のSteeringで追加する。

```text
kurastorage/
├── .github/
│   ├── ISSUE_TEMPLATE/
│   ├── PULL_REQUEST_TEMPLATE.md
│   ├── dependabot.yml
│   └── workflows/
│       ├── server-ci.yml
│       ├── android-ci.yml
│       ├── configuration-ci.yml
│       ├── security-scan.yml
│       └── release.yml
├── .agents/
│   └── skills/
├── .codex/
│   ├── agents/
│   ├── rules/
│   └── config.toml
├── .config/
│   └── dotnet-tools.json
├── .steering/
│   └── 20260712-kurastorage-phase1-mvp/
├── apps/
│   └── android/
│       ├── app/
│       │   └── src/
│       │       ├── main/
│       │       ├── test/
│       │       └── androidTest/
│       ├── core-model/
│       ├── core-network/
│       ├── core-database/
│       ├── core-security/
│       ├── core-ui/
│       ├── core-testing/
│       ├── feature-connection/
│       ├── feature-auth/
│       ├── feature-files/
│       ├── feature-media/
│       ├── feature-sharing/
│       ├── feature-backup/
│       ├── feature-settings/
│       ├── build-logic/
│       ├── gradle/
│       │   └── libs.versions.toml
│       ├── build.gradle.kts
│       ├── settings.gradle.kts
│       ├── gradle.properties
│       ├── gradlew
│       ├── gradlew.bat
│       └── local.properties.example
├── server/
│   ├── src/
│   │   ├── KuraStorage.Domain/
│   │   ├── KuraStorage.Application/
│   │   ├── KuraStorage.Infrastructure/
│   │   ├── KuraStorage.Api/
│   │   ├── KuraStorage.Worker/
│   │   ├── KuraStorage.AdminCli/
│   ├── tests/
│   │   ├── KuraStorage.Domain.Tests/
│   │   ├── KuraStorage.Application.Tests/
│   │   ├── KuraStorage.Infrastructure.IntegrationTests/
│   │   ├── KuraStorage.Api.IntegrationTests/
│   │   ├── KuraStorage.Worker.IntegrationTests/
│   │   ├── KuraStorage.AdminCli.IntegrationTests/
│   │   ├── KuraStorage.E2ETests/
│   │   ├── performance/
│   │   └── security/
│   ├── Directory.Build.props
│   ├── Directory.Build.targets
│   ├── Directory.Packages.props
│   └── KuraStorage.sln
├── contracts/
│   └── openapi/
│       └── kurastorage-api.yaml
├── deployment/
│   ├── config/
│   │   ├── server/
│   │   ├── nginx/
│   │   ├── systemd/
│   │   ├── firewall/
│   │   ├── tls/
│   │   └── logrotate/
│   └── raspberry-pi/
│       ├── install.sh
│       ├── upgrade.sh
│       ├── rollback.sh
│       ├── verify.sh
│       └── uninstall.sh
├── docs/
│   ├── product-requirements.md
│   ├── functional-design.md
│   ├── architecture-design.md
│   ├── repository-structure.md
│   ├── development-guidelines.md
│   ├── environment-info.example.md
│   ├── api/
│   ├── adr/
│   ├── operations/
│   │   ├── tls-private-ca.md
│   │   └── zerotier-network-access.md
│   └── testing/
├── scripts/
│   ├── ci/
│   ├── development/
│   ├── maintenance/
│   │   ├── generate-tls-certificates.sh
│   │   ├── verify-tls-certificates.sh
│   │   └── generate-jwt-signing-key.sh
│   └── test-data/
├── .editorconfig
├── .gitattributes
├── .gitignore
├── global.json
├── AGENTS.md
├── LICENSE
├── README.md
└── SECURITY.md
```

`apps/web/`と`apps/ios/`は、それぞれの実装を開始するPhaseで追加する。空Directoryを将来予約のためだけにCommitしない。

AndroidのテストはServerのような独立した`tests/`Directoryへ集約しない。各Gradle ModuleのSource Setとして
`src/test/`または`src/androidTest/`へ配置する。上記の`app/`は代表例であり、`core-*`と`feature-*`にも
必要なSource Setを同じ規則で追加する。`apps/android/tests/`は作成しない。

### 2.1 ルート配置方針

| 項目 | 方針 |
| --- | --- |
| Server Source | `server/src/` |
| Server Test | `server/tests/` |
| Server Solution・共通Build設定 | `server/KuraStorage.sln`、`server/Directory.*` |
| Android | `apps/android/`配下で独立したGradle Buildを構成 |
| 将来のWeb・iOS | `apps/web/`、`apps/ios/`として追加 |
| API契約 | `contracts/openapi/` |
| Product/Design Document | `docs/` |
| Raspberry Pi配置設定と配置処理 | `deployment/` |
| 開発・CI・保守補助Script | `scripts/` |
| .NET SDK固定 | Repository Rootの`global.json` |
| 秘密情報・実環境設定 | リポジトリ外。ExampleだけをVersion管理 |

リポジトリ内の`deployment/config/`はRaspberry Pi上の実配置Pathではない。Gitで管理する設定原本であり、`deployment/raspberry-pi/`の処理が`/etc`、`/opt`、`/var`等の実Pathへ配置する。

---

## 3. Server Solution構造

### 3.1 Project依存関係

```text
KuraStorage.Api ───────────────┐
KuraStorage.AdminCli ──────────┼──> KuraStorage.Application ──> KuraStorage.Domain
                                               │
                                KuraStorage.Infrastructure
                                  interface実装として注入
```

実際のProject Referenceは次を基本とする。

| Project | Reference可能 | Reference禁止 |
| --- | --- | --- |
| `KuraStorage.Domain` | なし | 全Project |
| `KuraStorage.Application` | Domain | Infrastructure、各Host |
| `KuraStorage.Infrastructure` | Domain、Application | 各Host |

`Infrastructure`がApplication/Domainのinterfaceを実装するため、Compile-time ReferenceはInfrastructureから内側へ向く。HostでDI登録して実装を組み立てる。

---

## 4. `server/src/KuraStorage.Domain/`

### 4.1 役割

業務Entity、Value Object、Enum、Policy、状態遷移、Domain Errorを保持する。Frameworkや外部I/Oに依存しない。

### 4.2 構造

```text
KuraStorage.Domain/
├── SharedKernel/
│   ├── Entity.cs
│   ├── AggregateRoot.cs
│   ├── DomainError.cs
│   └── Result.cs
├── Identity/
│   ├── Entities/
│   │   ├── User.cs
│   │   ├── Device.cs
│   │   ├── RefreshSession.cs
│   ├── ValueObjects/
│   │   ├── Username.cs
│   │   ├── PasswordHash.cs
│   │   └── RemoteAddress.cs
│   ├── Enums/
│   ├── Policies/
│   └── Errors/
├── Files/
│   ├── Entities/
│   │   ├── FileEntry.cs
│   │   └── FileOperation.cs
│   ├── ValueObjects/
│   │   ├── RelativeStoragePath.cs
│   │   ├── FileName.cs
│   │   └── FileVersion.cs
│   ├── Enums/
│   ├── Policies/
│   └── Errors/
├── Sharing/
│   ├── Entities/
│   ├── Enums/
│   ├── Policies/
│   └── Errors/
├── Transfer/
│   ├── Entities/
│   ├── ValueObjects/
│   ├── Enums/
│   └── Errors/
├── Media/
│   ├── Entities/
│   ├── ValueObjects/
│   ├── Enums/
│   └── Policies/
├── Backup/
│   ├── Entities/
│   ├── ValueObjects/
│   └── Policies/
├── Indexing/
│   ├── ValueObjects/
│   ├── Enums/
│   └── Policies/
├── Audit/
│   ├── Entities/
│   └── Enums/
└── KuraStorage.Domain.csproj
```

### 4.3 配置ルール

- Entity固有のRuleはEntityへ置く。
- 複数Entityにまたがる純粋なRuleは`Policies/`へ置く。
- HTTP Status、EF Attribute、JSON Attributeを置かない。
- `SharedKernel/`には2つ以上のDomain Moduleで本当に共通のものだけを置く。
- `Utils/`、`Helpers/`、`Common/`という曖昧なDirectoryを作らない。
- Domain Eventを導入する場合は各Moduleの`Events/`へ置き、Infrastructure Eventと混同しない。

---

## 5. `server/src/KuraStorage.Application/`

### 5.1 役割

ユースケース、Command/Query、認可、Transaction境界、外部interface、Application Error、DTOを保持する。

### 5.2 構造

```text
KuraStorage.Application/
├── Abstractions/
│   ├── Persistence/
│   │   ├── IUnitOfWork.cs
│   │   ├── IUserRepository.cs
│   │   ├── IFileEntryRepository.cs
│   │   └── IJobQueue.cs
│   ├── Storage/
│   │   ├── IStorageGuard.cs
│   │   ├── IFileStore.cs
│   │   └── IStorageHealthProbe.cs
│   ├── Security/
│   │   ├── IPasswordHasher.cs
│   │   ├── IAccessTokenIssuer.cs
│   │   └── IRefreshTokenGenerator.cs
│   ├── Media/
│   │   ├── IImageProcessor.cs
│   │   └── IVideoTranscoder.cs
│   ├── Networking/
│   ├── Observability/
│   │   └── IAuditWriter.cs
│   └── System/
│       ├── IClock.cs
│       ├── IIdGenerator.cs
│       └── IProcessRunner.cs
├── Shared/
│   ├── Behaviors/
│   ├── Errors/
│   ├── Pagination/
│   ├── SecurityContext/
│   └── Validation/
├── Identity/
│   ├── Commands/
│   │   ├── RegisterDevice/
│   │   ├── Login/
│   │   ├── RefreshToken/
│   │   └── Logout/
│   ├── Queries/
│   ├── Services/
│   └── Contracts/
├── Authorization/
│   ├── Queries/
│   ├── Services/
│   └── Contracts/
├── Files/
│   ├── Commands/
│   │   ├── RenameFile/
│   │   ├── MoveFile/
│   │   ├── TrashFile/
│   │   ├── RestoreFile/
│   │   └── PurgeFile/
│   ├── Queries/
│   │   ├── GetFile/
│   │   └── ListFiles/
│   └── Contracts/
├── Transfer/
│   ├── Commands/
│   ├── Queries/
│   └── Contracts/
├── Sharing/
│   ├── Commands/
│   ├── Queries/
│   └── Contracts/
├── Media/
│   ├── Commands/
│   ├── Queries/
│   └── Contracts/
├── Backup/
│   ├── Commands/
│   ├── Queries/
│   └── Contracts/
├── Indexing/
│   ├── Commands/
│   ├── Queries/
│   └── Contracts/
├── Administration/
│   ├── Commands/
│   ├── Queries/
│   └── Contracts/
├── Audit/
│   ├── Commands/
│   └── Contracts/
├── DependencyInjection.cs
└── KuraStorage.Application.csproj
```

### 5.3 ユースケースDirectory

1つのCommand/Queryは原則として専用Directoryを持つ。

```text
Login/
├── LoginCommand.cs
├── LoginCommandHandler.cs
├── LoginCommandValidator.cs
├── LoginResult.cs
└── LoginErrors.cs
```

小規模で1〜2FileしかないQueryはModule内に直接置いてよいが、File数が増えたら専用Directoryへ移す。

### 5.4 配置ルール

- Repository interfaceは`Abstractions/Persistence/`へ置く。
- Feature固有のinterfaceは該当Moduleへ置いてよい。
- API Request/ResponseではなくApplication Contractを置く。
- HandlerからDbContext、NpgsqlConnection、FileStream、Processを直接使用しない。
- Module間の更新は公開Application Service/Commandを経由する。
- `Shared/`へ業務機能を置かない。

---

## 6. `server/src/KuraStorage.Infrastructure/`

### 6.1 役割

PostgreSQL、HDD、暗号、Token、FFmpeg、libvips、systemd、時刻、ID生成、ログ等の実装を保持する。

### 6.2 構造

```text
KuraStorage.Infrastructure/
├── Persistence/
│   ├── KuraStorageDbContext.cs
│   ├── Configurations/
│   │   ├── UserConfiguration.cs
│   │   ├── DeviceConfiguration.cs
│   │   ├── FileEntryConfiguration.cs
│   │   └── TranscodeJobConfiguration.cs
│   ├── Repositories/
│   │   ├── UserRepository.cs
│   │   ├── FileEntryRepository.cs
│   │   └── JobQueue.cs
│   ├── Queries/
│   │   ├── FileAuthorizationQuery.cs
│   │   └── FileSearchQuery.cs
│   ├── Migrations/
│   ├── Interceptors/
│   └── Seed/
├── Storage/
│   ├── StorageGuard.cs
│   ├── FileStore.cs
│   ├── StorageHealthProbe.cs
│   ├── AtomicFileWriter.cs
│   ├── PathResolution/
│   └── MountInspection/
├── Identity/
│   ├── Argon2PasswordHasher.cs
│   ├── JwtAccessTokenIssuer.cs
│   ├── RefreshTokenGenerator.cs
│   └── TokenHashingService.cs
├── Media/
│   ├── ImageProcessor.cs
│   ├── FfmpegVideoTranscoder.cs
│   ├── MediaMetadataReader.cs
│   └── ProcessLimits/
├── BackgroundJobs/
│   ├── JobLeaseRepository.cs
│   └── JobRecoveryService.cs
├── Observability/
│   ├── AuditWriter.cs
│   ├── LoggingRedaction.cs
│   └── OpenTelemetryConfiguration.cs
├── System/
│   ├── SystemClock.cs
│   ├── UuidGenerator.cs
│   └── ProcessRunner.cs
├── Configuration/
│   ├── ServerOptions.cs
│   ├── StorageOptions.cs
│   ├── AuthenticationOptions.cs
│   ├── CacheOptions.cs
│   └── OptionValidators.cs
├── DependencyInjection.cs
└── KuraStorage.Infrastructure.csproj
```

### 6.3 配置ルール

- EF ConfigurationをEntity classへ混在させない。
- Migrationを手動編集する場合は理由をPRへ記載する。
- PostgreSQL固有Queryは`Persistence/Queries/`へ置く。
- HDD Path検証は`Storage/PathResolution/`に集約する。
- Configuration Typeは型付きOptionsとして`Configuration/`へ置く。
- Production Secretの読み込み実装は置いてよいが、値そのものは置かない。

---

## 7. Host Project

## 7.1 `server/src/KuraStorage.Api/`

### 7.1.1 役割

Nginxから要求を受けるREST API、認証Pipeline、Request検証、Application呼び出し、共通Error変換、File Streamingを実装する。

### 7.1.2 構造

```text
KuraStorage.Api/
├── Authentication/
│   ├── BearerAuthenticationHandler.cs
│   ├── CurrentSecurityContextFactory.cs
│   └── AuthorizationPolicies.cs
├── Contracts/
│   ├── Common/
│   ├── Identity/
│   ├── Files/
│   ├── Transfer/
│   ├── Sharing/
│   ├── Media/
│   └── Backup/
├── Endpoints/
│   ├── System/
│   ├── Identity/
│   ├── Files/
│   ├── Transfer/
│   ├── Sharing/
│   ├── Media/
│   └── Backup/
├── Middleware/
│   ├── RequestIdMiddleware.cs
│   ├── ExceptionMappingMiddleware.cs
│   ├── SecurityContextMiddleware.cs
│   └── AccessLogMiddleware.cs
├── Mapping/
├── OpenApi/
├── Streaming/
│   ├── RangeRequestParser.cs
│   └── FileStreamResultFactory.cs
├── Health/
├── DependencyInjection.cs
├── Program.cs
├── appsettings.json
└── KuraStorage.Api.csproj
```

### 7.1.3 配置ルール

- EndpointはModuleごとに分ける。
- 1Endpoint Fileに無関係な複数Resourceを置かない。
- API ContractとApplication ContractをMapperで変換する。
- EndpointからRepository、DbContext、FileStoreを直接呼ばない。
- 物理PathをContractへ含めない。
- 匿名Endpointは`Endpoints/System`と認証の必要箇所へ限定し、明示する。

## 7.2 MVP後: `server/src/KuraStorage.Worker/`

```text
KuraStorage.Worker/
├── Workers/
│   ├── MediaTranscodeWorker.cs
│   ├── ImageDerivativeWorker.cs
│   ├── CacheCleanupWorker.cs
│   ├── IndexEventWorker.cs
│   ├── FullRescanWorker.cs
│   ├── OperationRecoveryWorker.cs
│   ├── TrashPurgeWorker.cs
│   ├── ExpiredUploadCleanupWorker.cs
│   └── AuditRetentionWorker.cs
├── Scheduling/
│   ├── WorkerScheduleOptions.cs
│   └── WorkerConcurrencyOptions.cs
├── Health/
├── DependencyInjection.cs
├── Program.cs
└── KuraStorage.Worker.csproj
```

- Worker classはJob取得・Application呼び出し・結果記録に限定する。
- 変換や復旧の業務ロジックをWorker classへ直接書かない。
- WorkerごとにCancellation、Lease、Retry、Concurrencyを定義する。

## 7.3 `server/src/KuraStorage.AdminCli/`

```text
KuraStorage.AdminCli/
├── Commands/
│   ├── Users/
│   ├── Devices/
│   ├── Sessions/
│   ├── Database/
│   ├── Storage/
│   ├── Index/
│   └── Operations/
├── Console/
│   ├── SecureInput.cs
│   ├── ConsoleTableWriter.cs
│   └── ExitCodeMapper.cs
├── DependencyInjection.cs
├── Program.cs
└── KuraStorage.AdminCli.csproj
```

- Command classは引数解析とApplication Command呼び出しに限定する。
- PasswordをCommand Line引数で受け取らない。
- CLI専用の直接SQL更新を禁止する。
- Exit Codeと監査ログを一貫させる。

---

## 8. Android構造

### 8.1 Module依存関係

```text
app
├── feature-connection
├── feature-auth
└── feature-files

feature-* ──> core-model / core-ui / 必要なcore-*
core-network ──> core-model / core-security
core-data ──> core-model / core-network / core-security
core-security ──> core-model
core-ui ──> core-model
```

ルール。

- Feature同士の直接依存を原則禁止する。
- 複数Featureの画面遷移は`app`で組み立てる。
- 共通Modelは`core-model`、共通UI部品は`core-ui`へ置く。
- Feature固有Model/UIをCoreへ上げない。
- `core-network`からFeatureを参照しない。
- `core-data`からAndroid UIを参照しない。

### 8.2 `apps/android/app/`

```text
app/
├── src/
│   ├── main/
│   │   ├── kotlin/com/kurastorage/app/
│   │   │   ├── KuraStorageApplication.kt
│   │   │   ├── MainActivity.kt
│   │   │   ├── navigation/
│   │   │   ├── di/
│   │   │   └── startup/
│   │   ├── res/
│   │   └── AndroidManifest.xml
│   ├── test/
│   └── androidTest/
└── build.gradle.kts
```

`app`はApplication、Activity、Navigation、DI composition、Startupだけを持つ。Feature実装を置かない。

### 8.3 Core Module

#### `core-model/`

```text
core-model/src/main/kotlin/com/kurastorage/core/model/
├── identity/
├── files/
├── sharing/
├── media/
├── backup/
├── connection/
└── errors/
```

- Server DTOやRoom Entityではなく、Android内で共有するDomain Modelを置く。
- Android SDK依存を可能な限り避ける。

#### `core-network/`

```text
core-network/src/main/kotlin/com/kurastorage/core/network/
├── api/
├── dto/
├── interceptor/
├── serialization/
├── route/
├── tls/
├── streaming/
└── errors/
```

- Retrofit/OkHttp等の実装、DTO、Interceptor、Network bindingを置く。
- Access Token付与とRefresh調整を集約する。
- Feature固有Repository実装を置かない。
- TLS検証無効化Codeを置かない。

#### `core-data/`

```text
core-data/src/main/kotlin/com/kurastorage/core/data/
├── repository/
├── mapper/
├── transfer/
└── errors/
```

- MVPで複数Featureが共有するRepository実装、DTO変換、Streaming Transfer調整を置く。
- Room Entity、DAO、WorkManagerは置かない。永続Queueが必要になった時点でMVP後の`core-database`を追加する。

#### MVP後: `core-database/`

```text
core-database/src/main/kotlin/com/kurastorage/core/database/
├── KuraStorageDatabase.kt
├── dao/
├── entity/
├── migration/
├── mapper/
└── transaction/
```

- Room Database、DAO、Entity、Migrationを置く。
- 秘密情報を保存しない。

#### `core-security/`

```text
core-security/src/main/kotlin/com/kurastorage/core/security/
├── keystore/
├── credentials/
├── backup-exclusion/
└── errors/
```

- UI、Feature Repositoryを置かない。

#### `core-ui/`

```text
core-ui/src/main/kotlin/com/kurastorage/core/ui/
├── components/
├── theme/
├── state/
├── formatting/
└── accessibility/
```

- 複数Featureで利用するCompose部品だけを置く。
- `FileListScreen`等のFeature Screenを置かない。

#### MVP後または必要時: `core-testing/`

```text
core-testing/
├── src/
│   ├── main/kotlin/com/kurastorage/core/testing/
│   │   ├── fixtures/
│   │   ├── fakes/
│   │   ├── assertions/
│   │   ├── coroutine/
│   │   └── compose/
│   └── test/kotlin/
└── build.gradle.kts
```

- 複数ModuleのTest Source Setから再利用するFake Repository、Test Dispatcher、Fixture Builder、Assertionを置く。
- `LoginViewModelTest`等の対象機能そのもののTest Classは置かず、対象Moduleの`src/test/`または
  `src/androidTest/`へ置く。
- Production Sourceから参照しない。依存は`testImplementation`または`androidTestImplementation`に限定し、
  Productionの`implementation`へ追加しない。
- `core-testing`自身のTestだけを`core-testing/src/test/`へ置く。

#### Core ModuleのTest Source Set

`core-model`、`core-network`、`core-database`、`core-security`、`core-ui`も、Test本体を各Module内へ配置する。

```text
core-network/
├── src/
│   ├── main/kotlin/
│   ├── test/kotlin/
│   └── androidTest/kotlin/
└── build.gradle.kts
```

- Android Runtimeを必要としないModel、Mapper、Interceptor、状態変換等は`src/test/`へ置く。
- Room、Keystore、WorkManager、Compose、Android Framework API等を実際に必要とするTestは
  `src/androidTest/`へ置く。
- Android依存を持たないModuleでは、不要な`src/androidTest/`を作成しない。

### 8.4 Feature Module標準構造

```text
feature-files/
├── src/
│   ├── main/
│   │   └── kotlin/com/kurastorage/feature/files/
│   │       ├── domain/
│   │       │   ├── repository/
│   │       │   └── usecase/
│   │       ├── data/
│   │       │   ├── repository/
│   │       │   ├── remote/
│   │       │   ├── local/
│   │       │   └── mapper/
│   │       ├── presentation/
│   │       │   ├── list/
│   │       │   ├── detail/
│   │       │   └── components/
│   │       └── di/
│   ├── test/
│   └── androidTest/
└── build.gradle.kts
```

配置ルール。

- `presentation/`: Screen、ViewModel、UiState、Action、Feature固有Component。
- `domain/`: Repository interface、Use Case、Feature固有Rule。
- `data/`: Repository実装、Remote/Local Data Source、Mapper。
- `di/`: Module内のDI binding。
- 小規模Featureでは空Directoryを作らず、必要になった段階で追加する。
- UIから`data/`を直接参照しない。

### 8.5 Feature一覧

| Module | 主責務 |
| --- | --- |
| `feature-connection` | LOCAL_DIRECT/REMOTE_SECURE/DISCONNECTED、Health、RemoteAccessGuidanceController |
| `feature-auth` | 初回Device登録、Login、Refresh、Logout、Device失効対応 |
| `feature-files` | Home、一覧、詳細、Folder作成、Streaming Upload、Range Download、Trash、Restore |

`feature-media`、`feature-sharing`、`feature-backup`、`feature-settings`はMVP後に必要となった時点で追加する。

### 8.6 Gradle Build Logic

```text
apps/android/build-logic/
├── convention/
│   └── src/main/kotlin/
│       ├── kurastorage.android.application.gradle.kts
│       ├── kurastorage.android.library.gradle.kts
│       ├── kurastorage.android.compose.gradle.kts
│       └── kurastorage.android.test.gradle.kts
└── settings.gradle.kts
```

共通Compile SDK、minSdk 29、Kotlin、Compose、Lint、Test設定をConvention Pluginへ集約する。各Moduleの`build.gradle.kts`へ同じ設定を複製しない。

---

## 9. Tests構造

### 9.1 Server Test Project

```text
server/tests/
├── KuraStorage.Domain.Tests/
│   ├── Identity/
│   ├── Files/
│   ├── Sharing/
│   ├── Transfer/
│   ├── Media/
│   ├── Backup/
│   └── TestSupport/
├── KuraStorage.Application.Tests/
│   ├── Identity/
│   ├── Authorization/
│   ├── Files/
│   ├── Transfer/
│   ├── Sharing/
│   ├── Media/
│   ├── Backup/
│   ├── Indexing/
│   └── TestSupport/
├── KuraStorage.Infrastructure.IntegrationTests/
│   ├── Persistence/
│   ├── Storage/
│   ├── Identity/
│   ├── Media/
│   └── Fixtures/
├── KuraStorage.Api.IntegrationTests/
│   ├── Identity/
│   ├── Files/
│   ├── Transfer/
│   ├── Sharing/
│   ├── Media/
│   ├── Backup/
│   └── Fixtures/
├── KuraStorage.Worker.IntegrationTests/
├── KuraStorage.AdminCli.IntegrationTests/
└── KuraStorage.E2ETests/
    ├── Scenarios/
    ├── Fixtures/
    ├── RaspberryPi/
    └── AndroidClient/
```

- Unit TestはProduction namespace構造をMirrorする。
- Integration Testは技術境界または機能Scenarioで分ける。
- Test Fixture、Builder、Fakeは各Test Projectの`TestSupport/`へ置く。
- 複数Test Projectで共有するものだけ、専用`KuraStorage.Testing` Project追加を検討する。
- Production ProjectをTest用Utilityで汚染しない。

### 9.2 Performance Test

```text
server/tests/performance/
├── k6/
│   ├── file-list.js
│   ├── search.js
│   ├── range-download.js
│   ├── token-refresh.js
│   └── upload.js
├── datasets/
├── results/.gitkeep
└── README.md
```

生成されたResult本体はVersion管理しない。基準値や要約だけを`docs/testing/`へ保存する。

### 9.3 Security Test

```text
server/tests/security/
├── api/
├── storage/
├── authentication/
├── remote-access/
├── process-boundary/
└── README.md
```

攻撃用Payload、検証Script、期待する拒否結果を配置する。実秘密情報やProduction Endpointを置かない。

### 9.4 Android Test

AndroidのTest本体は対象コードと同じGradle Module内へ配置する。Serverの`server/tests/`に相当する
`apps/android/tests/`は作成しない。

```text
apps/android/
├── app/
│   └── src/
│       ├── test/kotlin/
│       └── androidTest/kotlin/
├── core-network/
│   └── src/
│       ├── test/kotlin/
│       └── androidTest/kotlin/
└── feature-backup/
    └── src/
        ├── test/kotlin/
        └── androidTest/kotlin/
```

| 配置 | 実行環境 | 主な対象 |
| --- | --- | --- |
| `<module>/src/test/` | 開発PC・CI上のJVM | Use Case、ViewModel、Mapper、Policy、Repositoryロジック、純粋なKotlin処理 |
| `<module>/src/androidTest/` | EmulatorまたはAndroid実機 | Compose UI、Room、WorkManager、Keystore、MediaStore、SAF、Android API |
| `app/src/androidTest/` | EmulatorまたはAndroid実機 | Navigationを含む複数FeatureのUI統合、Application起動、画面間フロー |

- Test Classは原則として対象Production Packageの構造をMirrorする。
- `core-testing/`は共通Fake、Fixture、Test Rule等の再利用部品だけを提供し、各機能のTest本体を集約しない。
- `app/src/androidTest/`のUI統合Testと、Raspberry Piを含む製品全体E2Eを同一視しない。
- Android Runtimeを必要としないTestを、理由なく`src/androidTest/`へ置かない。
- Source Setが不要なModuleでは空Directoryを作成しない。

---

## 10. `contracts/`と`docs/`

### 10.1 `contracts/`

```text
contracts/
└── openapi/
    └── kurastorage-api.yaml
```

- ServerとAndroid、将来のWeb・iOSの間で共有するHTTP API契約を置く。
- OpenAPIをRequest/Response、Error Code、認証方式、互換性判定のSource of Truthとする。
- C#、Kotlin、Swift、TypeScriptの生成物は各Project側へ出力し、`contracts/`へCommitしない。
- 運用説明や設計判断は`contracts/`へ置かず、`docs/api/`へ記載する。

### 10.2 `docs/`

```text
docs/
├── product-requirements.md
├── functional-design.md
├── architecture-design.md
├── repository-structure.md
├── development-guidelines.md
├── environment-info.example.md
├── api/
│   ├── error-codes.md
│   └── compatibility-policy.md
├── adr/
│   ├── README.md
│   └── 0001-modular-monolith.md
├── operations/
│   ├── installation.md
│   ├── upgrade.md
│   ├── release.md
│   ├── backup-and-restore.md
│   ├── incident-recovery.md
│   ├── zerotier-network-access.md
│   └── storage-replacement.md
├── ui/
│   └── android/
│       └── mockups/
│           ├── connection-auth/
│           ├── home-navigation/
│           ├── files-media/
│           └── backup-settings/
└── testing/
    ├── test-plan.md
    ├── e2e-scenarios.md
    ├── performance-baseline.md
    ├── security-test-plan.md
    ├── release-readiness.md
    └── code-quality-gates.md
```

### 10.3 文書配置ルール

- Product全体の判断はTop-level 5文書へ置く。
- 機械可読なAPI契約は`contracts/openapi/`、説明・互換性方針・Error一覧は`docs/api/`へ置く。
- 重要な技術判断の経緯はADRへ置く。
- 実行手順、障害対応は`docs/operations/`へ置く。
- `docs/ui/android/mockups/`には、Android画面実装時に参照するUI Mockup画像を置く。
- Test計画と測定Resultは`docs/testing/`へ置く。
- Source Code内のREADMEへ設計判断を分散させない。ただしDirectory固有の実行方法はREADMEを置いてよい。

---

## 11. `deployment/`

`deployment/`は、Raspberry Piへ配置する設定原本と配置処理を管理する。Raspberry Pi上の実Pathをリポジトリ内へ再現するDirectoryではない。

```text
deployment/
├── config/
│   ├── server/
│   │   ├── appsettings.Development.example.json
│   │   ├── appsettings.Testing.json
│   │   └── environment.example
│   ├── nginx/
│   │   └── kurastorage.conf.example
│   ├── systemd/
│   │   ├── kurastorage-api.service
│   │   ├── kurastorage-worker.service
│   │   └── README.md
│   ├── firewall/
│   │   ├── nftables.conf
│   │   └── README.md
│   ├── tls/
│   │   └── network_security_config.example.xml
│   └── logrotate/
│       └── kurastorage
└── raspberry-pi/
    ├── install.sh
    ├── upgrade.sh
    ├── rollback.sh
    ├── verify.sh
    └── uninstall.sh
```

配置関係は次のように対応する。

| リポジトリ内の原本 | Raspberry Pi上の代表的な実配置先 |
| --- | --- |
| `deployment/config/nginx/kurastorage.conf.example` | `/etc/nginx/sites-available/kurastorage.conf` |
| OpenSSL Scriptが生成した`server/server.crt`と`server/server.key` | `/etc/kurastorage/tls/`。生成物とRoot CA秘密鍵はRepository外 |
| `deployment/config/tls/network_security_config.example.xml` | Android Appの`res/xml/network_security_config.xml`の原本 |
| `deployment/config/systemd/kurastorage-api.service` | `/etc/systemd/system/kurastorage-api.service` |
| `deployment/config/logrotate/kurastorage` | `/etc/logrotate.d/kurastorage` |
| Server publish成果物 | `/opt/kurastorage/`配下 |

- Version管理するのはSecretを含まないTemplateとTest設定だけとする。
- `.example`に本物らしいSecretを記載しない。
- 環境固有の非Secret情報はGit管理外の`docs/environment-info.md`へ置き、公開用の項目定義だけを`docs/environment-info.example.md`へ置く。Tracked文書、Source、設定例は実値ではなく項目IDまたは正式な設定入力を参照する。
- `deployment/config/`を設定原本のSource of Truthとし、Raspberry Pi上での直接編集を通常運用にしない。
- 生成Package本体はGitへCommitせず、CI ArtifactまたはReleaseへ置く。
- EF Core Migrationは`server/src/KuraStorage.Infrastructure/`配下で管理し、`deployment/`へ重複配置しない。

---

## 12. `scripts/`

```text
scripts/
├── ci/
│   ├── verify-server.sh
│   ├── verify-android.sh
│   ├── verify-config.sh
│   ├── verify-e2e.sh
│   ├── verify-performance.sh
│   ├── verify-security.sh
│   ├── build-release.sh
│   └── generate-sbom.sh
├── development/
│   ├── start-postgres.sh
│   ├── reset-local-db.sh
│   ├── create-dev-certificate.sh
│   └── seed-development-data.sh
├── maintenance/
│   ├── backup-database.sh
│   ├── restore-database.sh
│   ├── verify-storage.sh
│   ├── collect-diagnostics.sh
│   ├── generate-tls-certificates.sh
│   ├── verify-tls-certificates.sh
│   └── generate-jwt-signing-key.sh
└── test-data/
    ├── generate-file-catalog.py
    └── generate-media-fixtures.sh
```

- CI、ローカル開発、保守、Test Data作成の補助処理を用途別Directoryへ置く。
- Raspberry PiへのInstall、Upgrade、Rollback、Uninstallは`deployment/raspberry-pi/`へ置き、`scripts/deployment/`を重複して作らない。
- 1回限りの個人作業ScriptをCommitしない。
- Database状態を変更する運用処理は可能な限りAdmin CLIへ実装し、ScriptからCLIを呼ぶ。
- `scripts/maintenance/`から直接Business TableをSQL更新しない。

---

## 13. GitHub・開発支援Directory

### 13.1 `.github/`

- `workflows/server-ci.yml`: .NET restore/build/format/test
- `workflows/android-ci.yml`: Gradle lint/unit/instrumented test
- `workflows/configuration-ci.yml`: Shell、Nginx、systemd、Markdown検証
- `workflows/security-scan.yml`: Secret、Dependency、Code scan
- `workflows/release.yml`: ARM64 publish、Android artifact、SBOM、Checksum
- `pull_request_template.md`: Development GuidelinesのPR項目を反映

### 13.2 Codex開発支援設定

KuraStorageのAI開発支援にはCodexを使用する。Claude Code専用の`.claude/`は作成しない。

Codex向けのRepository構成は次を基本とする。

```text
AGENTS.md
.agents/
└── skills/
.codex/
├── agents/
├── rules/
└── config.toml
```

各要素の役割は次のとおりとする。

| Path | 役割 | 配置方針 |
| --- | --- | --- |
| `AGENTS.md` | CodexがRepository内で常に従う開発指示 | Rootへ配置する。Build、Test、Lint、設計規則、禁止事項、完了条件を記載する |
| `.agents/skills/` | 複数作業で再利用するCodex Skill | 必要な場合だけ配置する。各Skillは専用Directoryと`SKILL.md`を持つ |
| `.codex/config.toml` | Repository固有のCodex設定 | Model、Approval、Sandbox等を共有する必要がある場合だけ配置する |
| `.codex/agents/` | Repository固有のCustom Agent定義 | Review、探索、Security確認等を専用Agentへ分ける場合だけTOML Fileを配置する |
| `.codex/rules/` | CodexのCommand実行Rule | Sandbox外Commandの許可、確認、禁止をRepository単位で制御する場合だけ配置する |

配置ルール。

- Repository共通の指示はRootの`AGENTS.md`をSource of Truthとする。
- ServerまたはAndroid固有の指示が必要な場合は、対象Directoryに`AGENTS.md`または`AGENTS.override.md`を追加する。
- 再利用可能な作業手順は独自Command Fileではなく`.agents/skills/<skill-name>/SKILL.md`として定義する。
- Custom Promptは使用せず、共有可能な処理はSkillへ集約する。
- `.codex/config.toml`へProduction Secret、Access Token、個人情報、環境固有Absolute Pathを記載しない。
- 個人専用設定はRepositoryへCommitせず、原則として利用者の`~/.codex/`または`~/.agents/`で管理する。
- Project固有のCodex設定は、信頼済みRepositoryでのみ読み込まれる前提とし、Source Review対象に含める。

### 13.3 `.steering/`

作業単位の一時設計を使用する場合は次の構造とする。

```text
.steering/
└── 20260712-add-upload-session/
    ├── requirements.md
    ├── design.md
    └── tasklist.md
```

`.steering/`は作業単位の要件、設計、実装計画、進捗をRepository内で共有するため、Git管理対象とする。

`.steering/`は今回の作業内容と実装手順を管理する文書であり、プロジェクト全体へ継続的に適用する正式な仕様や設計変更は、必要に応じて`docs/`へ反映する。

---

## 14. 命名規則

### 14.1 Directory

| 対象 | 規則 | 例 |
| --- | --- | --- |
| .NET Project | PascalCase | `KuraStorage.Application` |
| .NET内部Directory | PascalCase | `ValueObjects`, `RegisterDevice` |
| Android Module | kebab-case | `feature-files`, `core-network` |
| Kotlin Package Directory | lowercase | `com/kurastorage/feature/files` |
| Script/Config Directory | kebab-caseまたはlowercase | `test-data`, `systemd` |
| Document Directory | kebab-caseまたはlowercase | `backup-and-restore.md` |

### 14.2 File

| 対象 | 規則 | 例 |
| --- | --- | --- |
| C# Class/Record/Enum | PascalCase.cs | `RefreshSession.cs` |
| C# Handler | `[UseCase]CommandHandler.cs` | `LoginCommandHandler.cs` |
| C# EF Configuration | `[Entity]Configuration.cs` | `FileEntryConfiguration.cs` |
| C# Test | `[Target]Tests.cs` | `StorageGuardTests.cs` |
| Kotlin Class/File | PascalCase.kt | `ConnectionCoordinator.kt` |
| Kotlin Top-level Function | camelCase.ktを許可 | `formatFileSize.kt` |
| Gradle Module File | `build.gradle.kts` | 固定 |
| Shell | kebab-case.sh | `verify-storage.sh` |
| Markdown | kebab-case.md | `backup-and-restore.md` |
| YAML Workflow | kebab-case.yml | `server-ci.yml` |

### 14.3 曖昧名称の禁止

次を新規Directory/Fileの名前として単独使用しない。

- `misc`
- `stuff`
- `temp`
- `new`
- `old`
- `helper`
- `helpers`
- `util`
- `utils`
- `manager`
- `common`

共通処理が必要な場合は、`PathResolution`、`SecurityContext`、`Formatting`のように責務を表す名前を使用する。`SharedKernel`と`Shared`は本書で定義した限定用途にのみ使用する。

---

## 15. File配置判断

新しいFileを追加する際は次の順で判断する。

1. どの実行プロセスで使うか。
2. 業務Ruleか、Use Caseか、外部技術実装か、Presentationか。
3. どの機能Moduleに属するか。
4. 複数Moduleで本当に共通か。
5. Unit/Integration/E2EのどこでTestするか。

例。

| 追加物 | 配置先 |
| --- | --- |
| Refresh Token再利用Rule | `server/src/KuraStorage.Domain/Identity/Policies/` |
| LoginのUse Case | `server/src/KuraStorage.Application/Identity/Commands/Login/` |
| Argon2id実装 | `server/src/KuraStorage.Infrastructure/Identity/` |
| Login HTTP Contract | `server/src/KuraStorage.Api/Contracts/Identity/` |
| Login Endpoint | `server/src/KuraStorage.Api/Endpoints/Identity/` |
| 動画Job polling UI | `apps/android/feature-media/.../presentation/` |
| Transcode Worker Loop | `server/src/KuraStorage.Worker/Workers/MediaTranscodeWorker.cs` |
| FFmpeg引数生成 | `server/src/KuraStorage.Infrastructure/Media/` |
| Device失効CLI | `server/src/KuraStorage.AdminCli/Commands/Devices/` |

---

## 16. 依存禁止ルール

### 16.1 Server

- Domain → Application/Infrastructure/Host
- Application → Infrastructure/Host
- Infrastructure → Host
- Host → 別Host
- Endpoint → DbContext/FileStore/Process
- Worker → API Endpoint/Contract
- AdminCli → API Endpoint

### 16.2 Android

- `core-*` → `feature-*`
- `feature-a` → `feature-b`のPresentation/Data実装
- `core-model` → Android UI/Network/Database
- Production Source Setの`implementation` → `core-testing`（Test Source Setからの`testImplementation`・`androidTestImplementation`は許可）

### 16.3 検出

- .NET Project ReferenceをCIで確認する。
- AndroidのGradle DependencyをConvention/CIで確認する。
- 循環参照をBuild失敗として扱う。
- Architecture Testの導入を推奨する。

---

## 17. 拡張ルール

### 17.1 新しいBackend機能

1. 既存Moduleに属する場合、Domain/Application/Infrastructure/Hostへ縦に追加する。
2. 独立した業務概念とデータ所有権を持つ場合、新しいModuleを追加する。
3. Moduleを追加しても新しい実行Processを直ちに増やさない。
4. 高負荷、権限、障害分離が必要な場合だけHost分離をArchitecture Reviewする。

### 17.2 新しいAndroid機能

- 既存Feature内に収まる場合はScreen/Use Caseを追加する。
- 独立Navigation、独立Repository、複数Screenを持つ場合は`feature-*`追加を検討する。
- Feature固有CodeをCoreへ置いて見かけ上再利用しない。

### 17.3 Phase 2 Webと将来のiOS

Phase 2でWeb実装を開始するときは、クライアント群の一つとして次を追加する。

```text
apps/web/
├── src/
├── tests/
├── public/
├── package.json
└── ...
```

iOS実装を開始するときは`apps/ios/`を追加する。WebとiOSは既存`/api/v1`とApplication Ruleを利用し、Backend内へクライアント固有Business Ruleを追加しない。各ClientのBuild、CI、Version、Releaseは独立させる。


### 17.4 分割の目安

- Directory内のFileが10〜15個を超え、明確なSub責務がある場合はSubdirectory化する。
- 1Fileが500行を超える場合は分割を強く検討する。
- 共通化は2箇所で似ているだけでは行わず、責務と変更理由が同じ場合に行う。
- 循環依存を解消するために無関係な`Common`へ移動しない。InterfaceまたはModule境界を見直す。

---

## 18. 除外設定

### 18.1 `.gitignore`

最低限、次を除外する。

```gitignore
# .NET
**/bin/
**/obj/
**/TestResults/
*.user
*.suo

# Android / Gradle
apps/android/.gradle/
apps/android/**/.gradle/
apps/android/.kotlin/
apps/android/**/build/
apps/android/local.properties
*.jks
*.keystore

# IDE / OS
.idea/
.vscode/*.local.json
.DS_Store
Thumbs.db

# Secrets / local configuration
.env
.env.*
!**/.env.example
**/appsettings.Development.json
**/appsettings.Production.json
**/*secret*
**/*.key
**/*.pem
!**/*.example.pem

# Test / generated
coverage/
artifacts/
reports/
server/tests/performance/results/*
!server/tests/performance/results/.gitkeep

# Logs / runtime
*.log
logs/
*.pid
*.sock
```

秘密情報のpatternはSecret Scanと併用する。`.gitignore`だけを漏えい防止策にしない。`.steering/`は実行計画をRepositoryで共有するため除外しない。

`docs/environment-info.md`はIP Address、Hostname、端末情報、証明書Path／公開Fingerprint等のLocal情報用であり、Git管理しない。項目構造は`docs/environment-info.example.md`を正とする。秘密鍵、Password、Token、ZeroTier Node Identity、認可情報はLocal情報文書にも保存しない。


### 18.2 Formatter/Lint除外

生成物、Migration snapshot、Build出力、Test Resultだけを除外する。手書きSourceをFormatter/Lint除外にしない。

---

## 19. 構造変更の手順

Repository構造を変更する場合は次を行う。

1. Architecture上の責務と依存方向を確認する。
2. 本書のTree、役割、依存表を更新する。
3. Namespace/Package、Project Reference、Gradle Dependencyを更新する。
4. CIとScript内のPathを更新する。
5. Testの配置を同時に移動する。
6. 空の旧Directoryと暫定Compatibility Codeを残さない。
7. PRに移動理由と影響範囲を記載する。

単なる好みで大規模なDirectory移動を行わない。機能変更と大規模構造変更は可能な限り別PRに分ける。

---

## 20. 完了チェックリスト

- [ ] ArchitectureのProject構成と一致している
- [ ] Domain/Application/Infrastructure/Hostの依存方向が明確である
- [ ] ApplicationのIdentity、Authorization、Files、Transfer、Sharing、Media、Backup、Indexing、Administration、Auditが配置可能である
- [ ] Androidの`core-*`と`feature-*`の責務が明確である
- [ ] Unit、Integration、Android、E2E、Performance、Security Testの配置が定義されている
- [ ] PostgreSQL MigrationとQueryの配置が定義されている
- [ ] Production SecretがRepository外である
- [ ] 曖昧な`utils`、`helpers`、`misc`へ依存しない
- [ ] Codex用の`AGENTS.md`、`.agents/skills/`、`.codex/`の役割が定義されている
- [ ] Claude Code専用の`.claude/`へ依存しない
- [ ] Phase 2 Webを追加できる余地がある
