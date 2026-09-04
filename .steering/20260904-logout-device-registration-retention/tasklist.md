# ログアウト後の端末登録維持 タスクリスト

## 対象要件

- 通常ログアウトではAccess Token、Refresh Token、認証済みUser情報、Session固有状態を破棄し、非秘密の端末登録Metadata（`deviceId`、前回Username）を保持する。
- 登録済み端末の次回起動は`Register this device`ではなく`Sign in`を表示し、保持した同一Device IDで再ログインする。
- Session期限切れ・失効・Keystore内Token喪失では端末登録を保持し、`DEVICE_REVOKED`または端末登録Metadata消失時だけ再登録へ戻す。
- ログアウトと再ログインを繰り返してもServer上のDeviceレコードを増やさず、前SessionのToken、画面、Media、Backup状態を再利用しない。
- `.steering/20260904-logout-device-registration-retention/requirements.md`と`design.md`を本タスクリストの実装根拠とする。

## タスク完全完了の原則

- 全タスクを最終的に`[x]`にし、親タスクはすべての子タスク完了後だけ完了にする。
- 本変更は認証状態の分離からUI・回帰テストまでが強く依存するため、1つのPull Request単位として完了する。
- 実装、テスト、実機確認、正式文書、Commit、Push、英語Pull Request、必須CI、steeringモード3-Aの完了記録まで終えて停止する。
- 「時間の都合」「難しい」「別タスク」を理由にスキップしない。技術的に不要となった場合だけ、理由と代替実装を該当タスクとPull Request完了記録へ明記する。
- Password、Access Token、Refresh Token、実User情報、実Server秘密値をTest fixture、Log、文書、Commitへ含めない。

## 確定済みスコープ境界

- [x] 前回Usernameを非秘密Metadataとして保持し、`Sign in`画面へ初期入力する。Passwordは保持しない。
- [x] 今回は「この端末の登録を解除」UI、複数Account切替、複数Server切替を追加しない。
- [x] 旧版Logoutで既に消去されたDevice IDは復元せず、更新後の初回だけ再登録を許容する。
- [x] 過去に作成済みの重複Deviceを自動統合・自動削除しない。
- [x] Serverの認証APIとDeviceモデルは変更せず、Logout後もDeviceが有効である既存契約を回帰Testで固定する。
- [x] 新しい依存Library、DB Migration、公開API Error codeを追加しない。

---

## フェーズ0: 計画承認

- [x] `requirements.md`を作成し、User承認を得る。
- [x] `design.md`を作成し、User承認を得る。
- [x] 本`tasklist.md`をUserが確認し、1 Pull Requestでの実装範囲と検証内容の承認を得る。

---

## PR1: Android端末登録と認証Sessionの分離

### 1.1 開始条件・正式文書

- [x] PR1の開始条件を満たす。
  - [x] フェーズ0が完了している。
  - [x] `git status`と既存差分を確認し、Userの変更を保持したまま最新`main`から短命Branchを作成する。
  - [x] 本作業の`requirements.md`、`design.md`、`tasklist.md`と、正式文書の認証・Logout・Session Scope・Backup Account Scope関連節を再確認する。
  - [x] `CredentialMetadataStore`、`DefaultAuthenticationRepository`、`AuthViewModel`、`MainActivity`と既存Testの類似実装を再確認する。
- [x] 正式文書を新しいLogout契約へ整合させる。
  - [x] `docs/product-requirements.md`に、通常LogoutではSession秘密値を破棄し、非秘密の端末登録Metadataを保持する受け入れ条件を明記する。
  - [x] `docs/functional-design.md`に、未登録・登録済み未認証・Refresh可能・Device失効の起動時状態遷移と再ログインFlowを記載する。
  - [x] `docs/architecture-design.md`に、端末登録Metadata、暗号化Refresh Token、Session Metadataの保存境界と破棄条件を記載する。
  - [x] `docs/development-guidelines.md`に、Session失効とDevice失効を区別する実装・Test規約を記載する。
  - [x] 正式文書間で、Password非保存、Local Direct限定登録、Logout後のSession Scope破棄、Backup Account Scope分離が矛盾しないことを確認する。

