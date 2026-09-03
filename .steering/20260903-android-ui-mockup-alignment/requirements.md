# Android UIモックアップ整合 要求内容

## 概要

`docs/ui/android/mockups/`の参考UI 36枚と`docs/functional-design.md`の正式画面仕様に従い、現在のAndroidアプリのJetpack Compose UIを一貫したKuraStorageのデザインへ作り直す。既存機能と画面状態を維持し、正式画面に必要な実装が不足する場合は、`docs/`を正として必要最小限のServer契約、Application処理、永続状態、Android data/UIを追加する。

## 背景

現在のAndroidアプリには、接続、認証、ファイル管理、検索、共有、メディア、テキスト、自動バックアップ等の画面は実装されているが、多くがMaterial 3の基本部品を直接配置した状態で、参考UIの視覚的階層、配色、余白、カード、アイコン、主要操作の配置がアプリ全体で統一されていない。

`docs/functional-design.md`には正式画面と参考UIの対応が定義されている。ただし、モックアップは機能仕様そのものではなく、例えばLegacyの`VPN`表記は正式仕様のZeroTier表記と別アプリ案内に置き換える必要がある。そのため、モックアップを画像として貼り付けるのではなく、正式仕様を優先してCompose部品として再構築する。

## 対象環境と前提

- 対象は`apps/android/`のAndroid 10（API 29）以上向けネイティブアプリとする。
- UIはJetpack Composeで実装し、モックアップ画像全体を背景や操作面として使用しない。
- 機能、表示項目、権限、エラー、状態遷移は`docs/`の正式文書を正とし、参考UIと矛盾する場合は正式文書を優先する。
- 既存のViewModel、Repository、Navigation callback、API契約を優先して再利用する。正式画面のデータ取得または操作契約が存在しない場合だけ、正式文書を更新してServer/API/Databaseを必要最小限拡張する。
- 参考UIがない正式画面、確認Dialog、追加済みFeatureも、同じDesign systemと画面パターンで統一する。

## 実装対象の機能

### 1. KuraStorage Design system

- 参考UIから、深い藍を主色とする色、書体の階層、間隔、角丸、境界線、影、状態色、アイコン表現をDesign token化する。
- 共通のScaffold、Top app bar、Bottom navigation、Card、List row、Section header、Primary/secondary/destructive button、フォーム、Status badge、Empty/Loading/Error stateを`core-ui`で提供する。
- KuraStorageロゴと必要最小限の和風表現を再利用可能なAssetまたはCompose描画として用意する。
- ライトテーマを参考UIと合わせ、システムのDark theme設定時にも可読性と状態の識別を損なわない。

### 2. 起動・接続・認証UI

- 起動、接続確認中、ローカル直接接続、未接続、ZeroTier経路案内、ログイン、初回Device登録中、登録不可を`001`〜`008`の構成と視覚階層に合わせる。
- 接続状態ごとの理由と次に行える操作を、色だけに依存せずテキストとアイコンで表示する。
- KuraStorage内からZeroTierの接続・切断は行わず、別アプリでの確認案内と再確認操作を表示する。

### 3. ホーム・グローバルナビゲーションUI

- ホーム、自分のファイル、最近使用、共有、カテゴリ、検索、設定を`009`〜`015`を基準に再構築する。
- Bottom navigationは正式仕様に必要な主要導線を最大5項目で一貫表示し、選択状態、Back stack、画面タイトルの整合を保つ。
- ホームでは接続・バックアップの要約、主要導線、カテゴリ、最近のファイルを情報の優先度に沿って表示する。
- モックアップがないお気に入り、Tag管理、操作履歴も、同じ一覧・カード・状態表現を使用する。

### 4. ファイル管理・共有・転送UI

- ファイル一覧、ファイル詳細、フォルダ詳細、非対応ファイル、共有設定、共有相手・権限選択、サーバー保存先選択、転送状況、ゴミ箱、`MISSING`項目を`010`および`021`〜`029`を基準に再構築する。
- ファイル種別アイコン、サムネイル、名前、更新日時、サイズ、共有、権限、`MISSING`の表現を一貫させる。
- Rename、Move、Upload、Download、Share、Trash、Restore、Purge等の既存操作と、実行中、成功、通信結果不明、競合、権限不足、ストレージ利用不可の状態表示を維持する。
- 完全削除、共有解除等の危険操作は、通常操作と視覚的に区別し、取り消し可能な確認を経て実行する。

### 5. ビューアー・プレイヤー・テキストUI

