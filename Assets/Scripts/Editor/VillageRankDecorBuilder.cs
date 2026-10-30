using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// 村の格ごとの地面と装飾のプレハブ（Assets/Prefabs/Village/VillageRankDecor.prefab）を作るエディタスクリプト。
/// 置き場所は下の Layout*（手で決めた座標。VillageScene のマップ 64×48、x -32..31 / y -24..23 に合わせてある）。
/// 石畳は VillageScene の Path（土の道）のマスをそのまま写して、格ごとの Tilemap に分ける。
/// 石畳・旗・噴水の絵は Cainos に無いのでコードで描く（Assets/Art/VillageTiles/village_rank_*.png）。
///
/// バッチモード:
///   Unity.exe -batchmode -nographics -projectPath &lt;wt&gt; -executeMethod VillageRankDecorBuilder.BuildBatch -logFile &lt;log&gt; [-decorReport &lt;txt&gt;]
/// BuildBatch = 絵の生成 → プレハブ作成 → 歩けるかの検査（スポーンから全区画と南の出口まで。結果は -decorReport へ）。
/// シーンは開くだけで保存しない（シーンへの配置は手で: Docs/Village_UnityWiring.md §3-3）。
/// </summary>
public static class VillageRankDecorBuilder
{
    private const string Tag = "[VillageRankDecorBuilder]";
    private const string ScenePath = "Assets/Scene/VillageScene.unity";
    public const string PrefabPath = "Assets/Prefabs/Village/VillageRankDecor.prefab";
    private const string ArtDir = "Assets/Art/VillageTiles";
    private const string PavingTexPath = ArtDir + "/village_rank_paving.png";
    private const string PropsTexPath = ArtDir + "/village_rank_props.png";
    private const string CainosPrefabDir = "Assets/Cainos/Pixel Art Top Down - Village/Prefab";
    private const int Ppu = 32;

    // ───────── バッチの入口 ─────────

