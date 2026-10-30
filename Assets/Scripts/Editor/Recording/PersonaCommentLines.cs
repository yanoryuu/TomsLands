using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// 配信コメント（YouTube ライブ）への返事の一言（D:\Tools\tokotom-live\COMMENT_SPEC.md）。テンプレートから選ぶ（テキスト LLM は使わない）。
/// 人物像は tools/jikkyo_video/CHARACTERS.md。判断・確率・学習・教訓には一切触らない（乱数も呼ぶ側の専用の System.Random）。
///
/// キー: "z|cat|sub" / "m|cat|sub"（先に話す人）、"z+|cat" / "m+|cat"（相手の後に一言足す人）。sub が無ければ "z|cat" に落ちる。
/// 行の頭の [..] は使える条件: run（ラン中）/ between（ランの合間）/ poor・rich（所持金）/ debtSoon（返済が近い）/ win・lose（直前の配信）/ e=表情。
/// 穴埋め: {money} 所持金 / {day} 「17日目」 / {debtDays} 「3日」 / {debt} 返済額 / {funds} 村資金 / {runs} 「91回」 / {stream} 直前の配信の損益 / {amount} スパチャの額。
/// 穴が埋められない行は選ばない（ランの合間に所持金を言わない）。コメントの本文と名前は一言に入れない（画面には配信側が出す）。
/// </summary>
public static class PersonaCommentLines
{
    public static readonly string[] Cats =
    {
        "greet", "cheer", "tease", "bet_hero", "bet_demon", "ask", "superchat",
        "leave", "laugh", "clap", "game", "ai", "toko", "other",
    };

    /// <summary>同じ行（テンプレート）は 1 配信にこの回数まで。</summary>
    public const int MaxPerStream = 3;
    /// <summary>直近に使った行を覚えておく数（新しいほど選ばれにくくする）。</summary>
    public const int RecentWindow = 24;

    private sealed class Pool
    {
        public string E;
        public string[] Lines;
    }

    private static Pool P(string e, params string[] lines) => new() { E = e, Lines = lines };

