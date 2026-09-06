# 変更前baseline（2026-09-06）

## 実行境界

- Source revision: `912e3bb`（PR #63 Merge後の`origin/main`）。
- 作業Branch: `feat/android-upload-media-backup-usability`。
- Android端末: baseline確認時点でADB接続なし。
- Local container: baseline確認時点で起動中のKuraStorage containerなし。
- Network: Wi-Fi interfaceは接続中だが、SSID、BSSID、Host、IP等の識別情報は記録しない。
- PR #63の自動Test・Android 13実機・実Server結果は、
  `.steering/20260905-android-viewer-navigation-ux-fixes/evidence/final/README.md`を既存証跡として参照する。
- Token、Password、User/File/Folder ID、個人的なFile名、URI、物理Pathは本記録へ保存しない。

## 報告18項目の変更前状態

| # | 対象 | 確認結果と根拠 |
| ---: | --- | --- |
| 1 | Transfer status | `FileBrowserState.transfer`は単一`TransferEvent?`で、`runTransfer`が`UploadCompleted`をそのまま保持する。UIも`Upload completed.`を常設表示するため、成功後に消えない経路をSourceで再現した。 |
| 2 | 複数File Upload | `MainActivity`のFile browserは`ActivityResultContracts.OpenDocument()`を使用し、単一URIだけを`startUpload`へ渡す。複数Queue stateはない。 |
| 3 | Folder Upload | Backup rule用には`OpenDocumentTree`が存在するが、手動UploadにはFolder picker、tree walker、親優先作成計画がない。 |
| 4 | パンくずNavigation | `FileBrowserState`はID付きbreadcrumbを導出するが、`FileBrowserScreen`は単一`Text`へ`joinToString`しており、各祖先を操作できない。既存generation guardはFolder open/backにはある。 |
| 5 | 写真表示 | `PhotoViewerScreen`に全画面state/操作がない。画像は`ContentScale.Fit`かつ縦横同率zoomで歪みは抑止済みだが、`fillMaxSize`により小画像を既定で元pixel相当以上へ拡大し得る。 |
| 6 | Search→Home | Bottom navigationは全項目で同じ`navigateToTopLevel`を使い、Homeでも`popUpTo(HOME) { saveState = true }`と`restoreState = true`を組み合わせる。SearchからHomeを確実にrootへ戻す専用契約がないため、報告経路を回帰対象へ固定する。 |
| 7 | Thumbnail状況 | 個別Media Job取得APIはあるが、User権限で絞ったThumbnail/PDF Thumbnailのqueued/running/failed集計APIとAndroid表示はない。 |
| 8 | Thumbnail並列生成 | `MediaGenerationWorker.ExecuteAsync`は1 loopにつき`RunNextAsync`を1回awaitする。`MaximumConcurrentMediaJobs`の既定値も1で、Job生成は直列。DB/は既存worker token、lease、temporary publish契約を再利用できる。 |
| 9 | 動画品質 | AndroidにLow/Medium/Originalの品質UI、生成待機、Original fallback操作があり、Serverは`VideoLow`/`VideoMedium`Jobを生成できる。今回のOriginal固定と不一致。 |
| 10 | Settings視認性 | PR #63後のSettingsはMaterial theme tokenへ統一され、API 33 Emulator/Android 13実機のLight/Darkとcontrast Testが成功済み。報告現象は現行revisionでは再現せず、全下位画面の回帰確認対象とする。 |
| 11 | 現在Wi-Fi登録 | `AndroidCurrentWifiSource`はAndroid version別権限、非VPN Wi-Fi、SSID/BSSID正規化、未接続/取得不能を型付き結果へ変換する。登録画面は明示Saveを維持し、PR #63実機証跡でも外部Wi-Fi境界が成功済み。報告現象は回帰Test対象とする。 |
| 12 | 動画操作Overlay | 全画面には半透明overlayがあるが、通常表示ではPlayer surfaceと、その下の`PlayerControls`が分離している。通常/全画面の共通overlay要件を満たさない。 |
| 13 | 動画カクつき | 既存Playerは1 Player/1 item、Range再生、lifecycle保持を備えるが、Player sourceは通常表示で固定16:9 containerを使い、接続別buffer/cacheおよびstartup/rebuffer/dropped-frame比較記録がない。 |
| 14 | Backup並列処理 | `BackupTransferRepository.transfer`は最大100件をclaim後、Folder groupと各entryを通常の`for` loopで逐次`upload`する。Checkpoint/receipt/leaseは項目別だがdispatcherは直列。既存16 MiB実Serverbaselineは約3.37〜3.73 MiB/s。 |
| 15 | PDF | PR #63後はmetadata確認、256 MiB/Session 512 MiB上限、`.part`清掃、完全Fileだけの`PdfRenderer`、typed failure、responsive viewportを実装済み。Android 13実機で2 page PDFを表示済み。報告現象は正常/破損/暗号化/中断の回帰確認対象とする。 |
| 16 | File header | Top app barの下にPath、Admin storage panel、New folder、結果/error panelを同じ縦Columnへ常設し、一覧前の縦領域が大きい。主要操作を失わず圧縮する余地がある。 |
| 17 | 一覧scroll復元 | `LazyColumn`/`LazyVerticalGrid`へ保存可能なstateを渡さず、Folder/sort/filter別anchor ID・offset stateもない。Viewerから再Compositionすると先頭へ戻り得る。 |
| 18 | 安全な清掃 | 直近作業はmanifest限定清掃済みだが、今回run用manifestは未作成だった。Fixture作成前にRepository外manifestとexact-ID guardを作る。 |

## 正式仕様との差分

- `docs/product-requirements.md` 4.6およびMedia関連節は動画Low/Medium生成と品質選択を要求しているが、承認済みSteeringは動画Original固定へ変更する。
- `docs/product-requirements.md` 7.5.1は1操作1FileをMVP条件として記載しているが、今回の複数File/Folder Uploadで拡張する。
- 現行正式文書はThumbnail専用集計API、Thumbnail並列数、一覧anchor、共通転送枠をまだ定義していない。
- 影響対象は`docs/requirements.md`の「正式仕様との整合が必要な変更」と`design.md`に明記済みであり、最終実装に合わせてフェーズ11で5つの正式文書とOpenAPIを同じPR内で更新する。

## 既存性能・回帰基準

- Backup: `docs/testing/20260903-android-auto-backup-pr5.md`の16 MiB、4 x 4 MiB chunkで4.293秒/3.73 MiB/sおよび4.747秒/3.37 MiB/s。
- Media worker: `docs/testing/20260829-media-worker-pr2.md`のlease、atomic publish、実tool Testを正確性baselineとする。並列性能値は未取得のため、フェーズ6/12で直列1と2/4を同一fixtureで測定する。
- Viewer/Settings/PDF/Navigation: PR #63 final evidenceのRepository検証、API 33 Emulator 126件、Android 13実機154件成功を回帰baselineとする。

このbaselineは変更前の状態と既存成功範囲を記録するもので、今回の受け入れ条件達成を主張しない。