    [MenuItem("Tools/TomsLands/村の格の装飾プレハブを作り直す")]
    private static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAll(null);
    }

    public static void BuildBatch()
    {
        try
        {
            bool ok = BuildAll(Arg("-decorReport"));
            EditorApplication.Exit(ok ? 0 : 2);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 失敗: {e}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>絵 → プレハブ → 検査。検査で塞がれた区画があれば false。</summary>
    private static bool BuildAll(string reportPath)
    {
        GenerateArt();
        BuildPrefab();
        var report = new StringBuilder();
        bool ok = Verify(report);
        string text = report.ToString();
        if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, text);
        Debug.Log($"{Tag} 検査結果\n{text}");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        return ok;
    }

    // ───────── 絵（石畳・旗・噴水） ─────────

    private static readonly RectInt FlagRedRect = new(0, 0, 24, 72);
    private static readonly RectInt FlagGoldRect = new(24, 0, 24, 72);
    private static readonly RectInt FountainRect = new(56, 0, 112, 96);

    public static void GenerateArt()
    {
        Directory.CreateDirectory(ArtDir);

        // 石畳: 128×128 の継ぎ目なしの模様を 32px ずつ 16 枚に切る（マスの位置の 4 の剰余で選ぶので、どこに敷いても継ぎ目が出ない）
        var paving = DrawPaving(128);
        WritePng(PavingTexPath, paving, 128, 128);
        var pavingRects = new List<(string, Rect)>();
        for (int iy = 0; iy < 4; iy++)
            for (int ix = 0; ix < 4; ix++)
                pavingRects.Add(($"paving_{ix}_{iy}", new Rect(ix * 32, iy * 32, 32, 32)));
        ConfigureSprites(PavingTexPath, pavingRects, TextureWrapMode.Repeat, new Vector2(0.5f, 0.5f));

        for (int iy = 0; iy < 4; iy++)
            for (int ix = 0; ix < 4; ix++)
            {
                string name = $"paving_{ix}_{iy}";
                var sprite = LoadSprite(PavingTexPath, name);
                string tilePath = $"{ArtDir}/Tile_{name}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
            }

        // 旗（赤 = 町の飾り / 金 = 施設が最大Lv）と噴水
        const int w = 168, h = 96;
        var props = new Color32[w * h];
        var canvas = new PixelCanvas(props, w, h);
        DrawFlag(canvas, FlagRedRect.x, FlagRedRect.y, gold: false);
        DrawFlag(canvas, FlagGoldRect.x, FlagGoldRect.y, gold: true);
        DrawFountain(canvas, FountainRect.x, FountainRect.y);
        WritePng(PropsTexPath, props, w, h);
        ConfigureSprites(PropsTexPath, new List<(string, Rect)>
        {
            ("flag_red", ToRect(FlagRedRect)),
            ("flag_gold", ToRect(FlagGoldRect)),
            ("fountain", ToRect(FountainRect)),
        }, TextureWrapMode.Clamp, new Vector2(0.5f, 0f));
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 絵を書き出し: {PavingTexPath} / {PropsTexPath}");
    }

    private static Rect ToRect(RectInt r) => new(r.x, r.y, r.width, r.height);

    /// <summary>周期的なボロノイで丸っこい石を並べた石畳（灰色がかった砂岩。Cainos の石壁の色に寄せる）。</summary>
    private static Color32[] DrawPaving(int size)
    {
        var rnd = new System.Random(20261030);
        const int cells = 10;
        float cell = size / (float)cells;
        var seeds = new List<Vector2>();
        var tone = new List<int>();
        for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                float ox = (float)rnd.NextDouble() * 0.7f + 0.15f;
                float oy = (float)rnd.NextDouble() * 0.7f + 0.15f;
                // 1列ごとに半分ずらして石を互い違いにする
                float sx = (x + ox + (y % 2) * 0.5f) * cell;
                seeds.Add(new Vector2(sx % size, (y + oy) * cell));
                tone.Add(rnd.Next(3));
            }

        Color32 mortar = Hex("5c5547");
        Color32 mortarLight = Hex("665f50");
        Color32[] baseTone = { Hex("8b8473"), Hex("837c6b"), Hex("928a77") };
        Color32[] lightTone = { Hex("a19a86"), Hex("989180"), Hex("a8a08b") };
        Color32[] darkTone = { Hex("6f695a"), Hex("696353"), Hex("756e5e") };
        Color32 speck = Hex("787162");

        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d1 = float.MaxValue, d2 = float.MaxValue;
                int n1 = 0;
                Vector2 v1 = Vector2.zero;
                for (int i = 0; i < seeds.Count; i++)
                {
                    float dx = Wrap(x + 0.5f - seeds[i].x, size);
                    float dy = Wrap(y + 0.5f - seeds[i].y, size);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < d1) { d2 = d1; d1 = d; n1 = i; v1 = new Vector2(dx, dy); }
                    else if (d < d2) d2 = d;
                }
                float edge = d2 - d1;
                Color32 c;
                if (edge < 1.3f) c = ((x * 7 + y * 3) % 5 == 0) ? mortarLight : mortar;
                else
                {
                    int t = tone[n1];
                    c = baseTone[t];
                    if (edge < 3.2f)
                    {
                        // 左上が明るく、右下が暗い（上から光が当たる）
                        float lit = Vector2.Dot(v1.normalized, new Vector2(-0.7f, 0.7f));
                        if (lit > 0.15f) c = lightTone[t];
                        else if (lit < -0.15f) c = darkTone[t];
                    }
                    else if (Hash(x, y) % 37 == 0) c = speck;
                }
                px[y * size + x] = c;
            }
        return px;
    }

    private static float Wrap(float d, int size)
    {
        if (d > size * 0.5f) d -= size;
        if (d < -size * 0.5f) d += size;
        return d;
    }

    private static int Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 73856093 ^ y * 19349663;
            return Math.Abs(h);
        }
    }

    private static void DrawFlag(PixelCanvas c, int ox, int oy, bool gold)
    {
        Color32 pole = Hex("6b4a2c"), poleLight = Hex("8d6640");
        Color32 stone = Hex("7f786a"), stoneLight = Hex("9a927f");
        Color32 finial = Hex("e0b840"), finialLight = Hex("f7dc7a");
        Color32 cloth = gold ? Hex("dfae35") : Hex("b23a3a");
        Color32 clothDark = gold ? Hex("b4851d") : Hex("852a2e");
        Color32 clothLight = gold ? Hex("f6d877") : Hex("d05a54");
        Color32 trim = gold ? Hex("9c2f30") : Hex("d9b04a");
        Color32 emblem = gold ? Hex("fff1b8") : Hex("e9c860");

        // 台石
        c.Fill(ox + 7, oy, 10, 3, stone);
        c.Fill(ox + 7, oy + 2, 10, 1, stoneLight);
        // 竿
        c.Fill(ox + 11, oy + 3, 2, 62, pole);
        c.Fill(ox + 11, oy + 3, 1, 62, poleLight);
        // 横木
        c.Fill(ox + 4, oy + 61, 16, 2, pole);
        c.Fill(ox + 4, oy + 62, 16, 1, poleLight);
        // 先の玉
        c.Fill(ox + 10, oy + 65, 4, 3, finial);
        c.Fill(ox + 11, oy + 68, 2, 1, finial);
        c.Set(ox + 10, oy + 66, finialLight);
        c.Set(ox + 11, oy + 67, finialLight);

        // 垂れ幕（下は燕尾）
        for (int y = 28; y <= 60; y++)
            for (int x = 5; x <= 18; x++)
            {
                float mid = 11.5f;
                if (y < 36 && Mathf.Abs(x - mid) < (36 - y) * 0.9f) continue;
                Color32 col = cloth;
                if (x <= 6) col = clothLight;
                else if (x >= 17) col = clothDark;
                if (y >= 56 && y <= 57) col = trim;
                if ((x == 5 || x == 18) && y >= 36) col = trim;
                // 風で少しなびく影
                if ((y + x / 3) % 11 == 0 && x > 6 && x < 17) col = clothDark;
                c.Set(ox + x, oy + y, col);
            }
        // 紋章（ひし形）
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 4) continue;
                bool edge = Mathf.Abs(dx) + Mathf.Abs(dy) == 4;
                c.Set(ox + 12 + dx, oy + 46 + dy, edge ? trim : emblem);
            }
        c.Outline(ox, oy, 24, 72, Hex("3b3026"));
    }

    private static void DrawFountain(PixelCanvas c, int ox, int oy)
    {
        Color32 stone = Hex("8c8473"), stoneLight = Hex("a69e8a"), stoneDark = Hex("6c6657"), mortar = Hex("5a5446");
        Color32 water = Hex("4f8db5"), waterDark = Hex("3a6b8f"), waterLight = Hex("8cc6e3"), spray = Hex("d7f0fa");
        float cx = ox + 56f;

        // 外の水盤: 縁の楕円 + 手前の壁
        float bcy = oy + 30f, rx = 52f, ry = 20f;
        const int wallH = 12;
        for (int x = 0; x < 112; x++)
        {
            float dx = ox + x + 0.5f - cx;
            if (Mathf.Abs(dx) > rx) continue;
            float k = Mathf.Sqrt(1f - dx * dx / (rx * rx));
            int yLow = Mathf.RoundToInt(bcy - ry * k);
            for (int y = yLow - wallH; y < yLow; y++)
            {
                int ly = y - (yLow - wallH);
                bool row = ly % 6 == 5;
                bool joint = (((int)(dx + 200) + (ly / 6) * 5) % 10) == 0;
                Color32 col = row || joint ? mortar : stone;
                if (ly <= 1) col = stoneDark;
                if (Mathf.Abs(dx) > rx - 4) col = stoneDark;
                c.Set(ox + x, y, col);
            }
        }
        c.Ellipse(cx, bcy, rx, ry, stone);
        c.Ellipse(cx, bcy + 1, rx - 2, ry - 1, stoneLight);
        c.Ellipse(cx, bcy, rx - 6, ry - 5, waterDark);
        c.Ellipse(cx, bcy - 1.5f, rx - 7, ry - 7, water);
        // さざ波
        var rnd = new System.Random(7);
        for (int i = 0; i < 26; i++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
            float r = 0.25f + (float)rnd.NextDouble() * 0.6f;
            int x = Mathf.RoundToInt(cx + Mathf.Cos(a) * (rx - 10) * r);
            int y = Mathf.RoundToInt(bcy - 1 + Mathf.Sin(a) * (ry - 8) * r);
            c.Fill(x, y, 3, 1, waterLight);
        }
        // 台座まわりの波紋
        for (int i = 0; i < 40; i++)
        {
            float a = i / 40f * Mathf.PI * 2f;
            if (i % 3 == 2) continue;
            c.Set(Mathf.RoundToInt(cx + Mathf.Cos(a) * 15), Mathf.RoundToInt(bcy + Mathf.Sin(a) * 6), waterLight);
        }

        // 台座（柱）
        int px0 = Mathf.RoundToInt(cx) - 5;
        c.Fill(px0, oy + 28, 10, 32, stone);
        c.Fill(px0, oy + 28, 2, 32, stoneLight);
        c.Fill(px0 + 8, oy + 28, 2, 32, stoneDark);
        c.Fill(px0 - 1, oy + 28, 12, 2, stoneDark);

        // 上の受け皿
        float ucy = oy + 62f, urx = 17f, ury = 6f;
        for (int x = -17; x <= 17; x++)
        {
            float k = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (urx * urx)));
            int yLow = Mathf.RoundToInt(ucy - ury * k);
            for (int y = yLow - 4; y < yLow; y++) c.Set(Mathf.RoundToInt(cx) + x, y, y == yLow - 4 ? stoneDark : stone);
        }
        c.Ellipse(cx, ucy, urx, ury, stoneLight);
        c.Ellipse(cx, ucy, urx - 3, ury - 2, water);
        c.Fill(Mathf.RoundToInt(cx) - 6, Mathf.RoundToInt(ucy) - 1, 4, 1, waterLight);

        // 吹き出し口と先の玉
        c.Fill(Mathf.RoundToInt(cx) - 2, oy + 63, 4, 9, stone);
        c.Fill(Mathf.RoundToInt(cx) - 2, oy + 63, 1, 9, stoneLight);
        c.Fill(Mathf.RoundToInt(cx) - 3, oy + 72, 6, 4, stoneLight);
        c.Fill(Mathf.RoundToInt(cx) - 2, oy + 76, 4, 1, stoneLight);

        // しぶき: 先から受け皿へ、受け皿の縁から水盤へ
        for (int s = -1; s <= 1; s += 2)
        {
            for (int k = 0; k <= 16; k++)
            {
                int x = Mathf.RoundToInt(cx + s * (3 + k));
                int y = Mathf.RoundToInt(oy + 80 - k * k / 16f);
                c.Set(x, y, spray);
                if (k % 2 == 0) c.Set(x, y - 1, waterLight);
            }
            for (int k = 0; k <= 12; k++)
            {
                int x = Mathf.RoundToInt(cx + s * (17 + k * 0.9f));
                int y = Mathf.RoundToInt(ucy - 2 - k * k / 7f);
                c.Set(x, y, waterLight);
                if (k % 3 == 0) c.Set(x, y + 1, spray);
            }
        }
        c.Outline(ox, oy, 112, 96, Hex("3d382f"));
    }

    private sealed class PixelCanvas
    {
        private readonly Color32[] px;
        private readonly int w, h;

        public PixelCanvas(Color32[] px, int w, int h)
        {
            this.px = px;
            this.w = w;
            this.h = h;
        }

        public void Set(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            px[y * w + x] = c;
        }

        public Color32 Get(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? default : px[y * w + x];

        public void Fill(int x, int y, int fw, int fh, Color32 c)
        {
            for (int yy = y; yy < y + fh; yy++)
                for (int xx = x; xx < x + fw; xx++) Set(xx, yy, c);
        }

        public void Ellipse(float cx, float cy, float rx, float ry, Color32 c)
        {
            for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
                for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Set(x, y, c);
                }
        }

        /// <summary>透明と接する外周に1px の縁取り（Cainos の絵の輪郭線に合わせる）。</summary>
        public void Outline(int x0, int y0, int rw, int rh, Color32 c)
        {
            var add = new List<Vector2Int>();
            for (int y = y0; y < y0 + rh; y++)
                for (int x = x0; x < x0 + rw; x++)
                {
                    if (Get(x, y).a != 0) continue;
                    bool near = Get(x - 1, y).a != 0 || Get(x + 1, y).a != 0 || Get(x, y - 1).a != 0 || Get(x, y + 1).a != 0;
                    if (near && x > x0 && y >= y0 && x < x0 + rw - 1 && y < y0 + rh - 1) add.Add(new Vector2Int(x, y));
                }
            foreach (var p in add) Set(p.x, p.y, c);
        }
    }

    private static Color32 Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var col);
        return col;
    }

    private static void WritePng(string assetPath, Color32[] px, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
    }

