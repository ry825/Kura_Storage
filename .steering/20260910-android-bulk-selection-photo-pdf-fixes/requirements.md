# Android 一括選択・写真全画面・PDF表示修正 要求内容

## 概要

Androidアプリで報告された一括選択時の操作UI、写真プレビューの全画面操作、およびPDFアプリ内表示の不具合を修正する。実装・検証・清掃・Pull Requestは、関連変更をすべて含む1本のPull Requestとして完了する。

## 背景

一括選択時に削除などの操作ボタンが崩れて操作しにくい。写真プレビューでは全画面時の下部操作が背景と区別しにくく、左右Swipeが一度全画面を解除してから前後写真へ遷移するため、閲覧が中断される。PDFは既存のアプリ内表示契約があるにもかかわらず、正しく開けない。

前回のViewer修正で定義済みの正式PDF契約（通信量確認後のprivate temporary fileへのstreaming取得、`PdfRenderer`によるアプリ内表示）と、写真Viewerの前後移動契約を維持しつつ、実際の利用で発生する不具合を解消する。

## 実装対象の機能

### 1. 一括選択アクションのレイアウト安定化

- Files、Shared、Favorites、Search、Tagsなど、一括選択を提供する画面で選択中の削除を含むアクションUIを確認する。
- 画面幅、orientation、system inset、選択件数に関わらず、操作ボタンが重なり、切れ、意図しない位置へ移動、または読めない状態にならないようにする。
- 削除は既存の権限、確認、対象IDによる安全な操作契約を変更しない。

### 2. 写真プレビューの全画面操作とSwipe遷移

- 全画面写真プレビューの下部操作を、写真の明暗を問わず判別・操作できるcontrast、背景、safe inset、touch targetで表示する。
- 全画面中の有効な左右Swipeは、全画面状態を維持したまま前または次の写真へ1件だけ遷移する。
- Zoom中のpan、edge、loading/error、先頭・末尾、連続Swipe、および古い非同期結果が現在表示を上書きしないことを既存の写真Viewer契約どおりに扱う。

### 3. PDFアプリ内表示の修正

- `application/pdf`の対象を選択し、通信量確認後に端末への保存操作を要求せずアプリ内PDF Viewerで開けるようにする。
- HEAD/Content取得、private temporary file、`PdfRenderer`初期化・描画、lifecycle cleanupのいずれで失敗しているかを切り分け、既存の分類済みerror stateと再試行導線で扱う。
- 256MiB単体上限、Session合計512MiB、空き容量確認、64KiB streaming、完全性検証、Session分離、TTLおよびlogout時のcleanupを維持する。

### 4. 迅速かつ安全な検証・テスト資材清掃

- 失敗を再現する最小限のUnit、Compose/Instrumented、必要に応じた実機確認を追加し、変更対象に絞った高速な検証を先に実行する。最後に今回の変更に適用されるrepository標準検証を実行する。
- テストのために新規作成するUser、File、Folder、Tag、Favorite、Share、Recent、Activity、Backup、Media job/派生データ、端末temporary fileその他の資材は、作成直後に作業固有manifestへexact IDまたはexact pathで記録する。
- 清掃前にmanifest membershipを再確認し、今回のmanifestに記録された対象だけを削除する。既存データ、曖昧な名称一致、親Folder一括、全件削除は使用しない。

## 受け入れ条件

### 一括選択アクション

- [ ] 対象の一括選択画面で、削除を含むすべての選択アクションが360dp幅、landscape、system inset、およびOS文字200%でも重複・切断・不可視にならない。
- [ ] 1件、複数件、選択解除でアクション状態と対象件数が正しく更新され、既存の削除確認・認可・対象ID契約を維持する。

### 写真全画面

- [ ] 全画面写真の下部操作は明暗の異なる写真上でも視認でき、safe insetを避け、最低48dp相当の操作領域を確保する。
- [ ] 全画面中の左右Swipeで、全画面を解除せず隣接する写真へ1回のgestureにつき1件だけ遷移する。
- [ ] 先頭・末尾、zoom中、loading/error、連続Swipe、画面回転または再構成でも全画面stateと写真stateが矛盾しない。

### PDF表示

- [ ] 通信量確認を承認した256MiB以下の正常PDFは、SAF保存を要求せずprivate temporary fileからアプリ内Viewerへ表示される。
- [ ] authentication、permission、not found、too large、storage不足、incomplete、corrupt、password protected、render unsupported、networkの失敗が誤って`PDF unavailable`へ集約されず、再試行可能な場合は`Retry open`を表示する。
- [ ] PDF temporary file、Page、Bitmap、FileDescriptorが画面離脱、取消、Session終了、logoutで適切に閉じられ、既存の容量・完全性・Session境界を守る。

### 検証と清掃

- [ ] 追加・変更した状態遷移を自動テストで再現し、対象モジュールのformat、lint、build、testを成功させる。
- [ ] テストで今回追加した資材だけをmanifestに基づいて削除し、清掃結果と既存データを削除していない確認を記録する。
- [ ] すべての対象タスクを完了した後に、変更全体を含む英語のPull Requestを1本だけ作成する。

## 成功指標

- 一括選択、写真全画面、PDF表示の報告済み再現手順が修正後に成功する。
- 変更範囲に応じた最小の自動テストを優先して短いフィードバックを得つつ、Pull Request前に必要な標準検証を完了する。
- 今回作成したテスト資材を0件まで清掃し、既存資材の削除を0件にする。

## スコープ外

- 一括選択以外の一覧情報設計や、既存削除ポリシー・権限モデルの変更。
- 新しいPDF library、外部PDF viewer、またはPDFを恒久保存する仕組みの導入。
- 写真Viewerの対応MIME・画質契約・一覧順序の変更。
- manifestに記録されていない既存データの削除。
- この作業の途中での個別Pull Request作成（全対象を最後に1本へまとめる）。

## 参照ドキュメント

- `docs/product-requirements.md` - 写真Viewer、PDF Viewer、一覧操作のプロダクト要求
- `docs/functional-design.md` - 写真Swipe、PDF取得・表示・失敗分類の機能設計
- `docs/architecture-design.md` - Android Viewer、temporary file、lifecycleの設計
- `docs/development-guidelines.md` - Android UI、PDF resource lifecycle、テストの実装規約
- `.steering/20260905-android-viewer-navigation-ux-fixes/` - 先行するViewer/PDF修正の完了記録と設計
