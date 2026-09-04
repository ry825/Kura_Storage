# Android UI PR10 最終実機E2E

## 対象と判定基準

- Candidate: `0.13.0-pr10-rc1`（Server）と同一sourceから作成した署名済みAndroid release相当build
- 物理端末: OPPO CPH2333、Android 13 / API 33、1080 x 2412 px、density 480（360 dp幅）
- 端末条件: Display cutout 150 x 118 px、縦/横、Light/Dark、通常文字/OEM最大160%、TalkBack
- 正式仕様: `docs/product-requirements.md`、`docs/functional-design.md`を正とし、`docs/ui/android/mockups/`は情報階層、配色、余白、形状の参考とした。
- 200%文字は端末Settingsの上限が160%だったため、同じAPI 33物理端末で実行したCompose fixtureの`fontScale = 2.0`で補完した。

## 36画面の最終追跡表

「自動」は各moduleのJVM/Compose/Navigation testとPR10で成功した全module connected suiteを示す。「手動」は本記録の実機Capture、または各UI PRで保存した決定的Captureを示す。

| No. / 参考UI | Production owner | 対象状態 | 自動Test | 手動Capture | 正式仕様に基づく意図的差分 |
| --- | --- | --- | --- | --- | --- |
| 001 Splash | `app` / system splash・`KuraStorageApp` | cold start、Light/Dark | app startup/navigation | PR3 record、実機cold start | 固定待機と装飾背景を追加しない。 |
| 002 Connection check | `feature-connection` | checking、progress | connection Compose | PR3 record | 実際の到達性判定だけを表示する。 |
| 003 Local status | `feature-connection` | `LOCAL_DIRECT`、HDD available | connection/app | `01-home-local-dark.png` | SSID推測ではなく検証済みrouteを表示する。 |
| 004 Disconnected | `feature-connection` | unreachable、retry | connection/app | `03-connection-offline-dark.png` | TLS、Server、Storage等の失敗理由を区別する。 |
| 005 Remote status | `feature-connection` | `REMOTE_SECURE` | connection/app | `02-home-remote-dark.png` | Legacy VPN操作をZeroTier別アプリ案内と再確認に置換する。 |
| 006 Login | `feature-auth` | input、loading、error | auth/app | PR3 record、実機login | PasswordをCapture、Semantics、Logへ出さない。 |
| 007 Initial setup | `feature-auth` | local device registration | auth/app | PR3 record、実機登録 | 登録は`LOCAL_DIRECT`限定。固定Device名を使わない。 |
| 008 Registration error | `feature-auth` | remote/limit/failure | auth/app | PR3 record | remote経路で登録操作を有効にしない。 |
| 009 Home | `app` / `HomeScreen` | connection、backup、recent、admin/member | app | `01`、`02`、`08`〜`11` | 実件数・実Userを表示し、木々装飾と固定sampleを使わない。 |
| 010 My files | `feature-files` / browser | list/grid、empty、paging、error | files/app | `04-files-dark.png` | 実Entryと権限だけを表示する。Folder件数はAPI非提供。 |
| 011 Recent | `feature-search` | recent、empty、paging | search/app | PR5 record、実機Home/Search | 独立複製画面でなく正式Recent queryを使用する。 |
| 012 Shared | `feature-sharing` | shared、empty、permission | sharing/app | PR5 record、実機empty | 所有者・権限を実APIから表示する。 |
| 013 Category | `feature-search` | photo/video/audio/document | search/app | PR5 record、実機Home category | 固定件数を持たずcategory searchへ遷移する。 |
| 014 Search | `feature-search` | query/filter/result/error | search/app | `05-search-results-dark.png` | Client全件filterをせずServer pagingを使用する。 |
| 015 Settings | `feature-settings` + app hub | member/admin、navigation | settings/app | PR9 record、実機member/admin | 管理操作はAdminだけに表示する。 |
| 016 Photo viewer | `feature-media` + app route | quality、loading、error | media/app | PR6 record、2026-08-30 physical media record | Low/Medium/Originalの契約名を使い、解像度を固定表示しない。 |
| 017 Video player | `feature-media` + app route | play/seek/speed/quality | media/app | PR6 record、2026-08-30 physical media record | 実durationと生成状態を表示する。 |
| 018 Audio player | `feature-media` + app route | play/seek/background | media/app | PR6 record、2026-08-30 physical media record | 音声はOriginalだけでLow/Mediumを合成しない。 |
| 019 PDF viewer | `feature-media` + app route | confirm/download/render/page/zoom | media/app | PR6 record、2026-08-30 physical media record | private一時Fileへstreamし、常時多page thumbnailを作らない。 |
| 020 Text editor | `feature-text` + app route | view/edit/save/conflict/unsaved | text/app | `06-text-viewer-dark.png`、実機unsaved dialog | 競合時に端末内容で暗黙上書きしない。 |
| 021 Unsupported file | `feature-files` | unknown MIME、download | files/app | PR4 record | 危険なOpenを合成せず、type/MIME/理由を表示する。 |
| 022 File details | `feature-files` | metadata、authorized actions | files/app | PR4 record、実機Files | 物理Pathを表示しない。 |
| 023 Folder details | `feature-files` | metadata、authorized actions | files/app | PR4 record | APIにない子件数は捏造しない。 |
| 024 Sharing settings | `feature-sharing` | owner/manager、empty/error | sharing/app | PR5 record | 実Permissionとshare状態だけを表示する。 |
| 025 Share permissions | `feature-sharing` | add/change/revoke、confirm | sharing/app | PR5 record | Owner/Manager/ViewerのServer認可を弱めない。 |
| 026 Server folder picker | `feature-files` + app/backup callback | breadcrumb、permission、selection | files/backup/app | PR4/PR9 records | 作成権限のないFolderを選択確定できない。 |
| 027 Transfer status | `feature-files` | queued/hash/upload/download/pause/retry | files/app | PR4 record、実機upload snackbar | 結果不明時に成功を合成しない。 |
| 028 Trash | `feature-files` | restore/purge/admin warning | files/app | PR4 record | Server保持期限と不可逆性を表示する。 |
| 029 Missing | `feature-files` | candidate/missing/recheck/remove index | files/app | PR4 record | 物理Pathを隠し、確定Missingだけを索引削除可能にする。 |
| 030 Backup status | `feature-backup` + app | overview、history、progress/error | backup/app | PR9 record、実機Settings | WorkManagerの永続状態を表示し、架空progressを作らない。 |
| 031 Backup rules | `feature-backup` | list/disabled/error | backup/app | PR9 record | SAF permissionとServer destinationを実Ruleから表示する。 |
| 032 Rule editor | `feature-backup` | source/destination/policy/save | backup/app | PR9 record | pickerはapp境界へ委譲し、秘密値を保持しない。 |
| 033 Trusted Wi-Fi | `feature-backup` | permission/list/empty | backup/app | PR9 record | SSID/BSSIDは許可後だけ扱い、Log/Semanticsへ出さない。 |
| 034 Wi-Fi editor | `feature-backup` | register/edit/remove | backup/app | PR9 record | 位置情報権限拒否を説明し、設定値を推測しない。 |
| 035 Quality/network | `feature-settings` | route別quality、save/reset/error | settings/app | PR9 record | Local、Wi-Fi+ZeroTier、Mobile+ZeroTierを正式名称で扱う。 |
| 036 Cache management | `feature-settings` + admin cache API/Worker | status、confirm、pending/running/completed/error | settings/data/server | `07-cache-dark.png`、実機manual cleanup | Thumbnailを10 GiB対象へ含めず、未契約の失敗一括retryを追加しない。 |

