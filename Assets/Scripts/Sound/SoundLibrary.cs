using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(fileName = "SoundLibrary", menuName = "Sound/SoundLibrary")]
public class SoundLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string key;
        public AssetReferenceT<AudioClip> clipRef;
    }

    [Header("BGM")]
    public List<Entry> bgmList = new();
    [Header("SE")]
    public List<Entry> seList = new();

    public Entry FindBGM(string key) => bgmList.Find(e => e.key.Trim() == key.Trim());
    public Entry FindSE(string key)  => seList.Find(e => e.key.Trim() == key.Trim());
}
