# 人格マラソンの YouTube ライブ（読み上げ＋OBS）

Unity エディタで回っている人格マラソン（`tools/persona_play/marathon`）を OBS で YouTube にライブ配信する。
実況は、ずんだもん・四国めたんの人格 AI がログに書いた一言（`Recordings/persona_log_*.jsonl` の `line`）を
VOICEVOX でそのまま読み上げるだけ（新しく文章を作らない）。配信中は PC に触らない前提。

- `live_voice.py` … いちばん新しい persona_log を追いかけて、一言を VOICEVOX で読み、既定の出力デバイスで鳴らす。
  OBS が読むテキストと立ち絵の PNG を `obs/` に書く。ウィンドウは出さず、フォーカスも取らない。
- 次のラン（新しい persona_log）ができたら、今のファイルを読み切ってから自動で乗り換える。
- 相談ラン（ラン ID が `_c` で終わる）では、めたんのセリフが入っている `Recordings/consult_log_<ラン ID>.jsonl` も一緒に追う。
  persona_log の一言は相談ログとほとんど同じなので、相談ログに無かった分（新聞・日の結果など）だけを 2 秒遅れで読む。
- ゲームは timeScale 2 で進むので、ログの時刻から 20 秒（`--max-age`）を過ぎた一言は読まずに捨てる。
  相談のやり取りは、途中が捨てられたら残りも捨てる（返事だけ聞こえないように）。1 行目は 14 秒を過ぎたら始めない。
  同じ人の同じ一言が続いたら 1 回だけ読む。

## 1. VOICEVOX を起こす

ふだんは `http://127.0.0.1:50021` に常駐している。落ちていたら、どちらかで裏で起こす（起動に 20 秒ほど）。

```powershell
# a) live_voice.py に任せる（落ちているときだけ run.exe を隠して起動する）
py -I -X utf8 tools/persona_play/live/live_voice.py --start-voicevox

# b) 手で起こす
Start-Process 'D:\Tools\voicevox\windows-cpu\run.exe' -ArgumentList '--host','127.0.0.1','--port','50021' -WindowStyle Hidden
```

確認: `Invoke-RestMethod http://127.0.0.1:50021/version`

## 2. 読み上げを始める

リポジトリ（`D:\WorkTree\TomsLands\wt-04`）で:

```powershell
py -I -X utf8 tools/persona_play/live/live_voice.py --start-voicevox
```

止めるときは Ctrl+C（止めると `obs/` の字幕は空になる）。最後に「読んだ / 古くて捨てた / 省いた」の数と遅れが出る。

起動直後に 0.1 秒の無音を鳴らして音の出力を確かめる。`音の出力: OK` と出れば声も出る。
`!! 音を鳴らせない` と出たときは Windows の既定の出力デバイスを確認する（鳴らせなくても字幕と立ち絵は同じ速さで進む）。
初めて使う日は、配信の前に 10 秒ほど動かして、実際にスピーカーから声が出ることを耳で確かめる。

ターミナルも出したくないときは、隠したまま動かして進み具合をファイルに書く:

```powershell
Start-Process "$env:WINDIR\pyw.exe" -ArgumentList '-3','-I','-X','utf8','tools\persona_play\live\live_voice.py','--start-voicevox','--log','D:\WorkTree\TomsLands\_scratch_wt04\live_voice.log' -WindowStyle Hidden
# 止める
Get-CimInstance Win32_Process -Filter "Name='pyw.exe' OR Name='pythonw.exe'" | Where-Object CommandLine -match 'live_voice' | ForEach-Object { Stop-Process -Id $_.ProcessId }
```

主なオプション:

| オプション | 既定 | 中身 |
|---|---|---|
| `--max-age` | 20 | ログの時刻からこの秒数を過ぎた一言は読まない（0 で無効） |
| `--hold` | 4 | 読み終えて次が来ないとき、字幕を残す秒数（-1 で消さない） |
| `--speed` / `--intonation` / `--volume` | 1.2 / 1.1 / 1.0 | VOICEVOX の話速・抑揚・音量 |
| `--portrait-height` | 720 | 立ち絵 PNG の高さ（px） |
| `--dim` | 0.5 | 話していない方の立ち絵の明るさ |
| `--no-flip-metan` | — | 四国めたんを左右反転しない（既定は反転して、右に置くずんだもんの方を向く） |
| `--duration` | 0 | この秒数で止める（確認用） |
| `--no-audio` / `--save-wav <フォルダ>` | — | 鳴らさない / 読んだ声の wav をフォルダにも書く（確認用） |
| `--log <ファイル>` | — | 進み具合をファイルに書く（pyw で隠して動かすとき） |

声のスタイルはログの `e`（表情）で選ぶ。対応は `tools/jikkyo_video/jikkyo.py` の `STYLES` と同じ
（ずんだもん: ノーマル 3・あまあま 1・ツンツン 7・セクシー 5・ささやき 22・ヒソヒソ 38・ヘロヘロ 75・なみだめ 76 /
四国めたん: ノーマル 2・あまあま 0・ツンツン 6・セクシー 4・ささやき 36・ヒソヒソ 37）。

## 3. `obs/` に書かれるファイル（UTF-8）