#pragma warning disable 618 // spritesheet は Unity 6 でも動く（Sprite Editor のデータプロバイダより短く書ける）
    private static void ConfigureSprites(string assetPath, List<(string name, Rect rect)> rects, TextureWrapMode wrap, Vector2 pivot)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Ppu;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.wrapMode = wrap;
        importer.spritesheet = rects.Select(r => new SpriteMetaData
        {
            name = r.name,
            rect = r.rect,
            alignment = (int)SpriteAlignment.Custom,
            pivot = pivot,
        }).ToArray();
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteExtrude = 0;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }
#pragma warning restore 618

    private static Sprite LoadSprite(string texPath, string name) =>
        AssetDatabase.LoadAllAssetsAtPath(texPath).OfType<Sprite>().FirstOrDefault(s => s.name == name);

    // ───────── プレハブ ─────────

    /// <summary>VillageScene を開いて（保存しない）Path のマスを読み、装飾を組んでプレハブに書き出す。</summary>
    public static void BuildPrefab()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var path = FindTilemap(scene, "Path");
        if (path == null) throw new InvalidOperationException("VillageScene に Path の Tilemap が無い");
        var pathRenderer = path.GetComponent<TilemapRenderer>();

        var root = new GameObject("VillageRankDecor");
        var decor = root.AddComponent<VillageRankDecor>();
        var layers = new List<VillageRankDecor.Layer>();

        // 石畳（Path の上に重ねる。町 = 広場と北の道、王都 = 残りの道ぜんぶ）
        var gridGo = new GameObject("Paving", typeof(Grid));
        gridGo.transform.SetParent(root.transform, false);
        gridGo.GetComponent<Grid>().cellSize = path.layoutGrid != null ? path.layoutGrid.cellSize : Vector3.one;
        var paveTown = CreatePavingTilemap(gridGo.transform, "Paving_Town", pathRenderer, -85);
        var paveCapital = CreatePavingTilemap(gridGo.transform, "Paving_Capital", pathRenderer, -84);
        var tiles = new Tile[4, 4];
        for (int iy = 0; iy < 4; iy++)
            for (int ix = 0; ix < 4; ix++)
                tiles[ix, iy] = AssetDatabase.LoadAssetAtPath<Tile>($"{ArtDir}/Tile_paving_{ix}_{iy}.asset");
        path.CompressBounds();
        foreach (var cellPos in path.cellBounds.allPositionsWithin)
        {
            if (path.GetTile(cellPos) == null) continue;
            var tile = tiles[Mod(cellPos.x, 4), Mod(cellPos.y, 4)];
            (IsTownPaving(cellPos) ? paveTown : paveCapital).SetTile(cellPos, tile);
        }
        layers.Add(new VillageRankDecor.Layer { minRank = 2, root = paveTown.gameObject });
        layers.Add(new VillageRankDecor.Layer { minRank = 3, root = paveCapital.gameObject });

        // 装飾のまとまり
        layers.Add(Group(root, "Rank0_Settlement", 0, 0, LayoutSettlementOnly));
        layers.Add(Group(root, "Rank0to1_Clearing", 0, 1, LayoutClearing));
        layers.Add(Group(root, "Rank1_Village", 1, 99, LayoutVillage));
        layers.Add(Group(root, "Rank2_Town", 2, 99, LayoutTown));
        layers.Add(Group(root, "Rank3_Capital", 3, 99, LayoutCapital));

        var maxDecor = new List<VillageRankDecor.FacilityDecor>();
        var maxRoot = new GameObject("MaxLevel");
        maxRoot.transform.SetParent(root.transform, false);
        foreach (var (id, flags) in MaxLevelFlags)
        {
            var g = new GameObject("Max_" + id);
            g.transform.SetParent(maxRoot.transform, false);
            foreach (var p in flags) Flag(g.transform, p.x, p.y, gold: true);
            g.SetActive(false);
            maxDecor.Add(new VillageRankDecor.FacilityDecor { facilityId = id, root = g });
        }

        var so = new SerializedObject(decor);
        SetLayers(so.FindProperty("layers"), layers);
        var md = so.FindProperty("maxLevelDecor");
        md.arraySize = maxDecor.Count;
        for (int i = 0; i < maxDecor.Count; i++)
        {
            var e = md.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("facilityId").stringValue = maxDecor[i].facilityId;
            e.FindPropertyRelative("root").objectReferenceValue = maxDecor[i].root;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // 置いた直後（Presenter が Apply する前）は開拓地の見た目
        foreach (var l in layers) l.root.SetActive(l.minRank <= 0 && l.maxRank >= 0);

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"{Tag} プレハブを書き出し: {PrefabPath}");
    }

    private static void SetLayers(SerializedProperty prop, List<VillageRankDecor.Layer> layers)
    {
        prop.arraySize = layers.Count;
        for (int i = 0; i < layers.Count; i++)
        {
            var e = prop.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("minRank").intValue = layers[i].minRank;
            e.FindPropertyRelative("maxRank").intValue = layers[i].maxRank;
            e.FindPropertyRelative("root").objectReferenceValue = layers[i].root;
        }
    }

    private static int Mod(int a, int m) => (a % m + m) % m;

    /// <summary>町で先に石畳になるところ: 中央広場と、領主館へ向かう北の道と、南の道の入口。</summary>
    private static bool IsTownPaving(Vector3Int c) =>
        (c.x >= -14 && c.x <= 12 && c.y >= -4 && c.y <= 0)
        || (c.x >= -2 && c.x <= 3 && c.y >= 1 && c.y <= 12)
        || (c.x >= -3 && c.x <= 3 && c.y >= -9 && c.y <= -5);

    private static Tilemap CreatePavingTilemap(Transform parent, string name, TilemapRenderer like, int order)
    {
        var go = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<TilemapRenderer>();
        r.sortingLayerName = like != null ? like.sortingLayerName : "Ground";
        r.sortingOrder = order;
        r.mode = TilemapRenderer.Mode.Chunk;
        if (like != null) r.sharedMaterial = like.sharedMaterial;
        return go.GetComponent<Tilemap>();
    }

    private static VillageRankDecor.Layer Group(GameObject root, string name, int min, int max, Action<Transform> layout)
    {
        var g = new GameObject(name);
        g.transform.SetParent(root.transform, false);
        layout(g.transform);
        return new VillageRankDecor.Layer { minRank = min, maxRank = max, root = g };
    }

    // ───────── 置き方（手で決めた座標） ─────────
    // 施設の区画（建物・吹き出し）: 領主館(0,13) / ギルド(9,4) / 銀行(-7,3) / 広場(-26,-3) / 工房(-23,10) / 酒場(15,-4) / 掲示板(-20.9,-5.8)
    // 道: 中央広場 y -4..0 / 北 x -1..2 / 南 x -1..2（y=-24 が出口）/ 西・北西・北東・南東・南西へ斜めに伸びる

    /// <summary>開拓地だけ: テントの野営地・焚き火・資材の山・荷車（村になると片づく）。</summary>
    private static void LayoutSettlementOnly(Transform p)
    {
        // 広場の南東の野営地
        Put(p, "Building - Tent 01 S", 7.2f, -8.8f);
        Put(p, "Building - Teepee 01", 10.0f, -10.0f);
        Put(p, "Props - Campfire 01", 7.6f, -11.0f);
        Put(p, "Props - Wood Log 02 H", 6.0f, -11.4f);
        Put(p, "Props - Stool 01", 9.0f, -11.6f);
        Put(p, "Props - Firewood Pile 01", 5.4f, -9.2f);
        Put(p, "Props - Fence 01 H X2", 2.6f, -12.4f);
        Put(p, "Props - Fence 01 H X2", 4.6f, -12.4f);
        Put(p, "Props - Fence 01 V X2", 2.6f, -12.4f);
        // 資材置き場（広場の西）
        Put(p, "Props - Wood Pile 01 H", -15.8f, -1.4f);
        Put(p, "Props - Wood Log 03 H", -18.2f, -2.6f);
        Put(p, "Props - Wood Log 01 H", -15.6f, -3.4f);
        Put(p, "Props - Sack 04", -17.0f, -2.8f);
        Put(p, "Props - Cart 01 E", 11.8f, -8.6f);
        Put(p, "Props - Pallet Crate 01 H", 7.4f, -4.4f);
        // 広場の真ん中の焚き火とテント（王都では噴水が立つところ）
        Put(p, "Props - Campfire Cauldron 01", -6.8f, -2.6f);
        Put(p, "Props - Wood Log 02 H", -8.6f, -3.0f);
        Put(p, "Props - Wood Log 01 H", -5.0f, -3.2f);
        Put(p, "Building - Tent 01 S", -10.0f, -1.2f);
        Put(p, "Building - Tent 01 W", 6.4f, -2.6f);
        Put(p, "Props - Crate 02 A", 8.2f, -2.2f);
        Put(p, "Props - Sack 01", 8.8f, -2.8f);
        Put(p, "Props - Sack 03", 5.0f, -3.8f);
        // 道ばたの切りかけの丸太と石
        Put(p, "Props - Wood Log 03 V", -4.4f, 1.6f);
        Put(p, "Props - Rock 05", 3.6f, -6.6f);
        Put(p, "Props - Rock 02", -10.6f, -4.6f);
        Put(p, "Props - Rock 04", 5.6f, 2.4f);
        // 何もない空き地の柵と「売地」の立て札（村になると家が建つところ）
        FenceLot(p, 20.4f, 12.6f, 4, 4);
        Put(p, "Props - Sign 01", 22.4f, 11.6f);
        FenceLot(p, 23.0f, -9.8f, 4, 4);
        Put(p, "Props - Sign 01", 25.0f, -10.8f);
        FenceLot(p, -30.0f, -15.8f, 4, 4);
        Put(p, "Props - Sign 01", -28.0f, -16.8f);
        FenceLot(p, 6.6f, -15.8f, 4, 4);
        Put(p, "Props - Sign 01", 8.6f, -16.8f);
    }

    /// <summary>開拓地と村: 切り株と、まだ切り開いている途中の丸太（町になると消える）。</summary>
    private static void LayoutClearing(Transform p)
    {
        (float x, float y)[] stumps =
        {
            (-4.2f, -7.4f), (4.6f, -10.4f), (-20.0f, 1.2f), (-17.0f, 3.6f), (17.6f, 7.4f), (20.4f, 1.6f),
            (-9.0f, -8.2f), (11.0f, -13.6f), (-14.0f, -12.6f), (25.6f, 8.6f), (4.4f, 10.0f),
            (-13.4f, -19.0f), (17.0f, -19.6f), (-5.0f, 20.4f), (11.6f, 17.2f),
        };
        for (int i = 0; i < stumps.Length; i++)
            Put(p, i % 2 == 0 ? "Props - Wood Log 01 T" : "Props - Wood Log 02 T", stumps[i].x, stumps[i].y);
        Put(p, "Props - Wood Log 02 H", -3.8f, -8.6f);
        Put(p, "Props - Wood Log 04 H", 18.6f, 6.6f);
        Put(p, "Props - Hay Pile 02", -24.0f, -8.4f);
    }

    /// <summary>村から: 民家が数軒・畑・花・木の看板・街灯少し。</summary>
    private static void LayoutVillage(Transform p)
    {
        // 民家（開拓地で柵を立てていた空き地に建つ）
        Put(p, "Building - House 06", 22.4f, 13.0f);
        Put(p, "Building - House 10", 24.4f, -9.4f);
        Put(p, "Building - House 13", -28.0f, -14.6f);
        Put(p, "Building - House 14", 8.6f, -14.6f);
        // 家まわりの小物
        Put(p, "Props - Clothesline 01", 26.0f, 13.4f);
        Put(p, "Props - Barrel 01", 20.0f, 13.2f);
        Put(p, "Props - Beehive 01", 28.8f, -8.6f);
        Put(p, "Props - Bucket 01 Water", 22.0f, -9.6f);
        Put(p, "Props - Firewood Pile 01", -24.6f, -15.0f);
        Put(p, "Props - Washtub 01", 12.4f, -15.2f);
        // 花
        foreach (var (x, y, n) in new[]
                 {
                     (20.8f, 12.2f, 1), (21.6f, 12.0f, 2), (23.4f, 12.2f, 3), (24.2f, 12.0f, 4),
                     (6.4f, -15.4f, 1), (7.2f, -15.6f, 3), (10.0f, -15.4f, 2), (10.8f, -15.6f, 4),
                     (-30.2f, -15.4f, 2), (-29.4f, -15.6f, 1), (-26.6f, -15.4f, 4),
                     (-2.6f, 6.6f, 1), (-2.4f, 9.4f, 3), (4.6f, 9.2f, 2), (-3.2f, -6.0f, 4),
                 })
            Put(p, $"Plant - Flower 02 0{n}", x, y);

        // 畑（南西: 麦 / 南東: 野菜とかかし）
        for (int i = 0; i < 6; i++)
        {
            Put(p, i % 2 == 0 ? "Plant - Wheat 01 A" : "Plant - Wheat 01 B", -26.4f + i * 1.1f, -22.6f);
            Put(p, i % 2 == 0 ? "Plant - Wheat 01 B" : "Plant - Wheat 01 A", -26.0f + i * 1.1f, -21.4f);
        }
        FenceRow(p, -27.4f, -23.6f, 4);
        string[] crops = { "Plant - Cabbage 03", "Plant - Carrot 03", "Plant - Pumpkin 03" };
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 6; i++)
                Put(p, crops[row], 15.0f + i * 0.9f, -21.2f - row * 0.8f);
        Put(p, "Props - Scarecrow 01", 21.2f, -22.4f);
        FenceRow(p, 14.2f, -23.6f, 4);

        // 木の看板（道の分かれ目）
        Put(p, "Props - Road Sign 03", -13.6f, 1.6f);
        Put(p, "Props - Road Sign 01", 4.0f, 10.8f);
        Put(p, "Props - Road Sign 02", -3.0f, -11.2f);
        Put(p, "Props - Road Sign 01", 17.4f, 10.6f);
        // 広場の掲示板・樽・鉢植え
        Put(p, "Props - Billboard 01", 5.8f, -2.0f);
        Put(p, "Props - Barrel 03", 4.6f, -1.0f);
        Put(p, "Props - Flower Pot 01 A", -5.0f, -0.6f);
        Put(p, "Props - Flower Pot 01 B", 1.8f, -4.4f);
        // 街灯少し
        Put(p, "Props - Road Lamp 01 R", 4.8f, 1.2f);
        Put(p, "Props - Road Lamp 01 L", -14.2f, -4.6f);
    }

    /// <summary>町から: 民家が増え、露店・樽や木箱・街灯・花壇・旗で広場がにぎやかになる。</summary>
    private static void LayoutTown(Transform p)
    {
        Put(p, "Building - House 06", 5.8f, 13.8f);
        Put(p, "Building - House 14", -9.4f, -15.0f);
        Put(p, "Building - House 03", 26.4f, -1.6f);
        Put(p, "Building - Warehouse 01", -18.0f, 18.4f);

        // 露店（広場の南・南の道沿い）
        Put(p, "Building - Stall 01", 7.0f, -7.0f);
        Put(p, "Building - Stall 03", 4.6f, -11.0f);
        Put(p, "Building - Stall 05", -3.8f, -12.8f);
        Put(p, "Building - Stall 02", -17.4f, -5.8f);
        Put(p, "Props - Barrel 02", 9.0f, -6.8f);
        Put(p, "Props - Crate 01 B", 9.2f, -7.8f);
        Put(p, "Props - Crate 03 A H", 3.4f, -11.8f);
        Put(p, "Props - Sack 02", 6.6f, -11.6f);
        Put(p, "Props - Barrel 01", -2.0f, -13.4f);
        Put(p, "Props - Crate 02 B", -15.4f, -5.4f);
        Put(p, "Props - Barrel 04", -19.2f, -6.6f);
        Put(p, "Props - Barrel 01", 3.6f, 1.6f);

        // 花壇（北の道の両側・広場のふち）
        Put(p, "Props - Planter Box 01 A V", -2.2f, 3.0f);
        Put(p, "Props - Planter Box 01 B V", 3.6f, 3.6f);
        Put(p, "Props - Planter Box 01 C V", -1.4f, 7.4f);
        Put(p, "Props - Planter Box 01 A V", 3.8f, 7.6f);
        Put(p, "Props - Planter Box 01 A H", 10.6f, -6.6f);

        // 街灯（北の道・広場・南の道）
        Put(p, "Props - Road Lamp 01 L", -2.4f, 4.4f);
        Put(p, "Props - Road Lamp 01 R", 3.4f, -6.4f);
        Put(p, "Props - Road Lamp 01 L", -4.0f, -8.4f);

        // 旗
        Flag(p, -14.6f, 0.2f, false);
        Flag(p, 8.2f, -0.8f, false);
        Flag(p, -12.6f, -0.6f, false);
        Put(p, "Props - Road Lamp 01 L", -11.4f, -0.4f);
        Flag(p, 11.4f, -8.4f, false);
        Flag(p, 3.4f, 9.0f, false);
        Flag(p, -14.0f, -5.6f, false);
    }

    /// <summary>王都: 道がぜんぶ石畳（Paving_Capital）、南の城壁と門、広場の噴水、旗と街灯がさらに増える。</summary>
    private static void LayoutCapital(Transform p)
    {
        // 南の城壁（y=-18.6。門は南の道 x -1..2 の真ん中）
        const float wy = -18.6f;
        Put(p, "Building - Wall 01 H Gate", 0.5f, wy);
        for (int i = 0; i < 4; i++)
        {
            Put(p, $"Building - Wall 01 H 0{i % 3 + 1}", -2.0f - i * 2f, wy);
            Put(p, $"Building - Wall 01 H 0{(i + 1) % 3 + 1}", 3.0f + i * 2f, wy);
        }
        Put(p, "Building - Wall 01 Coner BL", -9.0f, wy);
        Put(p, "Building - Wall 01 Coner BR", 10.0f, wy);
        Flag(p, -1.6f, wy - 0.6f, false);
        Flag(p, 2.6f, wy - 0.6f, false);
        Put(p, "Props - Statue 01", -3.0f, -16.4f);
        Put(p, "Props - Statue 01", 4.0f, -16.4f);

        // 広場の噴水（銀行の前）
        Fountain(p, -7.6f, -3.0f);
        Put(p, "Props - Bench 01 H", -11.2f, -4.2f);

        // 領主館の前の礼拝堂と物見の塔
        Put(p, "Building - Chapel 01", 17.0f, 15.0f);
        Put(p, "Building - Tower 01", -28.0f, 17.6f);

        // 旗と街灯をさらに
        Flag(p, -5.4f, -5.2f, false);
        Flag(p, -9.8f, -5.2f, false);
        Flag(p, 5.0f, 6.8f, false);
        Flag(p, -23.4f, -6.8f, false);
        Flag(p, 16.6f, 8.6f, false);
        Flag(p, -13.4f, -12.0f, false);
        Flag(p, 14.4f, -11.0f, false);
        Put(p, "Props - Road Lamp 01 L", -2.4f, -14.6f);
        Put(p, "Props - Road Lamp 01 R", 3.4f, -14.6f);
        Put(p, "Props - Road Lamp 01 R", 19.0f, 7.6f);
        Put(p, "Props - Road Lamp 01 L", -18.4f, -12.6f);
        Put(p, "Props - Road Lamp 01 R", 17.2f, -10.2f);
    }

    /// <summary>施設が最大Lv（Lv5）の区画に立てる金の旗（建物の両わき。吹き出しは区画の 1.7 下）。</summary>
    private static readonly (string id, Vector2[] flags)[] MaxLevelFlags =
    {
        (VillageEffects.HallId, new[] { new Vector2(-3.6f, 12.2f), new Vector2(3.6f, 12.2f) }),
        (VillageEffects.GuildId, new[] { new Vector2(5.6f, 0.6f), new Vector2(17.2f, 4.4f) }),
        (VillageEffects.BankId, new[] { new Vector2(-11.0f, 2.2f), new Vector2(-2.6f, 1.0f) }),
        (VillageEffects.PlazaId, new[] { new Vector2(-29.8f, -2.6f), new Vector2(-22.2f, -2.6f) }),
        (VillageEffects.ArtisanId, new[] { new Vector2(-27.6f, 9.6f), new Vector2(-18.4f, 9.6f) }),
        (VillageEffects.TavernId, new[] { new Vector2(11.0f, -3.4f), new Vector2(20.4f, -4.6f) }),
    };

    // ───────── 置く道具 ─────────

    private static GameObject Put(Transform parent, string name, float x, float y)
    {
        string folder = name.StartsWith("Building") ? "Building" : name.StartsWith("Plant") ? "Plant" : "Props";
        string path = $"{CainosPrefabDir}/{folder}/PF Village {name}.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"{Tag} プレハブが無い: {path}");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = new Vector3(x, y, 0f);
        ApplySorting(go);
        return go;
    }

    /// <summary>シーンの Cainos の物と同じ描画順（本体 = Objects、影 = Default のまま。Y で前後）。</summary>
    private static void ApplySorting(GameObject go)
    {
        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr.name.Contains("Shadow")) continue;
            sr.sortingLayerName = "Objects";
        }
        foreach (var sg in go.GetComponentsInChildren<SortingGroup>(true)) sg.sortingLayerName = "Objects";
    }

    /// <summary>空き地を囲う木の柵（w×h マス。下の辺の真ん中は入口として空ける）。</summary>
    private static void FenceLot(Transform parent, float x0, float y0, int w, int h)
    {
        var lot = new GameObject("FenceLot");
        lot.transform.SetParent(parent, false);
        lot.transform.position = new Vector3(x0, y0, 0f);
        for (int i = 0; i < w; i += 2)
        {
            if (i + 2 < w || w <= 2) Put(lot.transform, "Props - Fence 01 H X2", x0 + i, y0); // 右下の 2 マスは入口
            Put(lot.transform, "Props - Fence 01 H X2", x0 + i, y0 + h);
        }
        for (int j = 0; j < h; j += 2)
        {
            Put(lot.transform, "Props - Fence 01 V X2", x0, y0 + j);
            Put(lot.transform, "Props - Fence 01 V X2", x0 + w, y0 + j);
        }
    }

    private static void FenceRow(Transform parent, float x0, float y, int pieces)
    {
        for (int i = 0; i < pieces; i++) Put(parent, "Props - Fence 02 H", x0 + i * 2.4f, y);
    }

    private static void Flag(Transform parent, float x, float y, bool gold)
    {
        var go = new GameObject(gold ? "Flag_Gold" : "Flag_Red");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, y, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(PropsTexPath, gold ? "flag_gold" : "flag_red");
        sr.sortingLayerName = "Objects";
        sr.spriteSortPoint = SpriteSortPoint.Pivot;
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.3f, 0.12f);
        col.offset = new Vector2(0f, 0.06f);
    }

    private static void Fountain(Transform parent, float x, float y)
    {
        var go = new GameObject("Fountain");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, y, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(PropsTexPath, "fountain");
        sr.sortingLayerName = "Objects";
        sr.spriteSortPoint = SpriteSortPoint.Pivot;
        // 水盤の手前半分だけ当たる（奥は絵の上に重なって歩けてもよい）
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(3.0f, 1.3f);
        col.offset = new Vector2(0f, 0.65f);
    }

    // ───────── 検査: 歩けるか・重なっていないか ─────────

    private static readonly Vector2 PlayerBox = new(0.55f, 0.6f);
    private const float Step = 0.25f;

    /// <summary>
    /// 各格 × 施設の段階（空き地 / 最大）で、スポーンから全区画と南の出口まで歩けるかを幅優先探索で調べる。
    /// あわせて、装飾が区画の建物・吹き出しや道に重なっていないかも書き出す（重なりは警告のみ）。
    /// </summary>
    public static bool Verify(StringBuilder sb)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        var decor = inst.GetComponent<VillageRankDecor>();
        var plots = Object.FindObjectsByType<FacilityPlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var player = Object.FindFirstObjectByType<PlayerMove>();
        var path = FindTilemap(scene, "Path");
        Vector2 start = player != null ? (Vector2)player.transform.position + new Vector2(0f, 0.3f) : new Vector2(-1.2f, -2.4f);

        // くらべる元: 装飾なし
        inst.SetActive(false);
        Physics2D.SyncTransforms();
        CheckReach(sb, "装飾なし（元のシーン）", start, plots, path, player);
        inst.SetActive(true);

        bool ok = true;
        foreach (int stage in new[] { 0, 3 })
        {
            foreach (var plot in plots)
            {
                if (plot.FacilityId == VillagePresenter.RecordBoardId) continue;
                plot.SetState(stage, false, null, plot.FacilityId);
            }
            for (int rank = 0; rank <= 3; rank++)
            {
                decor.Apply(rank);
                foreach (var plot in plots)
                {
                    if (plot.FacilityId == VillagePresenter.RecordBoardId) plot.gameObject.SetActive(rank >= 3);
                    else decor.ApplyFacilityLevel(plot.FacilityId, stage >= 3 ? 5 : 0, 5);
                }
                Physics2D.SyncTransforms();
                ok &= CheckReach(sb, $"格{rank}・区画Lv{(stage >= 3 ? "最大" : "0")}", start, plots, path, player);
            }
        }

        // 重なり（全部出した状態で）
        foreach (var plot in plots) plot.gameObject.SetActive(true);
        ReportOverlaps(sb, decor, plots, path, scene);
        Object.DestroyImmediate(inst);
        return ok;
    }

    private static bool CheckReach(StringBuilder sb, string label, Vector2 start, FacilityPlot[] plots, Tilemap path, PlayerMove player)
    {
        const float xMin = -31.8f, xMax = 31.8f, yMin = -23.8f, yMax = 23.8f;
        int nx = Mathf.CeilToInt((xMax - xMin) / Step), ny = Mathf.CeilToInt((yMax - yMin) / Step);
        var state = new sbyte[nx, ny]; // 0 未調査 / 1 歩ける / -1 塞がり
        var reached = new bool[nx, ny];
        var playerCols = player != null ? player.GetComponentsInChildren<Collider2D>(true) : Array.Empty<Collider2D>();
        var hits = new Collider2D[16];
        var filter = new ContactFilter2D { useTriggers = false };
        filter.NoFilter();
        filter.useTriggers = false;

        bool Free(int ix, int iy)
        {
            if (state[ix, iy] != 0) return state[ix, iy] > 0;
            var c = new Vector2(xMin + ix * Step, yMin + iy * Step);
            int n = Physics2D.OverlapBox(c, PlayerBox, 0f, filter, hits);
            bool free = true;
            for (int i = 0; i < n; i++)
            {
                if (hits[i] == null || hits[i].isTrigger || Array.IndexOf(playerCols, hits[i]) >= 0) continue;
                free = false;
                break;
            }
            state[ix, iy] = (sbyte)(free ? 1 : -1);
            return free;
        }

        int sx = Mathf.RoundToInt((start.x - xMin) / Step), sy = Mathf.RoundToInt((start.y - yMin) / Step);
        var q = new Queue<Vector2Int>();
        if (Free(sx, sy)) { q.Enqueue(new Vector2Int(sx, sy)); reached[sx, sy] = true; }
        else sb.AppendLine($"[{label}] スポーンが塞がれている");
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
            {
                var n = c + d;
                if (n.x < 0 || n.y < 0 || n.x >= nx || n.y >= ny || reached[n.x, n.y]) continue;
                if (!Free(n.x, n.y)) continue;
                reached[n.x, n.y] = true;
                q.Enqueue(n);
            }
        }

        bool ok = true;
        var lines = new List<string>();
        foreach (var plot in plots)
        {
            if (!plot.gameObject.activeInHierarchy) continue;
            var trigger = plot.GetComponent<BoxCollider2D>();
            var b = trigger.bounds;
            bool hit = false;
            for (int ix = 0; ix < nx && !hit; ix++)
                for (int iy = 0; iy < ny && !hit; iy++)
                {
                    if (!reached[ix, iy]) continue;
                    var p = new Vector2(xMin + ix * Step, yMin + iy * Step);
                    // 吹き出しが出る = プレイヤーの当たりが区画の Trigger に入る
                    if (p.x + PlayerBox.x / 2 > b.min.x && p.x - PlayerBox.x / 2 < b.max.x &&
                        p.y + PlayerBox.y / 2 > b.min.y && p.y - PlayerBox.y / 2 < b.max.y) hit = true;
                }
            if (!hit) { ok = false; lines.Add($"{plot.name}に届かない"); }
        }
        bool exit = false;
        int exitRows = Mathf.CeilToInt(0.8f / Step); // 下の境界の壁の手前 0.8 まで出られれば出口
        for (int ix = 0; ix < nx && !exit; ix++)
            for (int iy = 0; iy < exitRows && !exit; iy++) exit = reached[ix, iy];
        if (!exit) { ok = false; lines.Add("南の端に出られない"); }

        // 道のマスで歩けないもの（道の真ん中を塞いでいないか）
        int pathCells = 0, blocked = 0;
        var blockedList = new List<string>();
        if (path != null)
        {
            foreach (var cell in path.cellBounds.allPositionsWithin)
            {
                if (path.GetTile(cell) == null) continue;
                pathCells++;
                var w = path.GetCellCenterWorld(cell);
                int ix = Mathf.Clamp(Mathf.RoundToInt((w.x - xMin) / Step), 0, nx - 1);
                int iy = Mathf.Clamp(Mathf.RoundToInt((w.y - yMin) / Step), 0, ny - 1);
                bool any = false;
                for (int dx = -2; dx <= 2 && !any; dx++)
                    for (int dy = -2; dy <= 2 && !any; dy++)
                    {
                        int ax = ix + dx, ay = iy + dy;
                        if (ax >= 0 && ay >= 0 && ax < nx && ay < ny && reached[ax, ay]) any = true;
                    }
                if (!any) { blocked++; if (blockedList.Count < 12) blockedList.Add($"({cell.x},{cell.y})"); }
            }
        }
        sb.AppendLine($"[{label}] {(ok ? "OK" : "NG")} 区画・出口: {(lines.Count == 0 ? "全部届く" : string.Join(" / ", lines))}　道 {pathCells} マス中 届かない {blocked}{(blocked > 0 ? " " + string.Join(",", blockedList) : "")}");
        return ok;
    }

    private static void ReportOverlaps(StringBuilder sb, VillageRankDecor decor, FacilityPlot[] plots, Tilemap path, UnityEngine.SceneManagement.Scene scene)
    {
        // 区画の建物（全段階）と吹き出しの範囲
        var reserved = new List<(string, Rect)>();
        foreach (var plot in plots)
        {
            var stages = new SerializedObject(plot).FindProperty("stageObjects");
            for (int i = 0; i < stages.arraySize; i++)
            {
                var go = stages.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go == null) continue;
                bool was = go.activeSelf;
                go.SetActive(true);
                foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (r.name.Contains("Shadow") || r.sprite == null) continue;
                    reserved.Add(($"{plot.name}/Stage{i}/{r.name}", ToRect(r.bounds)));
                }
                go.SetActive(was);
            }
            var pos = plot.transform.position;
            reserved.Add(($"{plot.name}の吹き出し", new Rect(pos.x - 1.6f, pos.y - 2.4f, 3.2f, 1.3f)));
        }
        // シーンに元からある物の当たり
        var sceneCols = new List<(string, Rect)>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<VillageRankDecor>() != null || root.name == "Walls" || root.name == "Plots") continue;
            foreach (var c in root.GetComponentsInChildren<Collider2D>(false))
                if (!c.isTrigger) sceneCols.Add(($"{root.name}/{c.name}", ToRect(c.bounds)));
        }

        decor.Apply(3);
        var maxField = typeof(VillageRankDecor).GetField("maxLevelDecor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var layersField = typeof(VillageRankDecor).GetField("layers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var groups = new List<Transform>();
        foreach (var l in (VillageRankDecor.Layer[])layersField.GetValue(decor))
            if (l.root != null && l.root.GetComponent<Tilemap>() == null) groups.Add(l.root.transform);
        foreach (var d in (VillageRankDecor.FacilityDecor[])maxField.GetValue(decor))
            if (d.root != null) groups.Add(d.root.transform);
        foreach (var g in groups) g.gameObject.SetActive(true);
        Physics2D.SyncTransforms();

        int warn = 0;
        sb.AppendLine("── 重なり（警告） ──");
        foreach (var g in groups)
        {
            foreach (Transform item in g)
            {
                foreach (Transform t in item.name == "FenceLot" ? item.Cast<Transform>().ToArray() : new[] { item })
                {
                    var spr = SpriteRect(t);
                    var cols = t.GetComponentsInChildren<Collider2D>(true).Where(c => !c.isTrigger).Select(c => ToRect(c.bounds)).ToList();
                    string who = $"{g.name}/{t.name}@({t.position.x:F1},{t.position.y:F1})";
                    foreach (var (name, r) in reserved)
                    {
                        if (spr.HasValue && Area(spr.Value, r) > 0.25f) { sb.AppendLine($"  {who} の絵が {name} に重なる"); warn++; break; }
                    }
                    foreach (var c in cols)
                    {
                        foreach (var (name, r) in sceneCols)
                            if (Area(c, r) > 0.01f) { sb.AppendLine($"  {who} の当たりが {name} に重なる"); warn++; }
                        if (path != null && !g.name.StartsWith("Rank3") && CoversPath(path, c)) { sb.AppendLine($"  {who} の当たりが道にかかる"); warn++; }
                    }
                }
            }
        }
        sb.AppendLine($"警告 {warn} 件");
    }

    private static bool CoversPath(Tilemap path, Rect r)
    {
        for (int x = Mathf.FloorToInt(r.xMin); x <= Mathf.FloorToInt(r.xMax - 0.01f); x++)
            for (int y = Mathf.FloorToInt(r.yMin); y <= Mathf.FloorToInt(r.yMax - 0.01f); y++)
            {
                if (path.GetTile(new Vector3Int(x, y, 0)) == null) continue;
                var cell = new Rect(x, y, 1, 1);
                if (Area(cell, r) > 0.15f) return true;
            }
        return false;
    }

    private static Rect? SpriteRect(Transform t)
    {
        Rect? acc = null;
        foreach (var r in t.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r.name.Contains("Shadow") || r.sprite == null) continue;
            var b = ToRect(r.bounds);
            acc = acc.HasValue ? Rect.MinMaxRect(Mathf.Min(acc.Value.xMin, b.xMin), Mathf.Min(acc.Value.yMin, b.yMin),
                Mathf.Max(acc.Value.xMax, b.xMax), Mathf.Max(acc.Value.yMax, b.yMax)) : b;
        }
        return acc;
    }

    private static Rect ToRect(Bounds b) => Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);

    private static float Area(Rect a, Rect b)
    {
        float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return w > 0 && h > 0 ? w * h : 0f;
    }

    private static Tilemap FindTilemap(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var tm in root.GetComponentsInChildren<Tilemap>(true))
                if (tm.name == name) return tm;
        return null;
    }

    // ───────── 草地の継ぎ目の線 ─────────

    private const string GrassTexPath = "Assets/Cainos/Pixel Art Top Down - Village/Texture/TX Tileset Grass.png";

    /// <summary>
    /// 草地のタイルの継ぎ目に出る細い線を消す。
    /// 原因: 草のタイルは TX Tileset Grass.png の 128×128 の塊から 32px ずつ切ってあり、塊の外は透明。
    /// カメラの拡大率が整数倍でないと、タイルの端で隣の 1px（透明）を拾い、その列・行だけ背景が透けて線になる。
    /// 直し方: 中身のあるスプライトの枠の外（空の枠）の透明な画素を、隣の色で 2px ぶん塗り広げる（使っている絵は変えない）。
    /// </summary>
    public static void FixGrassSeams()
    {
        int filled = BleedOutsideSprites(GrassTexPath, 2);
        Debug.Log($"{Tag} 草地の継ぎ目: {filled} 画素を塗り広げた（{GrassTexPath}）");
    }

    public static void FixGrassSeamsBatch()
    {
        try
        {
            FixGrassSeams();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 失敗: {e}");
            EditorApplication.Exit(1);
        }
    }

