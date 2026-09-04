# ログアウト後の端末登録維持 要求内容

## 概要

Androidアプリで通常ログアウトした後も、Serverが発行した非秘密の`deviceId`と検証済みServerとの端末登録対応を保持する。次回起動時は「Register this device」ではなく「Sign in」を表示し、同じ登録済みDeviceとして再ログインできるようにする。

## 背景

現行実装は、ログアウト時にAccess TokenとRefresh Tokenだけでなく、端末Metadataの`deviceId`も削除する。`AuthViewModel`は保存済みCredentialがない場合を未登録端末とみなすため、次回起動時に再度「Register this device」を表示する。

Server側の通常ログアウトはRefresh Sessionを失効するがDevice自体は失効しないため、同じ物理端末で再登録を繰り返すと、Server上に複数のDeviceが作成される。これにより端末一覧の重複、監査情報の分散、端末上限への到達が起こり得る。

`deviceId`は単独で認証に使用しない識別子であり、TokenやPasswordと同じ秘密情報ではない。通常ログアウトでは認証Sessionと秘密情報を破棄しつつ、失効していない端末登録を再ログインに利用できる必要がある。

## 対象ユーザーフロー

### 1. 通常ログアウト

1. 登録済みDeviceでログインしている利用者がログアウトする。
2. Androidは可能な場合にServerの現在Refresh Sessionを失効させる。
3. AndroidはAccess Token、Refresh Token、認証済みUser情報、認証済みSession固有のCacheと画面状態を破棄する。
4. Androidは検証済みServerに対応する`deviceId`と、再ログインに必要な非秘密Metadataを保持する。
5. 次回起動時は「Sign in」を表示し、Password検証とServer側のDevice状態確認に成功した場合だけ新しいSessionを開始する。

### 2. 端末登録が無効な場合

- Server側でDeviceが失効されている場合は、保存済み`deviceId`を再ログインに利用し続けない。
- `DEVICE_REVOKED`を受信した場合は端末登録Metadataを破棄し、Local Direct接続中だけ「Register this device」を表示する。
- アプリデータ削除などで端末登録Metadata自体が存在しない場合も、未登録端末として扱う。
- Refresh Tokenの期限切れ、ログアウト済み、またはRefresh Session失効だけではDevice登録を破棄せず、「Sign in」へ収束させる。

### 3. 再登録と端末増殖の防止

- 通常ログアウト後の再ログインで`register-device`を呼び出さない。
- 同じ物理端末で「登録→ログアウト→ログイン」を繰り返しても、Server上のDevice件数が増えない。
- `deviceId`は必ずPassword検証、User所有関係、Device有効状態と組み合わせて使い、単独でログインを成立させない。

## 受け入れ条件

### Androidの認証状態

- [ ] 初回の未登録端末では、Local Direct接続中だけ「Register this device」を表示する。
- [ ] 登録済みDeviceから通常ログアウトした後、アプリを再起動すると「Sign in」を表示する。
- [ ] ログアウト後の「Sign in」は保持した`deviceId`を使用し、同じServer Deviceで認証する。
- [ ] Refresh Token期限切れ、Session失効、アプリProcess再生成、端末再起動の場合も、有効な端末登録Metadataがあれば再登録ではなく再ログインを要求する。
- [ ] `DEVICE_REVOKED`または端末登録Metadata消失時だけ、再登録フローへ遷移する。

### ログアウトとデータ保護

- [ ] ログアウト時にAccess Token、Refresh Token、Role、User ID、Session固有のMedia Cache、保護画面のBack Stackを破棄する。
- [ ] ログアウトはServerの現在Refresh Sessionを従来どおり失効させる。
- [ ] ServerへのLogout要求が通信エラーになっても、Android上のTokenと認証済みSession状態は必ず破棄し、端末登録Metadataだけを保持する。
- [ ] 保持した`deviceId`を認証秘密情報または認可根拠として扱わない。
- [ ] ログアウト後に前Userのファイル、共有、検索、履歴、Tag、お気に入り、自動バックアップ、Media状態を表示または操作できない。

### Serverと回帰防止

- [ ] 通常ログアウトでDevice自体を失効または削除せず、対象Refresh Session系列だけを失効させる。
- [ ] 登録済みDeviceの再ログインで新しいDeviceレコードを作成しない。
- [ ] 同じDeviceで複数回ログアウトと再ログインを行ってもDevice件数が不変であることを自動Testと実Serverで確認する。
- [ ] Logout、Login、Device失効の監査契約と、Refresh Token再利用検知を弱めない。
- [ ] AndroidとServerの既存認証Test、Session分離Test、自動バックアップのAccount Scope分離Testが成功する。

### 正式文書の整合

- [ ] 「ログアウト時に認証情報を削除する」という要求が、Tokenと認証済みSession情報の破棄を指し、非秘密の端末登録識別子は保持することを明確化する。
- [ ] `docs/product-requirements.md`、`docs/functional-design.md`、`docs/architecture-design.md`、`docs/development-guidelines.md`の関連節を実装契約と整合させる。

## 成功指標

- 通常ログアウト後の次回起動で「Register this device」が誤表示される件数: 0件。
- 通常のログアウト・再ログインによって新しく作成されるDevice件数: 0件。
- ログアウト後にAndroid上に残るAccess Token・Refresh Token・認証済みUser状態: 0件。
- 登録済みDeviceでの「ログアウト→アプリ再起動→再ログイン」の実機フローが成功する。

## スコープ外

以下は今回の修正に含めない。

- パスワード再設定用の管理CLIまたは画面の追加。
- 1台のAndroid端末で複数アカウントを切り替える機能。
- 利用者がServer側のDevice登録そのものを削除する「この端末の登録を解除」UI。
- Device上限10件やLocal Directの新規登録制約の変更。
- 過去の再登録で既に作成された重複Deviceレコードの自動統合・自動削除。

## 未決定事項

- 通常ログアウト後の「Sign in」画面で、前回のUsernameを表示・初期入力するか、Usernameも毎回入力するかは設計時に確定する。Usernameを保持する場合も認証根拠には使用しない。
- 将来の別アカウント利用に備え、「Sign in」画面に端末登録の明示的破棄導線を同時に設けるかは、今回のスコープとしてユーザー承認時に確定する。

## 参照ドキュメント

- `docs/product-requirements.md` 7.2.1、13.10 - ログイン、初回端末登録、ログアウト、自動端末登録の悪用防止
- `docs/functional-design.md` 6.1.5、6.1.9、6.2.12、8.2 - Android認証状態、端末登録、Session API
- `docs/architecture-design.md` - Android秘密値保存、Logout時のSession分離
- `docs/repository-structure.md` - `core-data`、`core-security`、`feature-auth`、`app`の配置
- `docs/development-guidelines.md` - 認証情報の削除、Session Scope、自動バックアップのAccount Scope分離
- `.steering/20260722-kurastorage-mvp/` - 既存のDevice登録・Login・Refresh・Logout実装履歴
