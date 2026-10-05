---
name: utage-write-scenario
description: "Use this skill whenever the user wants to write or edit UTAGE scenario script content, rebuild it, or fix an import error — e.g. 'キャラが告白するシーンを書いて', 'ここに選択肢の分岐を追加して', 'このシーンにBGMを追加して', 'シナリオをリビルドして', 'ゲームにシナリオの変更が反映されない', 'インポートエラーが出た', 'キャラ名が認識されない', '今シナリオの何ページ目か確認したい'. Covers writing/editing the Excel/CSV scenario data (by pointing at the package's own bundled Scenario Reference rather than re-deriving syntax here), rebuilding it via the Scenario Data Builder, reading Console import errors, and using the Scenario Viewer to inspect playback state. Do NOT use for initial project setup (see utage-setup-and-overview), AssetBundle/dicing resource conversion (see utage-resource-management), or C# extension (see utage-extend-with-code). When in doubt whether a request is about UTAGE scenario content — writing it or getting it working — use this skill."
metadata:
  asset: "UTAGE (Unity Text Adventure Game Engine) Version4"
  publisher: "Ryohei Tokimura (Madnesslabo)"
  asset-version: "4.2.9"
  skill-version: "1.0.0"
  unity: "6000.0.58f2+"
  render-pipelines: "Built-in, URP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/utage4-for-unity-text-adventure-game-engine-version4-266447"
  documentation-url: "https://madnesslabo.net/utage/"
  support-url: "https://madnesslabo.net/utage/"
  last-verified: "2026-09-01"
---

# UTAGEシナリオの記述・リビルド・デバッグ

UTAGEシナリオを扱う一連の作業サイクル全体をカバーする: Excel/CSVの内容を書く・編集する、
それをUTAGEのランタイム形式にリビルドする、うまくいかない時に原因を切り分ける。
このスキルはあえて構文そのものを再解説しない — UTAGEはパッケージ内に完全で正本の構文・
コマンドリファレンスを同梱しており、このスキルの役目はAIエージェントをそこへ誘導し、
「実在する定義済みの名前だけを使う」という規律を徹底させ、同梱のリビルド・デバッグツールを
扱うことにある。

## When to use this skill

- 既存または新規のUTAGEシナリオで、セリフ・キャラの表情・選択肢・分岐・シーン内コマンドの
  追加や編集をしたいとき
- UTAGEのシナリオ構文の書き方を聞かれたとき
- シナリオファイルを編集した後、変更を反映させたいとき
- 編集後にコンソールにインポートエラーが出たとき
- ゲームで期待したシナリオ内容が表示されず、原因がはっきりしないとき
- Not for: 初回のプロジェクトセットアップ（`utage-setup-and-overview`参照）、
  AssetBundle/ダイシングのリソース変換（`utage-resource-management`参照）、
  C#による拡張（`utage-extend-with-code`参照）

## Prerequisites

- UTAGEプロジェクトが既にセットアップ済みであること（未セットアップなら`utage-setup-and-overview`参照）
- `Assets/<ProjectName>/Scenarios/`配下にシナリオファイル（Excel `.xls`/`.xlsx`またはCSV）が
  存在すること — New Projectウィザードが自動生成する（`Scenarios/<ProjectName>.xls`）。
  同時に`Assets/<ProjectName>/<ProjectName>.project.asset`という`AdvScenarioDataProject`
  アセットも生成される
- パッケージに同梱されているシナリオリファレンス（`Assets/Utage/Docs/ScenarioReference/`配下）:
  - 日本語版（正本）: `Assets/Utage/Docs/ScenarioReference/ja/README.md`
  - 英語版（日本語正本から生成）: `Assets/Utage/Docs/ScenarioReference/en/README.md`

**プログラム的な確認方法** — 進める前に`Assets/Utage/Docs/ScenarioReference/ja/README.md`
（または`en`版）が存在すること、および型`Utage.AdvScenarioDataProject`が存在することを
確認する。どちらか欠けている場合はUTAGEのインポートが不完全な可能性があるので、
パッケージの再インポートをユーザーに促す。

## Quick start

**内容を書く:**

1. 作業言語に合わせて同梱リファレンスのREADMEを開く（`ja/README.md`または`en/README.md`）
2. そのREADME自身の「使い方」の手順に沿って進める — 飛ばさないこと:
   - まず01章で列構成・ラベル・`PageCtrl`・テキストタグを把握する
   - 使うコマンドの引数仕様を02〜06章で確認する
   - キャラ名・画像ラベル・サウンドラベル・変数は**07章の設定シートに既に定義されているものだけ**
     を使う — 未定義の名前はインポートエラーになる