## 参考UIのない正式画面

- Favorites、Tags、Entry organization、ActivityはPR5のDesign system採用画面と同じTheme、card、heading、status、48 dp操作領域を使用し、Server paging・認可・実データを維持した。
- Rename、Move、Share、Trash、Purge、unsaved edit、original download、cache cleanupの確認Dialogは、対象、影響範囲、取消し、実行を明示する共通構造を使用した。
- Production Android sourceにPNG/JPEG/WebPはなく、参考画像の背景化・複製、木々装飾、未実装button、固定sample値は検出されなかった。

## 物理端末E2E

1. Release相当APKをinstallし、実Server `0.13.0-pr10-rc1`へ`LOCAL_DIRECT`で接続した。Member login、Home、Files、Shared empty、Search、Text閲覧を確認し、編集後Backのunsaved dialogでDiscardした。
2. 一時Admin test accountを`LOCAL_DIRECT`で端末登録した。FilesへText、Video、Audio、PDFをAndroid DocumentsUIからuploadし、各完了SnackbarとServer一覧反映を確認した。
3. Admin CacheはREADY 0 MiB、上限10 GiB、目標6 GiBと内訳を取得した。確認Dialogから手動清掃を要求し、永続runがCompleted（examined 0、removed 0、pending/running 0）へ収束した。元File、Thumbnail、生成中、lease中を削除しない説明も確認した。
4. ZeroTier networkを有効にしてWi-Fiを切断し、`REMOTE_SECURE`とStorage availableを確認した。ZeroTierも無効にしてcold startし、unreachable理由、別アプリ案内、Check againを確認した。Wi-Fi復帰後は`LOCAL_DIRECT`へ戻り、Sessionを保持した。USB debugの一時切断・再接続後もappを再開できた。
5. 画面回転、background/foreground、System Dark/Light、TalkBack on/offを切り替えた。横画面はHomeを有界2列へ再配置し、cutout/system barに重ならずscroll可能だった。TalkBackはConnection cardへ意味単位でfocusし、focus trapはなかった。
6. 端末のSystem font最大160%でHome、Bottom navigation、主要操作へscroll到達できた。正確な200%は全module connected suite内の決定的Compose fixtureで確認した。
7. 写真/PDF/Video/Audioの実File、品質変更、Original転送確認、再生、回転、background/foreground、通信断/再接続は同じAndroid 13端末と実Serverを使った`docs/testing/20260830-android-media-integration-pr4.md`の物理E2Eも回帰根拠とした。PR10の全module connected suiteはその後のUI構造を360 dp、200%、Light/Dark、横画面で再検証した。

