# Android Viewer・一覧・Navigation UX修正 要求内容

## 概要

Androidアプリの動画・PDF・テキスト閲覧、Settings、Favorites、Search、Tags、File browserで確認された不具合と視認性の問題を修正する。ファイルの内容を主役とした表示、選択中画質と一致する情報表示、安定した階層Navigationを実現する。

今回の全実装・検証・文書更新は1本のPull Requestにまとめる。テストで追加したUser、File、Folder、Tag、Favorite、派生データ、一時データは追跡可能にし、今回追加したものだけを最終的に削除する。

## 背景

既存のAndroid UI・Media viewer・Favorites/Tags・Search・Text editorの各作業は実装済みだが、実際の利用で次の問題が確認されている。

- 動画を再生できず、Full screenで画面がscrollしてしまう。Playerの内容より周辺UIが目立ち、tap時の操作も分かりにくい。
- Settingsとその下位画面でIconが手前に強く表示され、文字と背景のcontrastも不足している。
- PDFがアプリ内Viewerへ進まず別途Downloadを求めるように見え、`PDF unavailable`となる場合がある。表示領域も狭い。
- Favoritesで写真・動画のThumbnailが小さく、説明やmetadataが相対的に大きい。SearchとTag別の対象一覧でも同様に視覚的な判別がしにくい。
- 写真・動画のSizeが選択中のLow・Medium・Originalではなく、OriginalのSizeだけを表示している。また、アプリ内のファイルSize表記が統一されていない。
- Text editorの対象が限定的で、テキストとして開けるDocumentでも拡張子やMIMEによって編集できない。
- File browserの左上Backを2回押さないと親Folderへ戻れない場合がある。Androidのsystem Backは親FolderではなくHomeへ戻る場合がある。
- Folderを短時間に連打すると、実在しない`/home/test/test/test`のようなパンくずが生成される。
- 登録済みWi-Fi利用時の接続経路が期待どおりか、実機で再確認する必要がある。

## 正式仕様との整合が必要な変更

### PDFの「Downloadなしで閲覧」

正式仕様では、PDF本体の通信前にSizeまたは予想通信量を示し、利用者の確認後にアプリ専用一時領域へstreaming取得する。この安全要件は維持し、ユーザーに別Fileとして保存させる操作や外部アプリへの移動を必須とせず、確認後はそのままアプリ内Viewerを表示する。

### Text editorの対象拡張

現在の正式仕様は、最大1 MiB、厳密なUTF-8、かつ`text/plain`、`text/markdown`、`text/csv`、`application/json`、`application/xml`、`application/yaml`のみを編集対象とする。今回はこれを変更し、拡張子のみで不要に拒否せず、端末上でテキストとしてdecode可能なDocumentを基本的に開いて編集できるようにする。バイナリやdecode不能な内容は文字化けやデータ破損の可能性を明示し、利用者が明示的に編集続行を選ぶまで上書きしない。対象、Size上限、decode方針、保存時の文字codeは`docs/product-requirements.md`、`docs/functional-design.md`、`docs/architecture-design.md`、`docs/development-guidelines.md`に同じ変更で反映する。

### 登録済みWi-Fiと接続

登録済みWi-Fiは自動Backupの実行候補を判定するポリシーであり、Server本人確認やUser認証の代替にはしない。登録済み外部Wi-FiではZeroTier、TLS、Server identity、User・Device・Session認証が成功した場合にアプリを利用できることを検証する。ローカル直接接続可能なWi-Fiでは、基盤networkへの通信bind、TLS、Server identity、API到達、User認証が成功することを検証する。SSID/BSSIDの一致だけで接続成功とは判定しない。

## 実装対象の機能

### 1. 動画再生とFull screen操作

- 対応MIMEの動画を現在の認証Sessionと正しい接続経路で再生できるようにする。
- Full screenでは動画表示領域を画面全体に固定し、通常の縦scrollや背後のページ操作を発生させない。
- 動画表示を1回tapすると再生・一時停止、seek、時間、速度、Full screen解除などの操作をoverlay表示し、再度tapまたは一定時間の無操作で非表示にできるようにする。
- 再生失敗、Range取得、codec非対応、画質派生生成待ちを分類し、再試行可能な表示にする。

### 2. 登録済みWi-Fiでのアプリ接続確認

