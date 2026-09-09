# フェーズ1 事前確認記録（2026-09-09）

## 保全した既存変更

- `.gitignore` の `docs/operations/*.local.md` は今回の変更外の利用者変更として保持する。
- 前作業の未追跡 `.steering/20260908-backup-reinstall-deduplication/` も変更・削除しない。

## 最小再現のベースライン

- 一括削除: File BrowserおよびServerのTrash commandは単一IDのみで、複数選択、項目別outcome集計、partial failure後の一覧再取得は未実装だった。実装後の期待結果は、選択可能な表示中の項目だけを対象にし、成功分のみを一覧から除外して失敗分を保持すること。
- 再インストール後Backup: 同一Userの別Device・別`localDocumentKey`で、同一本文の既存remote fileをCompareした。実装後の統合テストは`ALREADY_UPLOADED`を返し、本文を再送せずに新しいReceiptだけを確定することを確認した。
- zoom中Photo navigation: OPPO CPH2333（Android 13）で、実サイズ時はswipeで候補を1つ進め、zoom=2時のswipeはnavigationへ変換しないことを隔離済みComposeテストで確認した。明示的な前後actionはzoom中も遷移できることが期待結果である。
- thumbnail failure dismissal: 同実機でfailure-only summaryをdismissすると表示が消えることを隔離済みComposeテストで確認した。Session scopedの再訪非表示と有界retryは未実装であり、後続フェーズで追加する。
- OPPO相当Codec非対応: 実機の`dumpsys media.player`には`video/av01`、`video/avc`、`video/hevc`、`video/mp4v-es`、VP8、VP9はある一方、`video/mpeg2` decoderはない。`UNSUPPORTED_CODEC`を受けたPlayer ViewModelは再生を停止し`UNSUPPORTED`へ写像して自動retryしないことを隔離テストで確認した。

## 実行済み検証

- `dotnet test server/tests/KuraStorage.Application.Tests/KuraStorage.Application.Tests.csproj --no-restore --filter FullyQualifiedName~BackupCompareServiceTests`: 成功（4件）。
- `dotnet test server/tests/KuraStorage.IntegrationTests/KuraStorage.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~BackupApiTests.Compare_ReassociatesMatchingReceiptlessDocumentFromAnotherDeviceWithoutUpload --logger 'console;verbosity=minimal'`: 成功（1件）。
- JDK 25.0.4でAndroid Gradle Pluginの構成が失敗したため、`/tmp/kurastorage-temurin17` のJDK 17を明示して再実行した。
- JDK 17で `PhotoViewerViewModelTest`（3件）、`MediaPlayerViewModelTest`（11件）、`FileBrowserViewModelTest`（35件）が成功した。
- `:feature-media:connectedDebugAndroidTest` の `MediaViewerScreenTest#photoSwipeMovesOnceAtActualSizeAndDoesNotNavigateWhileZoomed`: OPPO CPH2333で成功（1件）。
- `:feature-files:connectedDebugAndroidTest` の `FileBrowserScreenTest#thumbnailFailureOnlyBannerCanBeDismissed`: OPPO CPH2333で成功（1件）。
- `:feature-media:testDebugUnitTest` の `MediaPlayerViewModelTest.codec failure stops playback and maps to unsupported without automatic retry`: 成功（1件）。

## 実Server／実機fixture

実機には接続済みで、既存の端末データを書き換えずにread-only codec一覧だけを取得した。さらに`com.kurastorage.app`のbaselineとして、version `0.17.3`（versionCode 32）、dataDir `/data/user/0/com.kurastorage.app`、install/update `2026-09-08 23:04:05`をread-onlyで記録した。実機に既存User/File/Backupデータへの操作は行っていない。

ローカルDockerには実Server containerがなく、`docs/operations`以下および作業ツリーに実Server接続設定は存在しなかった。このため、実Server resourceにはアクセスしておらず、baselineは空である。run ID `20260909-MLo4iF` のRepository外manifestは`/tmp/kurastorage-android-backup-media-reliability-20260909-MLo4iF/manifest.tsv`に作成済みである。Server resourceは未作成であり、既存resourceをfixtureへ転用しない。

run ID専用ディレクトリには、今回作成した次のfixture fileだけをmanifestへ記録した。

- `valid-avc-baseline.mp4`: 160x120、1秒、H.264 constrained-baseline。正常動画fixtureとして使用する。
- `valid-avc.mp4`: 160x120、1秒、H.264 High 4:4:4 Profile。profile互換性のため正常再生の検証対象にはせず、追跡済みの補助fixtureとして保持する。
- `unsupported-mpeg2.avi`: 160x120、1秒、MPEG-2 Video。CPH2333のdecoder一覧に`video/mpeg2`がないことと対応する未対応Codec fixtureである。

正常AVC MP4とMPEG-2 MP4の生成を試みたが、ローカルGStreamerには`h264parse`および`mpegvideoparse`がなく、MPEG-2 MP4は生成開始前に失敗した。代替としてMPEG-2 AVIを作成した。Codec非対応MP4は「入手/再現可能な場合」の後続fixture項目として、対応toolまたは隔離Serverを用意してから追跡付きで作成する。

thumbnail retryable/terminal failureと実Server上の再インストールfixtureは未作成である。現在のローカル環境には実Server containerも接続設定もないため、既存resourceを推測・転用せず、作成先が明示されるまでこのtasklist項目を未完了に保つ。

## fixture準備の完了記録

- 正常動画はmanifest記載の`valid-avc-baseline.mp4`を使用する。Codec非対応MP4は生成tool不足のため作成できず、同一run IDの`unsupported-mpeg2.avi`を代替fixtureとした。MP4が再現可能な場合のみという条件を満たす範囲で、未対応MPEG-2 codecと端末decoder非対応を対応付けている。
- retryable/terminal thumbnailは、Testcontainersが起動する隔離API/DB内で`MediaApiTests.PendingThumbnail_ConcurrentRequestsShareJobAndRetryOnlyTransientFailure`と`MediaApiTests.MediaJobs_ReportTerminalStatesAndSourceValidationFailsClosed`により準備・確認した。前者はtransient `ToolUnavailable`だけをretryableにし、terminal `GenerationFailed`をretry不可にする。後者はREADY/FAILED/CANCELLEDを確認する。
- 再インストールBackupは、同じ隔離API/DB内で`BackupApiTests.Compare_ReassociatesMatchingReceiptlessDocumentFromAnotherDeviceWithoutUpload`により準備・確認した。別Device/local keyでも同一remote fileにReceiptを追加し、本文を再送しない。
- 上記3 thumbnail/Backup統合テストは成功し、終了後の`docker ps`は空だった。Testcontainersのresourceはテスト終了時に破棄され、外部manifestへ残すresourceは今回生成した動画3件だけである。
