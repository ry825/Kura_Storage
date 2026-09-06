# 自動検証記録（2026-09-06）

## 結果

- Android変更対象JVM Unit Test: 成功（306 tasks、failure 0）。
- API 33 Emulator Instrumented Test: 成功（app 10、core-data 17、feature-files 31、feature-media 23、feature-backup 11、feature-settings 11、合計103件）。
- Android 13実機 Instrumented Test: 成功（初回全体はapp 10、core-data 17、feature-files 31、feature-media 23、feature-backup 11、feature-settings 11、合計103件）。追加修正後はfeature-files 33件、feature-backup 12件が成功した。
- Server標準検証: 成功（Domain 135、Application 355、Integration 236、合計726件、build warning/error 0）。
- Android標準検証: 成功（追加修正後の最終再実行は1392 tasks、build、Unit Test、coverage gate、ktlint、detekt、Android Lint、SBOM）。
- Configuration標準検証: 成功。
- Security標準検証: 成功。
- Deployment標準検証: 成功。権限制限環境のため、systemdはPostgreSQL service metadataなし、nftablesはkernel ruleset accessなし、Nginxはlisten socket accessなしで構文解析までを確認した。

## 実行環境上の補足

- Android buildはTemurin JDK 17とAPI 36を含む既存Android SDKを使用した。
- Hostへpackageを導入する権限がないため、`shellcheck`と`nginx`は取得したDebian packageを`/tmp`へ展開して検証時だけ`PATH`へ追加した。Nginxの`mime.types`も同じ展開先を明示した。
- CycloneDXは`androidx.media3:media3-ui-compose:1.11.0`のeffective POM補完警告を出したが、SBOM生成タスクとAndroid標準検証は成功した。
- Security検証が一時資格情報の変数名を固定値の秘密情報と誤認しないよう、Thumbnail benchmark内の生成値を`generated_credential`として表現した。値は実行時生成のままで、Repositoryや本証跡へ保存していない。

## File browser Header測定

- 測定viewportは変更前後とも360 x 800 dp、通常font scaleとした。
- 変更前（`912e3bb`）は64 dpのTopAppBarの下に、line height 20 dpの独立したPath行と12 dpのColumn間隔があり、一覧前のHeader領域は合計96 dpだった。
- 変更後のrootはPathをTopAppBar内へ統合し、API 33 EmulatorのCompose実測で`browser-header`は64 dpだった。
- 同一viewportのroot Headerを32 dp（33.3%）削減し、一覧に使える縦領域を32 dp増やした。
- child Folderでは64 dp TopAppBarの下へ全階層を折り返すBreadcrumbを配置する。深いPathでは高さを増やして全Linkを優先し、切り捨てない。
- 各navigation/action/Breadcrumb操作は48 dp以上のtouch targetを維持する。追加修正後のFeature Files Instrumented Test 33件は、360 dp・font scale 2.0・5階層を含めて成功した。

## Android 13実機の追加確認

- 既存の署名と一致する非debugのRelease APK（version code 24）を上書きし、保持された認証状態でZeroTier、TLS、Server identity、User/Device/Session認証が成功することを確認した。
- Wi-Fi権限の拒否時にBackup停止案内が維持されること、許可後に現在のSSIDと利用可能なBSSIDがFormへ反映されることを確認した。識別子の実値は記録していない。
- 実機で、Android 12以降の`NetworkCapabilities.transportInfo`が位置情報をredactした値を返す場合を検出した。権限確認済みの`WifiManager.connectionInfo`を安全な候補としてfallbackする修正とUnit Testを追加し、実機で再確認した。
- Release APKの初回検証時にテスト用CAと稼働ServerのCA不一致を検出した。稼働版の公開CAへ差し替え、証明書検証が成功することを確認した。証明書やendpointの実値はRepositoryと本証跡に保存していない。

## 後続の実機・実Server検証

- Raspberry Piへ新Server buildを配備し、Thumbnail並列負荷、DB状態、動画Range、Backup並列を照合した。
- 動画startup／Surface frame、端末memory、Server CPU／I/O、Backup並列性能を測定した。
- manifest限定清掃まで完了した。詳細は`physical-verification-20260906.md`と`docs/testing/20260906-android-video-playback-backup.md`を参照する。