    private static readonly Dictionary<string, Pool> Templates = new()
    {
        // ================= 挨拶 =================
        ["z|greet|morning"] = P("ama",
            "おはようなのだ！ 朝から見に来てくれてうれしいのだ",
            "おはようなのだ。ぼくはずっと起きてるのだ",
            "おはようなのだ！ 今日も勘は冴えてるのだ",
            "[run]おはようなのだ！ お店は{day}なのだ",
            "おはようなのだ！ 朝のずんだ餅はおいしいのだ"),
        ["m|greet|morning"] = P("normal",
            "おはよう。朝から帳簿を見る人は好きよ",
            "おはよう。ずんだもんは寝てないわよ、たぶん",
            "[run]おはよう。今は{day}よ",
            "おはよう。早起きね"),
        ["z|greet|evening"] = P("ama",
            "こんばんはなのだ！ ゆっくりしていくのだ",
            "こんばんはなのだ。夜のぼくは勘が鋭いのだ",
            "こんばんはなのだ！ ずんだ餅、半分あげるのだ",
            "[run]こんばんはなのだ！ 今{day}なのだ",
            "こんばんはなのだ。夜ふかし仲間なのだ"),
        ["m|greet|evening"] = P("normal",
            "こんばんは。夜ふかしはほどほどにね",
            "こんばんは。ちょうどいいところよ",
            "[run]こんばんは。今は{day}、所持金は{money}よ",
            "こんばんは。ゆっくりしていって"),
        ["z|greet|first"] = P("ama",
            "初見さんいらっしゃいなのだ！ ぼくがずんだもんなのだ",
            "はじめましてなのだ！ ここはぼくのお店なのだ",
            "いらっしゃいなのだ！ 勘でお店をやってるのだ",
            "初見さん歓迎なのだ！ ゆっくり見ていくのだ",
            "はじめましてなのだ。ぼくの勘、見ていってほしいのだ",
            "[between]はじめましてなのだ！ 今は村でひと休み中なのだ"),
        ["m|greet|first"] = P("normal",
            "いらっしゃい。ずんだもんが勘で決めて、私が止める配信よ",
            "はじめまして。止めても聞かない子の配信へようこそ",
            "ようこそ。説明書は私が読んでおいたわ",
            "初見さんね。ゆっくりしていって",
            "はじめまして。数字は私が見てるから安心して"),
        ["z|greet"] = P("ama",
            "こんにちはなのだ！ 来てくれてうれしいのだ",
            "いらっしゃいなのだ！ ゆっくりしていくのだ",
            "こんにちはなのだ。今日もぼくの勘が見られるのだ",
            "[run]いらっしゃいなのだ！ 今{day}、がんばってるのだ",
            "[between]いらっしゃいなのだ！ 今は村でひと休みなのだ",
            "やっほーなのだ！ ちょうどいいところに来たのだ"),
        ["m|greet"] = P("normal",
            "いらっしゃい。ちょうどいいところよ",
            "こんにちは。ゆっくりしていって",
            "[run]いらっしゃい。今は{day}よ",
            "[between]いらっしゃい。今はランの合間、村の帳簿を見てるところ",
            "こんにちは。ずんだもんが何かする前でよかったわ",
            "ようこそ。今日も数えてるからね"),
        ["z+|greet"] = P("ama",
            "いらっしゃいなのだ！",
            "ゆっくりしていくのだ！",
            "ぼくもあいさつするのだ。いらっしゃいなのだ",
            "来てくれてうれしいのだ！",
            "ずんだ餅、半分あげるのだ"),
        ["m+|greet"] = P("normal",
            "いらっしゃい。",
            "ゆっくりしていって。",
            "[run]今は{day}。所持金は{money}よ",
            "ようこそ。騒がしい子でごめんなさいね",
            "挨拶はできるのよね、この子",
            "ちょうどいいところよ。"),

        // ================= 応援・褒め =================
        ["z|cheer|cute"] = P("ama",
            "かわいいって言われたのだ！ ……照れるのだ",
            "えへへ、知ってるのだ。でもうれしいのだ",
            "かわいいのだ？ ずんだ餅のおかげなのだ",
            "照れるのだ……今日は勘も冴えそうなのだ",
            "ありがとうなのだ！ 77 倍うれしいのだ"),
        ["z|cheer|genius"] = P("ama",
            "天才って言われたのだ！ やっぱりなのだ",
            "ほめられたのだ！ めたん、聞いたのだ？",
            "えへへ、ぼくの勘は天才なのだ",
            "ほら、わかる人にはわかるのだ",
            "[win]さっきの配信、見てたのだ？ ぼくの勘なのだ"),
        ["z|cheer"] = P("ama",
            "がんばるのだ！ 見ててほしいのだ",
            "応援ありがとうなのだ！ 次は当てるのだ",
            "うれしいのだ！ 勘がもっと冴えるのだ",
            "まかせるのだ！ プラスにするのだ",
            "[run]がんばるのだ！ 今は{money}、もっと増やすのだ",
            "[poor]応援があればお金がなくても大丈夫なのだ",
            "[lose]さっきは負けたけど、次は当たるのだ！",
            "ありがとうなのだ！ ずんだ餅で乾杯なのだ"),
        ["m|cheer|cute"] = P("normal",
            "ありがとう。素直に受け取っておくわ",
            "……ありがとう。悪い気はしないわ",
            "ありがとう。でも帳簿はかわいくないのよ",
            "[e=tsun]かわいいのはずんだもんでしょ。……ありがと",
            "[e=hiso]……今のは聞かなかったことにするわ"),
        ["m|cheer|genius"] = P("normal",
            "ありがとう。読んでから決めてるだけよ",
            "天才じゃないわ。説明書を読んだだけ",
            "ありがとう。数字が合ってるときはね"),
        ["m|cheer"] = P("normal",
            "ありがとう。ちゃんと届いてるわ",
            "応援ありがとう。手堅くいくわね",
            "ありがとう。ずんだもんが調子に乗らない程度にお願いね",
            "ありがとう。その分、損はさせないわ",
            "[run]ありがとう。{day}も堅くいくわ",
            "ありがとう。……がんばるわ"),
        ["z+|cheer"] = P("ama",
            "ぼくにもなのだ？ うれしいのだ！",
            "めたんが照れてるのだ",
            "ぼくもがんばるのだ！",
            "応援されたら勝つしかないのだ！",
            "えへへ、なのだ"),
        ["m+|cheer"] = P("tsun",
            "調子に乗らないの。",
            "褒めると全部張るわよ、この子",
            "[e=normal]ありがとう。私からもお礼を言うわ",
            "[e=ama]……まあ、今日は認めてあげる",
            "[run]浮かれる前に、所持金は{money}よ",
            "言われたそばからやらかさないでね",
            "それは認めるわ。今日はね"),

        // ================= からかい =================
        ["z|tease|broke"] = P("tsun",
            "[rich]金欠じゃないのだ！ 今は{money}あるのだ！",
            "[poor][e=namida]……{money}しかないのだ。でも次は当たるのだ",
            "[run]金欠じゃないのだ。{money}は大金なのだ",
            "金欠じゃないのだ！ お金がちょっとお出かけしてるだけなのだ",
            "[e=hero]お金は使うと増えるのだ。今はその途中なのだ",
            "[between]村資金は{funds}もあるのだ！ 金欠じゃないのだ",
            "ずんだ餅は買えるから大丈夫なのだ"),
        ["z|tease|bankrupt"] = P("hero",
            "破産しないのだ！ ……たぶんなのだ",
            "[debtSoon][e=hiso]返済まであと{debtDays}……大丈夫なのだ、計画通りなのだ",
            "破産なんてしないのだ。ぼくの勘が言ってるのだ",
            "[run]{money}あるのだ。破産はまだ遠いのだ",
            "破産の二文字はぼくの辞書にないのだ",
            "縁起でもないのだ！ でも次は当たるのだ"),
        ["z|tease|missed"] = P("hero",
            "外してないのだ！ あれは練習なのだ",
            "[lose]さっきのは練習なのだ。次は当たるのだ",
            "惜しかったのだ。惜しくはないのだ",
            "[e=namida]うう……でも学習したのだ",
            "外したのはジュラル星人のせいなのだ！",
            "[win][e=tsun]さっきの配信は当てたのだ！ 見てなかったのだ？"),
        ["z|tease"] = P("tsun",
            "ぼくはちゃんとやってるのだ！",
            "[e=hero]言い返せないのだ……でも次は当たるのだ",
            "うう、手厳しいのだ。でも負けないのだ",
            "[e=hero]聞いてるのだ。聞いてるけど、ぼくの勘でいくのだ",
            "[e=hero]見てるのだ、次でびっくりさせるのだ"),
        ["m|tease|broke"] = P("normal",
            "[run]所持金は{money}。金欠かどうかは返済しだいね",
            "金欠なのは私の方よ。お小遣いの話だけど",
            "[between]村資金は{funds}。お店のお金と村のお金は別なのよ",
            "[poor]{money}。否定はできないわね",
            "[rich]{money}あるわ。今日のところはね"),
        ["m|tease|bankrupt"] = P("normal",
            "[debtSoon]返済まで{debtDays}、額は{debt}。笑えないわね",
            "[run]{money}あるうちは大丈夫。使い方しだいよ",
            "破産させないために私がいるの",
            "その心配、私もしてるわ"),
        ["m|tease|missed"] = P("normal",
            "[lose]外したのは事実ね。さっきの配信は{stream}よ",
            "外したのは事実。原因は見直すわ",
            "[win]さっきの配信は{stream}。外してばかりでもないのよ",
            "言わないであげて。本人は練習って言うから"),
        ["m|tease"] = P("normal",
            "事実だから言い返せないわね",
            "否定はしないわ。",
            "[e=tsun]数えてるからね。",
            "手厳しいけど、だいたい合ってるわ",
            "[run]今は{day}、所持金は{money}。判断はそれからね"),
        ["m+|tease"] = P("tsun",
            "さっき入れすぎようとしたのは誰だったかしら",
            "言い訳は 2 行までよ",
            "[run][e=normal]所持金は{money}。それが答えよ",
            "[lose]さっきの配信は{stream}。練習にしては高いわね",
            "練習の授業料、高いのよ",
            "聞いてるって言うときは、聞いてないのよ",
            "否定するところ、そこなの？",
            "[debtSoon][e=normal]返済まであと{debtDays}よ。忘れてないわよね",
            "はいはい。"),
        ["z+|tease"] = P("tsun",
            "めたんまで言うのだ！？",
            "[e=hero]ひどいのだ！ でも次は当たるのだ",
            "なんだお前、なのだ",
            "[e=hiso]聞いてるのだ……",
            "[e=namida]うう……学習したのだ"),
        // めたんへのからかい（慎重すぎ・ビビり・弱気・臆病。めたんが先に話す）
        ["m|tease|coward"] = P("tsun",
            "慎重なのと臆病なのは違うのよ",
            "慎重すぎて儲からない、ね。数えてから言って",
            "[e=hero]……言ったわね。次の配信、見てなさい",
            "[win][e=normal]さっきの配信は{stream}。慎重で悪かったわね"),
        ["z+|tease|coward"] = P("tsun",
            "めたんは慎重すぎて儲からないのだ！",
            "[e=ama]ほら、みんなもそう思ってるのだ",
            "ぼくみたいに全部張るのだ！"),

        // ================= 張る側の口出し（判断は変えない） =================
        ["z|bet_hero"] = P("hero",
            "勇者に張れって言われたのだ。考えておくのだ",
            "勇者なのだ？ ぼくの勘と相談するのだ",
            "勇者を推す声が来たのだ。……勘はまだ黙ってるのだ",
            "勇者が勝つと店が負けることもあるのだ。考えておくのだ",
            "[run]勇者かぁ……配信の前にもう一度考えるのだ",
            "勇者派なのだ？ 覚えておくのだ"),
        ["m|bet_hero"] = P("normal",
            "勇者ね。数字を見てから決めるわ",
            "勇者に張るなら、勝ち目を数えてからよ",
            "勇者派の意見、受け取ったわ。決めるのは画面の数字よ",
            "勇者が勝つと店が負ける日もあるの。よく見るわ"),
        ["z|bet_demon"] = P("hero",
            "魔王いけって言われたのだ。悪い子のコメントなのだ",
            "魔王軍なのだ？ 勇者には内緒で考えておくのだ",
            "[e=hiso]魔王派が来たのだ。……ちょっとわかるのだ",
            "勇者の敗北はぼくの黒字……考えておくのだ",
            "魔王軍かぁ、ボスは名前が強そうなのだ。考えておくのだ"),
        ["m|bet_demon"] = P("normal",
            "魔王軍ね。勇者には申し訳ないけど、数字しだいよ",
            "魔王に張るなら、防衛の見込みを見てからよ",
            "魔王派の意見ね。決めるのは画面の数字よ",
            "悪い誘いね。考えておくわ"),
        ["m+|bet"] = P("normal",
            "決めるのは数字を見てからよ",
            "張るかどうかは、そのときの画面しだいね",
            "どっちに張っても、全部は張らせないわ",
            "意見は受け取ったわ。判断は別よ",
            "[e=tsun]そう言われると逆に張りたくなるのよね、この子"),
        ["z+|bet"] = P("hero",
            "考えておくのだ！",
            "ぼくの勘と相談するのだ",
            "覚えておくのだ",
            "[e=hiso]うーん、勘がまだ黙ってるのだ"),

        // ================= 質問（ゲームの状態で答える） =================
        ["z|ask|money"] = P("normal",
            "[run][e=ama]今は{money}あるのだ！",
            "[run]所持金は{money}なのだ。もっと増えるのだ",
            "[between]今はランの合間なのだ。村資金は{funds}なのだ",
            "[poor][e=namida]……{money}なのだ。聞かないでほしかったのだ",
            "[e=hiso]お金はいっぱいあるのだ。……たぶんなのだ"),
        ["m|ask|money"] = P("normal",
            "[run]所持金は{money}よ",
            "[run]今は{money}。数えてるからね",
            "[between]今はランの合間。村資金は{funds}よ",
            "[debtSoon]所持金は{money}、返済まであと{debtDays}よ"),
        ["z|ask|day"] = P("normal",
            "[run]今は{day}なのだ！",
            "[run]{day}なのだ。まだまだこれからなのだ",
            "[between]今はランの合間なのだ。村でひと休みなのだ",
            "[between][e=ama]ランは{runs}済みなのだ！ ベテランなのだ"),
        ["m|ask|day"] = P("normal",
            "[run]{day}よ",
            "[run]今は{day}。返済まであと{debtDays}",
            "[between]ランの合間よ。これまでに{runs}遊んだわ"),
        ["z|ask|debt"] = P("normal",
            "[debtSoon][e=hiso]返済まであと{debtDays}なのだ……大丈夫なのだ",
            "[run]次の返済はあと{debtDays}なのだ",
            "[run][e=hero]返済は{debt}なのだ。……払えるのだ",
            "[between]今は返済はないのだ。村でのんびりなのだ"),
        ["m|ask|debt"] = P("normal",
            "[run]次の返済まであと{debtDays}、額は{debt}よ",
            "[run]返済は{debt}。所持金は{money}よ",
            "[between]ランの合間だから返済はないわ"),
        ["z|ask|village"] = P("ama",
            "村資金は{funds}なのだ！ ぼくの村なのだ",
            "村はでっかくなってるのだ。資金は{funds}なのだ",
            "[between]今は村にいるのだ。村資金は{funds}なのだ"),
        ["m|ask|village"] = P("normal",
            "村資金は{funds}よ",
            "村資金は{funds}。使う分と残す分は数えてあるわ"),
        ["z|ask|goal"] = P("hero",
            "目標はお店をでっかくすることなのだ！",
            "目標は黒字なのだ！ 額は見ないのだ",
            "目標は破産しないで勝つことなのだ",
            "村をいちばんの都会にするのだ！"),
        ["m|ask|goal"] = P("normal",
            "目標は破産しないで、ちゃんと増やすことよ",
            "黒字で終えること。それだけ。",
            "村資金を増やして、村を育てることね",
            "[e=tsun]ずんだもんに全部張らせないこと。毎日の目標よ"),
        ["z|ask"] = P("normal",
            "[run]今は{day}、所持金は{money}なのだ",
            "[between]今はランの合間なのだ。村資金は{funds}なのだ",
            "[e=hiso]いい質問なのだ。……めたん、パスなのだ",
            "[e=hero]えーと、ぼくの勘では大丈夫なのだ",
            "[win][e=ama]さっきの配信は{stream}だったのだ！"),
        ["m|ask"] = P("normal",
            "[run]今は{day}、所持金は{money}よ",
            "[between]ランの合間よ。村資金は{funds}",
            "[lose]さっきの配信は{stream}。次で取り返すわ",
            "[win]さっきの配信は{stream}よ",
            "いい質問ね。答えは帳簿にあるわ"),
        ["m+|ask"] = P("normal",
            "[run]返済まではあと{debtDays}よ",
            "[between]村資金は{funds}。ずんだもんの言う通りよ",
            "合ってるわ。珍しく",
            "数字は私が見てるから大丈夫",
            "今のは合ってるわ"),
        ["z+|ask"] = P("ama",
            "めたんは数字に強いのだ",
            "[e=hero]ぼくも言おうとしてたのだ",
            "そういうことなのだ！",
            "[run][e=hero]もっと増やすのだ！"),

        // ================= スパチャ =================
        ["z|superchat"] = P("surprise",
            "{amount}もありがとうなのだ！ 大事に使うのだ",
            "{amount}のスパチャなのだ！ ありがとうなのだ！",
            "わあ、{amount}なのだ！ ずんだ餅が買えるのだ",
            "[e=ama]{amount}ありがとうなのだ！ 仕入れに使うのだ……冗談なのだ",
            "[e=ama]スパチャありがとうなのだ！ {amount}、うれしいのだ",
            "[e=ama]ありがとうなのだ！ {amount}ぶん、がんばるのだ",
            "ありがとうなのだ！ 今日いちばんの黒字なのだ"),
        ["m|superchat"] = P("ama",
            "{amount}、ありがとう。大事にするわ",
            "スパチャありがとう。{amount}、ちゃんと届いてるわ",
            "[e=normal]{amount}もありがとう。ずんだもんには全部は渡さないわ",
            "[e=normal]ありがとう。{amount}、帳簿にしっかり書いておくわ",
            "ありがとう。……うれしいわ"),
        ["m+|superchat"] = P("ama",
            "[e=normal]ありがとう。全部使わせないから安心して",
            "本当にありがとう。私からもお礼を言うわ",
            "[e=tsun]ありがとう。この子に預けると減るから、私が見ておくわ",
            "ありがとう。大事にするわ",
            "ありがとう。……うれしいわ"),
        ["z+|superchat"] = P("ama",
            "ぼくからもありがとうなのだ！",
            "ありがとうなのだ！ がんばるのだ",
            "うれしいのだ！",
            "{amount}、大事に使うのだ！",
            "[e=surprise]めたん、見たのだ？ スパチャなのだ！"),

        // ================= 見送り（ずんだもんは寂しがる、めたんはあっさり） =================
        ["z|leave"] = P("ama",
            "おつかれさまなのだ！ また来てほしいのだ",
            "おやすみなのだ。ぼくはまだお店を開けてるのだ",
            "またねなのだ！ 次はもっと儲けておくのだ",
            "[run][e=hero]またなのだ！ 次に来るまでに{money}を倍にするのだ",
            "[e=namida]もう帰っちゃうのだ？ ……また来るのだ"),
        ["m|leave"] = P("normal",
            "おつかれさま。また来てね",
            "おやすみなさい。帳簿は私が見ておくわ",
            "またね。全部張らないように見張っておくわ",
            "[run]おつかれさま。次は{money}より増えてるといいわね",
            "ありがとう。ゆっくり休んで"),
        ["z+|leave"] = P("ama",
            "おやすみなのだ！",
            "またなのだ！",
            "気をつけて帰るのだ"),
        ["m+|leave"] = P("normal",
            "おやすみなさい。",
            "またね。",
            "[e=tsun]寂しがってるわよ、この子"),

        // ================= 笑われた（ずんだもんはウケたと勘違い、めたんは乗る） =================
        ["z|laugh"] = P("tsun",
            "笑いごとじゃないのだ！",
            "[e=ama]ウケたのだ！ ぼく、おもしろいのだ？",
            "[e=hiso]笑ってくれてうれしいのだ。……笑われてるのだ？",
            "[e=hero]これは作戦なのだ。笑うところじゃないのだ",
            "[lose][e=namida]笑うのはやめてほしいのだ……"),
        ["m|laugh"] = P("normal",
            "笑うしかないわよね。",
            "[e=tsun]笑ってる場合じゃないのよ、帳簿的には",
            "わかる。私も笑いをこらえてるわ",
            "[lose]さっきの配信は{stream}。笑うしかないわ"),
        ["z+|laugh"] = P("tsun",
            "めたんまで笑ってるのだ！？",
            "[e=ama]えへへ、なのだ",
            "[e=hiso]計画通りなのだ"),
        ["m+|laugh"] = P("tsun",
            "笑われてるのよ、あなた",
            "本人は真剣なのよ、これでも",
            "[e=normal]……私もちょっと笑ったわ"),

        // ================= 拍手（勝った直後はマウント、負けた直後は「どこに？」） =================
        ["z|clap"] = P("surprise",
            "[e=ama]拍手なのだ！ もっと拍手するのだ！",
            "[win][e=tsun]ぼくの勘が勝ったのだ！ めたん、見たのだ？",
            "[e=ama]やりますねぇ、なのだ",
            "[lose][e=hiso]……拍手するところ、あったのだ？",
            "[e=hiso]ありがとうなのだ。計画通りなのだ"),
        ["m|clap"] = P("normal",
            "ありがとう。今のは悪くなかったわ",
            "[win]さっきの配信は{stream}。拍手していいわよ",
            "[e=tsun]拍手すると調子に乗るから、ほどほどにね",
            "[lose][e=tsun]どこに拍手したのかしら"),
        ["z+|clap"] = P("ama",
            "えっへんなのだ！",
            "もっとほめるのだ！",
            "[e=tsun]めたん、聞いたのだ？"),
        ["m+|clap"] = P("tsun",
            "浮かれないの。",
            "[e=normal]私からも拍手しておくわ",
            "それ、次あなたに返ってくるからね"),

        // ================= どんなゲーム？（宣伝の一番の機会。タイトルと Steam を言う） =================
        ["z|game"] = P("ama",
            "『トコのトムトムランド』なのだ！ Steamにあるのだ",
            "勇者の配信に賭けて、お店で儲けるゲームなのだ！",
            "Steamで『トコのトムトムランド』って探すのだ！",
            "武器を仕入れて勇者に売って、配信で勝負するのだ",
            "[e=hero]『トコのトムトムランド』、ぼくより上手にできるのだ？",
            "Steamでウィッシュリストに入れてほしいのだ！"),
        ["m|game"] = P("normal",
            "『トコのトムトムランド』よ。Steamで探してみて",
            "勇者の配信を読んで、お店のお金を張るゲームよ",
            "勇者の勝ち負けに賭けるお店のゲーム。Steamにあるわ",
            "魔王側に張ってもいいの。なんでもありよ",
            "[e=hiso]説明書なら私が読んだわ。……表紙は"),
        ["z+|game"] = P("ama",
            "やってみてほしいのだ！",
            "[e=hero]ぼくより上手にできるのだ？",
            "Steamで待ってるのだ！"),
        ["m+|game"] = P("normal",
            "『トコのトムトムランド』、Steamで探してみて",
            "ずんだもんより手堅く遊べるはずよ",
            "全部張らなければ大丈夫よ"),

        // ================= AI なの？（判断は自分たちでしている。言い過ぎない） =================
        ["z|ai"] = P("hero",
            "お店の判断はぼくが自分で決めてるのだ！ 勘なのだ",
            "[e=ama]ぼくはAIなのだ。でも勘は本物なのだ",
            "判断に中の人はいないのだ。ずんだ餅の精がいるだけなのだ",
            "自分で決めてるのだ。だから外すのもぼくなのだ"),
        ["m|ai"] = P("normal",
            "ええ、二人とも自分で判断してるわ。私は慎重な方",
            "AIよ。だから数字はちゃんと見てるの",
            "お店の判断に中の人はいないわ。止める係が私なだけ",
            "判断は私たちが自分で。結果も自分たちのものよ"),
        ["z+|ai"] = P("ama",
            "そういうことなのだ！",
            "[e=hero]ぼくの勘はAIより上なのだ"),
        ["m+|ai"] = P("tsun",
            "勘で決めるAIもいるのよ、そこに",
            "[e=normal]私は数字で決める方よ"),

        // ================= トコちゃん（めたんが先に。トコには甘い） =================
        ["m|toko"] = P("ama",
            "トコちゃんの話？ あの子はいい子よ",
            "トコちゃんにはお世話になってるわ。ずんだもんが",
            "[e=normal]トコちゃんのお店を潰すわけにはいかないの"),
        ["z|toko"] = P("ama",
            "トコちゃんなのだ！ ぼくたちの主人公なのだ",
            "[e=namida]トコちゃん、ごめんなのだ……まだ何もしてないのだ",
            "[e=hero]トコちゃんのためにも儲けるのだ！"),
        ["z+|toko"] = P("tsun",
            "めたんはトコちゃんに甘いのだ",
            "ぼくにも甘くしてほしいのだ"),
        ["m+|toko"] = P("normal",
            "あの子の前では全部張らないでね",
            "[e=ama]トコちゃんの言うことは、だいたい正しいのよ"),

        // ================= それ以外 =================
        ["z|other|zunda"] = P("ama",
            "ずんだ餅の話なのだ？ ぼくの大好物なのだ！",
            "[e=hero]ずんだ餅の精は相場に強いのだ",
            "ずんだ餅、半分あげるのだ"),
        ["m+|other|zunda"] = P("tsun",
            "ずんだ餅の予算は帳簿に入ってないわよ",
            "その話になると長いわよ、この子"),
        ["z|other"] = P("normal",
            "コメントありがとうなのだ！",
            "うんうん、なのだ",
            "[e=hiso]なるほどなのだ。……たぶんわかったのだ",
            "読んだのだ！ ありがとうなのだ",
            "[e=hero]それ、ぼくも思ってたのだ",
            "[e=ama]コメントがあると勘が冴えるのだ",
            "[run]見ててほしいのだ！ 今{day}なのだ"),
        ["m|other"] = P("normal",
            "コメントありがとう。",
            "ええ、ちゃんと読んでるわ",
            "そうね。",
            "ありがとう。ゆっくりしていって",
            "なるほどね。",
            "[run]今は{day}。のんびり見ていって"),
        ["m+|other"] = P("normal",
            "そうね。",
            "ありがとう。",
            "ちゃんと読んでるわ",
            "[e=tsun]分かってないときの返事よ、それ"),
        ["z+|other"] = P("normal",
            "ありがとうなのだ！",
            "うんうん、なのだ",
            "そうなのだ！"),
    };

