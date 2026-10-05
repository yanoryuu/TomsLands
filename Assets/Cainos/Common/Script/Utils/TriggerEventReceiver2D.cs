using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Cainos.Common
{
    public class TriggerEventReceiver2D : MonoBehaviour
    {
        public bool useLayerMask = true;
        public LayerMask layerMask;
        [Space]
        public bool useTag = true;
        public new string tag;
        [Space]
        public TriggerEvent2D onTriggerEnter2D;
        public TriggerEvent2D onTriggerExit2D;


        private void OnTriggerEnter2D(Collider2D collision)
        {
 
            if (useLayerMask && layerMask.Contains(collision.gameObject.layer))
            {
                onTriggerEnter2D.Invoke(collision);
            }

            if ( useTag && collision.gameObject.CompareTag(tag))
            {
                onTriggerEnter2D.Invoke(collision);
            }
        }

        private void OnTriggerExit2D(Collider2D collision)
        {
            if (useLayerMask && layerMask.Contains(collision.gameObject.layer))
            {
                onTriggerExit2D.Invoke(collision);
            }

            if (useTag && collision.gameObject.CompareTag(tag))
            {
                onTriggerExit2D.Invoke(collision);
            }
        }

        [System.Serializable]
        public class TriggerEvent2D : UnityEvent<Collider2D>
        {
        }
    }
}
