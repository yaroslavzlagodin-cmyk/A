using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BrokenHeart.Player
{
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow : MonoBehaviour
    {
        private const float ZoomStep = 0.125f;
        [SerializeField] private Transform target;
        [SerializeField] private float smoothTime = 0.16f;
        private Camera view;
        private PixelPerfectCamera pixelPerfect;
        private int basePixelsPerUnit;
        private Vector3 velocity;
        public float Zoom { get; private set; } = 1f;

        private void Awake()
        {
            view = GetComponent<Camera>();
            pixelPerfect = GetComponent<PixelPerfectCamera>();
            if (pixelPerfect != null) basePixelsPerUnit = pixelPerfect.assetsPPU;
        }

        internal void StepZoom(float scroll)
        {
            float next = Mathf.Clamp(Zoom + Mathf.Sign(scroll) * ZoomStep, 0.5f, 2f);
            if (next == Zoom) return;

            // URP sets the projection each render, so update its pixel scale as well.
            if (pixelPerfect != null)
                pixelPerfect.assetsPPU = Mathf.RoundToInt(basePixelsPerUnit * next);
            view.orthographicSize *= Zoom / next;
            Zoom = next;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            var desired = new Vector3(target.position.x, target.position.y + 1.5f, -10);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
        }
    }
}
