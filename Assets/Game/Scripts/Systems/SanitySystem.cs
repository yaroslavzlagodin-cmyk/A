using System;
using UnityEngine;
namespace BrokenHeart.Systems
{
    public sealed class SanitySystem : MonoBehaviour
    {
        public float Value { get; private set; } = 100;
        public float AwakeSeconds { get; private set; }
        public bool HasReachedZero { get; private set; }
        public event Action<float> Changed;
        public event Action FirstCollapse;
        public void Advance(float seconds,bool sleeping,bool protectedMind)
        {
            if(seconds<=0)return;
            if(sleeping) { AwakeSeconds=Mathf.Max(0,AwakeSeconds-seconds*10); Adjust(seconds*6); }
            else
            {
                float overdueBefore=Mathf.Max(0,AwakeSeconds-90); AwakeSeconds+=seconds;
                float overdue=Mathf.Max(0,AwakeSeconds-90)-overdueBefore;
                if(overdue>0)Adjust(-overdue*0.12f*(protectedMind?0.25f:1));
            }
        }
        public void Adjust(float amount)
        {
            float before=Value; Value=Mathf.Clamp(Value+amount,0,100);
            if(Value!=before)Changed?.Invoke(Value);
            if(Value==0 && !HasReachedZero) { HasReachedZero=true; FirstCollapse?.Invoke(); }
        }
        public void RestCompleted(){AwakeSeconds=0;}
        public void Restore(float value,float awake,bool collapsed)
        { Value=Mathf.Clamp(value,0,100); AwakeSeconds=Mathf.Max(0,awake); HasReachedZero=collapsed; Changed?.Invoke(Value); }
    }
}
