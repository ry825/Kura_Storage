# Android動画再生・Backup並列 実機測定

## 結論

- Android 13実機とRaspberry Pi実Serverの基準動画は、Player生成からhardware AVC decoder生成まで1.771秒で到達した。
- 30 fps動画のSurface提示履歴は、取得できた末尾128 frameが33.333 ms間隔で連続し、1 frameを超えるgapは0件だった。再生中の目視rebufferも0回だった。
- 動画再生中のServerはCPU idle 93〜98%、I/O wait 0%、swap in/out 0だった。
- Androidの共通転送上限は2を維持する。4件の4 MiB Backupは最大2件を並列実行し、直列相当101.068秒に対して実wall time 56.438秒で、44.2%短縮した。

## 環境

| 項目 | 値 |
| --- | --- |
| Android | OPPO CPH2333、Android 13 / API 33 |
| APK | signed non-debuggable Release、version code 26 |
| Server | Raspberry Pi 4 Model B、8 GB、USB HDD、PostgreSQL 17 |
| 動画fixture | 3,897,342 bytes、360 x 640、H.264、30 fps、約6秒 |
| Backup fixture | 4,194,304 bytes x 4件 |

識別子、認証情報、接続先、SSID／BSSID、物理保存Pathは記録していない。

## 動画再生

Android logではPlayer生成が18:43:06.230、非同期hardware AVC decoder生成開始が18:43:08.001で、差は1.771秒だった。Serverでは同区間にmetadata取得とOriginal contentのRange経路だけが実行され、動画Low／Medium jobは作成されなかった。

SurfaceFlingerの対象Surface履歴では、ring bufferに残った末尾128 frameのpresent時刻が33.333 ms間隔で連続した。MediaCodecは同Sessionで180 sampleを処理し、decoder errorはなかった。platform captureが動画全体の明示的なdropped-frame counterを公開しなかったため、「全区間のdropが0」とは扱わず、計測できた末尾128 frameのgap 0件を採用する。

| 観測 | 今回 | 2026-08-30 baseline |
| --- | ---: | ---: |
| decoder生成まで | 1.771 s | cache revisit 3秒以内 |
| 目視rebuffer | 0 | 0 |
| Surfaceの連続提示 | 128 / 128 frame | 未採取 |
| PSS | 134,585 KiB | 117,912 KiB |
| RSS | 277,076 KiB | 259,820 KiB |

PSSは14.1%、RSSは6.6%増えたが、既定bufferはWi-Fi 15〜50秒、Cellular 5〜15秒の既存memory境界内であり、OOM、swap増加、ANRはなかった。`gfxinfo`のUI frame値は30 fpsの別Surface提示とOverlay操作を混在して数えるため、動画drop判定には使用しなかった。

Serverの1秒間隔7 sampleではCPU idleが93〜98%、I/O waitは全sample 0%、swap in/outも0だった。動画Range再生によるServer資源不足は観測されなかった。

## Backup並列

4件は同一Work内の固定dispatcherで処理し、開始中Sessionは常に2件以下だった。

| item | 所要時間 |
| --- | ---: |
| 1 | 31.218 s |
| 2 | 24.761 s |
| 3 | 20.775 s |
| 4 | 25.314 s |
| 直列相当合計 | 101.068 s |
| 並列2 wall time | 56.438 s |

全4件についてSize、Server確定受信量、receipt、最終`COMPLETED`を照合した。同じsourceを再scanしても4 Session／4 distinct File IDのままで、成功済みFileの重複Uploadはなかった。deterministic testでは並列数1・2・4、共通上限、手動Upload優先、部分失敗、429、timeout、Network切替、cancel、process再開を補完している。

Thumbnail混合負荷の詳細は`20260906-thumbnail-concurrency-raspberry-pi.md`に記録した。並列2で一覧p95とRange p95は直列より悪化せず、CPU余力、I/O wait、swap、thermalの安全条件を満たしたため、ThumbnailとAndroid転送の既定並列数はどちらも2を維持する。
