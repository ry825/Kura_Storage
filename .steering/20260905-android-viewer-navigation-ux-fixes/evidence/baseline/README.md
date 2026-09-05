# 変更前ベースライン（2026-09-05）

## 実行境界

- Source revision: `a395c3f`（PR #62 Merge後の`main`）
- Android実機: この確認時点ではADB接続なし。
- Local container: 起動中のKuraStorage containerなし。
- 実Server・Wi-Fiの変更前挙動は、既存の2026-09-04〜05の実機証跡を回帰基準とし、変更後の最終確認では再接続して検証する。
- File名、User名、Token、SSID/BSSID、物理Pathなどの実値は記録しない。

## 再現した実装上の原因

| 対象 | 変更前の状態 |
| --- | --- |
| Video full screen | `MediaPlayerScreen`の最上位`Column`が`fullscreen`でも`verticalScroll`を保持し、Player操作はSurface overlayではなく後続の通常Layoutに置かれる。 |
| Settings | Settings hub自体は共通rowへ整理済みだが、下位画面には個別の情報階層・Size表記が残るため、全到達画面で統一確認が必要。 |
| PDF | `PdfViewerScreen`全体が`verticalScroll`で、page viewportは固定420dp。失敗時の主要導線が`Download instead`で、表示は汎用`PDF unavailable`に集約される。 |
| Favorites/Search/Tags | Favoritesのleading Thumbnailは72dp。Search rowには既存Thumbnail注入がなく、Tag card本体tapからTag filter結果へ遷移するcallbackがない。 |
| Variant size | ViewerはOriginal確認用Sizeを保持するが、表示中Low/Medium派生のHEAD metadataを取得してSourceとSizeを同時commitする契約がない。 |
| Size formatting | Media PlayerとCache settingsなどに独自formatterがあり、`MiB`/`GiB`と利用者向け`KB`/`MB`/`GB`表記が混在する。 |
| Text document | ServerとAndroidが6 MIME allowlistおよび厳密UTF-8に依存し、UTF-16・lossy preview・確認付き保存契約を持たない。 |
| Folder navigation | mutableな`folderStack`と`breadcrumbs`を別々に先行更新し、UIも`currentFolder`を後付けする。同一target連打、古い応答、open中Backを直列化するgenerationがない。 |

## 既存回帰基準

- `docs/testing/20260830-android-media-integration-pr4.md`: Android 13物理端末でLOCAL_DIRECT/REMOTE_SECURE、実Media/PDF、通信断を確認した基準。
- `docs/testing/20260904-android-ui-pr10-final-e2e.md`: Android 13物理端末で全module connected test、主要画面、TalkBack、Light/Dark等を確認した基準。
- `.steering/20260905-android-ui-simplification/evidence/pr2/README.md`および`pr3/README.md`: 直近のViewer/Favorites実機確認基準。

この文書は変更前の構造的な再現記録であり、変更後の成功を主張しない。実装後は同じ観点を自動Test、API 33 Emulator、接続可能になった物理端末・実Serverで再確認する。
