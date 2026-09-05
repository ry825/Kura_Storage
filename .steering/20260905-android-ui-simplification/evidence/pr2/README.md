# PR 2 写真Viewer・実機検証記録

## 検証環境

- 実行日: 2026-09-05
- 実機: OPPO CPH2333 / Android API 33 / 360dp幅
- 接続: ZeroTier経由の実Server
- 対象Build: `0.14.0-pr2-e2e3`（release validation build）
- 自動実機検証: `feature-media` 18件、`feature-search` 11件成功

## Download失敗箇所の特定

変更前Buildで写真の保存を実行すると、SAFの保存先は作成されたが、Server originalが
10,047,953 bytesであるのに対し、保存先には3.84 KiBの不完全Fileが残っていた。
このため、失敗箇所はSAF選択前ではなく、OutputStreamを開いた後のHTTP responseから
copy・closeまでの区間と特定した。変更前の処理は表示用variantと保存用variantを分離せず、
copyまたはclose失敗後の保存先削除と失敗分類も保証していなかった。

修正後は次の段階を個別に確認した。

1. `CreateDocument`が元File名と`image/jpeg`でAndroid標準DocumentsUIを開く。
2. 保存確定後にOriginal固定のHTTP requestを開始し、転送中表示になる。
3. HTTP 200のresponse bodyをOutputStreamへstreaming copyする。
4. OutputStreamのclose後にだけ完了扱いにする。
5. 保存結果を端末から読み出し、Server originalとsizeおよびSHA-256を比較する。

Token、SAF URI、絶対Path、個人File名は診断Logと本記録へ残していない。

## 実ServerでのMedia比較

同一写真について、Server access log、生成物、保存原本を照合した。

| Variant | HTTP | Content-Type | Decoded dimensions | Bytes |
| --- | ---: | --- | ---: | ---: |
| Low | 200 | `image/webp` | 1280 x 853 | 137,516 |
| Medium | 200 | `image/webp` | 2560 x 1707 | 237,762 |
| Original | 200 | `image/jpeg` | 5472 x 3648 | 10,047,953 |

Viewerの`Displayed`表示も各requestの実variantと一致した。

## Original download結果

- 保存File: 10,047,953 bytes / `image/jpeg`
- 端末保存File SHA-256: `835be98e71267845b7a4f66469fcf96e3e0888899972ecc48d72f98b50469f14`
- Server original SHA-256: `835be98e71267845b7a4f66469fcf96e3e0888899972ecc48d72f98b50469f14`
- SAF Cancel前後のDownloads内File数: 116件 -> 116件
- Cancel後の誤った完了表示: なし
- 通信、open、copy、close、削除失敗: Coordinatorの自動Testで、成功表示を行わず作成済みURIの削除を試みることを確認

## Viewer操作結果

- 等倍Swipeで`1 / 26`から`2 / 26`へ1項目だけ移動した。
- Zoom、Pan、Double tap中は位置が`1 / 26`のままで、前後Swipeへ誤判定しなかった。
- Zoom後の画像は独立したclip layer内に収まり、Top app barと操作領域へ描画されない。
- Previous/Nextは写真外にあり、Semantics上で操作可能である。
- Favoriteは追加・解除を実Serverへ反映し、初期状態へ戻した。
- 一時Tagを作成して追加・解除し、検証後に一時Tagを削除した。
- pending/errorは自動Test、実機Semantics、通信中表示で確認し、結果不明を成功表示へ反映しない。

## Responsive・Accessibility

- 物理幅360dpのPortrait/Darkで、Viewer、Quality、Toolbar、Tag sheetの重なりと到達不能がない。
- API 33実機上のCompose Testで、360dp、文字200%、Portrait/Landscape、Light/Dark fixtureを検証した。
- 実機Semantics treeでBack、Favorite、Tags、Download original、Details、Zoom、Previous/Nextのcontent descriptionと48dp以上のtap targetを確認した。
- 端末メーカーの制限によりADB shellから実機全体のfont scale変更は拒否されたため、文字200%は同じ実機上のCompose fontScale fixtureで確認した。

## Screenshot比較

変更前の公開比較基準は`docs/ui/android/mockups/files-media/016-photo-viewer.png`とした。
変更後は同じPortrait条件で、通常表示、Zoom/Pan、Quality、Download転送中、Tag追加/解除を実機Captureした。
実データの写真と個人File名を含むCaptureはPull Requestへ添付せず、次の観点を目視比較した。

- 写真へ重なるPrevious/Next overlayを廃止した。
- 大きな固定Cardを除去し、写真の利用可能表示領域を広げた。
- Quality、Favorite、Tags、Download、Details、Zoomを写真外のcompact操作領域へ集約した。
- Zoom画像をviewport境界でclipし、Toolbarへの描画はみ出しを解消した。
