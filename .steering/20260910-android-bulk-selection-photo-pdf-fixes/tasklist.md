# Android 一括選択・写真全画面・PDF表示修正 タスクリスト

## 🚨 タスク完全完了の原則

このファイルの全タスクは、関連実装・テスト・清掃・レビューを完了させたうえで、最後に作成する**1本のPull Request**へまとめる。中間Pull Requestは作成しない。

- [ ] を [x] に変更できるのは、その項目の完了条件を満たした後だけとする。
- 現在存在するユーザーの未Commit変更（`.gitignore`、`ThumbnailRetryCoordinator`、先行Steering文書など）は本作業の対象外として内容とGit差分を保存し、混在・上書き・削除しない。
- 既存データ、manifestにないデータ、曖昧にしか特定できないデータを削除しない。
- 技術的理由で不要になった項目だけは、理由と代替実装をこのファイルに明記して完了扱いにできる。「時間がかかる」は理由にしない。

## Pull Request構成

| PR | 範囲 | 作成タイミング |
| --- | --- | --- |
| PR 1 | 一括選択actionのlayout、写真全画面controls/Swipe、PDF表示、全テスト、fixture清掃、文書・完了記録 | 全フェーズ完了後のみ |

> 実装開始時の確認（2026-09-10）: 先行PR #71 のmainマージ後、`main`を基点に `fix/android-bulk-selection-photo-pdf-pr` を作成して実装した。既存データを使うサーバーfixtureは作成せず、端末上のCompose/PdfRenderer testだけを実行した。

## フェーズ0: 実装開始・安全な事前確認

- [x] PR 1の実装前状態を確定する。
  - [x] `.steering/20260910-android-bulk-selection-photo-pdf-fixes/requirements.md`、`design.md`、本ファイルと関連する正式文書の写真Viewer、PDF、Android UI/テスト節を再確認する。
  - [x] `git status`、`git diff --check`、現在Branch、先行Pull Request/CI状態を確認し、既存の未Commit変更を対象外として記録する。
  - [x] 先行変更が未Mergeの場合はその依存を解消してから、最新`main`を基点とするPR 1用短命Branchを準備する。既存変更を含むBranchで安全に分離できない場合は実装を開始せず報告する。
  - [x] `FileBrowserScreen`のselection/bulk-trash、`PhotoViewerScreen`/PhotoCanvas、PDF route/`PdfViewerViewModel`/`TemporaryPdfStore`/`PdfDocumentController`、対応Testの現行構造を確認する。

- [ ] 作業固有の検証資材を安全に追跡できるようにする。
  - [ ] run IDを発行し、workspace外のmanifestに今回作成する資材の種別、exact ID/path、作成時刻、作成手段、削除手段だけを記録する。Token、Password、SSID/BSSID、物理Path、個人情報、File本文・名前は記録しない。
  - [ ] fixture作成前に、接続先の既存User、File/Folder、Tag/Favorite/Share、Backup、Media job/派生データ、端末temporary fileについてread-only baseline（ID/件数/checksumが必要なもの）を取得する。
  - [ ] manifestのexact membershipを再読込できない対象、baselineに含まれる対象、親Folder、wildcard、全件削除を拒否するcleanup guardを用意する。

## フェーズ1: 最小再現と原因確定

- [ ] 報告された一括選択action崩れを最小画面状態で再現する。
  - [ ] 1件・複数件の選択、bulk operation中、選択解除を確認する。
  - [ ] 360dp、landscape、system inset、font scale 2.0で選択件数、Clear、Move to trash/削除actionの重なり、切断、不可視、touch targetを記録する。
  - [ ] 同じ選択action構造を持つ画面を検索し、修正適用先と共通化の要否を確定する。

