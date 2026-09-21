# UTAGEの任意追加コンポーネント「Extra」

ユーザーの要望が、ゼロからライフサイクルイベントのフックを書く前に、UTAGEの既製の任意コンポーネント
で既に実現できそうな場合にこれを読む（メインの`SKILL.md`のWorkflows参照）。これらは
`Assets/Utage/Scripts/ADV/Extra/`配下にあり、`AdvEngine`階層に**デフォルトではアタッチされて
いない** — 必要なものだけ`Add Component > Utage > ADV > Extra > ...`で追加し、Inspectorの
項目を設定する。

各コンポーネントはソース内にドキュメントコメント（クラス直上の概要コメント）があるので、
推薦する前に実ファイルを読むこと。この表はメインの`SKILL.md`の`last-verified`時点で
確認した内容の要約に過ぎない。

| コンポーネント | 内容 |
|---|---|
| `AdvAgingTest` | エージング/耐久テスト補助。選択肢を自動選択させ、無人でシナリオを最後まで再生させる（自動テスト用）。 |
| `AdvBackLogFilter` | 特定の行をバックログに残すかどうかを制御する。 |
| `AdvCharacterGrayOutController` | 非発話中のキャラクターをグレーアウトする。ソースコメントによれば、有効にするには`AdvEngine`の`OnPageTextChange`イベントにこのコンポーネント自身の同名メソッドをリスナー登録する必要がある — メインの`SKILL.md`で説明したInspector/コードでのイベントフックパターンの具体例。 |
| `AdvGalleryController` | シーン回想（ギャラリー）の再生中かどうかを追跡・制御する。 |
| `AdvOpenGallery`／`AdvCloseGallery` | CG/シーンギャラリーの全項目を強制解放／強制未開放にする — ソースコメントによればデバッグ用途を想定。 |
| `AdvDisableDuringSaveDataLoad` | セーブデータのロード中、アタッチしたGameObjectを無効化する（ロード中にUI要素がちらつくのを防ぐ）。 |
| `AdvInterruptScenario` | 現在実行中のシナリオを強制的に中断し、指定のラベルにジャンプする。**UTAGE自身のソースコメントが、この強制中断による副作用は未検証だと明記している** — 通常使いのツールではなく上級者向け・リスクのあるツールとして扱い、ユーザーが使いたがった場合はその旨を伝えること。 |
| `AdvLoadScene` | `SendMessageByName`機構（`utage-write-scenario`の同梱リファレンス06章参照）経由で使える拡張命令で、コマンドの`Arg3`に指定したUnityシーンをロードする。 |
| `AdvSelectionTimeLimit`／`AdvSelectionTimeLimitText` | 選択肢の制限時間タイマーと、そのカウントダウンをテキスト表示するための対のコンポーネント。 |
| `AdvTextSound` | 文字送り（テキスト表示）に合わせて効果音を鳴らす。`Type`で「時間間隔」／「文字数間隔」のどちらでサウンドを鳴らすかを切り替えられる。 |
| `AdvVideoLoadPathChanger` | 動画アセットのロード元ルートパスを変更する。 |

ユーザーの要望に合うものがここに無い場合、「解決策が無い」と決めつけず、メインの`SKILL.md`の
ライフサイクルイベントWorkflow（Inspector配線またはコード購読）にフォールバックすること。
これらの「Extra」コンポーネントは同じ基盤イベントの上に乗った便利レイヤーであり、
唯一の実現手段ではない。
