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
    
    private CompositeDisposable _moveDisposables;

    private void Start()
    {
        _moveDisposables = new CompositeDisposable();
        moveInput = new ReactiveProperty<Vector2>();
        moveInput.Subscribe(x =>
            {
                if (x != Vector2.zero)
                {
                    animator.SetBool("1_Move",true);
                }
                else
                {
                    animator.SetBool("1_Move", false);
                }
            })
            .AddTo(_moveDisposables);
    }
    
    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput.Value = context.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput.Value * moveSpeed * Time.fixedDeltaTime);
    }
}