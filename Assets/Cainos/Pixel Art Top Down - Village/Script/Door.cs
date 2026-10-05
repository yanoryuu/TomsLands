using UnityEngine;
using Cainos.LucidEditor;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif


namespace Cainos.PixelArtTopDown_Village
{
    public class Door : MonoBehaviour
    {
        [FoldoutGroup("Params")] public bool enableAutoClose = true;
        [FoldoutGroup("Params")] public float autoCloseTime = 2.0f;

        [FoldoutGroup("Reference")] public SpriteRenderer spriteRendererDoor;
        [FoldoutGroup("Reference")] public SpriteRenderer spriteRendererShadow;
        [Space]
        [FoldoutGroup("Reference")] public Sprite spriteDoorOpened;
        [FoldoutGroup("Reference")] public Sprite spriteDoorClosed;
        [Space]
        [FoldoutGroup("Reference")] public Sprite spriteShadowOpened;
        [FoldoutGroup("Reference")] public Sprite spriteShadowClosed;

        private float autoCloseTimer;


        private Animator Animator
        {
            get
            {
                if (animator == null ) animator = GetComponent<Animator>();
                return animator;
            }
        }
        private Animator animator;


        [FoldoutGroup("Runtime"), ShowInInspector]
        public bool IsOpened
        {
            get { return isOpened; }
            set
            {
                isOpened = value;

                #if UNITY_EDITOR
                if (Application.isPlaying == false)
                {
                    EditorUtility.SetDirty(this);
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
                }
                #endif


                if (Application.isPlaying)
                {
                    Animator.SetBool("IsOpened", isOpened);
                }
                else
                {
                    if(spriteRendererDoor) spriteRendererDoor.sprite = isOpened ? spriteDoorOpened : spriteDoorClosed;
                    if (spriteRendererShadow) spriteRendererShadow.sprite = isOpened ? spriteShadowOpened : spriteShadowClosed;
                }
            }
        }
        [SerializeField,HideInInspector]
        private bool isOpened;


        [FoldoutGroup("Runtime"), HorizontalGroup("Runtime/Button"), Button("Open")]
        public void Open()
        {
            IsOpened = true;
        }


        [FoldoutGroup("Runtime"), HorizontalGroup("Runtime/Button"), Button("Close")]
        public void Close()
        {
            IsOpened = false;
        }

        private void Start()
        {
            Animator.Play(isOpened ? "Opened" : "Closed");
            IsOpened = isOpened;
        }

        private void Update()
        {
            if ( enableAutoClose && IsOpened)
            {
                autoCloseTimer += Time.deltaTime;
                if (autoCloseTimer > autoCloseTime)
                {
                    autoCloseTimer = 0;
                    Close();
                }
            }
        }
    }
}