- 写真、動画、音声、PDF、テキストを`016`〜`020`を基準に再構築し、ヘッダー、コンテンツ表示領域、品質・再生・編集操作の階層を揃える。
- 横画面、文字拡大、システムバー、ディスプレイCutoutがあっても、主要操作が隠れずスクロールまたは適切な再配置で到達可能とする。
- 画質選択、推定転送量、変換待ち・進捗・失敗、再接続、非対応Codec、PDF上限、テキスト未保存・競合を正式仕様どおり表示する。

### 6. 設定・自動バックアップUI

- バックアップ状態、Rule一覧、Rule追加・編集、許可Wi-Fi一覧、Wi-Fi登録・編集、画質・通信量、キャッシュ状態を`030`〜`036`を基準に再構築する。
- 実行中、保留、成功、失敗、一時停止、権限待ち、Network待ち、Battery・充電待ち、HDD待ちを、件数、理由、次の操作とともに表示する。
- Rule編集、Wi-Fi編集、画質選択は、現在値、説明、入力Error、保存中、保存成功・失敗を明確に示す。
- バックアップが一方向であること、Mobile通信で自動実行しないこと、強制停止後はアプリを再度開くまで予約実行できないことを省略しない。

### 7. Responsive・アクセシビリティ・状態別表示

- 360dp幅を含む対象範囲、縦・横画面、フォント拡大でレイアウトが欠落・重なり・切れを起こさず、主要操作へ到達できる。
- タップ領域、Content description、選択・処理中・失敗のSemantics、見出し順序、TalkBackでの読み上げを整備する。
- 色だけで状態を伝えず、テキスト、アイコン、形状のいずれかを併用し、コントラストと文字の可読性を満たす。
- Loading、Empty、Content、Recoverable error、Blocking error、Offline、Permission denied、処理中を、対象画面の特性に応じて表示する。

### 8. UI検証基盤と回帰テスト

- Design tokenと共通部品に対するCompose testを追加する。
- 各画面の主要状態、主要操作、Navigation、Error、SemanticsをCompose instrumented testで検証する。
- 代表画面には再現可能なスクリーンショット取得手順または同等の視覚回帰検証を設け、参考UIと正式仕様に対する差分をPull Requestごとに確認できるようにする。
- Android 13物理端末で主要ユーザーフローと表示を確認し、スクリーンショットと実測結果を`docs/testing/`に記録する。

### 9. 正式画面に不足する実装

- 36画面ごとに、現行Composable、Navigation、UiState、Repository、API、Server処理の有無を監査する。
- 欠落がある場合は、モックアップだけから機能を推測せず、`docs/product-requirements.md`と`docs/functional-design.md`に定義された表示・操作のみを追加する。
- 現状で不足が確認できるキャッシュ状態画面について、管理者専用の使用量、10GB上限、6GB清掃目標、最終清掃、生成中・失敗件数の取得と「今すぐ清掃」を実装する。
- 手動清掃はAPI Request中に長時間実行せず、永続化した要求を既存Media Cleanup Workerが取得する。API停止、Worker再起動、重複要求後も一貫した状態を返す。
- 正式仕様にない「失敗項目を一括再試行」等のモックアップ固有操作は追加せず、意図的差分として記録する。

## 受け入れ条件

### 1. Design systemと共通構造

- [ ] 色、Typography、Shape、Spacing、Elevation、Icon、状態表現が再利用可能な共通実装に集約され、各Featureが任意の値を重複定義していない。
- [ ] 参考UIの明るい背景、深い藍の主色、細い境界線、角丸カード、状態色、操作階層がアプリ全体で一貫している。
- [ ] 画像全体の背景利用に依存せず、テキスト、リスト、フォーム、状態、操作がCompose semanticsを持つ実コンポーネントとして実装されている。
- [ ] ロゴや装飾Assetを使用する場合、必要な権利と出典を確認でき、木々の下部装飾など正式仕様が除外する表現を含まない。

### 2. 対象画面と正式仕様の整合

- [ ] `001`〜`036`のすべてについて、対応する正式画面の実装箇所、対象状態、正式仕様との意図的な差分が追跡できる。
- [ ] 起動・接続・認証、ホーム・ナビゲーション、ファイル・共有・転送、ビューアー・エディター、設定・バックアップの各画面群が参考UIの情報階層と正式仕様の操作を両立している。
- [ ] `VPN`のLegacy表記やKuraStorage内でのVPN接続操作を実装せず、ZeroTierの別アプリ案内と到達性再確認を正しく表示する。
- [ ] 参考UIにないお気に入り、Tag管理、操作履歴、各確認Dialogにも同じDesign systemが適用されている。

### 3. 既存機能と状態の保持

