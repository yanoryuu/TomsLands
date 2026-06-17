using System.Collections.Generic;
using System.Linq;
using UnityEngine;
    
public class HeroLevelDataLoader
{
    private Dictionary<int, HeroLevelData> _levelDataMap = new Dictionary<int, HeroLevelData>();

    public void LoadFromCSV(string csvFileName)
    {
        TextAsset csvFile = AddressableLoader.Load<TextAsset>(csvFileName);
        if (csvFile == null)
        {
            Debug.LogError($"CSV file not found: {csvFileName}");
            return;
        }

        string[] lines = csvFile.text.Split('\n');
        
        // ヘッダー行をスキップ (i = 1から開始)
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] values = line.Split(',');
            if (values.Length < 4) continue;

            HeroLevelData data = new HeroLevelData
            {
                Level = int.Parse(values[0]),
                MaxHp = int.Parse(values[1]),
                Attack = int.Parse(values[2]),
                Defense = int.Parse(values[3])
            };

            _levelDataMap[data.Level] = data;
        }
    }

    public HeroLevelData GetLevelData(int level)
    {
        return _levelDataMap.TryGetValue(level, out var data) ? data : null;
    }

    public int GetMaxLevel()
    {
        return _levelDataMap.Count > 0 ? _levelDataMap.Keys.Max() : 1;
    }
}