- ローカル直接接続可能なWi-Fiと、登録済み外部Wi-Fi＋ZeroTierの両方で、起動、認証、File一覧取得、Media/PDF取得が成功するか実機確認する。
- 外部Wi-Fiだけが一致しZeroTierやTLS等の必須条件を満たさない場合は、安全側で接続不可またはBackup待機とし、理由を識別できるようにする。
- Wi-Fi登録が手動のアプリ利用可否やServer認証を不要に広げないことを回帰確認する。

### 3. Settings UIの視認性

- Settingsとその下位画面のIconを補助的な視覚要素として扱い、過度な前景色・大きさ・強調を抑える。
- 見出し、項目名、現在値、説明、状態、操作の視覚的優先度を整理する。
- Light/Dark themeの両方で文字と背景のcontrastを確保し、色だけに依存しない。
- 360dp幅、Landscape、OS文字200%で文字や操作が切れず、scrollが必要な場合も項目単位の読みやすさを維持する。

### 4. PDFのアプリ内表示

- 対象PDFをtapしたとき、別途保存操作を必須とせず、通信量確認後にアプリ専用一時領域へstreaming取得し、アプリ内Viewerを開く。
- `PDF unavailable`の原因を、認証、接続経路、HTTP、Size上限、空き容量、取得中断、破損、暗号化、Renderに分け、必要な再試行または案内を出す。
- PDF pageを表示領域に合わせて大きく描画し、拡大・縮小、pan、page移動、現在page/総page表示を使いやすくする。
- 256 MiB上限、Session一時File合計512 MiB、空き容量確認、部分File削除、TTL、Session分離を維持する。

### 5. Favorites・Search・TagsのThumbnail中心表示

- Favoritesの写真・動画は現在より大きなThumbnailを表示し、名前や説明・metadataは内容を圧迫しない大きさと行数にする。
- Search結果の写真・動画にも大きめのThumbnailを表示し、Thumbnail待ち・失敗時はFile type iconへfallbackする。
- TagsのTagをtapすると、そのTagが付いている現在閲覧可能なFile/Folder一覧を表示する。写真・動画には大きめのThumbnailを表示し、説明・metadataは小さくする。
- PDFは利用可能な既存Thumbnail、その他は対応するFile type iconを使い、各一覧から対応Viewerへ直接移動できるようにする。
- 一覧の安定順、pagination、権限再評価、非`ACTIVE`項目の扱いを既存契約と一致させる。

### 6. 選択画質と一致するSize表示

- 写真・動画のViewerでLowを選択中はLow派生データ、MediumはMedium派生データ、OriginalはOriginalのSizeを表示する。
- 派生データが未生成でSizeが未確定の場合はOriginalのSizeを誤表示せず、生成中またはSize未確定と表示し、確定後に更新する。
- 画質変更の途中では、現在表示または再生中のSourceとSize表示を一致させ、古いresponseで新しい選択状態を上書きしない。

### 7. アプリ全体のFile Size表記

- アプリ内でUserに表示するFile・Media variant・PDF・Transfer等のSizeは、共通formatterを使う。
- 1,024 bytes未満は`B`、1,024 bytes以上は`KB`、1,024 KB以上は`MB`、1,024 MB以上は`GB`とし、TB以上専用の表記は今回対象外とする。
- 値が不明な場合や0 byteの場合を実データと矛盾しない表記にし、負数やoverflowを表示しない。

### 8. DocumentのText閲覧・編集対象拡張

- `.txt`または現行の6 MIMEだけに限定せず、テキストとして取得可能なDocumentをアプリ内で開いて編集できるようにする。
- MIME、拡張子、内容検査の優先順と、許可するSize・文字codeを正式契約として定義する。
- decode不能またはバイナリの可能性がある場合は警告を表示し、明示的な確認なしに保存しない。
- 保存時は既存の権限、`expectedVersion`、`operationId`、version history、競合・復元契約を維持する。

### 9. File browserのBackとパンくずの安定化

