# KuraStorage MVP 要求定義

## 1. 概要

KuraStorageの最初の正式リリースとして、Raspberry Pi上のバックエンドとAndroidアプリを実装する。

MVPでは、LANまたはZeroTier経由で安全に接続し、ユーザー認証後に個人ファイルの一覧、手動アップロード、ダウンロード、ゴミ箱移動、復元を実行できる最小構成を完成させる。

自動バックアップ、共有、検索、メディア変換等はMVP完成後の機能追加とし、今回の実装範囲に含めない。

## 2. 背景

現在の正式文書には、ファイル管理、共有、自動バックアップ、サムネイル、動画変換、索引修復などがMVPとしてまとめて定義されている。この範囲のままでは、バックエンドとAndroidアプリを結合した最初の動作可能版が完成するまでの期間が長く、基本構成の問題を早期に検証できない。

そのため、最初のMVPを「接続・認証・安全な基本ファイル操作」に絞る。Raspberry Pi、HDD、PostgreSQL、Nginx、ZeroTier、Android実機を一連のシステムとして結合し、家庭内で実際に利用できる状態を先に作る。

## 3. MVPの目標

1. Raspberry PiとAndroidの実機間で、LANとZeroTierの両経路を用いてHTTPS接続できる。
2. 未登録Android端末をLocal Directから登録し、登録済み端末が認証・Token更新できる。
3. 認証済みUserが自分の領域だけで基本ファイル操作を実行できる。
4. HDD、DB、通信または認証の異常時に、誤保存、不正閲覧、不完全ファイル公開を起こさない。
5. ServerとAndroidのビルド、自動テスト、実機E2E確認、再現可能な配置手順を用意する。

## 4. 実装対象

### 4.1 正式文書のMVP再定義

- 最初の正式リリース名称は「MVP」に統一する。
- `docs/product-requirements.md`のMVP機能とリリース判定基準を今回の範囲へ絞る。
- `docs/functional-design.md`のデータモデル、API、画面、処理フロー、実装順序をMVPとMVP後に分類する。
- `docs/architecture-design.md`、`docs/repository-structure.md`、`docs/development-guidelines.md`を最小の実行Host、Module、配置、品質基準に合わせる。
- 後続機能は正式文書の「MVP後の機能追加」へ移し、今回の`tasklist.md`に実装タスクとして含めない。

### 4.2 Server実行基盤

- .NET 10でASP.NET Core APIとAdmin CLIを構成する。
- PostgreSQL 17に管理情報を保存する。
- NginxでTLSを終端し、KuraStorage APIを直接外部公開しない。
- Raspberry Piの専用HDDをファイルの正とし、HDD未マウント時の書き込みを拒否する。
- APIは非root Userで動作させる。
- Migration、Nginx、systemd、Firewall、設定Templateと配置・検証Scriptを用意する。

### 4.3 ZeroTier・接続経路

- Phase 1のリモート接続はZeroTierを使用する。
- ZeroTier daemon、Network参加、Member認可、Managed IP、Node IdentityはKuraStorage外で管理する。
- KuraStorageにZeroTier SDK、Controller API連携、接続・切断機能を実装しない。
- AndroidはLocal Directを優先し、利用できない場合にZeroTier経由の`REMOTE_SECURE`を確認する。
- LANとZeroTierで同じ`NET-API-HOSTNAME`を使用し、接続経路に応じて解決先IPを切り替える。
- ZeroTier未接続時は別アプリでの確認を案内し、KuraStorage復帰後に到達性を再確認できるようにする。

### 4.4 User・Device・Session管理

- Admin CLIからUserを作成できる。
- PasswordはArgon2id v1.3でハッシュ化し、平文または復号可能な形式で保存しない。
- 未登録Android端末のDevice登録は`LOCAL_DIRECT`からだけ許可する。
- ZeroTier経由の新規Device登録を拒否する。
- 登録済みDeviceはLocal DirectまたはZeroTierからログインできる。
- Access Tokenは15分、Refresh Tokenは発行・ローテーション時点から24時間有効とする。
- Refresh Tokenは使用ごとにローテーションし、DBにはハッシュだけを保存する。
- Refresh Token再利用を検知した場合は同じSession系列を失効する。
- AndroidでRefresh TokenをKeystore保護する。Access Tokenは原則メモリだけに保持する。
- Admin CLIからDevice一覧確認とDevice失効を実行できる。
- Device失効時は関連するRefresh Sessionを失効する。ZeroTier Member失効は独立した運用とする。

### 4.5 基本ファイル操作

