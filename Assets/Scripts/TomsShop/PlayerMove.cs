using System;
using R3;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    private ReactiveProperty<Vector2> moveInput;
    [SerializeField] private Rigidbody2D rb;

    [SerializeField] private PlayerInput playerInput;

    [SerializeField] private Animator animator;

    private CompositeDisposable moveDisposables;

    private void Start()
    {
        moveDisposables = new CompositeDisposable();
        moveInput = new ReactiveProperty<Vector2>();
        moveInput
            .Subscribe(x =>
            {
                if (x != Vector2.zero)
                {
                    animator.SetBool("1_Move", true);
                }
                else
                {
                    animator.SetBool("1_Move", false);
                }
            })
            .AddTo(moveDisposables);
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput.Value = context.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput.Value * moveSpeed * Time.fixedDeltaTime);

        // 向き変更処理
        if (moveInput.Value.x > 0)
        {
            // 右向き
            transform.localScale = new Vector3(-1f, 1f, 1f);
        }
        else if (moveInput.Value.x < 0)
        {
            // 左向き（X軸反転）
            transform.localScale = new Vector3(1f, 1f, 1f);
        }
    }

    private void OnDestroy()
    {
        moveDisposables?.Dispose();
    }
}