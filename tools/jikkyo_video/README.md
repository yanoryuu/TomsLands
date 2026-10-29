# 実況動画メーカー（ずんだもん × 四国めたん）

「トコのトムトムランド」の実況動画を、台本（JSON）→ VOICEVOX の音声 → ffmpeg 合成で作る。
調査と3本の設計は `research.md`。台本は `scripts/`。

| 台本 | 軸 | 推定尺 |
|---|---|---|
| `A_tantan` | 淡々プレイ（ゲーミングボーイ型・一話完結・説明最小） | 約10分 |
| `B_kaisetsu` | 解説（結論→仕組み3つ→実演→攻略） | 約10分 |
| `C_play` | 普通のプレイ動画（掛け合い・リアクション・ミーム多め） | 約10分 |

## 必要なもの

- VOICEVOX（エンジンが `http://127.0.0.1:50021` で動いていること。アプリを起動しておけばよい）
- ffmpeg / ffprobe（PATH に通っていること。libass・libx264 入り）
- Python 3.10+、Pillow、numpy
- フォント: Zen Kaku Gothic New（Google Fonts）、GN キルゴ U（フリー）、M PLUS Rounded 1c。`assets/fonts/` にコピー済み

## 作り方（YouTuber の流れ）

```
cd tools/jikkyo_video
py -3 -X utf8 jikkyo.py assets          # 初回だけ。ミームカード・ゲーム内の絵・顔アイコン・効果音・フォント
py -3 -X utf8 jikkyo.py plan            # 台本の行数・推定尺
py -3 -X utf8 jikkyo.py all A_tantan    # 音声 → 合成 → サムネ（out/A_tantan/）
py -3 -X utf8 jikkyo.py all             # 3本ぜんぶ
py -3 -X utf8 jikkyo.py render C_play --preview   # 画質を落として速く確認
```

出力（`out/<台本>/`、git 管理外）

| ファイル | 内容 |
|---|---|
| `<台本>.mp4` | 完成動画（1920×1080・30fps・H.264/AAC） |
| `thumb.png` | サムネ（1280×720） |
| `<台本>.srt` | 焼き込みとは別の素朴な字幕ファイル（YouTube にアップする用） |
| `youtube.txt` | タイトル・説明文・タグ |
| `timeline.json` | 行ごとの開始・終了秒（手直しの参考） |

## ゲーム映像の撮り方

いまの `footage.json` は 2026-04 の古い録画（`D:/TomsScreenShot/Movie_00*.mp4`）を**仮**に割り当ててある。
本番の素材は Unity で撮る:

1. Unity で **Tools/TomsLands/自動録画/実況動画用に録画して Play（初回ラン・ラン終了まで）**。
   TitleScene を開いて Play に入り、録画とオートプレイが同時に始まる（隔離セーブなのでユーザーのセーブは触らない。Game ビューのミュートも外す）。
   ラン終了（リザルト／ゲームオーバー）で自動停止し、`Recordings/Jikkyo_yyyyMMdd_HHmmss.mp4` に保存される。
2. 動画を見て、`footage.json` の `tags` を書き換える。タグは台本の章の `footage` と対応する:

   | タグ | 場面 |
   |---|---|
   | title / prologue | タイトル／開店の会話 |
   | blacksmith / display / sales / summary | 仕入れ／陳列／営業／精算 |
   | newspaper / info / prophet / map | 新聞／情報屋／占い師／地図 |
   | hero / demon / stream_prep / stream / stream_result | 勇者の装備／魔王軍支援／配信の準備／配信中／配信のリザルト |
   | tax / result / gameover | 納付／ラン終了 |

   `start` は素材の開始秒。章の長さだけ使い、足りなければ素材の頭からループする。
3. 配信の勝ち負けを `outcomes`（`stream1` / `stream2` を `win` / `lose`）に書く。台本の `alt` 付きのセリフがそれに合わせて切り替わる。
4. `jikkyo.py all` で作り直す（音声はキャッシュされるので2回目からは速い）。

## 台本の書き方（`scripts/*.json`）

