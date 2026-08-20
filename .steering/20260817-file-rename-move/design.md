# ファイル・フォルダの名前変更・移動 設計書

## 1. アーキテクチャ概要

既存のモジュラーモノリス、所有User境界、`FileEntry`索引、`FileOperation`操作ジャーナル、`StorageGuard`、同一ファイルシステム内のrenameを維持する。

名前変更と移動は同じ「FileEntryの配置変更」だが、API入力、監査イベント、`FileOperationType`、エラーを区別する。1回のAPI要求では名前変更または移動のどちらか一方だけを受け付け、複合変更は行わない。

```mermaid
flowchart LR
    UI[Android feature-files] --> Repo[Android FileRepository]
    Repo --> API[PATCH /api/v1/files/{fileId}]
    API --> Service[FileService]
    Service --> Guard[StorageGuard]
    Service --> Lock[PostgreSQL advisory lock]
    Service --> Catalog[(FileEntry)]
    Service --> Journal[(FileOperation)]
    Service --> Store[FileStore]
    Store --> HDD[(exFAT HDD)]
    Recovery[FileOperationRecoveryService] --> Lock
    Recovery --> Journal
    Recovery --> Catalog
    Recovery --> Store
```

実装は次の2つのPull Request単位に分ける。

1. Server・OpenAPI・永続文書: Domain、Application、Infrastructure、API、復旧、契約、自動Testを完成させる。
2. Android・実機E2E: Android Data・UI、自動Test、Raspberry PiとAndroid実機確認を完成させる。

PR2はPR1が`main`へMergeされた後に開始する。

## 2. API設計

### 2.1 Endpoint

```http
PATCH /api/v1/files/{fileId}
Authorization: Bearer <access-token>
Content-Type: application/json
```

既存の正式仕様にある「`PATCH`による名前変更・移動」を1 Endpointとして具体化する。

### 2.2 Request

```json
{
  "name": "new-name.txt",
  "parentId": null
}
```

```json
{
  "name": null,
  "parentId": "11111111-1111-1111-1111-111111111111"
}
```

`UpdateFileRequest`は次の規則を持つ。

- `name`と`parentId`のどちらか一方だけを指定する。
- 両方指定、両方未指定、空文字の`name`、空GUIDの`parentId`は`400 VALIDATION_FAILED`とする。
- 名前変更時は`FileName.TryCreate`でUnicode正規化、Trim、長さ、区切り文字、NUL、制御文字を検証する。
- 移動時の`parentId`はClientが取得したFolder IDであり、物理パスを受け取らない。
- 現在と同じ正規化済み名前、または現在と同じ親Folderへの変更は副作用のない成功として現在の`FileItem`を返す。

1要求で複合変更を許可しないことで、操作ジャーナル、監査、再試行結果を一意にする。名前変更後に移動したい場合は、名前変更成功後に別の移動要求を送る。

### 2.3 Response

- 成功: `200 OK`と更新後の既存`FileItem` DTO。
- ResponseのFile ID、`fileVersion`、種別、所有者境界、内容由来情報は維持する。
- `updatedAt`はServerで操作確定時刻へ更新する。

### 2.4 Error契約

| HTTP | Error Code | 条件 |
| --- | --- | --- |
| 400 | `VALIDATION_FAILED` | Request形状、新しい名前、GUIDが不正 |
| 401 | `AUTHENTICATION_REQUIRED` | Access Tokenがない、無効、期限切れ |
| 404 | `FILE_NOT_FOUND` | 対象、移動先が存在しない、非`ACTIVE`、他User所有 |
| 409 | `FILE_NAME_CONFLICT` | 同じ親に同名の`ACTIVE`項目がある、HDD上に移動先がある |
| 409 | `FILE_MOVE_CYCLE` | Folderを自分自身または子孫へ移動しようとした |
| 409 | `FILE_OPERATION_NOT_ALLOWED` | 個人Rootを変更しようとした、操作状態が変更不能 |
| 409 | `RECOVERY_REQUIRED` | 自動判定不能なDB・HDD状態を検出した |
| 503 | `STORAGE_UNAVAILABLE` | HDD、Mount、Storage ID、Filesystem、書き込み状態が不正 |

