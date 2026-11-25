using UnityEngine;
using UnityEngine.InputSystem;
using BrokenHeart.Inventory;
using BrokenHeart.World;

namespace BrokenHeart.Player
{
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private LayerMask groundLayers;
        [SerializeField] private float speed = 6f;
        [SerializeField] private float jumpSpeed = 11.5f;
        private Rigidbody2D body;
        private BoxCollider2D shape;
        private PlayerInventory inventory;
        private BlockWorld world;
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
        private ContactFilter2D groundFilter;
        private float move;
        private float jumpBuffer;
        private float coyoteTime;
        private Vector2 spawn;
        public bool Grounded { get; private set; }
        public bool InputSuppressed { get; set; }
        public float FallBoundary { get; set; } = -110;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            shape = GetComponent<BoxCollider2D>();
            inventory = GetComponent<PlayerInventory>();
            world = FindAnyObjectByType<BlockWorld>();
            spawn = body.position;
            groundFilter.SetLayerMask(groundLayers);
            groundFilter.useTriggers = false;
        }
        private void Update()
        {
            if(InputSuppressed || inventory != null && inventory.IsMapOpen){move=0;jumpBuffer=0;return;}
            var keyboard = Keyboard.current;
            move = keyboard == null ? 0 :
                ((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) ? 1 : 0) -
                ((keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) ? 1 : 0);
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) jumpBuffer = 0.12f;
            else jumpBuffer = Mathf.Max(0, jumpBuffer - Time.deltaTime);
            if (move != 0 && visual != null) visual.flipX = move < 0;
        }
        private void FixedUpdate()
        {
            Grounded = false;
            int hits = shape.Cast(Vector2.down, groundFilter, groundHits, 0.08f);
            for (int i = 0; i < hits; i++)
                if (groundHits[i].normal.y > 0.6f) Grounded = true;
            coyoteTime = Grounded && body.linearVelocity.y <= 0.1f ? 0.1f : coyoteTime - Time.fixedDeltaTime;
            var velocity = body.linearVelocity;
            velocity.x = move * speed;
            if (jumpBuffer > 0 && coyoteTime > 0)
            {
                velocity.y = jumpSpeed;
                jumpBuffer = coyoteTime = 0;
                Grounded = false;
            }
            body.linearVelocity = velocity;
            // Prototype recovery when walking off the finite test world.
            if (body.position.y < FallBoundary)
            {
                world?.PrepareCollisionAt(spawn);
                body.position = spawn;
                body.linearVelocity = Vector2.zero;
            }
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { move = 0; jumpBuffer = 0; }
        }
    }
}
