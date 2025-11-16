
using UnityEngine;
using TMPro;


public class InfoMessageSlot : MonoBehaviour
{
   
    [SerializeField] private TextMeshProUGUI messageText;

   
    public void SetMessage(InfoMessage message)
    {
       
        messageText.text = message.message;
    }
}