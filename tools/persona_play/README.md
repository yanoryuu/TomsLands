# 人格プレイ（ずんだもん / 四国めたん）

ローカルの判断モデル（Ollama の Jev 互換エンドポイント `POST /v1/systemone`）に「その日どうするか」を聞きながら、
Play モードの実画面をオートプレイ（`AutoPlayController` + `IAutoPlayStrategy`）で進める。実況動画（A〜D）と同じ撮り方で、種類は **Z（ずんだもん）** と **M（四国めたん）**。
Editor のデバッグ用ボット `AutoPlayJevBot`（ヘッドレス）は使わない。

| 人格 | 判断モデル | 選び方 |
|---|---|---|
| ずんだもん | `kev:4b-q8_0`（約 4.5 GB） | 確率分布から温度つきで抽選。予算・スパチャの勢いは 1 段上へ（自信過剰）。ときどき情報を買わずに当てに行く・魔王軍に張る。学びは弱い（教訓は最新 1 件だけ読む） |
| 四国めたん | `kev:9b-q4_K_M`（約 5.6 GB） | 慎重な選び方（`PersonaChooser`: 1 位がはっきりしていれば 1 位、近ければ画面の数字で選ぶ、5% だけ別の手を試す）。現金は 1 万G を割らない。毎朝新聞を読み、配信先の情報を先に買う。村資金は 4 分の 1 を残し、元が取れそうな施設には投資する。過去の損益で選択肢の重みを少しだけ寄せる |
| 相談（consult） | 2 人とも | ずんだもんが遊び、判断ごとにめたんに相談する（下の「相談プレイ」） |

人物像は `tools/jikkyo_video/CHARACTERS.md`。セリフ（一言）はテンプレート（`PersonaLines.cs`）で、テキスト生成の LLM は使わない。
ときどきテンプレートの代わりに、いま流行っているミームを言う（ミーム辞書 `tools/persona_play/memes/`。下の「ミーム」）。

## 準備

1. Ollama を入れる（このPCでは `E:\Ollama\app`、モデルは `E:\Ollama\models`。ユーザー環境変数 `OLLAMA_MODELS=E:\Ollama\models`）。
2. モデルを取る（済み）: `ollama pull kev:4b-q8_0` / `ollama pull kev:9b-q4_K_M`
3. サーバーが `http://localhost:11434` で動いていることを確かめる: `ollama list`
4. 設定ファイル `UserSettings/PersonaPlay.json`（無ければ初回に既定値で作られる。git には入らない）:

```json
{
  "baseUrl": "http://localhost:11434",
  "path": "/v1/systemone",
  "timeoutSec": 60.0,
  "models": { "zundamon": "kev:4b-q8_0", "metan": "kev:9b-q4_K_M" },
  "lessonsInState": 5,
  "memeLexiconPath": "tools/persona_play/memes/memes_2026-10.json"
}
```

`memeLexiconPath` はプロジェクト直下からの相対パス（絶対パスも可）。空にする・ファイルが無い・読めないときはミームなし（一言はテンプレートだけ。それ以外の動きは同じ）。

`TYPESAFE_API_KEY` / `TYPESAFE_BASE_URL` は触らない（Editor の Jev ツールは本物の Jev を叩き続ける）。

5. 通信の確認: `py -I tools/persona_play/test_request.py`
   C# と同じ JSON（サンプルの 1 日）を 2 人のモデルへ送り、答えと待ち時間を出す。
   C# 側が作った JSON と一致するかは `--compare <フォルダ>`（`csharp_day_<persona>.json` を置く）で確かめられる。

## 撮る（Z / M）

- メニュー: `Tools/TomsLands/自動録画/実況動画用に録画して Play（Z ずんだもん人格…）` / `（M 四国めたん人格…）`
- エディタを開いたまま外から: `Library/JikkyoRecordKind.txt` に `Z`（または `M`）と書く。末尾に ` quit` を付けると録画のあとエディタを閉じる。
- 他のプリセットと同じく **隔離セーブ**（ユーザーのセーブスロットは読まない・書かない）、1 周目 → 村 → 2 周目の終わりまで撮る。
- 録画: `Recordings/JikkyoZ_yyyyMMdd_HHmmss.mp4` / `Recordings/JikkyoM_*.mp4`
- デバッグメニュー「オート」タブの戦略一覧にも「人格 ずんだもん / 人格 四国めたん」が出る（録画なしで動かすとき）。

## 動き方