他Userの対象または移動先は認可情報を漏らさないため`FILE_NOT_FOUND`へ統一する。

## 3. Domain設計

### 3.1 `FileEntry`

`FileEntry`へ次のDomain操作を追加する。

```csharp
public void Rename(FileName name, RelativeStoragePath targetPath, DateTimeOffset now);

public void MoveTo(Guid parentId, RelativeStoragePath targetPath, DateTimeOffset now);

public void RelocateDescendant(RelativeStoragePath targetPath, DateTimeOffset now);
```

不変条件は次のとおり。

- `Status == ACTIVE`だけを変更できる。
- Rootを表す`ParentId == null`の項目は変更できない。
- `Rename`は`Name`と`RelativePath`だけを変更する。
- `MoveTo`は`ParentId`と`RelativePath`だけを変更する。
- 子孫は`RelativePath`だけをsource prefixからtarget prefixへ置換する。
- `Id`、`OwnerUserId`、`EntryType`、`MimeType`、`Size`、`CreatedAt`、`FileVersion`は変更しない。
- 変更対象と子孫の`UpdatedAt`を同じServer時刻へ更新する。
- DomainはHTTP Status、DB例外、物理絶対パスを扱わない。

### 3.2 `FileOperation`

`FileOperationType`へ次を追加する。

```text
RENAME
MOVE
```

既存列を次の意味で使用する。

| 列 | 名前変更・移動での値 |
| --- | --- |
| `owner_user_id` | 所有User ID |
| `file_entry_id` | 変更対象File ID |
| `source_relative_path` | 操作前の相対パス |
| `target_relative_path` | 操作後の相対パス |
| `operation_type` | `RENAME`または`MOVE` |
| `status` | `PENDING`、`FILESYSTEM_DONE`、`COMPLETED`、`RECOVERY_REQUIRED` |
| `idempotency_key` | `null`。配置変更は現在状態への収束で冪等化する |

`operation_type`は文字列列であり既存長の範囲内なので、Rename・Move追加だけを目的とするDB Migrationは作成しない。新しいTableまたは列も追加しない。

### 3.3 監査ログ

既存`AuditLog`へ次を記録する。

- `action`: `FILE_RENAME`または`FILE_MOVE`
- `actorUserId`: Access Tokenの`sub`
- `actorDeviceId`: Access Tokenの`device_id`
- `targetType`: `FILE_ENTRY`
- `targetId`: File ID
- `resultCode`: `SUCCESS`または公開Error Code
- `requestId`: HTTP Request ID

名前、相対パス、物理絶対パス、ファイル内容は監査ログへ保存しない。成功監査はFileEntry更新と`FileOperation(COMPLETED)`と同じDBトランザクションで保存する。拒否結果はHDD操作を伴わない範囲で記録し、監査記録失敗を理由に安全性検証を迂回しない。

## 4. Application・Infrastructure設計

### 4.1 Command

```csharp
public sealed record RenameFileCommand(
    Guid OwnerUserId,
    Guid ActorDeviceId,
    Guid FileEntryId,
    string Name,
    string RequestId);

public sealed record MoveFileCommand(
    Guid OwnerUserId,
    Guid ActorDeviceId,
    Guid FileEntryId,
    Guid TargetParentId,
    string RequestId);
```

`FileService.RenameAsync`と`FileService.MoveAsync`は共通の内部Relocate処理を使用するが、公開CommandとError分岐は分離する。

### 4.2 Repository拡張

`IFileRepository`へ次の能力を追加する。

- 対象ID群から導出したPostgreSQL advisory lockを昇順に取得し、操作終了まで同じConnectionで保持する。
- 所有Userと正規化済み相対パスから`ACTIVE` Folderを取得する。
- 既存の`ListDescendantsAsync`でFolder配下を更新対象として追跡する。
- File操作と同じDbContextへ`AuditLog`を追加する。

