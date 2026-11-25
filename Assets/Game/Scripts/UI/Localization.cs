using System;
using System.Linq;
using UnityEngine;
namespace BrokenHeart.UI
{
    public static class Localization
    {
        private static string[][] pairs;
        public static string Language {get;private set;}="uk";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadPreference()
        {try{SetLanguage(BrokenHeart.Core.Profiles.ProfileLibrary.Load().language);}catch{SetLanguage("uk");}}
        public static void SetLanguage(string language){Language=language=="en"?"en":"uk";}
        public static string T(string text)
        {
            if(string.IsNullOrEmpty(text))return text??"";
            if(pairs==null)
            {
                var asset=Resources.Load<TextAsset>("Translations");
                pairs=asset==null?Array.Empty<string[]>():asset.text.Split('\n').Select(line=>line.TrimEnd('\r').Split('\t')).Where(p=>p.Length==2).Select(p=>new[]{p[0].Replace("\\n","\n"),p[1].Replace("\\n","\n")}).ToArray();
            }
            int source=Language=="en"?0:1,target=1-source;
            foreach(var pair in pairs)if(text==pair[source])return pair[target];
            foreach(var pair in pairs.OrderByDescending(p=>p[source].Length))text=text.Replace(pair[source],pair[target]);
            return text;
        }
        private static GUIStyle wrapped;
        public static void Label(Rect r,string text)
        {wrapped??=new GUIStyle(GUI.skin.label){wordWrap=true};GUI.Label(r,T(text),wrapped);}
        public static void Box(Rect r,string text)=>GUI.Box(r,T(text));
        public static bool Button(Rect r,string text)=>GUI.Button(r,T(text));
    }
}