- 毎日の仕入れの前に 1 リクエスト（予算・仕入れ方針・新聞を読むか・情報を買うか・魔王軍支援・ダンジョン向けの割合と武器）。答えが来るまで店は待つ（初回はモデルの読み込みで 10〜40 秒）。
- 配信が始まったら裏で 1 リクエスト（どちらに張るか・どのスパチャか・どれくらい使うか）。答えが来るまで介入はしない。
- 通信に失敗・時間切れのときは、その局面だけ標準戦略で続ける（ログに `fallback: true`、AutoPlay のログに 1 回だけ出る）。
- choice の選択肢は 26 件までに絞る（買える・関係の深い順）。

## 出力

| 何 | 場所 |
|---|---|
| 判断と一言 | `Recordings/persona_log_<yyyyMMdd_HHmmss>.jsonl`（1 行 = 1 判断: time, day, persona, model, question, choice, probabilities, confidence, line, fallback, latencyMs。一言がミームのときは `meme`（id）、めたんの返しなら `memeCue`（きっかけのずんだもんの行）も） |
| 記憶（成長） | `UserSettings/PersonaMemory/zundamon.json` / `metan.json`（配信・ランの結果、英語の教訓、選択肢ごとの損益） |
| 想起の履歴 | `UserSettings/PersonaMemory/<persona>_recall.json`（結果の出た判断。最大 4,000 件。無ければ判断ログから作り直す） |
| 学習器 | `UserSettings/PersonaMemory/<persona>_model.json`（質問 × 選択肢ごとのリッジ回帰。大きさは選択肢の数で決まる） |

記憶を消せば最初からやり直しになる。教訓は次のリクエストの `state.lessonsFromPastRuns` に新しい順で載る（めたん 5 件、ずんだもん 1 件）。
2026-10-27 に弱点シャッフル（#135）・配信熱（#134）・配信中の値動き（#136）でルールが変わったので、それまでの記憶は `UserSettings/PersonaMemory/archive_20261027/` に移して 2 人とも新しく始めた。

## 学習（似た場面の想起 ＋ 小さな学習器）

`Assets/Scripts/Debug/AutoPlay/Jev/PersonaLearning.cs`。ゲームの係数や式（配信中の値動きなど）は使わず、画面・モデルから読んだ実際の値と結果の損益だけを使う。

- **特徴**: 判断ごとに 0〜1 の数 11 個（村の総合Lv の割合を含む。営業日の判断: 日・所持金・返済の重さ・返済までの日数・配信までの日数・弱点を知っているか・ダンジョン Lv・勇者 Lv・資産のうち在庫の割合・店 Lv／配信の判断: 日・所持金・勇者の HP・魔物の数・ボスがいるか・ダンジョン Lv・勇者 Lv・弱点を知っているか・スパチャ予算の割合・魔王軍に出資したか）。ダンジョン名は使わない（弱点がランごとにシャッフルされるため）。
- **結果**: 予算・仕入れ方針・新聞 = 次の朝までの資産（現金 + 在庫 × その日の価格）の増減／情報・魔王軍支援・配信向けの割合 = 配信の翌朝までの資産の増減／張る側・スパチャ・勢い = その配信の損益。武器の銘柄は学習しない。
- **想起**: 同じ種類の過去の判断から重み付き距離で近い順に、めたん 5 件・ずんだもん 3 件（同じ質問は 2 件まで）を `state.similarPastSituations` に英語で載せる。例:
  `similar past: day 7, cash ~32k G, debt due in 2 days, stream tomorrow, weakness unknown, dungeon Lv2 → chose to buy the dungeon information → result +4.1k G net worth by the day after the stream`
- **学習器**: 質問 × 選択肢ごとに リッジ回帰（特徴 → 損益/1 万G、±5 で切る、1 件ごとに古い結果を 0.998 倍）。予測と「kev の確率で重み付けした平均」の差を advantage として `p ∝ p_kev × exp(λ × advantage)`（1 選択肢 ±2 まで）。λ・リッジは人格ごと: めたん λ 0.8 / リッジ 5 / 薄め 0.99、ずんだもん λ 0.1 / リッジ 30（ほぼ学ばない）。件数が少ないうちはリッジで予測が 0 に縮むので、運の良し悪し 1 回では動かない。
- **めたんの固まり対策（2026-10-28）**: 前夜、配信は魔王軍 100%・情報 100%・支援 0 100%・勢い 2 100%・村は投資 1 回だけ、に固まった。
  原因は、未経験の選択肢の予測が 0 のままなのに、配信の損益はいつもプラス（売上が入る）なので、一度選んだ手の予測だけが +4〜5 になり、上限 ±2 まで寄せ続けたこと
  （kev は配信で 69 回中 61 回「見守る」が 1 位だったのに、最後は 69 回とも魔王軍）。
  めたんだけ: 予測を質問の平均へ 8 件ぶん縮める（未経験 = 平均扱い）・件数の少ない手に楽観 0.15 / √(1+件数)・log の上限 0.7。
  ずんだもんは前のまま（`ShrinkCount` 0 / `Optimism` 0 / 上限 2）。