名前変更時は対象、現在の親をLock対象とする。移動時は対象、現在の親、移動先FolderをLock対象とする。複数LockはGUIDから導出した64-bit keyの昇順で取得し、要求処理とRecoveryの両方で同じ規則を使用する。

PostgreSQLの部分一意Index`(owner_user_id, parent_id, name) WHERE status = 'ACTIVE'`を最終防御として維持する。事前確認後に競合が起きた場合も`FilePersistenceConflictException`を`FILE_NAME_CONFLICT`へ変換する。

### 4.3 名前変更処理

```text
1. StorageGuardで書き込み可能性を確認する。
2. 所有Userの対象を取得し、ACTIVE・非Rootを検証する。
3. 新しい名前をFileNameへ正規化する。
4. 対象と現在親のadvisory lockを取得し、対象を再取得する。
5. 同じ正規化済み名前なら現在DTOを返す。
6. 同じ親の同名ACTIVE項目とHDD移動先不存在を確認する。
7. source/target pathを持つFileOperation(RENAME, PENDING)を保存する。
8. FileStore.MoveAsyncで同一Filesystem内renameを実行する。
9. FileOperationをFILESYSTEM_DONEにして保存する。
10. Folderならsource prefixに一致する子孫を取得する。
11. FileEntryのName・RelativePathと子孫RelativePathを更新する。
12. 成功AuditLogとFileOperation(COMPLETED)を同じDB Transactionで保存する。
13. 更新後FileItemを返す。
```

### 4.4 移動処理

```text
1. StorageGuardで書き込み可能性を確認する。
2. 所有Userの対象と移動先を取得する。
3. 対象がACTIVE・非Root、移動先が同じUserのACTIVE Folderであることを検証する。
4. 対象、現在親、移動先のadvisory lockを取得し、対象と移動先を再取得する。
5. 同じ親なら現在DTOを返す。
6. Folderの場合、移動先IDが対象IDと一致しないことを確認する。
7. Folderの場合、移動先RelativePathが対象RelativePath + "/"で始まらないことを確認する。
8. 移動先の同名ACTIVE項目とHDD移動先不存在を確認する。
9. source/target pathを持つFileOperation(MOVE, PENDING)を保存する。
10. FileStore.MoveAsyncで同一Filesystem内renameを実行する。
11. FileOperationをFILESYSTEM_DONEにして保存する。
12. Folderならsource prefixに一致する子孫を取得する。
13. 対象ParentId・RelativePathと子孫RelativePathを更新する。
14. 成功AuditLogとFileOperation(COMPLETED)を同じDB Transactionで保存する。
15. 更新後FileItemを返す。
```

### 4.5 ファイルシステム操作

`FileStore.MoveAsync`を再利用する。

- sourceとtargetは`RelativeStoragePath`としてStorage Root内へ解決する。
- 経路中および対象のSymlinkを拒否する。
- targetが存在する場合は上書きしない。
- FileとFolderを区別してrenameする。
- sourceとtargetが同じStorage Root・同一Filesystemであることを前提とする。
- IOExceptionを一律成功扱いせず、再確認したsource/target存在状態に基づき競合またはStorage Errorへ変換する。

### 4.6 Folder配下更新

Folderの名前変更・移動では、DB上の子孫を`sourceRelativePath + "/"`のprefixで取得する。各子孫の相対パスを次で更新する。

```text
newPath = targetPrefix + oldPath[sourcePrefix.length..]
```

HDDはDirectory renameで配下を一括移動し、DBは対象と全子孫を1回のDB Transactionで更新する。最大階層深度64を維持し、移動後の深度が64を超える場合は`VALIDATION_FAILED`としてHDD操作前に拒否する。

## 5. 障害復旧設計

### 5.1 状態判定

Recoveryは対象File IDのadvisory lockを取得し、FileEntry、source、targetの状態を再取得する。

