# KuraStorage Environment Information (Example)

このファイルは、Git管理外の`docs/environment-info.md`で使用する項目と記入形式を示す公開用Templateである。

1. このファイルを`docs/environment-info.md`へCopyする。
2. `SET_LOCALLY`を対象環境の値へ置き換える。
3. Password、Token、Private Key、ZeroTier Node Identity、認可情報、Android Signing Key／Keystore内容は記載しない。
4. Source、Build、Deploymentへ必要な値は、本ファイルを実行時に直接解析せず、各項目で指定した環境変数、Gradle Property、または権限制限された設定Fileへ転記する。

`docs/environment-info.md`は`.gitignore`対象であり、RepositoryへCommitしない。

## Network

| 項目ID | Local値 | 正式な設定入力 | 用途 |
| --- | --- | --- | --- |
| `REMOTE-ACCESS-PROVIDER` | `ZEROTIER` | 運用文書 | Phase 1で使用するリモートアクセス方式。将来変更時は設計・脅威分析も更新する |
| `NET-API-HOSTNAME` | `SET_LOCALLY` | `KURASTORAGE_API_HOSTNAME`／`kurastorage.apiHostname` | TLSで検証するKuraStorage APIの固定Hostname |
| `NET-LAN-API-IP` | `SET_LOCALLY` | Nginx／Firewall設定、`kurastorage.lanApiAddress` | Local Direct経路で使用するRaspberry Piの固定LAN IP |
| `NET-LAN-CIDR` | `SET_LOCALLY` | Nginx／Firewall設定 | KuraStorage HTTPSを許可するLAN範囲 |
| `NET-ZEROTIER-API-IP` | `SET_LOCALLY` | Nginx／Firewall設定、`kurastorage.zerotierApiAddress` | ZeroTier経路で使用するRaspberry Piの固定Managed IP |
| `NET-ZEROTIER-CIDR` | `SET_LOCALLY` | Nginx／Firewall設定 | KuraStorage HTTPSを許可するZeroTier範囲 |
| `NET-ZEROTIER-NETWORK-ID` | `SET_LOCALLY` | KuraStorage外のZeroTier管理面 | 運用照合用Network ID。APK、Server設定、DBへ転記しない |
| `NET-ZEROTIER-CONTROLLER-TYPE` | `SET_LOCALLY` | KuraStorage外のZeroTier管理面 | `ZEROTIER_CENTRAL`または`SELF_HOSTED`。Tokenや認可情報は記載しない |

`NET-ZEROTIER-API-IP`は、ZeroTier Network上でRaspberry Piへ割り当てた固定Managed IPであり、LAN IPやPublic IPではない。`NET-ZEROTIER-CIDR`はそのNetworkのManaged IP範囲、`NET-ZEROTIER-NETWORK-ID`は管理画面と実機の所属先を照合する公開識別子、`NET-ZEROTIER-CONTROLLER-TYPE`は認可を管理するController種別を示す。具体値はZeroTier Centralまたは自己管理ControllerとRaspberry Piの`zerotier-cli listnetworks`等で確認し、確定前は`SET_LOCALLY`のままとする。

## Storage

この節はMVPのHDD誤保存防止に使用する。Pathは実環境情報でありSource Codeへ直書きしない。

| 項目ID | Local値 | 正式な設定入力 | 用途 |
| --- | --- | --- | --- |
| `STORAGE-ROOT-PATH` | `SET_LOCALLY` | `KuraStorage__Storage__RootPath` | 専用HDD上のKuraStorage Root。OS Root上の代替DirectoryへFallbackしない |
| `STORAGE-ID` | `SET_LOCALLY` | `KuraStorage__Storage__ExpectedStorageId` | HDD上の`.storage-identity`と照合する非Secret識別子 |
| `STORAGE-SAFETY-RESERVE-BYTES` | `SET_LOCALLY` | `KuraStorage__Storage__SafetyReserveBytes` | Upload可否判定で常に残す安全余裕Byte数 |

## TLS and signing material

この節にはPathと公開Fingerprintだけを記録し、秘密鍵本文やPassphraseは記録しない。

| 項目ID | Local値 | 正式な設定入力 | 用途 |
| --- | --- | --- | --- |
| `TLS-ROOT-CA-CERT-PATH` | `SET_LOCALLY` | Android Release Build入力／管理端末 | 配布するRoot CA公開証明書のPath |
| `TLS-ROOT-CA-SHA256` | `SET_LOCALLY` | 手動照合 | Root CA公開証明書のSHA-256 Fingerprint |
| `TLS-SERVER-CERT-PATH` | `SET_LOCALLY` | Nginx設定 | API Server公開証明書のPath |
| `TLS-SERVER-KEY-PATH` | `SET_LOCALLY` | root所有・権限制限File | API Server秘密鍵のPath。鍵本文は記載しない |
| `JWT-SIGNING-KEY-PATH` | `SET_LOCALLY` | `KuraStorage__Identity__JwtSigningKeyPath` | ES256署名秘密鍵のPath。鍵本文は記載しない |
| `ANDROID-SIGNING-KEYSTORE-PATH` | `SET_LOCALLY` | `KURASTORAGE_RELEASE_STORE_FILE` | Android署名KeystoreのPath。Passwordと鍵本文は記載しない |

## Physical validation environment

| 項目ID | Local値 | 用途 |
| --- | --- | --- |
| `DEVICE-ANDROID-MODEL` | `SET_LOCALLY` | 物理Android検証端末の識別 |
| `DEVICE-ANDROID-VERSION` | `SET_LOCALLY` | Android Version／API Levelの記録 |
| `DEVICE-ANDROID-STRONGBOX` | `SET_LOCALLY` | StrongBox対応有無の記録 |
| `SERVER-HARDWARE` | `SET_LOCALLY` | Raspberry Pi Model、Revision、RAMの記録 |
| `SERVER-OS` | `SET_LOCALLY` | OS、Architecture、Kernelの記録 |
| `SERVER-POSTGRESQL-VERSION` | `SET_LOCALLY` | 実機Test時のPostgreSQL Version |
| `SERVER-FFMPEG-VERSION` | `NOT_INSTALLED_FOR_MVP` | MVP後にMedia機能を追加する際のFFmpeg Version |

## MVP後: Local Wi-Fi backup policy

SSIDとBSSIDは自動Backup許可条件であり、Server認証やLocal Direct判定には使用しない。

| 項目ID | Local値 | 用途 |
| --- | --- | --- |
| `WIFI-TRUSTED-SSID` | `SET_LOCALLY` | 自動Backupを許可するWi-Fi |
| `WIFI-TRUSTED-BSSID` | `SET_LOCALLY` | 任意のAP限定。不要な場合は`NOT_SET` |