- **めたんの選び方（`PersonaChooser.Careful`）**: 学習器で寄せた確率に「場面の倍率」（返済が近くて重い・手持ちに余裕・弱点を知っている・魔王軍に出資した・張った側・情報料の割合・元が取れそうな施設 など）を掛け、
  選択肢は 1 位と 2 位の差が 0.25 以上なら 1 位、近ければ温度 0.5 で抽選。段階（予算・勢いなど）は期待値を使い、二つの段階の間なら近さで抽選。確率 5% で kev が 5% 以上つけた別の手（段階は隣）を試す。
  オフラインの確かめ: `py -3 -X utf8 tools/persona_play/replay_selection.py`（ログの入力を新旧の規則で再生。前の式はログの adjust と 1112/1112 一致）。
- **ログ**: 判断の行に `rules`（=3）`decisionId` `kind` `horizon` `features` `adjust`（log 倍率）`finalProbabilities` `recalled` を足した（`probabilities` は kev の確率のまま）。結果が出たら `question: "resolve"` の行（`ref` = decisionId、`reward` = 損益 G）。

## 人格の形成（rules 4、2026-10-28。設計は `Docs/Persona_Formation_Design.md`）

人格を「特性（固定）＋気分（動く）＋技能（学ぶ）」に分けた。学習器・想起は rules 4 から数え直し（rules 3 のファイルは `UserSettings/PersonaMemory/archive_20261028_rules3/`）。

- **特性** `PersonaTraits.cs`: Big Five ＋ H と、危険選好・損失回避 λ・衝動性・回復の速さ・自尊・助言の受け入れ・学ぶ速さ・ミーム好き。温度・自信過剰の段数・難しい道の確率・現金の下限・学習の強さ・派手な施設の倍率はここから導く（前の定数と同じ値。`replay_selection.py --check-traits` で選択が 100% 一致）。
- **気分** `PersonaMood.cs`: 自信・しょんぼり・ドヤ・イラ。結果（いつもの損益との差）・相談の後日談で動き、判断ごとに基準点へ戻る（半減期 ずんだもん 2・めたん 6）。ランの始まりに戻す。判断ログの `mood`、結果の行の `moodBefore` / `moodAfter`。
- **流儀 S(a)** `PersonaStyle.cs`: 選択肢の大胆さ b・下振れ d・難しい道 hard に、気分（ハウスマネー・取り返し）と損失回避（手持ちの苦しさ・負けた直後は λ×1.2）を掛けて確率に足す。めたんはマウントされてイラが高いと 1 ランに 1 回だけ崩れる（`beat: collapse`）。
- **学び方**: 損益を人格の効用に変えてから学習器へ（めたん: 負けは 2.25 倍・振れの罰、ずんだもん: 大勝ちを重く・負けは 0.7 倍）。人格の事前分布 p_kev·exp(S) への KL の上限（ずんだもん 0.15・めたん 0.35 nat）、自由な質問のエントロピーの下限（1.0 / 0.5 bit。慎重な選び方では別の手を試す確率を 15% に）。署名の質問（めたんの新聞・情報）は除く。固まりの安全弁: 1 ランで自由な質問の最多の手が 85% を超えたら次のランは技能の重みを半分（ログ `question: safety_valve`）。確かめ: `replay_selection.py --no-collapse --persona metan --runs 0215`。
- **相談**: 聞く確率は receptivity（0.30）と気分から（自信・ドヤで下がり、しょんぼりで上がる）。後日談は種類の重み（マウント 0.45・だから言ったでしょ 1.0・お礼 0.6・文句 0.5）× 動いた大きさで選ぶ（マウント : 泣き ≈ 1:3〜1:5）。
- **一言**: 気分つきのテンプレート（`persona|*|sad` など）、同じ行は 1 ラン 3 回まで・20 行あける（あいづちは言えなければ黙ってうなずく）、前の出来事の呼び戻し（「この前も〜で外したでしょ」）、人物の事実（金欠・表紙は読んだ・77）、めたんのまっすぐな褒めは 1 ランに 1 回（`beat: praise`）。
- **指標**: `metrics/persona_metrics.py`（M1〜M14）。マラソンの終わりに summary.md へ自動で足す。
- **rules 4.1（2026-10-28 午後）**: 村の予算（めたんは「村資金の 1/4 は残す」を「25% 使う」と取り違えて何も買えなかった → 段階は「使う割合」。残す割合を割らない範囲で、元が取れそうな施設に足りる段階まで上げる。決めた施設が予算に入らないときは `budget_short` をログに出し、ずんだもんは予算を上げ、めたんは元が取れて買える 1 つを選ぶ）。押し切りの「大丈夫なのだ、問題ないのだ」を 6 通りに、一言の数えはマラソンの 1 ラン（2 周）・話し手ごと（24 行あける）。条件つきの助言を聞く確率 +0.25 → +0.20。支援（4 段）は「抽選してから +1」をやめて傾き exp(0.8·段)。取り返し: 負けの後 8 判断は sad + 0.4、β_break 1.5 → 4。気分の記録に `lossStreak`・`last`。指標 M4（気分で分ける・hard は同じ質問どうし）・M6（`priorProbabilities`）・M8（同じ気分帯・偶然の JS を引く）の数え方を変えた。

