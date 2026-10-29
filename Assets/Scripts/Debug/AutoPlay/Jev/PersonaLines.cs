#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;

/// <summary>
/// 判断ごとの一言（日本語）。人格 × 質問 × 確信度の帯 のテンプレートから選ぶ（テキスト LLM は使わない）。
/// 人物像は tools/jikkyo_video/CHARACTERS.md。{choice} は選んだものの日本語名、{pct} は段階の額（判断時点の G。例「12万G」「半分の4万G」。スパチャの勢いは「全力」「控えめ」「見守るだけ」）。
/// 割合（「100%」）が入るのは、いちばん上の段階のときに 1 ランに数回だけ（PersonaStrategy.SpokenPct）。
/// 使わない言い回し（memory: jikkyo-phrase-bans）は出す直前にも検査して、入っていたら差し替える。
/// 同じキーで直近に出した行は続けて出さない（PickFresh）。
/// テンプレートの行を選んだあと、ミーム辞書（PersonaMemeLexicon、tools/persona_play/memes/）から確率で
/// ミームの行に差し替える（ApplyMeme。ラン単位の回数・使ったミームは MemeRun に持つ）。
/// </summary>
public static class PersonaLines
{
    /// <summary>出さない言い回し（「〜が効いてくる」系と「全額○回目」の数えネタ）。</summary>
    private static readonly string[] Banned = { "効いて", "効いた", "回目" };

    public enum Band { Low, Mid, High }

    public static Band BandOf(float confidence) => confidence >= 0.5f ? Band.High : confidence >= 0.25f ? Band.Mid : Band.Low;