```json
{ "s": "z", "e": "tsun", "t": "セリフ", "img": "meme_kusa", "telop": "上に出す短い文字",
  "on": "stream1", "alt": { "win": "勝ったとき", "lose": "負けたとき" } }
{ "pause": 3.0 }
```

- `s`: `z`=ずんだもん / `m`=四国めたん
- `e`: 感情。`normal` / `ama`（あまあま）/ `tsun` / `sexy` / `sasayaki` / `hiso`、ずんだもんだけ `hero`（ヘロヘロ）/ `namida`。声のスタイルと字幕横の顔アイコンが変わる
- `img`: 差し込み画像。`assets/memes/`（ミームの文字カード）か `assets/cards/`（生成イラスト `gen_*`・ゲーム内の絵 `game_*`）のファイル名。そのセリフの間だけ出て、次の画像で入れ替わる
- `telop`: 画面上部のキーワード（解説向け）
- 章に `card` を書くと章カード（帯）が出る
- 軸ごとの演出（話速・間・字幕サイズ・BGM の音量・名前タグ）は `jikkyo.py` の `STYLE_PRESET`
- 四国めたん（左）の立ち絵は、右のずんだもんの方を向くよう左右反転して重ねる（`STYLE_PRESET` の `flip_m`、既定 true。表情・口パクの全部に効く。字幕・画像・UI は反転しない）。前の向きに戻すなら台本に `"flip_m": false` か `JIKKYO_FLIP_M=0`

## BGM（CC0）とゲーム音の大きさ

台本に BGM の計画（`"bgm": [...]` / `{"plan": [...]}` / `"auto"` / `"<name>.bgm.json"`）を書いたときだけ、ゲームの BGM の代わりに
`assets/bgm_cc0/` の CC0 曲を区間ごとに流す（クロスフェード・区間いっぱいのくり返し・声の下で自動で下げる・大勝ちの一発）。
同時にゲーム（録画）の音は `game_vol`（既定 0.10、行・章ごとに上げられる）まで下げる。計画の無い台本は今まで通りに書き出される。
案は `py -3 jikkyo.py bgmplan <name>`、書き方と曲の一覧は `assets/bgm_cc0/README.md` と `bgm.py` の冒頭、確認は `py -3 -I test_bgm.py`。
これからの録画は、録画中だけゲームの BGM を 0（効果音は既定のまま）にして撮る（Unity の `Tools/TomsLands/自動録画/録画にゲームの BGM を入れる` で戻せる）。

## 素材の権利

- 声: VOICEVOX ずんだもん／四国めたん。動画の説明文とエンドカードに「VOICEVOX:ずんだもん」「VOICEVOX:四国めたん」と書く（`assets/chars/*_policy.txt`）
- 立ち絵: 坂本アヒル氏の「ずんだもん立ち絵素材V3.2（基本版）」「四国めたん立ち絵素材2.1」（ニコニコ静画 im10788496 / im10791276。readme: 公式の規約の範囲なら動画等に自由に使える、クレジット表記は任意）。PSD を `assets/tachie_src/zundamon.psd`, `metan.psd` に置いて `py -3 -X utf8 tachie.py` を実行すると、感情 × 口の開閉ごとの PNG が `assets/tachie/` に出る（git には入れない）。感情→レイヤーの対応は `tachie.py` の `ZUNDA` / `METAN`。`assets/tachie/` が無いときは VOICEVOX 公式の立ち絵＋顔アイコン（`assets/chars/`）で代用する。エンドカードと説明文に「立ち絵：坂本アヒル」を入れる
- テロップ（`telop`）は章の見出しと、最後のまとめ程度に絞る。セリフの要約を毎回出すと画面がうるさい（B 案で 38 本→8 本に減らした）
- 差し込みイラスト `assets/cutins/gen_*.png`: gpt-image-2 で生成（文字なし・ステッカー風）
- ミームカード: 文字だけを Pillow で描いたもの。権利物の画像は使っていない
- BGM・効果音・ゲーム内の絵: このプロジェクトのもの（効果音の pop/ding/whoosh/boom は numpy で合成）。BGM の計画で使う曲は Freesound の CC0（`assets/bgm_cc0/README.md`。表記は不要）