- **rules 4.2（2026-10-28 夜）**: 店への投資と勇者の装備を判断に足した（洗い出しは `Docs/Player_Actions_Inventory.md`）。
  質問 `hero_weapon` / `hero_armor` / `hero_tactics`（配信の前の営業日）、`shop_upgrade` / `smith_upgrade` / `broker_upgrade`（真偽）、`machine`、`machine_upgrade` / `machine_repair`（真偽。2026-10-29）、`finance` ＋ `finance_share`。
  押すのは AutoPlayController（勇者・情報屋・取引所は仕入れの前、鍛冶屋の開発はお任せ購入の前、店の改装・設備・魔王軍支援は陳列フェーズの頭 = `DebugAutoPlayer.ExternalBeforeDisplay`）。
  魔王軍支援は、ボタンが陳列フェーズにしか無いのに仕入れの前に押そうとしていて、それまで一度も払われていなかった（直した）。
  結果: 装備は配信の翌朝までの資産、それ以外はランの終わりまでの資産（horizon `runEnd`。資産に取引所の評価額を足した）。押せなかったものは「しなかった」として学ぶ（ログ `shop_invest_result`）。
  「絶対しない」を作らない下限（真偽 0.12・選択肢に一様 15%）を入れ、自由な質問の固まり対策（エントロピーの下限・安全弁）からは外した。手持ちの下限と 3 日以内の返済は残す（`shop_invest_guard`）。
- **クリア→初めから**: 村の総合Lv が 30（全施設 Lv5）になったセーブは、次のランの前に `Library/PersonaMarathonSaves_cleared/<人格>_<日時>/` へ移して新しいセーブで始める（`reset_village_level`、0 でしない）。progress.log・summary.md の「クリア→初めから」、履歴は同じフォルダの `clears.jsonl`（何周で届いたか）。記憶・学習器・想起はそのまま。
- **フォーカスが無くても進める（2026-10-28 夜）**: マラソンと実況の録画プリセットの Play の間だけ `AutoPlayInputGuard.Engage` で `Application.runInBackground = true` にし、同時に Input System の全デバイスを無効にする（つながり直したもの・有効に戻ったものも毎フレーム止め直す。旧 Input Manager を読む DebugResetManager の Esc 10 回・DebugMenuView の F11 も読まない）。Play を抜けると戻し、`PlayerSettings.runInBackground` もランの前の値に戻す。開始時とフォーカスが変わるたびに、止めたキーボード・パッドへ擬似入力（Esc / Enter / A）を入れて届かないことを Editor.log（`[InputGuard] 遮断の確認`）に出す。心拍に `focused` / `runInBackground` / `pausedNoFocus` / `inputGuard*`。見張り役は `pausedNoFocus` の間は固まりと見なさず待つ。開き直すときは最小化・非アクティブで起動を頼む（Unity が守るかは未確認）。

## 相談プレイ（ずんだもんが遊び、めたんに相談）

`PersonaStrategy(PersonaProfile.Zundamon, PersonaProfile.Metan)`（デバッグメニューの戦略「人格 相談」、マラソンの人格 `consult`）。
判断ごとに 2 人のモデルへ聞き（GPU では 2 つのモデルが VRAM 12 GB に同時に載らないので Ollama が入れ替える。読み込み済みの方から聞いて入れ替えを 1 回にする）、
`PersonaConsult`（`Assets/Scripts/Debug/AutoPlay/Jev/PersonaConsult.cs`）が掛け合いを作る。

