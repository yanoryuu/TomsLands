/// <summary>
/// ターン開始時にプレイヤーへ提示するヒント1件のデータ。
/// </summary>
public class TurnActionHint
{
    public HintPriority Priority;
    public string Message;
    public ActionTarget Target;
}

public enum HintPriority
{
    Critical, // 赤：今すぐやるべき
    Warning,  // 黄：できればやるべき
    Info,     // 青：やると有利
}

public enum ActionTarget
{
    None,
    ItemSelection, // 陳列設定パネルを開く
    BlackSmith,    // 鍛冶屋へ移動
    Advertisement, // 広告購入画面へ移動
}
