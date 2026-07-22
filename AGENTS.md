# KuraStorage Codex Instructions

## 1. 基本原則

KuraStorageでは、`docs/`と`.steering/`を使用してスペック駆動で開発する。

1. `docs/`で、プロジェクト全体の「何を作るか」「どう作るか」を定義する。
2. `.steering/`で、今回の作業内容と実装手順を定義する。
3. `tasklist.md`に従って実装する。
4. テストと動作確認を行う。
5. 必要な場合は、関連するドキュメントと進捗を更新する。

## 2. 文書の役割

### 2.1 永続文書

`docs/`はプロジェクト全体の正式な仕様と設計を管理する。

- `docs/product-requirements.md`
- `docs/functional-design.md`
- `docs/architecture-design.md`
- `docs/repository-structure.md`
- `docs/development-guidelines.md`

作業中に仕様や設計の変更が必要になった場合は、対象の正式文書も同じ変更で更新する。

### 2.2 作業単位の文書

`.steering/`は、特定の作業で「今回何をするか」を管理する。

作業ごとに、次の形式でディレクトリを作成する。

```text
.steering/YYYYMMDD-task-name/
├── requirements.md
├── design.md
└── tasklist.md
```

- `requirements.md`: 今回の要求と完了条件
- `design.md`: 今回の実装方針と変更内容
- `tasklist.md`: 実行する具体的なタスクと進捗

同じ作業のSteeringファイルが既に存在する場合は、新しく重複して作成せず、既存のファイルを使用する。

## 3. Steeringスキルの使用

**作業計画、実装、Pull Request完了処理、全体振り返りを行うときは、必ず`steering`スキルを使用する。**

- **作業計画時**: `steering`スキルのモード1を使用し、`requirements.md`、`design.md`、`tasklist.md`を作成または更新する。
- **実装時**: `steering`スキルのモード2を使用し、`tasklist.md`に従って実装と進捗更新を行う。
- **Pull Request完了時・全体完了時**: `steering`スキルのモード3を使用し、各Pull Requestの完了記録または全タスク完了後の全体振り返りを記録する。

各Pull Request作成後は、対象Pull Requestの完了記録を`tasklist.md`に追加する。
後続のPull Requestに未完了タスクが残っていても、完了したPull Requestの記録は行う。
全体振り返りは、すべてのPull Requestとタスクが完了した後にのみ記録する。

`steering`スキルを使用せずに、`.steering/**/tasklist.md`の実装、進捗更新、Pull Request完了記録、全体振り返りを行ってはならない。

進め方、チェック状態の更新、Pull Request単位の作業、完了記録、全体振り返りの詳細は、`steering`スキルの指示に従う。

## 4. 文書作成時の承認

`requirements.md`、`design.md`、`tasklist.md`を新しく作成する場合は、1ファイルずつ作成する。

各ファイルの作成後はユーザーへ内容確認を求め、承認を得てから次のファイルへ進む。

```text
「[文書名]の作成が完了しました。内容を確認してください。
承認いただけたら次の文書に進みます。」
```

ユーザーが複数ファイルの一括作成または一括更新を明示的に指示した場合は、その指示を優先する。

## 5. 実装前の確認

新しい実装を始める前に、次を行う。

1. 本ファイルを確認する。
2. 対象の`.steering/**/requirements.md`、`design.md`、`tasklist.md`を確認する。
3. 作業に直接関係する`docs/`の節を確認する。
4. `git status`と既存の変更差分を確認する。
5. 既存コードから類似実装を検索し、既存の構成と実装パターンを確認する。
6. `steering`スキルのモード2を使用して実装を開始する。

必要性なく、リポジトリ全体や全設計書を最初から読み直さない。

## 6. 文書の優先順位

判断が競合する場合は、次の順に優先する。

1. `docs/product-requirements.md`
2. `docs/functional-design.md`
3. `docs/architecture-design.md`
4. `docs/repository-structure.md`
5. `docs/development-guidelines.md`
6. 対象作業の`.steering/**/requirements.md`
7. 対象作業の`.steering/**/design.md`
8. 対象作業の`.steering/**/tasklist.md`
9. 既存実装

矛盾を見つけた場合は、独自判断で処理せず、矛盾箇所と影響を明確にする。
必要な修正は、関連する正式文書とSteeringファイルへ反映する。

## 7. 進捗管理

`.steering/**/tasklist.md`を正式な進捗記録として使用する。

- 実装開始時点では対象タスクを`[ ]`のままにする。
- 完了条件を満たしたタスクだけを`[x]`へ変更する。
- 親タスクは、すべての子タスクが完了した後にだけ`[x]`へ変更する。
- 未実装、未検証、失敗中のタスクを完了扱いにしない。
- 進捗更新の具体的なタイミングは`steering`スキルに従う。

## 8. Pull Requestの言語

- Pull Requestのタイトルと本文は英語で作成する。
- Pull Requestの本文には、英語で目的、対象タスク、変更内容、テスト結果、影響または未実施事項を記載する。
- `tasklist.md`の完了記録とユーザーへの報告は、別途指定がない限り日本語でよい。