    // =====================================================
    // 分類（受け箱の cat を使い、無い・知らない・other のときだけ本文の言葉で付け直す）と、cat の中の言い方の違い（sub）
    // =====================================================

    public static string NormalizeCat(PersonaComment c)
    {
        if (c == null) return "other";
        if (c.SuperchatAmount != null) return "superchat";
        string cat = c.Cat;
        if (!string.IsNullOrEmpty(cat) && Array.IndexOf(Cats, cat) >= 0 && cat != "other") return cat;
        return Classify(c.Text);
    }

    /// <summary>
    /// 本文の言葉で分ける（live_chat.py の CATS と同じ言葉）。宣伝の機会（game）と AI の質問を先に見る。
    /// 全角英数・半角カナは NFKC でそろえ、英字は小文字で語の途中では当てない（machine の ai など）。
    /// </summary>
    public static string Classify(string text)
    {
        string t = (text ?? "").Normalize(System.Text.NormalizationForm.FormKC);
        string lower = t.ToLowerInvariant();
        if (Has(t, "何のゲーム", "なんのゲーム", "なにのゲーム", "ゲーム名", "タイトル", "どこで買", "どこで売", "スチーム", "トムトムランド",
                "面白そう", "おもしろそう", "やってみたい", "買いたい", "遊びたい") || Word(lower, "steam")) return "game";
        if (Has(t, "中の人", "自動", "本当に考え", "ほんとに考え", "人間", "人工知能") || Word(lower, "ai")) return "ai";
        if (Has(t, "初見", "はじめまして", "初めまして", "こんにちは", "こんちは", "こんばんは", "こんばんわ", "おはよ", "やっほ", "わこつ")) return "greet";
        if (Regex.IsMatch(t, "勇者.*(張|いけ|行け|賭|推|勝)")) return "bet_hero";
        if (Regex.IsMatch(t, "魔王.*(張|いけ|行け|賭|推|勝)")) return "bet_demon";
        if (Has(t, "おつかれ", "お疲れ", "おやすみ", "またね", "また来", "落ちます", "おちます", "寝ます", "ねます", "ばいばい", "バイバイ",
                "さようなら", "おつ", "乙")) return "leave";
        if (Has(t, "トコ")) return "toko";
        if (Has(t, "金欠", "貧乏", "破産", "借金", "外した", "外れ", "ハズレ", "負け", "下手", "へたくそ")) return "tease";
        if (Has(t, CowardWords)) return "tease";
        if (Has(t, "888", "おめでと", "ナイス", "上手い", "うまい", "やるやん", "やりますね", "拍手") || Word(lower, "gg") || Word(lower, "nice")) return "clap";
        if (IsLaugh(t)) return "laugh";
        if (Has(t, "いくら", "何日", "何G", "目標", "所持金", "村資金", "?", "？")) return "ask";
        if (Has(t, "がんば", "頑張", "かわい", "可愛", "天才", "すご", "応援", "好き", "えらい")) return "cheer";
        return "other";
    }