    // key = "persona|question|band"。band を省いた "persona|question" は全帯共通。各バケツ 5〜6 行。
    private static readonly Dictionary<string, string[]> Templates = new()
    {
        // ---------------- ずんだもん ----------------
        ["zundamon|budget|High"] = new[]
        {
            "{pct}使うのだ！ ぼくの勘がそう言ってるのだ",
            "{pct}なのだ。77 はラッキーナンバーだから実質勝ちなのだ",
            "{pct}いくのだ！ ずんだ餅の精が相場に強いのだ",
            "{pct}なのだ。めたん、止めても無駄なのだ",
            "今日は{pct}なのだ！ 迷う理由がないのだ",
            "{pct}出すのだ。お金は使うと増えるのだ",
        },
        ["zundamon|budget|Mid"] = new[]
        {
            "{pct}くらいでいくのだ。聞いてるのだ、めたんの忠告は",
            "うーん、{pct}なのだ。ずんだ餅の精が相場に強いのだ",
            "{pct}にするのだ。今日のぼくはちょっとだけ大人なのだ",
            "{pct}なのだ。残りは明日のぼくに任せるのだ",
            "{pct}でいくのだ。半分は勘、もう半分も勘なのだ",
        },
        ["zundamon|budget|Low"] = new[]
        {
            "{pct}……たぶんなのだ。計画通りなのだ",
            "よくわからないけど{pct}なのだ。プラスになるのだ",
            "{pct}なのだ。……めたん、これ少ないのだ？",
            "今日は{pct}なのだ。勘がお昼寝してるのだ",
            "{pct}にしておくのだ。学習したのだ",
        },

        ["zundamon|buy_style|High"] = new[]
        {
            "{choice}で買うのだ！ 名前が強そうなのだ",
            "{choice}なのだ。説明書より勘なのだ",
            "{choice}しかないのだ！ ぼくの勘なのだ",
            "{choice}でいくのだ。めたんは慎重すぎて儲からないのだ",
            "{choice}なのだ！ 響きがもう黒字なのだ",
        },
        ["zundamon|buy_style|Mid"] = new[]
        {
            "{choice}でいくのだ。コメントで 3 人が欲しいって言ったのだ",
            "{choice}にするのだ。学習したのだ",
            "{choice}なのだ。昨日のぼくもそう言ってたのだ",
            "{choice}でいいのだ。聞いてるのだ、ちゃんと",
            "今日は{choice}の気分なのだ",
        },
        ["zundamon|buy_style|Low"] = new[]
        {
            "{choice}……でいいのだ？ いいのだ",
            "迷ったら{choice}なのだ。ぼくの勘なのだ",
            "{choice}なのだ。たぶんなのだ",
            "えーと、{choice}なのだ。なんとなくなのだ",
            "{choice}……計画通りなのだ。計画はしてないのだ",
        },

        ["zundamon|newspaper_yes"] = new[]
        {
            "新聞を読むのだ。見出しで相場が分かるのだ",
            "読むのだ！ ずんだ餅の記事が載ってるかもしれないのだ",
            "新聞なのだ。四コマから読むのだ",
            "今日は読むのだ。ぼくだって勉強するのだ",
            "読むのだ。占いのコーナーが大事なのだ",
        },
        ["zundamon|newspaper_no"] = new[]
        {
            "新聞は読まないのだ。見出しだけで十分なのだ",
            "読まないのだ。相場は顔で読むのだ",
            "新聞より勘なのだ",
            "読まないのだ。字が多いのだ",
            "今日はパスなのだ。ぼくの勘が新聞なのだ",
        },
        ["zundamon|intel_yes"] = new[]
        {
            "情報を買うのだ。今日は慎重なぼくなのだ",
            "弱点を聞いておくのだ。勝つための投資なのだ",
            "情報なのだ。めたんの真似なのだ",
            "買うのだ。答え合わせが早い方が楽しいのだ",
            "弱点を買うのだ！ ……勘の確認なのだ",
        },
        ["zundamon|intel_no"] = new[]
        {
            "情報は買わないのだ。情報を買わずに当てるのだ",
            "弱点はぼくの勘で分かるのだ",
            "買わないのだ。そのお金でもう一本仕入れるのだ",
            "情報なしでいくのだ！ そのほうがかっこいいのだ",
            "いらないのだ。ぼくの勘は無料なのだ",
        },
        // {choice} は「出資しない」「1 レベル」「出せるだけ」
        ["zundamon|support|High"] = new[]
        {
            "魔王軍への出資は「{choice}」なのだ！ 勇者の敗北はぼくの黒字なのだ",
            "「{choice}」なのだ。圧倒的じゃないか、我が軍は",
            "魔王軍に「{choice}」なのだ！ 勇者には内緒なのだ",
            "出資は「{choice}」なのだ。ぼくの勘が魔王を推してるのだ",
            "「{choice}」でいくのだ！ ボスは名前が強そうなのだ",
        },
        ["zundamon|support|Mid"] = new[]
        {
            "魔王軍は「{choice}」なのだ。計画通りなのだ",
            "「{choice}」……勇者には内緒なのだ",
            "出資は「{choice}」にするのだ。ちょっとだけ悪い子なのだ",
            "魔王軍は「{choice}」なのだ。保険なのだ",
            "「{choice}」なのだ。聞いてるのだ、めたん",
        },
        ["zundamon|support|Low"] = new[]
        {
            "魔王軍は「{choice}」なのだ。たぶんなのだ",
            "「{choice}」でいいのだ。聞いてるのだ",
            "出資は「{choice}」なのだ。よくわからないのだ",
            "魔王軍……「{choice}」なのだ。なんとなくなのだ",
            "「{choice}」なのだ。今日は様子見なのだ",
        },
        ["zundamon|focus_share"] = new[]
        {
            "ダンジョン向けに{pct}なのだ。全部当てるのだ",
            "{pct}つぎ込むのだ！ 弱点はもう見えてるのだ",
            "ダンジョン用は{pct}なのだ。勇者が欲しがる顔をしてたのだ",
            "{pct}なのだ。ぼくの勘は一点張りが好きなのだ",
            "次のダンジョンに{pct}なのだ！ 売り切れる未来が見えるのだ",
        },
        ["zundamon|weapon"] = new[]
        {
            "{choice}を買うのだ！ 名前が強そうなのだ",
            "{choice}なのだ。若木の弓より強そうなのだ",
            "{choice}なのだ！ 勇者がこれを持ったら最強なのだ",
            "{choice}にするのだ。見た目で選んだのだ",
            "{choice}なのだ。コメントで 3 人が推してたのだ",
        },

        ["zundamon|stream_side|High"] = new[]
        {
            "今日は{choice}に張るのだ！ 全力なのだ",
            "{choice}側なのだ。ぼくの勘が勝つのだ",
            "{choice}なのだ！ もう勝った顔をしておくのだ",
            "{choice}に張るのだ。めたん、見てるのだ",
            "{choice}一択なのだ！ 圧倒的じゃないか",
        },
        ["zundamon|stream_side|Mid"] = new[]
        {
            "{choice}に張るのだ。たぶん勝つのだ",
            "{choice}でいくのだ。やりますねぇ、ぼく",
            "{choice}なのだ。昨日の反省を生かしたのだ",
            "{choice}側にするのだ。なんとなく勝ちそうなのだ",
            "{choice}なのだ。聞いてるのだ、ちゃんと",
        },
        ["zundamon|stream_side|Low"] = new[]
        {
            "{choice}……でいいのだ。計画通りなのだ",
            "迷ったから{choice}なのだ",
            "{choice}なのだ。コインの裏が出たのだ",
            "えーと、{choice}なのだ。たぶんなのだ",
            "{choice}……勘が小声なのだ",
        },
        ["zundamon|intervene|High"] = new[]
        {
            "{choice}で決めるのだ！",
            "ここで{choice}なのだ。見てるのだ、めたん",
            "{choice}を連打するのだ！",
            "{choice}なのだ！ これで勝ちなのだ",
            "{choice}しかないのだ！ ぼくの勘なのだ",
        },
        ["zundamon|intervene|Mid"] = new[]
        {
            "{choice}を用意しておくのだ",
            "{choice}なのだ。コメントも欲しがってるのだ",
            "{choice}を構えておくのだ",
            "とりあえず{choice}なのだ。保険なのだ",
            "{choice}なのだ。タイミングは勘なのだ",
        },
        ["zundamon|intervene|Low"] = new[]
        {
            "{choice}……たぶんなのだ",
            "とりあえず{choice}なのだ",
            "{choice}なのだ？ ……なのだ",
            "{choice}でいいのだ。ボタンが光ってたのだ",
            "うーん、{choice}なのだ",
        },
        ["zundamon|intensity"] = new[]
        {
            "スパチャは{pct}でいくのだ",
            "スパチャは{pct}なのだ！ 応援は気持ちなのだ",
            "今日のスパチャは{pct}なのだ。お金は配信で増やすのだ",
            "{pct}でいくのだ！ 勇者もびっくりなのだ",
            "スパチャ、{pct}なのだ！",
        },
        ["zundamon|intervene_now"] = new[]
        {
            "いまなのだ！ {choice}なのだ！",
            "{choice}を送るのだ！ 圧倒的じゃないか",
            "{choice}なのだ！ 見たのだ、めたん",
            "ここなのだ！ {choice}！",
            "{choice}！ ぼくの勘が鳴ってるのだ",
        },

        ["zundamon|stream_result|win"] = new[]
        {
            "見たのだ？ ぼくの勘が勝ったのだ",
            "その気になっていたお前の姿はお笑いだったぜ、なのだ",
            "めたん、見たのだ？ 録音したのだ",
            "やりますねぇ、ぼく！ なのだ",
            "計画通りなのだ。計画はしてないのだ",
            "勝ったのだ！ 説明書より勘なのだ",
        },
        ["zundamon|stream_result|lose"] = new[]
        {
            "うわーん、なのだ。でも次は当たるのだ",
            "ボルガ博士、お許しくださいなのだ……次は当たるのだ",
            "ジュラル星人め！ なのだ。……次は当たるのだ",
            "惜しかったのだ。惜しくはないのだ",
            "うわーん……でも赤字の幅が減ったのは成長なのだ",
            "負けたのだ。握手なのだ……",
        },
        ["zundamon|run_result|win"] = new[]
        {
            "プラスなのだ！ 額は見ないのだ",
            "計画通りなのだ。計画はしてないのだ",
            "黒字なのだ！ めたん、見たのだ？",
            "やりますねぇ、ぼく。プラスなのだ",
            "増えたのだ！ ずんだ餅で乾杯なのだ",
            "勝ったのだ。めたんは慎重すぎて儲からないのだ",
        },
        ["zundamon|run_result|lose"] = new[]
        {
            "これはひどい、なのだ。でも学習したのだ",
            "トコちゃん、ごめんなのだ。次は当たるのだ",
            "うわーん、なのだ。……でも次は当たるのだ",
            "ボルガ博士、お許しくださいなのだ",
            "赤字の幅が減ったのは成長なのだ。たぶんなのだ",
            "握手なのだ……帳簿と握手なのだ",
        },
        // ---- 村（{choice} は施設名、{pct} は村資金のうち使う割合） ----
        ["zundamon|village_budget"] = new[]
        {
            "村のお金は{pct}使うのだ！ 村はでっかい方がかっこいいのだ",
            "{pct}なのだ。お金は村に置いておくと寂しがるのだ",
            "村資金から{pct}なのだ。ぼくの勘が建てろって言ってるのだ",
            "{pct}いくのだ！ 村長はぼくなのだ",
            "今回は{pct}なのだ。計画通りなのだ",
        },
        ["zundamon|invest_yes"] = new[]
        {
            "{choice}を建てるのだ！ 名前が強そうなのだ",
            "{choice}なのだ！ 説明は読んでないけど絶対いいのだ",
            "{choice}に投資なのだ。キラキラしてるのだ",
            "{choice}を大きくするのだ！ 村の顔なのだ",
            "{choice}なのだ。コメントで 3 人が建てろって言ったのだ",
        },
        ["zundamon|invest_no"] = new[]
        {
            "{choice}は今度なのだ。地味なのだ",
            "{choice}は見るだけなのだ",
            "{choice}……字が多いからパスなのだ",
            "{choice}はまた今度なのだ。ぼくの勘がお昼寝してるのだ",
            "{choice}はいいのだ。もっと派手なのがあるのだ",
        },
        ["zundamon|village_result|win"] = new[]
        {
            "村が育ったのだ！ ぼくの村なのだ",
            "建てたのだ！ 次のランは勝ったも同然なのだ",
            "村がちょっと都会になったのだ",
            "投資完了なのだ。お金は使うと増えるのだ",
            "村長ずんだもん、いい仕事をしたのだ",
        },
        ["zundamon|village_result|lose"] = new[]
        {
            "今回は建てなかったのだ。お金が足りないのだ",
            "村は見るだけだったのだ。次は建てるのだ",
            "建てられなかったのだ……次のランで稼ぐのだ",
            "村は据え置きなのだ。計画通りなのだ",
            "今日は村の散歩だけなのだ",
        },
        // ---- 店への投資・勇者の装備（rules 4.2。{choice} は品・設備・商品の名前か額、{pct} は額） ----
        ["zundamon|hero_weapon"] = new[]
        {
            "勇者には{choice}を持たせるのだ！ 名前が強そうなのだ",
            "{choice}なのだ。勇者が勝てばこれが値上がりするのだ",
            "{choice}を渡すのだ。ぼくの勘が光ってるって言ってるのだ",
            "勇者、{choice}で行ってくるのだ！",
            "{choice}にするのだ。在庫もいっぱいあるのだ",
        },
        ["zundamon|hero_weapon_keep"] = new[]
        {
            "武器は{choice}のままでいいのだ。相棒なのだ",
            "{choice}で続投なのだ。勇者も慣れてるのだ",
            "武器はそのままなのだ。{choice}は裏切らないのだ",
        },
        ["zundamon|hero_armor"] = new[]
        {
            "防具は{choice}なのだ！ 固そうなのだ",
            "{choice}を着せるのだ。勇者が転んでも大丈夫なのだ",
            "{choice}なのだ。見た目が強いのだ",
            "勇者には{choice}なのだ。ぼくのおすすめなのだ",
        },
        ["zundamon|hero_armor_keep"] = new[]
        {
            "防具は{choice}のままなのだ",
            "{choice}で十分なのだ。勇者は頑丈なのだ",
            "防具はそのままでいいのだ",
        },
        ["zundamon|hero_tactics"] = new[]
        {
            "作戦は{choice}なのだ！",
            "{choice}でいくのだ。勇者もそう言ってる気がするのだ",
            "今日の勇者は{choice}なのだ",
            "{choice}なのだ。響きが強いのだ",
        },
        // ---- 出店準備（スターターレリック。{choice} はレリックの名前） ----
        ["zundamon|starter_relic"] = new[]
        {
            "持っていくのは{choice}なのだ！ 名前がもう強いのだ",
            "{choice}にするのだ。ぼくの勘が光ってるのだ",
            "今回のランは{choice}で勝つのだ！",
            "相棒は{choice}なのだ。見た目で決めたのだ",
            "{choice}なのだ。説明は半分読んだから大丈夫なのだ",
        },
        ["zundamon|shop_upgrade_yes"] = new[]
        {
            "店を改装するのだ！ {choice}でお店がでっかくなるのだ",
            "改装なのだ。棚が増えたら売上も増えるのだ",
            "{choice}払って改装なのだ。お店はぼくの城なのだ",
            "改装するのだ！ ずんだ色の看板にするのだ",
            "店を広げるのだ。お金は使うと増えるのだ",
        },
        ["zundamon|shop_upgrade_no"] = new[]
        {
            "改装はまた今度なのだ",
            "店はこのままでいいのだ。狭い方が落ち着くのだ",
            "改装は見るだけなのだ",
            "今日はやめておくのだ。{choice}は大金なのだ",
        },
        ["zundamon|smith_upgrade_yes"] = new[]
        {
            "鍛冶屋を開発するのだ！ 新しい武器が見たいのだ",
            "{choice}で鍛冶屋をレベルアップなのだ。強い武器は高く売れるのだ",
            "開発なのだ。ピカピカの剣が並ぶのだ",
            "鍛冶屋さん、頼んだのだ！",
        },
        ["zundamon|smith_upgrade_no"] = new[]
        {
            "鍛冶屋はこのままなのだ",
            "開発はまた今度なのだ。今の武器も好きなのだ",
            "今日はパスなのだ",
        },
        ["zundamon|broker_upgrade_yes"] = new[]
        {
            "情報屋を開発するのだ！ 取引所の品ぞろえが増えるのだ",
            "{choice}で情報屋をレベルアップなのだ。投資の時代なのだ",
            "情報屋さんを育てるのだ",
        },
        ["zundamon|broker_upgrade_no"] = new[]
        {
            "情報屋はこのままでいいのだ",
            "開発はまた今度なのだ",
        },
        ["zundamon|machine_yes"] = new[]
        {
            "{choice}を置くのだ！ キラキラしてるのだ",
            "{choice}なのだ！ {pct}でも安いのだ",
            "お店に{choice}を置くのだ。お客さんが喜ぶのだ",
            "{choice}を買うのだ！ 一目ぼれなのだ",
            "{choice}なのだ。毎日働いてくれるのだ",
        },
        ["zundamon|machine_no"] = new[]
        {
            "設備は今日は買わないのだ",
            "機械はまた今度なのだ",
            "見るだけなのだ。どれもかっこいいのだ",
        },
        ["zundamon|finance_fund"] = new[]
        {
            "{choice}を買うのだ！ 夢があるのだ",
            "{choice}に張るのだ。相場はぼくの味方なのだ",
            "{choice}なのだ。上がる気しかしないのだ",
            "投資信託デビューなのだ！ {choice}なのだ",
            "{choice}なのだ。ずんだ餅の精が相場に強いのだ",
        },
        ["zundamon|finance_bond"] = new[]
        {
            "{choice}を買うのだ。たまには堅実なのだ",
            "{choice}なのだ。利息がもらえるのだ",
            "今日は大人の{choice}なのだ",
        },
        ["zundamon|finance_sell"] = new[]
        {
            "{choice}を売るのだ！ 利益確定なのだ",
            "{choice}はここで売るのだ。計画通りなのだ",
            "{choice}を手放すのだ。さよならなのだ",
        },
        ["zundamon|finance_no"] = new[]
        {
            "取引所は今日はいいのだ",
            "投資はまた今度なのだ",
            "今日はお店で稼ぐのだ",
        },
        ["zundamon|finance_share"] = new[]
        {
            "{choice}に{pct}入れるのだ！",
            "{pct}なのだ。どーんといくのだ",
            "{choice}に{pct}なのだ。増えて帰ってくるのだ",
            "{pct}入れるのだ。ぼくの勘なのだ",
        },
        ["zundamon|fallback"] = new[]
        {
            "……頭が真っ白なのだ。いつものでいくのだ",
            "考えるのはあとなのだ。いつもどおりなのだ",
            "勘が電波を拾えないのだ。いつものなのだ",
            "えーと……いつものでいくのだ",
            "ぼくの勘が休憩中なのだ。いつもどおりなのだ",
        },

        // ---------------- 四国めたん ----------------
        ["metan|budget|High"] = new[]
        {
            "{pct}。1 万は残すわ",
            "{pct}で十分。それ以上は使わないの",
            "{pct}。数字が揃ってるもの",
            "今日は{pct}。根拠は 2 行で言えるわ",
            "{pct}にするわ。読んだうえでね",
            "{pct}。数えてるからね",
        },
        ["metan|budget|Mid"] = new[]
        {
            "{pct}にしておくわ。缶ジュース 2 本ぶんの余裕は要るのよ",
            "{pct}。数えてるからね",
            "{pct}かしら。残りは明日の仕入れよ",
            "{pct}。全部は使わない、それだけ。",
            "{pct}にするわ。余白は保険よ",
        },
        ["metan|budget|Low"] = new[]
        {
            "{pct}……かしら。様子見よ",
            "決め手がないから{pct}。それだけ。",
            "{pct}。今日は守る日よ",
            "{pct}にしておくわ。お財布が寒いのよ",
            "{pct}。私の 3 か月分のお小遣いを賭ける日じゃないわ",
        },

        ["metan|buy_style|High"] = new[]
        {
            "{choice}。理由は値段と需要よ",
            "{choice}で買うわ。正解。",
            "{choice}。グラフを見れば分かるわ",
            "{choice}にするわ。迷う理由がないもの",
            "{choice}。説明書の 3 ページ目に書いてあるわ",
        },
        ["metan|buy_style|Mid"] = new[]
        {
            "{choice}にしておくわ。悪くないじゃない",
            "{choice}。説明書どおりよ",
            "{choice}かしら。数字は悪くないわ",
            "{choice}。手堅くね",
            "{choice}にするわ。理由は後で言うわ",
        },
        ["metan|buy_style|Low"] = new[]
        {
            "{choice}……読んだわよ、表紙は",
            "決め手は弱いけど{choice}にするわ",
            "{choice}。……たぶんね",
            "{choice}かしら。他よりはましよ",
            "{choice}にしておくわ。消去法よ",
        },

        ["metan|newspaper_yes"] = new[]
        {
            "新聞は先に読むわ。基本よ",
            "原因を読まずに買う人はいないわ",
            "新聞を読むわ。相場は見出しの次の行に出るの",
            "読むわ。知らないで買うのは賭けよ",
            "新聞が先。ずんだもんは見習いなさい",
        },
        ["metan|newspaper_no"] = new[]
        {
            "今日は読まないわ。……表紙は読んだわよ",
            "新聞は後。今日は数字だけで足りるわ",
            "読まないわ。昨日ので足りるもの",
            "今日はいいわ。それだけ。",
            "読まない。……見出しは見たわよ",
        },
        ["metan|intel_yes"] = new[]
        {
            "情報は買うわ。弱点を知らずに仕入れるのは賭けよ",
            "情報料は保険料みたいなものよ",
            "情報を買うわ。外したときの方が高いもの",
            "弱点は先に知っておくの。基本よ",
            "買うわ。勘で当てるのはずんだもんの仕事よ",
        },
        ["metan|intel_no"] = new[]
        {
            "情報は買わない。今は 1 万を割るもの",
            "今回は見送るわ。それだけ。",
            "買わないわ。新聞で足りてるもの",
            "情報料が高すぎるわ。しない。",
            "今日は見送り。……分かってるわよ、弱点くらい",
        },
        // {choice} は「出資しない」「1 レベル」「出せるだけ」
        ["metan|support|High"] = new[]
        {
            "魔王軍への出資は「{choice}」。防衛報酬が出資を上回るなら、の話よ",
            "「{choice}」。勇者には申し訳ないけど",
            "魔王軍に「{choice}」。数字の上ではこっちが得よ",
            "「{choice}」にするわ。勇者が勝つと店が負けるもの",
            "出資は「{choice}」。元は取れるわ",
        },
        ["metan|support|Mid"] = new[]
        {
            "「{choice}」にしておくわ。元が取れる範囲でね",
            "魔王軍は「{choice}」。それだけ。",
            "出資は「{choice}」。1 万は残すわ",
            "「{choice}」かしら。張りすぎは損よ",
            "魔王軍には「{choice}」。保険の範囲でね",
        },
        ["metan|support|Low"] = new[]
        {
            "「{choice}」……数字が弱いもの",
            "魔王軍は「{choice}」。しない理由の方が多いのよ",
            "出資は「{choice}」。今日は見送りよ",
            "「{choice}」。賭けるほどの根拠がないわ",
            "魔王軍は「{choice}」。お財布と相談した結果よ",
        },
        ["metan|focus_share"] = new[]
        {
            "ダンジョン向けは{pct}。残りは保険よ",
            "{pct}だけ寄せるわ。全部は寄せない",
            "次のダンジョンに{pct}。残りは売れ筋で手堅くね",
            "{pct}。一点張りはずんだもんの役よ",
            "ダンジョン用は{pct}にしておくわ",
        },
        ["metan|weapon"] = new[]
        {
            "{choice}。弱点に合っているから",
            "{choice}にするわ。属性が合うのよ",
            "{choice}。名前じゃなくて属性で選ぶの",
            "{choice}にしておくわ。値段に見合うもの",
            "{choice}。説明書どおりよ",
        },

        ["metan|stream_side|High"] = new[]
        {
            "今日は{choice}。数字がそう言ってるの",
            "{choice}に寄せるわ。正解。",
            "{choice}。迷う理由がないわ",
            "{choice}にするわ。読んだうえでね",
            "{choice}。根拠は 2 行で言えるわ",
        },
        ["metan|stream_side|Mid"] = new[]
        {
            "{choice}にしておくわ。悪くないじゃない",
            "{choice}。理由は 2 行で言えるわ",
            "{choice}かしら。手堅い方よ",
            "{choice}。勘じゃないわ、数字よ",
            "{choice}にするわ。外したら原因を見直すだけよ",
        },
        ["metan|stream_side|Low"] = new[]
        {
            "{choice}……かしら。決め手はないわ",
            "迷うなら{choice}。手堅くね",
            "{choice}。……たぶんね",
            "{choice}にしておくわ。消去法よ",
            "{choice}。今日は自信がないの、それだけ。",
        },
        ["metan|intervene|High"] = new[]
        {
            "{choice}を一回だけ。値段に見合うもの",
            "使うなら{choice}よ",
            "{choice}。ここは払う価値があるわ",
            "{choice}にするわ。一番安くて確実よ",
            "{choice}。無駄撃ちはしないわ",
        },
        ["metan|intervene|Mid"] = new[]
        {
            "{choice}は残高しだいね",
            "{choice}。1 万は残すわ",
            "{choice}を候補にしておくわ",
            "{choice}かしら。様子を見て一回ね",
            "{choice}。使うかは数字しだいよ",
        },
        ["metan|intervene|Low"] = new[]
        {
            "{choice}……でも使わないかもしれないわ",
            "{choice}。様子を見てからね",
            "{choice}。払うほどの場面じゃないかもね",
            "{choice}かしら。保留よ",
            "{choice}。……たぶん使わないわ",
        },
        ["metan|intensity"] = new[]
        {
            "スパチャは{pct}。それ以上は払わないわ",
            "スパチャは{pct}にしておくわ。缶ジュース何本ぶんか数えてるからね",
            "{pct}でいいわ。応援は気持ち、払いは数字よ",
            "スパチャは{pct}。それだけ。",
            "{pct}よ。私の 3 か月分のお小遣いは賭けないわ",
        },
        ["metan|intervene_now"] = new[]
        {
            "ここで{choice}。今が一番安いわ",
            "{choice}。今しかないのよ",
            "{choice}。……一回だけよ",
            "今よ、{choice}",
            "{choice}。ここで払うのが正解。",
        },

        ["metan|stream_result|win"] = new[]
        {
            "……悪くないじゃない",
            "前回がひどすぎただけ。でもそれは認めるわ",
            "正解。……当然よ",
            "読んだとおりよ。数えてるからね",
            "勝ったわね。浮かれないで、次も読むのよ",
            "……まあ、悪くないわ",
        },
        ["metan|stream_result|lose"] = new[]
        {
            "次は絶対当てるわ",
            "相場はジュラル星人じゃないのよ。原因を見直すわ",
            "逆。……原因を読み直すわ",
            "外したわ。それだけ。次は外さない",
            "……今のは数字が悪いのよ。私じゃないわ",
            "外れね。お小遣いが泣いてるわ",
        },
        ["metan|run_result|win"] = new[]
        {
            "黒字。数えてるからね",
            "単位は G。ちゃんと増えたわ",
            "黒字よ。……悪くないじゃない",
            "増えたわ。1 万は残したうえでね",
            "正解。帳簿が証明してるわ",
            "黒字。浮かれるのは缶ジュース 1 本ぶんだけよ",
        },
        ["metan|run_result|lose"] = new[]
        {
            "……あなたの帳簿よ。いいえ、私の帳簿ね",
            "読んだわよ、表紙は……次は中身まで読むわ",
            "赤字。原因は 2 行で言えるわ、言わないけど",
            "次は絶対当てるわ。帳簿に誓って",
            "……今回はお小遣いが減っただけよ",
            "単位は G。減ったのも G よ。次で戻すわ",
        },
        // ---- 村 ----
        ["metan|village_budget"] = new[]
        {
            "村資金から{pct}使うわ。残りは手を付けないの",
            "{pct}にしておくわ。回収の見込みを数えたうえでね",
            "使うのは{pct}。残りは次のランの持ち込みよ",
            "{pct}。帳簿に書ける範囲でね",
            "村には{pct}。それだけ。",
        },
        ["metan|invest_yes"] = new[]
        {
            "{choice}に投資するわ。何ランで回収できるか数えたもの",
            "{choice}。次のランから毎回得をするのよ",
            "{choice}を上げるわ。説明は全部読んだわ",
            "{choice}。値段に見合うもの",
            "{choice}にするわ。理由は 2 行で言えるわ",
        },
        ["metan|invest_no"] = new[]
        {
            "{choice}は見送るわ。回収に時間がかかりすぎるの",
            "{choice}は今じゃないわ",
            "{choice}。派手なだけじゃ払わないわ",
            "{choice}は保留よ。数字が足りないの",
            "{choice}は次の機会ね。1 万は残すわ",
        },
        ["metan|village_result|win"] = new[]
        {
            "村が育ったわ。……悪くないじゃない",
            "投資完了。次のランで回収するわ",
            "施設が 1 つ上がったわ。帳簿どおりよ",
            "村も帳簿も、ちゃんと増えたわ",
            "正解。村は裏切らないもの",
        },
        ["metan|village_result|lose"] = new[]
        {
            "今回は投資しないわ。お金を貯めるのも仕事よ",
            "見送りよ。次のランで村資金を増やすわ",
            "何も建てなかったわ。それだけ。",
            "村は据え置き。数字が揃うまで待つの",
            "……今は我慢の時期よ",
        },
        // ---- 店への投資・勇者の装備（rules 4.2） ----
        ["metan|hero_weapon"] = new[]
        {
            "勇者には{choice}。在庫の多い品を持たせるのが筋よ",
            "{choice}にするわ。勝てばこれが値上がりするもの",
            "{choice}を持たせるわ。強さと在庫の両方で選んだの",
            "{choice}よ。弱点を見てから決めたわ",
        },
        ["metan|hero_weapon_keep"] = new[]
        {
            "武器は{choice}のまま。替える理由がないわ",
            "{choice}で続投よ。それだけ。",
            "武器はそのまま。在庫も足りてるもの",
        },
        ["metan|hero_armor"] = new[]
        {
            "防具は{choice}。HPが残れば勝ち目も残るわ",
            "{choice}にするわ。守りは数字で選ぶの",
            "{choice}よ。在庫もあるもの",
        },
        ["metan|hero_armor_keep"] = new[]
        {
            "防具は{choice}のままでいいわ",
            "{choice}で十分よ",
            "防具はそのまま。それだけ。",
        },
        ["metan|hero_tactics"] = new[]
        {
            "作戦は{choice}。値動きは荒らさないの",
            "{choice}にするわ。理由は 2 行で言えるわ",
            "{choice}よ。勝ち負けは変わらないもの",
        },
        // ---- 出店準備（スターターレリック） ----
        ["metan|starter_relic"] = new[]
        {
            "持っていくのは{choice}。最初の日から最後まで働くもの",
            "{choice}にするわ。毎日の数字で選んだの",
            "{choice}よ。ランの間は替えられないから、よく読んだわ",
            "{choice}。説明は全部読んだわ",
            "{choice}にするわ。それだけ。",
        },
        ["metan|shop_upgrade_yes"] = new[]
        {
            "改装するわ。{choice}なら並べる品が増えた分で取り返せるもの",
            "店を改装するわ。数えたうえでよ",
            "{choice}で改装。棚が増えれば売上も増えるわ",
            "改装するわ。ランの終わりまで残るもの",
        },
        ["metan|shop_upgrade_no"] = new[]
        {
            "改装は見送るわ。{choice}は今は重いの",
            "今は改装しない。返済が先よ",
            "改装はまだ。それだけ。",
            "見送るわ。私なら1万は残すもの",
        },
        ["metan|smith_upgrade_yes"] = new[]
        {
            "鍛冶屋を開発するわ。高い品ほど1個の利益が大きいの",
            "{choice}で開発。強い装備は勇者にも持たせられるわ",
            "開発するわ。説明は全部読んだわ",
        },
        ["metan|smith_upgrade_no"] = new[]
        {
            "開発は見送るわ。{choice}は今の手持ちには重いの",
            "鍛冶屋はこのままでいいわ",
            "今は開発しない。それだけ。",
        },
        ["metan|broker_upgrade_yes"] = new[]
        {
            "情報屋を開発するわ。債券をもっと持てるようになるの",
            "{choice}で開発。取引所の選択肢が増えるわ",
        },
        ["metan|broker_upgrade_no"] = new[]
        {
            "情報屋は今のままで足りてるわ",
            "開発は見送り。それだけ。",
        },
        ["metan|machine_yes"] = new[]
        {
            "{choice}を置くわ。何日で元が取れるか数えたもの",
            "{choice}。{pct}なら回収の見込みが立つわ",
            "{choice}にするわ。毎日働く設備は裏切らないの",
            "{choice}よ。派手だからじゃないわ",
        },
        ["metan|machine_no"] = new[]
        {
            "設備は見送るわ。元が取れる前にランが終わるもの",
            "今は置かない。それだけ。",
            "設備は今じゃないわ",
        },
        ["metan|finance_bond"] = new[]
        {
            "{choice}を買うわ。満期まで減らないもの",
            "{choice}。利息は約束されてるの",
            "{choice}にするわ。寝かせるなら利息が付く方よ",
        },
        ["metan|finance_fund"] = new[]
        {
            "{choice}を少しだけ。相場を読んだうえでよ",
            "{choice}。減ることもあるのは分かってるわ",
            "{choice}にするわ。……新聞の見出しは読んだわよ",
        },
        ["metan|finance_sell"] = new[]
        {
            "{choice}は売るわ。利が乗ったうちにね",
            "{choice}を解約するわ。欲張らないの",
            "{choice}はここまで。それだけ。",
        },
        ["metan|finance_no"] = new[]
        {
            "取引所は今日は見送り。仕入れに回すわ",
            "投資はしない。手持ちが先よ",
            "今日はいいわ。それだけ。",
        },
        ["metan|finance_share"] = new[]
        {
            "{choice}には{pct}だけ。残りは手を付けないの",
            "{pct}にしておくわ。全部は入れないの",
            "{pct}。帳簿に書ける範囲でね",
        },
        ["metan|fallback"] = new[]
        {
            "考える時間がないわ。いつもどおりにするの",
            "情報が来ないから手堅くいくわ",
            "今は読めないわ。いつもの手堅い方よ",
            "決めきれないときは基本に戻るの",
            "数字が出ないなら、いつもどおりよ",
        },
    };