- [ ] 写真全画面の視認性とSwipe遷移を最小fixtureで再現する。
  - [ ] 明暗の異なる写真でbottom controlsのcontrast、safe inset、48dp target、TalkBack semanticsを確認する。
  - [ ] 前・中間・末尾photoについて、等倍Swipe、zoom中pan、連続Swipe、loading/error、Back、回転/再構成を記録する。
  - [x] Swipe時に`fullscreen`が解除される箇所と、Photo ID/source変更時に全画面stateが再初期化される箇所を確定する。

- [ ] PDF表示失敗を境界ごとに切り分ける。
  - [ ] 正常な256MiB以下PDFで、File detail、HEAD metadata、通信量確認、content download、temporary file検証、`PdfRenderer.open`、最初のpage render、route disposeを順番に観測する。
  - [ ] Content-Type parameter、Content-Length、Accept-Ranges、認証/権限/404、途中切断、0 byte、signature不正、暗号化、storage不足の入力をMockWebServerまたは既存test doubleで再現する。
  - [ ] 実際の失敗原因、期待するtyped failure、修正対象を記録する。正式仕様またはAPI契約との矛盾を見つけた場合は、実装前に影響を明記してユーザー確認を求める。

## フェーズ2: 一括選択actionの修正

- [ ] 一括選択actionのresponsive layoutに対する失敗テストを追加する。
  - [ ] 狭幅、landscape、font scale 2.0、insetで全actionがsemantics treeと表示領域に存在するCompose/Instrumented testを追加する。
  - [ ] 選択件数、Clear、destructive action、進行状態が正しく表示・disableされるtestを追加する。

- [x] 選択actionの表示コンテナを修正する。
  - [x] `FlowRow`または幅に応じたRow/Columnによって、幅不足時にactionを安全に折り返す。
  - [x] actionごとに48dp以上のtouch target、目的が分かるcontent description、状態を色だけに依存しない表示を適用する。
  - [x] 既存のselection ID、confirmation dialog、bulk-trash use case、認可、error handlingを変更していないことを確認する。

- [ ] 選択actionの対象検証を完了する。
  - [x] `:feature-files:testDebugUnitTest`と対象Compose/Instrumented testを実行する。
  - [ ] 実機で1件・複数件の選択、Clear、Trash確認、進行中表示を確認する。

## フェーズ3: 写真全画面controlsとSwipeの修正

- [ ] 全画面写真controlsとSwipeの失敗テストを追加する。
  - [x] 全画面時のbottom controlsがsafe inset、背景surface、48dp target、content descriptionを持つCompose/Instrumented testを追加する。
  - [x] 等倍の左右Swipeが1 gestureにつき1回だけ前/次callbackを呼び、全画面stateを解除しないtestを追加する。
  - [ ] zoom中pan、先頭/末尾、loading/error、連続Swipe、旧image responseの破棄をUnit/Compose testで固定する。

- [ ] 全画面写真controlsの視認性を修正する。
  - [x] bottom controlsを写真内容と分離したcontrast surface/scrimへ配置し、safe drawing/navigation bar insetを避ける。
  - [ ] 狭幅と文字拡大でactionが切れないよう折返しまたはadaptive layoutを適用する。
  - [x] Backは全画面だけを閉じ、操作ボタンは全画面中も安定して操作できるようにする。

- [x] 全画面を維持するSwipe遷移を実装する。
  - [x] PhotoCanvasのgesture arbitrationを等倍・水平・閾値超過時だけの1回navigationに限定する。
  - [x] gesture発火後の重複navigationを抑止し、zoom pan、垂直gesture、edge、前後なしをno-opにする。
  - [x] Photo ID/sourceの更新で`fullscreen`を意図せずfalseへ戻さず、既存ViewModelの順序・request generation・prefetchを利用する。

- [ ] 写真Viewerの対象検証を完了する。
  - [x] `:feature-media:testDebugUnitTest`、対象Compose/Instrumented test、必要な`:app:testDebugUnitTest`を実行する。
  - [ ] 実機で暗い/明るい写真、先頭/中間/末尾、zoom、連続Swipe、Back、回転を確認し、全画面のまま前後写真へ遷移する証跡を記録する。

