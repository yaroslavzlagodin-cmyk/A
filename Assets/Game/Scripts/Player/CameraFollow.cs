using UnityEngine;

namespace BrokenHeart.Player
{
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float smoothTime = 0.16f;
        private Vector3 velocity;
        private void LateUpdate()
        {
            if (target == null) return;
            var desired = new Vector3(target.position.x, target.position.y + 1.5f, -10);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
        }
    }
}