    // ---------------- 気分つきのテンプレート（P6。4.2: キーに気分を足す） ----------------
    // key = "persona|question|mood" か、全質問共通の "persona|*|mood"（{what} = 選んだもの・割合。無い質問では使わない）。
    // 気分が平常でなければ確率 MoodLineChance でこちらから選ぶ（人物の芯は CHARACTERS.md のまま、言い方だけ気分で変える）。
    public const double MoodLineChance = 0.6;

    private static readonly Dictionary<string, string[]> MoodTemplates = new()
    {
        // ずんだもん: 泣いた後は取り返しに行く（難しい手に挑戦）。引きずらない
        ["zundamon|*|sad"] = new[]
        {
            "さっきのは練習なのだ。{what}で取り返すのだ",
            "うう……でも{what}なのだ。次は当たるのだ",
            "泣いてないのだ。{what}でいくのだ",
            "取り返すのだ！ {what}なのだ",
            "トコちゃん見てて、{what}で取り返すのだ",
            "負けたままじゃ終われないのだ。{what}なのだ",
            "ぼくの77個のぶんまで、{what}なのだ",
        },
        // ずんだもん: 当てた後（ドヤ）。子供の自慢であって見下しではない
        ["zundamon|*|smug"] = new[]
        {
            "{what}なのだ。また当てちゃうのだ",
            "めたん、見てるのだ？ {what}なのだ",
            "ぼくの勘、今日は冴えてるのだ。{what}なのだ",
            "{what}なのだ。説明書より勘なのだ",
            "勝ってるときは攻めるのだ。{what}なのだ",
            "録音しておくのだ。{what}なのだ",
        },
        // ずんだもん: 自信が高いとき（勝った直後）。声が大きくなる
        ["zundamon|*|conf_high"] = new[]
        {
            "{what}なのだ！ 今のぼくは負ける気がしないのだ",
            "絶好調なのだ。{what}いくのだ！",
            "{what}なのだ！ 77 はラッキーナンバーなのだ",
            "{what}なのだ。ずんだ餅の精もそう言ってるのだ",
            "今日のぼくは天才なのだ。{what}なのだ",
            "流れが来てるのだ！ {what}なのだ",
        },
        // めたん: マウントされて悔しい（イラ）。慎重策を捨てかける
        ["metan|*|irk"] = new[]
        {
            "{what}。今度は私が当てるわ",
            "……{what}よ。数えてるからね",
            "{what}。慎重なだけじゃ勝てないもの",
            "見てなさい。{what}よ",
            "{what}。……さっきのは忘れて",
        },
        // めたん: 読みが当たった後
        ["metan|*|smug"] = new[]
        {
            "{what}。言った通りでしょ",
            "{what}よ。当然の判断",
            "{what}。数字は嘘をつかないの",
            "{what}。説明書にも書いてあったわ。……表紙の裏に",
        },
        ["metan|*|sad"] = new[]
        {
            "{what}。……今は堅くいくわ",
            "{what}よ。私なら1万は残すもの",
            "{what}。これ以上は私のお小遣いが持たないわ",
        },
    };

