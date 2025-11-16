
[System.Serializable]
public class InfoMessage
{
    public string message;
    public InfoType infoType;
    public float confidence; // 0-1の確信度
    public string targetItemId; // 装備予測の場合のアイテムID
    public string targetDungeonId; // ダンジョン予測の場合のダンジョンID

    public InfoMessage(string message, InfoType type, float confidence)
    {
        this.message = message;
        this.infoType = type;
        this.confidence = confidence;
    }
}

public enum InfoType
{
    Equipment,  // 装備情報
    Dungeon,    // ダンジョン情報
    General     // 一般情報
}