## フェーズ4: PDFアプリ内表示の修正

- [ ] 原因確定済みPDF失敗境界のテストを先に追加する。
  - [ ] metadata MIME正規化、size/Range検査、typed failure mapping、`Retry open`のstate遷移をJVM testで固定する。
  - [ ] 正常streaming、Content-Length不一致、途中切断、signature不正、空き容量、256MiB、Session合計512MiB、partial file cleanup、Session外File拒否を`TemporaryPdfStoreTest`で固定する。
  - [x] renderer open/render/retry/disposeでdescriptor、renderer、page、leaseがcloseされるtestを追加する。
  - [ ] 正常PDFが`Open PDF`からSAFへ進まずアプリ内Viewerを開き、失敗時にtyped reasonと`Retry open`が表示されるCompose/Instrumented testを追加する。

- [ ] PDFの失敗原因を最小変更で修正する。
  - [ ] metadata、download、temporary file、renderer、ViewModel/route lifecycleのうち、フェーズ1で確定した層だけを修正する。
  - [x] retry開始前に旧load/render job、旧document、old leaseを安全にclose/cancelし、古い結果がretry stateを上書きしないようにする。
  - [x] `PdfRenderer`には完全なscope内private fileだけを渡し、256MiB、512MiB、64KiB streaming、空き容量、signature、TTL、logout/Session cleanupの契約を維持する。
  - [x] 新規PDF library、外部viewer、恒久保存、認可のclient-side迂回を追加しない。

- [ ] PDF表示の対象検証を完了する。
  - [x] `:core-data:testDebugUnitTest`、`:feature-media:testDebugUnitTest`、対象PDF Instrumented testを実行する。
  - [ ] MockWebServerで正常PDFと各主要failureを実行し、`PDF unavailable`へ不必要に集約されないことを確認する。
  - [ ] 実機・実Serverで正常PDFの通信量確認→Open PDF→page表示→zoom/page移動→Back/再試行を確認する。

## フェーズ5: 統合品質確認と安全な清掃

- [x] 変更対象を高速に統合検証する。
  - [x] `./apps/android/gradlew -p apps/android :feature-files:testDebugUnitTest :feature-media:testDebugUnitTest :core-data:testDebugUnitTest`を実行する。
  - [x] `./apps/android/gradlew -p apps/android :feature-files:ktlintCheck :feature-files:detekt :feature-files:lintDebug :feature-media:ktlintCheck :feature-media:detekt :feature-media:lintDebug :core-data:ktlintCheck :core-data:detekt :core-data:lintDebug`を実行する。
  - [x] `./apps/android/gradlew -p apps/android :app:assembleDebug :feature-files:assembleDebugAndroidTest :feature-media:assembleDebugAndroidTest`を実行する。
  - [x] 利用可能なAndroid実機またはemulatorで`:feature-files:connectedDebugAndroidTest :feature-media:connectedDebugAndroidTest`を`--max-workers=1`で実行する。

- [x] 最終のrepository標準Android検証を完了する。
  - [x] `./scripts/ci/verify-android.sh`を実行し、build、JVM tests、coverage、ktlint、detekt、Android lint、APK/SBOM生成が成功することを確認する。
  - [x] `git diff --check`と対象差分のself-reviewを実行し、debug code、機密情報、無関係な変更、generated artifactが含まれないことを確認する。

- [x] 今回追加した検証資材だけを安全に清掃する（リモートfixtureは0件）。
  - [x] manifestの全entryについてexact ID/path、run ID、現在のidentityを再読込し、baseline対象でないことを確認する。
  - [x] manifestで確認できたUser、File、Folder、Tag、Favorite、Share、Recent、Activity、Backup、Media job/派生データ、端末temporary fileだけを個別に削除する。テスト専用Android packageだけを実行後にアンインストールし、リモートデータは作成・削除していない。
  - [x] cleanup guardが拒否した対象は削除せず、理由を記録して報告する。
  - [x] manifest対象が0件になったこと、baselineの既存ID/件数/checksumが不変であることをread-onlyで再確認する。