    private static readonly Random Rng = new();

    /// <summary>
    /// キーごとに直近に出した行。候補数の半分までは同じ行を出さない。
    /// static なので Play をまたいでも（ドメインリロードがなければ）残り、前のランの締めの一言も避けやすい。
    /// </summary>
    private static readonly Dictionary<string, List<string>> RecentByKey = new();

    /// <summary>
    /// 一言を選ぶ。persona|question|band → persona|question → persona|fallback の順に探す。
    /// </summary>
    public static string Pick(string persona, string question, Band band, string choiceJa, string pct = null, System.Random rng = null,
        MemeRun memes = null, string mood = null)
    {
        memes?.ClearLast();
        var r = rng ?? Rng;
        string what = !string.IsNullOrEmpty(choiceJa) ? choiceJa : pct;
        string key = null;
        string[] list = null;
        // 気分つき（平常でなければ確率で）: 質問ごとの気分のキー → 全質問共通の気分のキー（{what} がある質問だけ）
        if (!string.IsNullOrEmpty(mood) && mood != "neutral" && r.NextDouble() < MoodLineChance)
        {
            key = $"{persona}|{question}|{mood}";
            if (!MoodTemplates.TryGetValue(key, out list))
            {
                key = $"{persona}|*|{mood}";
                if (string.IsNullOrEmpty(what) || !MoodTemplates.TryGetValue(key, out list)) list = null;
            }
        }
        if (list == null)
        {
            key = $"{persona}|{question}|{band}";
            if (!Templates.TryGetValue(key, out list))
            {
                key = $"{persona}|{question}";
                if (!Templates.TryGetValue(key, out list))
                {
                    key = $"{persona}|fallback";
                    Templates.TryGetValue(key, out list);
                }
            }
        }
        if (list == null || list.Length == 0) return "";

        string line = PickFresh(key, list, r, memes, t => t.Replace("{choice}", choiceJa ?? "").Replace("{pct}", pct ?? "").Replace("{what}", what ?? ""));
        if (!IsClean(line)) line = persona == "metan" ? "それだけ。" : "いくのだ！";
        line = ApplyMeme(persona, question, band.ToString(), false, line, choiceJa, pct, false, memes);
        memes?.Use(line, persona);
        return line;
    }