#pragma warning disable 618
    private static int BleedOutsideSprites(string assetPath, int passes)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(assetPath));
        int w = tex.width, h = tex.height;
        var px = tex.GetPixels32();
        // 草の画像は 32px の格子で全面がスプライトに切ってあるので、中身のある（不透明な画素を含む）枠だけを守る
        var rects = importer.spritesheet.Select(s => s.rect).Where(r =>
        {
            for (int y = (int)r.yMin; y < (int)r.yMax; y++)
                for (int x = (int)r.xMin; x < (int)r.xMax; x++)
                    if (x >= 0 && y >= 0 && x < w && y < h && px[y * w + x].a != 0) return true;
            return false;
        }).ToArray();

        bool InSprite(int x, int y)
        {
            foreach (var r in rects)
                if (x >= r.xMin && x < r.xMax && y >= r.yMin && y < r.yMax) return true;
            return false;
        }

        int total = 0;
        for (int pass = 0; pass < passes; pass++)
        {
            var next = (Color32[])px.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * w + x].a != 0 || InSprite(x, y)) continue;
                    // 上下左右の不透明な画素の色（最初に見つかったもの）
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int ax = x + dx, ay = y + dy;
                        if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
                        var c = px[ay * w + ax];
                        if (c.a < 255) continue;
                        next[y * w + x] = c;
                        total++;
                        break;
                    }
                }
            px = next;
        }
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        return total;
    }