1. ずんだもんの案（kev 4B・彼の選び方。一言はいつものテンプレートとミーム）
2. めたんの助言（kev 9B・彼女の選び方と決まり）: 賛成 / 条件つき（段階が 1 つ違う・自信が低い）/ 反対。理由は画面の数字（手持ち・返済・弱点・情報料・支援の額・勇者の HP・村資金）
3. ずんだもんが決める: 聞く確率 = 0.42（条件つき 0.6）+ 0.4 ×（めたんの確信 − 0.5）− 0.4 ×（自分の確信 − 0.5）+ 信頼（質問の種類ごと、±0.25）− 自信満々なら 0.1 + 返済が怖いとき 0.15（0.1〜0.85）
4. 結果が出たら（翌朝・配信の後・ランの終わり）、決着のついた反対・条件つきのうち一番大きく動いた 1 件にだけ後日談: 聞かずに外した → めたん「だから言ったでしょ」＋ずんだもんが泣く、聞かずに当てた → マウント、聞いて当てた → 渋々のお礼、聞いて外した → 文句

- 声に出さない判断: 2 人が同じで新聞・村の施設ごと（黙って決める）、新聞で意見が割れても 7 割は口に出さない、同じ判断が続いたらめたんは半分うなずくだけ。武器の銘柄は相談しない。
- 配信中は山場（最初の 1 発・勇者の HP が 3 割を切った・ボス）だけ、投げる前に聞く（1 配信 3 回まで）。
- 学び: どちらの想起・学習器にも、実際に選ばれた手の結果を足す。`UserSettings/PersonaMemory/consult.json` にずんだもんの「聞くと得か」の信頼（質問の種類ごと、1 件 ±0.04）とめたんの助言の成績。後日談の 1 件は両方の教訓（英語）にもなる。
- ミーム音: `ConsultSounds.Scenes` に、index.json の usage.fit を読んで合うと確かめた音だけを場面ごとに書いてある（読み込み時にも use・verified・who を確かめる）。確率 0.4、1 ラン 8 回まで、同じ音は 1 ランに 1 回。
- 会話ログ: `Recordings/consult_log_<ラン ID>.jsonl`（1 行 = 1 セリフか 1 判断: time / unixMs / day / phase / gamePhase / exchange / seq / kind / speaker / line / e（jikkyo.py の表情）/ meme / snd・sndTiming / decision（2 人の答え・確率・確信・関係・聞く確率・聞いたか・最終）/ result（損益・勝った方））。
  台本へ: `py -3 -X utf8 tools/persona_play/consult_to_script.py Recordings/consult_log_<ラン ID>.jsonl [--video <録画>] [--out <台本.json>]`（レンダーはしない）。
- セーブ: `Library/PersonaMarathonSaves/consult/main`（新しく始める）。試走: `py -3 -X utf8 tools/persona_play/marathon/marathon.py --personas consult --max-runs 1 --laps 1 --no-record`。録画は `MarathonC_*`。

## ミーム（一言に混ぜる流行りのネタ）

`Assets/Scripts/Debug/AutoPlay/Jev/PersonaLines.cs` の `ApplyMeme`。辞書は `tools/persona_play/memes/memes_YYYY-MM.json`（形と集め方は `memes/README.md`）。
テンプレートの一言をいつもどおり選んだあとで、合うミームがあれば確率で差し替える。

- **合うミーム**: エントリの `question_keys` に、その一言の `質問` か `質問|帯`（結果なら `stream_result|win` など）がある。同じミームは 1 ランで 2 回使わない。
  重みは `trend_score × weight`。`weight` が 0.1 未満・`status: "retired"`・`retired: true` は使わない（`weight` が下がったものほど出にくい）。
- **ずんだもん**（`who` が `zundamon` / `both`）: 差し替える確率 0.15。山場（配信・ランの結果、予算・張る側が High）は 0.3、結果で損益が大きく動いたとき（配信 ±2 万G・ラン ±5 万G・破産）は 0.4。
  - 一言は `zundamon_line`。テンプレートの頭の 1 文に選んだもの・割合が入っていれば「`100%使うのだ！ 今のぼく、ドパいのだ！ 全部張るのだ！`」のようにつなぐ（48 文字を超えるならミームだけ）。
  - 寄せ方: 読みが当たった結果（マウント）は `smug` を 3 倍、負けの結果は `sad` / `panic` を 2 倍、予算・スパチャ・張る側の High（自信過剰の賭け）は `hype` を 1.5 倍。