    /// <summary>
    /// 結果用（question = stream_result / run_result、outcome = win / lose）。
    /// bigSwing = 損益が大きく動いた（ミームに差し替える確率を上げる）。
    /// </summary>
    public static string PickResult(string persona, string question, bool win, System.Random rng = null,
        MemeRun memes = null, bool bigSwing = false)
    {
        memes?.ClearLast();
        string outcome = win ? "win" : "lose";
        string key = $"{persona}|{question}|{outcome}";
        if (!Templates.TryGetValue(key, out var list) || list.Length == 0) return "";
        string line = PickFresh(key, list, rng ?? Rng, memes, t => t);
        if (!IsClean(line)) return "";
        line = ApplyMeme(persona, question, outcome, true, line, null, null, bigSwing, memes);
        memes?.Use(line, persona);
        return line;
    }

    // =====================================================
    // ミーム（tools/persona_play/memes/README.md の「ペルソナのコードでの使い方」）
    // =====================================================

    /// <summary>ずんだもんがミームに差し替える確率（ふだん / 山場 / 損益が大きく動いた結果）。</summary>
    public const double MemeChanceZundamon = 0.15, MemeChanceZundamonClimax = 0.3, MemeChanceZundamonBigSwing = 0.4;
    /// <summary>めたんがミームに反応する（または渋々使う）確率（ふだん / 山場）。めたんは自分からは出さないので低め。</summary>
    public const double MemeChanceMetan = 0.08, MemeChanceMetanClimax = 0.15;
    /// <summary>1 ランの上限: ずんだもんのミーム / めたんの反応 / めたんが渋々使う（metan_reluctant）。</summary>
    public const int MaxMemesZundamon = 5, MaxReactionsMetan = 3, MaxReluctantMetan = 1;
    /// <summary>テンプレートの頭（選んだもの・割合が入った 1 文）とミームをつなぐときの長さの上限。超えたらミームだけ。</summary>
    public const int MaxCombinedLength = 48;
    /// <summary>
    /// 山場でない場面のミームは 1 ランにこの数まで（残りは山場に取っておく）。1 ランの判断は 100 を超えるので、
    /// これが無いと最初の 1〜2 日で上限に達する（CHARACTERS.md: 使いどころは感情が振り切れた瞬間）。
    /// </summary>
    public const int MaxOrdinaryMemesZundamon = 2, MaxOrdinaryReactionsMetan = 1;
    /// <summary>結果（stream_result / run_result）以外の一言で出すのは 1 ランにこの数まで（残りは結果の一言に取っておく）。</summary>
    public const int MaxNonResultMemesZundamon = 3, MaxNonResultReactionsMetan = 2;
    /// <summary>
    /// 前のミームから空ける一言の数（山場でない場面 / 予算・張る側の High / 結果）。1 営業日は判断の一言がおよそ 7〜10 行。
    /// check_lexicon.py の試算（2 周 × 12 日）で、ずんだもん平均 4.6 回・ランの前半と後半にほぼ散らばる値。
    /// </summary>
    public const int MemeGapOrdinary = 48, MemeGapClimax = 24, MemeGapResult = 24;
    /// <summary>tone の寄せ: マウント（ずんだもんの勝ち）は smug、負けは sad / panic、自信過剰の賭けは hype。</summary>
    private const double MountSmugFactor = 3.0, LoseSadFactor = 2.0, OverconfidentHypeFactor = 1.5;

