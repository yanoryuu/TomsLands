public static class DungeonLevelUpDialogueLoader
{
    public static string Get(string id, string dungeonName = "", int value = 0)
    {
        return id switch
        {
            "open" =>
                "ふふん、ダンジョンへの支援設営はここでやるのじゃ！ ゴールドを投じればダンジョンの格が上がり、次の階層ではより手強い魔物を配備できる。見返りの報酬も大きくなるから、勇者どもを迎え撃つ準備としては大事な投資じゃぞ。",
            "success" =>
                $"{dungeonName}への支援、完了じゃ！ Lv.{value}相当の設営になったぞ。魔物の配置も報酬も強化されるゆえ、次に勇者が来たらたっぷり思い知らせてやるのじゃ。",
            "shortage" =>
                $"{dungeonName}を支援するには {value}G 必要じゃ。むむ、今の金庫では設営班が動かぬな。先に稼いでから、もう一度わらわに任せるがよい！",
            "max" =>
                $"{dungeonName}はもう支援し尽くしておる。これ以上は設備も魔物も入りきらぬ、まさに完成形じゃ！",
            _ => string.Empty
        };
    }

    public static string GetSelection(string dungeonName, int currentLevel, int nextLevel, int cost, bool isInfoKnown, bool isMax)
    {
        if (isMax)
        {
            return $"{dungeonName}はLv.{currentLevel}、もう仕上がっておる。ここまで整えば、魔王軍の拠点としては申し分ないのじゃ！";
        }

        string infoPart = isInfoKnown
            ? "敵の構成と報酬も見えておるから、支援効果を比べて選ぶがよい。"
            : "まだ敵情報は伏せられておる。費用と現在Lvを見て、勘と野望で選ぶのじゃ。";

        return $"{dungeonName}はLv.{currentLevel}からLv.{nextLevel}へ支援できるぞ。必要な設営費は{cost}Gじゃ。{infoPart}";
    }

    public static string GetCharacterTalk(int index)
    {
        return index switch
        {
            1 => "くく、どのダンジョンに兵站を回すかで戦局は変わるのじゃ。派手な勝利ほど、裏では地味な準備が物を言うのじゃぞ。",
            2 => "金貨は血潮じゃ。今ここで流した一滴が、次の戦いで勇者どもの膝を折らせる。惜しむでない、攻めの投資をせい！",
            3 => "わらわの設営班に任せれば、罠も補給も万全じゃ。勇者が来る前に盤面を固めて、笑って迎え撃つのじゃ！",
            _ => "支援計画はいつでも立て直せる。焦らず、だが確実に魔王軍を強くしていくのじゃ。"
        };
    }
}
