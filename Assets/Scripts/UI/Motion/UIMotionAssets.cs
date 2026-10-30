using UnityEngine;

/// <summary>
/// UI アニメーションの共通部品が使う既存スプライトへの参照（Resources/UIMotion/UIMotionAssets.asset）。
/// 絵そのものは複製せず、村の部品・到着演出の絵を指すだけ。未設定の項目はコードで描いた代わりの絵を使う。
/// </summary>
[CreateAssetMenu(menuName = "TomsLands/UI Motion Assets", fileName = "UIMotionAssets")]
public sealed class UIMotionAssets : ScriptableObject
{
    [Tooltip("コイン（#2）。Assets/UI/Village/ArrivalFx/arrival_coin.png")]
    public Sprite coin;
    [Tooltip("きらめき星（#5・#8・#11）。arrival_sparkle.png")]
    public Sprite sparkle;
    [Tooltip("ぼんやりした光（#11 レア）。arrival_glow.png")]
    public Sprite glow;
    [Tooltip("放射光（#5）。vui_rays.png")]
    public Sprite rays;
    [Tooltip("金の板（#9 トースト・#5 の札）。vui_wood_plate_gold.png（9 スライス）")]
    public Sprite goldPlate;
    [Tooltip("角丸の白（色を付けて使う）。vui_round_white.png（9 スライス）")]
    public Sprite roundWhite;

    private const string ResourcePath = "UIMotion/UIMotionAssets";
    private static UIMotionAssets cached;
    private static bool resolved;

    /// <summary>Resources から読む（無ければ空のインスタンス＝全部コードで描いた絵）。</summary>
    public static UIMotionAssets Instance
    {
        get
        {
            if (cached != null) return cached;
            if (!resolved)
            {
                resolved = true;
                cached = Resources.Load<UIMotionAssets>(ResourcePath);
            }
            if (cached == null)
            {
                cached = CreateInstance<UIMotionAssets>();
                cached.hideFlags = HideFlags.HideAndDontSave;
            }
            return cached;
        }
    }
}
