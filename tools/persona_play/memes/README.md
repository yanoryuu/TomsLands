# ミーム辞書（ずんだもん・四国めたん用）

ペルソナ bot が「トコのトムトムランド」を遊びながら出す一言に、いま日本の X で流行っているミームを混ぜるための辞書です。人物像は `tools/jikkyo_video/CHARACTERS.md`（ずんだもんはミーム好きで声と表情で真似る、めたんは知らないふりをして乗るのは 1 本に 1 回）に合わせています。

- `memes_2026-10.json` … 2026 年 10 月版。102 件（2026-10-28 に 45 件から拡張。日本の X で流行っているもの 102 件、元ネタが海外の猫動画など 23 件。猫ミーム 23 件）
- `memes_2026-10.v1.json` … 拡張前の 45 件（2026-10-27 版）。比較用に残す
- 毎朝の収集と辞書の更新は `C:\Users\yanor\Desktop\Meme`（朝獲れミーム市場）の `collector/` 側で動く。このフォルダには月 1 回、確定版を書き出して置く

## エントリの形

| 項目 | 内容 |
|---|---|
| `id` | 英小文字の識別子。台本・実況動画の `assets/memes_user/<id>` にもそのまま使える |
| `phrase` | 口に出すときの形。`〇〇` は差し替える部分、`（…）` は声と表情で再現する画像・動画ミーム |
| `meaning` | 意味（1 行） |
| `usage` | ゲームのどの場面に合うか（`big_win` / `big_loss` / `overconfident_bet` / `failed_prediction` / `being_right` / `waiting` / `buying_info` / `skip_info` / `stream_reversal` / `support_choice` / `mount` / `metan_tease` など） |
| `question_keys` | `Assets/Scripts/Debug/AutoPlay/Jev/PersonaLines.cs` のキー（`question`、`question|帯`、`run_result|win` など）への対応 |
| `tone` | `hype` / `sad` / `smug` / `deadpan` / `panic` / `hero` / `surprise` |
| `who` | `zundamon`（ずんだもんが使う）/ `metan_reluctant`（めたんが渋々使う・乗る）/ `both` |
| `zundamon_line` / `metan_reaction` | 例文。ずんだもんは「〜のだ」、めたんは知らないふりか短いツッコミ |
| `sources` | 出典 URL と日付 |
| `trend_score` | 1〜5。複数の調査・解説記事に出ている、X で数万いいね規模の拡散が確認できる、ほど高い |
| `first_seen_estimate` | 流行り始めの推定 |
| `via` | `article`（解説記事・調査から）/ `collector`（収集ツールの X 投稿から） |
| `region` / `origin` | 流行っている地域 / 元ネタの出どころ（`jp` / `intl`） |
| `last_seen` / `weight` | 最後に見かけた日と減衰の重み（収集ツールの digest が更新） |
| `status` | `active` だけが書き出される（`retired` は減衰、`dropped` は外したもの。lexicon.json に理由つきで残る） |
| `asset_hint` | 猫ミームだけ。`tools/jikkyo_video/assets/memes_user/` の GB 素材のファイル名（素材が無いものは空）。`asset_name` は素材の通称 |
| `match` | 収集ツールの digest が新着投稿と照らし合わせる語（無ければ `phrase` から作る） |

## どう集めたか（2026-10-27）

1. **解説記事・調査**（agent-reach の Exa 検索と Jina Reader）
   - バズネタ解説「2026年10月に流行ったネットミームまとめ」と個別の解説記事、ミーム大百科「2026年に流行したネットミーム総まとめ」「構文系ミーム24選」、渋谷トレンドリサーチ（2026 年秋）、おもちゃいろ（若者言葉 2026、マイナビティーンズラボ・AMF の調査を含む）、TORIHADA POST（2026 年 9 月号）
   - Exa は無料枠の上限（HTTP 429）に当たったため、途中から記事本文を Jina Reader で直接読んだ
2. **収集ツール**（`C:\Users\yanor\Desktop\Meme\data\memes.json`、twitter-cli の `user-posts`）
   - X の検索は upstream（twitter-cli 0.8.5）で壊れていて使えない。トレンドを直接取る手段もない。日本の X での流行は、解説記事と調査の記述から間接的に判断している
   - 英語圏の投稿は画像を見て意味を確かめ、日本でも通じる言い回しにできるものだけ残した