- File browserで左上Backを1回押すと、現在Folderの親Folderへ正確に1階層戻る。
- Androidのsystem BackもFile browser内では同じ親Folder遷移を行い、browser rootにいる場合にだけApp shellの前画面またはHomeへ戻る。
- Folder遷移中の同一項目への連打、異なるFolderへの連続tap、読み込み完了前のBackを安全に直列化または無効化する。
- パンくずはtap回数に応じて文字列を追加せず、確定したFolder IDと親子関係から構築する。
- 取得失敗や遷移の競合時は最後に確定した実在Folderとパンくずを維持し、実在しないPathを表示しない。

### 10. 検証資材とテストデータの安全な清掃

- 不具合ごとに失敗を再現する自動Testまたは手動検証手順を用意し、修正後の回帰防止に使う。
- テスト資材に作業固有のprefix、ID一覧、またはmanifestを付与し、今回作成した対象を一意に特定できるようにする。
- 削除前に対象が今回のmanifestに含まれることを再確認し、曖昧な名前一致、親Folder一括、全件削除を使わない。
- User、File、Folder、Tag、Favorite、Share、Recent、Activity、Backup、Media job・派生データ、Android一時Fileなど、今回の検証で追加したデータだけを最終確認後に削除する。
- 作業前から存在するUser、File、Folder、DB row、物理File、派生データ、端末データは絶対に削除しない。
- 清掃後にmanifestの対象が残っていないことと、作業前の既存データが維持されていることを確認する。

## 受け入れ条件

### 動画

- [x] Android実機で保証対象MIMEの動画を再生・一時停止・seekできる。
- [x] Full screenで動画が利用可能領域全体に表示され、縦scrollや背後画面の移動が起きない。
- [x] 画面を1回tapするとPlayer操作overlayが表示され、操作可能である。
- [x] Portrait/Landscape、system bar表示切替、App background/foreground復帰でPlayerの再生状態とlifecycleが破綻しない。

### Wi-Fi・接続

- [x] ローカル直接Wi-Fiで起動・認証・一覧・動画・PDF取得が成功する。
- [x] 登録済み外部Wi-Fi＋ZeroTierで同じ操作が成功する。
- [x] 登録済み外部Wi-FiでもZeroTier、TLS、Server identity、User認証のいずれかが不正な場合は接続成功と扱わない。

### Settings

- [x] Settingsと下位設定のIconが文字より強く主張せず、項目名・現在値・説明を順に読める。
- [x] Light/Dark themeで通常文字は4.5:1以上、大きな文字と必要なUI要素は3:1以上のcontrastを満たす。
- [x] 360dp幅、Landscape、OS文字200%、TalkBackで設定項目を識別・操作できる。

### PDF

- [x] 256 MiB以下の正常なPDFは、Size/通信量確認後に別途保存操作なしでアプリ内表示される。
- [x] 取得可能なPDFが汎用的な`PDF unavailable`だけで終了せず、失敗原因に応じた再試行または案内が表示される。
- [x] PDF pageが画面で十分に大く表示され、zoom・pan・page移動を操作できる。
- [x] 大容量、空き容量不足、中断、破損、暗号化PDFで部分Fileや開いたresourceを残さない。

### Favorites・Search・Tags

- [x] FavoritesとSearchの写真・動画Thumbnailが現在より大きく表示され、説明・metadataが主要内容を圧迫しない。
- [x] Tags画面のTagをtapすると、そのTagが付いた現在閲覧可能な対象だけが表示される。
- [x] Tag別一覧の写真・動画に大きめのThumbnail、PDFに利用可能なThumbnail、その他にFile type iconが表示される。
- [x] Thumbnail生成中・失敗、pagination、権限失効、非`ACTIVE`項目で結果が漏洩または不整合にならない。

### 画質別Size・共通Size表記

- [x] 写真・動画でLow、Medium、Originalを切り替えるたび、実際に表示・再生するvariantのSizeが表示される。
- [x] 未生成variantのSizeをOriginalのSizeで代用せず、未確定状態が分かる。
- [x] アプリ内のすべてのFile Size表示が共通の`B`・`KB`・`MB`・`GB`基準に従う。
- [x] 0、1,023、1,024 bytes、1 MiB前後、1 GiB前後、不明値、大きな値の境界Testが成功する。

### Document編集

- [x] 正式契約で追加対象としたDocumentを、`.txt`以外の拡張子でもアプリ内で開いて編集・保存できる。
- [x] decodeが不確実な内容は警告され、明示確認なしに原本を上書きしない。
- [x] 編集対象外、Size上限超過、権限不足、version競合が判別可能に表示され、元Fileを破損しない。
- [x] 既存のversion history、復元、冪等再送、共有権限のTestが引き続き成功する。

