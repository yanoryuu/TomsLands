using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// アイコンの輪郭に沿った太い縁取り（uGUI の Outline を多方向に増やしたもの）。
/// 標準の Outline は斜め4方向だけで、しかもスプライトに使うと「テクスチャ × 頂点カラー」になって縁が白くならない。
/// そこで円周上に directions 個ずらした複製に TEXCOORD1.x = 1 の印を付け、
/// UI/IconOutline シェーダー（Assets/Shaders/UIIconOutline.shader）で単色に塗る。
/// マテリアルが未設定でも例外は出ない（その場合は縁がアイコンと同じ絵になるだけ）。
/// </summary>
[RequireComponent(typeof(Graphic))]
public class MapIconOutline : BaseMeshEffect
{
    [SerializeField] private Color effectColor = Color.white;
    [SerializeField, Range(1f, 12f)] private float distance = 5f;
    [SerializeField, Range(4, 16)] private int directions = 12;

    public Color EffectColor
    {
        get => effectColor;
        set
        {
            effectColor = value;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }

    private static readonly List<UIVertex> Verts = new();
    private static readonly List<UIVertex> Result = new();

    protected override void OnEnable()
    {
        base.OnEnable();
        // 縁の印（TEXCOORD1）をシェーダーへ渡すため、Canvas の追加チャンネルを有効にする
        var canvas = graphic != null ? graphic.canvas : null;
        if (canvas != null) canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;

        Verts.Clear();
        vh.GetUIVertexStream(Verts);
        int original = Verts.Count;

        Result.Clear();
        for (int d = 0; d < directions; d++)
        {
            float rad = Mathf.PI * 2f * d / directions;
            var offset = new Vector3(Mathf.Cos(rad) * distance, Mathf.Sin(rad) * distance, 0f);
            for (int i = 0; i < original; i++)
            {
                var v = Verts[i];
                v.position += offset;
                var c = effectColor;
                c.a *= v.color.a / 255f;
                v.color = c;
                v.uv1 = new Vector4(1f, 0f, 0f, 0f);
                Result.Add(v);
            }
        }
        // 本体は最後に描く（縁の上に乗る）
        for (int i = 0; i < original; i++)
        {
            var v = Verts[i];
            v.uv1 = Vector4.zero;
            Result.Add(v);
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(Result);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (graphic != null) graphic.SetVerticesDirty();
    }
#endif
}