## フェーズ6: ドキュメント・Pull Request・完了記録

- [x] 正式文書への更新要否を確認する。
  - [x] 実装が既存の正式仕様・設計の範囲内なら、更新不要の理由を記録する。既存のPDF一時保存・写真Swipe・削除認可契約を変更していない。
  - [x] ~~仕様・設計を変える必要が判明した場合は、`docs/product-requirements.md`、`functional-design.md`、`architecture-design.md`、`repository-structure.md`、`development-guidelines.md`の該当箇所を同じPull Requestで整合更新する。~~（既存正式仕様の範囲内であり、更新対象なし）

- [ ] PR 1の最終準備を完了する。
  - [ ] 本tasklistのフェーズ0〜5および本フェーズの該当項目がすべて`[x]`であることを確認する。
  - [ ] 変更範囲、テスト結果、実機確認結果、清掃結果、未実施事項をself-reviewする。
  - [ ] 対象変更だけをCommitし、作業BranchをremoteへPushする。既存の未Commit変更はCommitへ含めない。

- [x] 英語のPR 1を1本作成する。
  - [x] PR titleとbodyにPurpose、Scope、Changes、Tests、Impact/Not performedを英語で記載する。
  - [x] baseを`main`とし、Mergeは行わない。

- [ ] Steeringモード3でPR 1完了記録を追加する。
  - [ ] 作成日、PR番号/URL、実施したbuild/test/lint/実機確認、清掃結果を「各Pull Request完了記録」へ記録する。
  - [ ] 計画との差分、追加タスク、技術的に不要になった項目と代替、後続引継ぎを記録する。該当なしは「なし」と記載する。
  - [ ] 完了記録を同じBranchへCommit/Pushし、PRへ反映されたことを確認する。

- [ ] 全体振り返りを記録する。
  - [ ] 本ファイルに未完了の`[ ]`がないことと、PR 1完了記録があることを確認する。
  - [ ] 実装完了日、計画と実績の差分、技術的な学び、プロセス上の改善点、次回への提案を「全体振り返り」へ記録する。
  - [ ] 全体振り返りをPRへ反映し、ユーザーへPR URL、検証結果、清掃結果を報告して停止する。

## PR進捗記録

PR作成後の進捗を記録する。全未完了タスクを解消するまでは、完了記録・全体振り返りにはしない。

### PR 1（2026-09-10、Open）

- PR: https://github.com/ry825/Kura_Storage/pull/72
- 実施内容: 一括選択actionを折返し可能かつ48dp以上に変更し、写真全画面の下部操作へcontrast surfaceとnavigation bar insetを追加した。写真切替で全画面状態を維持し、PDF再オープン時に旧render job・documentを閉じるようにした。
- 検証: `scripts/ci/verify-android.sh`（JDK 17、Android SDK API 36）成功。実機で一括選択の360dp・文字200%テスト、写真の全画面Swipe保持テスト、feature-media全27件（PDF rendererを含む）に成功。
- 清掃: テスト専用Android packageを各実行後にアンインストールした。今回新規のUser、File、Folder、Tag、Favorite、Share、Backup、Media job、端末temporary fileは作成していない。既存データの削除は0件。
- 計画との差分: 実サーバーfixtureを作らず、既存test doubleと端末上のCompose/PdfRenderer testで境界を検証した。サーバーAPI・認可・既存削除契約は変更していない。
- 追加タスク・不要タスク・後続引継ぎ: なし。
- 未完了: 一括選択のlandscape/system inset/進行状態、写真の文字拡大・明暗・回転等の境界、PDFのMockWebServerおよび実Server確認。これらは未完了のチェック項目として残す。

## 全体振り返り

すべてのタスクとPR完了記録が完了した後に記録する。