| DB RelativePath | source | target | 処理 |
| --- | --- | --- | --- |
| source | あり | なし | HDD renameを再実行し、DB確定へ進む |
| source | なし | あり | `FILESYSTEM_DONE`へ進め、DB確定する |
| target | なし | あり | 既にDB確定済みとしてOperationを`COMPLETED`にする |
| 任意 | あり | あり | 上書きせず`RECOVERY_REQUIRED`にする |
| 任意 | なし | なし | `RECOVERY_REQUIRED`にする |
| 上記以外 | 任意 | 任意 | `RECOVERY_REQUIRED`にする |

source、target存在確認では対象の`FileEntryType`を使用し、Operation TypeだけでDirectoryと判定しない。

### 5.2 DB確定

`FILESYSTEM_DONE`からの確定では次を実行する。

- `RENAME`: target path末尾から`FileName`を復元し、現在ParentIdを維持する。
- `MOVE`: target pathの親RelativePathから同じUserの`ACTIVE` Folderを取得し、ParentIdを更新する。
- Folderの場合はDBに残るsource prefixから子孫を取得し、target prefixへ置換する。
- File ID、所有者、内容属性、`fileVersion`を変更しない。
- 対象更新、子孫更新、成功監査、Operation完了を同じDB Transactionで保存する。

target親Folderが存在しない、非`ACTIVE`、別User所有、またはtarget pathとの対応が一意でない場合は自動確定せず`RECOVERY_REQUIRED`にする。

### 5.3 通常一覧からの隔離

対象File IDに未完了の`RENAME`または`MOVE` Operationがある場合、一覧・詳細・Download・変更操作から対象と配下を除外または`RECOVERY_REQUIRED`で拒否する。Recovery完了後に再び公開する。

## 6. Android設計

### 6.1 Network・Data

`core-network`へ`UpdateFileRequestDto`と`PATCH` APIを追加する。`core-data`の`FileRepository`へ次を追加する。

```kotlin
suspend fun rename(fileId: String, name: String): FileEntry
suspend fun move(fileId: String, targetParentId: String): FileEntry
```

共通API ExecutorによるAccess Token更新と既存Error変換を再利用する。新しいError Codeを`VALIDATION`、`CONFLICT`、`STORAGE`へ分類し、Request IDを保持する。

LAN用OkHttpClientは、Socket作成ごとに現在の非VPN Wi-FiまたはEthernet `Network`を再取得する委譲`SocketFactory`を使用する。これにより、通信結果不明後のWi-Fi再接続で`Network`ハンドルが変更されても、再取得を新しいLAN経路で実行できる。

### 6.2 ViewModel状態

`FileBrowserState`へ次を追加する。

- 名前変更対象と入力状態
- 移動対象
- 移動先Pickerの現在Folder ID、Folder stack、候補一覧、Loading、Error
- 変更処理中状態
- 操作結果または再取得が必要な状態

`FileBrowserViewModel`へ`rename`、`startMove`、`openMoveFolder`、`moveBack`、`confirmMove`、`dismissMutation`を追加する。

成功時は現在一覧をServerから再取得し、選択中詳細がある場合は対象詳細も再取得する。通信結果不明時はローカルで成功状態を合成せず、利用者へ再読み込みを提供する。

### 6.3 名前変更UI

- 通常一覧のActionまたは詳細Dialogから開始する。
- 現在名を入力済みのDialogを表示する。
- 空白、明らかな長さ超過、区切り文字をClient側で拒否する。
- Server検証を正とし、Client検証だけで安全性を判断しない。
- 実行中は重複送信を防ぎ、取消で既に送信したRequestを成功扱いしない。

### 6.4 移動先Picker

- 個人Rootから開始し、`ACTIVE` Folderだけを候補として表示する。
- File移動では任意の候補Folderを選択できる。
- Folder移動では対象Folderを開けないようにし、Rootから対象へ入れないことで子孫選択もUI上抑止する。
- 現在の親は確定不可または副作用のない操作として扱う。
- ServerはUI制御と独立して循環と所有者境界を再検証する。
- 移動対象名と選択した移動先を確認してから送信する。

