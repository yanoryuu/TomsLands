using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Cainos.LucidEditor;

namespace Cainos.PixelArtTopDown_Village
{

    [CreateAssetMenu]
    public class RandomDecorationTile : TileBase
    {
        [FoldoutGroup("Params")] public float decorationRate = 0.3f;
        [FoldoutGroup("Params")] public int seed = 0;

        [FoldoutGroup("Sprites")] public List<Sprite> basic;
        [FoldoutGroup("Sprites")] public List<Sprite> decoration;

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            Random.InitState(seed.GetHashCode() ^ position.GetHashCode());

            if (Random.value < decorationRate)
            {
                tileData.sprite = decoration[Random.Range(0, decoration.Count)];
            }
            else
            {
                tileData.sprite = basic[Random.Range(0, basic.Count)];
            }
        }
    }
}