## Adaptive・Accessibility・コントラスト

| 条件 | 結果 |
| --- | --- |
| 360 dp縦 / cutout | 主要画面、FAB、Snackbar、Bottom navigationに欠落・重なり・意図しない横切れなし |
| 2412 x 1080横 | Homeが有界2列へ再配置され、主要導線へscroll可能 |
| 160%実機文字 | 長いlabelはwrapし、主要操作へscroll可能 |
| 200% fixture | 共通componentと各Featureの主要画面・Dialogで縦再配置と操作到達を確認 |
| TalkBack | Title、heading、状態、list item、主/補助操作の意味順、selected/state/progress、icon descriptionを確認 |
| Light/Dark | 本文、補足、button、status、outlineを確認。実機で発見したCache title/section headingのDark低コントラストを修正しpixel回帰testを追加 |
| 非色依存 | Success、Warning、Error、Permission、Offlineはlabelとicon/shapeを併用 |

## Screenshot index

- `01-home-local-dark.png`: 360 dp、Dark、LOCAL_DIRECT Home
- `02-home-remote-dark.png`: Wi-Fi切断、ZeroTier REMOTE_SECURE Home
- `03-connection-offline-dark.png`: Wi-Fi/ZeroTier切断、到達不可と復旧案内
- `04-files-dark.png`: 実ServerのFile一覧
- `05-search-results-dark.png`: 実Server検索結果
- `06-text-viewer-dark.png`: Text viewer/editor導線
- `07-cache-dark.png`: 修正後のAdmin cache title/section contrast
- `08-home-landscape-dark.png`: 2412 x 1080横画面
- `09-home-font160-dark.png`: 端末System font最大160%
- `10-home-talkback-dark.png`: TalkBack accessibility focus
- `11-home-light.png`: System Light theme

## 実機性能とログ

Home、Files、Search、Settingsを巡回する直前に`gfxinfo reset`し、131 frameを採取した。中央値15 ms、p90 23 ms、p95 32 ms、p99 65 ms、janky 12/131（9.16%）、slow bitmap upload 0だった。短い自動巡回には画面遷移直後のframeを含むが、操作を妨げる停止や継続的なjankはなかった。

同じprocessのmemoryはtotal PSS 107,847 KiB、RSS 226,332 KiB、Java heap 9,888 KiB、native heap 18,436 KiB、graphics 45,416 KiBだった。WebViewは0、OpenSSL socketは4で、画面巡回後に際限なく増える資源は観測しなかった。

現在のapp PIDに限定したlogcatとactivity process stateを、`AndroidRuntime`、`FATAL EXCEPTION`、`ANR`、`StrictMode`、fatal signal、process death、TLS/DNS/connect/timeout例外で検索した。該当は0件だった。LOCAL_DIRECT、REMOTE_SECURE、意図的offline、復旧後のいずれにも自動再試行loopや明らかなnetwork regressionはなかった。

## 自動検証

| Gate | 結果 |
| --- | --- |
| `./scripts/ci/verify-android.sh` | 成功、1,387 actionable tasks（Build、JVM tests、Coverage、ktlint、Detekt、Lint、Debug/Release APK、AndroidTest APK、CycloneDX SBOM） |
| 全module `connectedDebugAndroidTest --max-workers=1` | 変更後sourceをAndroid 13物理端末で再実行し10分03秒で成功。app 8、core-data 10、core-database 11、core-ui 5、activity 2、auth 6、backup 11、connection 5、files 23、media 14、search 11、settings 10、sharing 6、text 8 |
| PR10修正focused connected tests | core-ui 5/5、settings 10/10成功 |
| `./scripts/ci/verify-server.sh` | Domain 135、Application 353、Integration 229、失敗0 |
| config / security / deployment | すべて成功。sandboxではsocket/netlink lookupを行わず、syntax/grammarを検証 |
| `git diff --check` | 成功 |

CycloneDXのMedia3 effective-POM warningは既存の非fatal警告で、SBOM生成とgateは成功した。

## 計画との差分と残存不具合

- 実機Dark themeでCache画面titleと共通section headingの色継承により低コントラストを発見した。`onBackground`を明示し、Dark themeのpixel regression testを追加して同一端末とconnected suiteで再確認した。
- 物理端末のSystem font UIは160%が上限だった。160%を実機で確認し、要求する200%は同じ物理端末上のCompose fixtureで補完した。
- Formal docs 5文書を最終実装と照合した。VPNの出現は将来候補またはLegacy mockup filenameの説明だけで、現行UIはZeroTierに統一されている。Cache API、Worker、Android module/path、公開境界にも矛盾はなかったため正式文書変更は不要だった。
- 一時的な資格情報を含む端末dumpとdebug署名passwordファイルは検証後に削除した。Repository差分と証跡にはendpoint、credential、token、非公開network値を含めていない。
- 受け入れを妨げる残存不具合はない。