    /// <summary>
    /// ラン単位のミームの状態（PersonaStrategy が持ち、ランの始まりに ResetRun）。
    /// Rng は人格の乱数の種から作った別系統（ミームの抽選で判断・テンプレートの乱数列をずらさない）。
    /// </summary>
    public sealed class MemeRun
    {
        public MemeRun(PersonaMemeLexicon lexicon, System.Random rng)
        {
            Lexicon = lexicon;
            Rng = rng ?? new System.Random();
        }

        public PersonaMemeLexicon Lexicon { get; }
        public System.Random Rng { get; }
        public bool Enabled => Lexicon != null && Lexicon.Memes.Count > 0;

        /// <summary>このランで使ったミームの id（同じミームは 1 ランで 2 回使わない）。</summary>
        public readonly HashSet<string> Used = new();
        /// <summary>このランで出したミームの数（ずんだもん = ミーム、めたん = 反応）。</summary>
        public int Count;
        /// <summary>めたんが渋々使った数（metan_reluctant）。</summary>
        public int Reluctant;
        /// <summary>このランで山場でない場面に出した数 / 結果以外の一言で出した数（めたんの渋々は数えない）。</summary>
        public int Ordinary;
        public int NonResult;
        /// <summary>このランで ApplyMeme に来た一言の数と、最後にミームを出したときの番号（間を空ける用）。</summary>
        public int Lines;
        public int LastMemeLine = -1;

        /// <summary>直前の Pick / PickResult がミームにした行と、その id・きっかけ（めたんの反応のときのずんだもんの行）。</summary>
        public string LastLine { get; private set; }
        public string LastId { get; private set; }
        public string LastCue { get; private set; }

        // ---- 一言の間隔（P6・M12: 同じ行はマラソンの 1 ラン（2 周）に 3 回まで、同じ行の間は「同じ話し手の」一言で 20 行あける。相談の掛け合いも同じ数え方） ----
        // MemeRun は AutoPlay.Start（周）ごとに作り直されるので、数えはマラソンのラン ID（PersonaLog.RunTag）ごとに持ち越す（指標 M12 と同じ単位）。
        public const int MaxSameLinePerRun = 3, MinSameLineGap = 24;   // 指標は相談ログの行だけで 20 行。こちらは結果の一言（判断ログだけに出る）も数えるので少し多めにあける

        private sealed class Spacing
        {
            public readonly Dictionary<string, int> Count = new();
            public readonly Dictionary<string, int> Last = new();
            public readonly Dictionary<string, string> Speaker = new();
            public readonly Dictionary<string, int> Index = new();
        }

        private static readonly Dictionary<string, Spacing> SpacingByRun = new();
        private Spacing _spacing;

        private Spacing Sp
        {
            get
            {
                if (_spacing != null) return _spacing;
                string tag = PersonaLog.RunTag;
                if (string.IsNullOrEmpty(tag)) return _spacing = new Spacing();
                if (!SpacingByRun.TryGetValue(tag, out _spacing))
                {
                    if (SpacingByRun.Count > 16) SpacingByRun.Clear();
                    SpacingByRun[tag] = _spacing = new Spacing();
                }
                return _spacing;
            }
        }

        private int IndexOf(string speaker) => Sp.Index.TryGetValue(speaker ?? "*", out var i) ? i : 0;

        public int CountOf(string line) => !string.IsNullOrEmpty(line) && Sp.Count.TryGetValue(line, out var n) ? n : 0;

        /// <summary>この行を今言ってよいか（1 ラン 3 回未満・同じ話し手が前に言ってから 20 行以上）。</summary>
        public bool Fresh(string line)
        {
            if (string.IsNullOrEmpty(line) || !Sp.Count.TryGetValue(line, out var n)) return true;
            return n < MaxSameLinePerRun && IndexOf(Sp.Speaker[line]) - Sp.Last[line] >= MinSameLineGap;
        }

        public bool UnderCap(string line) => CountOf(line) < MaxSameLinePerRun;

        /// <summary>言った行を数える（speaker = zundamon / metan。話し手ごとに行を数える）。</summary>
        public void Use(string line, string speaker = null)
        {
            if (string.IsNullOrEmpty(line)) return;
            string s = speaker ?? "*";
            var sp = Sp;
            int idx = IndexOf(s) + 1;
            sp.Index[s] = idx;
            sp.Count[line] = CountOf(line) + 1;
            sp.Last[line] = idx;
            sp.Speaker[line] = s;
        }