## 2026-10-28 の拡張（45 → 102 件）

- 情報源: 収集ツールの今夜の日本語投稿（ねとらぼ・posfie の反応の言い回し 4 件）、バズネタ解説の個別記事（10 月のまとめ・ネットミーム／ネットスラングの一覧・流行語大賞 2026 予想）、ミーム大百科、おもちゃいろ（Z 世代の流行語）、ピクシブ百科事典（猫ミーム・現場猫・完全に理解した）。Exa は使っていない（無料枠切れ）
- 猫ミーム 23 件: id は `meme_cat_<name>`。うち 20 件は GB 素材（YouTube「猫ミーム資料館」のフリー素材）のファイル名と同じ id。拍手する黒猫は GB 版が AI 生成なので素材なし（`asset_hint` は空）
- 外したもの（lexicon.json に `dropped` で残る）: `skrr`・`teketeke`・`gugugaga`（文字の一言では場面と結びつかない）、`heroes_capes`・`shots_miss`・`maxxing`（海外の言い回しで日本では通じにくい）、`black_cat_clap`（`meme_cat_black_clap` に改名）
- 書き直したもの: `reishou`・`bakuretsu`・`e_suki`・`maratan`・`anata_32`・`dundun` の一言と質問キー。ほか十数件は質問キーを足しただけ
- めたんが渋々使う（`metan_reluctant`）は 7 → 15 件

## 入れないもの（フィルタ）

次のどれかに当たるものは入れない。迷ったら入れない。

- 実在の私人をからかう・いじるもの、嫌がらせ、罵倒（例：「キモいんだよ!消えろ!」から始まる構文）
- 差別語・差別的な画像、国籍のステレオタイプ、領土ネタ
- 性的なもの・性的な含みのあるもの（名前や出自を含む）
- 政治・選挙・政治家の話し方の構文
- 宗教
- 悲劇・災害・事故・訃報・犯罪とその被害（情報漏洩事件をもじった構文、飲酒運転の映像なども含む）
- 誰かの実生活の不幸を笑うもの
- 暴力的な言い回しが流行の形になっているもの（「殺すぞって感じの〜」など）
- 意味を確かめられなかったもの
- 長い引用（短い決めゼリフだけにする）
- 使わない言い回し「効いて」「効いた」「〜が効いてくる」「全額○回目」（memory: jikkyo-phrase-bans）

## 毎月の更新手順

毎朝 07:00 にタスク `MemeCollector` が `collect.mjs` → `digest.mjs` を走らせ、`Meme\data\digest\<日付>.json` に候補、`Meme\data\lexicon.json` に辞書（減衰つき）を書く。月 1 回、次をする。

1. 解説記事で言い回し系を補う（収集ツールは画像つき投稿とトレンド系アカウントが中心で、流行語は取りこぼす）
   ```bash
   agent-reach doctor --json                      # twitter の active_backend とチャネルの状態を見る
   mcporter call exa.web_search_exa query="2026年11月 ネットミーム まとめ" numResults=8
   mcporter call exa.web_search_exa query="最近Xで流行っている 構文 元ネタ" numResults=8
   curl -s "https://r.jina.ai/https://buzzneta-kaisetsu.com/meme-matome-2026-11/"   # 月別まとめ（URL は月で変わる）
   curl -s "https://r.jina.ai/https://memehyakka.com/features/b0620056-1d08-4843-adce-a1d58b66c5d9"
   ```
   - Exa が 429 を返したら、記事の URL を直接 Jina Reader で読む
   - 意味はニコニコ大百科・ピクシブ百科事典や個別の解説記事で確かめる
