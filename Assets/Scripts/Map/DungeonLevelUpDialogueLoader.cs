public static class DungeonLevelUpDialogueLoader
{
    public static string Get(string id, string dungeonName = "", int value = 0)
    {
        return id switch
        {
            "open" =>
                "ふふん、魔王軍への出資はここで受け付けるのじゃ！　出資すればダンジョンの格が上がり、次の階層ではより手強い魔物を配備できる。見返りの配当も大きくなるから、勇者どもを迎え撃つ準備としては大事な出資じゃぞ。",
            "success" =>
                $"{dungeonName}への出資、確かに受け取った！　Lv.{value}の設営になったぞ。魔物の配置も配当も強化されるゆえ、次に勇者が来たらたっぷり思い知らせてやるのじゃ。",
            "shortage" =>
                $"{dungeonName}に出資するには {value:N0}G 必要じゃ。むむ、今の金庫では設営班が動かぬな。先に稼いでから、もう一度我に任せるがよい！",
            "max" =>
                $"{dungeonName}はもう出資し尽くしておる。これ以上は設備も魔物も入りきらぬ、まさに完成形じゃ！",
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
            ? "敵の構成と配当も見えておるから、出資の効き目を比べて選ぶがよい。"
            : "まだ敵情報は伏せられておる。出資額と今のLvを見て、勘と野望で選ぶのじゃ。";

        if (currentLevel <= DungeonLevels.Unfunded)
            return $"{dungeonName}にはまだ出資しておらんな。{cost:N0}Gの出資でLv.1になり、勇者が負ければ配当が入るぞ。{infoPart}";
        return $"{dungeonName}はLv.{currentLevel}からLv.{nextLevel}へ上げられるぞ。必要な出資は{cost:N0}Gじゃ。{infoPart}";
    }

    public static string GetCharacterTalk(int index)
    {
        return index switch
        {
            1 => "くく、どのダンジョンに兵站を回すかで戦局は変わるのじゃ。派手な勝利ほど、裏では地味な準備が物を言うのじゃぞ。",
            2 => "金貨は血潮じゃ。今ここで流した一滴が、次の戦いで勇者どもの膝を折らせる。惜しむでない、攻めの出資をせい！",
            3 => "我が設営班に任せれば、罠も補給も万全じゃ。勇者が来る前に盤面を固めて、笑って迎え撃つのじゃ！",
            _ => "焦らず、だが確実に魔王軍を強くしていくのじゃ。どのダンジョンに出資するかは、お主の読み次第じゃぞ。"
        };
    }
}