#pragma warning restore 618

    // ───────── 調査用 ─────────

    /// <summary>村シーンの構成（タイルマップ・物の位置・コライダー）をテキストに書き出す（シーンは保存しない）。</summary>
    public static void DumpScene()
    {
        string outPath = Arg("-dumpOut") ?? "village_dump.txt";
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        sb.AppendLine("SortingLayers: " + string.Join(",", SortingLayer.layers.Select(l => l.name + "(" + l.value + ")")));
        foreach (var grid in Object.FindObjectsByType<Grid>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            sb.AppendLine($"Grid {grid.name} pos={grid.transform.position} cell={grid.cellSize}");
            foreach (var tm in grid.GetComponentsInChildren<Tilemap>(true))
            {
                var r = tm.GetComponent<TilemapRenderer>();
                tm.CompressBounds();
                sb.AppendLine($"  Tilemap {tm.name} bounds={tm.cellBounds} layer={r?.sortingLayerName}/{r?.sortingOrder} mode={r?.mode}");
            }
        }
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<Canvas>() != null) continue;
            DumpObj(sb, root.transform, 0, 3);
        }
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log($"{Tag} dump → {outPath}");
    }

    private static void DumpObj(StringBuilder sb, Transform t, int depth, int maxDepth)
    {
        var go = t.gameObject;
        string prefab = PrefabUtility.IsAnyPrefabInstanceRoot(go)
            ? Path.GetFileNameWithoutExtension(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go))
            : "";
        string cols = string.Join(" ", go.GetComponents<Collider2D>().Select(c =>
            $"{c.GetType().Name}{(c.isTrigger ? "(T)" : "")}[{c.bounds.min.x:F1},{c.bounds.min.y:F1}..{c.bounds.max.x:F1},{c.bounds.max.y:F1}]"));
        sb.AppendLine($"{new string(' ', depth * 2)}- {go.name}{(go.activeSelf ? "" : " (off)")} p=({t.position.x:F2},{t.position.y:F2}){(prefab.Length > 0 ? " PF=" + prefab : "")} {cols}");
        if (depth >= maxDepth || (prefab.Length > 0 && depth > 0)) return;
        foreach (Transform c in t) DumpObj(sb, c, depth + 1, maxDepth);
    }

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }
}
