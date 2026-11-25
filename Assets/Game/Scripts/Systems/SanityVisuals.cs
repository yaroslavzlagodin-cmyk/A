using BrokenHeart.World;
using UnityEngine;
namespace BrokenHeart.Systems
{
    public sealed class SanityVisuals : MonoBehaviour
    {
        [SerializeField] private SanitySystem sanity;
        [SerializeField] private Transform observer;
        [SerializeField] private BlockWorld world;
        [SerializeField] private Sprite eyeSprite,shadowSprite;
        [SerializeField] private Material material;
        // Sanity still changes while its apparitions are paused.
        [SerializeField] private bool hallucinationsEnabled = false;
        private SpriteRenderer eyes,shadow,phantom;
        private Vector3 shadowOrigin;
        private float manifestIn=4;
        public bool EyesVisible=>eyes!=null && eyes.gameObject.activeSelf;
        private bool VisualsActive => hallucinationsEnabled && isActiveAndEnabled;
        private void Awake()
        {
            if (!hallucinationsEnabled) return;
            eyes=Create("Unreal eyes",eyeSprite,Color.white);
            shadow=Create("Moving shadow",shadowSprite,new Color(0.12f,0.08f,0.18f,0.4f));
            shadow.transform.localScale=new Vector3(1.2f,2.2f,1);
            phantom=Create("False stone",world.Palette[2].Sprite,new Color(0.6f,0.5f,0.7f,0.6f));
        }
        private SpriteRenderer Create(string label,Sprite sprite,Color color)
        {
            var go=new GameObject(label,typeof(SpriteRenderer));go.transform.SetParent(transform);
            var visual=go.GetComponent<SpriteRenderer>();visual.sprite=sprite;visual.sharedMaterial=material;visual.color=color;visual.sortingOrder=15;
            go.SetActive(false);return visual;
        }
        public void ShowEyes(Vector3 position)
        {
            if (!VisualsActive || eyes == null) return;
            eyes.transform.position = position;
            eyes.gameObject.SetActive(sanity.Value < 55);
        }
        public void RefreshEffects()
        {
            if (!VisualsActive || eyes == null) return;
            if(sanity.Value>=55 || Vector2.Distance(eyes.transform.position,observer.position)<3)eyes.gameObject.SetActive(false);
            if(sanity.Value>=70)shadow.gameObject.SetActive(false);
            if(sanity.Value>=25 || Vector2.Distance(phantom.transform.position,observer.position)<2.5f)phantom.gameObject.SetActive(false);
            if(shadow.gameObject.activeSelf)shadow.transform.position=shadowOrigin+new Vector3(Mathf.Sin(Time.time*1.3f)*0.9f,Mathf.Sin(Time.time*2)*0.1f,0);
        }
        private void Update()
        {
            if (!VisualsActive || eyes == null) return;
            manifestIn-=Time.deltaTime;
            if(manifestIn<=0)
            {
                manifestIn=9;
                float direction=Mathf.Sin(Time.time)>0?1:-1;
                int x=Mathf.FloorToInt(observer.position.x+direction*8);
                float y=observer.position.y < -8 ? observer.position.y:world.Generator.Surface(x)+2;
                ShowEyes(new Vector3(x+0.5f,y,0));
                shadowOrigin=new Vector3(observer.position.x-direction*7,y,0);
                shadow.gameObject.SetActive(sanity.Value<70);
                phantom.transform.position=new Vector3(x,y-1,0);phantom.gameObject.SetActive(sanity.Value<25);
            }
            RefreshEffects();
        }

        private void OnDisable()
        {
            if (eyes != null) eyes.gameObject.SetActive(false);
            if (shadow != null) shadow.gameObject.SetActive(false);
            if (phantom != null) phantom.gameObject.SetActive(false);
        }
    }
}