    /// <summary>めたんへのからかい（tease の sub = coward）の言葉。</summary>
    private static readonly string[] CowardWords = { "慎重すぎ", "慎重過ぎ", "ビビ", "びび", "弱気", "臆病", "チキン" };

    /// <summary>笑い: 草・笑・わろ、「ww」、「〜w」で終わる。ゲームの品物（薬草など）の「草」は数えない。</summary>
    private static bool IsLaugh(string t)
    {
        string s = t.Replace("薬草", "").Replace("毒草", "").Replace("雑草", "");
        if (Has(s, "草", "笑", "わろ", "ワロ")) return true;
        return Regex.IsMatch(s, "[wWｗＷ]{2,}") || Regex.IsMatch(s, "[ぁ-んァ-ヶ一-龠ー！!。…][wWｗＷ]$");
    }

    /// <summary>英単語として含むか（前後が英字でない）。lower は小文字にした本文。</summary>
    private static bool Word(string lower, string word) =>
        Regex.IsMatch(lower, $"(?<![a-z]){Regex.Escape(word)}(?![a-z])");

    public static string SubOf(string cat, PersonaComment c)
    {
        string t = c?.Text ?? "";
        switch (cat)
        {
            case "greet":
                if (c != null && c.First || Has(t, "初見", "はじめまして", "初めまして")) return "first";
                if (Has(t, "おはよ")) return "morning";
                if (Has(t, "こんばんは", "こんばんわ", "ばんわ")) return "evening";
                return null;
            case "cheer":
                if (Has(t, "かわい", "可愛")) return "cute";
                if (Has(t, "天才", "すご", "賢", "えらい")) return "genius";
                return null;
            case "tease":
                // めたんへのからかい（ずんだもんを名指ししていなければ）。めたんが先に話す
                if (c?.To != "z" && Has(t.Normalize(System.Text.NormalizationForm.FormKC), CowardWords)) return "coward";
                if (Has(t, "金欠", "貧乏", "お金ない", "金ない", "文無し")) return "broke";
                if (Has(t, "破産", "借金", "倒産", "返済")) return "bankrupt";
                if (Has(t, "外", "ハズレ", "負け", "下手", "へた")) return "missed";
                return null;
            case "ask":
                if (Has(t, "返済", "借金")) return "debt";
                if (Has(t, "村")) return "village";
                if (Has(t, "目標", "ゴール")) return "goal";
                if (Has(t, "何日", "日目")) return "day";
                if (Has(t, "いくら", "所持金", "お金", "残高", "何G", "何万")) return "money";
                return null;
            case "other":
                // ずんだ餅の話（「ずんだもん」「ずんだちゃん」の名指しは除く）
                if (Regex.IsMatch(t, "ずんだ(?!もん|ちゃん)")) return "zunda";
                return null;
        }
        return null;
    }