- **めたん**（自分からは出さない）: 確率 0.08、山場 0.15。
  - `who` が `zundamon` / `both` のミーム → `metan_reaction`（ずんだもんのミームへの返し）。ログの `memeCue` にきっかけの `zundamon_line` を残す。
  - `who` が `metan_reluctant` のミーム → 1 ランに 1 回だけ、めたん自身が使う。「知らない」「元ネタじゃない」が入っていなければ末尾に「……知らないけど」を付ける。
- **回数**: 1 ランに、ずんだもん 5 回まで（結果以外の一言では 3 回・山場でない場面では 2 回まで）、めたんの返しは 3 回まで（結果以外 2 回・山場でない場面 1 回）と渋々 1 回。
  前のミームから一言を 48 行（山場 24 行・結果 24 行）空ける。1 ランの判断は 100 行を超えるので、これが無いと最初の 1〜2 日で使い切る。回数・使ったミームはランの始まりに 0 に戻る。
- **乱数**: 人格の乱数と同じ種から作った別系統（`PersonaStrategy` の `_memes`）。同じ種なら同じミームが出て、ミームの有無で判断やテンプレートの乱数列は変わらない。
- **禁止語**: 差し替えた後の一言にも `IsClean`（「効いて」「効いた」「回目」）をかけ、当たったらミームをやめてテンプレートの一言に戻す。

### 辞書の更新

毎朝 07:00 にタスク `MemeCollector` が `C:\Users\yanor\Desktop\Meme` で `collector\collect.mjs` → `collector\digest.mjs` を走らせ、`Meme\data\lexicon.json`（減衰つきの辞書）を更新する。月 1 回くらい、書き出してこのリポジトリに置く（手順の詳細は `memes/README.md`）。

```powershell
cd C:\Users\yanor\Desktop\Meme
node collector\digest.mjs --export D:\WorkTree\TomsLands\<作業ツリー>\tools\persona_play\memes\memes_YYYY-MM.json
cd D:\WorkTree\TomsLands\<作業ツリー>
py -3 -I tools/persona_play/memes/check_lexicon.py tools/persona_play/memes/memes_YYYY-MM.json
```

- `--export` は `retired`（weight 0.1 未満）を書き出さない。残ったものも `weight` で出にくくなる。
- `check_lexicon.py`（標準ライブラリだけ）は、必須項目・`who`・`question_keys` が `PersonaLines.cs` のキーにあるか・禁止語・id の重複を見て、C# と同じ規則でサンプルの一言 10 件と 1 ランの回数の試算を出す。エラーがあれば終了コード 1。
- 新しいファイルを使うときは `UserSettings/PersonaPlay.json` の `memeLexiconPath` を書き換える（次の Play から読み直す）。

## 夜通し回す（人格マラソン）

ずんだもん → めたん → … と交互に、ユーザーの操作なしで朝まで回す。エディタは開いたまま、1 ランごとに Play に入り直す。

- **セーブは人格ごとに 1 つ**（`Library/PersonaMarathonSaves/<persona>/main/`、隔離セーブ。ずんだもんとめたんは混ぜない）。毎ラン「続きから」遊び、村・村資金・施設・終えたラン数が次に引き継がれる。
  1 本は 2 周（録画なしの既定）で、最後の周はリザルトのボタンで精算してから村に着いたところで止める（`AutoPlayRunEndAction.SettleAndStopAtVillage`）。セーブが無ければ新しく始める（progress.log に記録）。破産でもゲームは村へ戻って続く。
- **村を育てる**: 村に着いたら施設を見て回る前に 1 リクエスト（`village_budget` = 村資金のうち使う割合、`invest_<施設ID>` = いま投資できる施設ごとの noul）。
  ずんだもんは派手な施設（広場・ギルド・職人組合）を ×1.6 して抽選、めたんは閾値 0.5 で村資金 1 万G を残す。予算の中で、投資したい度合いの高い順に予定を立てて押す。
  想起・学習器の種類 `village`（特徴 11 個: ラン数・村資金・投資できる施設の割合・領主館 Lv・その施設の Lv・費用/村資金・領主館か・前のランの儲け・村の格・建った施設の割合・総合Lv の割合）。
  結果は次のラン（1 周）の所持金の増減（horizon `nextRun`）。投資の結果は判断ログの `village_result` の行（投資額・村資金・総合Lv）。
  営業日・配信の特徴にも村の総合Lv の割合を足した（11 個。ルールの版 `rules` = 3。版 2 の想起・学習は使わない）。