- Userごとに分離された個人ファイル領域を作成する。
- ファイル・フォルダ一覧と詳細を取得できる。
- 個人領域にフォルダを作成できる。
- Android Storage Access Frameworkで選択したファイルを手動アップロードできる。
- アップロードはファイル全体をメモリに読み込まず、一時領域へストリーミングする。
- サーバーはサイズと、指定された場合のSHA-256を検証し、検証後だけ正式配置する。
- 通信中断したアップロードは最初から再試行する。分割転送と中断再開はMVP後とする。
- ダウンロードはHTTP Rangeに対応し、Android Storage Access Frameworkで選択した保存先へ出力できる。
- ファイルまたはフォルダをゴミ箱へ移動できる。
- ゴミ箱の項目を元の場所へ復元できる。復元先に同名項目がある場合は上書きせず拒否する。
- 物理絶対パスをAPIへ公開しない。
- 所有User以外のファイルIDを指定した操作を拒否する。
- シンボリックリンク、絶対パス、`..`、NUL文字等を拒否する。

### 4.6 Androidアプリ

- Android 10（API Level 29）以上に対応する。
- Jetpack Composeで接続状態、ログイン、ホーム、ファイル一覧、詳細、アップロード、ダウンロード、ゴミ箱の最小画面を実装する。
- 接続経路、認証、Device、HDDを独立した状態として表示する。
- TLS証明書またはホスト名の検証に失敗した場合は接続しない。
- ファイル操作の進捗、完了、再試行可能失敗、永久失敗を表示する。
- アプリ内の写真、動画、音声、PDF、テキスト表示はMVPで実装しない。ダウンロードしたファイルはOSの対応アプリで開ける。

### 4.7 品質・運用基盤

- ServerとAndroidのフォーマット、静的解析、ビルド、単体テストをCIで実行する。
- PostgreSQLとHDD操作を含むServer結合テストを実施する。
- AndroidのRepository、ViewModel、接続判定、Token保護の単体テストを実施する。
- Raspberry PiとAndroid実機でLAN、ZeroTier、再起動、異常系を含むE2Eを実施する。
- Release APKを生成し、対象Android端末へインストールできる。
- 秘密情報、実環境値、物理絶対パス、ファイル本文をログまたはリポジトリへ残さない。

## 5. 受け入れ条件

### 5.1 配置・接続

- [ ] Raspberry Piの再起動後にPostgreSQL、API、Nginxが必要な順序で起動する。
- [ ] APIが非root Userで動作する。
- [ ] HDDが正しくマウントされている場合だけ、ファイル書き込みを許可する。
- [ ] Androidが`NET-API-HOSTNAME`を使ってLANとZeroTierの両経路からHTTPS接続できる。
- [ ] TLS証明書またはホスト名が不正な場合、Androidが接続を拒否する。
- [ ] ZeroTier MemberからKuraStorage HTTPS以外のSSH、PostgreSQL、SMB、LAN端末、他のZeroTier端末へ到達できない。

### 5.2 認証・Device

- [ ] Admin CLIからUserを作成できる。
- [ ] 異なるUserに同じPasswordを設定しても、保存されるArgon2idハッシュが異なる。
- [ ] 未登録Android端末がLocal DirectからDevice登録できる。
- [ ] 同じ未登録端末がZeroTier経由でDevice登録できない。
- [ ] 登録済みDeviceがLANとZeroTierの両方からログインできる。
- [ ] Access Tokenが15分、Refresh Tokenが発行・ローテーションから24時間で失効する。
- [ ] Refresh Token再利用で対象Session系列が失効する。
- [ ] Logout後にRefresh Tokenが使用できない。
- [ ] Admin CLIからDeviceを失効すると、対象DeviceのRefreshと保護API呼び出しが拒否される。
- [ ] AndroidのRefresh TokenがKeystoreで保護され、通常の平文Preferenceへ保存されない。

### 5.3 ファイル操作

- [ ] 認証済みUserが自分のファイル・フォルダ一覧と詳細を取得できる。
- [ ] Userが自分の領域にフォルダを作成できる。
- [ ] Androidで選択したファイルを、ファイル全体をClientまたはServerのメモリへ読み込まずアップロードできる。
- [ ] 通信中断したアップロードの一時ファイルが通常一覧に表示されない。
- [ ] アップロード完了後のファイルサイズと内容が送信元と一致する。
- [ ] AndroidがファイルをStorage Access Frameworkの選択先へダウンロードできる。
- [ ] Range Requestに対して正しい範囲とHTTP Statusを返す。
- [ ] Userがファイルまたはフォルダをゴミ箱へ移動し、元の場所へ復元できる。
- [ ] 復元先に同名項目が存在する場合、既存ファイルを上書きせず競合エラーを返す。
- [ ] 他Userが所有するファイルIDを指定しても、存在の有無を過度に公開せず操作を拒否する。
- [ ] `..`、絶対パス、NUL、不正区切り文字、シンボリックリンクを使って専用ストレージ外へアクセスできない。
- [ ] HDD未マウント時にOSルートファイルシステムへ誤保存されない。

