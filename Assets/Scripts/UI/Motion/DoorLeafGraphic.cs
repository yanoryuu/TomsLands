using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// #17 両開きの扉の片側（<see cref="DoorTransition"/> が実行時に作る）。uGUI は 2D なので、
/// 蝶番（画面の端）を軸に Y 回転したときの見え方を、縦に細かく割った板の頂点へ透視投影で書き込む（遠い側ほど小さく暗い）。
/// RectTransform は画面全体に広げて使い、左の扉は画面の左半分、右の扉は右半分に描く。入力は受けない。
/// </summary>
[AddComponentMenu("")]
public sealed class DoorLeafGraphic : MaskableGraphic
{
    private const int Columns = 12;
    /// <summary>視点までの距離（画面の半幅に対する比）。小さいほど遠近が強い。</summary>
    private const float EyeDistance = 2.6f;
    /// <summary>中央の継ぎ目を隠すために内側へはみ出す幅（px）。</summary>
    private const float Overlap = 3f;

    private Texture texture;
    private bool leftSide = true;
    private float angle;

    /// <summary>扉の板の絵（u=0 が蝶番側、u=1 が取っ手側）。</summary>
    public Texture Texture
    {
        get => texture;
        set { texture = value; SetMaterialDirty(); }
    }

    /// <summary>true なら左の扉（蝶番は画面の左端）。</summary>
    public bool LeftSide
    {
        get => leftSide;
        set { leftSide = value; SetVerticesDirty(); }
    }

    /// <summary>開いた角度（度）。0 で閉じている、正で奥へ開く。90 を越えると蝶番の外側へ回り込む。</summary>
    public float Angle
    {
        get => angle;
        set
        {
            if (Mathf.Approximately(angle, value)) return;
            angle = value;
            SetVerticesDirty();
        }
    }

    public override Texture mainTexture => texture != null ? texture : s_WhiteTexture;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Vector2 c = r.center;
        float halfW = r.width * 0.5f;
        float halfH = r.height * 0.5f;
        if (halfW <= 0f || halfH <= 0f) return;

        float rad = angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        float eye = halfW * EyeDistance;
        float width = halfW + Overlap;
        float side = leftSide ? 1f : -1f;
        float hinge = -side * halfW;
        // 90 度を越えたら裏側が見えるので少し暗く
        float backShade = cos < 0f ? 0.7f : 1f;

        for (int i = 0; i <= Columns; i++)
        {
            float u = (float)i / Columns;
            float d = width * u;
            float x = hinge + side * d * cos;
            float z = d * sin;
            float f = eye / Mathf.Max(1f, eye + z);
            // 奥へ回るほど暗く（遠い側ほど）
            float shade = backShade * (1f - 0.45f * Mathf.Abs(sin) * u);
            var col = color;
            col.r *= shade;
            col.g *= shade;
            col.b *= shade;
            vh.AddVert(new Vector3(c.x + x * f, c.y - halfH * f), col, new Vector4(u, 0f));
            vh.AddVert(new Vector3(c.x + x * f, c.y + halfH * f), col, new Vector4(u, 1f));
        }
        for (int i = 0; i < Columns; i++)
        {
            int k = i * 2;
            vh.AddTriangle(k, k + 1, k + 3);
            vh.AddTriangle(k, k + 3, k + 2);
        }
    }
}