### Navigation・連打

- [x] 左上BackとAndroid system Backは、File browser rootより下では1回の操作で必ず親Folderへ1階層戻る。
- [x] browser rootでのBackだけがApp shellの前画面またはHomeへ移動する。
- [x] 同一Folderを高速に連打しても、パンくずとNavigation stackにFolderが重複しない。
- [x] 複数Folderの連続tap、読み込み中のBack、取得失敗を組み合わせても、確定した実在Folderとパンくずが一致する。

### 品質・清掃・Pull Request

- [x] 変更対象のViewModel、Navigation、Compose UI、Media3、PDF、Thumbnail、Text契約、Size formatterに対応するUnit・Contract・Compose/Instrumented Testがある。
- [x] Androidと関連Serverの必須CI、実機・実Server E2E、Light/Dark、360dp、Landscape、文字200%、TalkBackの確認が成功する。
- [x] 今回作成したテストデータがmanifest等で追跡され、それらだけが削除されている。
- [x] 作業前から存在したUser、File、Folder、関連DB row、物理File、派生データ、端末データが削除または変更されていない。
- [ ] 今回のすべての実装、Test、正式文書更新、検証記録、清掃を1本の英語Pull Requestにまとめる。
- [ ] Pull Request作成後に`tasklist.md`へ完了記録を追加し、同じBranchとPull Requestへ反映する。

## 成功指標

- 報告された動画、PDF、Settings、Favorites、Search、Tags、Size、Document、Back・パンくずの各問題を実機・実Serverで再現し、修正後に同じ手順で解消を確認できる。
- Media・PDF・Thumbnailは元Fileの不要な取得、Main threadでのdecode/render、resource leak、認証情報の漏洩を発生させない。
- 一覧とSettingsは、写真・動画・文字の優先度が明確で、主要操作に到達しやすい。
- すべての検証データが今回の作成物として識別でき、既存データを変更せずに清掃できる。

## Pull Request方針

- 今回は項目ごとにPull Requestを分割せず、全タスクを1つの作業Branchと1本のPull Requestで完了する。
- `tasklist.md`は依存順のフェーズに分けるが、全フェーズは同じPull Request単位に属する。
- Pull Requestは全実装、自動Test、実機・実Server確認、正式文書更新、テストデータ清掃、差分review完了後にのみ作成する。
- Pull RequestのTitleと本文は英語で作成し、目的、対象タスク、変更内容、Test結果、影響、未実施事項を記載する。

## スコープ外

以下は今回の実装対象外とする。

- Web UIの同等変更。
- 動画・音声の対応codec/MIME追加、DRM、subtitle、picture-in-picture、cast、playlist自動再生。
- PDFのannotation、form入力、編集、OCR、全文検索、256 MiB上限の拡張。
- Thumbnail生成profileの変更や新しい派生データ種別の追加。
- TB以上専用のFile Size単位。
- Wi-Fiのscan・自動接続、SSID/BSSIDだけによるServer認証、未登録Wi-Fiを自動Backup対象に広げる変更。
- 今回の検証と無関係な既存User、File、Folder、DB row、物理File、派生データ、端末データの削除。

## 参照ドキュメント

- `AGENTS.md` - KuraStorage Codex Instructions
- `docs/product-requirements.md` - プロダクト要求定義書
- `docs/functional-design.md` - 機能設計書
- `docs/architecture-design.md` - アーキテクチャ設計書
- `docs/repository-structure.md` - Repository構造定義書
- `docs/development-guidelines.md` - 開発ガイドライン
- `.steering/20260824-search-recent-files/` - Search UIとNavigationの既存契約
- `.steering/20260828-favorites-tags/` - Favorites・Tagsの既存契約
- `.steering/20260829-android-media-viewers-players/` - Video・PDF・Media Sizeの既存契約
- `.steering/20260830-text-file-version-history/` - Text編集・version historyの既存契約
- `.steering/20260903-android-ui-mockup-alignment/` - Settings・Viewer・一覧UIの既存契約
- `.steering/20260905-android-ui-simplification/` - Content-first UI・Favorites thumbnail・Media操作の既存契約