### 5.4 Android UI

- [ ] 接続確認中、Local Direct、ZeroTier経由、未接続、TLS失敗、HDD利用不可を区別して表示できる。
- [ ] ZeroTier未接続時に別アプリの確認案内と再確認操作を表示できる。
- [ ] 登録、ログイン、Token更新、ログアウトのフローを完了できる。
- [ ] ファイル一覧、フォルダ移動、詳細表示、フォルダ作成、アップロード、ダウンロード、ゴミ箱移動、復元を画面から実行できる。
- [ ] 大容量ファイル転送中に進捗を表示し、UI Threadを停止させない。
- [ ] 通信中断、Token失効、Device失効、HDD利用不可、権限拒否、復元競合を利用者が判別できる。

### 5.5 ビルド・テスト・実機確認

- [ ] ServerとAndroidのフォーマット、静的解析、ビルド、単体テストがCIで成功する。
- [ ] PostgreSQLと一時HDD領域を使うServer結合テストが成功する。
- [ ] Androidの主要画面と操作に対するUIテストが成功する。
- [ ] Raspberry PiとAndroid実機でLocal Direct登録、LANログイン、ZeroTierログイン、アップロード、ダウンロード、ゴミ箱移動、復元を一通り実行できる。
- [ ] Raspberry PiとAndroidをそれぞれ再起動した後も、再接続と再ログインを完了できる。
- [ ] Release APKを生成し、対象Android端末へインストールできる。
- [ ] 再現可能なインストール、起動、Migration、更新、ロールバックの手順が文書化されている。

## 6. 成功指標

- Android実機でアプリ起動からログイン、ファイル一覧表示まで10秒以内を目標とする。ただし、ネットワーク未接続や認証入力待ちの時間は除く。
- 正常なLANまたはZeroTier接続で、基本E2Eシナリオを10回連続して完了できる。
- ファイル漏えい、User間の認可突破、HDD未マウント時の誤保存、不完全ファイルの公開を0件とする。
- ServerとAndroidの必須CIをすべて成功させる。
- 主要な失敗状態を、利用者が次の操作を選べる形で表示する。

## 7. スコープ外

以下はMVP完成後に機能単位の別Steeringで計画する。

- Android自動バックアップ、WorkManager、保留キュー、信頼済みWi-Fiルール
- ファイル・フォルダ共有、共有権限継承
- 検索、カテゴリ表示、最近使用したファイル
- 名前変更、別フォルダへの移動
- ゴミ箱の完全削除、30日保持、自動清掃
- 中断位置から再開できる分割アップロード
- 写真・動画サムネイル、低・中画質キャッシュ
- 動画変換、音声再生、アプリ内写真・PDF・テキスト表示・編集
- 外部変更監視、定期全件再スキャン、`MISSING`判定、高度な自動復旧
- Webアプリ
- WireGuard、VPS経由VPN、その他のリモートアクセス方式

## 8. 制約・前提

- 正式文書の優先順位に従い、実装開始前にMVP再定義を正式文書へ反映する。
- `docs/environment-info.md`の`NET-LAN-CIDR`、`NET-ZEROTIER-API-IP`、`NET-ZEROTIER-CIDR`、`NET-ZEROTIER-NETWORK-ID`、`NET-ZEROTIER-CONTROLLER-TYPE`は、最終実機E2Eまでに実環境値へ更新する。
- 上記の実環境値が未確定でも、ローカル開発、Server結合テスト、Fake APIを使ったAndroid開発は進行可能とする。
- `docs/environment-info.md`はGit管理外とし、Password、Token、Private Key、ZeroTier Node Identity、ZeroTier認可情報を保存しない。
- 既存のAndroid Mockupは視覚参考とし、MVP対象画面と本要求を優先する。
- 実装はPull Request単位で進め、各Pull Requestの対象タスク、テスト、`tasklist.md`更新、Commit、Push、Pull Request作成、完了記録までを完了して停止する。

## 9. 参照ドキュメント

- `docs/product-requirements.md` - プロダクト要求定義
- `docs/functional-design.md` - 機能設計
- `docs/architecture-design.md` - アーキテクチャ設計
- `docs/repository-structure.md` - リポジトリ構造定義
- `docs/development-guidelines.md` - 開発ガイドライン
- `docs/environment-info.example.md` - 公開環境項目Template
- `docs/environment-info.md` - Git管理外のローカル環境情報