### 1.2 認証Model・Metadata Store

- [x] 端末登録と認証SessionのModelを分離する。
  - [x] `DeviceRegistrationMetadata`にDevice IDと前回Usernameだけを保持する。
  - [x] `SessionMetadata`にUser ID、Role、Refresh Token有効期限だけを保持し、Refresh Token本体を含めない。
  - [x] `StoredCredential`との変換境界を整理し、端末登録の存在をRefresh可能性と混同しないAPIにする。
- [x] `CredentialMetadataStore`を登録情報とSession情報の独立操作へ変更する。
  - [x] 登録MetadataとSession Metadataを個別にread/writeできるInterfaceを定義する。
  - [x] `clearSession()`が`user_id`、`role`、`refresh_token_expires_at`だけを単一DataStore transactionで削除する。
  - [x] `clearRegistration()`が登録・Session両方のMetadataを単一DataStore transactionで削除する。
  - [x] 既存の`device_id`、`last_username`、`user_id`、`role`、`refresh_token_expires_at` keyを再利用し、既存インストールの保存値を破壊しない。
  - [x] 欠落・不正なDevice IDは未登録、不完全または不正なSession Metadataは登録済み未認証として安全に扱う。
- [x] `CredentialMetadataStoreTest`を更新する。
  - [x] 登録・Session Metadataの独立read/writeと、通常Logout相当の`clearSession()`後にDevice IDとUsernameだけが残ることを検証する。
  - [x] `clearRegistration()`後は登録・Session Metadataがすべて消えることを検証する。
  - [x] 旧key一式の読取互換、不完全Session、Role不正、Device ID不正を検証する。

### 1.3 Authentication Repository

- [x] `AuthenticationRepository`で登録有無とRefresh Credential有無を別々に取得できるようにする。
  - [x] `storedRegistration()`からRefresh TokenなしでDevice IDと前回Usernameを取得できるようにする。
  - [x] `storedCredential()`はSession Metadata、有効期限、暗号化Refresh Tokenが揃う場合だけRefresh可能なCredentialを返す。
  - [x] 期限切れ、Keystore読取失敗、Refresh Token欠落ではSessionだけを破棄し、登録Metadataを保持する。
- [x] Register・Login・Refresh成功時の永続化を更新する。
  - [x] Register成功時に端末登録、暗号化Refresh Token、Session Metadataを保存し、途中失敗時に利用不能なSession秘密値を残さない。
  - [x] Loginは保存済み登録のDevice IDを必須とし、Refresh TokenがないLogout後も同じDevice IDで`POST /auth/login`を呼ぶ。
  - [x] Login応答のUser ID形式とDevice ID、Refresh応答のUser IDとDevice IDを保存済み状態に照らして検証し、不一致時に認証済み状態を成立させない。
  - [x] Login・Register・Refresh成功後のAccess Token、Role、User ID、Device ID accessorを現行どおり認証中だけ利用可能にする。
- [x] LogoutとError別破棄処理を更新する。
  - [x] Logout API成功・通信失敗の両方でメモリSession、暗号化Refresh Token、Session Metadataを必ず破棄する。
  - [x] 通常Logout後も端末登録のDevice IDとUsernameを保持する。
  - [x] `AUTHENTICATION_REQUIRED`、Refresh Token期限切れ、`REFRESH_TOKEN_REUSED`ではSessionだけを破棄する。
  - [x] `DEVICE_REVOKED`と不正な登録MetadataではSessionと端末登録をすべて破棄する。
