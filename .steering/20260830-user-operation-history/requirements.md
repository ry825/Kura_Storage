# ユーザー向け操作履歴 要求仕様書

## 1. 概要

アップロード、移動、テキスト編集、共有、削除について、利用者が自分に関係する成功操作を確認できる履歴を追加し、管理者には用途を限定した検索機能を提供する。既存のセキュリティ監査ログとは別の永続モデル・表示契約として定義する。

## 2. 背景

既存`AuditLog`は認証、端末、CLI、共有権限変更、完全削除、復旧などのセキュリティ・運用証跡を追記専用で保存する。一方、利用者が「誰が、いつ、どの項目を操作したか」を理解するために必要な表示名、操作種別、対象snapshot、移動元・移動先等の安定した契約は持たない。監査内部情報を一般APIへ公開せず、利用目的に合った履歴を設ける必要がある。

## 3. 用途と境界

- ユーザー向け操作履歴は、製品UIで成功操作を説明するための`UserActivity`とする。
- セキュリティ監査ログは、認証・認可・管理・復旧の調査証跡であり、既存`AuditLog`を維持する。
- 同じ成功操作が両方の目的に該当する場合は、Application use caseが同一DB transaction内で両レコードを作る。片方からもう片方を非同期変換しない。
- `UserActivity`は認可判断、FileEntryの存在判定、復旧journalの代替に使用しない。
- 一般利用者へDevice ID、OS User、Request ID、物理Path、内部result code、失敗したセキュリティ操作を公開しない。

## 4. 実装対象

### 4.1 記録対象

- `UPLOAD`: File Uploadが正式公開された時点。
- `MOVE`: File／Folderの親が変わった時点。Renameだけは初回範囲外とする。
- `EDIT`: テキスト保存または過去版復元により内容版が増加した時点。
- `SHARE`: Share作成、権限変更、解除が成功した時点。
- `DELETE`: ゴミ箱移動および完全削除が成功した時点。復元は初回範囲外とする。
- 失敗要求、Validation拒否、認証・認可拒否、冪等再送で状態が変わらなかった要求は利用者向け履歴へ追加しない。

### 4.2 履歴データ

- Activity ID、操作種別、発生日時、Actor User IDと表示用snapshot、Actor Device表示名snapshot（利用者表示可能な場合）を保持する。
- 対象種別、File ID（Purge前のみ参照可能）、対象名snapshot、Owner ID、操作後に存在する場合の親IDを保持する。
- 操作固有metadataは型ごとに許可列を限定し、Move元・先Folder名snapshot、Share対象User表示名snapshot・権限、Edit版番号、Delete種別等だけを保持する。
- File本文、過去版本文、Token、Password、物理Path、自由形式の秘密情報は保持しない。
- Rename、Move、Purge後も当時の説明が変わらないよう表示用snapshotを用い、現行FileEntryへのjoinだけに依存しない。

### 4.3 利用者向け一覧

- 認証済み利用者は、自分がActorであるActivity、または現在閲覧可能な対象に関するActivityを新しい順でPage表示できる。
- TargetがPurge済みの場合は、Actorまたは当時のOwnerだけが必要最小限の削除snapshotを表示できる。
- 共有解除、権限変更、Moveによる継承変更は次の一覧要求で反映し、権限を失った利用者へ過去Activityを表示し続けない。
- `ADMIN` Roleだけを理由に一般利用者APIで全User履歴を閲覧できない。
- 一般利用者APIは自由なUser検索、Device検索、Request ID検索を提供しない。

### 4.4 管理者検索

- 管理者検索はRaspberry PiローカルのAdmin CLIとして提供し、通常のネットワークAPIへ全履歴検索を公開しない。
- Actor User、Owner User、操作種別、UTC期間、対象File ID、結果上限／Page tokenで組み合わせ検索できる。
- 検索実行自体を既存セキュリティ監査ログへ記録するが、検索結果のFile名やUser入力を通常Logへ出さない。
- CLIは件数制限、決定的な並び順、端末出力とJSON出力を提供し、機微な内部列を既定表示しない。

### 4.5 保持・削除

- `UserActivity`は追記専用とし、一般APIから更新・削除できない。
- 初回実装の保持期間は無期限とする。将来のretention導入は正式仕様、管理者操作、監査、Backup要件を別途定義する。
- User無効化やFile完全削除でActivityをcascade削除せず、nullable参照とsnapshotで履歴を維持する。

## 5. 受け入れ条件

- [ ] Upload、Move、Edit、Share作成・変更・解除、Trash・Purgeの成功時にActivityを1件だけ記録する。
- [ ] 失敗要求と副作用のない冪等再送ではActivityを記録しない。
- [ ] Activityと対象操作が同一transaction／回復境界で整合する。
- [ ] 一般利用者一覧はActor本人または現在閲覧可能な対象だけを返す。
- [ ] 共有解除、Move、Trash、Purge後の可視性が定義どおり更新される。
- [ ] Purge後もActor／当時Owner向けの削除説明を必要最小限のsnapshotで表示できる。
- [ ] Admin CLIでActor、Owner、種別、期間、File IDを組み合わせ検索できる。
- [ ] 一般APIから監査ログ、管理者検索、内部識別子・失敗情報へアクセスできない。
- [ ] 既存のセキュリティ監査ログの追記専用性・完全削除一意制約・保存内容を後退させない。
- [ ] Androidで履歴一覧、Paging、Empty、Error、Refresh、操作詳細を表示できる。
- [ ] 30万FileEntryと100万Activityの再現可能データで、利用者一覧と管理者の限定検索が通常2秒以内である。
- [ ] LAN／ZeroTier、Android実機、Admin CLI、PostgreSQL実体で主要フローを確認する。

## 6. 成功指標

- 権限のない利用者へActivityを公開する事象0件。
- 完了した対象操作にActivityがなく、または同一操作で重複する事象0件。
- 監査ログの内部情報を一般利用者契約へ露出する事象0件。
- 新規認可・検索・transaction境界はLine Coverage 95%以上、Domain／Application全体80%以上を維持する。

## 7. スコープ外

- 失敗したログインや認可拒否等のセキュリティイベント表示。
- 通常HTTP APIによる全User横断の管理者検索。
- Rename、Download、閲覧、Favorite、Tag、自動Backup、復元を利用者履歴へ追加すること。
- 履歴の編集・個別削除、通知、集計Dashboard、CSV export、Web UI。

## 8. 参照ドキュメント

- `docs/product-requirements.md` 7.12.4
- `docs/functional-design.md` 5.2、ファイル操作履歴、各対象操作
- `docs/architecture-design.md` 18.1、18.2
- `docs/repository-structure.md`
- `docs/development-guidelines.md`