    private static bool Has(string t, params string[] words)
    {
        foreach (var w in words)
            if (t.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    /// <summary>スパチャの額を読み上げやすくする（「¥500」→「500円」）。読めない形はそのまま。</summary>
    public static string AmountJa(string amount)
    {
        if (string.IsNullOrWhiteSpace(amount)) return null;
        string a = amount.Trim();
        var m = Regex.Match(a, @"^[¥￥]\s*([\d,]+)$");
        if (m.Success) return m.Groups[1].Value + "円";
        m = Regex.Match(a, @"^([\d,]+)\s*円$");
        if (m.Success) return m.Groups[1].Value + "円";
        return a;
    }

    // =====================================================
    // 選ぶ
    // =====================================================

    /// <summary>テンプレートの 1 行（頭の [..] を読んだもの）。</summary>
    public sealed class Template
    {
        public string Raw;
        public string Text;
        public string E;
        public List<string> Conds = new();
        public List<string> Holes = new();
    }

    private static readonly Regex HoleRe = new(@"\{(\w+)\}");
    private static readonly Dictionary<string, Template> Parsed = new();

    public static Template Parse(string raw, string defaultE)
    {
        string key = defaultE + "\u0001" + raw;
        if (Parsed.TryGetValue(key, out var t)) return t;
        t = new Template { Raw = raw, E = defaultE };
        string s = raw;
        while (s.StartsWith("[", StringComparison.Ordinal))
        {
            int end = s.IndexOf(']');
            if (end < 0) break;
            string tag = s.Substring(1, end - 1);
            if (tag.StartsWith("e=", StringComparison.Ordinal)) t.E = tag.Substring(2);
            else t.Conds.Add(tag);
            s = s.Substring(end + 1);
        }
        t.Text = s;
        foreach (Match m in HoleRe.Matches(s)) t.Holes.Add(m.Groups[1].Value);
        Parsed[key] = t;
        return t;
    }

    /// <summary>全部のテンプレート（テスト用: 人格・キー・行）。</summary>
    public static IEnumerable<(string key, Template t)> All()
    {
        foreach (var kv in Templates)
            foreach (var raw in kv.Value.Lines)
                yield return (kv.Key, Parse(raw, kv.Value.E));
    }

    public static bool HasKey(string key) => Templates.ContainsKey(key);

    /// <summary>
    /// その人・種類のテンプレートの既定の表情（"z|greet" の E など。LLM の返事は表情を持たないのでこれを使う）。無ければ "normal"。
    /// </summary>
    public static string DefaultE(string speaker, string cat)
    {
        string group = cat is "bet_hero" or "bet_demon" && !Templates.ContainsKey($"{speaker}|{cat}") ? "bet" : cat;
        foreach (var key in new[] { $"{speaker}|{cat}", $"{speaker}|{group}", $"{speaker}+|{group}" })
            if (Templates.TryGetValue(key, out var pool) && !string.IsNullOrEmpty(pool.E)) return pool.E;
        return "normal";
    }

    /// <summary>
    /// 1 行選ぶ。speaker = "z" / "m"、follow = 相手の後に足す一言か。条件と穴が満たせる行のうち、1 配信 MaxPerStream 回未満で、
    /// 直近に使った行ほど選ばれにくく。満たす行が無ければ null（呼ぶ側はその人を黙らせる）。
    /// counts / recent は呼ぶ側が持つ（配信ごと）。選んだ行はここで数える。
    /// </summary>
    public static (string line, string e, string template)? Pick(string speaker, bool follow, string cat, string sub, PersonaCommentFacts facts,
        string amount, Random rng, Dictionary<string, int> counts, List<string> recent)
    {
        string group = cat is "bet_hero" or "bet_demon" && follow ? "bet" : cat;
        string who = follow ? speaker + "+" : speaker;
        var keys = new List<string>();
        if (!string.IsNullOrEmpty(sub)) keys.Add($"{who}|{group}|{sub}");
        keys.Add($"{who}|{group}");
        var vars = Vars(facts, amount);
        foreach (var key in keys)
        {
            if (!Templates.TryGetValue(key, out var pool)) continue;
            var cands = new List<(Template t, string line)>();
            foreach (var raw in pool.Lines)
            {
                var t = Parse(raw, pool.E);
                if (!CondsOk(t, facts) || !t.Holes.All(vars.ContainsKey)) continue;
                string line = Fill(t.Text, vars);
                if (!PersonaLines.IsClean(line)) continue;
                cands.Add((t, line));
            }
            if (cands.Count == 0) continue;
            var under = cands.Where(c => Count(counts, c.t.Raw) < MaxPerStream).ToList();
            if (under.Count == 0) continue;   // この配信で使い切った: 汎用のキーに落ちる（それも無ければ黙る）
            var weights = under.Select(c => Weight(recent, c.t.Raw)).ToList();
            double sum = weights.Sum();
            double x = rng.NextDouble() * sum;
            int pick = under.Count - 1;
            for (int i = 0; i < under.Count; i++)
            {
                x -= weights[i];
                if (x < 0) { pick = i; break; }
            }
            var chosen = under[pick];
            if (counts != null) counts[chosen.t.Raw] = Count(counts, chosen.t.Raw) + 1;
            if (recent != null)
            {
                recent.Remove(chosen.t.Raw);
                recent.Add(chosen.t.Raw);
                while (recent.Count > RecentWindow) recent.RemoveAt(0);
            }
            return (chosen.line, chosen.t.E, chosen.t.Raw);
        }
        return null;
    }

    private static int Count(Dictionary<string, int> counts, string raw) =>
        counts != null && counts.TryGetValue(raw, out var n) ? n : 0;

    /// <summary>直近に使った行ほど軽く（いちばん新しい 0.05 → 窓の端で 1 近く）。使っていない行は 1。</summary>
    private static double Weight(List<string> recent, string raw)
    {
        if (recent == null) return 1.0;
        int idx = recent.LastIndexOf(raw);
        if (idx < 0) return 1.0;
        int age = recent.Count - 1 - idx;   // 0 = いちばん新しい
        return 0.05 + 0.95 * age / RecentWindow;
    }

    public static bool CondsOk(Template t, PersonaCommentFacts f)
    {
        f ??= new PersonaCommentFacts();
        foreach (var c in t.Conds)
        {
            bool ok = c switch
            {
                "run" => f.InRun,
                "between" => !f.InRun,
                "poor" => f.IsPoor,
                "rich" => f.IsRich,
                "debtSoon" => f.DebtSoon,
                "win" => f.LastStreamDelta.HasValue && f.LastStreamDelta.Value > 0,
                "lose" => f.LastStreamDelta.HasValue && f.LastStreamDelta.Value < 0,
                _ => false,
            };
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>埋められる穴（値が分かるものだけ）。</summary>
    public static Dictionary<string, string> Vars(PersonaCommentFacts f, string amount)
    {
        f ??= new PersonaCommentFacts();
        var v = new Dictionary<string, string>();
        if (f.InRun && f.Money >= 0) v["money"] = PersonaConsult.Money(f.Money);
        if (f.InRun && f.Day > 0) v["day"] = f.Day.ToString(CultureInfo.InvariantCulture) + "日目";
        if (f.InRun && f.DebtDaysLeft >= 1) v["debtDays"] = f.DebtDaysLeft.ToString(CultureInfo.InvariantCulture) + "日";
        if (f.InRun && f.DebtAmount > 0) v["debt"] = PersonaConsult.Money(f.DebtAmount);
        if (f.VillageFunds >= 0) v["funds"] = PersonaConsult.Money(f.VillageFunds);
        if (f.Runs > 0) v["runs"] = f.Runs.ToString(CultureInfo.InvariantCulture) + "回";
        if (f.LastStreamDelta.HasValue && f.LastStreamDelta.Value != 0)
            v["stream"] = (f.LastStreamDelta.Value > 0 ? "プラス" : "") + PersonaConsult.Money(f.LastStreamDelta.Value);
        string a = AmountJa(amount);
        if (!string.IsNullOrEmpty(a)) v["amount"] = a;
        return v;
    }

    private static string Fill(string text, Dictionary<string, string> vars) =>
        HoleRe.Replace(text, m => vars.TryGetValue(m.Groups[1].Value, out var s) ? s : "");

    // =====================================================
    // 1 件のコメントへの返事（1〜2 行）
    // =====================================================

    public sealed class ReplyLine
    {
        public string Speaker;   // "z" / "m"
        public string Line;
        public string E;
        public string Template;
    }

    /// <summary>
    /// 返事を作る。基本はずんだもん → めたん（逆・片方だけもある）。質問はめたんが先に答えることが多い。トコの話とめたんへのからかいはめたんが先。
    /// 名指し（受け箱の to = "z" / "m"）があればその人が先に話す（抽選より優先）。
    /// 先に話す人の行が無ければもう一人が先に話し、二人とも無ければ空（返さない）。
    /// </summary>
    public static List<ReplyLine> Reply(PersonaComment c, string cat, PersonaCommentFacts facts, Random rng,
        Dictionary<string, int> counts, List<string> recent)
    {
        var lines = new List<ReplyLine>();
        if (c == null || rng == null) return lines;
        string sub = SubOf(cat, c);
        double zFirst = sub == "coward" ? 0.0 : cat switch
        {
            "ask" => 0.45, "superchat" => 0.75, "toko" => 0.15, "game" => 0.55, "ai" => 0.55, _ => 0.72,
        };
        double second = cat switch { "other" => 0.3, "superchat" => 0.85, _ => 0.72 };
        string lead = rng.NextDouble() < zFirst ? "z" : "m";   // 名指しがあっても乱数は同じ数だけ使う
        if (c.To is "z" or "m") lead = c.To;
        string other = lead == "z" ? "m" : "z";
        var first = Pick(lead, false, cat, sub, facts, c.SuperchatAmount, rng, counts, recent);
        if (first == null)
        {
            (lead, other) = (other, lead);
            first = Pick(lead, false, cat, sub, facts, c.SuperchatAmount, rng, counts, recent);
        }
        if (first == null) return lines;
        lines.Add(new ReplyLine { Speaker = lead, Line = first.Value.line, E = first.Value.e, Template = first.Value.template });
        if (rng.NextDouble() < second)
        {
            var next = Pick(other, true, cat, sub, facts, c.SuperchatAmount, rng, counts, recent);
            if (next != null && next.Value.line != first.Value.line)
                lines.Add(new ReplyLine { Speaker = other, Line = next.Value.line, E = next.Value.e, Template = next.Value.template });
        }
        return lines;
    }
}

/// <summary>受け箱の 1 行（D:\Tools\tokotom-live\chat\inbox.jsonl）。</summary>
public sealed class PersonaComment
{
    public string Id;
    public long UnixMs;
    public string Author;
    public string Text;
    public string Cat;
    public bool First;
    /// <summary>スパチャの額（superchat.amount）。スパチャでなければ null。</summary>
    public string SuperchatAmount;
    /// <summary>名指し（"z" / "m"）。その人が先に話す。無い・知らない値は null。</summary>
    public string To;

    /// <summary>1 行の JSON を読む（読めない・id が無い行は null）。</summary>
    public static PersonaComment Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            var c = new PersonaComment
            {
                Id = (string)o["id"],
                UnixMs = o["unixMs"]?.Type is Newtonsoft.Json.Linq.JTokenType.Integer or Newtonsoft.Json.Linq.JTokenType.Float ? (long)o["unixMs"] : 0,
                Author = (string)o["author"] ?? "",
                Text = (string)o["text"] ?? "",
                Cat = (string)o["cat"],
                First = o["first"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)o["first"],
            };
            if (o["to"]?.Type == Newtonsoft.Json.Linq.JTokenType.String && (string)o["to"] is "z" or "m") c.To = (string)o["to"];
            if (o["superchat"] is Newtonsoft.Json.Linq.JObject sc)
                c.SuperchatAmount = (string)sc["amount"] ?? "";
            return string.IsNullOrEmpty(c.Id) ? null : c;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>返事に使うゲームの状態（分からない値は -1 / null）。ランの合間は村の情報だけ。</summary>
public sealed class PersonaCommentFacts
{
    public bool InRun;
    public int Money = -1;
    public int Day = -1;
    public int DebtDaysLeft = -1;
    public int DebtAmount = -1;
    public int VillageFunds = -1;
    public int Runs = -1;
    /// <summary>このランの直前の配信の損益（G）。分からなければ null。</summary>
    public int? LastStreamDelta;

    public bool IsPoor => InRun && Money >= 0 && (Money < 30000 || (DebtAmount > 0 && Money < DebtAmount));
    public bool IsRich => InRun && Money >= 100000 && !IsPoor;
    public bool DebtSoon => InRun && DebtDaysLeft >= 1 && DebtDaysLeft <= 2 && DebtAmount > 0;
}