- [x] `AuthRepositoryTest`を拡充する。
  - [x] Logout成功・通信失敗後にToken、Role、User ID、メモリDevice IDが消え、永続Device IDとUsernameだけが残ることを検証する。
  - [x] Logout後のLoginが同じDevice IDをRequestへ使用し、新規登録を呼ばないことを検証する。
  - [x] 期限切れ、Token欠落、Keystore喪失、`AUTHENTICATION_REQUIRED`、`REFRESH_TOKEN_REUSED`で登録だけが残ることを検証する。
  - [x] `DEVICE_REVOKED`、不正Device ID、応答User/Device不一致、永続化途中失敗で安全な状態へ収束することを検証する。
  - [x] 同時401時のRefresh単一化とToken rotationの既存Testを維持する。

### 1.4 Auth UI・Session Scope

- [x] `AuthViewModel`の起動時状態遷移を登録状態とSession状態に分離する。
  - [x] 登録なしの場合だけ、LOCAL_DIRECTでは`Register this device`、REMOTE_SECUREではLocal Direct案内を表示する。
  - [x] 登録あり・Credentialなしの場合は接続Routeにかかわらず`Sign in`を表示し、保持Usernameを初期入力する。
  - [x] 有効Credentialがある場合だけRefreshを試行し、成功時は認証済み画面へ遷移する。
  - [x] Session系Refresh失敗は操作可能な`Sign in`へ収束させ、Passwordを空のままにする。
  - [x] `DEVICE_REVOKED`だけ登録Metadataを破棄した再登録Flowへ遷移させる。
- [x] `AuthScreen`の表示と入力保護を確認し、必要最小限修正する。
  - [x] 登録済み未認証状態では`Sign in`を表示し、Usernameのみ初期入力する。
  - [x] PasswordをState復元、Semantics、Log、Screenshot fixture、DataStoreへ保存しない。
  - [x] Session失効・Token再利用検知の理由と再Login操作を同じ画面で理解できる表示にする。
- [x] Logout後のApp Session Scope破棄を確認し、必要な場合だけ修正する。
  - [x] `MainActivity`と`ServiceContainer`が前SessionのDI container、Back Stack、Media context、Backup UI状態を破棄する。
  - [x] 新しい認証Flowが閉じたRepositoryのメモリSessionを流用せず、永続登録Metadataを再読込する。
  - [x] Logout後に前UserのFiles、Shared、Search、Recent、Tags、Favorites、Activity、Backup、Media画面へ戻れない。
- [x] `AuthViewModelTest`と`AuthScreenTest`を更新する。
  - [x] 未登録LOCAL_DIRECT、未登録REMOTE_SECURE、登録済み未認証、有効Credentialの4起動状態を検証する。
  - [x] Refresh期限切れ・Session失効・Token再利用検知は`Sign in`、Device失効は再登録へ遷移することを検証する。
  - [x] Username初期入力、Password非保持、二重送信防止、Inline error、Local Direct登録制約を検証する。
  - [x] Logout後のNavigationとSession Scope分離についてApp testを追加または更新する。

### 1.5 Server契約・回帰Test

- [x] Android・Server間の既存認証契約が変更されていないことを確認する。
  - [x] `register-device`、`login`、`refresh`、`logout`のRequest/Responseと既存Error codeを変更しない。
  - [x] `KuraStorageApiContractTest`でLoginに保持Device ID、Logoutに現在Device IDとRefresh Tokenを送る既存契約を維持する。
- [x] Server認証回帰Testを補強する。
  - [x] `PostgreSqlAuthFlowTests`で登録直後のDevice IDとDevice件数を記録する。
  - [x] Logout後も対象DeviceがACTIVEで、Refresh Sessionだけが失効していることを確認する。
  - [x] 同じUsername、Password、Device IDで再Loginでき、Device IDとDevice件数が不変であることを確認する。
  - [x] Device失効後のLoginまたはRefreshが`DEVICE_REVOKED`となる既存Security契約を維持する。
  - [x] Logout、再Login、Device失効、Refresh Token再利用検知の監査契約を弱めていないことを確認する。

