using System.Collections.Generic;
using UnityEngine;
    
public class HeroLevelDataLoader
{
    private Dictionary<int, HeroLevelData> _levelDataMap = new Dictionary<int, HeroLevelData>();

    public void LoadFromCSV(string csvFileName)
    {
        TextAsset csvFile = Resources.Load<TextAsset>(csvFileName);
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
            if (values.Length < 5) continue;

            HeroLevelData data = new HeroLevelData
            {
                Level = int.Parse(values[0]),
                ExpToNextLevel = int.Parse(values[1]),
                MaxHp = int.Parse(values[2]),
                Attack = int.Parse(values[3]),
                MaxMp = int.Parse(values[4])
            };

            _levelDataMap[data.Level] = data;
        }
    }

    public HeroLevelData GetLevelData(int level)
    {
        return _levelDataMap.TryGetValue(level, out var data) ? data : null;
    }
}