```powershell
# 始める（裏で動く。PowerShell は閉じてよい）
powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/start.ps1
# 止める（走っているランを止め、Ollama を GPU に戻し、まとめを書いて終わる）
powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/stop.ps1
# すぐ止める（見張り役を終わらせ、エディタに止める指示だけ出す）
powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/stop.ps1 -Force
# 一時停止（今のランが終わったら止める。エディタは開いたまま待機。-Now ですぐ止める）
powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/pause.ps1
# 再開（前回と同じ設定ファイル・同じセーブの続きから）
powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/resume.ps1
```

- **学習と撮影は分ける（2026-10-28）**: 設定の `"mode": "learn"` で録画しない・Ollama は GPU・記憶を育てる（`never_record` などをまとめて設定）。`"mode": "video"`（記憶を読むだけで撮る）はまだ C# 側の読み取り専用が無いので、指定すると始めずに止まる。長く回すときは `"stop_at": "2026-10-30T23:00"`（日付つき）・`"restart_every_hours": 6`（ランの合間に Unity をふつうに閉じて開き直し、Ollama も立て直す）。2 日目からは `progress_<日付>.log` に書き、`summary_<日付>.md` にその日の最後のまとめを残す。
  例: `{"mode": "learn", "persona_pattern": ["consult", "consult", "zundamon", "metan"], "stop_at": "2026-10-30T23:00", "no_new_run_after": "2026-10-30T22:15", "restart_every_hours": 6}`

- **録画**: ふだんは録画しない（Ollama は GPU、x2・会話は飛ばす・音は消す）。前の録画から 2 時間たったら 1 本録画する（Ollama は CPU だけ、等速・撮影プリセットと同じ設定、`Recordings/MarathonZ_<ラン ID>_*.mp4`）。録画ランは音が鳴る（録画に音を入れるため Mute を外す）。08:00 までに終わらない・D: の空きが 30 GB 未満なら録画しない。
- **時刻**: 07:30 を過ぎたら新しいランを始めない。07:54 に走っているランへ止める指示、08:00 を過ぎても Play が終わらなければ Unity を強制終了。
- **見張り**: 心拍（`Library/PersonaMarathon/heartbeat.json`、5 秒ごと）が 4 分来ない・12 分進まない・考え中のまま 6 分・Windows の「応答なし」が 3 分・1 ランが 100 分（録画 85 分）を超えた → Unity を強制終了して `-projectPath` で開き直し、同じ隔離セーブを「続きから」遊ぶ（終わった周の数も引き継ぐ）。Unity は元気でランだけ途中で止まった（詰まり検知など）ときは、1 度だけ落とさずに続きから。3 回続けて失敗したらやめる。
- **ユーザーのセーブ**: 開始時に `%USERPROFILE%\AppData\LocalLow\MozaicWorks\TomSLand` を控え（`Player.log` / `Player-prev.log` を除く）、毎ランの後に md5 で比べる。違えば全部止める。`remoteconfig/`（起動のたびに書き直されるダウンロードのキャッシュ）は記録だけして止めない。
  会話エンジン（宴）は隔離セーブを使わずユーザーのフォルダの `Utage/SaveTomsScenario/`（既読・自動セーブ）に書くので、ここだけは違っていたら開始時の控えから戻してから確かめ直す（`user_save_restore`、progress.log と state.json の `restored` に記録）。
- **出力**: `Recordings/marathon_<日時>/progress.log`（進み具合）・`summary.md`（朝のまとめ: ラン・人格・録画・周ごとの所持金・配信の賭けの勝ち負け・判断の秒数・開き直し・選び方と学習の前半 → 後半）・`state.json`・`user_save_backup/`・`ollama.log`。判断ログは `Recordings/persona_log_<日時>_<ラン ID>.jsonl`。
- **混ぜて回す（2026-10-28 午後から）**: `"persona_pattern": ["consult", "consult", "zundamon", "metan"]` でランの番号ごとに人格を繰り返す（`personas` / `first_persona` より優先）。`"record_personas": ["consult"]` なら録画するのは相談ランだけ（2 時間の番が 1 人プレイに来たら録画は次の相談ランへ回す。間隔は前の録画ランの開始から）。`"last_record_at": "2026-10-28T14:37:00"` で前のマラソンの録画から間隔を引き継ぐ。1 人プレイのセーブは `Library/PersonaMarathonSaves/<zundamon|metan>/main`、記憶・想起・学習器（`UserSettings/PersonaMemory/<persona>*.json`）は相談プレイと共有（相談ではずんだもんが遊び手、めたんは助言者として同じファイルから学ぶ）。混ぜたときは毎ランの後のまとめにも人格の指標（単独 / 相談の 2 つの表）を足す。
- **設定**: `tools/persona_play/marathon/config.json`（任意。`marathon.py` の `DEFAULTS` のキーを上書き。例 `{"laps_growth": 3, "time_scale_growth": 1.5}`）。
- **エディタとのやり取り**: `Library/PersonaMarathon/` の `command.json`（見張り役 → エディタ）・`status.json` / `heartbeat.json`（エディタ → 見張り役）・`stop.flag`（今のランを止める）・`runner_stop.flag`（stop.ps1 → 見張り役）。エディタ側は `Assets/Scripts/Editor/Recording/PersonaMarathon.cs`。
- **試す（Unity も Ollama も使わない）**: `py -3 tools/persona_play/marathon/marathon.py --dry-run --sim-scenario hang`（筋書き: ok / hang / crash / abort / fail3 / thinking / notresp / save）、単体テスト `py -3 -m unittest tools/persona_play/marathon/test_marathon.py -v`。