### 1.6 自動検証・実機E2E

- [x] 自動品質確認を完了する。
  - [x] `./scripts/ci/verify-android.sh`が成功する。
  - [x] `./scripts/ci/verify-server.sh`が成功する。
  - [x] `./scripts/ci/verify-security.sh`が成功する。
  - [x] `./apps/android/gradlew -p apps/android :core-data:connectedDebugAndroidTest :feature-auth:connectedDebugAndroidTest :app:connectedDebugAndroidTest --no-daemon --no-configuration-cache`がAndroid実機またはEmulatorで成功する。
  - [x] `git diff --check`が成功する。
- [x] 実Android端末と実ServerでLogout・再起動・再Loginを確認する。
  - [x] LOCAL_DIRECTで限定Test Deviceを1回登録し、Server DB上のUser、Device ID、Device件数を秘密値を出力せず記録する。
  - [x] Logout直後にServerのRefresh Sessionが失効し、DeviceがACTIVEのままであることを確認する。
  - [x] アプリを強制終了・再起動し、`Register this device`ではなく`Sign in`と前回Usernameが表示され、Passwordが空であることを確認する。
  - [x] Passwordで再Loginし、同じDevice IDでFiles・共有・設定へアクセスできることを確認する。
  - [x] Logout・再起動・再Loginを3回繰り返し、Device IDとDevice件数が不変であることを確認する。
  - [x] Logout後に前Sessionの保護画面、Media状態、Backup状態へ戻れず、Token・Role・User IDが再利用されないことを確認する。
  - [x] 限定Test DeviceをServer側で失効し、次回Login/Refresh後に再登録Flowへ戻ることを確認する。
- [x] 検証記録を`docs/testing/20260904-logout-device-registration-retention.md`へ記載する。
  - [x] 実行環境、実行コマンド、合否、確認した状態遷移、匿名化したDevice件数の前後を記録する。
  - [x] 旧版でLogout済みの端末は初回再登録が必要である互換上の制約を記録する。
  - [x] 実User名、Password、Token、Device ID、接続先秘密値、物理保存Pathを記録しない。

### 1.7 PR1完了

- [x] PR1の差分をself-reviewする。
  - [x] 要件・設計・正式文書・実装・Testが一致し、通常LogoutとDevice失効の破棄範囲が逆転していない。
  - [x] Password・Token・実User情報・Device ID・秘密値・debug code・無関係な変更が含まれていない。
  - [x] 新しい依存Library、DB Migration、Server Production変更、不要な公開API変更が含まれていない。
- [ ] PR1を作成し、必須CIを完了する。
  - [x] 今回選択したPR1の実装・文書・Testタスクがすべて`[x]`になっている。
  - [ ] 変更をCommitし、作業BranchをRemoteへPushする。
  - [ ] 目的、対象タスク、変更内容、Test結果、影響・未実施事項を英語で記載したPull Requestを`main`向けに作成する。
  - [ ] 必須CIが成功し、Pull RequestをMergeせずに停止する。
- [ ] steeringスキルのモード3-AでPR1完了記録を本ファイルへ追加する。
  - [ ] 完了日、Pull Request番号/URL、実施した自動・実機確認、計画との差分、追加タスク、不要化タスク、引継ぎ事項を記録する。
  - [ ] 該当しない項目にも「なし」と記載する。
  - [ ] 完了記録をCommit・Pushし、作成済みPull Requestへ反映されたことを確認する。
- [ ] 全タスク完了後にsteeringスキルのモード3-Bで全体振り返りを記録する。

---

## 各Pull Request完了記録

PR作成後、steeringスキルのモード3-Aに従ってここへ記録する。

## 全体振り返り

全タスクとPR1完了記録が完了した後、steeringスキルのモード3-Bに従ってここへ記録する。