| ファイル | 中身 |
|---|---|
| `caption.txt` | 「ずんだもん：一言」。字幕 1 本で済ませるならこれ |
| `line.txt` / `speaker.txt` | 一言だけ / 話している人の名前だけ |
| `zundamon_line.txt` / `metan_line.txt` | その人の一言だけ（もう一人が話し始めると空になる）。立ち絵の横に吹き出しを置く用 |
| `speaking.txt` | `zundamon` / `metan` / 空 |
| `day.txt` | 「13日目」 |
| `credit.txt` | `VOICEVOX:ずんだもん　VOICEVOX:四国めたん` |
| `zundamon.png` / `metan.png` | 立ち絵。話している方は表情つき・口あき、もう一人は暗くなる。黙っている間は二人とも普通の顔 |

字幕は読み終えてから `--hold` 秒たつと空になる（次の一言が来ればそのまま差し替わる）。
立ち絵の素は `tools/jikkyo_video/assets/tachie/{z,m}_<表情>_{open,closed}.png`（坂本アヒルさんの立ち絵素材 PSD から
`tools/jikkyo_video/tachie.py` で書き出したもの。git には入っていない）。無いときは VOICEVOX 公式の
`tools/jikkyo_video/assets/chars/{z,m}_portrait.png` を使う。AI で作った画像は使わない。

## 4. OBS の設定（ユーザーが手で行う）

OBS: `C:\Program Files\obs-studio`。以下はすべて OBS の画面での操作。

1. **配信の設定**: 設定 → 配信 → サービス「YouTube - RTMPS」→ ストリームキーを自分で貼る（キーは誰にも渡さない）。
   または「アカウント接続」で YouTube にログインする。
2. **出力**: 設定 → 出力 → 出力モード「詳細」→ 配信タブ
   - エンコーダ: **NVIDIA NVENC H.264**（RTX 4070）
   - レート制御 CBR、ビットレート 6000 Kbps 前後、キーフレーム間隔 2 秒、プリセット P5（遅め＝高画質）、プロファイル high
   - 音声ビットレート 160 Kbps
3. **映像**: 設定 → 映像 → 基本解像度 1920×1080、出力解像度 1920×1080、FPS 30。
4. **音声**: 設定 → 音声 → デスクトップ音声「既定」。マイクは「無効」（PC に人はいないので）。
   - Unity の Game ビュー上部の **Mute Audio を必ずオフ**にする（オンだとゲームの音が配信に乗らない）。
   - VOICEVOX の声とゲームの音は同じデスクトップ音声に乗る。ゲームが大きすぎるときは Unity 側の音量か、
     音声ミキサーで調整する（声は `--volume` でも上げられる）。
5. **シーン**（例: シーン名「マラソン配信」）。ソースは下から順に:
   1. **ウィンドウキャプチャ**「Unity」: ウィンドウ = Unity エディタ（`[Unity.exe]: TomsLands - ...`）、
      キャプチャ方式「Windows 10 (1903 以降)」、カーソルのキャプチャはオフ。
      追加したら、ソースを右クリック → フィルタ → 「クロップ/パッド」で Game ビューだけ残るよう上下左右を削る
      （または Alt を押しながら赤枠の辺をドラッグ）。右クリック → 変形 → 「画面に合わせる」で全体に広げる。
      Game ビューは 16:9（1920×1080 など）にしておくと黒帯が出ない。
   2. **画像**「めたん」: ファイル = `D:\WorkTree\TomsLands\wt-04\tools\persona_play\live\obs\metan.png`。画面左下に置く。
   3. **画像**「ずんだもん」: ファイル = `...\live\obs\zundamon.png`。画面右下に置く。
      （画像ソースはファイルが変わると 1 秒以内に読み直すので、話し手が変わると明るさと表情が切り替わる）
   4. **テキスト（GDI+）**「字幕」: 「ファイルから読み取り」にチェック → `...\live\obs\caption.txt`。
      フォントは太めのゴシック（例: Zen Kaku Gothic New Black / BIZ UDPゴシック）48〜60pt、白文字＋縁取り（黒 6px）、
      「折り返し」をオンにして幅 1400px 前後。画面下中央に置く。
      吹き出し風にしたいときは、代わりに `zundamon_line.txt` / `metan_line.txt` を立ち絵の上に 1 本ずつ置く。
   5. **テキスト（GDI+）**「日付」（任意）: `...\live\obs\day.txt`、左上。
   6. **テキスト（GDI+）**「クレジット」（**必須**）: 「ファイルから読み取り」で `...\live\obs\credit.txt`、
      または直接「VOICEVOX:ずんだもん　VOICEVOX:四国めたん」と入力。24pt 前後で画面の隅（右上など）に常に表示する。
6. **配信の説明欄**: 「VOICEVOX:ずんだもん」「VOICEVOX:四国めたん」「立ち絵：坂本アヒル様」と、
   ストアページ https://store.steampowered.com/app/4535810/ を書く。
7. **始める前の確認**: `live_voice.py` を動かした状態で、OBS の音声ミキサーの「デスクトップ音声」が声に合わせて振れること、
   字幕と立ち絵が切り替わることを確かめてから「配信開始」。まずは YouTube Studio で「限定公開」にして数分試すと安全。

## 注意

- Unity は触らない（マラソンが止まる）。`live_voice.py` はログを読むだけ。
- `winsound` で鳴らすので、Windows の「既定の出力デバイス」に出る。配信中に出力先を変えない。
- 一度に十数行まとめて書かれる日（日の始めの判断）は、20 秒に収まる分だけ読んで残りは捨てる。
  読み残しを減らしたいときは `--speed` を上げるか `--max-age` を延ばす（延ばすほど画面との時差が広がる）。