3. プロジェクトに既存のシナリオファイルがあれば、まずそれらを読んで、既に使われているキャラ名や
   ラベルと整合させる。新しい名前を勝手に作らないこと
4. リファレンスの構文・引数仕様に沿って、Excel/CSVのシナリオファイルに内容を書く・編集する

**リビルドして反映させる:**

5. `Tools > Utage > Scenario Data Builder`を開く
6. 上部の"Project"欄に、そのプロジェクトの`<ProjectName>.project.asset`が
   設定されていることを確認する（New Project作成直後は自動で設定される。別の/空の
   プロジェクトが表示されている場合は正しいものを設定し直す）
7. "Import Scenario Files"の下にある**Import**ボタンをクリックする
   （シナリオファイルが登録されていないと無効のまま — Common issues参照）
8. コンソールを確認する。エラーが無ければリビルド成功で、編集内容が反映されている
9. エラーが出た場合、当てずっぽうで直そうとしないこと。同梱リファレンス08章
   （`08_CommonErrors_ja.md`/`08_CommonErrors_en.md`）にUTAGEのインポートエラーメッセージの
   読み方と典型的な原因が書かれているので、それに沿ってシナリオファイルを直し、
   再度インポートする

期待される結果: UTAGEの構文に沿い、プロジェクトの設定シートに存在する名前だけを参照する
シナリオ内容ができあがり、コンソールにエラーを出さずインポートできる。

## Workflows

### Workflow: 既存ファイルへのシナリオ内容の追加

**Goal:** 既にあるシナリオ（セリフ追加・新しい分岐・新規コマンド）を、既存の動作を壊さずに拡張する。

**Steps:** まず対象のシナリオファイルと設定シート（07章）を読み、実際に使われているキャラ名・
画像/サウンドラベル・既存のラベル構造（`*label`マーカー・`PageCtrl`の使い方）を把握する。
その上で、同じ規則に沿って新しい行を追加し、使うコマンドの正確な構文は02〜06章で確認する。
その後リビルドする（Quick startの手順5〜9）。

**Expected result:** 既に定義済みの名前だけを使い、`Jump`／ラベル参照先も実在する新しい行が
追加され、リビルドがエラー無く完了する。

### Workflow: New Projectで作られたテンプレートからシナリオを書き始める

**Goal:** `utage-setup-and-overview`のNew Projectウィザードが作った、ほぼ空のシナリオ
テンプレートに実際の内容を書き込む。

**Steps:** `Scenarios/<ProjectName>.xls`を開き、01章で必要な列構成を確認した上で、
02〜06章に沿ってセリフ・コマンドを書く。テンプレートの07章設定シートに既にある
プレースホルダーのキャラ名・ラベルを流用するか、実際の名前をユーザーに確認する。
その後リビルドする（Quick startの手順5〜9）。

**Expected result:** ドキュメント化された列構成に沿い、定義済みの名前だけを参照する、
実際の内容が入ったシナリオシートが、問題無くインポートされる。

### Workflow: インポートエラーの原因特定

**Goal:** コンソールのインポートエラーを、シナリオファイルの具体的な修正箇所に落とし込む。

**Steps:** エラーメッセージ全文とその文脈（どのファイル／行を指しているか）を読む。
同梱リファレンスの08章で該当する症状を照合する。よくある原因は、未定義のキャラ名／
画像ラベル／サウンドラベル／変数（07章参照）、フォーマット不正な行（列数の不一致・
必須セルの空欄）、壊れた`Jump`／ラベル参照先など。

**Expected result:** 具体的な原因が特定・修正され、Quick startの再インポートが
エラー無く完了する。

### Workflow: デバッグ中の再生状態の確認

**Goal:** エンジンが実際にどのシナリオ／ページ／ラベルにいるかを、作業しながら確認する。

**Steps:** `AdvEngine`を含むシーンが開いていること（通常はPlay Modeで実際にシナリオを
再生している状態）を確認する。`Tools > Utage > Viewer > Scenario Viewer`を開く。
"Not found AdvEngine"と表示される場合は、シーンが間違っているか`AdvEngine`が存在しない
ので、まずそれを直す（`utage-setup-and-overview`参照）。

**Expected result:** ビューワーに現在のシナリオ／ページデータが表示され、ユーザーが
想定している箇所まで再生が到達しているかを確認できる。