### 6.5 Error表示

| Error | Android表示・操作 |
| --- | --- |
| `FILE_NAME_CONFLICT` | 同名項目があるため別名または別Folderを選ぶ |
| `FILE_MOVE_CYCLE` | 対象Folder配下へは移動できないと表示しPickerへ戻る |
| `FILE_OPERATION_NOT_ALLOWED` | 対象が変更できないため一覧を再取得する |
| `FILE_NOT_FOUND` | 対象または移動先が変化したため一覧を再取得する |
| `STORAGE_UNAVAILABLE` | HDD状態を表示し、復旧後の再試行を案内する |
| `RECOVERY_REQUIRED` | 操作結果確認中として再取得を案内し、成功表示しない |
| 認証・Device Error | 既存の再Login・再登録Flowへ遷移する |

## 7. セキュリティ設計

- `fileId`と`parentId`の両方をAccess TokenのUser IDで検索し、他Userの存在を返さない。
- Admin Roleにも他User個人領域への暗黙アクセスを与えない。
- Client入力の名前を物理パスとして扱わず、`FileName`と`RelativeStoragePath.Append`で構築する。
- Storage Root、Mount、UUID、Storage ID、Filesystem、書き込み可否を毎操作で検証する。
- `..`、絶対Path、代替区切り文字、NUL、制御文字、Symlinkを拒否する。
- target存在時はFile・Directoryを上書きしない。
- Folder循環と最大深度超過をHDD操作前に拒否する。
- Logと監査へPassword、Token、Key、ファイル内容、物理絶対パスを記録しない。
- 同一項目と関係Folderの並行操作をadvisory lockで直列化する。

## 8. パフォーマンス設計

- File rename・moveはHDD上で内容をコピーせず、同一Filesystem内のDirectory Entry renameを使用する。
- File内容をServerまたはAndroid Memoryへ読み込まない。
- Folder配下DB更新はprefix queryで対象を限定し、1 Transactionで更新する。
- 既存の`relative_path`最大2048文字と最大階層深度64を変更しない。
- 配下項目数が多いFolderでもN+1 DB queryを行わず、子孫を一括取得して1回のSaveChangesで確定する。
- Androidの移動先Pickerは既存Paging APIを利用し、全Folderを一括取得しない。

## 9. テスト戦略

### 9.1 Domain・Application単体Test

- File・FolderのRenameとMove成功
- Root、`TRASHED`、他User、非Folder移動先の拒否
- `FileName`境界値と正規化
- 同名競合
- 自分自身・子孫への循環移動拒否
- 最大深度64境界
- Folder配下RelativePathのprefix置換
- File ID、所有者、内容属性、`fileVersion`維持
- 同一名前・同一親への副作用のない再実行
- Error Code変換と監査内容

### 9.2 Server結合Test

- `PATCH /api/v1/files/{fileId}`の名前変更・移動契約
- 両方指定、両方未指定、不正値の`400`
- 認証なし、失効Device、他User IDOR
- PostgreSQL一意制約による競合
- File・FolderのHDD renameとDB更新
- 配下を持つFolderの名前変更・移動
- Range Download内容チェックサム維持
- HDD未Mount、Storage ID不一致、読み取り専用、Symlink、Path Traversal
- 並行Rename、Move、Trash、Restore時の直列化
- `PENDING`各source/target存在組合せのRecovery
- atomic rename後・DB確定前のProcess停止Recovery
- `RECOVERY_REQUIRED`対象の一覧・詳細・Download隔離
- 既存Folder作成、Upload、Download、Trash、Restore回帰

### 9.3 Android単体・UI・契約Test

- Rename・Move DTOとOpenAPI Fixture一致
- Repository成功、Error、401 Refresh、通信結果不明
- ViewModelのRename、Picker遷移、Move、再取得
- 対象Folderおよび子孫をPickerで選択できないこと
- Rename Dialog、Move Picker、確認、Loading、Error表示
- File・Folder両方の操作入口
- Trash画面にはRename・Move操作を表示しないこと

