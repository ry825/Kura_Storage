# Androidバックアップ・メディア信頼性改善 要求内容

## 概要

Androidアプリのファイル削除、再インストール後の自動バックアップ、写真Viewer、サムネイル生成エラー、動画再生で確認された問題を修正する。利用者が重複なく安全にバックアップし、メディアを継続して閲覧でき、繰り返し表示される不要なエラー通知に妨げられない状態を実現する。

今回の実装・検証・文書更新は、最後に作成する1本のPull Requestへまとめる。テストで新規作成したUser、File、Folder、Backup Receipt、Media Job、派生データ、端末一時データは、今回作成したことをexact IDで追跡し、最終検証後に削除する。既存データは絶対に削除しない。

## 背景

- 複数のファイルを選択して削除できず、整理に手間がかかる。
- アプリを削除・再インストールして同一バックアップ元を設定すると、既に保存済みの同一内容まで再アップロードされ、重複Fileが作られる。
- 写真を拡大表示したまま前後の写真へ移動できない。
- サムネイル生成失敗をdismissしても、同じ失敗が画面を開くたびに再表示される。失敗は安全に再試行したい一方、恒久的な失敗で無限再試行してはならない。
- 一部端末（例: OPPO）でMP4を開くと、端末のCodec非対応として再生できない。Pixel 10で再生可能なMP4でも、端末差に応じた利用可能な再生経路または明確な代替導線が必要である。

## 実装対象の機能

### 1. 複数選択による削除

- File一覧の選択モードから複数の削除可能項目をまとめてゴミ箱へ移動できるようにする。
- 実行前に対象件数と不可逆でないゴミ箱移動であることを確認し、各項目の認可・状態を既存のTrash契約に従って検証する。
- 一部失敗時は成功・失敗を区別して表示し、未選択・未処理項目を誤って削除済みにしない。

### 2. 再インストール後の自動バックアップ重複防止

- 同一認証User、同一保存先Folder、同一相対Pathの候補について内容同一性を安全に検証できる場合のみ、既存バックアップとの対応を再確立する。
- 同一内容なら本文を再アップロードせず、新しい端末・`localDocumentKey`のReceiptを確定する。
- 内容変更、候補複数、checksum不一致、状態不正、権限不足は既存Fileを誤更新せず、既存の安全なBackup契約に従う。

### 3. 拡大中の写真Viewer navigation

- 写真のPinch zoom・Pan状態中でも、明確な前後操作により同じ閲覧Context内の前後の写真へ移動できるようにする。
- 移動時は前の写真の拡大・Pan・非同期読込結果を次の写真へ誤適用せず、表示対象と画質・アクセス権を一貫させる。
- 全画面写真Viewerの下部に、通常の写真Previewと同じ小型IconによるFavorite、Tag、Original download、全画面解除のactionを表示する。写真を隠さず、各actionの認可・処理中・失敗状態は通常Previewと一致させる。

### 3.1 フォルダ一覧の先頭移動

- Folder内のFile一覧を下方へscrollした時だけ、一覧先頭へ戻る小さく明瞭なIcon buttonを表示する。
- 見た目は控えめにする一方、touch target、TalkBack名、色以外の識別を確保し、一覧内容・既存のscroll anchor・他の主要操作を遮らない。

### 4. サムネイル失敗の通知抑制と有界再試行

- 同一File Version・thumbnail種別の失敗通知を利用者がdismissした後、同じSession scope内では自動再表示しない。
- ServerのMedia Jobがretry可能な失敗の場合だけ、上限回数と待機時間を持って再試行する。再試行の重複実行を防ぐ。
- 上限到達または恒久失敗では自動生成を停止し、一覧はPlaceholderを維持する。利用者が明示的に再試行した場合だけ、既存の冪等Retry契約を通じて再開できる。
- 失敗時にOriginalをサムネイルの代替として自動取得しない。

### 5. 端末Codec差を考慮したMP4再生

- `video/mp4`でも端末のMediaCodecが内部Codecをサポートしない場合を検出・分類し、Crashや無限再試行を避ける。
- Serverが提供する動画はOriginal固定という正式仕様を維持する。クライアントで互換性のないCodecを変換・偽装しない。
- 再生不能な端末では理由が分かる表示と、既存の安全なDownloadまたは外部対応アプリへ渡す導線を提供する。再生可能な端末のRange再生、認証、通信量確認を回帰させない。

## 受け入れ条件

### 複数選択削除