        public void ResetRun()
        {
            // 一言の間隔の数え（_lineCount など）は周ごとには戻さない（MemeRun はセッション＝マラソンの 1 ラン（2 周）ごとに作り直す。指標 M12 と同じ単位）
            Used.Clear();
            Count = 0;
            Reluctant = 0;
            Ordinary = 0;
            NonResult = 0;
            Lines = 0;
            LastMemeLine = -1;
            ClearLast();
        }

        public void ClearLast()
        {
            LastLine = null;
            LastId = null;
            LastCue = null;
        }

        internal void SetLast(string line, string id, string cue)
        {
            LastLine = line;
            LastId = id;
            LastCue = cue;
        }

        /// <summary>line が直前にミームにした行ならその id（ログの meme 用）。違えば null。</summary>
        public string MemeFor(string line) => line != null && line == LastLine ? LastId : null;

        /// <summary>line が直前にミームにした行なら、めたんの反応のきっかけ（ログの memeCue 用）。</summary>
        public string CueFor(string line) => line != null && line == LastLine ? LastCue : null;
    }

    /// <summary>山場（ずんだもんの確率を 0.3 に上げる）: 結果、予算・張る側の High。</summary>
    private static bool IsClimax(string question, string bandOrOutcome, bool result) =>
        result || (bandOrOutcome == nameof(Band.High) && question is "budget" or "stream_side");

    /// <summary>
    /// テンプレートの行 templateLine をミームの行に差し替えるか決める。差し替えなければ templateLine をそのまま返す。
    /// bandOrOutcome は帯（High / Mid / Low）か、結果なら win / lose。質問キーは "question" と "question|帯"（結果は "question|win/lose"）。
    /// </summary>
    private static string ApplyMeme(string persona, string question, string bandOrOutcome, bool result, string templateLine,
        string choiceJa, string pct, bool bigSwing, MemeRun memes)
    {
        if (memes == null || !memes.Enabled || string.IsNullOrEmpty(templateLine)) return templateLine;
        bool zundamon = persona == "zundamon";
        bool metan = persona == "metan";
        if (!zundamon && !metan) return templateLine;

        memes.Lines++;
        bool climax = IsClimax(question, bandOrOutcome, result);
        bool canReact = zundamon ? memes.Count < MaxMemesZundamon : memes.Count < MaxReactionsMetan;
        bool canReluctant = metan && memes.Reluctant < MaxReluctantMetan;
        if (!climax && memes.Ordinary >= (zundamon ? MaxOrdinaryMemesZundamon : MaxOrdinaryReactionsMetan)) canReact = false;
        if (!result && memes.NonResult >= (zundamon ? MaxNonResultMemesZundamon : MaxNonResultReactionsMetan)) canReact = false;
        int gap = result ? MemeGapResult : climax ? MemeGapClimax : MemeGapOrdinary;
        if (memes.LastMemeLine >= 0 && memes.Lines - memes.LastMemeLine < gap) return templateLine;
        if (!canReact && !canReluctant) return templateLine;

        string keyBand = question + "|" + bandOrOutcome;
        bool mount = zundamon && result && bandOrOutcome == "win";
        bool lose = result && bandOrOutcome == "lose";
        bool overconfident = zundamon && !result && bandOrOutcome == nameof(Band.High) && question is "budget" or "intensity" or "stream_side";

        // 候補: 質問キーが合う・このランでまだ使っていない・この人格が使える（重み = trend_score × weight × tone の寄せ）
        var candidates = new List<PersonaMeme>();
        var weights = new List<double>();
        foreach (var m in memes.Lexicon.Memes)
        {
            if (memes.Used.Contains(m.Id)) continue;
            if (!m.HasKey(question) && !m.HasKey(keyBand)) continue;
            bool reluctant = m.Who == PersonaMeme.WhoMetanReluctant;
            if (zundamon && reluctant) continue;            // metan_reluctant はめたんが使う
            if (metan && (reluctant ? !canReluctant : !canReact)) continue;
            double w = m.SelectionWeight;
            if (mount && m.Tone == "smug") w *= MountSmugFactor;
            if (lose && m.Tone is "sad" or "panic") w *= LoseSadFactor;
            if (overconfident && m.Tone == "hype") w *= OverconfidentHypeFactor;
            if (w <= 0) continue;
            candidates.Add(m);
            weights.Add(w);
        }
        if (candidates.Count == 0) return templateLine;

        var r = memes.Rng;
        double p = zundamon
            ? (result && bigSwing ? MemeChanceZundamonBigSwing : climax ? MemeChanceZundamonClimax : MemeChanceZundamon)
            : (climax ? MemeChanceMetanClimax : MemeChanceMetan);
        if (r.NextDouble() >= p) return templateLine;

        double total = 0;
        foreach (var w in weights) total += w;
        double x = r.NextDouble() * total;
        int pick = candidates.Count - 1;
        for (int i = 0; i < weights.Count; i++)
        {
            x -= weights[i];
            if (x < 0) { pick = i; break; }
        }
        var meme = candidates[pick];

        string line, cue = null;
        bool isReluctant = false;
        if (zundamon)
        {
            line = meme.ZundamonLine;
            // 選んだもの・割合が入った頭の 1 文があれば、つないで判断が分かるようにする（長ければミームだけ）
            string lead = FirstSentence(templateLine);
            bool leadHasDetail = (!string.IsNullOrEmpty(choiceJa) && lead.Contains(choiceJa)) ||
                                 (!string.IsNullOrEmpty(pct) && lead.Contains(pct));
            if (lead.Length > 0 && "！。？!?".IndexOf(lead[lead.Length - 1]) < 0) lead += "。";
            if (leadHasDetail && lead.Length + 1 + line.Length <= MaxCombinedLength) line = lead + " " + line;
        }
        else if (meme.Who == PersonaMeme.WhoMetanReluctant)
        {
            // めたんが渋々使う（1 ランに 1 回）。使った直後に照れる
            isReluctant = true;
            line = meme.MetanReaction;
            if (line.IndexOf("知らない", StringComparison.Ordinal) < 0 && line.IndexOf("元ネタじゃない", StringComparison.Ordinal) < 0)
                line += "……知らないけど";
            cue = meme.ZundamonLine;
        }
        else
        {
            // ずんだもんのミームに返す（きっかけの行はログの memeCue に残す）
            line = meme.MetanReaction;
            cue = meme.ZundamonLine;
        }

        // 同じミームはこのランで 2 度選ばない（禁止語で捨てた場合も）
        memes.Used.Add(meme.Id);
        if (string.IsNullOrEmpty(line) || !IsClean(line)) return templateLine;   // 禁止語が入ったらミームをやめてテンプレートの行
        if (!IsClean(cue)) cue = null;

        if (isReluctant) memes.Reluctant++;
        else
        {
            memes.Count++;
            if (!climax) memes.Ordinary++;
            if (!result) memes.NonResult++;
        }
        memes.LastMemeLine = memes.Lines;
        memes.SetLast(line, meme.Id, cue);
        return line;
    }

    /// <summary>最初の 1 文（「！」「。」「？」まで）。区切りが無ければ全体。</summary>
    private static string FirstSentence(string line)
    {
        int idx = line.IndexOfAny(new[] { '！', '。', '？', '!', '?' });
        return idx >= 0 ? line.Substring(0, idx + 1) : line;
    }

    /// <summary>
    /// 直近に出した行（候補数の半分まで）を除き、さらにこのランの間隔の決まり（MemeRun.Fresh: 同じ行は 1 ラン 3 回まで・20 行あける）で選ぶ。
    /// 決まりを満たす行が無ければ 3 回未満の行、それも無ければいちばん使っていない行。fill = テンプレートの穴埋め（決まりは埋めた後の行で見る）。
    /// </summary>
    private static string PickFresh(string key, string[] list, System.Random r, MemeRun memes, Func<string, string> fill)
    {
        if (!RecentByKey.TryGetValue(key, out var recent)) RecentByKey[key] = recent = new List<string>();
        int keep = Math.Max(1, list.Length / 2);
        var pool = new List<string>(list.Length);
        foreach (var l in list)
            if (!recent.Contains(l)) pool.Add(l);
        if (pool.Count == 0) pool.AddRange(list);
        if (memes != null)
        {
            var strict = pool.Where(t => memes.Fresh(fill(t))).ToList();
            if (strict.Count == 0) strict = list.Where(t => memes.Fresh(fill(t))).ToList();
            if (strict.Count == 0) strict = list.Where(t => memes.UnderCap(fill(t))).ToList();
            if (strict.Count == 0)
            {
                int min = list.Min(t => memes.CountOf(fill(t)));
                strict = list.Where(t => memes.CountOf(fill(t)) == min).ToList();
            }
            pool = strict;
        }
        string tpl = pool[r.Next(pool.Count)];
        recent.Add(tpl);
        while (recent.Count > keep) recent.RemoveAt(0);
        return fill(tpl);
    }