### 9.4 実機E2E

- File名変更、Folder名変更
- File移動、配下を持つFolder移動
- 変更前後のFile ID、`fileVersion`、SHA-256一致
- 同名競合、循環移動、HDD利用不可の拒否
- LANとZeroTierの両方で操作
- 名前変更・移動を含む主要シナリオ10回連続成功
- Raspberry Pi再起動後の対象表示・Download

### 9.5 検証Command

```bash
./scripts/ci/verify-config.sh
./scripts/ci/verify-server.sh
./scripts/ci/verify-security.sh
./scripts/ci/verify-android.sh
```

Android実機では既存手順に従い、`connectedDebugAndroidTest --max-workers=1`またはRelease APKによる同等確認を実行する。

## 10. 依存ライブラリ・Migration

- 新しいNuGet Package、Gradle Plugin、Android Libraryを追加しない。
- `FileOperationType`は既存の文字列列へ追加値を保存するため、Rename・Move専用Migrationは不要とする。
- 既存Databaseを破壊、再作成、上書きしない。
- 実装中にDB制約または列追加が必要と判明した場合は、正式設計と本書を先に更新し、前方互換Migrationとして追加する。

## 11. 主な変更対象

```text
contracts/openapi/kurastorage-api.yaml
server/src/KuraStorage.Domain/Files/
  FileEntry.cs
  FileEnums.cs
server/src/KuraStorage.Application/Abstractions/FileAbstractions.cs
server/src/KuraStorage.Application/Files/
  FileContracts.cs
  FileService.cs
  FileOperationRecoveryService.cs
server/src/KuraStorage.Infrastructure/Persistence/FileRepository.cs
server/src/KuraStorage.Infrastructure/Storage/FileStore.cs
server/src/KuraStorage.Api/Program.cs
server/tests/
apps/android/core-model/src/
apps/android/core-network/src/
apps/android/core-data/src/
apps/android/feature-files/src/
docs/product-requirements.md
docs/functional-design.md
docs/architecture-design.md
docs/repository-structure.md
docs/development-guidelines.md
.steering/20260817-file-rename-move/
```

新しいServer ProjectまたはAndroid Moduleは追加しない。

## 12. 実装順序

### PR1: Server・契約・正式文書

1. 正式文書とOpenAPIへ確定契約を反映する。
2. DomainへRename・Move不変条件とOperation Typeを追加する。
3. Repositoryへadvisory lock、相対Path Folder検索、監査追加を実装する。
4. FileServiceへRename・MoveとFolder配下更新を実装する。
5. Recoveryをsource・target・DB状態の組合せへ対応させる。
6. API EndpointとError変換を実装する。
7. Domain・Application・Integration・Security・契約Testを完成させる。
8. 必須Server系検証、Commit、Push、Pull Request、完了記録まで行って停止する。

### PR2: Android・実機E2E

1. PR1のMergeを確認する。
2. Android DTO、API、Repository、Error分類を追加する。
3. ViewModelへRenameとMove Picker状態を追加する。
4. Compose UIへRename Dialog、Move Picker、確認、Error表示を追加する。
5. Android単体・契約・Compose・Instrumented Testを完成させる。
6. Raspberry PiへServer Artifactを配置し、Release APKを生成する。
7. LAN・ZeroTier・HDD異常を含む実機E2Eを完了する。
8. 必須検証、文書最終整合、Commit、Push、Pull Request、完了記録まで行って停止する。

## 13. 将来拡張

- 共有機能追加時は所有User検索をAuthorizationServiceへ置き換え、`EDITOR`以上だけにRename・Moveを許可する。
- 派生データ追加後もFile IDと`fileVersion`が不変なので、名前変更・移動だけではキャッシュを再生成しない。
- 検索・最近使用・タグ追加後はFile ID参照を維持し、相対パス変更を各索引へ反映する。
- 完全削除、コピー、複数選択、Drag & Drop、atomic replaceはそれぞれ別Steeringで契約と復旧方式を定義する。