2. 収集ツールの候補を解釈する（Claude Code に頼む）
   - 「`C:\Users\yanor\Desktop\Meme\data\digest\` の新しいファイルを読んで、ミームとして通じるものを辞書の形式に直して `interpreted-<日付>.json` に書き、`node collector\digest.mjs --merge` で取り込んで」
   - 画像は WebFetch で見るだけで保存しない。`--merge` は必須項目の欠け・NG 語・使わない言い回し・重複を弾く
3. 書き出す
   ```bash
   cd C:\Users\yanor\Desktop\Meme
   node collector\digest.mjs --export D:\WorkTree\TomsLands\<作業ツリー>\tools\persona_play\memes\memes_YYYY-MM.json
   ```
   `weight` が 0.1 未満（30 日の半減期で約 100 日見かけていない）のものは `retired` になり、書き出しに入らない。
   既存のエントリを書き直すときは `--merge <file> --upsert`（同じ id を差し替え）。入力の `drop: [{id, reason}]` は `dropped` にして書き出さない。
   `weight` は書き出した日の日付で減衰を計算し直すので、同じ日に `--export` すれば同じファイルになる
4. `grep -E "効いて|効いた|回目"` で使わない言い回しが無いか見る

## ペルソナのコードでの使い方（提案）

> 2026-10-28 に `PersonaLines.ApplyMeme` として実装した。確率・回数・間の空け方の確定値は `tools/persona_play/README.md` の「ミーム」。

`PersonaLines.Pick` / `PickResult` の手前で、確率でミームの行に差し替える。

- **ずんだもん**
  - その判断の `question`（と帯・勝ち負け）に合う `question_keys` を持ち、`who` が `zundamon` か `both` のエントリから選ぶ。重みは `trend_score × weight`
  - 差し替える確率 p は 0.15 前後。感情が振り切れる場面（`stream_result` / `run_result`、`budget|High`、`stream_side|High`）だけ 0.3 に上げる。CHARACTERS.md の「1 本に 3〜5 回」に収まるよう、1 ランの上限を 5 回にする
  - 同じミームは 1 ランで 2 回使わない（使った `id` をラン単位で記録）。ただし CHARACTERS.md の「返ってくる構造」で、めたんが同じミームを返すのは可
  - マウントの場面（ずんだもんの読みが当たった `stream_result|win`、`run_result|win`）は `tone: smug` を優先する。マウントは 3 行以内
  - 大負けは `sad` / `panic` を優先し、次の判断では「次は当たるのだ」に戻す（泣いた 2 行後に前を向く）
- **めたん**
  - 自分からは使わない。ずんだもんがミームを出した直後に `metan_reaction` を返すのが基本
  - `who` が `metan_reluctant` のものは、1 ランに 1 回だけ、めたん自身が使ってよい。使った直後に「……知らないけど」「今のは元ネタじゃないから」を付けて照れる
  - 知ったかぶりの癖：元ネタをずんだもんが間違えたら、めたんが正しい続きを言ってしまう（1 本に 1 回）
- 出す直前の検査（`IsClean` の禁止語）はミームの行にもかける

## 実況動画用に元の画像・動画を用意する価値があるもの

猫ミームの GB 素材 20 本は `tools/jikkyo_video/assets/memes_user/README.md` を参照（各エントリの `asset_hint`）。

ダウンロードはしていない。`tools/jikkyo_video/assets/memes_user/<id>.<拡張子>` に置けば画面に出る。権利は各素材の規約に従い、生成画像は使わない。

| id | 何か | 元 |
|---|---|---|
| `crying_dog` | スマホを見て泣く犬 | 海外の GIF「Dog Scroll」とされる。X で「犬の検索履歴」として拡散（2026-10） |
| `hakanai` | 「どう見たって儚いだろ！」 | YouTuber あぞ「儚い言うてるやろ」（2026-08-15 公開）とその切り抜き |
| `waho` | 「ワホー」 | SeikinTV「ミンティア一気食いした結果・・・」（2014-09-26 公開）。本人の「ワホー2026」もある |
| `doku_maniae` | 「ヤバい！これ毒かも！間に合え間に合え！」 | ジェラードン公式 YouTube のコント「人生初の雪に触れて、死を覚悟するバカ」 |
| `nihilist_penguin` | 群れを離れて山へ歩くペンギン | 2007 年のドキュメンタリー映画（南極）の一場面 |
| `uso_tsukanaide` | 「私に嘘はつかないで」 | TBS 日曜劇場『VIVANT』。テレビ局の権利物なので扱いに注意 |
| `mise_daseru` | 「ジン、これ店出せるよ」 | 動画チャンネル「ブルーシー」のしらすの写真と組み合わせる大喜利 |
| `vs_darkrai` | VSダークライ | 2007 年のポケモン映画のタイトル |