- [ ] 現在提供されている画面、主要操作、Navigation、ViewModel状態、Repository呼び出しがUI更新後も利用できる。
- [ ] Loading、Empty、Content、Error、Offline、権限不足、処理中、競合、通信結果不明の必要状態が、正式仕様どおり判別可能に表示される。
- [ ] 危険操作、認証、権限、通信経路、TLS、ストレージの既存のSecurity制約がUI変更で弱められていない。
- [ ] 新規Server API、Application処理、Database migrationは正式画面の欠落契約を満たす最小範囲に限定され、同一操作を重複実装していない。
- [ ] キャッシュ状態の取得と手動清掃要求はAdminだけが利用でき、Member、未認証、無効Device/Sessionは拒否される。
- [ ] 手動清掃要求が重複しても同時清掃にならず、Worker停止・再起動後も未完了要求と実行結果を復元できる。

### 4. Responsiveとアクセシビリティ

- [ ] 360dp幅の縦画面、横画面、通常文字、200%文字拡大で、情報の欠落、操作不能な重なり、横方向の意図しない切れがない。
- [ ] 主要操作のタップ領域が48dp相当以上で、アイコンだけの操作に理解可能なContent descriptionがある。
- [ ] 色だけで状態を伝えず、テキストと非色視覚情報を併用し、文字と主要UI要素が必要なコントラストを持つ。
- [ ] TalkBackで見出し、状態、一覧項目、フォーム、主要操作、エラーの意味と順序を理解できる。

### 5. 検証と記録

- [ ] `./scripts/ci/verify-android.sh`が成功し、`minSdk 29`のBuild、Lint、Android単体Testが通過する。
- [ ] 変更対象のCompose instrumented testが成功し、主要操作、状態、Navigation、Semanticsを検証している。
- [ ] 各Pull Requestで対象画面の参考UI対応、意図的差分、検証済み表示条件がチェックリストまたは`docs/testing/`に記録されている。
- [ ] Android 13物理端末で、起動〜認証、主要ナビゲーション、ファイル操作、各Viewer、共有、検索、設定、自動バックアップの主要フローと表示を確認し、スクリーンショットを含む結果を記録している。
- [ ] 変更対象外を含む既存Android instrumented testを実行し、UI整合による回帰がない。

## 成功指標

- 参考UI 36枚すべてが、正式画面、実装Pull Request、自動テスト、手動検証のいずれから追跡可能である。
- 主要画面群すべてにDesign systemと共通Navigationが適用され、同じ意味のUIに視覚的な不一致が残っていない。
- 対象画面の必須状態と主要操作がCompose testまたはAndroid実機検証で確認される。
- 360dp幅、200%文字拡大、TalkBackの確認で主要操作が到達可能で、重大な可読性・操作性の問題が0件である。

## スコープ外

以下はこの作業では実装しない。

- WebクライアントのUI実装。
- 正式画面の不足契約と無関係なServer API、Database schema、ファイル保存方式の機能変更。
- 正式文書にない新規機能、画面、ナビゲーションの追加。
- 参考UI内のサンプルファイル、件数、日時、ユーザー名等を実データとして固定すること。
- KuraStorageアプリ内からのZeroTierネットワーク接続・切断・認可操作。
- 木々の下部装飾や、画面全体を覆う過剰な和風装飾。
- 参考UIへのピクセル単位の完全一致を目的とすること。端末差、文字サイズ、システムUIに追従できるResponsiveな実装を優先する。

## 参照ドキュメント

- `docs/product-requirements.md` - 8章「UI・UX要件」および各Android機能の受け入れ条件
- `docs/functional-design.md` - 10章「画面遷移図」、11章「UI設計」
- `docs/architecture-design.md` - Androidクライアントの境界と依存方向
- `docs/repository-structure.md` - 8章「Android構造」および`core-ui`・各Featureの配置規則
- `docs/development-guidelines.md` - Android実装、テスト、Pull Request、Definition of Done
- `docs/ui/android/mockups/connection-auth/001-splash.png`〜`008-device-registration-error.png`
- `docs/ui/android/mockups/home-navigation/009-home.png`〜`015-settings.png`
- `docs/ui/android/mockups/files-media/016-photo-viewer.png`〜`029-missing-files.png`
- `docs/ui/android/mockups/backup-settings/030-backup-status.png`〜`036-cache-management.png`
- `.steering/20260722-kurastorage-mvp/` - Android基盤とMVP画面の履歴
- `.steering/20260823-file-folder-sharing-permissions/` - 共有UIの履歴
- `.steering/20260824-search-recent-files/` - 検索・最近使用UIの履歴
- `.steering/20260828-favorites-tags/` - お気に入り・Tag UIの履歴
- `.steering/20260829-android-media-viewers-players/` - メディアUIの履歴
- `.steering/20260830-text-file-version-history/` - テキストUIの履歴
- `.steering/20260830-user-operation-history/` - 操作履歴UIの履歴
- `.steering/20260902-android-auto-backup/` - 自動バックアップUIの履歴