## Verification

- 使用しているキャラ名・画像ラベル・サウンドラベル・変数が、すべてプロジェクトの設定シート
  （リファレンスの07章）に定義されていること
- 参照している`Jump`／ラベル先が、すべてシナリオ内に実在すること
- 使用した構文（列・`PageCtrl`・タグ）が01章および該当するコマンド章（02〜06）と一致していること
- Importボタンをクリックした後、コンソールにエラーが出ていないこと — これが、書いた構文単体
  よりも確実な合否シグナルになる
- （シーンが開いていて`AdvEngine`が存在する状態で）Scenario Viewerが期待通りの
  現在のシナリオ状態を表示していること
- ユーザーから「想定と違う動作」の報告があった場合、原因となった具体的な行・コマンドを
  特定し、単に「今は動く」で終わらせていないこと

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Assets/Utage/Docs/ScenarioReference/ja/README.md`／`en/README.md` | 同梱Markdownリファレンス | このスキルが常に参照する、パッケージ同梱の正本の構文・コマンドリファレンス |
| `Assets/<ProjectName>/Scenarios/<ProjectName>.xls` | Excelシナリオデータ | New Projectウィザードが作成する、シナリオ内容そのもののファイル |
| `Tools > Utage > Scenario Data Builder` | Editorメニュー項目 | Importボタンで、Excel/CSVソースからシナリオデータをリビルドするウィンドウを開く |
| `Utage.AdvScenarioDataProject.ImportAll()` | メソッド | Importボタンが実際に呼び出す処理。プロジェクトに登録された全シナリオファイルをリビルドする |
| `Tools > Utage > Viewer > Scenario Viewer` | Editorメニュー項目 | 開いているシーン内の`AdvEngine`の現在のシナリオ／ページ状態を確認する |
| `Tools > Utage > Tools > Scenario Character Validator` | Editorメニュー項目 | シナリオのテキストが想定範囲の文字だけを使っているか検証する（フォント/ローカライズが対応していないグリフの検出等）。これは**キャラ名**の検証ではなく**文字**そのものの検証 |

## Common issues

- **編集後にインポートエラーが出る** — 当てずっぽうで直そうとしないこと。同梱リファレンスの
  08章（`08_CommonErrors_ja.md`／`08_CommonErrors_en.md`）にエラーの読み方と典型的な原因が
  書かれている
- **未定義の名前を参照しているというインポートエラー** — 最も多い原因。シナリオ中で使われている
  キャラ名／画像ラベル／サウンドラベル／変数が設定シート（07章）に定義されていない。
  対処: 定義を追加するか、既存の名前に合わせて修正する
- **Importボタンが無効のまま押せない** — 原因: プロジェクトアセットにシナリオファイルが
  1つも登録されていない（`IsEnableImport`がfalse。これは`GetAllScenarioFiles()`が
  非nullのパスを1つ以上返す場合にtrueになる）。対処: プロジェクトにシナリオファイルが
  存在するか、Project欄に正しい`.project.asset`が設定されているか確認する
- **Scenario Viewerが"Not found AdvEngine"と表示する** — 原因: 現在開いているシーンに
  `AdvEngine`が存在しない、またはシーンが開かれていない。対処: 正しいUTAGEシーンを開く
  （`utage-setup-and-overview`参照）
- **編集が反映されないように見える** — 原因: シナリオを編集したが再インポートしていない。
  対処: Quick startのImport手順を実行する。UTAGEは元のExcel/CSVをランタイムでライブ扱い
  しない

## Boundaries

- このスキルはあえてUTAGEのシナリオ構文を再解説しない — 常に同梱リファレンスへ誘導することで、
  両者が食い違わないようにしている。同梱リファレンスが見つからない・不完全に見える場合は、
  記憶から構文を捏造せず、その旨をそのまま伝えること
- AssetBundleビルドや「ダイシング」リソース変換（`Tools > Utage > Resource Converter`・
  `Tools > Utage > Dicing Converter`）は別のツールで扱う — `utage-resource-management`参照
- Scenario Character Validatorが検証するのは**使用文字**（フォント/グリフのカバー範囲等）で
  あり、キャラ**名**が定義済みかどうかではない。デバッグ時に混同しないこと
- コードによる拡張は扱わない — `utage-extend-with-code`参照
- 英語話者のユーザーには`Assets/Utage/AI/Skills/`配下の英語版を案内する