- [ ] File一覧で複数の削除可能File/Folderを選択し、1回の確認後に各項目をゴミ箱へ移動できる。
- [ ] 権限不足、既にTrash、競合、通信失敗は項目単位で結果を表示し、成功項目以外を削除済みとして表示しない。
- [ ] 既存の単一削除、Restore、Permanent Delete、共有・認可境界に回帰がない。

### 自動バックアップ

- [ ] アプリ再インストール後、同じUser・保存先Folder・バックアップ元を設定した際、同一内容で候補が一意のFileは本文を再アップロードせず、重複Fileを作成しない。
- [ ] 再関連付け後は新しいDevice/`localDocumentKey`のReceiptにより次回Compareが`ALREADY_UPLOADED`になる。
- [ ] 変更済みまたは不確実な候補では、既存Fileを誤って関連付け・更新しない。

### 写真Viewer

- [ ] 拡大・Pan中でも前後の写真へ遷移でき、遷移先は初期表示状態から正しい写真・品質を表示する。
- [ ] 高速な前後遷移、読込失敗、権限・Session変更でも、古い非同期結果が現在の写真を上書きしない。
- [ ] 全画面写真Viewerで、Favorite、Tag、Original download、全画面解除を通常Previewと同じ小型Iconで実行できる。
- [ ] 全画面のactionは、操作対象写真のFavorite/Tag状態、DownloadのStreaming・取消・失敗、system Backによる全画面解除と矛盾しない。

### フォルダ一覧

- [ ] Folder一覧を一定量scrollした後に、視覚的には小さいがaccessibilityを満たす先頭移動buttonを表示し、1操作で先頭へ戻れる。
- [ ] 再Composition、refresh、Folder移動、一覧復帰時に既存のscroll anchor復元と競合せず、先頭移動buttonが不要な状態で常駐しない。

### サムネイル生成

- [ ] 同一失敗をdismissした後、同じSession scopeで一覧を再度開いても同じ自動popupは再表示されない。
- [ ] retry可能な失敗は回数・backoffの上限内だけ再試行し、同一Jobへの重複Retryを作らない。
- [ ] 上限到達または恒久失敗では自動生成を停止し、明示Retry以外で再開しない。

### MP4再生

- [ ] 端末Codec非対応を再生取得・認証・Range失敗と区別して表示し、無限再試行やCrashを起こさない。
- [ ] 互換端末で既存のMP4 Range再生が成功し、非互換端末では安全な代替導線を使える。

### 品質・後始末

- [ ] 今回の変更に対応するServer/Android unit・integration・instrumented/E2E検証が成功する。
- [ ] テスト開始前の既存データをread-onlyで記録し、run IDとexact IDのmanifestで追跡した今回のfixtureだけを依存関係の逆順で削除する。
- [ ] 清掃後に各fixtureの不存在とbaselineとの差分なしを確認し、清掃失敗はテスト成功と別に未完了として扱う。
- [ ] 最後に本作業の変更のみを含む単一Pull Requestを作成する。

## 成功指標

- 再インストール後に同一内容のFile本文を重複転送する件数: 0件。
- dismiss済みの同一thumbnail失敗通知の同一Session内再表示: 0件。
- 自動thumbnail retryの上限超過実行: 0件。
- 追跡外または既存のテストデータを削除する件数: 0件。

## スコープ外

- Server上にすでに存在する重複Fileの一括検出・統合・削除。
- アプリデータ、Room Database、SAF権限、認証情報をAndroid Auto Backup/端末移行で復元すること。
- 動画のServer側トランスコード、動画Low/Medium派生、端末Codecのソフトウェアデコード実装。
- Folderを含む複数選択のPermanent Delete。
- dismiss状態をLogout、Session失効、File Version変更をまたいで永続化すること。

## 参照ドキュメント

- `docs/product-requirements.md` - Trash、自動Backup、永続Media Job、動画Original配信の要求
- `docs/functional-design.md` - Android File操作、Backup、Media閲覧・Media Jobの契約
- `docs/architecture-design.md` - Backup Receipt、Media Job、Android state管理
- `docs/repository-structure.md` - Server/Androidの変更配置
- `docs/development-guidelines.md` - 認可、Backup一意性、Media、fixture清掃の必須規則
- `.steering/20260908-backup-reinstall-deduplication/requirements.md` - 再インストール後のBackup照合要求
- `.steering/20260905-android-viewer-navigation-ux-fixes/requirements.md` - Android Viewer/Navigationの既存改善要求
