using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Cainos.PixelArtTopDown_Village
{
    public class CameraZoom : MonoBehaviour
    {
        public Vector2 OrthoSizeRange = new Vector2(5.0f, 8.0f);
        public KeyCode keyZoomIn = KeyCode.Q;
        public KeyCode keyZoomOut = KeyCode.E;
        public float lerp = 2.0f;

        private float curSize;
        private float targetSize;

        private Camera Camera
        {
            get
            {
                if ( cam == null) cam = GetComponent<Camera>();
                return cam;
            }
        }
        private Camera cam;

        private void OnEnable()
        {
            curSize = Camera.orthographicSize;
            targetSize = curSize;
        }

        private void Update()
        {
            if (Input.GetKeyDown(keyZoomIn)) targetSize -= 0.5f;
            else
            if (Input.GetKeyDown(keyZoomOut)) targetSize += 0.5f;

            targetSize = Mathf.Clamp(targetSize, OrthoSizeRange.x, OrthoSizeRange.y);
            curSize = Mathf.Lerp(curSize, targetSize, lerp * Time.deltaTime);

            Camera.orthographicSize = curSize;
        }

    }
}
