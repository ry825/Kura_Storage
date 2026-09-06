# 実機・実Server検証記録（2026-09-06）

## 環境

- Android 13 / API 33の物理端末。
- signed non-debuggable Release APK、version code 26。
- Raspberry Pi 4実Server、TLS・Server identity・User／Device／Session認証を維持。
- 作業prefixは`ks-20260906-ux-`。秘密情報、接続先、SSID／BSSID、個人的なFile名は記録しない。

## Upload・File browser・Navigation

- 単一File、複数File、入れ子Folder、空Folder、同名Fileを実ServerへUploadし、Server側の親子関係、Size、Upload sessionのexpected／received Sizeとchecksumを照合した。
- 全成功後は常設Transfer panelが消え、完了Snackbarだけになった。
- 作業Folder内の3階層で、`My files`、長い親Folder名、現在Folderが折り返してすべて表示された。祖先Linkからrootへ戻れた。
- 360 dp・font scale 2.0・5階層のInstrumented Testでも、全階層表示と全祖先Linkを確認した。
- 深いscroll位置、stable File ID、offset、Folder／Sort／Filter別context、Refresh・削除・rotation復元はUnit／Compose testで確認した。

## Thumbnail・Media・PDF

- 実Server集計はThumbnail系11 Jobに対して`COMPLETED=9`、異常PDF fixtureに対して`FAILED=2`だった。Android表示の失敗2件とDBが一致した。
- `Thumbnail generation failed`はstale表示ではなく、破損PDFと暗号化PDFの実失敗だった。Failure-only bannerの`Dismiss`を実機で操作し、File一覧を利用可能なまま非表示にできた。
- 対象Fileを完全削除した後はServerのfailed Thumbnail job集計が0になり、失敗表示の根拠も解消した。
- 対応H.264動画をOriginalだけで再生し、Player生成からdecoder生成まで1.771秒、目視rebuffer 0回、末尾128提示frameのgap 0件を確認した。詳細値は`docs/testing/20260906-android-video-playback-backup.md`に記録した。
- Thumbnail並列1・2・4・6・8と混合負荷は`docs/testing/20260906-thumbnail-concurrency-raspberry-pi.md`に記録し、安全な既定値2を採用した。

## Settings・Trusted Wi-Fi

- 権限拒否時の停止案内、許可後の現在Wi-Fi名と利用可能なaccess pointのForm反映を実機で確認した。
- 検出だけではPolicyへ追加されず、明示的な登録操作後だけ一覧へ追加された。
- work scopeのPolicyでBackupを実行したが、Wi-Fi一致はTLS、Server identity、ZeroTier、User／Device／Session認証を代替していない。
- Backup画面は単一`LazyColumn`へ修正し、物理端末と360 dp・font scale 2.0のInstrumented Testで下部操作へ到達できた。

## Backup並列E2E

- 4 MiB x 4件を共通上限2で実行し、同時Sessionは最大2件だった。
- 全4件が`COMPLETED`となり、各4,194,304 bytesのexpected／received Size、File ID、receiptに欠落・破損・重複がなかった。
- 直列相当101.068秒に対し、並列2のwall timeは56.438秒で44.2%短縮した。
- 同じfixtureの2回目scan後も4 Session／4 distinct File IDで、成功済みFileを再Uploadしなかった。
- 手動Upload優先、上限超過待機、部分失敗、429、timeout、Network切替、cancel、process再開はdeterministic testで補完した。

## manifest限定清掃

- ServerのFile／Folder 24件をmanifest exact IDで再取得し、子から親へ1件ずつTrash／Purge APIで完全削除した。親Folderの再帰削除、wildcard、名前部分一致は使用していない。
- API purgeで関連Media job 11件、Derivative 11件、失敗Thumbnail job 2件が消えた。
- 保持契約により残った完了済みUpload session 21件は、manifest membership、`COMPLETED`、expected／received Size一致を1件ずつ確認してexact IDで削除した。
- Android側はmanifestと事前列挙に一致したFile 21件、空Folder 5件を子から親へ削除した。work scopeのBackup RuleとWi-Fi Policy、検証用debug packageだけを削除し、production packageは維持した。
- 最終再照会は`remaining_manifest_server_ids=0`、work prefix File entry 0、failed Thumbnail job 0、Android work prefix 0、debug package 0だった。
- 既存のroot Folder、既存Backup Rule、production Android packageは残っていることをspot-checkした。