    public static bool IsClean(string line)
    {
        if (string.IsNullOrEmpty(line)) return true;
        foreach (var b in Banned)
            if (line.IndexOf(b, StringComparison.Ordinal) >= 0) return false;
        return true;
    }

    /// <summary>
    /// 一言の表情（jikkyo.py の表情）。気分が強ければ気分（ずんだもん: しょんぼり → namida、ドヤ → tsun、自信 → ama）、
    /// そうでなければ自信の帯（High → ama、Mid → hero、Low → 本当に迷っている（確信 2% 未満で気分の自信も基準点より下）ときだけ hiso）。
    /// めたん: イラ → hero（「次は絶対当てる」）、ドヤ → tsun、ふだんは normal。CHARACTERS.md の表情の表。
    /// </summary>
    public static string Expression(string persona, Band band, string moodLabel, float confidence, bool belowBase = true)
    {
        if (persona == "metan")
        {
            return moodLabel switch
            {
                "irk" => "hero",
                "smug" => "tsun",
                "sad" => "tsun",   // めたんは泣かない。泣きそうなときは tsun で誤魔化す
                _ => band == Band.High ? "normal" : band == Band.Low && confidence < 0.02f ? "hiso" : "normal",
            };
        }
        switch (moodLabel)
        {
            case "sad": return "namida";
            case "smug": return "tsun";
            case "conf_high": return band == Band.Low ? "hero" : "ama";
        }
        return band switch
        {
            Band.High => "ama",
            Band.Mid => "hero",
            // 迷い（hiso）は確信がほぼ無く、しかも気分の自信が基準点より下のときだけ（ふだんのずんだもんは根拠なく自信がある＝hero）
            _ => confidence < 0.02f ? (belowBase ? "hiso" : "hero") : "normal",
        };
    }

    /// <summary>選択肢キーの日本語名。</summary>
    public static string ChoiceJa(string question, string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        switch (key)
        {
            case "recommend": return "おすすめ順";
            case "bargain": return "割安買い";
            case "dividend": return "配当重視";
            case "dungeon_focus": return "次ダンジョン特化";
            case "hero": return "勇者";
            case "demon": return "魔王軍";
            case "none": return question == "stream_side" ? "見守るだけ" : "なし";
            case "heal": return "回復";
            case "skill": return "スキル";
            case "special": return "必殺技";
            case "trap": return "罠";
            case "curse": return "呪い";
            case "reinforce": return "増援";
            case "bossbuff": return "ボス強化";
            case "true": return question == "newspaper" ? "読む" : "買う";
            case "false": return question == "newspaper" ? "読まない" : "買わない";
        }
        return key;
    }
}

/// <summary>ミーム辞書の 1 件（tools/persona_play/memes/memes_YYYY-MM.json の memes[]。使う項目だけ）。</summary>
public sealed class PersonaMeme
{
    public const string WhoZundamon = "zundamon";
    public const string WhoMetanReluctant = "metan_reluctant";
    public const string WhoBoth = "both";

    [JsonProperty("id")] public string Id;
    [JsonProperty("phrase")] public string Phrase;
    [JsonProperty("tone")] public string Tone;
    [JsonProperty("who")] public string Who;
    [JsonProperty("usage")] public List<string> Usage;
    [JsonProperty("question_keys")] public List<string> QuestionKeys;
    [JsonProperty("zundamon_line")] public string ZundamonLine;
    [JsonProperty("metan_reaction")] public string MetanReaction;
    [JsonProperty("trend_score")] public float TrendScore = 1f;
    /// <summary>減衰の重み（digest.mjs が更新。無ければ 1）。</summary>
    [JsonProperty("weight")] public float? Weight;
    /// <summary>digest.mjs の状態（"active" / "retired"）。retired は使わない。</summary>
    [JsonProperty("status")] public string Status;
    [JsonProperty("retired")] public bool? Retired;

    /// <summary>選ぶときの重み = trend_score × weight（減衰したものほど出にくい）。</summary>
    [JsonIgnore] public double SelectionWeight => Math.Max(0f, TrendScore) * Math.Max(0f, Weight ?? 1f);

    public bool HasKey(string key) => QuestionKeys != null && QuestionKeys.Contains(key);
}

/// <summary>
/// ミーム辞書（UserSettings/PersonaPlay.json の memeLexiconPath。既定 tools/persona_play/memes/memes_2026-10.json）。
/// 読めない・空なら null（一言はテンプレートだけ＝ミーム導入前と同じ）。retired・weight が 0.1 未満・必須項目の欠けは捨てる。
/// 同じパスで更新時刻が変わっていなければ前回読んだものを使う。
/// </summary>
public sealed class PersonaMemeLexicon
{
    /// <summary>digest.mjs の RETIRE_BELOW と同じ。これ未満の weight は使わない。</summary>
    public const float RetireBelow = 0.1f;

    internal sealed class FileShape
    {
        [JsonProperty("version")] public string Version;
        [JsonProperty("memes")] public List<PersonaMeme> Memes;
    }

    public string FullPath { get; private set; }
    public string Version { get; private set; }
    public IReadOnlyList<PersonaMeme> Memes { get; private set; } = Array.Empty<PersonaMeme>();

    private static string _cachedPath;
    private static DateTime _cachedWriteTime;
    private static PersonaMemeLexicon _cached;
    private static readonly HashSet<string> Warned = new();

    public static PersonaMemeLexicon Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try
        {
            full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(PersonaPlayConfig.ProjectRoot, path));
        }
        catch (Exception e)
        {
            Warn(path, $"ミーム辞書のパスが不正なのでミームなしで続けます: {path}: {e.Message}");
            return null;
        }
        try
        {
            if (!File.Exists(full))
            {
                Warn(full, $"ミーム辞書が無いのでミームなしで続けます: {full}");
                return null;
            }
            var time = File.GetLastWriteTimeUtc(full);
            if (_cached != null && _cachedPath == full && _cachedWriteTime == time) return _cached;

            var shape = JsonConvert.DeserializeObject<FileShape>(File.ReadAllText(full));
            var list = new List<PersonaMeme>();
            var ids = new HashSet<string>();
            int dropped = 0;
            if (shape?.Memes != null)
            {
                foreach (var m in shape.Memes)
                {
                    if (!Usable(m) || !ids.Add(m.Id)) { dropped++; continue; }
                    list.Add(m);
                }
            }
            if (list.Count == 0)
            {
                Warn(full, $"ミーム辞書に使えるエントリが無いのでミームなしで続けます: {full}");
                return null;
            }
            _cached = new PersonaMemeLexicon { FullPath = full, Version = shape.Version, Memes = list };
            _cachedPath = full;
            _cachedWriteTime = time;
            UnityEngine.Debug.Log($"[PersonaPlay] ミーム辞書 {shape.Version ?? "?"}: {list.Count} 件（除外 {dropped} 件）{full}");
            return _cached;
        }
        catch (Exception e)
        {
            Warn(full, $"ミーム辞書を読めないのでミームなしで続けます: {full}: {e.Message}");
            return null;
        }
    }

    private static bool Usable(PersonaMeme m)
    {
        if (m == null || string.IsNullOrEmpty(m.Id) || m.QuestionKeys == null || m.QuestionKeys.Count == 0) return false;
        if (m.Retired == true || string.Equals(m.Status, "retired", StringComparison.OrdinalIgnoreCase)) return false;
        if (m.Weight.HasValue && m.Weight.Value < RetireBelow) return false;
        if (m.SelectionWeight <= 0) return false;
        switch (m.Who)
        {
            case PersonaMeme.WhoZundamon:
            case PersonaMeme.WhoBoth:
            case PersonaMeme.WhoMetanReluctant:
                // ずんだもんの行（めたんの反応のきっかけにも使う）とめたんの返しの両方が要る
                return !string.IsNullOrEmpty(m.ZundamonLine) && !string.IsNullOrEmpty(m.MetanReaction);
            default:
                return false;
        }
    }

    private static void Warn(string key, string message)
    {
        if (Warned.Add(key)) UnityEngine.Debug.LogWarning("[PersonaPlay] " + message);
    }
}
#endif