## 人格の指標（persona_metrics、設計の P0）

`tools/persona_play/metrics/persona_metrics.py`（標準ライブラリだけ・ログを読むだけ）。`Docs/Persona_Formation_Design.md` の 6 章の M1〜M14 を数え、目標の範囲と OK / NG を出す。

```powershell
# マラソン 1 回ぶん（判断ログは同じ Recordings から探す）。metrics.json / metrics.md をマラソンのフォルダに書く
py -3 -X utf8 tools/persona_play/metrics/persona_metrics.py --marathon Recordings/marathon_20261028_0215
# ログを直接（--logs は何回でも）。--persona zundamon|metan|consult で絞る。--out で書き先、--no-write で表だけ
py -3 -X utf8 tools/persona_play/metrics/persona_metrics.py --logs "Recordings/persona_log_*_0953_*.jsonl" --logs "Recordings/consult_log_20261028_0953_*.jsonl" --out <フォルダ>
# 単体テスト
py -3 -m unittest tools/persona_play/metrics/test_persona_metrics.py -v
```

- 単独プレイ（Z / M）と相談（ずんだもんの案・めたんの助言）を分けて数える。相談の大胆さ・偏りは「最終の手」ではなく、それぞれの案（`proposal` / consult_log の `m.key`）で見る。
- **赤**: M2（固まり）・M4（負けへの反応）・M9（助言を聞く率）・M10（マウント : 泣き）のどれかが範囲外なら「人格の回帰」の行を出す（`--fail-on-red` で終了コード 2）。
- 書きかけのログ（走っているマラソン）も読める（壊れた最終行は飛ばす）。走っている間は `--out` で別のフォルダに書く。
- いまのログで数えられないもの（表情は相談だけ・気分・流儀の事前分布・褒め / 崩れの印・λ）は表の「-」と `metrics.json` の `notComputable` に、足すべきログの項目（P1〜P6）と一緒に出す。
- `marathon.py` にはまだつないでいない。つなぐときは `finish()` の `path = write_summary(self)` の直後に
  `persona_metrics.append_to_summary(self.out_dir)` を try / except で呼ぶ（summary.md の先頭に赤の行、末尾に表。何度呼んでも 1 つに差し替わる）。

## 注意

- **録画中はモデルを CPU で動かす。** GPU だと 9B は約 8.9 GB、4B は約 8.3 GB の VRAM を使い（/v1/systemone はコンテキスト 73,728 固定で、`OLLAMA_CONTEXT_LENGTH`・`OLLAMA_GPU_OVERHEAD` が効かない）、
  2026-10-27 に 1080p 録画中の Unity が「Failed to present D3D11 swapchain due to device reset/removed」で落ちた（ボス戦の直前）。
  CPU だけで動かすときは、トレイの Ollama を終了してから PowerShell で:
  `$env:OLLAMA_MODELS='E:\Ollama\models'; $env:CUDA_VISIBLE_DEVICES='-1'; $env:OLLAMA_VULKAN='0'; $env:GGML_VK_VISIBLE_DEVICES='-1'; $env:OLLAMA_KEEP_ALIVE='2h'; Start-Process 'E:\Ollama\app\ollama.exe' serve -WindowStyle Hidden`
  （`CUDA_VISIBLE_DEVICES` だけだと Ollama 0.40 は Vulkan で GPU に載せる。`/api/ps` の `size_vram` が 0 か確かめる）
  判断 1 回は CPU で約 22〜30 秒（Unity の Play 中。GPU なら 0.4〜3 秒）。`timeoutSec` は 90 にしてある。
- 2 人を同時には動かさない。
- 一言のテンプレートに「〜が効いてくる」「全額○回目」は入れない（出す直前にも検査している）。
